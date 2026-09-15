using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Authors the open map's landscape: its settings (from an export profile), and the map's channels,
/// layers and materials.
///
/// Catalog edits go through the edit session like any other entity edit, so they undo and commit with
/// everything else. Settings do not: they are per-map configuration rather than an entity, and
/// changing them is a rebuild rather than an undoable step, so they save immediately.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class LandscapeWindow : Window
{
    public override string? Category => "Landscape";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.H, ShortcutModifiers.Alt);

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
            DrawTab("Attributes", DrawAttributes);
            DrawTab("Layers", DrawLayers);
            DrawTab("Materials", DrawMaterials);
            ImGui.EndTabBar();
        }

        _texturePicker.Draw();

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
        int holes = _draft.ChunkHoleResolution;
        int textures = _draft.TextureLimit;
        int chunkLimit = _draft.ChunkLimit;
        float worldSize = _draft.ChunkWorldSize;
        int originX = _draft.OriginChunkX;
        int originY = _draft.OriginChunkY;

        Heading("Chunk");
        if (ImGui.DragFloat("World size", ref worldSize, 1.0f, 1.0f, 4096.0f)) { _draft.ChunkWorldSize = worldSize; }
        if (ImGui.DragInt("Height resolution", ref height, 1.0f, 2, 1024)) { _draft.ChunkHeightResolution = height; }
        if (ImGui.DragInt("Alpha resolution", ref alpha, 1.0f, 1, 4096)) { _draft.ChunkAlphaResolution = alpha; }
        if (ImGui.DragInt("Hole resolution", ref holes, 1.0f, 1, 256)) { _draft.ChunkHoleResolution = holes; }
        ImGui.SameLine();
        ImGui.TextDisabled("(independent of height/alpha; coarse like an export target's own hole grid)");

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
            ImGuiEx.TextColored(CommonColors.Error, problem);
        }

        LandscapeChangeCost cost = Landscape.CostOf(_draft);
        if (cost != LandscapeChangeCost.Free)
        {
            string warning = cost == LandscapeChangeCost.Rebuild
                ? "Applying this rebuilds every chunk in the map. Entities are untouched."
                : "Applying this re-resolves every chunk, so layers may appear or disappear.";
            ImGuiEx.TextColored(CommonColors.Warning, warning);
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
            Create(new LandscapeChannel
            {
                Map = _context.Maps.CurrentMap,
                Name = UniqueName("Channel", Landscape.Catalog.Channels.Select(c => c.Name)),
            });
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

                DrawComponentsCombo(channel);

                ImGui.TextDisabled($"{channel.BytesPerChunk / 1024.0f:0.#} KB per chunk");
                DrawDelete(channel);
            }

            ImGui.PopID();
        }
    }

    private static string ComponentsLabel(int components) => components switch
    {
        3 => "RGB",
        4 => "RGBA",
        _ => "Scalar",
    };

    private void DrawComponentsCombo(LandscapeChannel channel)
    {
        int components = channel.Components;
        if (ImGui.BeginCombo("Format", ComponentsLabel(components)))
        {
            foreach (int candidate in new[] { 1, 3, 4 })
            {
                if (ImGui.Selectable(ComponentsLabel(candidate), candidate == components))
                {
                    RecordNow(channel, "format", components, candidate, v => channel.Components = v);
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawAttributes()
    {
        if (ImGui.Button("Add attribute"))
        {
            string name = UniqueName("Attribute", Landscape.Catalog.Attributes.Select(a => a.Name));
            Create(new TerrainAttribute
            {
                Map = _context.Maps.CurrentMap,
                Name = name,
                Key = UniqueName("attribute", Landscape.Catalog.Attributes.Select(a => a.Key)),
            });
        }

        ImGui.TextDisabled("A declared output kind: materials write it, an exporter reads it, nothing filters it.");
        ImGui.Separator();

        foreach (TerrainAttribute attribute in Landscape.Catalog.Attributes)
        {
            ImGui.PushID(attribute.Id.Value.GetHashCode());
            if (ImGui.CollapsingHeader($"{attribute.Name}##header", ImGuiTreeNodeFlags.DefaultOpen))
            {
                DrawName(attribute, attribute.Name, value => attribute.Name = value);

                string key = attribute.Key;
                if (ImGui.InputText("Key", ref key, NameMaxLength)) { attribute.Key = key; }
                _tracker.Track(_context.EditSessions, attribute, "key", attribute.Key, v => attribute.Key = v);
                ImGui.SameLine();
                ImGui.TextDisabled("(what a material write binds to)");

                string description = attribute.Description;
                if (ImGui.InputText("Description", ref description, PathMaxLength)) { attribute.Description = description; }
                _tracker.Track(_context.EditSessions, attribute, "description", attribute.Description, v => attribute.Description = v);

                int cells = attribute.CellsPerChunkEdge;
                if (ImGui.DragInt("Cells per chunk edge", ref cells, 1.0f, 1, 256)) { attribute.CellsPerChunkEdge = cells; }
                _tracker.Track(_context.EditSessions, attribute, "cells", attribute.CellsPerChunkEdge, v => attribute.CellsPerChunkEdge = v);

                DrawAttributeComponentsCombo(attribute);
                DrawAttributeWidthCombo(attribute);
                DrawAttributeKindCombo(attribute);

                if (attribute.Kind == TerrainAttributeKind.CatalogRef)
                {
                    string catalog = attribute.CatalogName;
                    if (ImGui.InputText("Catalog", ref catalog, NameMaxLength)) { attribute.CatalogName = catalog; }
                    _tracker.Track(_context.EditSessions, attribute, "catalog", attribute.CatalogName, v => attribute.CatalogName = v);
                }

                int defaultValue = unchecked((int)attribute.DefaultValue);
                if (ImGui.DragInt("Default value", ref defaultValue)) { attribute.DefaultValue = unchecked((uint)defaultValue); }
                _tracker.Track(_context.EditSessions, attribute, "default", (int)attribute.DefaultValue,
                    v => attribute.DefaultValue = unchecked((uint)v));

                if (attribute.Components > 1)
                {
                    string names = attribute.ComponentNames;
                    if (ImGui.InputText("Component names (comma-separated)", ref names, PathMaxLength))
                    {
                        attribute.ComponentNames = names;
                    }

                    _tracker.Track(_context.EditSessions, attribute, "component names", attribute.ComponentNames,
                        v => attribute.ComponentNames = v);
                }

                if (attribute.Kind is TerrainAttributeKind.Enum or TerrainAttributeKind.Flags)
                {
                    DrawAttributeValues(attribute);
                }

                ImGui.TextDisabled($"{attribute.BytesPerChunk / 1024.0f:0.##} KB per chunk");
                DrawDelete(attribute);
            }

            ImGui.PopID();
        }
    }

    private void DrawAttributeComponentsCombo(TerrainAttribute attribute)
    {
        int components = attribute.Components;
        if (ImGui.BeginCombo("Format", ComponentsLabel(components)))
        {
            foreach (int candidate in new[] { 1, 3, 4 })
            {
                if (ImGui.Selectable(ComponentsLabel(candidate), candidate == components))
                {
                    RecordNow(attribute, "format", components, candidate, v => attribute.Components = v);
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawAttributeWidthCombo(TerrainAttribute attribute)
    {
        int width = attribute.ElementWidth;
        if (ImGui.BeginCombo("Element width", $"{width}-bit"))
        {
            foreach (int candidate in new[] { 8, 16, 32 })
            {
                if (ImGui.Selectable($"{candidate}-bit", candidate == width))
                {
                    RecordNow(attribute, "element width", width, candidate, v => attribute.ElementWidth = v);
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawAttributeKindCombo(TerrainAttribute attribute)
    {
        TerrainAttributeKind kind = attribute.Kind;
        if (ImGui.BeginCombo("Kind", kind.ToString()))
        {
            foreach (TerrainAttributeKind candidate in System.Enum.GetValues<TerrainAttributeKind>())
            {
                if (ImGui.Selectable(candidate.ToString(), candidate == kind))
                {
                    RecordNow(attribute, "kind", (int)kind, (int)candidate, v => attribute.Kind = (TerrainAttributeKind)v);
                }
            }

            ImGui.EndCombo();
        }
    }

    private void DrawAttributeValues(TerrainAttribute attribute)
    {
        Heading(attribute.Kind == TerrainAttributeKind.Flags ? "Bits" : "Values");

        int attributeId = attribute.RecordId ?? 0;
        List<TerrainAttributeValue> rows = _context.Catalog.OfType<TerrainAttributeValue>()
            .Where(row => row.AttributeId == attributeId)
            .ToList();

        foreach (TerrainAttributeValue row in rows)
        {
            ImGui.PushID(row.Id.Value.GetHashCode());

            int value = (int)row.Value;
            ImGui.SetNextItemWidth(120.0f);
            if (ImGui.DragInt("##value", ref value)) { row.Value = value; }
            _tracker.Track(_context.EditSessions, row, "value", (int)row.Value, v => row.Value = v);

            ImGui.SameLine();
            string name = row.Name;
            ImGui.SetNextItemWidth(220.0f);
            if (ImGui.InputText("##name", ref name, NameMaxLength)) { row.Name = name; }
            _tracker.Track(_context.EditSessions, row, "name", row.Name, v => row.Name = v);

            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
            {
                var command = new DeleteCatalogEntityCommand(_context.Catalog, row);
                command.Apply();
                _context.EditSessions.Record(command);
            }

            ImGui.PopID();
        }

        if (ImGui.SmallButton("Add value"))
        {
            Create(new TerrainAttributeValue
            {
                Map = attribute.Map,
                AttributeId = attributeId,
            });
        }
    }

    private void DrawLayers()
    {
        if (ImGui.Button("Add layer"))
        {
            IReadOnlyList<LandscapeLayer> existing = Landscape.Catalog.Layers;
            Create(new LandscapeLayer
            {
                Map = _context.Maps.CurrentMap,
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
            Create(new LandscapeMaterial
            {
                Map = _context.Maps.CurrentMap,
                Name = UniqueName("Material", Landscape.Catalog.Materials.Select(m => m.Name)),
            });
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

                int surfaceEffect = material.SurfaceEffectId;
                if (ImGui.DragInt("Surface effect id", ref surfaceEffect, 1.0f, 0, int.MaxValue))
                {
                    material.SurfaceEffectId = surfaceEffect;
                }

                _tracker.Track(_context.EditSessions, material, "surface effect id", material.SurfaceEffectId,
                    v => material.SurfaceEffectId = v);
                ImGui.SameLine();
                ImGui.TextDisabled("(0 = none; a per-texture export id, e.g. WoW's MCLY effectId)");

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

                Heading("Vertex Color");
                DrawFunctionBinding(
                    material, "vertexcolor", Landscape.Functions.VertexColor,
                    material.VertexColorFunction, value => material.VertexColorFunction = value,
                    material.VertexColorParameters, value => material.VertexColorParameters = value,
                    optional: true);

                Heading("Vertex Light");
                DrawFunctionBinding(
                    material, "vertexlight", Landscape.Functions.VertexLight,
                    material.VertexLightFunction, value => material.VertexLightFunction = value,
                    material.VertexLightParameters, value => material.VertexLightParameters = value,
                    optional: true);

                Heading("Attributes");
                DrawAttributeWrites(material);

                DrawDelete(material);
            }

            ImGui.PopID();
        }
    }

    private static readonly LandscapeSwizzle[] AttributeSwizzleChoices =
    [
        LandscapeSwizzle.Native, LandscapeSwizzle.R, LandscapeSwizzle.G, LandscapeSwizzle.B, LandscapeSwizzle.A,
        LandscapeSwizzle.Rgb, LandscapeSwizzle.Rgba,
    ];

    // A material's list of terrain-attribute writes — the open-ended sixth output kind. Each row
    // picks an attribute, the component(s) to land on when it has more than one, a function, then
    // that function's parameters (with the AttributeValue editor rendered against the chosen
    // attribute). The writes are child rows, so each is its own undoable create/delete.
    private void DrawAttributeWrites(LandscapeMaterial material)
    {
        IReadOnlyList<TerrainAttribute> attributes = Landscape.Catalog.Attributes;
        if (attributes.Count == 0)
        {
            ImGui.TextDisabled("No attributes are declared for this map. Add one on the Attributes tab.");
            return;
        }

        foreach (LandscapeMaterialAttributeWrite write in Landscape.Catalog.AttributeWritesOf(material).ToList())
        {
            ImGui.PushID(write.Id.Value.GetHashCode());

            TerrainAttributeBinding binding = write.Binding;
            TerrainAttribute? target = attributes.FirstOrDefault(a => a.Key == binding.Attribute);

            string attrLabel = target?.Name
                ?? (binding.Attribute.Length == 0 ? "(pick attribute)" : $"{binding.Attribute} (missing)");
            if (ImGui.BeginCombo("Attribute", attrLabel))
            {
                foreach (TerrainAttribute attribute in attributes)
                {
                    if (ImGui.Selectable($"{attribute.Name}##{attribute.Key}", attribute.Key == binding.Attribute))
                    {
                        string next = new TerrainAttributeBinding(attribute.Key, LandscapeSwizzle.Native).ToString();
                        RecordNow(write, "attribute", write.Attribute, next, v => write.Attribute = v);
                    }
                }

                ImGui.EndCombo();
            }

            if (target is { Components: > 1 })
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(110.0f);
                if (ImGui.BeginCombo("##component", SwizzleLabel(binding.Swizzle)))
                {
                    foreach (LandscapeSwizzle candidate in AttributeSwizzleChoices)
                    {
                        if (ImGui.Selectable(SwizzleLabel(candidate), candidate == binding.Swizzle))
                        {
                            string next = new TerrainAttributeBinding(binding.Attribute, candidate).ToString();
                            RecordNow(write, "component", write.Attribute, next, v => write.Attribute = v);
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ILandscapeAttributeFunction? bound = Landscape.Functions.FindAttribute(write.Function);
            string fnLabel = bound?.DisplayName
                ?? (write.Function.Length == 0 ? "(none)" : $"{write.Function} (missing)");
            if (ImGui.BeginCombo("Function", fnLabel))
            {
                foreach (ILandscapeAttributeFunction function in Landscape.Functions.Attribute)
                {
                    if (ImGui.Selectable($"{function.DisplayName}##{function.Id}", function.Id == write.Function))
                    {
                        RecordNow(write, "function", write.Function, function.Id, v => write.Function = v);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(function.Description);
                    }
                }

                ImGui.EndCombo();
            }

            if (bound != null)
            {
                DrawParameters(write, bound, write.Parameters, v => write.Parameters = v, target);
            }
            else if (write.Function.Length > 0)
            {
                ImGuiEx.TextColored(CommonColors.Error, $"No loaded function provides '{write.Function}'.");
            }

            if (ImGui.SmallButton("Remove write"))
            {
                var command = new DeleteCatalogEntityCommand(_context.Catalog, write);
                command.Apply();
                _context.EditSessions.Record(command);
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button("Add attribute write"))
        {
            Create(new LandscapeMaterialAttributeWrite
            {
                Map = material.Map,
                MaterialId = material.RecordId ?? 0,
                Attribute = attributes[0].Key,
            });
        }
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
                ImGuiEx.TextColored(CommonColors.Error, $"No loaded function provides '{boundId}'.");
            }

            ImGui.PopID();
            return;
        }

        ImGui.TextDisabled($"v{bound.Version} · reach {bound.MaxSampleRadius:0.##} units");
        DrawParameters(material, bound, serialized, setParameters);
        ImGui.PopID();
    }

    // <paramref name="attribute"/> is set only when these parameters belong to a material's terrain-
    // attribute write — it drives the AttributeValue editor (catalog picker / flags / enum). Every
    // other binding passes null and an AttributeValue parameter falls back to a plain integer.
    private void DrawParameters(
        CatalogEntity entity,
        ILandscapeFunction function,
        string serialized,
        System.Action<string> setParameters,
        TerrainAttribute? attribute = null)
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

                    TrackParameter(entity, function, values, serialized, setParameters);
                    break;
                }

                case LandscapeParameterKind.Int:
                {
                    int value = values.GetInt(parameter);
                    if (ImGui.DragInt(parameter.DisplayName, ref value, 1.0f, (int)parameter.Min, (int)parameter.Max))
                    {
                        values.Set(parameter, value);
                    }

                    TrackParameter(entity, function, values, serialized, setParameters);
                    break;
                }

                case LandscapeParameterKind.Bool:
                {
                    bool value = values.GetBool(parameter);
                    if (ImGui.Checkbox(parameter.DisplayName, ref value))
                    {
                        values.Set(parameter, value);
                        RecordNow(entity, parameter.DisplayName, serialized, values.Serialize(), setParameters);
                    }

                    break;
                }

                case LandscapeParameterKind.Channel:
                {
                    DrawChannelParameter(entity, parameter, values, serialized, setParameters);
                    break;
                }

                case LandscapeParameterKind.AttributeValue:
                {
                    DrawAttributeValueParameter(entity, function, parameter, values, serialized, setParameters, attribute);
                    break;
                }

                case LandscapeParameterKind.Color:
                {
                    Godot.Color color = values.GetColor(parameter);
                    var value = new Vector4(color.R, color.G, color.B, color.A);
                    if (ImGui.ColorEdit4(parameter.DisplayName, ref value))
                    {
                        values.Set(parameter, new Godot.Color(value.X, value.Y, value.Z, value.W));
                    }

                    TrackParameter(entity, function, values, serialized, setParameters);
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

    // The AttributeValue editor: a plain int with no attribute context, otherwise rendered against
    // the target attribute's declaration — a value picker for CatalogRef/Enum, a checkbox list for
    // Flags. The column stays a serialized integer whatever the widget.
    private void DrawAttributeValueParameter(
        CatalogEntity entity,
        ILandscapeFunction function,
        LandscapeParameter parameter,
        LandscapeParameterValues values,
        string serialized,
        System.Action<string> setParameters,
        TerrainAttribute? attribute)
    {
        uint current = values.GetUInt(parameter);

        void Commit(uint next)
        {
            values.Set(parameter, unchecked((int)next));
            RecordNow(entity, parameter.DisplayName, serialized, values.Serialize(), setParameters);
        }

        List<TerrainAttributeValue> named = attribute is { RecordId: { } id }
            ? _context.Catalog.OfType<TerrainAttributeValue>().Where(row => row.AttributeId == id).ToList()
            : [];

        if (attribute is { Kind: TerrainAttributeKind.Flags } && named.Count > 0)
        {
            foreach (TerrainAttributeValue bit in named)
            {
                uint mask = unchecked((uint)bit.Value);
                bool on = (current & mask) == mask && mask != 0;
                if (ImGui.Checkbox($"{bit.Name}##{bit.RecordId}", ref on))
                {
                    Commit(on ? current | mask : current & ~mask);
                }
            }

            return;
        }

        if (attribute is { Kind: TerrainAttributeKind.Enum } && named.Count > 0)
        {
            string label = named.FirstOrDefault(row => unchecked((uint)row.Value) == current)?.Name ?? current.ToString();
            if (ImGui.BeginCombo(parameter.DisplayName, label))
            {
                foreach (TerrainAttributeValue option in named)
                {
                    if (ImGui.Selectable($"{option.Name}##{option.RecordId}", unchecked((uint)option.Value) == current))
                    {
                        Commit(unchecked((uint)option.Value));
                    }
                }

                ImGui.EndCombo();
            }

            return;
        }

        int raw = unchecked((int)current);
        if (ImGui.DragInt(parameter.DisplayName, ref raw))
        {
            values.Set(parameter, raw);
        }

        TrackParameter(entity, function, values, serialized, setParameters);
    }

    private static readonly LandscapeSwizzle[] SwizzleChoices =
    [
        LandscapeSwizzle.Native, LandscapeSwizzle.R, LandscapeSwizzle.G, LandscapeSwizzle.B, LandscapeSwizzle.A,
        LandscapeSwizzle.Rgb, LandscapeSwizzle.Rgba, LandscapeSwizzle.Luminance,
    ];

    private static string SwizzleLabel(LandscapeSwizzle swizzle) => swizzle switch
    {
        LandscapeSwizzle.Native => "Native",
        LandscapeSwizzle.R => "R",
        LandscapeSwizzle.G => "G",
        LandscapeSwizzle.B => "B",
        LandscapeSwizzle.A => "A",
        LandscapeSwizzle.Rgb => "RGB",
        LandscapeSwizzle.Rgba => "RGBA",
        LandscapeSwizzle.Luminance => "Luminance",
        _ => swizzle.ToString(),
    };

    // Channels belong to the open map, same as the material itself, so the choices offered here are
    // always the material's own map's channels.
    private void DrawChannelParameter(
        CatalogEntity entity,
        LandscapeParameter parameter,
        LandscapeParameterValues values,
        string serialized,
        System.Action<string> setParameters)
    {
        LandscapeChannelBinding binding = values.GetChannelBinding(parameter);
        string access = parameter.Access == LandscapeChannelAccess.Write ? "writes" : "reads";
        string label = binding.IsEmpty ? "(unbound)" : binding.Channel;

        if (ImGui.BeginCombo($"{parameter.DisplayName} ({access})", label))
        {
            foreach (LandscapeChannel channel in Landscape.Catalog.Channels)
            {
                if (ImGui.Selectable(channel.Name, channel.Name == binding.Channel))
                {
                    var next = new LandscapeChannelBinding(channel.Name, binding.Swizzle);
                    values.Set(parameter, next.ToString());
                    RecordNow(entity, parameter.DisplayName, serialized, values.Serialize(), setParameters);
                }
            }

            ImGui.EndCombo();
        }

        if (binding.IsEmpty)
        {
            return;
        }

        LandscapeChannel? bound = Landscape.Catalog.Channels.FirstOrDefault(channel => channel.Name == binding.Channel);
        if (bound == null)
        {
            ImGuiEx.TextColored(CommonColors.Error, $"Channel '{binding.Channel}' does not exist on the open map.");
            return;
        }

        // A scalar (1-component) channel has nothing to swizzle: native is its only value.
        if (bound.Components == 1)
        {
            return;
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(110.0f);
        if (ImGui.BeginCombo($"##{parameter.Name}_swizzle", SwizzleLabel(binding.Swizzle)))
        {
            foreach (LandscapeSwizzle candidate in SwizzleChoices)
            {
                if (ImGui.Selectable(SwizzleLabel(candidate), candidate == binding.Swizzle))
                {
                    var next = new LandscapeChannelBinding(binding.Channel, candidate);
                    values.Set(parameter, next.ToString());
                    RecordNow(entity, $"{parameter.DisplayName} swizzle", serialized, values.Serialize(), setParameters);
                }
            }

            ImGui.EndCombo();
        }
    }

    // Numeric parameters are dragged, so the whole drag is one undo step like every other field here.
    private void TrackParameter(
        CatalogEntity entity,
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
            RecordNow(entity, $"{function.DisplayName} parameters", before, after, setParameters);
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
