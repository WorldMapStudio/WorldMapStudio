using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

public sealed partial class ExportSystem : ISubsystemHost
{
    public EditorContext Context { get; }

    public ChunkChangeLog Changes => Context.ChunkChanges;

    public ExportProfileRegistry Profiles { get; }

    public IEnumerable<IChunkExportScript> Exporters => Subsystems.OfType<IChunkExportScript>();

    public ExportSystem(EditorContext context)
    {
        Context = context;
        InitializeSubsystems();
        Profiles = new ExportProfileRegistry(context);
    }

    public LandscapeSettings? LoadLandscapeSettings(MapId map) => Context.Landscape.LoadSettingsFor(map);

    // Scheduled for deletion: this whole system is replaced by BatchSystem, and its per-profile
    // export cache is already gone — every run now re-exports everything it is pointed at. Kept only
    // so the plugin's exporter compiles until it moves onto IBatchOperation.

    /// <summary>Schedules an export behind <see cref="WorldOperations"/>' exclusive gate — refuses
    /// (returning null) if the edit session is dirty or another exclusive operation is already running,
    /// and no session edit can be recorded until the export finishes.</summary>
    public WorkHandle? Run(ExportProfile profile, ChunkExportScope scope) =>
        Run(profile, BlockingWork.Run(() => Changes.ChangedSinceAsync(
            DateTime.MinValue,
            scope == ChunkExportScope.CurrentMap ? Context.Maps.CurrentMap : null)));

    /// <summary>Exports every edited chunk in an explicit range.</summary>
    public WorkHandle? RunRange(ExportProfile profile, ChunkRange range) =>
        Run(profile, BlockingWork.Run(() => Changes.InRangeAsync(range)));

    private WorkHandle? Run(ExportProfile profile, IReadOnlyList<ChunkChange> chunks)
    {
        if (Exporters.FirstOrDefault(candidate => candidate.Id == profile.ExporterId) is not { } exporter)
        {
            GD.PushWarning($"[Export] Profile '{profile.Name}' targets unknown exporter '{profile.ExporterId}'.");
            return null;
        }

        exporter.LoadSettings(profile.Settings);

        WorkHandle? handle = Context.Operations.TryRun(
            $"{profile.Name}: {chunks.Count} chunks",
            async work =>
            {
                work.Step("Exporting");
                var context = new ChunkExportContext(this, profile.Id);
                await exporter.ExportAsync(context, chunks, work).ConfigureAwait(false);
            },
            out string? blocker,
            reloadAfter: false);

        if (handle == null)
        {
            GD.PushWarning($"[Export] Refused to start '{profile.Name}': {blocker}");
        }

        return handle;
    }

    internal Task<LandscapeBuildResult?> BuildLandscapeChunksAsync(MapId map, IReadOnlyList<ChunkCoord> coords) =>
        Context.Landscape.BuildFromStorageAsync(map, coords);

    /// <summary>A profile's previously-assigned stable ids, keyed by <see cref="EntityId"/> — for a
    /// target format that needs one but has no id of its own to reuse (e.g. an ADT placement's unique
    /// id). Empty when the editor storage isn't the default <see cref="EditorStorage"/>.</summary>
    public async Task<IReadOnlyDictionary<long, long>> LoadExportedEntityIdsAsync(string profileId)
    {
        if (Context.Database.Storages.OfType<EditorStorage>().FirstOrDefault() is not { } storage)
        {
            return new Dictionary<long, long>();
        }

        return await storage.LoadExportedEntityIdsAsync(profileId).ConfigureAwait(false);
    }

    /// <summary>Persists newly-assigned or changed entity ids from <see cref="LoadExportedEntityIdsAsync"/>.</summary>
    public async Task UpsertExportedEntityIdsAsync(string profileId, IReadOnlyDictionary<long, long> ids)
    {
        if (Context.Database.Storages.OfType<EditorStorage>().FirstOrDefault() is { } storage)
        {
            await storage.UpsertExportedEntityIdsAsync(profileId, ids).ConfigureAwait(false);
        }
    }

    internal Task<IReadOnlyList<SceneEntity>> ScanSceneAsync(MapId map, Aabb region) =>
        Context.Database.ScanSceneAsync(map, region);
}
