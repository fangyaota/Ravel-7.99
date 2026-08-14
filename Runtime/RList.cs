namespace Ravel.Runtime;

/// <summary>持久化单链表(老 SingleLinkedList):Add 头插,共享尾,纯数据</summary>
public sealed class RList<T>
{
    public static readonly RList<T> Empty = new();
    public readonly T Head;
    public readonly RList<T> Tail;
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

    /// <summary>第 i 个(0 基,最早加入的在前);i 从尾部数时用 Head</summary>
    public T At(int i)
    {
        var c = this;
        for (int k = 0; k < Count - 1 - i; k++) c = c.Tail;
        return c.Head;
    }

    public T Last => Head;
    public bool IsEmpty => Count == 0;
}
