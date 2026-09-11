using System.Collections.Generic;

namespace WorldMapStudio;

/// <summary>
/// Generates a set of <see cref="IViewCategory"/>s at discovery time rather than declaring them as
/// individual subsystem classes — for an axis with one category per registered kind of something
/// else (e.g. one per <c>IModelFormat</c>), where the categories themselves have no fixed identity to
/// attach <c>[Subsystem(nameof(ViewCategorySystem))]</c> to.
/// </summary>
public interface IViewCategorySource : ISubsystem
{
    IEnumerable<IViewCategory> Categories();
}
