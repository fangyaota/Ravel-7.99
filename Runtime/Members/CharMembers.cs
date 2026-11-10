namespace Ravel.Runtime;

using Ravel.Runtime.Values;

/// <summary>**`Char` 这个类给它的实例准备什么。**
///
/// 从前这几条夹在 `RegisterStringMethods` 里(用一个本地函数 `CharMethod` 一批批往
/// `Char` 上挂)—— 现在字符自己一个文件:`[ClassMethod]` 的名字是**写死的一个名字对一条方法**,
/// 不再有"名字当参数传进去"的那层循环,想知道 `Char` 有哪几条,扫一眼这个类就是。
///
/// 判类是**不管区域设置**的:`char.IsDigit` / `IsLetter` / … 问的是字符本身,
/// 和 `"abc".ToUpper` 那条同一个口径(同一段程序换台机器不该换答案)。
///
/// `ToString` 在这一份里**盖掉** `Object` 那条(子类先扫,见 <see cref="MemberSet.Scan"/>):
/// 字符的"打印出来"就是它自己那一个字。</summary>
internal static class CharMembers
{
    internal static readonly IMemberSet Set = MemberSet.Scan(typeof(CharMembers), ObjectMembers.Set);

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

    /// <summary>`int c` 的显式版:字符的码位。</summary>
    [ClassMethod("Code")]
    private static RuntimeValue Code(RuntimeValue self) => IntVal.Of(((CharVal)self).Value);

    /// <summary>盖掉 `Object.ToString`:字符打印出来是那一个字,不是 `Char(97)` 那种。</summary>
    [ClassMethod("ToString")]
    private static RuntimeValue ToString(RuntimeValue self) => new StringVal(((CharVal)self).Value.ToString());
}
