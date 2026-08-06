using System;

namespace WorldMapStudio;

public interface IRef<T>
{
    T Value { get; set; }
}

public sealed class Ref<T> : IRef<T>
{
    public T Value { get; set; }

    public Ref(T value)
    {
        Value = value;
    }
}
