namespace WorldMapStudio;

public interface IModalDialog<TContext>
{
    public ModalDialogState Draw(TContext context);
    public void OnClose() { }
}