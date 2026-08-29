using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors landscape materials — the global catalog of texture and function bindings shared across
/// every map, split out from <see cref="LandscapeWindow"/> because materials are not scoped to one
/// map the way channels and layers are.
///
/// Catalog edits go through the edit session like any other entity edit, so they undo and commit with
/// everything else.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class LandscapeMaterialsWindow : Window
{
    private const uint NameMaxLength = 128;
    private const uint PathMaxLength = 512;

    private readonly EditorContext _context;
    private readonly FieldEditTracker _tracker = new();
    private readonly TextureAssetPicker _texturePicker;

    private string? _parameterBefore;

    public LandscapeMaterialsWindow(WindowManager manager)
        : base("Landscape Materials", startOpen: false, defaultSize: new Vector2(720.0f, 560.0f))
    {
        _context = manager.Context;
        _texturePicker = new TextureAssetPicker(_context.Assets);
    }

    private LandscapeSystem Landscape => _context.Landscape;

    protected override void DrawContent()
    {
        if (ImGui.Button("Add material"))
        {
            Create(new LandscapeMaterial { Name = UniqueName("Material", Landscape.Catalog.Materials.Select(m => m.Name)) });
        }

        ImGui.Separator();

        foreach (LandscapeMaterial material in Landscape.Catalog.Materials)
        {
            ImGui.PushID(material.Id.Value.GetHashCode());
            if (ImGui.CollapsingHeader($"{material.Name}##header", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawName(material, material.Name, value => material.Name = value);

                string texture = material.TexturePath;
                if (ImGui.InputText("Texture", ref texture, PathMaxLength)) { material.TexturePath = texture; }
                _tracker.Track(_context.EditSessions, material, "texture", material.TexturePath, v => material.TexturePath = v);
                ImGui.SameLine();
                if (ImGui.Button("Browse"))
                {
                    _texturePicker.Browse(material.TexturePath, selected =>
                        RecordNow(material, "texture", material.TexturePath, selected, v => material.TexturePath = v));
                }

                Heading("Alpha");
                DrawFunctionBinding(
                    material, "alpha", Landscape.Functions.Alpha,
                    material.AlphaFunction, value => material.AlphaFunction = value,
                    material.AlphaParameters, value => material.AlphaParameters = value,
                    optional: false);

                Heading("Height");
                DrawFunctionBinding(
                    material, "height", Landscape.Functions.Height,
                    material.HeightFunction, value => material.HeightFunction = value,
                    material.HeightParameters, value => material.HeightParameters = value,
                    optional: true);

                Heading("Hole");
                DrawFunctionBinding(
                    material, "hole", Landscape.Functions.Hole,
                    material.HoleFunction, value => material.HoleFunction = value,
                    material.HoleParameters, value => material.HoleParameters = value,
                    optional: true);

                DrawDelete(material);
            }

            ImGui.PopID();
        }

        _texturePicker.Draw();
    }

    // ImGui.NET 1.88 predates SeparatorText, so a section heading is a separator plus dimmed text.
    private static void Heading(string label)
    {
        ImGui.Separator();
        ImGui.TextDisabled(label);
    }

    // Draws one function slot on a material: which function, then editors generated from whatever
    // that function declares. Parameter values live in a serialized bag, so an edit rewrites the bag
    // as a single undoable field change on the material.
    private void DrawFunctionBinding(
        LandscapeMaterial material,
        string role,
        IEnumerable<ILandscapeFunction> available,
        string boundId,
        System.Action<string> setFunction,
        string serialized,
        System.Action<string> setParameters,
        bool optional)
    {
        ImGui.PushID(role);

        ILandscapeFunction? bound = Landscape.Functions.Find(boundId);
        string label = bound?.DisplayName ?? (boundId.Length == 0 ? "(none)" : $"{boundId} (missing)");

        if (ImGui.BeginCombo("Function", label))
        {
            if (optional && ImGui.Selectable("(none)", boundId.Length == 0))
            {
                RecordNow(material, $"{role} function", boundId, "", setFunction);
            }

            foreach (ILandscapeFunction function in available)
            {
                if (ImGui.Selectable($"{function.DisplayName}##{function.Id}", function.Id == boundId))
                {
                    RecordNow(material, $"{role} function", boundId, function.Id, setFunction);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(function.Description);
                }
            }

            ImGui.EndCombo();
        }

        if (bound == null)
        {
            if (boundId.Length > 0)
            {
                // The binding is kept, not cleared: the plugin providing it may simply not be loaded.
                ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), $"No loaded function provides '{boundId}'.");
            }

            ImGui.PopID();
            return;
        }

        ImGui.TextDisabled($"v{bound.Version} · reach {bound.MaxSampleRadius:0.##} units");
        DrawParameters(material, bound, serialized, setParameters);
        ImGui.PopID();
    }

    private void DrawParameters(
        LandscapeMaterial material,
        ILandscapeFunction function,
        string serialized,
        System.Action<string> setParameters)
    {
        LandscapeParameterValues values = LandscapeParameterValues.Parse(serialized);

        foreach (LandscapeParameter parameter in function.Parameters)
        {
            ImGui.PushID(parameter.Name);

            switch (parameter.Kind)
            {
                case LandscapeParameterKind.Float:
                {
                    float value = values.GetFloat(parameter);
                    if (ImGui.DragFloat(parameter.DisplayName, ref value, 0.01f, parameter.Min, parameter.Max))
                    {
                        values.Set(parameter, value);
                    }

                    TrackParameter(material, function, values, serialized, setParameters);
                    break;
                }

                case LandscapeParameterKind.Int:
                {
                    int value = values.GetInt(parameter);
                    if (ImGui.DragInt(parameter.DisplayName, ref value, 1.0f, (int)parameter.Min, (int)parameter.Max))
                    {
                        values.Set(parameter, value);
                    }

                    TrackParameter(material, function, values, serialized, setParameters);
                    break;
                }

                case LandscapeParameterKind.Bool:
                {
                    bool value = values.GetBool(parameter);
                    if (ImGui.Checkbox(parameter.DisplayName, ref value))
                    {
                        values.Set(parameter, value);
                        RecordNow(material, parameter.DisplayName, serialized, values.Serialize(), setParameters);
                    }

                    break;
                }

                case LandscapeParameterKind.Channel:
                {
                    DrawChannelParameter(material, parameter, values, serialized, setParameters);
                    break;
                }
            }

            if (parameter.Description.Length > 0 && ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(parameter.Description);
            }

            ImGui.PopID();
        }
    }

    // Channels belong to the open map, so the choices offered here are that map's channels — the
    // usual case is authoring a material with a specific map's channels in view. The binding itself
    // is just a name, so it still resolves correctly if a different map is open later with a
    // same-named channel, and is flagged below when nothing matches.
    private void DrawChannelParameter(
        LandscapeMaterial material,
        LandscapeParameter parameter,
        LandscapeParameterValues values,
        string serialized,
        System.Action<string> setParameters)
    {
        string bound = values.GetChannel(parameter);
        string access = parameter.Access == LandscapeChannelAccess.Write ? "writes" : "reads";
        string label = bound.Length == 0 ? "(unbound)" : bound;

        if (ImGui.BeginCombo($"{parameter.DisplayName} ({access})", label))
        {
            foreach (LandscapeChannel channel in Landscape.Catalog.Channels)
            {
                if (ImGui.Selectable(channel.Name, channel.Name == bound))
                {
                    values.Set(parameter, channel.Name);
                    RecordNow(material, parameter.DisplayName, serialized, values.Serialize(), setParameters);
                }
            }

            ImGui.EndCombo();
        }

        if (bound.Length > 0 && Landscape.Catalog.Channels.All(channel => channel.Name != bound))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), $"Channel '{bound}' does not exist on the open map.");
        }
    }

    // Numeric parameters are dragged, so the whole drag is one undo step like every other field here.
    private void TrackParameter(
        LandscapeMaterial material,
        ILandscapeFunction function,
        LandscapeParameterValues values,
        string serialized,
        System.Action<string> setParameters)
    {
        if (ImGui.IsItemActivated())
        {
            _parameterBefore = serialized;
        }

        if (!ImGui.IsItemDeactivatedAfterEdit() || _parameterBefore == null)
        {
            return;
        }

        string before = _parameterBefore;
        _parameterBefore = null;

        string after = values.Serialize();
        if (before != after)
        {
            RecordNow(material, $"{function.DisplayName} parameters", before, after, setParameters);
        }
    }

    private void DrawName<T>(T entity, string current, System.Action<string> set) where T : CatalogEntity
    {
        string value = current;
        if (ImGui.InputText("Name", ref value, NameMaxLength))
        {
            set(value);
        }

        _tracker.Track(_context.EditSessions, entity, "name", value, set);
    }

    private void DrawDelete(CatalogEntity entity)
    {
        ImGui.Spacing();
        if (!ImGui.SmallButton("Delete"))
        {
            return;
        }

        var command = new DeleteCatalogEntityCommand(_context.Catalog, entity);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private void Create<TEntity>(TEntity entity) where TEntity : CatalogEntity, IKeyedCatalogEntity
    {
        // Identified before it is added, so a stamp created in the same session can reference it.
        _context.Catalog.AssignId(entity);

        var command = new CreateCatalogEntityCommand(_context.Catalog, entity);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    // A combo has no activate/deactivate pair to bracket, so its edit is recorded on the spot.
    private void RecordNow<T>(CatalogEntity entity, string field, T before, T after, System.Action<T> set)
    {
        if (EqualityComparer<T>.Default.Equals(before, after))
        {
            return;
        }

        var command = new SetFieldCommand<T>(entity, field, set, before, after);
        command.Apply();
        _context.EditSessions.Record(command);
    }

    private static string UniqueName(string prefix, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken);
        for (int i = 1; ; i++)
        {
            string candidate = $"{prefix} {i}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
