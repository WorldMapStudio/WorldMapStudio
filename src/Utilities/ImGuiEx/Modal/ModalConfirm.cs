using System;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class ModalConfirm
{
    private sealed class ConfirmOperation : IModalOperation<object?>
    {
        private readonly ModalConfirm _owner;

        public ConfirmOperation(ModalConfirm owner)
        {
            _owner = owner;
        }

        public ModalOperationState Draw(object? context)
        {
            ImGui.Text(_owner.Title);
            ImGui.Separator();
            ImGui.TextWrapped(_owner.Message);
            ImGui.Spacing();

            var state = ModalOperationState.Running;
            if (ImGui.Button(_owner.ConfirmText))
            {
                state = ModalOperationState.Confirmed;
            }

            ImGui.SameLine();

            if (ImGui.Button(_owner.CancelText))
            {
                state = ModalOperationState.Cancelled;
            }

            return state;
        }
    }

    private readonly ModalOperator<ConfirmOperation, object?> _operator;

    public ModalConfirm(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel")
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        _operator = new($"##ModalConfirm_{Guid.NewGuid():N}", () => new ConfirmOperation(this));
    }

    public ModalOperationState State => _operator.State;

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    public void Show()
    {
        _operator.Show();
    }

    public ModalOperationState Draw(bool canBeClosed, ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize)
    {
        ImGui.SetNextWindowSizeConstraints(
            new System.Numerics.Vector2(320, 0),
            new System.Numerics.Vector2(float.MaxValue, float.MaxValue)
        );
        return _operator.Draw(null, canBeClosed, flags);
    }
}
