using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

public sealed partial class ExportSystem : ISubsystemHost
{
    public EditorContext Context { get; }

    public ChunkChangeRegistry Changes { get; }

    public ExportProfileRegistry Profiles { get; }

    public IEnumerable<IChunkExportScript> Exporters => Subsystems.OfType<IChunkExportScript>();

    public ExportSystem(EditorContext context)
    {
        Context = context;
        Changes = new ChunkChangeRegistry(context);
        InitializeSubsystems();
        Profiles = new ExportProfileRegistry(context);
    }

    public LandscapeSettings? LoadLandscapeSettings(MapId map) => Context.Landscape.LoadSettingsFor(map);

    /// <summary>Schedules an export behind <see cref="WorldOperations"/>' exclusive gate — refuses
    /// (returning null) if the edit session is dirty or another exclusive operation is already running,
    /// and no session edit can be recorded until the export finishes. An export only ever reads
    /// *committed* chunk changes, so this isn't about correctness so much as making sure nobody starts
    /// painting terrain a running export is about to read, or loses track of an export that's still
    /// in flight; it doesn't touch the loaded world's own content, so it skips the reload
    /// <see cref="WorldOperations.TryRun"/> would otherwise request afterward.</summary>
    public WorkHandle? Run(ExportProfile profile, ChunkExportScope scope) =>
        Run(profile, Changes.DirtyFor(profile.Id, scope), onSuccess: chunks => Changes.MarkExported(profile.Id, chunks));

    /// <summary>Exports every chunk in an explicit range regardless of dirty status. On full success the
    /// range's chunks are marked exported too, so a dirty-based export won't redo them afterward.</summary>
    public WorkHandle? RunRange(ExportProfile profile, ChunkRange range) =>
        Run(profile, Changes.ForRange(range), onSuccess: chunks => Changes.MarkExported(profile.Id, chunks));

    /// <summary>Force-redirties a profile's exported state for a scope, so the next export re-does it
    /// even though content hasn't changed.</summary>
    public void ClearDirty(ExportProfile profile, ChunkExportScope scope) => Changes.ClearExported(profile.Id, scope);

    /// <summary>Force-redirties a profile's exported state for an explicit range.</summary>
    public void ClearDirty(ExportProfile profile, ChunkRange range) => Changes.ClearExported(profile.Id, range);

    private WorkHandle? Run(ExportProfile profile, IReadOnlyList<ChunkChange> chunks, Action<IReadOnlyList<ChunkChange>> onSuccess)
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
                ChunkExportResult result = await exporter.ExportAsync(context, chunks, work).ConfigureAwait(false);

                if (result.ExportedChunks == chunks.Count && chunks.Count > 0)
                {
                    work.Step("Updating export state");
                    onSuccess(chunks);
                }
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
