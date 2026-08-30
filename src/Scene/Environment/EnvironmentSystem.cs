using System.Collections.Generic;
using System.Linq;
using Godot;

namespace WorldMapStudio;

/// <summary>
/// Collects every loaded <see cref="IEnvironmentSource"/> component, blends them against the viewport
/// focus and the world clock, and exposes the result. A plain member of <see cref="EditorContext"/>
/// (the core spine, not an extension point), mirroring how <see cref="LandscapeSystem"/> collects
/// <see cref="ILandscapeDeformer"/>s from the same scene registry.
/// </summary>
public sealed class EnvironmentSystem : IWorldParticipant
{
    // How far the focus must move before a re-blend is worth it. A moving camera's exact position
    // changes practically every frame; comparing it exactly (rather than within this radius) meant
    // Update recomputed — and EnvironmentRenderer rebuilt its sky material — on nearly every frame
    // while flying, not just when a source's weight would actually have changed.
    private const float RescanDistance = 8.0f;

    private readonly EditorContext _context;

    private EnvironmentValues _current = new();
    private IReadOnlyList<(IEnvironmentSource Source, float Weight)> _active = [];

    private bool _forceUpdate = true;
    private Vector3 _lastFocus;
    private float _lastDayFraction = -1.0f;
    private int _lastSceneVersion = -1;

    public EnvironmentSystem(EditorContext context)
    {
        _context = context;
    }

    /// <summary>The blended environment as of the last <see cref="Update"/>.</summary>
    public EnvironmentValues Current => _current;

    /// <summary>
    /// Every source that contributed to <see cref="Current"/> with its resolved weight, in the order
    /// blended: the global source (weight 1) first if there is one, then the rest weakest to strongest.
    /// </summary>
    public IReadOnlyList<(IEnvironmentSource Source, float Weight)> Active => _active;

    public bool HasSources => Sources.Any();

    /// <summary>Bumps whenever <see cref="Current"/> is recomputed, so views can tell when to refresh.</summary>
    public int Version { get; private set; }

    private IEnumerable<IEnvironmentSource> Sources =>
        _context.Scene.Entities.SelectMany(entity => entity.Components).OfType<IEnvironmentSource>();

    /// <summary>
    /// Forces the next <see cref="Update"/> to recompute regardless of what moved. An edit to a
    /// source's own fields (its colours, its radii) has to be able to say "what you have is stale",
    /// the same way <see cref="StreamingSystem.Invalidate"/> does for streamed content.
    /// </summary>
    public void Invalidate() => _forceUpdate = true;

    /// <summary>
    /// Recomputes <see cref="Current"/> if the focus, the clock, or the scene moved since the last
    /// call; otherwise does nothing. Cheap to call every frame from the viewport.
    /// </summary>
    public void Update(Vector3 focus)
    {
        float dayFraction = _context.Clock.DayFraction;
        int sceneVersion = _context.Scene.Version;

        if (!_forceUpdate
            && focus.DistanceSquaredTo(_lastFocus) < RescanDistance * RescanDistance
            && Mathf.IsEqualApprox(dayFraction, _lastDayFraction)
            && sceneVersion == _lastSceneVersion)
        {
            return;
        }

        _forceUpdate = false;
        _lastFocus = focus;
        _lastDayFraction = dayFraction;
        _lastSceneVersion = sceneVersion;

        EnvironmentBlender.Result result = EnvironmentBlender.Blend(
            Sources, focus, new EnvironmentTime(dayFraction, _context.Clock.Elapsed));
        _current = result.Current;
        _active = result.Active;
        Version++;
    }

    // Sources come from the scene registry, which a reload clears out from under this — the blended
    // result has to be forgotten too, or it goes on reporting sources that no longer exist until the
    // focus happens to move far enough to force a recompute.
    void IWorldParticipant.UnloadWorld()
    {
        _current = new();
        _active = [];
        _forceUpdate = true;
        _lastSceneVersion = -1;
    }
}
