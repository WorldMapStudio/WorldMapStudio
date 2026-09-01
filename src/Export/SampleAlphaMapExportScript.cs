using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ImGuiNET;

namespace WorldMapStudio;

[Subsystem(nameof(ExportSystem))]
public sealed class SampleAlphaMapExportScript : IChunkExportScript
{
    private string _folder = "";
    private bool _writeEmptySlots;

    public string Id => "wms.sample.alphamaps";

    public string DisplayName => "Sample Alphamaps";

    public float Priority => 0.0f;

    public SampleAlphaMapExportScript(ExportSystem exports)
    {
    }

    public void DrawSettings()
    {
        ImGui.InputTextWithHint("Output", "Project exports/alphamaps", ref _folder, 512);
        ImGui.Checkbox("Write empty slots", ref _writeEmptySlots);
    }

    public JsonObject SaveSettings() => new()
    {
        ["folder"] = _folder,
        ["writeEmptySlots"] = _writeEmptySlots,
    };

    public void LoadSettings(JsonObject settings)
    {
        _folder = settings["folder"]?.GetValue<string>() ?? "";
        _writeEmptySlots = settings["writeEmptySlots"]?.GetValue<bool>() ?? false;
    }

    public async Task<ChunkExportResult> ExportAsync(
        ChunkExportContext context,
        IReadOnlyList<ChunkChange> chunks,
        WorkContext work)
    {
        string root = string.IsNullOrWhiteSpace(_folder)
            ? Path.Combine(context.ProjectFolder, "exports", "alphamaps")
            : _folder.Trim();

        Directory.CreateDirectory(root);

        int exported = 0;
        for (int i = 0; i < chunks.Count; i++)
        {
            work.ThrowIfCancellationRequested();
            ChunkChange change = chunks[i];
            work.Step($"Exporting {i + 1}/{chunks.Count} ({change.Map.Value}:{change.Coord})");

            LandscapeChunkOutput? output = await context.BuildLandscapeChunkAsync(change.Map, change.Coord)
                .ConfigureAwait(false);
            if (output == null)
            {
                continue;
            }

            string mapFolder = Path.Combine(root, change.Map.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(mapFolder);
            WriteAlphas(mapFolder, output);
            exported++;

            if (i % 8 == 7)
            {
                await work.Yield();
            }
        }

        return new ChunkExportResult(exported, $"Exported {exported} chunks.");
    }

    private void WriteAlphas(string folder, LandscapeChunkOutput output)
    {
        for (int slot = 0; slot < output.Layers.Count; slot++)
        {
            LandscapeChunkLayer layer = output.Layers[slot];
            if (layer.Alpha == null && !_writeEmptySlots)
            {
                continue;
            }

            byte[] alpha = layer.Alpha ?? new byte[output.AlphaResolution * output.AlphaResolution];
            string material = Sanitize(layer.Material?.Name ?? "none");
            string file = Path.Combine(folder, $"chunk_{output.Coord.X}_{output.Coord.Y}_slot_{slot}_{material}.pgm");
            WritePgm(file, output.AlphaResolution, alpha);
        }
    }

    private static void WritePgm(string path, int resolution, byte[] pixels)
    {
        byte[] header = Encoding.ASCII.GetBytes($"P5\n{resolution} {resolution}\n255\n");
        byte[] data = new byte[header.Length + pixels.Length];
        header.CopyTo(data, 0);
        pixels.CopyTo(data, header.Length);
        File.WriteAllBytes(path, data);
    }

    private static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Length == 0 ? "unnamed" : value;
    }
}
