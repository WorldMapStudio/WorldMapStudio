using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorldMapStudio;

/// <summary>
/// One long job that takes exclusive ownership of the editor: an export, an import, a migration, a
/// bulk fixup. Registers with <c>[Subsystem(nameof(BatchSystem))]</c>.
///
/// An operation is handed nothing to process. What to do, and what not to redo, is entirely its own
/// business — it asks <see cref="BatchContext.ChunkChanges"/> (or anything else) for whatever it wants
/// and keeps whatever cache that needs itself. It reports through <see cref="BatchContext"/> and fails
/// by throwing.
/// </summary>
public interface IBatchOperation : ISubsystem
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>A sentence about what this operation does. Shown in the window, and the only thing a
    /// script listing operations has to go on.</summary>
    string Description { get; }

    void DrawSettings();

    /// <summary>Captures this operation's current settings fields into a storable blob.</summary>
    JsonObject SaveSettings();

    /// <summary>Restores settings fields from a blob produced by <see cref="SaveSettings"/>. Missing
    /// keys (a first run, or a settings shape from an older version) must fall back to sensible
    /// defaults rather than throwing.</summary>
    void LoadSettings(JsonObject settings);

    /// <summary>
    /// The body. Runs on a background worker; hop to the main thread with
    /// <see cref="WorkContext.SwitchToMain"/> only for what genuinely needs it (a Godot node, the
    /// procedural system) and hop straight back — a long stretch there freezes the UI. Read settings
    /// from <see cref="BatchContext.Settings"/>, never from this instance's own fields, which the
    /// window keeps editing while the run is in flight.
    /// </summary>
    Task RunAsync(BatchContext context, WorkContext work);
}
