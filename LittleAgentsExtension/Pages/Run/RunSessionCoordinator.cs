namespace LittleAgentsExtension;

internal sealed class RunSessionCoordinator
{
    private readonly object _gate = new();
    private ChatRunPage? _activePage;

    public void Activate(ChatRunPage page)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activePage, page))
            {
                return;
            }

            _activePage?.CancelActiveStreamForSupersededRun();
            _activePage = page;
        }
    }

    internal bool IsActive(ChatRunPage page)
    {
        lock (_gate)
        {
            return ReferenceEquals(_activePage, page);
        }
    }
}
