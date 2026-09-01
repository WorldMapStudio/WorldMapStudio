using System;
using System.Text.Json.Nodes;

namespace WorldMapStudio;

/// <summary>
/// A user-authored, named export configuration: which <see cref="IChunkExportScript"/> to run and
/// that exporter's settings (via <see cref="IChunkExportScript.SaveSettings"/>/<see cref="IChunkExportScript.LoadSettings"/>).
/// <see cref="Id"/> is the key dirty/exported chunk state is tracked under, so two profiles pointing
/// at the same exporter track their own independent state.
/// </summary>
public sealed class ExportProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "";

    public string ExporterId { get; set; } = "";

    public JsonObject Settings { get; set; } = new();
}
