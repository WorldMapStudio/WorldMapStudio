using System;

namespace WorldMapStudio;

public sealed class Scoped : IDisposable
{
    private readonly Action _action;

    public Scoped(Action action)
    {
        _action = action;
    }

    public void Dispose()
    {
        _action?.Invoke();
    }
}
