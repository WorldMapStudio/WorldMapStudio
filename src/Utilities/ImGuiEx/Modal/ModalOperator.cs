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
    private bool _openRequested;

    public ModalOperator(string id, Func<TOperation> factory, Vector2? minSize = null)
    {
        _id = id;
        _factory = factory;
        _minSize = minSize;
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

        ImGuiEx.PopupModal($"##{_id}", canBeClosed, ref isOpen, flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove, () =>
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
