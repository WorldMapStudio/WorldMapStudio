using System;
using ImGuiNET;

namespace WorldMapStudio;

public sealed class ModalConfirm
{
    private sealed class ConfirmDialog : IModalDialog<object?>
    {
        private readonly ModalConfirm _owner;

        public ConfirmDialog(ModalConfirm owner)
        {
            _owner = owner;
        }

        public ModalDialogState Draw(object? context)
        {
            ImGui.Text(_owner.Title);
            ImGui.Separator();
            ImGui.TextWrapped(_owner.Message);
            ImGui.Spacing();

            var state = ModalDialogState.Running;
            if (ImGui.Button(_owner.ConfirmText))
            {
                state = ModalDialogState.Confirmed;
            }

            ImGui.SameLine();

            if (ImGui.Button(_owner.CancelText))
            {
                state = ModalDialogState.Cancelled;
            }

            return state;
        }
    }

    private readonly ModalDialogHost<ConfirmDialog, object?> _operator;

    public ModalConfirm(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel")
    {
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
        _operator = new($"##ModalConfirm_{Guid.NewGuid():N}", () => new ConfirmDialog(this));
    }

    public ModalDialogState State => _operator.State;

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    public void Show()
    {
        _operator.Show();
    }

    public ModalDialogState Draw(bool canBeClosed, ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize)
    {
        ImGui.SetNextWindowSizeConstraints(
            new System.Numerics.Vector2(320, 0),
            new System.Numerics.Vector2(float.MaxValue, float.MaxValue)
        );
        return _operator.Draw(null, canBeClosed, flags);
    }
}
