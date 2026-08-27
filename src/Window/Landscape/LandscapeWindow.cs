using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors the map's landscape: its settings (from an export profile), and the catalog of channels,
/// layers and materials that chunks are resolved against.
///
/// Catalog edits go through the edit session like any other entity edit, so they undo and commit with
/// everything else. Settings do not: they are per-map configuration rather than an entity, and
/// changing them is a rebuild rather than an undoable step, so they save immediately.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class LandscapeWindow : Window
{
    private const uint NameMaxLength = 128;
    private const uint PathMaxLength = 512;

    private readonly EditorContext _context;
    private readonly FieldEditTracker _tracker = new();
    private readonly TextureAssetPicker _texturePicker;

    private LandscapeSettings? _draft;
    private string? _status;
    private int _profileIndex;
    private string? _parameterBefore;

    public LandscapeWindow(WindowManager manager)
        : base("Landscape", startOpen: false, defaultSize: new Vector2(720.0f, 560.0f))
    {
        _context = manager.Context;
        _texturePicker = new TextureAssetPicker(_context.Assets);
    }

    private LandscapeSystem Landscape => _context.Landscape;

    protected override void DrawContent()
    {
        if (!Landscape.IsEnabled)
        {
            DrawSetup();
            return;
        }

        if (ImGui.BeginTabBar("LandscapeTabs"))
        {
            DrawTab("Settings", DrawSettings);
            DrawTab("Channels", DrawChannels);
            DrawTab("Layers", DrawLayers);
            DrawTab("Materials", DrawMaterials);
            DrawTab("Functions", DrawFunctions);
            DrawTab("Problems", DrawProblems);
            ImGui.EndTabBar();
        }

        if (_status != null)
        {
            ImGui.Separator();
            ImGui.TextDisabled(_status);
        }
    }

    // ImGui.NET 1.88 predates SeparatorText, so a section heading is a separator plus dimmed text.
    private static void Heading(string label)
    {
        ImGui.Separator();
        ImGui.TextDisabled(label);
    }

    private static void DrawTab(string label, System.Action draw)
    {
        if (!ImGui.BeginTabItem(label))
        {
            return;
        }

        ImGui.Spacing();
        draw();
        ImGui.EndTabItem();
    }

    // ---- Setup -------------------------------------------------------------------------------

    private void DrawSetup()
    {
        ImGui.TextWrapped(
            $"'{_context.Maps.Current.DisplayName}' has no landscape. Pick the profile matching the format " +
            "you are exporting to — it decides how terrain is represented for this map.");
        ImGui.Separator();

        List<ILandscapeProfile> profiles = Landscape.Profiles.ToList();
        if (profiles.Count == 0)
        {
            ImGui.TextDisabled("No landscape profiles are registered.");
            return;
        }

        _profileIndex = System.Math.Clamp(_profileIndex, 0, profiles.Count - 1);
        if (ImGui.BeginCombo("Profile", profiles[_profileIndex].Name))
        {
            for (int i = 0; i < profiles.Count; i++)
            {
                if (ImGui.Selectable(profiles[i].Name, i == _profileIndex))
                {
                    _profileIndex = i;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextWrapped(profiles[_profileIndex].Description);
        ImGui.Spacing();

        if (ImGui.Button("Create landscape"))
        {
            _status = Landscape.Enable(profiles[_profileIndex]) ?? "Landscape created.";
            _draft = null;
        }
    }

    // ---- Settings ----------------------------------------------------------------------------

    private void DrawSettings()
    {
        _draft ??= Landscape.Settings!.Clone();

        if (_draft.ProfileName.Length > 0)
        {
            ImGui.TextDisabled($"From profile: {_draft.ProfileName}");
        }

        int height = _draft.ChunkHeightResolution;
        int alpha = _draft.ChunkAlphaResolution;
        int textures = _draft.TextureLimit;
        int chunkLimit = _draft.ChunkLimit;
        float worldSize = _draft.ChunkWorldSize;
        int originX = _draft.OriginChunkX;
        int originY = _draft.OriginChunkY;

        Heading("Chunk");
        if (ImGui.DragFloat("World size", ref worldSize, 1.0f, 1.0f, 4096.0f)) { _draft.ChunkWorldSize = worldSize; }
        if (ImGui.DragInt("Height resolution", ref height, 1.0f, 2, 1024)) { _draft.ChunkHeightResolution = height; }
        if (ImGui.DragInt("Alpha resolution", ref alpha, 1.0f, 1, 4096)) { _draft.ChunkAlphaResolution = alpha; }

        Heading("Budget");
        if (ImGui.DragInt("Texture limit", ref textures, 1.0f, 1, 64)) { _draft.TextureLimit = textures; }
        ImGui.SameLine();
        ImGui.TextDisabled("(includes the base layer)");
        if (ImGui.DragInt("Chunk limit", ref chunkLimit, 1.0f, 1, 4096)) { _draft.ChunkLimit = chunkLimit; }
        if (ImGui.DragInt("Origin chunk X", ref originX)) { _draft.OriginChunkX = originX; }
        if (ImGui.DragInt("Origin chunk Y", ref originY)) { _draft.OriginChunkY = originY; }

        DrawEncoding();
        DrawFallbackMaterial();
        DrawSettingsActions();
    }

    private void DrawEncoding()
    {
        Heading("Encoding");

        if (ImGui.BeginCombo("Height", _draft!.HeightEncoding.ToString()))
        {
            foreach (HeightEncoding encoding in new[] { HeightEncoding.Float32, HeightEncoding.UInt16 })
            {
                if (ImGui.Selectable(encoding.ToString(), _draft.HeightEncoding == encoding))
                {
                    _draft.HeightEncoding = encoding;
                }
            }

            ImGui.EndCombo();
        }

        if (_draft.HeightEncoding == HeightEncoding.UInt16)
        {
            float offset = _draft.HeightOffset;
            float scale = _draft.HeightScale;
            if (ImGui.DragFloat("Height offset", ref offset, 0.1f)) { _draft.HeightOffset = offset; }
            if (ImGui.DragFloat("Height scale", ref scale, 0.0001f, 0.0001f, 1.0f, "%.5f")) { _draft.HeightScale = scale; }
        }

        int bits = _draft.AlphaBitDepth;
        if (ImGui.DragInt("Alpha bits", ref bits, 1.0f, 8, 16)) { _draft.AlphaBitDepth = bits <= 8 ? 8 : 16; }

        bool normalized = _draft.AlphaNormalized;
        if (ImGui.Checkbox("Alphas sum to 1", ref normalized)) { _draft.AlphaNormalized = normalized; }
    }

    private void DrawFallbackMaterial()
    {
        Heading("Fallback");

        List<LandscapeMaterial> materials = Landscape.Catalog.Materials.ToList();

        // Only look up a real id: matching on a null id would find the first *uncommitted* material
        // and show its name as though it were the fallback, when none is set.
        LandscapeMaterial? current = _draft!.FallbackMaterialId is { } id
            ? materials.FirstOrDefault(m => m.RecordId == id)
            : null;

        if (ImGui.BeginCombo("Fallback material", current?.Name ?? "(none)"))
        {
            if (ImGui.Selectable("(none)", current == null))
            {
                _draft.FallbackMaterialId = null;
            }

            foreach (LandscapeMaterial material in materials)
            {
                int? recordId = material.RecordId;
                if (ImGui.Selectable($"{material.Name}##{recordId}", recordId == _draft.FallbackMaterialId))
                {
                    _draft.FallbackMaterialId = recordId;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.TextDisabled("Used for the base slot when no layer claims one, so a chunk is never a hole.");
    }

    private void DrawSettingsActions()
    {
        ImGui.Separator();

        foreach (string problem in _draft!.Validate())
        {
            ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), problem);
        }

        LandscapeChangeCost cost = Landscape.CostOf(_draft);
        if (cost != LandscapeChangeCost.Free)
        {
            string warning = cost == LandscapeChangeCost.Rebuild
                ? "Applying this rebuilds every chunk in the map. Entities are untouched."
                : "Applying this re-resolves every chunk, so layers may appear or disappear.";
            ImGui.TextColored(new Vector4(1.0f, 0.72f, 0.22f, 1.0f), warning);
        }

        bool valid = _draft.Validate().Count == 0;
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button("Apply"))
        {
            _status = Landscape.Save(_draft.Clone()) ?? "Settings saved.";
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Revert"))
        {
            _draft = null;
        }
    }

    // ---- Catalog -----------------------------------------------------------------------------

    private void DrawChannels()
    {
        if (ImGui.Button("Add channel"))
        {
            Create(new LandscapeChannel { Name = UniqueName("Channel", Landscape.Catalog.Channels.Select(c => c.Name)) });
        }

        ImGui.Separator();

        foreach (LandscapeChannel channel in Landscape.Catalog.Channels)
        {
            ImGui.PushID(channel.Id.Value.GetHashCode());
            if (ImGui.CollapsingHeader($"{channel.Name}##header", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawName(channel, channel.Name, value => channel.Name = value);

                int resolution = channel.Resolution;
                if (ImGui.DragInt("Resolution", ref resolution, 1.0f, 1, 4096)) { channel.Resolution = resolution; }
                _tracker.Track(_context.EditSessions, channel, "resolution", channel.Resolution, v => channel.Resolution = v);

                int bits = channel.BitDepth;
                if (ImGui.DragInt("Bit depth", ref bits, 1.0f, 8, 32)) { channel.BitDepth = bits; }
                _tracker.Track(_context.EditSessions, channel, "bit depth", channel.BitDepth, v => channel.BitDepth = v);

                ImGui.TextDisabled($"{channel.BytesPerChunk / 1024.0f:0.#} KB per chunk");
                DrawDelete(channel);
            }

            ImGui.PopID();
        }
    }

    private void DrawLayers()
    {
        if (ImGui.Button("Add layer"))
        {
            IReadOnlyList<LandscapeLayer> existing = Landscape.Catalog.Layers;
            Create(new LandscapeLayer
            {
                Name = UniqueName("Layer", existing.Select(l => l.Name)),
                DrawOrder = existing.Count == 0 ? 0 : existing.Max(l => l.DrawOrder) + 1,
                IsBase = existing.Count == 0,
            });
        }

        ImGui.TextDisabled("What a layer carries is decided by the material bound to it.");
        ImGui.Separator();

        foreach (LandscapeLayer layer in Landscape.Catalog.LayersInOrder)
        {
            ImGui.PushID(layer.Id.Value.GetHashCode());
            string label = layer.IsBase ? $"{layer.Name} (base)##header" : $"{layer.Name}##header";
            if (ImGui.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawName(layer, layer.Name, value => layer.Name = value);

                bool isBase = layer.IsBase;
                if (ImGui.Checkbox("Base layer", ref isBase))
                {
                    RecordNow(layer, "base", layer.IsBase, isBase, v => layer.IsBase = v);
                }

                ImGui.SameLine();
                ImGui.TextDisabled("(opaque, writes no alpha)");

                int order = layer.DrawOrder;
                if (ImGui.DragInt("Draw order", ref order)) { layer.DrawOrder = order; }
                _tracker.Track(_context.EditSessions, layer, "draw order", layer.DrawOrder, v => layer.DrawOrder = v);

                int priority = layer.Priority;
                if (ImGui.DragInt("Priority", ref priority)) { layer.Priority = priority; }
                _tracker.Track(_context.EditSessions, layer, "priority", layer.Priority, v => layer.Priority = v);
                ImGui.SameLine();
                ImGui.TextDisabled("(higher survives an overflow)");

                DrawDelete(layer);
            }

            ImGui.PopID();
        }
    }

    private void DrawMaterials()
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

                DrawDelete(material);
            }

            ImGui.PopID();
        }

        _texturePicker.Draw();
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
            ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), $"Channel '{bound}' does not exist.");
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

    private void DrawFunctions()
    {
        LandscapeFunctions functions = Landscape.Functions;

        ImGui.TextDisabled($"{functions.All.Count} discovered · max reach {Landscape.Catalog.MaxSampleRadius:0.##} units in use");
        if (ImGui.Button("Rescan"))
        {
            functions.Discover();
        }

        foreach (string warning in functions.Warnings)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.72f, 0.22f, 1.0f), warning);
        }

        ImGui.Separator();

        foreach (ILandscapeFunction function in functions.All)
        {
            string kind = function is ILandscapeAlphaFunction ? "alpha" : "height";
            if (!ImGui.CollapsingHeader($"{function.DisplayName} ({kind})##{function.Id}"))
            {
                continue;
            }

            ImGui.TextWrapped(function.Description);
            ImGui.TextDisabled($"{function.Id} · v{function.Version} · reach {function.MaxSampleRadius:0.##} units");

            foreach (LandscapeParameter parameter in function.Parameters)
            {
                string detail = parameter.Kind == LandscapeParameterKind.Channel
                    ? $"channel, {parameter.Access.ToString().ToLowerInvariant()}"
                    : parameter.Kind.ToString().ToLowerInvariant();
                ImGui.BulletText($"{parameter.DisplayName} — {detail}");
            }
        }
    }

    private void DrawProblems()
    {
        IReadOnlyList<LandscapeIssue> issues = Landscape.Catalog.Validate();
        if (issues.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.42f, 0.85f, 0.46f, 1.0f), "The catalog is valid.");
            return;
        }

        foreach (LandscapeIssue issue in issues)
        {
            Vector4 colour = issue.Severity == LandscapeIssueSeverity.Error
                ? new Vector4(1.0f, 0.45f, 0.4f, 1.0f)
                : new Vector4(1.0f, 0.72f, 0.22f, 1.0f);
            ImGui.TextColored(colour, $"{issue.Severity}: {issue.Message}");
        }
    }

    // ---- Shared ------------------------------------------------------------------------------

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
