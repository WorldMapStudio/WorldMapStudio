#nullable enable
using System;
using System.Threading.Tasks;
using Godot;
using ImGuiNET;

namespace WorldMapStudio;

/// <summary>
/// Bridges project selection and the editor: it builds the <see cref="EditorContext"/> and runs its
/// blocking startup (launching dolt, ensuring schemas, checking migrations) on a background task while
/// showing a centered progress bar, so the app never freezes. On success it transitions into the
/// <see cref="Editor"/> with the ready context; on failure it shows the error and returns to
/// <see cref="ProjectSelect"/>.
/// </summary>
public sealed class LoadingScreen : IScene
{
    private readonly Node3D _root;
    private readonly Project _project;

    private EditorContext _context = null!;
    private volatile string _step = "Preparing";
    private volatile bool _ready;
    private volatile string? _error;

    public LoadingScreen(Node3D root, Project project)
    {
        _root = root;
        _project = project;
    }

    public void Start()
    {
        // Constructing the context is cheap (subsystems only wire themselves up); the expensive,
        // blocking part is Startup(), which we push onto a background thread so the UI keeps drawing.
        _context = new EditorContext(_root, _project);

        _ = Task.Run(() =>
        {
            try
            {
                _context.Startup(step => _step = step);
                _ready = true;
            }
            catch (Exception e)
            {
                _error = e.Message;
                GD.PushError($"[Loading] Failed to open '{_project.Name}': {e}");
            }
        });
    }

    public IScene? Update()
    {
        if (_ready)
        {
            return new Editor(_context);
        }

        IScene? scene = this;

        ImGuiEx.FullScreen("Loading", ImGuiWindowFlags.None, () =>
        {
            ImGuiEx.Center(320, 40, 15, center =>
            {
                center.Label($"Opening {_project.Name}");

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

        return scene;
    }
}
