#nullable enable
using System.Collections.Generic;
using ImGuiNET;
using NVector2 = System.Numerics.Vector2;

namespace WorldMapStudio;

/// <summary>
/// A REPL over the shared <see cref="ScriptEngineHost"/> — the human-facing, interactive way to
/// exercise the scripting API surface while it's being built out. Secondary to the eventual HTTP
/// endpoint (see ScriptingPlan.md's Phase 12): this is for developing/debugging the API, not the
/// primary way scripts get run.
/// </summary>
[Subsystem(nameof(WindowManager))]
public sealed class ScriptConsoleWindow : Window
{
    private readonly ScriptingSystem _scripting;
    private readonly List<(string Input, string Output)> _history = [];

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
            _history.Add((_input, engine.Evaluate(_input)));
            _input = "";
            _scrollToBottom = true;
            ImGui.SetKeyboardFocusHere(-1);
        }
    }

    private void DrawHistory()
    {
        ImGui.BeginChild("##ScriptHistory", new NVector2(0, -32), true);
        foreach ((string input, string output) in _history)
        {
            ImGui.TextColored(new System.Numerics.Vector4(0.55f, 0.75f, 1.0f, 1.0f), $"> {input}");
            ImGui.TextWrapped(output);
        }

        if (_scrollToBottom)
        {
            ImGui.SetScrollHereY(1.0f);
            _scrollToBottom = false;
        }

        ImGui.EndChild();
    }
}
