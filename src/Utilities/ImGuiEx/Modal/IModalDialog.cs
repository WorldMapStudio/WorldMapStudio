namespace WorldMapStudio;

public interface IModalOperation<TContext>
{
    public ModalOperationState Draw(TContext context);
    public void OnClose() { }
}