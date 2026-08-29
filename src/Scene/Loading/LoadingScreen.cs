#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Runs one blocking phase of opening a project on a background task while showing a centered progress
/// bar, then hands off to whatever comes next. On failure it shows the error and offers a way back to
/// <see cref="ProjectSelect"/>.
///
/// Used twice, because opening a project has two blocking phases with the migration gate between them:
/// <list type="number">
/// <item><see cref="EditorContext.Startup"/> — launch dolt, ensure databases and schemas, check for
/// drift. Followed by <see cref="Migration"/> if there is any.</item>
/// <item><see cref="EditorContext.LoadContent"/> — read the maps and the landscape, which the migration
/// gate has to run before, since their tables may not exist until it has.</item>
/// </list>
/// The second phase used to run in <c>Editor.Start()</c> instead, which is the Godot main thread — so
/// the two largest reads in the whole open sequence were the two that froze the window.
/// </summary>
public sealed class LoadingScreen : IScene
{
    // A confirm prompt raised by the background phase, waiting for the main thread to resolve it.
    // The background thread blocks on Signal, not on anything Godot — Update() is the only thing that
    // ever sets it, so there's no risk of the main-thread-async deadlock this pattern would otherwise invite.
    private sealed class PendingConfirm
    {
        public required ModalConfirm Modal;
        public readonly ManualResetEventSlim Signal = new(false);
        public bool Result;
    }

    private readonly Node3D _root;
    private readonly string _caption;
    private readonly Action<Action<string>, Func<string, bool>> _work;
    private readonly Func<IScene> _next;

    private volatile string _step = "Preparing";
    private volatile bool _ready;
    private volatile string? _error;
    private volatile PendingConfirm? _confirm;

    /// <param name="caption">Headline shown above the bar, e.g. "Opening Azeroth".</param>
    /// <param name="work">The blocking phase. Runs on a background thread; reports progress by calling
    /// its first argument with a step name, and can block on its second argument to ask the user a
    /// yes/no question (see <see cref="Confirm"/>).</param>
    /// <param name="next">The scene to move to once the phase succeeds.</param>
    public LoadingScreen(Node3D root, string caption, Action<Action<string>, Func<string, bool>> work, Func<IScene> next)
    {
        _root = root;
        _caption = caption;
        _work = work;
        _next = next;
    }

    /// <summary>
    /// Opens a project: constructs the context (cheap — subsystems only wire themselves up), then runs
    /// the two blocking phases with the migration gate between them.
    /// </summary>
    public static LoadingScreen OpenProject(Node3D root, Project project)
    {
        var context = new EditorContext(root, project);

        return new LoadingScreen(
            root,
            $"Opening {project.Name}",
            (step, confirm) => context.Startup(step, confirm),
            () => context.Migrations.HasPending
                ? new Migration(root, context)
                : LoadContent(root, context));
    }

    /// <summary>The second phase, entered directly by <see cref="Migration"/> once the user continues.</summary>
    public static LoadingScreen LoadContent(Node3D root, EditorContext context) =>
        new(root,
            $"Opening {context.Project.Name}",
            (step, _) => context.LoadContent(step),
            () => new Editor(context));

    public void Start()
    {
        _ = Task.Run(() =>
        {
            try
            {
                _work(step => _step = step, Confirm);
                _ready = true;
            }
            catch (Exception e)
            {
                _error = e.Message;
                GD.PushError($"[Loading] {_caption} failed: {e}");
            }
        });
    }

    /// <summary>
    /// Called from the background phase to ask the user a yes/no question. Blocks that thread until
    /// <see cref="Update"/> (main thread) draws the modal and the user picks an answer.
    /// </summary>
    private bool Confirm(string message)
    {
        var pending = new PendingConfirm { Modal = new ModalConfirm("Confirm", message, "Kill and Retry", "Cancel") };
        pending.Modal.Show();
        _confirm = pending;
        pending.Signal.Wait();
        return pending.Result;
    }

    public IScene? Update()
    {
        if (_ready)
        {
            return _next();
        }

        IScene? scene = this;

        ImGuiEx.FullScreen("Loading", ImGuiWindowFlags.None, () =>
        {
            ImGuiEx.Center(320, 40, 15, center =>
            {
                center.Label(_caption);

                if (_error == null)
                {
                    // No real fraction to report, so animate a marquee-style bar for liveness.
                    float fraction = (float)((ImGui.GetTime() * 0.6) % 1.0);
                    center.ProgressBar(fraction, $"{_step}…");
                }
                else
                {
                    center.LabelColored(_error, new System.Numerics.Vector4(0.9f, 0.4f, 0.4f, 1f));
                    center.Button("Back", () => scene = new ProjectSelect(_root));
                }
            });
        });

        PendingConfirm? confirm = _confirm;
        if (confirm != null)
        {
            ModalOperationState state = confirm.Modal.Draw(true);
            if (state is ModalOperationState.Confirmed or ModalOperationState.Cancelled)
            {
                confirm.Result = state == ModalOperationState.Confirmed;
                _confirm = null;
                confirm.Signal.Set();
            }
        }

        return scene;
    }
}
