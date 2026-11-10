namespace Ravel.Runtime;

using Ravel.Runtime.Values;

internal static class CharMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(CharMembers), ObjectMembers.Set);

    [ClassCtor]
    private static RuntimeValue Init(RuntimeValue arg) => BuiltinClasses.CastToChar(arg);

    [ClassMethod("IsDigit")]
    private static RuntimeValue IsDigit(RuntimeValue self, RuntimeValue _) => new BoolVal(char.IsDigit(((CharVal)self).Value));

    [ClassMethod("IsLetter")]
    private static RuntimeValue IsLetter(RuntimeValue self, RuntimeValue _) => new BoolVal(char.IsLetter(((CharVal)self).Value));

    [ClassMethod("IsUpper")]
    private static RuntimeValue IsUpper(RuntimeValue self, RuntimeValue _) => new BoolVal(char.IsUpper(((CharVal)self).Value));

    [ClassMethod("IsLower")]
    private static RuntimeValue IsLower(RuntimeValue self, RuntimeValue _) => new BoolVal(char.IsLower(((CharVal)self).Value));

    [ClassMethod("IsSpace")]
    private static RuntimeValue IsSpace(RuntimeValue self, RuntimeValue _) => new BoolVal(char.IsWhiteSpace(((CharVal)self).Value));

    [ClassMethod("Code")]
    private static RuntimeValue Code(RuntimeValue self) => IntVal.Of(((CharVal)self).Value);

    [ClassMethod("ToString")]
    private static RuntimeValue ToString(RuntimeValue self) => new StringVal(((CharVal)self).Value.ToString());
}
