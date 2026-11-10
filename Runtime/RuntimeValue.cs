using System.Runtime.CompilerServices;
using Ravel.Runtime.Values;

namespace Ravel.Runtime;

public abstract record RuntimeValue
{
    public abstract ObjectVal Type { get; }

    internal string KindName => this is ModuleVal m ? $"模块 '{m.Label}'" : $"类型 '{Type.DisplayName}'";

    public bool IsClosure => this is FunctionVal and not (BoolVal or ClassVal);

    public virtual Scope MemberScope => ValueTables.GetValue(this, static v => new ValueTable(v));

    private static readonly ConditionalWeakTable<RuntimeValue, Scope> ValueTables = new();

    public virtual bool HasOwnMembers => false;
}

public enum ErrorKind
{
    Error,
    Type,
    Name,
    Attribute,
    Index,
    Key,
    ZeroDivision,
    Assert,
    Access,
    Argument,
    Value,
    Io,
    Regex,
}

public class RuntimeException(string message, ErrorKind kind = ErrorKind.Error) : Exception(message)
{
    public ErrorKind Kind { get; } = kind;

    public string? File { get; internal set; }
    public int Line { get; internal set; }
    public int Column { get; internal set; }

    public IReadOnlyList<string> Trace { get; internal set; } = [];

    internal bool Located => Line != 0;
}

public sealed class TypeMismatchException(string message) : RuntimeException(message, ErrorKind.Type);

public sealed class RejectedException(string message) : RuntimeException(message, ErrorKind.Type);

public class ExitException(string message) : Exception(message);

public sealed record SourceSpot(string? File, int Line, int Column);

public sealed class SyntaxException(string message, SourceSpot spot) : Exception(message)
{
    public SourceSpot Spot { get; } = spot;
}
