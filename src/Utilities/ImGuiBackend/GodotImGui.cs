using Godot;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WorldMapStudio;

public sealed partial class GodotImGui : Node
{
    private sealed partial class FrameNode : Node
    {
        private readonly GodotImGui _owner;

        public FrameNode(GodotImGui owner)
        {
            _owner = owner;
        }

        public override void _Ready()
        {
            Name = "GodotImGuiFrame";
            ProcessPriority = int.MinValue;
            ProcessMode = ProcessModeEnum.Always;
        }

        public override void _Process(double delta)
        {
            _owner.BeginFrame(delta);
        }
    }

    private static readonly IntPtr BackendName = Marshal.StringToCoTaskMemAnsi("godot_imgui_csharp");
    private static readonly IntPtr RendererName = Marshal.StringToCoTaskMemAnsi("godot_imgui_rd");
    private static readonly List<Action> PostRender = [];
    private readonly List<Action> _layouts = [];
    private ImGuiLayer _layer = null!;
    private ImGuiInput _input = null!;
    private ImGuiRenderer _renderer = null!;
    private readonly FontAtlasBuilder _fonts = new();
    private bool _frameBegun;
    private bool _initialized;

    public void AddLayout(Action layout)
    {
        _layouts.Add(layout);
    }

    /// <summary>Runs after this frame's ImGui draw has been recorded, before Godot renders viewports.</summary>
    public static void AfterRender(Action action)
    {
        PostRender.Add(action);
    }

    public override void _EnterTree()
    {
        if (IntPtr.Size != sizeof(ulong))
        {
            throw new PlatformNotSupportedException("ImGui requires 64-bit Godot for RID texture ids.");
        }

        if (DisplayServer.GetName() == "headless" || RenderingServer.GetRenderingDevice() is null)
        {
            GD.PushWarning("GodotImGui disabled because RenderingDevice is unavailable.");
            _initialized = false;
            SetProcess(false);
            SetProcessInput(false);
            return;
        }

        ImGui.SetCurrentContext(ImGui.CreateContext());

        ImGuiIOPtr io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        io.BackendFlags =
            ImGuiBackendFlags.HasSetMousePos |
            ImGuiBackendFlags.HasMouseCursors |
            ImGuiBackendFlags.RendererHasVtxOffset;

        unsafe
        {
            io.NativePtr->BackendPlatformName = (byte*)BackendName;
            io.NativePtr->BackendRendererName = (byte*)RendererName;
            io.NativePtr->IniFilename = null;
        }

        EditorStyle.Initialize();
        _fonts.RebuildIfNeeded(this, io, EditorStyle.Active);

        _renderer = new ImGuiRenderer();
        _input = new ImGuiInput(this);

        _layer = new ImGuiLayer(_renderer);
        AddChild(_layer);
        AddChild(new FrameNode(this));
        _initialized = true;
    }

    public override void _ExitTree()
    {
        _renderer?.Dispose();

        if (ImGui.GetCurrentContext() != IntPtr.Zero)
        {
            ImGui.DestroyContext();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!_initialized)
        {
            return;
        }

        if (_input.ProcessInput(@event))
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Notification(int what)
    {
        if (!_initialized || ImGui.GetCurrentContext() == IntPtr.Zero)
        {
            return;
        }

        switch (what)
        {
            case int value when value == MainLoop.NotificationApplicationFocusIn:
                ImGui.GetIO().AddFocusEvent(true);
                break;
            case int value when value == MainLoop.NotificationApplicationFocusOut:
                ImGui.GetIO().AddFocusEvent(false);
                break;
            case int value when value == MainLoop.NotificationOsImeUpdate:
                ImGui.GetIO().ClearInputKeys();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_initialized || !_frameBegun)
        {
            return;
        }

        foreach (Action layout in _layouts)
        {
            layout();
        }

        ImGui.Render();
        _renderer.Render(_layer.SubViewportRid, ImGui.GetDrawData());
        _frameBegun = false;

        foreach (Action action in PostRender)
        {
            action();
        }

        PostRender.Clear();
    }

    private void BeginFrame(double delta)
    {
        if (!_initialized)
        {
            return;
        }

        Vector2I size = _layer.UpdateViewport();
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new System.Numerics.Vector2(size.X, size.Y);
        io.DisplayFramebufferScale = System.Numerics.Vector2.One;
        io.DeltaTime = delta > 0.0 ? (float)delta : 1.0f / 60.0f;

        _input.Update(io);
        EditorStyle.ApplyPending(ImGui.GetStyle(), io, _fonts, this);
        ImGui.NewFrame();
        _frameBegun = true;
    }
}
