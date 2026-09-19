using System;
using System.Numerics;
using ImGuiNET;

namespace WorldMapStudio;

public class ModalOperator<TOperation, TContext>
    where TOperation : class, IModalOperation<TContext>
{
    public ModalOperationState State { get; private set; }

    public TOperation? _item;
    private readonly string _id;
    private readonly Func<TOperation> _factory;
    private readonly Vector2? _minSize;
    private readonly Vector2? _initialSize;
    private readonly bool _resizable;
    private bool _openRequested;

    /// <param name="minSize">Floor for the window's size. With <paramref name="resizable"/> it's also
    /// the drag-resize lower bound; without it, the only thing <c>AlwaysAutoResize</c> leaves for it to
    /// do is set a minimum.</param>
    /// <param name="resizable">False (default) keeps the historical auto-fit-to-content, no-drag-resize
    /// popup. True drops <c>AlwaysAutoResize</c> so the user can drag the window's edges; the window
    /// opens at <paramref name="initialSize"/> (or <paramref name="minSize"/> if that's not given) and
    /// won't shrink below <paramref name="minSize"/>.</param>
    public ModalOperator(string id, Func<TOperation> factory, Vector2? minSize = null, bool resizable = false, Vector2? initialSize = null)
    {
        _id = id;
        _factory = factory;
        _minSize = minSize;
        _resizable = resizable;
        _initialSize = initialSize ?? minSize;
    }

    public void Show()
    {
        _item ??= _factory();
        _openRequested = true;
    }

    public ModalOperationState Draw(TContext context, bool canBeClosed, ImGuiWindowFlags flags)
    {
        if (_item == null)
            return ModalOperationState.Cancelled;

        if (_openRequested)
        {
            ImGui.OpenPopup($"##{_id}");
            _openRequested = false;
        }

        var state = ModalOperationState.Running;
        bool isOpen = true;

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (_minSize.HasValue)
        {
            ImGui.SetNextWindowSizeConstraints(_minSize.Value, new Vector2(float.MaxValue, float.MaxValue));
        }

        ImGuiWindowFlags windowFlags = flags | ImGuiWindowFlags.NoMove;
        if (_resizable)
        {
            if (_initialSize.HasValue)
            {
                ImGui.SetNextWindowSize(_initialSize.Value, ImGuiCond.FirstUseEver);
            }
        }
        else
        {
            windowFlags |= ImGuiWindowFlags.AlwaysAutoResize;
        }

        ImGuiEx.PopupModal($"##{_id}", canBeClosed, ref isOpen, windowFlags, () =>
        {
            state = _item.Draw(context);
        });

        if (state is ModalOperationState.Confirmed or ModalOperationState.Cancelled || !isOpen)
        {
            _item.OnClose();
            _item = null;
        }

        State = state;
        return state;
    }
}
