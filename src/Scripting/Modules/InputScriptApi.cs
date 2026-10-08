using System;
using System.Threading.Tasks;
using Godot;

namespace WorldMapStudio;

/// <summary>Synthetic pointer and keyboard input for the editor window, exposed to JS as <c>wms.input</c>. Coordinates are window pixels, as in <c>wms.editor.Screenshot</c>.</summary>
[Subsystem(nameof(ScriptingSystem))]
public sealed class InputScriptApi : IScriptModule
{
    // Time given to ImGui to see the pointer move or a key change before the next event.
    private const int FrameMilliseconds = 60;

    public string Name => "input";

    public InputScriptApi(ScriptingSystem system)
    {
    }

    /// <summary>Moves the pointer to (x, y) and keeps it there, overriding the OS mouse until <see cref="Release"/>.</summary>
    [ScriptFunction]
    public async Task Move(double x, double y)
    {
        ImGuiInput.SyntheticMouse = new Vector2((float)x, (float)y);
        Send(new InputEventMouseMotion { Position = new Vector2((float)x, (float)y), GlobalPosition = new Vector2((float)x, (float)y) });
        await Task.Delay(FrameMilliseconds).ConfigureAwait(false);
    }

    /// <summary>Clicks at (x, y) with "left", "right" or "middle".</summary>
    [ScriptFunction]
    public async Task Click(double x, double y, string button = "left")
    {
        MouseButton index = button switch
        {
            "left" => MouseButton.Left,
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => throw new ArgumentException($"Unknown mouse button '{button}'."),
        };
        await Move(x, y).ConfigureAwait(false);
        var position = new Vector2((float)x, (float)y);
        Send(new InputEventMouseButton { ButtonIndex = index, Pressed = true, Position = position, GlobalPosition = position });
        await Task.Delay(FrameMilliseconds).ConfigureAwait(false);
        Send(new InputEventMouseButton { ButtonIndex = index, Pressed = false, Position = position, GlobalPosition = position });
        await Task.Delay(FrameMilliseconds).ConfigureAwait(false);
    }

    /// <summary>Presses and releases a key by Godot name (e.g. "Enter", "Escape", "A", "F5").</summary>
    [ScriptFunction]
    public async Task Key(string name, bool ctrl = false, bool shift = false, bool alt = false)
    {
        Key key = OS.FindKeycodeFromString(name);
        if (key == Godot.Key.None)
        {
            throw new ArgumentException($"Unknown key '{name}'.");
        }

        foreach (bool pressed in new[] { true, false })
        {
            Send(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed, CtrlPressed = ctrl, ShiftPressed = shift, AltPressed = alt });
            await Task.Delay(FrameMilliseconds).ConfigureAwait(false);
        }
    }

    /// <summary>Types text into the focused text field.</summary>
    [ScriptFunction]
    public async Task Type(string text)
    {
        foreach (char c in text)
        {
            Send(new InputEventKey { Unicode = c, Pressed = true });
            await Task.Delay(FrameMilliseconds / 3).ConfigureAwait(false);
        }
    }

    /// <summary>Gives the pointer back to the OS mouse.</summary>
    [ScriptFunction]
    public void Release() => ImGuiInput.SyntheticMouse = null;

    private static void Send(InputEvent inputEvent) =>
        Callable.From(() => Godot.Input.ParseInputEvent(inputEvent)).CallDeferred();
}
