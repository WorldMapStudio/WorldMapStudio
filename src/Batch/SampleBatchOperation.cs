using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// The worked example, and the reference implementation of the watermark pattern every real operation
/// follows: read a watermark, ask what changed after it, do the work, store the newest edit time you
/// actually saw. Writes each changed chunk's alpha slots out as PGM files, which is just something
/// cheap to have produced.
///
/// The one part worth copying exactly is the watermark. It is taken from the rows the run observed,
/// never from <see cref="DateTime.UtcNow"/> — a commit that lands mid-run would fall on the wrong
/// side of a wall-clock stamp and be skipped forever, whereas taking it from the data makes the worst
/// case a redundant reprocess.
/// </summary>
[Subsystem(nameof(BatchSystem))]
public sealed class SampleBatchOperation : IBatchOperation
{
    private const string WatermarkKey = "exportedUpTo";

    private string _folder = "";
    private bool _writeEmptySlots;

    public string Id => "wms.sample.alphamaps";

    public string DisplayName => "Sample Alphamaps";

    public string Description =>
        "Writes an 8-bit PGM per alpha slot for every chunk edited since the last run.";

    public SampleBatchOperation(BatchSystem batch)
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

    public async Task RunAsync(BatchContext context, WorkContext work)
    {
        string folder = context.Settings["folder"]?.GetValue<string>() ?? "";
        bool writeEmptySlots = context.Settings["writeEmptySlots"]?.GetValue<bool>() ?? false;

        string root = string.IsNullOrWhiteSpace(folder)
            ? Path.Combine(context.ProjectFolder, "exports", "alphamaps")
            : folder.Trim();

        DateTime watermark = ParseWatermark(await context.State.GetAsync(WatermarkKey).ConfigureAwait(false));
        IReadOnlyList<ChunkChange> changes = await context.ChunkChanges.ChangedSinceAsync(watermark).ConfigureAwait(false);

        if (changes.Count == 0)
        {
            context.Report("Nothing changed since the last run.");
            return;
        }

        Directory.CreateDirectory(root);

        for (int i = 0; i < changes.Count; i++)
        {
            work.ThrowIfCancellationRequested();

            ChunkChange change = changes[i];
            context.Step($"Chunk {i + 1}/{changes.Count} ({change.Map.Value}:{change.Coord})");
            context.Progress((i + 1) / (float)changes.Count);

            LandscapeBuildResult? built = await context.BuildChunksAsync(change.Map, [change.Coord]).ConfigureAwait(false);
            if (built?.Chunks.GetValueOrDefault(change.Coord) is not { } output)
            {
                continue;
            }

            string mapFolder = Path.Combine(root, change.Map.Value.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(mapFolder);
            WriteAlphas(mapFolder, output, writeEmptySlots);
            context.Log($"{change.Map.Value}:{change.Coord}");

            if (i % 8 == 7)
            {
                await work.Yield();
            }
        }

        // The newest edit this run actually saw — not the clock. See the class remarks.
        DateTime observed = changes.Max(change => change.LastEditedUtc);
        await context.State.SetAsync(WatermarkKey, observed.ToString("O", CultureInfo.InvariantCulture)).ConfigureAwait(false);

        context.Report($"Wrote {changes.Count} chunk(s).");
    }

    private static DateTime ParseWatermark(string? stored) =>
        DateTime.TryParse(stored, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime value)
            ? value
            : DateTime.MinValue;

    private static void WriteAlphas(string folder, LandscapeChunkOutput output, bool writeEmptySlots)
    {
        for (int slot = 0; slot < output.Layers.Count; slot++)
        {
            LandscapeChunkLayer layer = output.Layers[slot];
            if (layer.Alpha == null && !writeEmptySlots)
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
