import io, re, sys
root = r"D:/Workspace/Ravel 7.99/"
def rd(p):
    with io.open(root+p, encoding="utf-8-sig") as f: return f.read()
def wr(p, s, bom=False):
    with io.open(root+p, "w", encoding="utf-8-sig" if bom else "utf-8", newline="") as f: f.write(s)

def sub(s, old, new, path, count=1):
    if s.count(old) < 1:
        sys.exit("MISSING in %s:\n%s" % (path, old[:200]))
    return s.replace(old, new, count)

# ---- A. Scope: Contains / Variables 变 virtual ----
p = "Runtime/Scope.cs"; s = rd(p)
s = sub(s, "public bool Contains(string name) =>", "public virtual bool Contains(string name) =>", p)
s = sub(s, "public IEnumerable<KeyValuePair<string, Variable>> Variables", "public virtual IEnumerable<KeyValuePair<string, Variable>> Variables", p)
wr(p, s, bom=True)

# ---- B. 助手改 internal ----
p = "Runtime/BuiltinClasses.Operators.cs"; s = rd(p)
for old, new in [
    ("private static T Operand<T>", "internal static T Operand<T>"),
    ("private static RuntimeValue IntOp(", "internal static RuntimeValue IntOp("),
    ("private static int ShiftCount(", "internal static int ShiftCount("),
    ("private static int ShiftUp(", "internal static int ShiftUp("),
    ("private static int ShiftDown(", "internal static int ShiftDown("),
    ("private static int RotateUp(", "internal static int RotateUp("),
    ("private static int RotateDown(", "internal static int RotateDown("),
    ("private static int NonZero(int d, string op)", "internal static int NonZero(int d, string op)"),
    ("private static RuntimeValue IntPow(int x, RuntimeValue b)", "internal static RuntimeValue IntPow(int x, RuntimeValue b)"),
]:
    s = sub(s, old, new, p)

# ---- C. 删掉 int 的注册 ----
start = s.index('        // int 运算符 —— 右操作数按"宽度"升级')
end = s.index('        // real 运算符 —— **全程 double**')
s = s[:start] + s[end:]

s = sub(s, '        DefineOp(Int, "**", (a, b) => IntPow(((IntVal)a).Value, b));\n', "", p)

s = sub(s, '        // int 位运算符\n'
           '        DefineOp(Int, "&", (a, b) => IntVal.Of(((IntVal)a).Value & Operand<IntVal>(b, "&").Value));\n'
           '        DefineOp(Int, "|", (a, b) => IntVal.Of(((IntVal)a).Value | Operand<IntVal>(b, "|").Value));\n'
           '        DefineOp(Int, "^", (a, b) => IntVal.Of(((IntVal)a).Value ^ Operand<IntVal>(b, "^").Value));\n',
           "", p)

s = sub(s, "foreach (var t in new[] { Int, Real, BigInt, Fraction, BigFraction })",
           "foreach (var t in new[] { Real, BigInt, Fraction, BigFraction })", p)
wr(p, s, bom=True)

# ---- D. 删 ToString ----
p = "Runtime/BuiltinClasses.Methods.cs"; s = rd(p)
s = sub(s, '        Int.DefineMethod("ToString", (s, _) => new StringVal(((IntVal)s).Value.ToString()));\n', "", p)
wr(p, s, bom=True)

# ---- E. IntVal 用自己的表 ----
p = "Runtime/Values/IntVal.cs"; s = rd(p)
s = sub(s, "    public override ObjectVal Type => BuiltinClasses.Int;",
           "    public override ObjectVal Type => BuiltinClasses.Int;\n\n"
           "    /// <summary>成员表是 <see cref=\"IntegerScope\"/> 那一张(建好就不再变,所有整数共用)。</summary>\n"
           "    public override Scope MemberScope => IntegerScope.Instance;", p)
wr(p, s, bom=True)
print("ok")
