using Godot;
using ImGuiNET;
using System;

namespace WorldMapStudio;

internal sealed class ImGuiInput
{
    private readonly GodotImGui _owner;
    private readonly bool _hasMouse = DisplayServer.HasFeature(DisplayServer.Feature.Mouse);
    private Vector2 _mouseWheel = Vector2.Zero;
    private ImGuiMouseCursor _currentCursor = ImGuiMouseCursor.None;

    public ImGuiInput(GodotImGui owner)
    {
        _owner = owner;
    }

    public void Update(ImGuiIOPtr io)
    {
        if (!_hasMouse)
        {
            return;
        }

        if (io.WantSetMousePos)
        {
            Godot.Input.WarpMouse(new Vector2(io.MousePos.X, io.MousePos.Y));
        }
        else
        {
            Vector2I mousePos = DisplayServer.MouseGetPosition() - _owner.GetWindow().Position;
            io.AddMousePosEvent(mousePos.X, mousePos.Y);
        }

        if (_mouseWheel != Vector2.Zero)
        {
            io.AddMouseWheelEvent(_mouseWheel.X, _mouseWheel.Y);
            _mouseWheel = Vector2.Zero;
        }

        if (io.WantCaptureMouse && !io.ConfigFlags.HasFlag(ImGuiConfigFlags.NoMouseCursorChange))
        {
            ImGuiMouseCursor cursor = ImGui.GetMouseCursor();
            if (cursor != _currentCursor)
            {
                DisplayServer.CursorSetShape(ConvertCursorShape(cursor));
                _currentCursor = cursor;
            }
        }
        else
        {
            _currentCursor = ImGuiMouseCursor.None;
        }
    }

    public bool ProcessInput(InputEvent inputEvent)
    {
        ImGuiIOPtr io = ImGui.GetIO();

        if (inputEvent is InputEventMouseButton mouseButton)
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.Left:
                    io.AddMouseButtonEvent((int)ImGuiMouseButton.Left, mouseButton.Pressed);
                    break;
                case MouseButton.Right:
                    io.AddMouseButtonEvent((int)ImGuiMouseButton.Right, mouseButton.Pressed);
                    break;
                case MouseButton.Middle:
                    io.AddMouseButtonEvent((int)ImGuiMouseButton.Middle, mouseButton.Pressed);
                    break;
                case MouseButton.Xbutton1:
                    io.AddMouseButtonEvent((int)ImGuiMouseButton.Middle + 1, mouseButton.Pressed);
                    break;
                case MouseButton.Xbutton2:
                    io.AddMouseButtonEvent((int)ImGuiMouseButton.Middle + 2, mouseButton.Pressed);
                    break;
                case MouseButton.WheelUp:
                    _mouseWheel.Y = mouseButton.Factor;
                    break;
                case MouseButton.WheelDown:
                    _mouseWheel.Y = -mouseButton.Factor;
                    break;
                case MouseButton.WheelLeft:
                    _mouseWheel.X = -mouseButton.Factor;
                    break;
                case MouseButton.WheelRight:
                    _mouseWheel.X = mouseButton.Factor;
                    break;
            }

            return io.WantCaptureMouse;
        }

        if (inputEvent is InputEventMouseMotion)
        {
            return io.WantCaptureMouse;
        }

        if (inputEvent is InputEventKey key)
        {
            UpdateKeyModifiers(io);
            ImGuiKey imguiKey = ConvertKey(key.Keycode);
            if (imguiKey != ImGuiKey.None)
            {
                io.AddKeyEvent(imguiKey, key.Pressed);
            }

            if (key.Pressed && key.Unicode != 0)
            {
                io.AddInputCharacter((uint)key.Unicode);
            }

            return io.WantCaptureKeyboard || io.WantTextInput;
        }

        if (inputEvent is InputEventPanGesture panGesture)
        {
            _mouseWheel = new Vector2(-panGesture.Delta.X, -panGesture.Delta.Y);
            return io.WantCaptureMouse;
        }

        return false;
    }

    private static void UpdateKeyModifiers(ImGuiIOPtr io)
    {
        io.AddKeyEvent(ImGuiKey.ModCtrl, Godot.Input.IsKeyPressed(Key.Ctrl));
        io.AddKeyEvent(ImGuiKey.ModShift, Godot.Input.IsKeyPressed(Key.Shift));
        io.AddKeyEvent(ImGuiKey.ModAlt, Godot.Input.IsKeyPressed(Key.Alt));
        io.AddKeyEvent(ImGuiKey.ModSuper, Godot.Input.IsKeyPressed(Key.Meta));
    }

    private static DisplayServer.CursorShape ConvertCursorShape(ImGuiMouseCursor cursor) => cursor switch
    {
        ImGuiMouseCursor.Arrow => DisplayServer.CursorShape.Arrow,
        ImGuiMouseCursor.TextInput => DisplayServer.CursorShape.Ibeam,
        ImGuiMouseCursor.ResizeAll => DisplayServer.CursorShape.Move,
        ImGuiMouseCursor.ResizeNS => DisplayServer.CursorShape.Vsize,
        ImGuiMouseCursor.ResizeEW => DisplayServer.CursorShape.Hsize,
        ImGuiMouseCursor.ResizeNESW => DisplayServer.CursorShape.Bdiagsize,
        ImGuiMouseCursor.ResizeNWSE => DisplayServer.CursorShape.Fdiagsize,
        ImGuiMouseCursor.Hand => DisplayServer.CursorShape.PointingHand,
        ImGuiMouseCursor.NotAllowed => DisplayServer.CursorShape.Forbidden,
        _ => DisplayServer.CursorShape.Arrow,
    };

    private static ImGuiKey ConvertKey(Key key) => key switch
    {
        Key.Tab => ImGuiKey.Tab,
        Key.Left => ImGuiKey.LeftArrow,
        Key.Right => ImGuiKey.RightArrow,
        Key.Up => ImGuiKey.UpArrow,
        Key.Down => ImGuiKey.DownArrow,
        Key.Pageup => ImGuiKey.PageUp,
        Key.Pagedown => ImGuiKey.PageDown,
        Key.Home => ImGuiKey.Home,
        Key.End => ImGuiKey.End,
        Key.Insert => ImGuiKey.Insert,
        Key.Delete => ImGuiKey.Delete,
        Key.Backspace => ImGuiKey.Backspace,
        Key.Space => ImGuiKey.Space,
        Key.Enter => ImGuiKey.Enter,
        Key.Escape => ImGuiKey.Escape,
        Key.A => ImGuiKey.A,
        Key.C => ImGuiKey.C,
        Key.G => ImGuiKey.G,
        Key.R => ImGuiKey.R,
        Key.V => ImGuiKey.V,
        Key.X => ImGuiKey.X,
        Key.Y => ImGuiKey.Y,
        Key.Z => ImGuiKey.Z,
        Key.Key0 => ImGuiKey._0,
        Key.Key1 => ImGuiKey._1,
        Key.Key2 => ImGuiKey._2,
        Key.Key3 => ImGuiKey._3,
        Key.Key4 => ImGuiKey._4,
        Key.Key5 => ImGuiKey._5,
        Key.Key6 => ImGuiKey._6,
        Key.Key7 => ImGuiKey._7,
        Key.Key8 => ImGuiKey._8,
        Key.Key9 => ImGuiKey._9,
        Key.Kp0 => ImGuiKey.Keypad0,
        Key.Kp1 => ImGuiKey.Keypad1,
        Key.Kp2 => ImGuiKey.Keypad2,
        Key.Kp3 => ImGuiKey.Keypad3,
        Key.Kp4 => ImGuiKey.Keypad4,
        Key.Kp5 => ImGuiKey.Keypad5,
        Key.Kp6 => ImGuiKey.Keypad6,
        Key.Kp7 => ImGuiKey.Keypad7,
        Key.Kp8 => ImGuiKey.Keypad8,
        Key.Kp9 => ImGuiKey.Keypad9,
        Key.Period => ImGuiKey.Period,
        Key.KpPeriod => ImGuiKey.KeypadDecimal,
        Key.Minus => ImGuiKey.Minus,
        Key.KpSubtract => ImGuiKey.KeypadSubtract,
        Key.KpEnter => ImGuiKey.KeypadEnter,
        _ => ImGuiKey.None,
    };
}
