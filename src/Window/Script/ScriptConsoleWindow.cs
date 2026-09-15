#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// A REPL over the shared <see cref="ScriptEngineHost"/> — the human-facing, interactive way to
/// exercise the scripting API surface while it's being built out. Secondary to the eventual HTTP
/// endpoint (see ScriptingPlan.md's Phase 12): this is for developing/debugging the API, not the
/// primary way scripts get run.
///
/// Submits through <see cref="ScriptEngineHost.EvaluateAsync"/> rather than the synchronous
/// <see cref="ScriptEngineHost.Evaluate"/>, so a script that uses <c>await</c> shows the value it
/// settled on instead of <c>[object Promise]</c>. The cost is that every result lands a frame or more
/// later, which is what the pending placeholder is for.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ScriptConsoleWindow : Window
{
    public override string? Category => "Developer";
    public override KeyboardShortcut DefaultShortcut => new(ImGuiKey.F, ShortcutModifiers.Alt);

    private sealed class Entry(string input, Task<ScriptResult> pending)
    {
        public string Input { get; } = input;

        public Task<ScriptResult> Pending { get; } = pending;

        /// <summary>What to show: the settled result, or a placeholder while it is still running.</summary>
        public string Output => Pending.IsCompletedSuccessfully
            ? Pending.Result.Success ? Pending.Result.Output : $"Error: {Pending.Result.Output}"
            : "…";
    }

    private readonly ScriptingSystem _scripting;
    private readonly List<Entry> _history = [];

    private string _input = "";
    private bool _scrollToBottom;

    public ScriptConsoleWindow(WindowManager manager)
        : base("Script Console", startOpen: false, defaultSize: new NVector2(640, 420))
    {
        _scripting = manager.Context.Scripting;
    }

    protected override void DrawContent()
    {
        if (_scripting.Engine is not { } engine)
        {
            ImGui.TextDisabled("Scripting has not started yet.");
            return;
        }

        DrawHistory();

        ImGui.Separator();
        ImGui.PushItemWidth(-70.0f);
        bool submitted = ImGui.InputText("##ScriptInput", ref _input, 4096, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        submitted |= ImGui.Button("Run");

        if (submitted && _input.Length > 0)
        {
            _history.Add(new Entry(_input, engine.EvaluateAsync(_input)));
            _input = "";
            _scrollToBottom = true;
            ImGui.SetKeyboardFocusHere(-1);
        }
    }

    private void DrawHistory()
    {
        ImGui.BeginChild("##ScriptHistory", new NVector2(0, -32), true);
        using (ImGuiEx.PushFont(CommonFonts.Monospace))
        {
            foreach (Entry entry in _history)
            {
                ImGuiEx.TextColored(CommonColors.Accent, $"> {entry.Input}");
                ImGui.TextWrapped(entry.Output);
            }
        }

        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();
    }
}
