namespace Ravel.Runtime;

internal static partial class BuiltinClasses
{
    internal static ClassVal ErrorClass(ErrorKind kind) => kind switch
    {
        ErrorKind.Type => TypeError,
        ErrorKind.Name => NameError,
        ErrorKind.Attribute => AttributeError,
        ErrorKind.Index => IndexError,
        ErrorKind.Key => KeyError,
        ErrorKind.ZeroDivision => ZeroDivisionError,
        ErrorKind.Assert => AssertionError,
        ErrorKind.Access => AccessError,
        ErrorKind.Argument => ArgumentError,
        ErrorKind.Value => ValueError,
        ErrorKind.Io => IoError,
        ErrorKind.Regex => RegexError,
        _ => Exception,
    };

    internal static ObjectVal NewException(string message, ErrorKind kind = ErrorKind.Error)
    {
        var type = ErrorClass(kind);
        var obj = new ObjectVal(type, null);
        var scope = obj.Scope;
        if (type.ClassBody is { } body) scope.Reparent(body.CaptureScope);
        scope.Define(ObjectVal.ThisMember, obj);
        scope.Define(MessageMember, new StringVal(message));
        return obj;
    }
}
