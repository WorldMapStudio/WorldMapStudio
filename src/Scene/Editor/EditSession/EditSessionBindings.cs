using System;

namespace WorldMapStudio;

/// <summary>
/// What an <see cref="EditSessionManager"/> reaches out to. Each is read when used rather than when the
/// manager is built, because the manager is constructed before the systems these point at.
/// </summary>
/// <param name="Store">Where a commit is written. Null keeps the edits in memory.</param>
/// <param name="Streaming">Re-judged after a commit or abort, so entities that stayed loaded only for
/// the edit can unload. Null leaves the loaded set untouched.</param>
/// <param name="RequestReload">What <see cref="EditSessionManager.Abort"/> asks for once it has reverted
/// in memory. Null makes an abort the in-memory revert alone.</param>
/// <param name="ActiveOperation">The exclusive world operation running, if any; recording is refused
/// while it is non-null.</param>
public sealed record EditSessionBindings(
    Func<IEditSessionStore?> Store,
    Func<StreamingSystem?> Streaming,
    Action? RequestReload,
    Func<string?> ActiveOperation)
{
    public static readonly EditSessionBindings None = new(() => null, () => null, null, () => null);
}
