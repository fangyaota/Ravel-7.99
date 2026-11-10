namespace Ravel.Runtime;

public sealed class RList<T>
{
    public static readonly RList<T> Empty = new();

    // Empty 之外每次 Add 都会给 Head/Tail 赋值,只有空表这两个字段无意义
    public readonly T Head = default!;
    public readonly RList<T> Tail = null!;
    public readonly int Count;

    private RList()
    {
        Count = 0;
    }

    private RList(T head, RList<T> tail)
    {
        Head = head;
        Tail = tail;
        Count = tail.Count + 1;
    }

    public RList<T> Add(T v) => new(v, this);

    public T At(int i)
    {
        var c = this;
        for (int k = 0; k < Count - 1 - i; k++) c = c.Tail;
        return c.Head;
    }

    public T Last => Head;
}
