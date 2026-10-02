# Ravel 语言教程

## 一、起步

### 1.1 Hello World

```ravel
print "Hello, World!"
```

### 1.2 定义变量

```ravel
a := 42            # 类型推断，a 是 Integer
b: int = 100       # 显式类型标注
c = 200            # 赋值（变量必须已存在）
```

`:=` 是**定义**，`=` 是**赋值**。对未定义变量用 `=` 报错。

赋值是个**表达式**，交出去的就是赋进去的那个值（变量和字段一个样）：

```ravel
x := 1
print (x = 5)     # 5，而且 x 真的变成 5 了
print x           # 5
```

而变量的 `:=` 是**语句**，不能塞进表达式（`print (y := 6)` 报「变量的定义（':='）是语句」）。
成员定义 `a.def := 6` 倒是表达式（字段没有"语句"这一说）。

### 1.3 自动命名

```ravel
add ::= (x: int) => { x + 1; }
add.name   # "add"
```

`::=` 定义变量并把变量名自动设为 `FunctionVal.Name`。只对函数/类有效。

### 1.4 注释

`#` 到行尾。

### 1.5 打印和输入

```ravel
print "hello"              # 输出（带换行）
System.Write "no newline"  # 输出（不带换行）
name := input ()           # 读一行输入
```

`print` 是 `System.WriteLine` 的别名。

---

## 二、类型系统

### 2.1 内置类型

| 类型 | 说明 | 字面量 |
|------|------|--------|
| Integer | 整数 | `42` `-1` `7i` |
| Float | 浮点 | `3.14` `5.0` `2f` `Inf` `NaN` |
| Bool | 布尔 | `true` `false` |
| String | 字符串 | `"hello"` |
| Char | 字符 | `'a'` |
| List | 列表 | `[1 2 3]` |
| Set | 集合 | `{1 2 3}` |
| Dict | 字典 | `{"a":1 "b":2}` |
| Range | 区间 | `[1..3]` `(3..5)` `[1..5)` `(1..5]` |
| Void | 空 | `()` |

### 2.2 类型名

```ravel
int.name         # "Integer"
string.name      # "String"
typeof 42        # Integer
typeof "hello"   # String
```

类型名是 PascalCase，小写是别名。`System.Integer` 是权威名。

**成员（方法/字段）一律 PascalCase**：`obj.Fields ()`、`f.Scope ()`、`Math.Sin x`、`Ex.Throw e`。
两处例外，都是有意留的小写：

- **全局别名** —— `print`、`true`、`if`、`typeof`、`assert`…… 它们是 `predefined.rav` 里的名字，
  更像"语言的关键词"而不是谁的成员。
- **机制成员** —— `parent` / `block` / `name` / `init` / `this`：类对象自己那层的数据。
  `C.parent` 是原型链指针（父类），`C.name` 是类名。它们**不沿链继承**，所以实例上读不到
  （`c.parent` 报「没有方法」——不然 `(5).parent` 会从报错变成返回 `ValueType`）。
  `parent` / `block` 是**只读**的（改它等于把类换一个：`C.parent = int` 之后 `C ()`
  就去跑 `Integer` 的构造器）；`name` 故意可写，那是它的用法（`F.name = "x"`）。

**注解里的名字就是作用域里的那个变量**（`int := System.Integer` 只是 `predefined.rav`
里的普通赋值），所以：

```ravel
C ::= class { init = (n: int) => { n; } }
string := C            # 遮蔽掉内置名
z: string = 5          # ← 这里的 string 指的是 C
```

注解本身是个**表达式**，求值在定义处（变量）或创建处（参数）做一次：

```ravel
pick := (flag: bool) => { if { flag; } { int; } { float; } }
a: (pick true) = 5      # 注解算出 Integer
b: (pick false) = 5     # 算出 Float
f := (x: (pick true)) => { x; }   # 参数注解同理
```

写法两种：**一个名字**（可带 `.` 成员访问），或**括号里的任意表达式**。
括号是必需的，不是可选 —— 不带括号的 `f ()` 会和「下一个参数/字段」撞上
（`(x: int y: int)` 里的 `int y` 会被当成一次调用）。所以 `x: System.List` 仍然写不了，
要在括号里：`x: (System.List)`。

### 2.3 类型反射

```ravel
int.parent             # ValueType  — 父类型
int <: ValueType       # true       — 子类型检查
int <: string          # false
object :> int          # true       — 反过来问(父类型)
int.Subtypes ()        # [Every]  — 所有子类型(Integer 没有自己的子类)
int.Default ()         # 0           — 默认值
```

值的类型判定用 **`is` / `isnot`** 运算符（下面 3.3 有）——`1 is int`。
两者分工：`<:` / `:>` **两边都得是类型**，`is` 左边是**值**。

想整棵树一起看就用库里的 `Types.PrintTree`（`using "types.rav"`，要显式引用）：

```ravel
Types.PrintTree object
# Object [Type]
# ├── ValueType [Type]
# │   ├── Integer [Type]
# │   └── …
# └── Ravel [Type]
```

方括号里是那个类型的 **typeof**（创建者）：类对象显 `Type`，你自己类的实例显它那个类。

### 2.4 类型转换

```ravel
int "42"       # 42
string 100     # "100"
float 3        # 3
bool default   # false
```

`bool` 只认 bool 和 `default`——数字**没有**到 bool 的转换，`bool 0` 会报「无法转换为 bool」。

**标注位置上的隐式转换和显式转换是同一张表**：`x: int = <别的类型>` 走的就是
`int <别的类型>`，转得动就转、转不动才报「类型不匹配」。

```ravel
x: int = 3.9            # 3（截断）
y: bigint = "123"       # 123
z: int = fraction 7 2   # 3
n: int = true           # 1
bad: int = NaN          # 报「NaN 不能转换为 int」
```

### 2.5 空值

```ravel
()             # Void 类型唯一值
default        # Every 类型唯一值
int default    # 0
string default # ""
bool default   # false
list default   # []
```

`default` 也认注解：`x: int = default` 拿到的是 **0**（不是一个万能占位）—— 按注解变成本类型
的那个空值，`n: int = default` 之后 `n + 1` 照常算。各类型的空值：数值 `0`、字符串 `""`、
`bool` 假、容器空表、`function` 空函数、`Continuation` 是"还没到手的那一枚"（见 4.5）。

**三条处理"没有"的糖**（`?.` / `??` / `??=`）。这里的「空」**只有 `None` 一样** ——
`()` 不是空：它是 Void 类型那个**值**，和 `0` / `""` / `[]` 平起平坐（`Some ()` 更不是）。
要"没设过"就明写 `None`：`Cache: Option = None`。

```ravel
d ?? "默认"          # None 就给后备；**惰性**，后备只在真要用时才求值
u?.Name ()           # 空就短路；有值就拿着值走完**整条链**（含调用）
x ??= 算一次 ()      # 空才写（缓存那种写法）
```

三条的语义都在库里（`lib/predefined.rav` 的 `IsNothing` / `NilChain` / `NilOr` / `NilFill`），
引擎只管脱糖 —— 和 `if` / `while` / `foreach` 一个路子。细看：

- **`a ?? b`**：`None` 给 `b`，`Some x` 给 **`x`**（解包），别的值原样（`()` 也照交 ——
  它不是空）。右边惰性。
- **`a?.b c`**：`None` **原样**短路；`Some x` 拿着 `x`
  把后面整条链走完，**再包回去**（`u?.Name ()` 还是 Option，一路 `?.` 形状不变）；
  普通对象照常取成员，不包。最后用 `??` 或 `Value ()` 收口：
  ```ravel
  print (u?.Name () ?? "无名")     # u 是 None → "无名"；有 u → 名字
  ```
- **`x ??= v`**：空才写，**不空一个字符都不碰**（成员那条连 setter 都不调）；
  交回写完之后的值。`0` / `""` / `[]` / `()` 都是**值**，不算空 —— 想"没设过才写"就用
  `Cache: Option = None` 打底。
- `?.` 和 `.` 一个位置：**只能在 `.` 能出现的地方写**（primary 之后、或 `|>` 之后）——
  `.成员` 比并列的调用绑得紧，`f ().g ()` 本来就要写 `f () |> .g ()`（见「调用比运算符松」
  那一节），`f ()?.g ()` 同理。

**用例见 tests/258。**

### 2.6 类型层次

```
Object (parent = 自身)
├── ValueType → Integer Float String BigInt Fraction BigFraction Range   (并列)
├── Function → Bool  Block  Type
├── List  Set  Dict
├── Void  Exception  Ravel(模块)  Scope  Property
├── Any (顶类型, parent = 自身, 不在 Object 子树里)
└── Every (底类型 = default 的类, parent = 自身, 不在 Object 子树里)
```

**没有 `Class` 类型**：`class` 就是 `type` 的别名（`predefined.rav` 里 `class := System.Type`），
两者是同一个值，所以 `typeof Person`（用户类）和 `typeof int`（内置类型）都是 `Type`。
用户类也是同一个表示（`Runtime/Values/ClassVal.cs`，见「七、类」），
它的父类是 `Object`、元类是 `Type`。

`Any` 和 `Every` 画在最后只是排版方便——它们的父类型是**自己**，不是 `Object`
（`object.Subtypes ()` 里没有它们）。权威快照见 `examples/type_tree.rav`。

`Bool` 挂在 `Function` 下是有意的：`true`/`false` 可以像函数一样调用，
收两个代码块、返回选中那个的结果。于是 `if` 就是 `c {t} {e}`——见「四、控制流」。

### 2.7 BigInt / Fraction / BigFraction

```ravel
bigint 12345678901234567890   # 构造器:把值转成 BigInt
fraction 3 4          # 3/4
bigfraction 123 456   # 大数分数
```

**数字后缀**能在字面量上直接写类型，省掉那层构造器：

```ravel
42n        # BigInt（放不放得下 int 都走大整数）
42i        # Integer（装不下 int 当场报错，**不悄悄升级**成 bigint）
2f / 2.5f  # Float（`2f` 这种没小数点的也能强制成浮点）
```

**没写后缀时按文本猜**：有小数的形状就是 float —— 所以 **`2.0` 是 float，不是 int**，
哪怕小数位是 0；整数装得下 `int` 就是 `int`，装不下（`2147483648`）退化成 `bigint`。
`2.5i` / `2.5n` 报错：小数没有"整数后缀"这回事。

后缀**要吞得干净才算**：后面再粘着标识符字符就不是后缀，所以 `0if`、`2not` 照旧是
"数字 + 标识符"（`2n` 后面跟 `)` `.` `[` 空格才算数）。

---

## 三、运算符

### 3.1 算术

```ravel
1 + 2     # 3
5 - 3     # 2
4 * 2     # 8
10 / 3    # 3  (整数除法)
10.0 / 3  # 3.333...
7 % 3     # 1
```

**`%` 取的是"截断余数"**（和 C#/C 一样，和 Python 不一样）—— 被除数是负的时候尤其要留意：

```ravel
-7 % 3     # -1  (Python 会给 2)
7 % -3     # 1
```

一个口径贯穿所有数值类型：`a % b = a - b * trunc(a / b)`（`trunc` 朝零截断）。
`int` / `bigint` / `float` / **分数**（`fraction` / `bigfraction`）都有；除数为零和 `/` 报同一句话。

### 3.2 比较

```ravel
1 == 2    # false
1 != 2    # true
3 < 5     # true
5 > 3     # true
3 <= 3    # true
```

整数和浮点可以混用：`3 == 3.0` → `true`。

相等性的口径：**原子值比值，对象按身份**（类对象、函数、容器都在"对象"这一边）。

```ravel
() == ()        # true —— () 是单例
default == default  # true
[1] == [1]      # false —— 两个不同的列表
one := [1]
one == one      # true
```

一边是对象一边不是就报「不支持操作数」（`[1] == ()`、`C == 5`）——
和别的运算符遇到对不上的操作数一个口径，不静默给 `false`。

特殊浮点值 `NaN` / `Inf`（`-Inf` 就是 `-Inf`，一元 `-` 对浮点取负）按 IEEE 来：
**`NaN` 不等于自己**（`NaN == NaN` 是 `false`），`Inf` 参与运算照常（`1 / Inf` → `0`，
`Inf - Inf` → `NaN`）。它们打印成 `NaN` / `Inf` / `-Inf`（ASCII，不是 `∞`），
`int Inf` 这类越界转换报的是「数值 Inf 超出 int 范围」。

### 3.3 类型判定 is / isnot

```ravel
1 is int          # true
1 is float        # false（Integer 和 Float 是兄弟，不是子类型）
1 is object       # true（Integer <: ValueType <: Object）
1 isnot int       # false
```

判据就是类型树上那条 `A <: B`。`is` / `isnot` 是**词形运算符**，所以和 `+` 一样
三种写法都成立：

```ravel
1.is              # 等右操作数（和 2.+ 对称）
is.int            # 等左操作数（和 +.2 对称）→ 等价于 (_ is int)
isnot.string "a"  # false
```

类对象自己也是值——它是 `type` 的实例，所以 `int is type` 是 `true`，
而 `typeof 1 is int` 是 `false`（`typeof 1` 求出来的是类对象 `Integer`，不是 int 值）。

**类型之间**的关系用 `<:` / `:>`（两边都得是类型对象）：

```ravel
int <: object        # true   —— A 是不是 B 的子类型
object :> int        # true   —— A 是不是 B 的父类型，方向反过来
int <: int           # true   —— 自反
Every <: int         # true   —— 底类型是所有类型的子类
1 <: int             # 报错：'<:' 的左边得是个类型，得到 Integer 的实例
```

和 `is` 的分工：`is` 左边是**值**（`1 is int`），这一对两边都得是**类型**。
写法上它和别的运算符一样，节也认：`int.<: object` / `object.:>`。

### 3.4 逻辑

```ravel
true && false    # false  (短路与)
true || false    # true   (短路或)
!true            # false
```

`&&` `||` 是短路特殊结构，**不可重载**。

### 3.5 位/逻辑运算

```ravel
3 & 1    # 1    (位与 / 逻辑与)
3 | 1    # 3    (位或 / 逻辑或 / 函数交替)
3 ^ 1    # 2    (位异或 / 逻辑异或)
```

`&` `|` `^` 可重载。

**移位**（也在这一族里：看的是那 32 个格子，不检查数值溢出）：

```ravel
1 << 4          # 16     左移：高位丢出去就没了
1 << 31         # -2147483648   （位型就是 0x80000000）
1 << 32         # 0       （全丢光了）
(0 - 8) >> 1    # -4     >> 是**算术**右移，符号留住 —— 就是"除以 2"
5 >>> 1         # -2147483646   >>> 是**循环**右移：那个 1 转到最上面去了
(0 - 1) <<< 1   # -1     （全 1 转完还是全 1）
```

优先级**照 C 的脾气**：比加减低、比比较高 —— `1 + 2 << 3` 是 `(1 + 2) << 3`、
`2 << 1 == 4` 是 `(2 << 1) == 4`。移位量得是**非负的 int**（负数当场报错）。
`bigint` 没有固定宽度，`<<` `>>` 照常（移多少有多少），循环移位对它**不成立**（明确报错）。

**`>>>` 是循环右移，不是"无符号右移"** —— 要逻辑右移（补零）用 `6.15` 的 `Bits.Shr`。

### 3.6 字符串拼接与转义

```ravel
"hello " + "world"   # "hello world"
"ab" * 3   # 报错！字符串不支持 *
```

字符串里的转义：`\"` `\\` `\n` `\t` `\r`。不认识的转义**连同反斜杠原样保留**，
所以 Windows 路径不用双写：

```ravel
print ("say \"hi\"")        # say "hi"
print ("line1\nline2")      # 分两行
print ("c:\path\file")      # c:\path\file —— \p \f 不认识，原样留着
```

字符串可以跨行（直接换行即可），不需要续行符。

### 3.6.1 原始字符串（`"""…"""`）

上面那串转义在**正则**、**Windows 路径**这种文本上很碍事 —— 全是反斜杠，得一个个双写。
那种文本用**原始字符串**：连着三个引号开头、连着三个引号收尾，**里面一个字符都不动**。

```ravel
print """C:\path\nope"""      # C:\path\nope —— `\n` 不变换行
print """say "hi" 吧"""       # say "hi" 吧 —— 单个引号不用转义
print """a # 不是注释"""      # a # 不是注释 —— `#` 也是普通字符
```

- **不插值**。要 `${…}` 请用普通字符串（`$` 那套只认普通字符串）。
- **内容里写不了 `"""`** —— 碰到连着三个引号就收尾了。真要那三个字符，拿普通字符串拼。
- **空白按 C# 那套**：开引号后面直接换行时，那个换行不算内容；再按**收尾那三个引号
  所在行**的缩进，给每行剥掉同样多 —— 所以块能跟着代码正常缩进：

```ravel
sql := """
    select *
    from users
    """
print (sql)                   # select *\nfrom users
```

收尾引号那行缩进 4 格，于是每行都剥 4 格。**某一行缩进写浅了会当场报错**（而它又不是
一个空行）—— 这是护栏：不报的话结果是悄悄多出几个空格，肉眼看不出来。

**用例见 tests/287。**

### 3.7 比较 Bool / String

```ravel
true == true    # true
"a" != "b"      # true
```

Bool 只支持 `==` `!=`；String 支持 `==` `!=`，另外还有 `+`（拼接，见 3.6）。

### 3.8 复合赋值

```ravel
x := 10
x += 5     # x = 15
x -= 3     # x = 12
x *= 2     # x = 24
x /= 4     # x = 6
x %= 4     # x = 2
```

### 3.9 成员运算符访问与运算符节

```ravel
1.+     # 返回函数: (rhs) => 1 + rhs   —— 右操作数留空
true.^  # 返回函数: (rhs) => true ^ rhs
+.2     # 返回函数: (x) => x + 2       —— 左操作数留空
```

`+.2` 和 `(_ + 2)` 是同一个东西，只是省掉下划线。对称地，`2.+` 是等右操作数、
`+.2` 是等左操作数。

运算符节右边的表达式**只吃一个 primary**（含成员访问），复杂式要自己加括号：

```ravel
add2 := +.2
print (add2 5)          # 7
print ((+.(2 * 3)) 10)  # 16
print ((+.1) 41)        # 42 —— 括号界定了节的范围
```

### 3.10 给类定义运算符

类体里**直接用符号**定义，符号本身就是成员名：

```ravel
Vec ::= class {
    init = () => { x = 0; }
    x: int = 0
    + := (o: Vec) => {
        r := Vec ()
        r.x = x + o.x
        r
    }
    == := (o: Vec) => { x == o.x; }
}
a := Vec ()
a.x = 1
b := Vec ()
b.x = 2
c := a + b
print (c.x)      # 3
print (a == b)   # false
```

- `+ := f` —— **定义**这个类的运算符
- `+ = f` —— **覆盖**从父类层继承来的那个（父类自己不受影响）

一个类体里没写过的符号就用不了（`a * 2` 会报「类型 class 不支持运算符 '*'」）。
一元 `!`、短路 `&&`/`||` 是求值器特判的，**不能**自定义。

---

### 3.7 调用比运算符**松**（实参吃到运算符为止）

并列的调用（`f a b`）比运算符**松** —— 调用"抓住"它右边那一整条算式：

```ravel
print 1 + 2                 # 3 —— 是 print (1 + 2)，不是 (print 1) + 2
print "a" + "b"             # ab
inc ::= (n: int) => { n + 1; }
inc 1 * 5                   # 6 —— inc (1 * 5)
add 1 2 * 3                 # 柯里化照旧：((add 1) (2 * 3))
```

**没有例外** —— 括号开头的实参也一样被吃：

| 写法 | 读作 |
|---|---|
| `f 1 + 2` | `f (1 + 2)` |
| `f (1) + 2` | `f ((1) + 2)` |
| `f x.y + 1` | `f (x.y + 1)` |
| `f x is int` | `f (x is int)`（词形运算符一样被吃）|
| `xs.Count () + 1` | `xs.Count (() + 1)` —— 数完再加一要写 `(xs.Count ()) + 1` |
| `xs.At (0) + 1` | `xs.At ((0) + 1)` —— 取第 0 个要写 `(xs.At (0)) + 1` |

规矩只有一条：**运算符作用在调用结果上，就自己加括号**。库和用例里常写成
`(x.Count ()) == 0` / `(typeof x) == int`，就是这条的产物。

**转换器式调用**（`string x` / `bigint n` / `typeof x`）后面还要接东西时，同样要把实参括起来：

```ravel
print ("共 " + string (n) + " 个")     # 不括的话 string 会把 `+ " 个"` 一起吃掉
print ((typeof x) == int)
```

`<|` 与 `|>`（5.5）是把这件事写明白的语法糖：`f <| a + b` 和 `f a + b` 是一回事；
`x.f () |> .g ()` 是"先算调用，再取成员"。**用例见 tests/01。**

## 四、控制流

### 4.1 if

```ravel
if { x > 0; } {
    print "positive";
} {
    print "non-positive";
}
```

`if` 接受三个块：条件、then、else（块需要 `;` 或换行分隔）。

它其实是**库函数**，不是内建——`Bool` 挂在 `Function` 下，`true`/`false` 本身就能选块：

```ravel
true  { print "then"; } { print "else"; }      # then
false { print "then"; } { print "else"; }      # else
(1 < 2) { "yes"; } { "no"; }                   # yes —— 比较结果直接当函数调
```

所以 `if { c; } { t; } { e; }` 等价于 `c { t; } { e; }`。**热路径上写后者** ——
前者要多进一次 `if`、多造一个块:循环里每轮判一次的话,量的数差约 **30%**
（`lib/predefined.rav` 的 `while`、`lib/iterator.rav` 里"每个元素判一次"那几处都写的直调版）。
一个类里也常见 `init = (cond) => { ... }` 这种把条件当值传递的写法。

条件**不是 bool** 时两者报的错不一样:`if` 会先用注解收一遍(`cond : bool = c ()`),
报「类型不匹配: 无法将 X 赋值给 Bool」;直调则是「值 X 不是函数，不能调用」。

### 4.2 while

```ravel
i := 0
while { i < 5; } {
    print i;
    i += 1;
}
```

### 4.3 foreach

```ravel
foreach [1 2 3] (x: int) => { print x; }
```

`foreach` 也是库函数（`predefined.rav` 里用 `while` 写的）。它吃的是 **`IEnumerable`** ——
这是个用 `interface` 写出来的库级接口，形状**照 C#**：

```ravel
IEnumerable ::= interface IMonad { by GetEnumerator : function = default; … }
IEnumerator ::= interface IEnumerable {
    by MoveNext : function = default
    by Current  : object   = default
    by GetEnumerator = property (() => { () => { instance; }; }) (…)   # 自己就是自己的枚举器
}
```

它体内还有**一整套默认实现**（`Count` / `Map` / `Where` / `Take` / `First` / `Fold` …），
所以凡是实现了它的东西都白拿整套方法（见本节末尾），而且它**也是 monad**（见 5.9）
—— 父接口写的是 `IMonad`。

`GetEnumerator ()` 交回一个枚举器，枚举器 `MoveNext ()` 往前走一步（返回还有没有）、
`Current` 是当前那个。`foreach` 展开就是这个循环：

```ravel
e := xs.GetEnumerator ()
while { e.MoveNext (); } { f (e.Current); }
```

三种容器都实现了它（见 6.4 与 7.11），所以：

```ravel
foreach {1 2 3}  (x: int) => { print x; }     # set 也行
foreach {"a": 1} (v: int) => { print v; }    # 字典迭代的是**值**
foreach 5 (x: int) => { print x; }
# foreach 需要 IEnumerable（能按顺序交出一串的东西：list / set / dict / string / Generator / 枚举器 / Option…），得到 Integer
```

**每次进来都新开一个枚举器**，所以嵌套遍历同一串值互不打扰（和 C# 一样）。

枚举器**自己也是一串**（`IEnumerator <: IEnumerable`，见下面的小节）：它交回的枚举器就是它自己
—— 像 C# 里 `yield` 生成的那个类一样。所以 `Enumerator`、`Generator` 的光标、用户自己写的游标
都能直接 `foreach`、也白拿整套方法。**两条语义**（单遍的东西本来就这样）：遍历的是
**还没走完的那一段**，而且走一遍就把它**消耗掉**了：

```ravel
e := Enumerator [1 2 3]
e.Count ()        # 3
e.Count ()        # 0 —— 上一句把它走完了

c := (Generator (y: function) => { y 1; y 2; y 3; }).GetEnumerator ()
Seqs.Gather (c.Take 2)   # [1 2] —— 先看两个
c.ToList ()              # [3]   —— 剩下的照旧
```

#### 实现了它，就白拿一整套

`IEnumerable` 的**类体**里写着那套方法的默认实现（接口的类体会在每个实现对象上跑一遍，
所以"实现体不填就用它"）：

```ravel
G := Generator (y: function) => { y 1; y 2; y 3; }
G.Count ()      # 3
G.Sum ()        # 6
G.Join "-"      # 1-2-3
G.Max ()        # 3
G.Fold 0 (acc: int x: int) => { acc + x; }   # 6
```

**分界线**：交回**一串**的**惰性**（交回 `Generator`），要**一个值 / 容器**的当场算：

| 惰性（一串进、一串出） | 急切（要值 / 容器） |
|---|---|
| `Map` `Where` `Bind` `Take` `Skip` `Concat` `Distinct` `Reverse` `SortBy` | `Count` `IsEmpty` `First` `Last` `At` `Contains` `Sum` `Min` `Max` `Join` `ToList` `ToSet` `ToGenerator` `Each` `All` `Any` `Find` `Fold` `TryFind` `TryFirst` |

无限流上只有惰性那批用得起来：

```ravel
Nats := Generator (y: function) => { n := 0; while { true; } { y n; n += 1 } }
Seqs.Gather (Nats.Take 4)                       # [0 1 2 3]
doubled := Nats.Map ((n: int) => { n * 2; })
Seqs.Gather (doubled.Take 3)                    # [0 2 4]
```

容器上那几个**同名方法**是引擎给的（也在类链上、先命中），行为一样、只是**急切**：
`[1 2 3].Map f` 交回 `list`，而 `G.Map f` 交回 `Generator`。要转就 `ToList ()` /
`ToGenerator ()`。

自己的类只要交回一个枚举器（库里的 `Enumerator` 拿来就能用）也能进 `foreach`，顺带白拿整套：

```ravel
use (IEnumerable MyThing {
    by GetEnumerator = property (() => { () => { Enumerator [1 2 3]; }; }) ((v: function) => { (); })
})
```

#### 自己造一串：`Generator`

不想写枚举器，就把"往外送值"的那段代码交给 `Generator` —— 体的参数 `y` 是投喂口：
**`y v` 送出一个值并挂起**，消费者再要下一个才接着跑（`lib/generator.rav`）：

```ravel
Nats := Generator (y: function) => {
    n := 0
    while { true; } { y n; n += 1 }      # 无限流也写得出来
}

callcc (stop: function) => {             # 要几个拿几个（早期退出，见 4.5）
    i := 0
    foreach Nats (x: int) => {
        if { i >= 5; } { stop (); } { 0; }
        print x                          # 0 1 2 3 4
        i += 1
    }
}
```

- **惰性**：算到哪儿送到哪儿，没要走的部分一句都不跑（写几句 `print` 在 `y` 之间就能看出来）。
- `Generator f` 本身就是个 `IEnumerable`，`foreach` 直接吃；`is IEnumerable` 对它成立。
- **每次 `foreach` 都从头跑一遍体**（体是配方，和类体一个规矩）：同一个 `Generator` 遍历两次，
  两次各自从头。要"一次性"就在外面用变量兜住。
- 能 `foreach` 的东西都能包成生成器（`Generator (y) => { foreach xs (x) => { y x; } }`），
  生成器也能套生成器做扁平化。

### 4.4 match（按条件挑一支）

`match v cases dflt` 从头试每一对 `[条件 结果]`，第一对**条件为真**的交出它的结果；一对都不真就给 `dflt`。
条件是个**谓词**（拿值、回 bool），而 `is.int` 这种**运算符节**就是现成的那个：

```ravel
kind := (v: object) => {
    match v [
        [is.int    {"整数";}]
        [is.bool   {"布尔";}]
        [is.string {"字符串";}]
    ] {"别的";}
}
kind 3        # 整数
kind true     # 布尔
kind 1.5      # 别的
```

- **结果和默认值都得是「可调用的东西」—— 推荐写成块**：只有命中那一支（或默认那支）会跑，
  和 `if { 条件 } { 这 } { 那 }` 一个规矩。块的值 = 最后一条语句的值，所以分支里想干什么都行
  （`{ 记一笔 (); 7; }`）；`() => { … }` 这种 lambda 也认（它同样可调用）。
- 拿**普通值**糊弄会当场报错（「match 第 1 对的结果要写成块」）—— 不悄悄当成"进 `match` 之前
  就算好的值"：那会让人以为分支是延迟的，其实全都跑了。
- 想**比值**也这么写：`[<.0 {"负";}]` / `[==.0 {"零";}]`——节就是"左操作数留空"的函数。
- 条件是普通函数也行：`[[IsLong {"长";}]]`，`IsLong` 是 `(s: string) => bool`。
- **条件按顺序试、第一个为真的胜出**，后面的不再问（短路）。
- 每对必须**正好两项**，不是就当场报错（说清是第几对）。

**用例见 tests/245。**

### 4.5 早期退出

```ravel
callcc (exit: function) => {
    i := 0
    while { true; } {
        if { i >= 5; } { exit i; } { 0; }
        i += 1
    }
}
# → 5
```

调用续延会**丢弃当前帧链**、把值当作 `callcc` 表达式的返回值从捕获点继续，
所以它是逃出多层嵌套的办法。

拿到的续延**是它自己的类型** —— `Continuation`（`Continuation <: function`）：

```ravel
esc: Continuation = default          # 起手是"还没到手的那一枚"
print (typeof esc)                   # Continuation
callcc (k: function) => { esc = k; "先给个值"; }
print (typeof esc)                   # Continuation
print (esc is Continuation)          # true
print (esc is function)              # true —— 它照样是函数(能调、能存进字段、能当参数传)
```

- `is function` 照旧成立（续延的父类就是 `function`）；**要和普通函数区分开的地方用
  `is Continuation`**。注意 `typeof k == function` 在续延上**不再成立**（它现在报
  `Continuation`）—— 那种写法要改成 `k is function`。
- `default` 那枚是**哨兵**：照样能存、能传，一调**当场报错**（"这枚续延是 default（还没到手
  的那一枚），调不了"）。做成"什么都不做"是不行的 —— 那会静默地把控制权留在原地，调用方
  还以为跳走了；`lib/generator.rav` 里"还没起跑"那条路正需要它响。
- `Continuation f` 是把一枚函数**当成**续延（调它 = 调那枚函数）—— 库里的 `callcc` 就是
  用它把"先还原控制状态、再跳回去"那层也包成续延的，所以上面那枚类型上就是 `Continuation`。

同一个机制反过来用就是循环——`while` 本身就是这么写的：

```ravel
while := (c: function body: function) => {
    again := (x: int) => { x; }
    callcc (k: function) => { again = k; }
    cond := c ()      # 必须在恢复点之后,否则 again 0 跳回来时不重求,条件只判一次
    assert (typeof cond == bool) ("while 的条件必须是 bool，得到 " + string (typeof cond))
    if { cond; } { body (); again 0; } { 0; }
}
```

注意续延调用是「从捕获点继续」，**callcc 之后的语句会被重新执行**：

```ravel
saved := (x: int) => { x; }
n := 0
n = 1 + callcc (k: function) => { saved = k; 0; }
print n                                      # 1
if { n < 10; } { saved 10; } { 0; }          # 跳回捕获点:n = 11,然后重新往下走
                                             # → 又打印一次 n(11),这次条件不成立,结束
# 输出:1 然后 11
```

**同一枚**续延反复调用不是"一次产出多个值"：它会不断从捕获点重来，所以调用点本身也在被重跑的
代码里时，要像上面那样加个守卫，否则会一直转下去。不过**两枚**续延来回跳就能写生成器 ——
每吐一个值就把控制权交给对方，而且两边每一轮都重新捕获自己的续延（见 tests/40）。

**谁跟着续延走、谁不跟。** 续延还原的是**控制状态**：帧链，外加两样容易忘的——

- `Ex.HandlerStack`：拿外层续延从 `try` 体里逃出去，那个 handler **跟着走掉**（不会留下一个
  "僵尸"去接后面没人接的错误）；反过来，调一个在 `try` 体里捕获的续延，那个 try 的 handler
  也回来，重进的那一段再抛错照样被它接住（见 tests/40）；
- 模块加载栈：从模块体里逃出去，**这次加载算没发生**，之后还能正常 `using` 那个模块（tests/226）。

这两件事是**库里的 `callcc` 做的**：它在把续延交给用户之前先拍两份快照（自己的 handler 栈，
外加引擎的模块加载状态 —— 后者引擎给了 `System.LoadingState` / `System.RestoreLoading` 两个原语），
用户拿到的续延被调用时先把这两份还原、再跳。所以：**直接用 `System.CallCC` 就绕过了这层保护** ——
`callcc` 才是入口。

**不跟**的是普通那点数据：列表、字段、对象上的改动都是原地改的，恢复不会回滚它们。
所以往 `Map` 的回调里塞一个续延、事后再调，已经攒了一半的结果会接着往上长（tests/40 最后一节）——
这是"只还原控制状态"的代价，不是 bug。

---

## 五、函数

### 5.1 单参数

```ravel
double := (x: int) => { x * 2; }
double 5    # 10
```

**注解可以省**：`(x) => …` 就是 `(x: object) => …`（谁都收得下）。只知道"有东西来了"
的地方不必硬写 —— 回调尤其常见：

```ravel
show := (x) => { print x; }
[[1 2] [3 4]].Map ((p) => { (p.At 0) + (p.At 1); })   # [3 7]
```

省了注解就**不在调用时挑类型**了 —— 要挑就写注解，或者在体里自己过一手（`n: int = x`）。
两种写法可以混在同一个参数表里（`(a b: int)`）。

`()` 空参数：`() => { 42; }`，调用：`f ()`。

**圆括号 / 方括号里的换行不算数**：没闭合的 `(` / `[` 里，换行不当语句结束 —— 表和长参数
列表因此能写好看（`Lexer` 里拿一个括号栈盯着）。花括号**不在**这条里：块就是靠换行分语句的。

```ravel
kind := (v: object) => {
    match v [
        [is.int    {"整数";}]
        [is.string {"字符串";}]
    ] {"别的";}
}
```

**单行的块要用 `;` 收尾。** 没换行时 `{ x }` 是集合、`{ a: 1 }` 是字典，
`;`（和换行一样都是 Newline token）才是「这是块」的标记。
这条规则不看位置，lambda 体同样适用——`=> { x * 2 }` 是**错的**，
`=> { x * 2; }` 才对。多行块自然不用，因为已经有换行了。

### 5.2 多参数（柯里化）

```ravel
add := (x: int y: int) => { x + y; }
add 3 4          # 7

# 部分应用
add3 := add 3    # (y: int) => { 3 + y; }
add3 4           # 7
```

语法：`(x: T1 y: T2) => { body }`。

### 5.3 _ 占位符

```ravel
inc := _ + 1              # (x: object) => x + 1
both := _ + _             # (x: object) => (y: object) => x + y
is_pos := _ > 0           # (x: object) => x > 0
to_int := int _           # (x: object) => int x
```

每个 `_` 一个参数，从左到右编号。`_` 只是一个洞，脱糖后就是普通函数调用——
所以 `_` 填进去的位置必须真的能吃这个参数：`print _ "!"` 会变成「先 `print x`、
再拿它的返回值当函数用 `"!"`」，调用时报「值 () 不是函数」。

### 5.4 丢弃

```ravel
_ := 42          # 求值但不绑定
_ = f ()         # 调函数丢弃结果
_ : int = 99     # 类型标注丢弃
```

### 5.5 向函数喂参（`<|`）

```ravel
double ::= (x: int) => { x * 2; }
double <| 5    # 10 —— 等价于 double 5
```

`<|` 和 F#/Haskell 那个 `<|` 一样是**函数在左**：`f <| x` ≡ `f x`，右结合。
它**最低优先级**（在 `ParsePipe` 那一层），所以右边整条算式都算它的实参 ——
`f <| a + b` 不用写成 `f (a + b)`。

它和 `|>` 是一对：**`<|` 封右边、`|>` 封左边**。并列调用是「左嵌套」（`f a b` ≡ `(f a) b`），
这两个各封一头：

```ravel
inc ::= (n: int) => { n + 1; }
inc 1 * 5              # inc (1 * 5) —— 实参吃到运算符为止（3.7）
(inc 1) * 5            # 想先算调用，自己加括号
inc <| 1 * 5           # inc (1 * 5) —— <| 把**右边**整个封成一个实参
wrap <| inc <| 1 + 2   # wrap (inc (1 + 2))

xs.Count () |> .ToString ()   # (xs.Count ()).ToString () —— |> 把**左边**封口
```

**`<|` 还能喂给一个"柯里化了一半"的调用** —— 实参吃到运算符为止，但**吃不掉 `<|`**
（这是实参那条规矩唯一的例外）：

```ravel
add ::= (a: int) => { (b: int) => { a + b; }; }
add 1 <| 7             # (add 1) 7 —— 拿整串调用的**结果**当函数
```

（调用比运算符松、实参吃到运算符为止 —— 那是 3.7 讲的另一条；这几条一起决定了
"哪儿要括号"。）

**`$` 不是运算符** —— 它只出现在字符串里（插值 `${…}`）。想在代码里封右边，写 `<|`。

`|>` 非有不可的理由：`.成员` 比并列的调用绑得紧，所以 `xs.Count ().ToString ()` 会被读成
`xs.Count ((().ToString ()))`（见「常见陷阱」）。`a |> b`（后面不是 `.成员` 时）就是显式的
「到这儿为止」——`add 1 |> 2` 和 `add 1 2` 是一回事。

### 5.6 函数名与打印

```ravel
add ::= (x: int) => { x + 1; }
add.name         # "add"
add.name = "sum" # 改名
```

**打印一个函数打出来的是它的代码**（函数值没有可显示的标量），`::=` 命名的带上名字：

```ravel
print add                 # <function add (x: int) => { x + 1; }>
print (add 3)             # 已经是普通值 4
add2 := (x: int y: int) => { x + y; }
print add2                # <function (x: int y: int) => { x + y; }>
print (add2 3)            # <function (y: int) => { x + y; } applied x=3>
```

签名里是**还等着**的参数，已经喂过的实参跟在 `applied` 后面 —— 两者合起来就是完整的形状。
体太长会截断（它不是给你复制代码用的，是给你认出"这是哪个函数"的）。

### 5.7 缓存函数的返回结果（Cached）

`Cached count f` 把 `f` 的返回结果记下来，同一组实参再来就直接给记下的那个 —— 不再重算。
`count` 是 **`f` 的参数个数**。

最典型的用法是给递归函数自己套上缓存（函数体里读的是外层那个名字，调用时才解析，
所以把缓存版的名字给它，递归的每一层就都走缓存了）：

```ravel
FibCalls := 0
fib: object = 0
fib = Cached 1 ((n: int) => { FibCalls += 1; if { n < 2; } { n; } { fib (n - 1) + fib (n - 2); } })
print (fib 25)          # 75025
print FibCalls          # 26 —— 没缓存是 242785 次
```

（`fib: object = 0` 那行的标注别省：`:=` 会把类型钉在初值的类型上，而这里得先有个名字
让它能被递归引用、再换成缓存版。）

基本用法（注释标出哪几次是命中）：

```ravel
Calls := 0
slow := (x: int) => { Calls += 1; x * 10; }
c := Cached 1 slow
print (c 1)          # 10   算
print (c 1)          # 10   命中
print (c 2)          # 20   另一个实参,算
print Calls          # 2

add := (a: int b: int) => { a + b; }
c2 := Cached 2 add   # 两个参数
print (c2 1 2)       # 3
print (c2 1 2)       # 3(整组实参命中)
```

几条规矩：

- **喂够 `count` 个才算一次完整调用**。柯里化喂到一半的（`c2 1`）不进缓存，
  它拿到的还是个普通半成品；继续喂到 2 个才查缓存。
- 实参**逐个**用 `==` 认，而且**先比类型**：原子值比值（`1` 与 `1` 命中，
  `1` 与 `"1"` 是两个键），对象/函数/容器按身份（同一个对象再来才命中）。类型不同的
  两项是两个键 —— 数值运算符碰到字符串会当场报错，所以这一步不能省。
- **没有上限，也不过期**：这就是记忆化。实参取值很多时它会一直涨，
  要限容得在外面自己包一层。
- 参数个数写成 0 当场报错（`Cached 的参数个数至少是 1，得到 0`），
  不然那个函数会一直收参数、永远不给结果。无参函数写 `Cached 1`，调用时喂 `()`。

**它是个函数**（`lib/cached.rav` 里 `readonly Cached ::= (count f) => …`）——
  直接交出包装函数，不是"要实例化的类型"，也没有小写别名：一个东西一个名字。
缓存本身是 `Cached` 体里的两个 list —— `keys`（算过哪几组实参）与 `vals`（各得了什么结果），
包装函数捕获它们，所以活得和包装函数一样长。

交出来的是**包装函数**：

```ravel
print (typeof (Cached 1 slow))   # Function —— 拿到手就能直接调
print (Cached.name)              # "Cached"
```

所以 `Cached 2 add` 可以直接 `(…) 1 2`（`Cached` 自己是个 `(count f) => …` 的函数，
不是要实例化的类型）。缓存本身是它捕获的那两个 list，所以活得和包装函数一样长。
每次 `Cached …` 都是一份**独立**的缓存，包同一个函数互不干扰。

### 5.8 Option（可能没有值的包）

`Some 5` 包着一个值，`None` 什么都没有。要问有没有值用 `IsSome ()` —— 两者**类型相同**
（都是 `Option`），`is Option` 分不出来：

```ravel
(Some 5).IsSome ()     # true
None.IsSome ()         # false
(Some 5).Value ()      # 5 —— 解包
None.Value ()          # 报错：Option.Value: 这个 Option 里没有值（…）
None.ValueOr 0         # 0 —— 解包，没有值就给默认
```

链式操作用 `Map` / `Bind` / `Where`，它们**对 `None` 一律原样放过** ——
所以中间落空一次，后面全不用算：

```ravel
safeDiv := (a: int b: int) => { if { b == 0; } { None; } { Some (a / b); } }
ok := (Some 100).Bind (safeDiv 100)                      # Some 1
print ((ok.Bind (safeDiv 10)).Value ())                  # 10
print ((((Some 100).Bind (safeDiv 0)).Bind (safeDiv 10)).IsSome ())   # false —— 两次除法一次都没算
```

| 方法 | 做什么 |
|------|--------|
| `IsSome ()` / `IsNone ()` | 有没有值 |
| `Value ()` | 解包。`None` 上当场报错（不拿 `()` 糊弄）|
| `ValueOr d` | 解包，没有值就给 `d` |
| `OrElse m` | 没有值就换成 `m` |
| `Map f` | 有值就包上 `f` 的结果；`None` 上 `f` 根本不会被调 |
| `Bind f` | 有值就把 `f` 的结果**摊平**接上（`f` 自己得回一个 `Option`），链起来不套娃 |
| `Where p` | 不满足 `p` 就变成 `None` |
| `Exists p` | 有值且满足 `p` |
| `ToList ()` | 往**序列**那半走：有值就是 `[x]`，没值就是 `[]`（见下）|

`None` 是**一个值**（单例，大家共用它），不用写 `None ()`；`(Some 5).Where (…)` 落空拿到的
就是它，所以 `== None` 成立（对象按身份比）。

**`Option` 本身就是一串 —— 0 个或 1 个**（`impl (IEnumerable Option …)`，见 4.3）：

```ravel
(Some 5) is IEnumerable     # true
foreach (Some 7) (x: int) => { print x; }   # 走一次
foreach None (x: int) => { print x; }       # 一次都不走
Seqs.Flatten [(Some 1) (None) (Some 3)]     # [1 3]
(Some 5).Count ()           # 1
(None).ToList ()            # []
```

整串方法（`ToList` / `Count` / `First` / `Fold` / `Take` …）由 `IEnumerable` 的
**默认实现**给。但 `Map` / `Bind` / `Where` 还是**上面那三条**（类链上的先命中）——
它们在这族的语义是"包着 / 摊平"，`(Some 5).Map f` 交回的仍是 `Option`，
`do { n :< Some 5; Some (n * 2); }` 也仍在 `Option` 里。

**括号提醒**（柯里化那条规则的正常结果：实参只吃「主表达式 + 取成员」）：

```ravel
(Some 5).Map f              # 这对括号不能省：Some 5.Map f 是 Some (5.Map f)
xs.Count () |> .ToString ()  # 链式调用也是：`.成员` 比并列的调用绑得紧，
                            # `xs.Count ().ToString ()` 会被读成 `xs.Count ((().ToString ()))`
((Some 5).Map f).Value ()   # Map 的结果也要套括号，否则 .Value 会贴到 f 上
```

#### Expected（可能出错的那半）

`Expected` 是 `Option` 的兄弟：形状相同（都实现 `IMonad`，`do { … }` 也吃得下），
区别只在"没有"的那半 —— 那边是**没有值**（`None`），这边是**一句报错**（`Err e`，`e` 是个
`Exception`），所以多一条 `Error ()`。

主要用途是**给一次调用兜底**：`Expect argCount f` 把函数包起来，喂满 `argCount` 口才真调用，
成功包成 `Ok v`、报错包成 `Err e` —— 报错不往外抛，调用方可以先看再决定：

```ravel
add := (a: int b: int) => { a + b; }
safeAdd := Expect 2 add

(safeAdd 1 2).Value ()        # 3
(safeAdd 1 "x").IsErr ()      # true —— 类型错误被接住，没打穿程序
(safeAdd 1 "x").Message ()    # 参数 'b' 需要 Integer，得到 String
```

- 两个构造子：`Ok v`（正常）与 `Err e`（出错）；问哪一半用 `IsOk ()` / `IsErr ()`。
- 解包：`Value ()` 在 `Err` 上**当场报错**（用错就问一句），要默认值用 `ValueOr d`；
  反过来 `Error ()` 拿那句报错、`Message ()` 拿它的文本。
- `Map` / `Bind` 和 `Option` 一个规矩：`Err` 一律原样传下去，里面的函数根本不会被调。
- `argCount` 数的是**喂几口**：`f := () => { … }` 要 `Expect 1 f`（那一口是 `()`），
  `(a b) => …` 要 `Expect 2 f`。给错元数不会静默 —— 那也是一条 `Err`。

**用例见 tests/244。**

### 5.9 do 块

一串 `Bind` 写起来很别扭（嵌套的括号、还要给 lambda 标参数类型），所以有 `do`：

```ravel
do {
    x :< Some 3
    y :< Some 4
    Some (x + y)
}
```

`:<` 读作「从这个 Monad 里取出值，给它绑个名字」。整块等价于：

```ravel
(Some 3).Bind ((x: object) => { (Some 4).Bind ((y: object) => { Some (x + y); }); })
```

几条规矩：

- **最后一条语句的值就是整块的值**（通常是 `Some …`），它落在最内层那个 lambda 体里；
  以 `:<` 收尾会报错（那样整块没有值）。
- 中间**任意一条落空，后面全不算** —— 这正是 `Bind` 的行为，`do` 只是把它写直白了。
- 块里可以有**普通语句**（定义、赋值…），它们落在同一个 lambda 体里，还能改绑来的名字。
  单行写要用 `;` 收尾，和普通块一样。
- 绑定出来的名字标 `object`（最宽的那个 —— 这里对拿到什么一无所知）；
  想要具体类型就在块里自己过一手：`n: int = x`。
- `do` 是个**普通表达式**：当参数、当返回值、跟 `Map` 串都行。
- `do` 只是语法糖，**运行时不为它添任何东西**，所以打印一个含 `do` 的函数会看到脱糖后的
  `Bind` 链。

`:<` 是词法里的**一个** token。挑这个形状（而不是 do-notation 常见的 `<-`）是有意的：
`<-` 一进 token 表，`x < -1` 那类写法就再也切不出词了（不管中间有没有空格）。
而「`:` 紧跟 `<`」在合法代码里只可能是这个绑定（注解和字典值都不会以 `<` 开头），
所以 `x < -1` 一点不受影响。

```ravel
print (do { v :< Some 10; Some (v * 2); }.Value ())    # 20
```

#### 序列也是 monad：`do` 块就是列表推导

`IEnumerable` 也是 `IMonad` 这一族的（`IEnumerable ::= interface IMonad`，见 4.3）：`Map` 是
逐个映射、`Bind` 是「每个元素交回一串、接起来」（flatMap）。于是 `do { … }` 在序列上
就是**列表推导**：

```ravel
pairs := do {
    x :< [1 2]
    y :< [10 20]
    [x y];
}
Seqs.Gather pairs        # [1 10 1 20 2 10 2 20] —— 笛卡尔积
```

`[1 2] is IMonad` / `"ab" is IMonad` / `G is IMonad` 都成立（凡是实现了 `IEnumerable` 的都算）。
最后一条语句得交回**一串**（`Bind` 要的就是"每个元素变一串"），单层时那正是 `Map`：

```ravel
Seqs.Gather (do { x :< [1 2 3]; [x * x]; })   # [1 4 9]
```

**两边都可以是无限的**（`Bind` 交回的是惰性的一串），只要下游 `Take` 得住：

```ravel
Nats := Generator (y: function) => { n := 0; while { true; } { y n; n += 1 } }
sums := do { a :< Nats; b :< Nats; [a + b]; }
Seqs.Gather (sums.Take 5)     # [0 1 2 3 4]
```

和 `Option` 那半本来就通着 —— **`Option` 自己就是"0 个或 1 个的一串"**
（`Seqs.Flatten [(Some 1) (None)]` → `[1]`，见 5.8），反方向则用
`xs.TryFirst ()` / `xs.TryFind p`（给你一个 `Option`，不用去 `try` 里接 `First` / `Find` 那句错）。

### 5.10 IO Monad（把效果做成值）

`IoMonad.PutStrLn "hi"` **什么都不打印** —— 它交出的是一份**说明书**（一个 `IoMonad.Action`）。
直到 `Perform ()` 那一刻，效果才真的发生：

```ravel
using "iomonad.rav"

Hello := IoMonad.PutStrLn "hello"
print "还没跑"
Hello.Perform ()        # 打印发生在这
print "跑完了"
```

```
还没跑
hello
跑完了
```

说明书可以传、可以拼、可以放着不跑，也可以跑第二遍（`Perform ()` 再来一次就是再来一遍）。
于是「要做什么」和「做」分成两件事 —— `do { … }` 拼出来的那一长串，在 `Perform ()`
之前一个字符都还没输出：

```ravel
Main := do {
    name :< IoMonad.GetLine
    _ :< IoMonad.PutStrLn ("你好 " + name)
    IoMonad.PutStrLn "再见"
}
print "还没跑"
Main.Perform ()
```

这就是 IO Monad：`Action` 是说明书的类型，`Bind` 把两份说明书订成一份，所以 `:<` 直接能用。
注意「Monad」在这里是**形状**（有 `Bind` / `Map`）而不是某个类型 —— `Action` 和 5.8 那个
`Option` 是**同一个形状的两个实例**：两者都实现 `IMonad`（`monad.rav` 里那个接口，两条槽就是
`Map` / `Bind`），所以 `x is IMonad`、注解 `(m: IMonad)`、`IMonad.GetImplementors ()` 都认它们；
而各自的 `Bind` 各干各的 —— 这边真跑效果，那边没有值就短路。

| 名字 | 做什么 |
|------|--------|
| `IoMonad.Action f` | 造一份说明书（`f` 收 `()`、交出一个值，**在 `Perform` 时才被调用**）|
| `.Perform ()` | 跑它。效果世界的边界 |
| `.Map f` / `.Bind f` | 结果过一道纯函数 / 接上另一份说明书（`Bind` 摊平）|
| `IoMonad.Return v` | 不做效果，只交出 `v` |
| `IoMonad.PutStrLn s` / `IoMonad.PutStr` | 打印（带 / 不带换行）|
| `IoMonad.GetLine` | 读一行。它是**一个值**，每次 `Perform` 都真读一次 |
| `IoMonad.Foreach xs f` | 对表里每个元素造一份说明书并依次执行 |
| `.Then next` | 接着做下一份，交回**后一份**的结果（两份互相不知道）|
| `.Discard ()` | 只跑，结果丢掉 |
| `.Attempt ()` | 跑它：成功 `Some 结果`、失败 `None`（只问"成没成"）|
| `.Catch handler` | 出错了换一份说明书接着做（`handler` 收那句错误，交回一份 `Action`）|
| `IoMonad.Sequence xs` | 一串说明书 → 一份，交出**结果的表** |
| `IoMonad.When c body` / `Unless c body` | 条件到**那一刻**再算（所以 `c` 是个函数），不成立就交出 `()` |
| `IoMonad.PutStrLnErr s` / `PutStrErr s` | 写到**标准错误** |

IO 里**没有「落空」这回事**：每一步都跑，值一路往下传（和 `Option` 的短路正好相反）。
`.Perform` 要贴给调用的**结果**时，用 `|>`（5.5）或者括号：

```ravel
IoMonad.Foreach [1 2 3] ((x: int) => { IoMonad.PutStrLn (string x); }) |> .Perform ()
(IoMonad.Foreach [1 2 3] ((x: int) => { IoMonad.PutStrLn (string x); })).Perform ()   # 一样
r := (IoMonad.Return 20).Map ((x: int) => { x + 1; })    # 或者先绑个名字
print (r.Perform ())                                     # 21
```

拼和接错：

```ravel
(IoMonad.PutStrLn "一").Then (IoMonad.PutStrLn "二") |> .Perform ()   # 先一后二

risky := IoMonad.Action (() => { 1 / 0; })
print (risky.Attempt () |> .Perform () |> .IsSome ())                  # false
(risky.Catch ((e: Exception) => { IoMonad.PutStrLn ("接住 " + string e); })).Perform ()
```

---

### 5.11 文件系统（`lib/io.rav`）

**要显式引用**：`using "io.rav"` 之后 `Io.xxx` 才是一个名字。

```ravel
using "io.rav"

d := Io.Dir "notes"
d.Create ()                       # 建目录（已有就不管）
f := d.Child "a.txt"              # 子条目：不存在就当一个文件（写新文件是常事）
f.Write "第一行
第二行
"
print (f.Read ())                 # 整个文件一个字符串
print (f.Lines ())                # 按行切开
f.Append "尾巴"
print (f.Size ())                 # 字节数
print ((d.List ()).Keys ())       # 列目录：名字 → 条目，按名字排序
print ((d.Files ()).Keys ())      # 只要文件（`Dirs ()` 只要子目录）

d.Mkdir "sub"                     # 建子目录（`Child` 对不存在的名字按文件算）
(f.CopyTo "notes/b.txt").Read ()  # 复制 / 移动 / 改名：`MoveTo` / `Rename`
f.Delete ()                       # 删文件；`Dir.Delete ()` 对**非空**目录会报错（不递归删）
```

**报错**是中文、带路径的 `Exception`：读不存在的文件、对目录 `Read`、删非空目录、父目录不在就写……
`Exists ()` 是**问**，不报错：

```ravel
try { (Io.File "notes/nope.txt").Read (); } (e: Exception) => { print (string e); }
# 读文件: 找不到文件 —— notes/nope.txt
```

**路径**基准是进程当前目录（`Io.Current ()` 看，`Io.ChDir p` 改）；没有沙箱 —— 能跑这段代码的人，
本来就能读写那些文件。路径的活交给 `Io.Join` / `Io.DirName` / `Io.BaseName` / `Io.Ext`。

#### "文件"是个接口

`IEntry` / `IFile` / `IDir` 三个接口（**全局名**）才是本体：磁盘上的、内存里的、zip 条目、远程的，
都只是它的实现。**实现子接口也就实现了父接口**：`IFile ::= interface IEntry`。

```ravel
# 对着接口写，两边都吃
describe := (f: IFile) => { f.Name () + " = " + f.Read (); }
```

**库里已经有了四种**（第四种见 6.16 那个 `Http.Url`）：磁盘的 `Io.File` / `Io.Dir`、
控制台的 `Terminal.Stdout` / `Terminal.Stderr` / `Terminal.Stdin`、**内存里的** `Io.MemFile` / `Io.MemDir`、
**一棵 JSON 树** `Io.JsonNode`（`Io.JsonFile "conf.json"` 从文件读一棵出来）：

```ravel
m := Io.MemFile "mem.txt" "内存里的内容"     # 内容就是一个字符串
Io.CopyTo m (Io.File "a.txt")                 # 内存 → 磁盘，同一段代码

t := Io.JsonFile "conf.json"                 # 一棵 JSON = 一个目录树
(t.Child "ver").Read ()                      # "2"（节点内容是它的 JSON 文本）
(t.Child "ver").Write "3"                    # 当 JSON 解析，换掉那棵子树
Io.EachDir t (p: string e: object) => { print p; }   # 把配置树当目录走一遍
Io.CopyTo t (Io.File "conf.json")              # 攒好了落盘
```

想加一个自己的实现，就把那几条成员写出来、登记一下：

```ravel
MemFile ::= class {
    Label: string = ""
    Text: string = ""
    init = (label: string text: string) => { Label = label; Text = text; this; }
    Name := () => { Label; }
    Exists := () => { true; }
    IsDir := () => { false; }
    Read := () => { Text; }
    Write := (t: string) => { Text = t; (); }
    Append := (t: string) => { Text = Text + t; (); }
    Size := () => { Text.Length (); }
    Delete := () => { Text = ""; (); }
}
impl (IFile MemFile {
    ()
})

print (MemFile.GetImplements ())       # [IFile IEntry]
Io.CopyTo (Io.File "notes/b.txt") (MemFile "m" "")   # 磁盘 → 内存，同一段代码
```

`Io.CopyTo` / `Io.EachDir` / `Io.Lines` 就是**对着接口写的**三段：任何实现都吃。
`Io.Lines f` 是个**生成器**（见 4.3 那节）：边要边给，`foreach (Io.Lines f) (l: string) => { … }`。
（zip 条目那种只读的实现，让 `Write` / `Delete` 抛一句"这份文件是只读的"就行。）

**一个 URL 也是文件**（要 `using "http.rav"`，见 6.16）：`Http.Url "https://…/a.txt"`
交回的东西就有那一套成员，所以上面这三段对它照样能用 ——
`Io.CopyTo (Http.Url "…") (Io.File "a.txt")` 抓下来存着、`Io.Lines (Http.Url "…")` 一行行读远程日志。
这就是"对着接口写"的意思：加一种实现，用它的代码不用改。

#### 控制台也是文件

`Terminal.Stdout` / `Terminal.Stderr` / `Terminal.Stdin` 三个值都实现了 `IFile` —— 抽象那一层现成的用例：

```ravel
Terminal.Stdout.Write "直接写到终端
"
Io.CopyTo (Io.File "notes/a.txt") Terminal.Stdout      # 对着接口写的代码:文件 → 终端
Terminal.Stdin.ReadLine ()                           # 一行(就是 input)
foreach (Io.Lines Terminal.Stdin) (l: string) => { print l; }   # 读到 EOF 的每一行（生成器，边要边给）
```

只写的那两个读不了、只读的那个写不了，报错说人话（控制台也没有大小、删不掉）：

```ravel
try { Terminal.Stdout.Read (); } (e: Exception) => { print (string e); }
# 这是只写的（stdout），读不了
```

`Terminal.Stdin.Read ()` 是**读到 EOF**（终端上要 Ctrl+Z / Ctrl+D 收），所以测试里别去碰它。
日常还是用 `print` / `input`；这两个值存在的意义是"用同一段代码对付文件和终端"。

#### 什么时候值得用 `IoMonad` 包一层

`lib/io.rav` 那几条是**直接做**的：`f.Read ()` 那一刻就读了。想要"先拼好一串要做的效果、
之后再 `Perform ()`"（比如日志、事务），用它的 **Action 版本** —— 做的是同一件事，只是先攒着：

```ravel
f := Io.File "notes/a.txt"
job := Io.ReadAction f |> .Map ((t: string) => { t.Length (); })      # 到这儿什么都没读
print (job.Perform ())

(Io.WriteAction f "hi").Then (Io.ReadAction f) |> .Perform ()         # 先写后读
Io.EachLineAction f ((l: string) => { print ("行 " + l); }) |> .Perform ()
```

（`using "io.rav"` 会顺手把 `iomonad.rav` 拉进来 —— 这几条要用它。反过来不行：
`iomonad.rav` 由 predefined 加载，那时候 `IFile` 还不存在。）

### 5.12 比较与排序（`IComparable` / `Sorting`）

比大小的协议是 **`CompareTo`**：收对方、交回一个 int —— 负数=我小、0=一样、正数=我大
（和 C# 的 `IComparable` 同约定）。

**每个值都有这条**：引擎在根类 `Object` 上放了一条（体就是内建那把尺子，也就是 `<` 的口径），
所以内建的那些拿起来就能用：

```ravel
3.CompareTo 5          # -1
"a".CompareTo "b"      # -1
```

自己写的类型，那把尺子比不了（它只认数值和字符串），就在**类里写一条**盖掉它 ——
和 `ToString` 同一个规矩 —— 再登记一下"我这个类型讲了怎么比":

```ravel
Rec ::= class {
    K: int = 0
    N: string = ""
    CompareTo := (o: Rec) => { if { K < o.K; } { -1; } { if { K > o.K; } { 1; } { 0; } } }
}
impl (IComparable Rec { () })     # 类型层面的事实:x is IComparable / 注解都认
```

⚠️ 这里**不能用** `by CompareTo = property …` 现装：这个名字 `Object` 上已经有了，
成员查找是**类链先说话**，接口那条槽还没轮到（`IFile` 那种"类里本来没有的名字"才能现装）。

排东西用 `Sorting`：

| 写法 | 意思 |
|---|---|
| `Sorting.Compare a b` | 谁大谁小（就是 `a.CompareTo b`）|
| `Sorting.Sort xs` | 排一个表，**稳定** |
| `Sorting.SortBy xs key` | 按 `key` 算出来的键排，键相等时保持原来的先后 |
| `Sorting.Max xs` / `Min xs` | 最大 / 最小（空表报错）|
| `Sorting.MaxBy xs key` / `MinBy xs key` | 按 key 挑 |

```ravel
Sorting.Sort [3 1 2]                       # [1 2 3]
Sorting.SortBy ["bb" "a" "ccc"] ((s: string) => { s.Length (); })    # [a bb ccc]
Sorting.Max rs                             # 实现了 IComparable 的对象照样排
```

两点说明：

- 引擎另有一套 `xs.Sort ()` / `xs.Min ()` / `xs.Max ()`（见「六、集合」），走的是 `<` 那把尺子 ——
  **只认内建标量**，碰见对象只会说「比不了 Rec 与 Rec」。库里这套按 `CompareTo` 比，
  两套并存：内建那些两边结果一样，自己的类型只有库里这条路。
- 空表 `Max` / `Min` **报错**（和 `First` / `Last` 一个规矩：「没有最大」和「最大的是空」
  不该长得一样）。

**用例见 tests/217。**

### 5.13 键与查找（`IKey` / `Keys` / `Keyed`）

字典的键**只能是值类型**（数、字符串），而且比的就是值自己：

```ravel
d := {}
d.Set 1 "int"
d.Set "1" "str"       # 和 1 是两个格子
d.Set 1.0 "float"     # 和 1 也是两个格子
d.Keys ()             # [1 1 1] —— 打印出来都像 1，但确实是三个（打印是给眼睛看的）
```

字面量的键是**表达式**：`{"a": 1}`（字符串要写引号），`{1: "x"}`、`{k: v}` 也成；
`{a: 1}` 里的 `a` 是**变量**，没定义就报「未定义的变量 'a'」。

**值语义的键：`IKey` + `IDict`。** 协议是 `Key ()` —— 交回"我当键时等于什么"（一个值类型）。
每个值都有默认那条（数、字符串交回**自己**，别的类型当场报错说「在类里写一条 `Key`」），
自己写的类型在类里写一条盖掉它，再登记一下：

```ravel
Rec ::= class {
    X: int = 0
    Key := () => { X; }        # 我当键时 = 我的 X
}
impl (IKey Rec { () })

d := {}
d.Set (Rec 1 "甲") "one"       # 先把 Rec 规范成 1，再进表
d.Get (Rec 1 "别的")           # "one" —— 另一个对象、X 一样 → 同一条
d.SysGet (Rec 1 "甲")          # 报「dict.SysGet 的键得是值类型」—— 引擎那层不认对象
```

**为什么分两层**：`d.Get` / `d.Set` / `d.Has` / `d.Remove` / `d.GetOr` 是库里
**注入**给 `dict` 的（`lib/keys.rav` 里 `IDict` 那五条接口槽），它们先 `Of` 一道把键规范成
值类型、再转到引擎那套 `SysGet` / `SysSet` / … 上。引擎那层做不到这件事不是设计口味，是
**够不着**：它是同步的 C#，而 `Key ()` 是 Ravel 函数；更要紧的是查表那一步发生在 .NET 字典
的内部，那儿根本插不进帧。你自己写代码时敲的永远是 `d.Set` 那一层。

`Keys.Of k` 是"规范键"的公开名字（`k.Key ()` 外加一道"交回的得是值类型"的确认）。

**要把原对象拿回来，用 `Keyed`。** 表里存的是规范键，所以 `d.Keys ()` 交回 `1` 而不是那个
`Rec`；`Keyed` 多记一张"规范键 → 原键"（它也登记了 `IDict`，所以对着接口写的代码两种表都吃）：

```ravel
t := Keyed ()
t.Set (Rec 1 "甲") "one"
t.Get (Rec 1 "别的")                        # "one"
(t.Keys ()).Map ((r: Rec) => { r.N; })      # [甲] —— 原对象回来了
```

⚠️ 两条要记住：

- **`Key` 要交回稳定的值类型**（`() => { X; }` 这样）。交回一个新造的容器（`[X]`）不行：
  `Keys.Of` 当场报「`Key` 要交回一个值类型的键（数 / 字符串）」。
- **不能现装**（`by Key = property …` 用不了）：`Key` 这个名字 `Object` 上已经有了，
  成员查找是**类链先说话** —— 和 `CompareTo` 同一个道理。

**用例见 tests/217。**

### 5.14 跑外部命令（`cmd`）

```ravel
r := cmd "git status --short"
print ((r.Get "out").Trim ())
if { (r.Get "code") != 0; } { print ("失败了:" + (r.Get "err")); }
```

`cmd` 走**系统 shell**（Windows 上是 `cmd.exe /c`，别处 `/bin/sh -c`）—— 管道、重定向、
通配符都归它管。交回的是一张 dict：`out`（标准输出）/ `err`（标准错误）/ `code`（退出码）。

- **非零退出码不是错误**：程序失败是常事，`code` 原样交给你判断；真正起不来进程才报错。
- 输出**按"先试 UTF-8、不合法退回控制台编码"解**（git/python 吐 UTF-8，`dir` 那类走控制台那套）。
- 命令本身可以是算出来的：`cmd ("echo " + string n)` 或者 `cmd "echo ${n}"` 都行。
- **不做沙箱** —— 和文件那几条一个待遇，跑什么由你自己负责。

**用例见 tests/229。**

### 5.15 JSON

```ravel
j := Json {"a": [1 2.5 ()] "b": {"c": true}}     # 原生值 → Json
print (j)                                        # {"a":[1,2.5,null],"b":{"c":true}}
print ((j.Get "a").Count ())                     # 3
print (((j.Get "a").At 1).Extract ())            # 2.5
print ((j.Text 2))                               # 缩进两格写回文本

k := Json.FromString "{\"x\": 1}"                # 字符串 → Json（唯一的解析入口）
print ((k.Get "x") |> .Extract ())                # 1
```

`Json` 里包着的是一棵**还没转成原生值**的树，所以可以先看再转：

- **看**：`Kind ()`（`"null"` / `"object"` / `"array"` / `"string"` / `"number"` / `"bool"`）、
  `IsNull ()`、`Get k` / `GetOr k dflt`、`At i`、`Count ()`、`Keys ()`（保 JSON 原顺序）；
  这些交回的还是 **Json**，可以一层层往下走。
- **转**：`Extract ()` 一次拿原生值 —— `dict` / `list` / `int`（太大退 `bigint`）/ `float` /
  `string` / `bool` / **`()`**（JSON 的 null）。
- **写**：`Json v`（`dict` / `list` / 数 / 字符串 / 布尔 / `()` 都行）→ `j.Text ()`（紧凑）
  或者 `j.Text 2`（缩进两格）。JSON 里没有的类型（自有类、分数……）**当场报错**，不悄悄降级。
- **改**：`Set k v` / `SetAt i v` / `Add v`（数组尾巴上接一个）/ `Remove k`（交回有没有删掉）/
  `RemoveAt i` —— 直接改那棵树，不用为了改一个字段把整棵 `Extract ()` 出去再包回来：

```ravel
j := Json.FromString "{\"a\": 1, \"b\": [1,2,3]}"
j.Set "c" "新加的"              # 加一个键（也可以换掉已有的）
(j.Get "b").SetAt 0 99          # Get / At 交回的是**那棵树的窗口**，改它父的那份也跟着变
(j.Get "b").Add 4
print (j)                       # {"a":1,"b":[99,2,3,4],"c":"新加的"}
```

  写进去的值走**同一张换算表**（也可以直接给一个 Json 值）。想留一份独立的就用
  `Extract ()` 拿原生值 —— 那份和 Json 里那棵树是两回事。

注意两点：**`null` 转出来是 `()`**（要问"是不是 null"就在 Json 那层问 `IsNull ()`，
转完就分不清"值是 null"和"函数没返回值"了）；`At i` 后面接 `.Extract` 要加括号
（`.成员` 绑得比并列调用紧），`((j.Get "a").At 1).Extract ()` 或者
`(j.Get "a").At 1 |> .Extract ()` 都行。

**用例见 tests/241。**

### 5.16 时间（`Time`）

一个时刻就是**毫秒数**（1970-01-01 00:00:00 UTC 起，`bigint`），库把它包成 `Time`：

```ravel
t := TimeFrom "2026-10-01 12:34:56" IsoFormat   # 格式串按 .NET 那套
t.Text ()                # 2026-10-01 12:34:56
t.Text "yyyy/MM/dd"      # 2026/10/01
t.Year () / t.Month ()   # 零件直接问
t.WeekdayName ()         # 周四
(t.AddDays 1).Text ()    # 加的是毫秒:一天 = 86400000
t < (t.AddDays 1)        # true
Now ()                   # 现在
```

- 比大小比**毫秒数**（`CompareTo`），且登记进了 `IComparable` —— 所以 `Sorting.Sort` /
  `Max` / `Min` 直接吃 `Time`；`<` `<=` `>` `>=` `==` `!=` 也照同一个口径。
- 时区**本地**；加减就是毫秒算术（不跟夏令时那套，简单可预期）。
- `print t` 打的是**字段快照**（`Ms` 和一袋零件），不是格式化后的样子 —— 要文本用 `t.Text ()`。
- 底下是 `System` 那五条原语（`NowMs` / `TimeParts` / `MakeTime` / `FormatTime` / `ParseTime`），
  库只负责"显示成什么样、怎么比"。

**用例见 tests/247。**

## 六、集合

### 6.1 List

```ravel
lst := [1 2 3]
lst.At 0          # 1
lst.Count ()      # 3
lst.Add 4         # 返回 ()，lst 变成 [1 2 3 4]
lst.Remove 1      # 返回被删掉的元素 2，lst 变成 [1 3 4]
lst.Insert 0 99   # 返回 ()，lst 变成 [99 1 3 4]
lst.Set 0 100     # 返回 ()，lst 变成 [100 1 3 4]
```

写列表的这几个方法都是**就地改**；只有 `Remove` 把删掉的元素交回来，
`Add`/`Insert`/`Set` 返回 `()`。

### 6.2 Set

```ravel
s := {1 2 3}
s.Count ()        # 3
s.Add 4
s.Remove 2
s.Contains 3      # true
```

### 6.3 Dict

```ravel
d := {"a": 1 "b": 2}      # 键是**表达式**：字符串要写引号（`{a: 1}` 里的 `a` 是变量）
d.Count ()        # 2
d.Get "a"         # 1
d.Set "c" 3
d.Has "a"         # true
```

### 6.4 序列方法(三种容器共用)

这些方法只跟**元素顺序**有关、跟容器是什么无关,所以 list / set / dict 都有同一份
(字典的元素是**值**)。名字照 C# 的集合 / Linq API 起:

| 方法 | 说明 |
|------|------|
| `Count ()` `IsEmpty ()` `Any ()` | 个数 / 空不空 / 有没有 |
| `Contains v` | 元素里有它(原子值按值比、容器按身份比) |
| `First ()` `Last ()` | 头一个 / 末一个(空容器**报错**) |
| `Take n` `Skip n` | 前 n 个 / 跳过前 n 个 |
| `Distinct ()` `Reverse ()` `Concat ys` | 去重 / 倒过来 / 接上另一串 |
| `Sum ()` `Min ()` `Max ()` | 求和 / 最小 / 最大(比大小走 `<`,比不了报错) |
| `Join sep` | 拼成字符串 |
| `ToList ()` `ToSet ()` | 换容器 |
| `Each f` | 每个跑一遍(结果丢掉) |
| `Map f` | 变换(C# 的 `Select`) |
| `Where p` | 过滤(`p` 得交回 bool) |
| `Fold init f` | 折叠(C# 的 `Aggregate(seed, f)`;`f 累积值 元素`) |
| `All p` `Any p` | 是不是都满足 / 有没有满足的 |
| `Find p` | 第一个满足的(没有就**报错**) |
| `SortBy f` | 按键排(`f` 交回键;**稳定**) |

三种容器都是 `IEnumerable`（见 4.3 的枚举器形状）：`foreach` 能遍历它们，
`(xs: IEnumerable) => …` 这样的注解也收得下它们。

三条规矩:

- **变换和查询交回新的 list**,原容器不动;要 set 就 `.ToSet ()`;
- 顺序类的按**枚举顺序** —— set / dict 的顺序是它们枚举器给的,别当插入顺序用;
- 比大小一律走 `<`:数值之间能混着比、字符串按序数比,别的类型**当场报错**。

```ravel
[1 2 3 4].Map (x: int) => { x * 10; }                 # [10 20 30 40]
[1 2 3 4].Where (x: int) => { x % 2 == 0; }           # [2 4]
[1 2 3 4].Fold 0 (acc: int x: int) => { acc + x; }    # 10
[5 2 9].SortBy (x: int) => { 0 - x; }                 # [9 5 2] —— 降序
{"a" "bb"}.Max ()                                     # bb
{"a": 3 "b": 1}.Min ()                              # 1 —— 字典的元素是值
```

**list 还有**带下标的:`At i` / `Set i v` / `Insert i v` / `RemoveAt i`(`Remove i` 是同一件事)/
`IndexOf v` / `AddRange xs` / `Sort ()` / `Clear ()`。

**set 还有**集合代数:`Union` / `Intersect` / `Except` / `IsSubsetOf` / `Add` / `Remove` / `Clear`。

**dict 还有**:`Get k`(没有就报错)/ `GetOr k fallback` / `Set k v` / `Has k` / `HasValue v` /
`Remove k` / `Keys ()` / `Values ()` / `Clear ()`。

### 6.5 集合的相等性是「同一个对象」

`int`/`string`/`bool`/`float`/`bigint`/`fraction` 这些值比较的是**值本身**，
所以 `{1 2 2}` 只有 2 个元素。但 `list`/`set`/`dict` 比较的是**身份**（是不是同一个对象）：

```ravel
{[1] [1]}.Count ()     # 2 —— 两个不同对象,内容一样也算两个
s := {[1]}
s.Contains [1]         # false —— 新造的 [1] 不是集合里那个
```

这也是为什么集合类型没有 `==`/`!=` 运算符：可变集合上的值相等很难说清
（往集合里放个列表、回头再改那个列表，集合的哈希就对不上了）。
要比内容就自己写循环，或先把集合转成别的东西。

### 6.6 代码块 vs 集合

```ravel
{1 2 3}              # Set（单行无分号）
{"a": 1 "b": 2}      # Dict（单行，第一个元素后面跟 `:`）
{ print 1; }         # Block（有分号/换行）
{
    print 1     # Block（多行）
}
```

---

### 6.7 字符与字符串

**`char` 是一个字符**（精确说：一个 UTF-16 码元，和 `s.Length` / `s.At i` 一个口径 ——
`"😀".Length` 是 2，一个 `'…'` 装不下它）。字面量用单引号，转义和字符串一样：

```ravel
'a'          '
'          '''          char 97          char "x"
```

它是**值类型**：能比大小、能当字典的键、能进集合去重（和数、字符串一个待遇）。

```ravel
'a' + 'b'        # "ab" —— 拼成字符串
'-' * 5          # "-----" —— 重复（0 或负数给空串）
int 'A'          # 65 —— 码位
'7'.IsDigit ()   # true      'A'.IsUpper ()   # true
```

**字符串的方法**（`s.At i` 给的是**字符**，不是长度 1 的串）：

| 归类 | 方法 |
|---|---|
| 长度 | `Length ()` · `IsEmpty ()` |
| 取 | `At i`（越界报错）· `Chars ()`（一串字符）|
| 找 | `Contains` · `StartsWith` · `EndsWith` · `IndexOf`（找不到**报错**）· `Find`（给 `-1`）· `LastIndexOf` |
| 切 | `Slice [1..3)`（**收一个区间**，见 6.11）· `Take n` · `Skip n` |
| 变 | `Trim ()` · `ToUpper ()` · `ToLower ()` · `Replace a b` · `Repeat n` · `Reverse ()` · `Split sep` |

```ravel
s := "Hello, World"
s.At 0                 # H（字符）
s.Contains 'H'         # true —— 收字符串也收字符
s.Slice [0..4]         # Hello —— 区间:两端都含；s.Slice (0..5) 则是不含两端那一段
"a,b,,c".Split ","     # [a b  c]
(s.Chars ()).Count ()  # 12 —— 想逐个处理就先拆成字符
```

两点注意：

- **字符串能 `foreach`**：元素是**字符**（`foreach "abc" (c: char) => …`）。
  想拿一串字符交给别的序列方法，用 `s.Chars ()`（`Map` / `Where` 那批挂在容器上）。
- 大小写转换**不跟区域设置走**（用的是 invariant）：同一段程序换台机器结果一样。

**用例见 tests/237。**

### 6.8 字符串插值

```ravel
name := "小明"
n := 3
print "你好 ${name}!"            # 你好 小明!
print "共 ${n} 个,${n * 2} 个才对"   # 共 3 个,6 个才对
print "表的显示 ${[1 2]}"        # 表的显示 [1 2]
```

**只有 `${表达式}` 一种写法**（没有 `$名字` 那种短的）。段里可以是任何表达式，甚至再放一个
字符串（`"${"abc".ToUpper ()}"` ✓ 扫描时会跳过引号里的内容）。

它不是新语义 —— 上面那句就是 `"" + "你好 " + string (name) + "!"`，所以"什么都能插"
（数、字符、对象、表……转换器管着转字符串），报错也指得准（段里写错变量，插入符落在那一段上）。

三条小事：

- **`$` 后面不是 `{` 就是字面量**：`"$5"`、`"$name"` 都不用转义；要写 `${` 本身用 `\${`；
- 没闭合的 `${` 报「插值没有收尾的 `}`」；
- **和代码里的 `$` 不冲突**：代码里根本没有 `$` 这个运算符了（5.5 那个"把右边封口"现在写
  `<|`）。`$` 只在字符串**里面**有意义 —— 插值，或者一个字面量。

**用例见 tests/237。**

### 6.9 再补几件（`Seqs`）

`Seqs` 模块里五件：摊平、切块、拉链、分组、计数。全都**对着 `IEnumerable` 写**，
所以 list / set / dict / string / `Generator` 一视同仁（和 `Io.CopyTo` 那三条一个路子），
交回的是**当场算好的** `list` / `dict`：

```ravel
Seqs.Flatten [[1 2] [3] [4 5]]        # [1 2 3 4 5]        摊平一层
Seqs.Chunk [1 2 3 4 5] 2              # [[1 2] [3 4] [5]]  每 n 个一组
Seqs.Zip [1 2 3] ["a" "b"]            # [[1 a] [2 b]]      到短的那边为止
Seqs.Group [1 2 3 4 5] ((n: int) => { n % 2; })
                                      # {1: [1 3 5] 0: [2 4]}  键是结果规范化之后的值
Seqs.Frequency ["a" "b" "a"]          # {a: 2 b: 1}        数出现几次
```

- `Group` / `Frequency` 的键走 **`.Key ()` 那套规范**（和 `dict` 的键同一个规矩）：
  `1` 与 `"1"` 是两个键；对象没写 `Key` 就当场报错。
- `Group` 每组**保持原顺序**；键的先后 = 第一次出现的先后（`dict` 保插入序）。
- `Chunk` 的每一摞是**各自独立**的 list（往里加东西不会串到别摞）；`n` 至少是 1。
- 要**惰性**就用 `Generator`（见 4.3）；这五件都是"一次算完"的。

分工：这五件是**模块函数**（`Seqs.Xxx xs …`，对着 `IEnumerable` 写的通用件）；
`IEnumerable` 自己那套（`xs.Map f` / `xs.Where p` / …，见 4.3）是**方法**，凡实现者都有；
容器上那几个是引擎给的急切版本。要连成一串就用方法那套，要"一次算完的通用件"就用 `Seqs`。

**用例见 tests/248、tests/252、tests/253。**

### 6.10 随机数（`Random`）—— 台子可以挑

`using "random.rav"` 之后有三台，**换台子只换一行构造子**：

```ravel
r := Random.Shared ()      # 进程共享(.NET Random.Shared)—— 不可复现
r := Random.Make 42        # 带种子(.NET Random(seed))—— 同一个运行时里可复现
r := Random.Xoshiro 42     # 带种子(xoshiro256**)—— **算法定死**:跨版本跨机器都一样
r := Random.Crypto ()      # 加密级 —— 不可复现、也没有种子
```

两台带种子的差别在**"可复现"到什么程度**：`Make` 走的是 .NET 的 `Random(seed)`，
它只保证**同一个运行时里**同种子同序列（换个 .NET 版本就可能变）；`Xoshiro` 是我们自己
实现的 xoshiro256**（Blackman & Vigna，公有领域）—— 算法是固定的，所以同一个种子在
**哪个运行时、哪台机器**上都给同一串。要把具体的数写死（对答案、发出去的脚本、
当伪随机表用）就用它：

```ravel
Random.Xoshiro 42 |> .Below 1000000     # 47179（定死的，换台机器也是它）
```

取数那一面是一套方法（写成接口 `IRandom` 的**默认实现**，所以三台都白拿）：

```ravel
r.Int [1..6]               # 掷骰子（开闭由区间说了算）
r.Below 10                 # 0..9
r.Float ()                 # 0.0 .. 1.0
r.Bool ()                  # true / false
r.Choice ["a" "b" "c"]     # 挑一个（空表当场报错）
r.Shuffle [1 2 3 4 5]      # 洗过的**新 list**（原表不动）
r.Sample 2 [1 2 3 4 5]     # 取 2 个不重复的（无放回）
r.Choices 3 [1 2 3]        # 有放回抽 3 个（允许重复）
r.Float (0.0..1.0)         # 区间里的小数（不收区间就是 [0, 1)）
r.Normal 0.0 1.0           # 正态：均值、标准差
r.Weighted xs ws           # 按权重挑一个（权重 0 的轮不到）
r.Bytes 8                  # 8 个随机字节（0..255）
```

**要可复现就用带种子那两台**（写测试、复现 bug 的场合）：同种子给出同一串，换种子就换一串。
加密那台和共享那台都**钉不住**（本来就没有种子）。

```ravel
draw := (rng: IRandom) => { out := []; i := 0; while { i < 5; } { out.Add (rng.Below 100); i += 1; } out; }
draw (Random.Xoshiro 7).Join "," == draw (Random.Xoshiro 7).Join ","    # true
draw (Random.Xoshiro 7).Join "," == draw (Random.Xoshiro 8).Join ","    # false
```

`Random.Make 42` 交回的是一个**能调方法的值**（`Kind` 是 `"seeded"`），可以传、可以存：
`(r is IRandom)` 成立 —— 想写"要哪台都行"的函数就把参数标成 `IRandom`。

**`Int` 收一个区间**（`r.Int [1..6]` 就是掷骰子）—— 开闭全看那对括号，不必再记
"上界含不含"：想要 0..9 就 `[0..9]`、想要不含右端就 `[0..10)`。只要个数不要区间就用
`Below n`（`0 .. n-1`）。（从前那个全局 `randint lo hi` 上界不含，已经删掉了：
现在取数只走 `Random` 这一条路。）

**用例见 tests/254。**

### 6.11 区间（`Range`）

一个**内置值类型**：从 a 到 b 的一串整数。那一对括号**各带一半的意思** ——
`[` `]` 含那一端、`(` `)` 不含 —— 所以四个组合都认：

```ravel
[1..3]     # 1 2 3            两端都含
(3..5)     # 4                两端都不含
[1..5)     # 1 2 3 4          左闭右开
(1..5]     # 2 3 4 5          左开右闭
```

打印出来就是写出来那个样子，`Text ()` 也一样。端点、个数、包含都是 O(1)：

```ravel
lo := [1..3]
lo.Start ()      # 1        lo.End ()      # 3
lo.Count ()      # 3        allOpen.Count () # 1（(3..5) 里只剩 4）
lo.Contains 2    # true     lo.Contains 5  # false
lo.IsEmpty ()    # false
```

**方向由两端自己说了算**：起点在终点**后面**就是**倒着数**；

```ravel
[5..1]        # 5 4 3 2 1        [5..1)   # 5 4 3 2
(5..1)        # 4 3 2            (5..1]   # 4 3 2 1
(3..1) → foreach 依次给 3 2 1；([5..1]).Sum () 是 15、.Join "-" 是 5-4-3-2-1
```

开闭**跟着方向走**（"不含那一端"排掉的仍是紧挨着它的那个整数），`First ()` 交回的是
**起点**那头、`Last ()` 是终点那头。切一段时方向也照区间的意思：`xs.Slice [4..0]` 是
**倒着切**（`"Hello".Slice [4..0]` → `olleH`）。

于是"空"只剩一种情形：**区间里一个整数都没有** —— `(3..3)`、`[0.1..0.9]`、NaN 那几种
（从前"起点在后所以空"已经换成"倒着数"）。

**端点收任何数值**（int / bigint / float / fraction，混着写也行），而**元素是「区间里的整数」**：

```ravel
[1.5..3.5]        # → 2 3          夹在中间那些整数就是元素
(0.1..0.9)        # → 空           一个整数都不在里面
[1..2.5]          # → 1 2
[1..bigint 5e9]   # 元素是 bigint（值有多大就多大），Count () 装不下也给 bigint
(1/2..5/2)        # → 1 2
```

于是 `Start ()` / `End ()` 和 `First ()` / `Last ()` 是**两回事**：前者交回**写出来那个数**
（`[1.5..3.5].Start ()` 是 1.5），后者交回区间里**真有**的第一个/最后一个整数。

**"里头有没有"和"落不落在这段里"是两个问题，各有一条**：

```ravel
[1..10].Contains 2      # true    —— 是不是**元素**（元素是那些整数，和别的容器一个意思）
[1..10].Contains 2.5    # false   —— 2.5 不是集合里的元素
[1..10].Covers 2.5      # true    —— 但"落不落在这段里"是另一回事：按**端点**比，小数也算
[1..10).Covers 10       # false   —— 开闭照旧
[1.5..3.5].Covers 1.7   # true    —— 端点带小数也一样（`Contains 1.7` 才是 false）
```

（Ruby 也是这么分的：`include?` 问元素、`cover?` 问区间。）

无穷端点（`[1..Inf]`）当场报错（给不出界），NaN 当空区间。

**它也是一串**（`Range` 也是 `IEnumerable`），所以整套白拿：

```ravel
foreach [1..4] (i: int) => { print i; }
([1..4]).Sum ()                        # 10
([1..10]).Where ((i: int) => { (i % 3) == 0; }).ToList ()   # [3 6 9]
([1..5]).Join "-"                      # 1-2-3-4-5
```

端点和**头一个/末一个元素**分开：`Start ()` / `End ()` 是写出来那两个数，`First ()` / `Last ()`
是区间里**真有**的第一个和最后一个（开区间那端不在里面）—— 都 O(1)：

```ravel
[1..9].First ()      # 1        [1..9].Last ()      # 9
(1..9).First ()      # 2        (1..9).Last ()      # 8
```

**切一段**给任何一串都用 `Slice`，**收一个区间**（字符串上那条也一样 —— 从前"两个数字 + 半开"
的写法已经删掉）：

```ravel
xs := [10 20 30 40 50]
xs.Slice [1..3]      # [20 30 40]
xs.Slice [1..4)      # [20 30 40] —— [1, 4) 里是 1 2 3
xs.Slice (0..2]      # [20 30]
([1..1000000000]).Slice [5..7].ToList ()   # [6 7 8] —— 惰性，后端无限也不怕
```

（序列上那条 `Slice` 越界**不报错**：一串到底就没了，和 `Take` / `Skip` 一个脾气；
字符串上那条越界当场报错，和它的两个数字写法一致。）

而且**是惰性的**（枚举器是生成器的光标）：`([1..1000000000]).Take 3` 秒回、
`([1..1000000]).First ()` 拿一个就走。`Count ()` / `Contains n` / `ToList ()` 走的是引擎
那几条（O(1) / O(1) / O(n)），所以`([1..1000000000]).Count ()` 也是秒回。

区间**按内容比**：`[1..3] == [1..3]` 成立、`[1..3] == [1..4]` 不成立（它挂在 `ValueType`
那一支下 —— `[1..3] is ValueType`）。

**用例见 tests/255。**

### 6.12 正则（`Regex`）

字符串那批（`Contains` / `Find` / `Split` / `Replace` …）只认**字面量**；凡是"形状" ——
一串数字、可选的结尾、几个空白折成一个 —— 用正则。**要显式引用**：

```ravel
using "regex.rav"

re := Regex.C "(\d+)-(\d+)"
re.IsMatch "12-34"               # true
(re.Match "a 12-34 b").Value ()  # "12-34"
re.FindAll "12-34 56-78"         # 命中的两段
re.Replace "12-34" "$2-$1"       # "34-12"（组引用）
re.Split "a1b22c"                # 按模式切（字面量那套做不到的事）
Regex.Escape "a.b"               # "a\.b" —— 把字面量转成模式
```

命中的那一段是 `RegexMatch`：`Value ()` / `At ()`（在原文里的位置）/ `Length ()` /
`Groups ()` / `Group 1` / `Group "名字"`（命名组写 `(?<名字>…)`）。**没参与匹配的组给 `()`**
—— 和"匹配到空串"分得开。有没有命中用 `TryMatch`（→ `Option`），`Match` 没有就当场报错
（和 `Find` 一个脾气）。

**选项**：第二个参数（`Regex.With pattern "i"`），或者写在模式里（`(?i)abc`）——
`i` 不分大小写、`m` 多行（`^`/`$` 认每一行）、`s` 让 `.` 也吃换行、`x` 忽略模式里的空白。

**两条兜底在引擎那层**（见 `Runtime/Builtins/SysRegex.cs`）：一条正则最多跑 **2 秒** ——
写歪一个模式（`(a+)+$` 配一长串 a）能让进程**卡死**，而卡死不是异常、`try` 接不住；
模式写错（括号没配对上）也报成普通的 Ravel 错误，不会绕过 `try` 把程序打掉。

**用例见 tests/256。**

### 6.13 格式化、文本、编码（`Format` / `Text` / `Encoding`）

三件"每天都用、手搓不值当"的事，各自一个模块，**都要显式引用**。

```ravel
using "format.rav"
Format.Fmt "{} 有 {} 个" ["苹果" 3]        # 苹果 有 3 个
Format.Fmt "{0} 与 {0}" ["甲"]              # 位置能复用;给 dict 就按名字取
Format.PadL "7" 3 / Format.PadR "7" 3      # "7  " / "  7"
Format.Num 3.14159 2                       # "3.14"（不足补零）
Format.Thousands 1234567                   # "1,234,567"
Format.Hex 255 / Format.Bytes 1536         # "ff" / "1.5 KB"
```

```ravel
using "text.rav"
Text.Lines "a
b"                # ["a" "b"]（顺带去掉 Windows 的 
）
Text.Words "a  b"                # ["a" "b"]（空白折成一个分隔）
Text.Wrap "…很长…" 20             # 按宽度折行；长词不硬切，自己占一行
Text.Indent "a
b" 4             # 每行前加 4 个空格（空行不动）
Text.Dedent "    a
    b"       # 去掉公共缩进
Text.Truncate "abcdef" 4         # "abc…"
Text.Quote "说不清\"的话"         # 转义成看得见的样子（拼报错/写期望时用）
```

```ravel
using "encoding.rav"
Encoding.Base64Text "你好"        # "5L2g5aW9"（先 UTF-8 变字节，再编码）
Encoding.Base64 [1 2 3]           # "AQID"（也可以直接喂字节表）
Encoding.Hex [255 0] / Encoding.Unhex "ff00"
Encoding.UrlEncode "a b/中"       # "a%20b%2F%E4%B8%AD"
Encoding.HtmlEscape "<a&b>"       # "&lt;a&amp;b&gt;"
```

**字节表**是"一串 `0..255` 的 int"（`Random.Bytes n` 交回的就是它）。Ravel 的 `string`
是 UTF-16，**装不下任意字节** —— 所以这层的规矩是：要编码就先进字节表，要文本就先 `Utf8Text`。

**注意**：`Format.PadL` 这类补出来的空格**在行尾**，写 golden 用例时要套一层方括号才钉得住
（跑测试那套会把每行末尾的空白裁掉）。

**用例见 tests/260、tests/261、tests/262。**

### 6.14 数据类（`dataclass`）

一堆字段 + 「打印、相等、构造」三件事每次都手写太啰嗦，`dataclass` 一个都不用手写。
**要显式引用**：

```ravel
using "dataclass.rav"

Point ::= dataclass {
    x: int = 0
    y: int = 0
}

p := Point 1 2                 # 按位置收，顺序照声明
print (p.Text ())              # Point(x=1, y=2)
print (p.Values ())            # [1 2]
print (p == (Point 1 2))       # true
print (p == (Point 1 3))       # false
print ((Point ()).Text ())     # Point(x=0, y=0) —— 空实参 = 全用默认值
print ((Point 1 ()).Text ())   # Point(x=1, y=0) —— 某一位给 `()` = 那一位用默认值
```

自动拿到的成员：`Text ()` / `Values ()` / `Eq` / `==` / `!=` / 按位置收的 `init`。
**用户自己写了同名的就以用户的为准**（一条都不盖）—— 比如写一条自己的 `Text`。
`Text` 里的名字用 `::=` 那个；`==` 是"同类型 && 逐字段"（值是值语义、对象按身份，
和 `==` 一贯的口径）。

**字段是看得出来**的：实例自己那层里"值不是函数"的那些 —— 方法、`init`、从 `Object`
继承来的机制成员都不算。所以数据类里照样能写方法：

```ravel
Named ::= dataclass {
    first: string = ""
    last: string = ""
    Full := () => { first + " " + last; }      # 方法，不会被当字段
}
```

字段表**到用的时候才算**，所以拿数据类当父类也认得出子类新加的字段；反过来，
`{ }` 是**空字典**，一个字段都没有的数据类要写 `dataclass { 0; }`。

**它是个元类**（`class type { … }`，见 7.10）—— 建类时把一块拼到类体后面
（`body.Append inject`），那块和类体同属一层、实例化时接着跑，于是"注入"和"自己写几条成员"
没有区别。这条机制（`ObjectVal.BodyExtras`）任何元类都能用。

**用例见 tests/257。**

### 6.14.1 枚举（`enum`）

一串名字和值，每个名字拿到一枚**这个枚举的**值：

```ravel
using "enum.rav"
myEnum ::= enum {
    A := 1
    B := 2
    C := 3
}

myEnum.A              # 一枚 myEnum 类型的值（单例）
myEnum.A.Value        # 1
myEnum.A.Name         # "A"
myEnum.A.Text ()      # "A" —— `print` 打的是 C# 那侧的 `ToString`，要好看的走 `Text ()`
myEnum.Values ()      # [A B C]，按声明的先后
myEnum.Of 2           # myEnum.B；没有这个值当场报错
myEnum.A == myEnum.A  # true（单例，比的就是身份）
```

值是**任何东西**都行 —— `Red := "红"` 也成立。

**元类只管固定的那一套**（`Value` / `Name` / `Text ()` / `Values ()` / `Of`），
`A`/`B`/`C` 是类体正文里写的，一个都不写死在元类里。它这么收：建一个**空类**，
拿一个普通实例当**落脚点**把那份 `{ A := 1 … }` 跑一遍（`with` 现在**不推层**，
所以定义留得下来），再把每个名字换成"枚举值"挂到类上。

> 为什么要绕这一圈：要是拿 `{ A := 1 … }` 直接当类体，`A` 就先成了 `int`，
> 回头再往它上面挂一枚实例会撞类型。落脚点把这一步隔开了。

**继承**：父类**必须也是个 enum**（拿普通类当父类当场报错 —— 枚举继承过来的是"父类那几枚成员"，
一个普通类没有任何东西可迁）。父类那几枚会**迁过来**，这一层的块接着填新的：

```ravel
base ::= enum { A := 1  B := 2 }
sub  ::= enum base { X := 7 }

sub.A.Value      # 1 —— 父类那枚迁过来了
sub.X.Value      # 7
sub.Values ()    # [A B X]，父类的在前
sub.A is sub     # true  —— 迁过来的是**这个枚举的**值
sub.A == base.A  # false —— 所以两边同名不同枚
(sub ()) is base # true  —— 但 sub 的实例仍是 base（类链那条）
```

**用例见 tests/290。**

### 6.15 位（`Bits`）

运算符那边已经有 `&` `|` `^` `<<` `>>` `<<<` `>>>`；这个模块补的是它们给不了的 ——
数位、取位/置位、位域、和字节表互转：**要显式引用**。

```ravel
using "bits.rav"

Bits.Test 5 0            # true —— 第 0 位是不是 1
Bits.Set 5 1             # 7    —— 把第 1 位置起来（原值不动，交回新的）
Bits.Clear 7 1           # 5
Bits.Toggle 5 0          # 4
Bits.Not 0               # -1   —— 逐位取反（`!` 那个是**逻辑**取反）

Bits.Count 255           # 8    —— 置起来的位有几个（popcount）
Bits.Width 255           # 8    —— 要几位才装得下（0 给 0，负数给 32）
Bits.High 5 / Bits.Low 40      # 2 / 3 —— 最高/最低那个置位在哪（0 基，没有给 -1）
Bits.ZerosHigh 1 / Bits.ZerosLow 40   # 31 / 3

Bits.Mask 4              # 15   —— 低 4 位全 1（`Mask 0` 给 0、`Mask 32` 给 -1）
Bits.Field n 4 4         # 从第 4 位起取 4 位
Bits.PutField n 4 4 15   # 那 4 位换成 15 的低 4 位，别处不动

Bits.Shr 5 1             # 2    —— **逻辑**右移（补零）；`>>` 是算术的，符号留住
Bits.Bytes 16909060      # [1 2 3 4] —— 大端（`BytesLE` 是小端）
Bits.FromBytes [1 2 3 4] # 16909060
Bits.Unsigned (0 - 1)    # 4294967295 —— 32 位位型读成非负数（bigint）
Bits.Signed 4294967295   # -1
```

位下标一律 **0..31**（0 是最低那一位），**越界当场报错** —— 静默"什么都没做"是最难查的那种。
`Bytes` / `FromBytes` 那两条和 `6.13` 的**字节表**是同一个形状（一串 `0..255` 的 int），
所以 `Encoding.Base64 (Bits.Bytes n)` 这样接得起来。

**用例见 tests/267。**

### 6.16 网络（`Http`）

抓一个网页、调一个 API 都走这个模块，**要显式引用**：

```ravel
using "http.rav"

r := Http.Get "https://example.com"
r.status              # 200
r.reason              # "OK"
r.Ok ()               # 2xx?
r.Text ()             # 正文（按响应头里的 charset 解，没写就是 UTF-8）
r.Bytes ()            # 原样的字节表
r.Json ()             # 直接当 Json 用（先看再转那一套都在）
r.Header "content-type" / r.HeaderOr "x-nope" "-"
r.Size ()             # 多少字节
r.Save "page.html"    # 正文写进文件
```

**发什么**：`body` 给字符串就按 UTF-8 发，给 dict / list / set 就**自动当 JSON**
（连 `content-type` 一起补上）：

```ravel
Http.Post "https://…/api" {"name": "Ravel"}      # JSON
Http.Post "https://…/form" "a=1&b=2"             # 原样发字符串（记得自己写 content-type）
Http.Put / Http.Patch / Http.Delete / Http.Head
Http.Request {"url": … "method": "PATCH" "headers": {"x-token": "…"} "body": …
              "retries": 2 "backoff": 500 "timeout": 30000 "follow": true}
Http.Query "https://…/s" {"q": "中文 词"}         # 拼查询串（值先转义）
```

**下载与上传**：正文**边收边写**，不进内存 —— 几百 MB 也不怕（普通请求有条 16 MB 的封顶，
超了会明确让你改用 `Download`）：

```ravel
d := Http.Download "https://…/a.zip" "a.zip"     # d.size 是写了多少字节
Http.Upload "https://…/put" "a.zip"             # multipart，字段名默认 file
Http.UploadTo "https://…/put" "a.zip" "upload"   # 换个字段名
```

几条规矩：

- **4xx / 5xx 不是错误**，那是响应 —— 自己看 `status`。想"非 2xx 就抛"用
  `Http.Expect r`（消息里带状态和正文的头一段）。
- **连不上 / 超时 / 域名解析不了**才是错误，是 `IoError`，消息里带 URL 和原因。
- **同步**：一个请求等一个，回来了才往下走。（以后加并发时这一层不变 —— 见 CONTEXT。）
- 老编码认：响应头写着 `charset=gbk` 的老网页照样解得对。

**一个 URL 就是一个文件** —— 这条是 `io.rav` 开头那句"磁盘上的、内存里的、zip 条目、
**远程的**……都只是 `IFile` 的实现"的落地。`Http.Url "…"` 交回的东西有那一套成员，
所以**对着接口写的东西直接能用**：

```ravel
Io.CopyTo (Http.Url "https://…/a.txt") (Io.File "a.txt")   # 抓下来存着
Io.Lines (Http.Url "https://…/log")                       # 一行行读远程日志（惰性）
Io.CopyTo (Http.Url "…") Terminal.Stdout                          # 直接倒进终端
Http.Download "https://…/a.zip" (Io.File "a.zip")         # 落点也能给条目，不只路径字符串

u := Http.Url "https://…/a.txt"
u.Read ()        # 每次都是**一次请求**（和 Io.File.Read () 每次都读盘一样，不藏缓存）
u.Exists ()      # 走 HEAD，不下载正文；404 就是"没有"
u.Size ()        # HEAD 的 content-length
u.Write "…"      # 发的是 PUT（HTTP 的"写一个资源"）
u.Delete ()      # 发的是 DELETE
u.Append "…"     # 报错：HTTP 没有"追写"这回事
```

**用例见 tests/268（离线）、tests/269（真发请求，靠运行器起的回环服务器）；
想真出网看 `examples/http.rav`。**

### 6.17 摘要、标识与数据库（`Hash` / `Uuid` / `Sqlite`）

**`Hash`** —— 摘要、HMAC、CRC32、令牌：

```ravel
using "hash.rav"
Hash.Sha256 "abc"      # "ba7816bf…"（小写十六进制；字符串按 UTF-8 进）
Hash.Sha256Bytes "abc" # 要字节表
Hash.Hmac "sha256" "密钥" "报文"
Hash.File "a.zip" "sha256"   # 文件摘要（流式，多大的文件都不进内存）
Hash.Crc32 "abc"       # 891568578（纯 Ravel 算的）
Hash.Equal a b         # 常数时间比，防时序攻击
Hash.Token 16          # 32 个十六进制字符的随机串
```

**`Uuid`** —— 标识：

```ravel
using "uuid.rav"
Uuid.V4 ()        # 随机
Uuid.V7 ()        # 按时间排（头 6 字节是毫秒时间戳）—— 当数据库主键不捅索引
Uuid.IsValid s / Uuid.Version s / Uuid.Bytes s / Uuid.Time s
```

**`Sqlite`** —— 一个文件就是一个库：

```ravel
using "sqlite.rav"
db := Sqlite.Open "app.db"        # 或 Sqlite.Memory ()
db.Exec "create table t (id integer primary key, name text)" {}
db.Exec "insert into t (name) values (@n)" {"n": "ada"}   # 参数按名字绑
db.All  "select * from t" {}      # [{id: 1 name: "ada"}]（一行一个 dict）
db.One  "select * from t where id = @id" {"id": 1}        # 一行；没有给 ()
db.Value "select count(*) from t" {}                       # 一个值
db.Each "select * from t" {} (row: dict) => { print (row.Get "name"); }
db.Tx { … }                       # 事务：出错自己回滚，再把错抛出去
db.Close ()
```

**参数那格永远要给**（没有参数就写 `{}`）—— Ravel 没有默认参数，而"有参/无参"分成两个名字
读起来更烦。存得进的是 数 / 字符串 / 字符 / 布尔（存 0/1）/ `()`（就是 **NULL**）/ 字节表（**BLOB**）；
读出来 INT 装得下就给 `int`、否则 `bigint`。

**用例见 tests/272、tests/273、tests/274（内存库，不落盘）。**

### 6.18 数据结构（`Stack` / `Queue` / `Deque` / `Heap` / `SortedDict` / `SortedSet`）

这六个是**插件**：类和函数在一个独立项目里（`Ravel.Structures/`），编成
`plugins/Ravel.Structures.dll`；`using "structures.rav"` 把它装进来，顺手登记
`IEnumerable`（于是 `foreach` / `Map` / `Fold` 那一整套白拿）。名字在 `Structures` 模块下：

```ravel
using "structures.rav"
s := Structures.Stack ()     # 也可以 Structures.Stack [1 2 3]
```

```ravel
s := Structures.Stack ()            # 也可以 Structures.Stack [1 2 3]（从底往上垒）
s.Push 1 / s.PushRange [2 3] / s.Pop () / s.Peek () / s.ToList ()

q := Structures.Queue [1 2 3]       # 先进先出
q.Enqueue 4 / q.Dequeue () / q.Peek ()

d := Structures.Deque [2 3]         # 两头都能进能出（Push / Pop 是后端的简写）
d.PushFront 1 / d.PopFront () / d.PopBack ()

h := Structures.Heap [3 1 4]        # 每次弹出**最小**的那个；Heap true 是大顶堆
h.Push 0 / h.Pop () / h.ToList ()      # ToList 是弹出来的顺序 = 排好序

sd := Structures.SortedDict ()      # 键永远有序
sd.Set "b" 2 / sd.Keys () / sd.Get "b" / sd.Remove "b"

ss := Structures.SortedSet [3 1 2]  # 升序、去重
ss.Add 4 / ss.Min () / ss.Max ()
```

几条规矩：

- **比大小的那些结构**（`Heap` / `SortedDict` / `SortedSet`）按 Ravel 的 `<` 排 ——
  里面装的得能互相比大小，比不了当场报「比不了 Integer 与 String」。
  相等也按 `<` 算：`1` 与 `1.0` 在这儿是同一个（`dict` / `set` 那边按类型分，不一样）。
- 有序字典的键是**值类型**（数 / 字符串），和 `dict` 一个规矩。
- 空结构上 `Pop` / `Peek` / `Min` 这类**报错**（不是给 `()` ——「没有」和「是空值」不该长得一样）。
- 打印出来带前缀（`Stack [3 2 1]` / `SortedSet {1 2 3}`），枚举顺序就是那个顺序。

**用例见 tests/275。**

想自己写一族?照 `Ravel.Structures/` 的样子开一个项目:类上挂
`[RavelModule("模块名")]` + `[RavelClass("类名")]`,方法挂 `[ClassMethod]` / `[ClassCtor]`,
模块里的函数挂 `[RavelFn]` —— `using "你的.dll"` 之后就能用(见 `Runtime/Builtins/PluginApi.cs`)。

### 6.19 官方扩展（`Native`）

有六个库的本机半边**不在引擎里**,在一个官方的扩展 dll 里(`Ravel.Extensions/` →
`plugins/Ravel.Extensions.dll`):`Hash` / `Crypto` / `Http` / `Regex` / `Sqlite` / `Random`。
它们是"这台机器能干什么"(算摘要、发请求、开数据库),不是"这门语言是什么"——
所以搬出了 `System`。`Math` 也在这里(整个模块,不只是原语):

```ravel
using "native.rav"                        # 或者直接 using "hash.rav" 那类库,它们会带进来
print (Native.HashBytes "sha256" (Encoding.Utf8 "abc"))
```

平时用不着碰这一层:`Hash.Sha256` / `Http.Get` / `Sqlite.Open` 那些库面才是给人用的。
一层薄壳(native.rav)是为了把 `plugins/...` 这个路径收在**一处**。

### 6.20 ZIP 归档（`Zip`）

归档**是一个文件系统** —— 这是 `IFile` 那条缝许诺过的那一格:

```ravel
using "zip.rav"
z := Zip.Open "a.zip"                 # 归档是 IDir
z.Entry "a/one.txt" |> .Read ()        # 成员是只读的 IFile
Io.EachDir z (p: string e: object) => { print (e.Name ()); }   # 对着接口写的一律照吃

w := Zip.Create "new.zip"             # 新建（同名整个覆盖）
w.Add "hello.txt" "你好"
w.AddFile "big.bin" "一个磁盘文件"     # 流式，内容不进堆
w.Close ()                            # ← 写这条路上不能省
```

成员**写不回去**（`Write` / `Append` / `Delete` 一律报错）:zip 改一个成员得整档重写,
与其做一个悄悄重写的 `Write`，不如当场说清楚。要造新档走 `Zip.Create`。

**用例见 tests/282。**

### 6.21 通配符（`Glob`）

```ravel
using "glob.rav"
Glob.Match "src/**/*.cs" "src/a/b.cs"    # true
Glob.Find "src/**/*.cs"                  # 递归找，交回相对路径（一律正斜杠）
```

四样:`*`（这一层任意个，**不跨 `/`**）、`?`、`**`（跨 `/`，零层也算）、`[...]`（`[!...]` 取反）。
**按整串比** —— `*.txt` 不配 `sub/a.txt`，要"哪儿都算"就写 `**/*.txt`。

**用例见 tests/283。**

### 6.22 XML（`Xml`）

```ravel
using "xml.rav"
x := Xml.Parse "<config><port>8080</port></config>"
print (x.Name ())                 # config
print ((x.Child "port").Text ())  # 8080
print (Xml.Render x)              # 打回文本（`print x` 给的是字段表，要内容得显式取）
(x.Child "port").Put (Xml.Text "9090")
```

和 `JsonNode` 是**同一个模型**：节点是一层包装，底下是普通的 dict / list——所以改一个节点
就是改那棵树。它同时也**是一个文件系统**（`IFile` + `IDir`），`Io.EachDir` 那几件照吃。

三条取舍：**命名空间不参与**（前缀和 `xmlns:` 都丢掉，按局部名认）、**空白文本节点不收**、
属性是有序表。同名兄弟在 `List ()` 里带 `[2]` 这样的尾巴（XPath 那个写法）。

**用例见 tests/284。**

### 6.23 终端（`Terminal`）

```ravel
using "terminal.rav"
Terminal.Line (Terminal.Green "好了")          # 上色写一行（管道里自动不上色）
print (Terminal.Plain "[b]粗[/]")          # 只看渲染结果，一定不带颜色
name := Terminal.Ask "叫什么？"
if { Terminal.Confirm "删掉？"; } { … }
pick := Terminal.Choose "选一个" ["a" "b"]
print (Terminal.Bar 3 10 20)               # [######..............] 30%
```

标记就是 **Spectre.Console 那套**（`[red]…[/]`、`[[` 表示一个方括号），不是新发明的一套。
糖（`Terminal.Red` / `Terminal.Good` …）交回的是**标记串**，并且会把你给的文本**转义**掉——
所以糖套糖不行（里层会被当字面量），要嵌套就直接写标记。

上色与否看 `Terminal.Tty ()`（**进程**的 stdout 是不是真终端）。要确定性的输出（测试、写文件）
用 `Terminal.Plain`，别用 `Terminal.Render`；往日志里写"同一条消息但没有颜色"用 `Terminal.Strip`。

**用例见 tests/285。**

### 6.24 用 Ravel 写一个 REPL（`Repl`）

`examples/repl.rav` —— 多页缓冲、三维光标、按词上色、按键编辑、主菜单、会话存盘，
**全用 Ravel 写**，在 [lib/repl.rav](lib/repl.rav) 里：

```ravel
using "repl.rav"
Repl.Run ()
```

它是 C# 那版 `Repl/NeoInteractor.cs` 的**一比一复刻**（连启动那张大图和菜单项的顺序都一样），
为的是看看这门语言自己够不够用 —— 结论是够：**引擎只多了六样原语**（读单个键、清屏、
定位光标、藏光标、把异常渲染成那份报告，再加"把 stdout 收进字符串"那一对），
其余全在库里（连高亮用的扫描器都是）。接管道时它不当编辑器，把喂进来的整段跑完就走：

```bash
echo 'print 1 + 1' | dotnet out/ravel.dll examples/repl.rav
```

**用例见 tests/286。**

### 6.25 小工具四件（`Crypto` / `Args` / `Table` / `Log`）

**`Crypto`** —— 加密与口令。和 `Hash` 是两件事：那边是"防篡改"，这边是"藏起来"。

```ravel
using "crypto.rav"
key := Crypto.DeriveKey "口令" (Crypto.Salt ()) 600000   # PBKDF2-SHA256 → 32 字节
c   := Crypto.Seal key "秘密"                      # AES-256-GCM → base64 串
Crypto.Open key c                                  # "秘密"；拆不开就报错，不给垃圾

stored := Crypto.HashPassword "hunter2"            # "pbkdf2$sha256$600000$盐$哈希"
Crypto.CheckPassword "hunter2" stored              # true（常数时间比）
Crypto.CheckPassword "hunter3" stored              # false
```

两条路分得很开：**藏数据**用 `Seal` / `Open`（密钥在手，丢了就没了）；
**存口令**用 `HashPassword` / `CheckPassword`（存的是"怎么校验"，还原不出口令，
所以这两条**没有**"解密"那一半——那是故意的）。

**`Args`** —— 命令行参数：

```ravel
using "args.rav"
a := Args.Parse (System.Args ())                          # 不看规格
a := Args.ParseWith (System.Args ()) {"port": "int" "v": "flag"}
Args.Get a "port" 8080 / Args.Has a "v" / Args.Positional a / Args.Usage spec
```

认 `--x=v` / `--x v` / `--x` / `-x` / `-abc`（合并的短开关，**不带值**）/ `--`（之后全算位置参数）；
`-5` 和光杆 `-` 当位置参数。**没给规格时光杆一律当开关**（不带 `=` 就不吃后面那个实参）——
不然 `-v a.txt` 里的 `a.txt` 会被 `v` 吞掉。结果是一张 dict：选项进同名的键，位置参数进 `"_"`。

**`Table`** —— 打表格。最要紧的是**宽度**：算的是"终端里占几格"（汉字 2、其余 1），
不是 `s.Length ()` —— `Format` 那几条数的是字符数，拿来对齐中文会歪。

```ravel
using "table.rav"
print (Table.Render [{"名字": "张三" "年龄": 30} {"名字": "李四" "年龄": 7}])
# ┌──────┬──────┐
# │ 名字 │ 年龄 │
# ├──────┼──────┤
# │ 张三 │   30 │      ← 数那一列自动右对齐
# │ 李四 │    7 │
# └──────┴──────┘
Table.RenderWith rows {"cols": ["名字"] "style": "plain" "align": {"名字": "right"}}
Table.Width "中文 abc"      # 8
```

**`Log`** —— 分级日志：

```ravel
using "log.rav"
lg := Log.New {"level": "debug" "file": "app.log" "tag": "db"}
lg.Info "连接上了"          # 2026-10-01 09:30:00 [INFO] db: 连接上了
lg.Warn "磁盘快满了"
Log.Info "不拎 logger 的写法（走默认那台，级别看 RAVEL_LOG）"
```

`debug < info < warn < error`，比当前级别松的一律**不落笔**（过滤在拼串之前）。落点：给了
`"file"` 追加到文件、`"err": true` 走 stderr，否则 stdout；落盘失败**当场报错**，不咽下去。

**用例见 tests/276 ～ tests/279。**

## 七、类

### 7.1 类就是一个对象

```ravel
Person ::= class {
    init = () => { this; }
    name: string = ""
    age: int = 0
}
p := Person ()
p.name = "Alice"
print (p.name)           # Alice
print (Person.name)      # Person
print (typeof p)         # Person
print (typeof Person)    # Type
```

**没有 `Class` 类型，也没有单独的「类」表示。** `class` 和 `type` 是**同一个值**
（`print (class == type)` 打出 `true`），所以 `Person ::= class { ... }` 和
`Person ::= type { ... }` 建出来的是同一个东西。

一个类就是一个**类对象**（C# 层是 `Runtime/Values/ClassVal.cs`），
它的成员和实例一样挂在一张作用域表里：

| 成员 | 含义 | 怎么看 |
|---|---|---|
| `parent` | 父类对象（链的上游）。**只读** | `print (Person.parent)` → `Object` |
| `block` | 类体——实例化时重跑的配方。**只读** | `typeof Person.block` 是 `Block` |
| `name` | 类名 | `Person.name` → `"Person"` |

类对象本身就是**可调用的东西**（不是靠某个成员表示"我能被调用"），所以写得出
`Person ()`；而普通实例不是函数，`p ()` 会报「值 … 不是函数，不能调用」。

`typeof X` 取的是**创建 X 的那个类对象**（元类）：`typeof p` 是 `Person`，
`typeof Person` 是 `Type`；`type` 的元类是它自己，链在那里到头。所以「类是实例的类、
类自己也有类」不需要另一套机制——**建类就是调用一个类对象**，和普通构造调用走同一条路
（见 7.10）。

### 7.2 init 是构造器：必须以 this 收尾

构造器就是类体里那个名字叫 `init` 的变量。一个类最多一个，没有按参数类型重载。

**写 `init = …`，不是 `init := …`。** 每个类（不管继承谁）都已经从 `Object` 那儿继承了
一条默认构造器，所以 `init := …` 是**在同一个名字上又来一条定义** → 当场报错。
`=` 是覆盖：子类写 `init = …` 换掉继承来的那条，**父类那条不会被调用**（要接着做父类的
构造得自己想办法，语言里没有 super）。

**它是 `protected` 的**：类体系里随便用（上面那句 `init = …` 就是），外面 `obj.init` /
`obj.init = …` 会报「受保护的」—— 构造器不是给人从外面拨的开关。

**`init` 交出的返回值就是构造的结果**，所以约定以 `this` 收尾：

```ravel
Good ::= class {
    init = () => {
        x = 1
        this;                # ← 交出去的就是这个对象
    }
    x: int = 0
}
print ((Good ()).x)          # 1
```

忘了写就**静默**拿到 `()`——块的值是最后一条语句的值，而赋值语句的值是 `()`：

```ravel
Bad ::= class {
    init = () => { x = 1; }     # 最后一句是赋值,没有 this
    x: int = 0
}
print (typeof (Bad ()))          # Void —— 不是 Bad
```

不报错，只是对象不见了。同一个坑还有两种长相：`init` 以 `print` 收尾、
以及元类的 `init` 忘了交出 `parentInit` 建出来的那个类（见 7.10）。

`init` **是变量名，不是修饰符**。旧写法会被专门拦下来：

```ravel
C ::= class { init ctor := () => { this; } }
```

```
Error: 构造器不再用 init 修饰符，直接写 `init = () => { ... }`
```

### 7.3 构造器与参数

```ravel
Point ::= class {
    init = (x0: int y0: int) => {
        x = x0
        y = y0
        this;
    }
    x: int = 0
    y: int = 0
}
p := Point 3 4           # 和普通函数一样柯里化:((Point 3) 4)
print (p.x)              # 3

half := Point 10         # 少给一个参数 → 半成品构造器
q := half 20
print (q.y)              # 20
```

构造器调用和普通函数走**同一条柯里化路径**（`tests/53_ctor_curry.rav`）：
对象在第一次调用时就建好（类体已跑、`this` 已绑），`init` 每收到一个参数就往下走一层；
参数没收齐交出的是函数，收齐了才交出 `init` 的返回值。

⚠️ 同一个半成品被调用多次，会作用在**同一个对象**上（接着上面的 `half` / `q`）：

```ravel
r := half 30
q.y = 99
print (r.y)              # 99 —— q 和 r 是同一个对象
```

要独立对象就写 `Point 10 20` 和 `Point 10 30`，别复用同一个 `half`。

一个类只有一个构造器，**没有按参数类型重载**。要按参数分派就在 `init` 里自己判断：

```ravel
Flex ::= class {
    init = (v: object) => {
        x = 0
        if { typeof v == int; } { x = int v; } { x = 0 - 1; }
        this;
    }
    x: int = 0
}
print ((Flex 7).x)       # 7
print ((Flex "hi").x)    # -1
```

### 7.4 继承

```ravel
Animal ::= class {
    init = () => { name = "from-init"; this; }
    name: string = "?"
}
Dog ::= class Animal {
    init = () => { breed = "husky"; this; }
    breed: string = ""
}
d := Dog ()
print (d.name)      # ?       —— 父类 init 里的赋值不生效
print (d.breed)     # husky
```

继承是**平铺**的：`Dog ()` 时沿 parent 链从顶祖先到自己**依次跑每一层类体**，
所有层的字段落在**同一个实例作用域**里，所以子类能直接读写父类的字段。

- **父类的 `init` 不会自动调用。** 初始值要写在字段声明上（`name: string = "?"`），
  写在父类 `init` 里的赋值不生效——上面 `d.name` 是 `"?"`，不是 `"from-init"`。
- **只调用最具体的那一层 `init`**：子类没写就落回父类的。

  ```ravel
  NoInit ::= class Animal {
      breed: string = ""
  }
  print ((NoInit ()).name)      # from-init —— 用的是 Animal 那一层
  ```

  整条链都没写就落回 **`object` 那份默认构造器**（`init = () => { this; }`（引擎内部那份预设写法），什么都不做、
  把对象交出来）——所以任何类都构造得出来，不用非得写一个空的 `init`：

  ```ravel
  Bare ::= class { x: int = 1; }
  Bare ()          # Bare { x = 1 }
  Bare default     # 一样（默认构造器不看参数）
  ```

  这份默认构造器在 `object` 的类体里，跑在**最前**（祖先优先），所以谁写了 `init` 谁覆盖它；
  内建那些更具体的构造器（`int` 的转换器、`List` 那个只认 `default` 的）也照样赢。
- **子类重声明同名字段就是覆盖**（`:=` 是定义）。
- 想改从父类继承来的字段，直接赋值即可（`breed = "husky"`）。
- **没有 `base`**，也不需要：父类实例不由子类手工构造。

### 7.5 ::= 命名

```ravel
Named ::= class { init = () => { this; } }
print (Named.name)      # Named

Anon := class { init = () => { this; } }
print (Anon.name)       # class —— 没名字,读到的是显示名
print (string (Anon))   # class
```

`::=` 定义变量、顺手把变量名写进那个类对象；`:=` 建的类**没有名字**，
显示和报错时退化成 `class`（`Anon.name` 读到的就是这个显示名；真改名要写
`Anon.name = "Foo"`，那是往成员里写）。内置类型都自带名字（`int.name` → `"Integer"`）。

### 7.6 字段与修饰符

字段用 `:=` 定义、用 `=` 改值。对象建好之后**也能加字段**——`obj.field := v` 是定义语句的
成员版：字段不存在就新建，存在就整条替换（类型约束随之更新）。

```ravel
P ::= class {
    init = () => { this; }
    age: int = 0
}
p := P ()
p.nickname := "Al"      # 新增字段
print (p.nickname)      # Al
p.age += 5              # 成员复合赋值,左边只求值一次
print (p.age)           # 5
```

`=` 只能改**已存在**的字段，给不存在的字段赋值会报「对象没有字段」。
新字段的类型约束来自值的类型，之后 `p.nickname = 5` 会报：

```
Error: 类型错误: 无法将 Integer 赋值给 'nickname' (声明为 String)
```

字段的复合赋值和变量一样好用：`+=` `-=` `*=` `/=` `%=`，左边只求值一次
（`(f ()).n += 1` 不会把 `f` 调两遍）；`by` 属性也支持（读走 getter、写走 setter）。
`??=` 同样只求值一次，而且**不空就一个字符都不碰**（连 setter 都不调，见 2.5）。

| 修饰符 | 作用 |
|--------|------|
| `public` | 外部可访问（默认）|
| `private` | 仅本对象内部可访问 |
| `protected` | 类内 + 子类实例可访问 |
| `readonly` | `=` 和 `:=` 都报错：「无法给只读变量赋值」/「无法重新定义只读变量」。块里的 `:=` 是另开局部变量（遮蔽），不受影响 |
| `unreadable` | 读取时报「变量 'x' 不可读取」|
| `outdated` | 读取时往 stderr 打一行 `[outdated] 'x' is deprecated` |
| `core` | 读写都需要先 `unsafe ()`，见「十一、常见陷阱」 |
| `by` | 属性（getter/setter），见 7.7 |

`lib/` 里的 API 都标了 `readonly`：`predefined.rav` 的语言级别名（`print` / `true` /
`if` / …）、`Math` 与 `Ex` 的函数。**状态**没标（`Ex.HandlerStack` / `ReferencesPath`）——
那些本来就该能改。

为什么连 `:=` 也挡：`:=` 换掉的是**整个 Variable**，attrs 跟着老的那个一起没 ——
不挡的话 `true = 1` 报错、`true := 1` 静默成功，同一个「只读」两条路两个答案。

**`override` / `new` 已删除**：它们从前被解析器接受、记进 attrs，而全库没有一处读它们
（语言里既没有重载也没有重定义检查），写上去等于没写。现在写出来会明确报
「'override' 修饰符已删除」。

**挂在类上的东西，实例看不到。** 类体里写的字段/方法在实例化时落进**每个实例自己的表**，
谁都能读；而事后往**类对象**上挂的（`C.func := …`）只进类自己那张表，实例一个都读不到：

```ravel
C ::= class { n: int = 7 }
c := C ()
C.func := (x: int) => { x + 1; }
print C.func 5      # 6      —— 类对象自己读得到
c.func 5            # ❌ 类型 'C' 没有方法 'func'
c.n                 # 7      —— 类体里的字段在实例自己身上
```

原因是**一个类对象有两张表**：上面那张「类自己的」（`name` / `parent` / `block` / 用户挂上去的
都在这儿），和一张「**给实例的**」（内置方法、类运算符、序列方法）。实例读的是后者，所以
类上挂的东西它看不见。想让它看得见，就写在类体里（那才是每个实例自己的成员）。

反方向的同一件事：内置的**实例方法**住在「给实例的那张」里，**类那一侧**就读不到 ——
从前那是个真崩（`list.Add 2` 读到的是一份没绑 `self` 的方法，调用时 C# 强转当场炸
「`!!` 解释器内部错误」）：

```ravel
l := [1 2 3]
l.Add 4             # ✓ 实例那一侧照旧
list.Add 2          # ❌ 类型 'Type' 没有方法 'Add'
list.Fields ()      # 也列不出 Add —— 列的就是这一侧读得到的
Json.FromString "1" # ✓ 类侧的 API 住在「类自己那张」里，照旧
```

自己写的 `private` 字段照旧只认「仅本对象内部」那条老规矩（内置方法不带 `private`）。
用例见 `tests/242`。

**`init` 不在表里**——它只是构造器的名字，不是修饰符（见 7.2）。

```ravel
C ::= class {
    init = () => { _age = 0; this; }
    public name: string = "n"
    private _age: int = 0
    readonly id: int = 1
}
```

### 7.7 by 属性

```ravel
Person ::= class {
    init = () => { _name = ""; this; }
    _name: string = ""
    by name := property (() => { _name; }) ((v: string) => { _name = v; })
}
p := Person ()
p.name = "Alice"    # setter
print (p.name)      # getter → "Alice"
```

`property getter setter` 两个参数都是函数。

`by` 和 `property` 是**两半**：`by` 是那个标记，值得由 `property` 造出来 —— **或者 `default`**
（见下）。别的值（比如 `by v := 5`）读写都当场报同一句话 —— 从前裸读会把那个值悄悄交出去、
裸写什么都不发生：

```ravel
by v := 5
v          # Error: 'v' 标了 by，但它的值不是 property（Integer 上没有 getter）
v = 1      # Error: 'v' 标了 by，但它的值不是 property（Integer 上没有 setter）
```

### 属性的默认值（`= default`）

`default` 是这类值的"空"，和 `int default` 给 `0`、`function default` 给空函数一个道理：
**属性的默认值**就是一对什么都不做的函数

```
Get = () => { (); }
Set = (_ : object) => { (); }
```

于是 `by a: int = default` 得到一个**能读能写、只是都不做事**的属性：读出来是 `()`、
写进去丢掉（不报错）。这正好是"先声明、之后再装实现"要的形状：

```ravel
Trait ::= class {
    init = () => { 0; this; }
    by a: int = default          # 先给个空实现
}
t := Trait ()
print (t.a)                      # ()
t.a = 1                          # 丢掉了（不报错）
by t.a = property (() => { 100; }) ((v: int) => { print "收到"; })   # 换上真实现
print (t.a)                      # 100
```

⚠️ **写进去是静默丢掉的**：默认实现什么都不做，所以别指望它留下了什么。
它是"还没有实现"的占位，不是带初值的自动属性（Ravel 没有后者 —— 想要存值就自己写
`_a` + `property (() => { _a; }) ((v) => { _a = v; })`）。

它没有状态，所以 `Copy ()` 让副本和原件共享同一个也无害。

**可以标类型**：`by n: int = property g s` —— 注解管的是**写进来的值**（和普通字段一个
道理：Ravel 只约束赋值，从不检查某个函数返回什么）。所以 getter 返回什么没人管，
写错类型当场报错：

```ravel
by n: int = property (() => { _n; }) ((q: int) => { _n = q; })
c.n = 7         # ok
c.n = "str"     # Error: 类型错误: 无法将 String 赋值给 'n' (声明为 Integer)
```

不标类型就是 `Any`，写什么都行：

```ravel
by name: string = property g s     # 标类型
by name := property g s            # 不标（约束是 Any）
```

### 取/换槽里的 property（`by a` 与 `by a = X`）

四件事分清楚：

```ravel
by n := property g s     # 定义**槽**：n 由此成为一个属性
n = 1                    # 给属性赋值：过 setter
by n = property g2 s2    # 换掉**槽里的那份 property**：不过旧 setter
p := by n                # 取出**槽里的那份 property 本身**：不过 getter
```

后两个都是"**属性本身**"，所以都绕开那半边：写那边不走 setter，读这边不走 getter
（`p := by n` 一次 getter 都不会调 —— tests/138 用计数钉着）。`by` 标记留着，
所以换完槽之后 `n` 照样走**新的** getter/setter：

```ravel
s.swap ()       # 里面是 by n = property (() => { _n * 100; }) ((q: int) => { … })
print (s.n)     # 走新的 getter
s.n = 7         # 走新的 setter（旧的那个一次都没跑）
```

`by a` / `by a.x` 拿出来的是个**普通值**，所以存得进表、传得出去，也能自己调：

```ravel
p := by s.n          # <property>
print (p.Get ())     # 自己过 getter
p.Set 9              # 自己过 setter
Bag.Add (by s.n)     # 当值传给别处
```

两个方向都认**同一个"槽路径"**：名字（自己身上）或 `对象.成员`（别人身上）——
所以能给别的对象换槽、也能在别人身上**建**槽：

```ravel
by o.q = property (() => { 1000; }) ((v: int) => { … })    # 换:o 身上那个槽得已经在
by o.q := property (() => { 42; }) ((v: int) => { … })     # 建:没有也行
```

`:=` 定义 / `=` 换，和语言里别处一个规矩（`obj.a := v` 定义、`obj.a = v` 赋值）——
只是这儿定义的是"一个属性槽"。所以在别人身上装属性：

```ravel
install := (o: object) => {
    by o.nick := property (() => { _nick; }) ((v: string) => { _nick = v; })
}
install someone
someone.nick = "Bob"      # 走装上去的那个 setter
```

两条护栏：目标必须**已经是个 by 属性**（`by plain` / `by plain = 2` 用在普通变量上报
「'plain' 不是 by 属性」）；`by a.x` **取**值那种只在表达式里写（语句开头只有换槽的写法，
`by a.x` 单独一行没有落脚点）。

**`readonly` 对 by 槽一样管**：`readonly by r := property g s` 之后，`r = 1`、`r += 1`、
`by r = …` 三条路都报「无法给只读变量 'r' 赋值」（赋值走的是 setter，不经过变量那条路，
所以这三处各自问了一次）。

⚠️ `by` 声明必须写在**类体一级**，不能写在 `init` 里面——`init` 是个 lambda，
它的块有自己的局部作用域，写在里面的 `by name := ...` 挂不到对象上，`p.name` 只会报

```
Error: 类型 'Person' 没有方法 'name'
```

`init` 只负责给 `_name` 赋初值。

### 7.8 with（进到对象的成员表里跑一段）

```ravel
Person ::= class {
    init = () => { _name = ""; this; }
    _name: string = ""
    by name := property (() => { _name; }) ((v: string) => { _name = v; })
}
p := Person ()
p.name = "Alice"
p2 := with p { name = "Bob"; }
print (p2.name)     # Bob
print (p.name)      # Bob —— **就是同一个对象**,`with` 不拷
```

`with (对象: 块)` 做且只做一件事:**换一下"接下来这段代码算谁的成员"**。

- 块里的**赋值**(`name = "Bob"`)沿作用域链落回原对象的字段 —— 改的就是它本身;
- 块里的**定义**(`tmp := 1`)**也落在那个对象上** —— 块结束也不没,它成了对象的一个字段;
- 交回的是那个对象**本身**(所以上面 `p2` 和 `p` 是同一个东西)。

（"定义留下"这条是 `enum` 那种元类要的：拿一个普通实例当**落脚点**，把一块里的名字收出来。
要临时的名字就自己开个作用域 —— 拿个一次性的实例当落脚点。同一个对象上分两次跑同一块、
里面又有 `:=`,第二次会撞"`:=` 是定义不是覆盖"。）

**要副本请明说**:

```ravel
p3 := p.Copy ()            # 浅拷贝一份
p3.name = "Carol"
print (p.name)             # Bob —— 原件不动
```

（从前 `with` 自带一份浅拷贝、交回副本;那既是每用一次整份拷一次的开销,
又让"改副本、原件不动"这件事藏在括号里 —— 现在它归 `Copy ()` 明着说。）

### 7.9 object 方法

```ravel
Person ::= class {
    init = () => { this; }
    name: string = ""
    age: int = 0
}
p := Person ()
p.Copy ()             # 浅拷贝
p.ToString ()         # 字符串表示
print (p.Fields ())   # [init name age ToString Copy Fields]
```

`p.Fields ()` 列的**就是这个值的作用域里有哪些成员**：字段和方法一视同仁
（`name` 和 `init` 都在里面——它们确实是成员，读得到也调得动），
然后再并上类型自己的方法（沿继承链到 `object`）。
模块（`ravel`/`using` 建立的）的成员就是它作用域里的变量，排在最前。
只排掉 `this`：它是这个值自己，不是成员。

**列的永远是"这一侧读得到的"**：从一个**类对象**上问（`list.Fields ()` / `Json.Fields ()`），
里面就没有那些内置**实例方法**（`Add`/`Map`/`Kind`…）——它们住在类对象那张「给实例的表」里，
类那一侧读不到（`list.Add 2` 报「没有方法」），列出来等于骗人；
它们在**实例**那一侧列（`[1 2].Fields ()` 仍是整串）。

`print p` 是另一回事——它是**数据快照**，方法不出现在里面（`class { name = ..., age = ... }`）。

### 7.10 元类

**父类是 `type` 的类就是元类**：它继承了 `type` 那层「建类」的 init，
所以它建出来的东西是**类**，不是实例。

```ravel
MyMeta ::= class type {
    private parentInit := init;          # 覆盖之前先把继承来的那层存下来
    init = (parent: type body: function) => {
        parentInit parent body
    } | (body: function) => {
        parentInit object body
    }
}
MyClass ::= MyMeta {
    init = () => { 0; this; }
    x: int = 42
}
print (typeof MyMeta)         # Type   —— 它自己由 class 建
print (typeof MyClass)        # MyMeta —— 它由 MyMeta 建
print ((MyClass ()).x)        # 42
```

- **不需要 `base`**：super 调用退化成「覆盖之前先把继承来的 `init` 取出来」，
  `private parentInit := init;` 就是这个惯用法。`parentInit parent body` 就是
  「走默认那套建类逻辑」，它返回建出来的那个类。
- **两分支 `|`** 让「带父类」和「不带父类」两种写法都能用，靠**参数类型**分流：
  `MyMeta Base { ... }` 走第一支，`MyMeta { ... }` 走第二支（不带父类时默认 `object`）。
  `Base` 可以是任何类对象，包括别处建的普通类。
- `init` 在类体里读到的是**继承来的那个**（预设类体先跑、把 `init` 落进同一个实例作用域），
  而 `init = ...` 改的是这个实例作用域的副本，`type` 本身不受影响。
- 元类的 `init` 同样**交出返回值**，所以最后一句得是 `parentInit ...` 或 `this`——
  以 `print` 收尾就会拿到 `()`（见 7.2）。
- **可以往类体上拼一块**：`parentInit parent (body.Append { … })` —— 拼上来的块和类体
  **同属一层**，实例化时接着类体跑，落在同一张实例表上，于是"注入几条成员"和用户
  自己写在那儿的没有区别。`Prepend` 也行（排在类体**前面**，那时字段还没声明）。
  作用域照**类体**的写法处解析（和"每层各按各的写法处"一个道理）。
  `lib/dataclass.rav` 就是靠这个把 `Text` / `==` / 按位置构造注入进去的。

在 `init` 里挂钩建类过程（`tests/98_metaclass_print.rav`）：

```ravel
LoggingMeta ::= class type {
    private parentInit := init;
    init = (parent: type body: function) => {
        print "meta: creating..."
        r := parentInit parent body
        print "meta: done"
        r
    } | (body: function) => {
        print "meta: creating..."
        r := parentInit object body
        print "meta: done"
        r
    }
}
M2 ::= LoggingMeta object {
    init = () => { 0; this; }
    name: string = "hello"
}
m := M2 ()
print (m.name)
# meta: creating...
# meta: done
# hello
```

`class class { ... }` 是同一件事的简写（第二个 `class` 是父类）：

```ravel
M ::= class class { x: int = 1; }
C := M { init = () => { 0; this; }; y: int = 2 }
print (typeof C)      # M
print ((C ()).y)      # 2
```

typeof 链（`MyMeta` 沿用上面的）：

```
typeof 实例   →  它的类
typeof 类     →  建它的那个类(元类)
typeof type   →  type 自己(自指,链的起点)
```

```ravel
A ::= MyMeta object { init = () => { 0; this; }; }
B ::= class { init = () => { 0; this; }; }
print (typeof A)          # MyMeta
print (typeof B)          # Type
print (typeof MyMeta)     # Type
print (typeof (A ()))     # A
print (typeof (B ()))     # B
```

最后一条护栏：**没写 `init` 的类不会被当成类再建一次**。引擎找构造器只看实例作用域那一层
（不走原型链、也不问元类），所以 `Bare ()` 拿到的是 `object` 那层给的默认构造器建出的
**普通实例**，不会掉进 `type` 那层的建类逻辑（那层是建**类**用的）。

### 7.11 接口与实现（interface / use）

接口是**一套具名的槽**；实现是**把它们接到某个类上**的一段代码。接口不是类树上的父类 ——
它是一层"套在实例上的视图"：

```ravel
myTrait ::= interface {
    by a : int = default
    by b : function = default
}

myClass ::= class {
    x : int = 0
}

myImplement := myTrait myClass {
    by a = property (() => { instance.x; }) ((v: int) => { instance.x = v; })
    by b = property (() => { () => { print "b!"; } }) ((v: function) => { (); })
}

use myImplement          # 只在这个作用域里生效

u := myClass ()
print u.a                # 0
u.a = 1
print u.x                # 1
u.b ()                   # b!
print (u is myTrait)     # true
```

三件事：

1. **`interface { … }` 造出一个类型**（接口本身）。它的 **parent 是 `BaseInterface`**（所有接口的
   基类，而它**自己也是个接口**，是这一支的根）、而**类型是 `interface`**：`myTrait.parent`
   是 `BaseInterface`，而 `typeof myTrait` 是 `Interface`（`myTrait is interface` 成立，
   类型树上也这么标）。基类类体里那份 `init` 正是"造实现"跑得起来的原因
   （`myTrait myClass { … }` 是**实例化 `myTrait`**，得有 `init` 才动得了）—— 它和
   `interface { … }` 用的那份（挂在工厂 `Interface` 上）是**两份不同的 init**，各干各的。
2. **`myTrait myClass { … }` 造一个实现**。块里用 `by a = property …` 把接口声明的槽**换掉**
   （所以写 `=`，不是 `:=`）；块里有个 `instance`，就是"这一次在服务谁"（随调用走，见下）。
3. **`use impl` 把它登记在当前位置的作用域里**。从此这里 `u.a` / `u.b` 都走实现那条槽；
   `myClass` 本身一个字没动 —— 换个作用域写 `myClass ()` 照旧没有 a/b。

#### 槽住在实现里

实现块在**它自己的作用域**里跑一遍：接口声明的槽先摆好，再由实现块把它们换掉。那些槽就住在
实现身上，实例身上没有 —— 所以 `u.Fields ()` 里不列 a/b。

读 `u.a` 的时候，引擎把那条槽**绑到这一次的接收者** `u` 上再交出去，getter/setter 里那个 `instance`
就是 `u`：

```ravel
v := myClass ()
v.a = 99
print u.a      # 1 —— 还是 u 自己的
print v.a      # 99
```

**`instance` 是"这一次调用"的事**：它不落在实现身上，而是每次分发临场绑一层。所以留存下来的东西
永远指它自己那个实例：

```ravel
Late ::= interface {
    by peek : function = default
}
lateImpl := Late myClass {
    by peek = property (() => { () => { instance.x; }; }) ((v: function) => { (); })
}
use lateImpl

mine := myClass ()
mine.x = 5
peek := mine.peek       # 这个闭包属于 mine
u.a = 9                 # 中间服务过别的实例
print (peek ())         # 5 —— 还是 mine 的，不会跟着漂
```

一个实现也就能**同时**服务多个实例（`u` 的槽体里去读 `v` 的槽，回来读 `instance` 仍是 `u`）。
`instance` 只在实现体的槽里有效：`by u.a` 取出来的那份槽没有"这一次"可言，事后 `.Get ()` 用它会
报「此刻没有正在被服务的实例」——**响亮**，不给一个陈旧的值。

#### 作用域就是它生效的范围

`use` 登记在**它所在的那个作用域**上,管的是"词法上在这个作用域里的代码"。上面那段是在顶层
`use` 的,于是整份文件（包括里面定义的函数）都算"里面"。把 `use` 挪进函数体，就只在那次调用里
算数（下面这一段是独立的，没有顶层那句）：

```ravel
scoped := () => {
    use myImplement
    inner := myClass ()
    print inner.a           # 0 —— 这个函数里 use 过
}
scoped ()

try { w := myClass (); print w.a; } (e: Exception) => { print (string e); }
# 类型 'myClass' 没有方法 'a' —— 外面没 use 过
```

要**处处生效**就用 `impl`（它登记在全局作用域上，任何作用域都看得到）：

```ravel
globalize := () => {
    impl myImplement        # 写在函数里，但生效范围是全局
}
globalize ()
print ((myClass ()).a)      # 0 —— 函数外面也生效
myImplement.Dispose ()      # 取消和 use 一样
```

（顶层写 `use` 和写 `impl` 是一回事 —— 顶层那个作用域就是全局。差别只在函数体、模块里写的时候。）

`Dispose ()` 提前取消（在哪个作用域调都一样，取消的是这个实现）；已经造出来的实例不残留什么
（槽从来没写到实例上过）。取消了之后想再用，`use` 一次就行：

```ravel
myImplement.Dispose ()
# …这里 u.a 又报没有方法了…
use myImplement          # 在你 use 的这个作用域里重新生效
```

#### 注解也认接口

判定和类型检查用的是同一个判据，所以在实现生效期间，接口可以当注解使：

```ravel
typed : myTrait = u            # 可以（实现不在作用域里时，这一行会报类型不匹配）
print typed.a                  # 0

take := (v: myTrait) => { v.a; }
print (take u)                 # 0 —— 参数注解同理
print (take (myClass ()))      # 0 —— 接口是"视图"：实现生效期间任何 myClass 实例都算
```

`myImplement` 自己反过来天然成立：`myImplement is myTrait` 为真（它的类型就是那个接口）。
`Dispose ()` 之后的赋值照旧报「无法将 myClass 赋值给 myTrait」—— 和 `u.a` 一起失效。

`<:` / `:>` 问的是**类型之间**的关系，而"这个类在当前作用域里算不算那个接口"这件事要靠实例才能问
（`u is myTrait`）—— 所以 `(typeof x) <: myTrait` 是 false，而 `x is myTrait` 是 true。
反过来 `myClass <: myTrait`（左边给类型）**是**认的：实现生效期间它成立，`Dispose` 之后又不成立。

#### 叠几个实现

同一个作用域里 `use` 两次：**后 use 的先试**，它没有那个名字时再回头试前一个。
接口里写了 `= default` 而实现没填的槽，就是那个"什么都不做"的默认属性 —— 读出来是 `()`。

#### 库里的例子：`IEnumerable`

库里已经有两个:`INumber`（最简单的——**一个槽都没有**,只是"这个类型是数"的标记:
五种数值类型各 `impl` 一条,于是 `(x: INumber)` 收得下 `int 5` 也收得下 `float 5.0`,
`lib/math.rav` 那四个函数就标的它）、以及 `IEnumerable` / `IEnumerator`（见 4.3 与 6.4）：
`IEnumerable` 声明 `by GetEnumerator`（外加一整套**默认实现**，见下）、`IEnumerator` 只声明
`by MoveNext` / `by Current`，三种容器各 `impl` 一条，于是 `foreach` 能遍历它们、
`(xs: IEnumerable) => …` 收得下它们。

#### 接口体里也能写实现（默认实现）

接口的**类体**会在**每个实现对象上跑一遍**（造实现就是实例化那个接口），所以写在接口体里的
`by X := property …` 就是"实现体不填就用它" —— 实现块里写 `by X = …` 能盖掉它
（`=` 是换槽、`:=` 是定义）：

```ravel
IThing ::= interface {
    by Name : string = default                              # 要填的
    by Loud := property (() => {                            # 默认实现
        () => { (instance.Name).ToUpper (); }
    }) ((v: function) => { (); })
}

C ::= class { n: string = "hi" }
impl (IThing C {
    by Name = property (() => { instance.n; }) ((v: string) => { (); })
})
print ((C ()).Loud ())        # HI
```

于是"一个接口管一类形状、顺手把那类形状上的通用操作也带来"这件事就落地了 ——
`IEnumerable` 就是这么给 `Generator`、字符串、用户类带上整套 `Map` / `Where` / `Count` 的
（见 4.3）。两条要注意：

- **实现体要写在 getter 里面**（`property (() => { <这里> }) (…)`）：接收者 `instance` 是
  每次分发才绑的，闭包得在那时候创建才认得出它。
- 类链上已经有同名成员时**它先命中**（接口那份只补"本来要报没有方法"的），所以容器上
  那几个同名方法是引擎那份。

#### 接口也能继承接口（外加一串要求）

```ravel
myTrait ::= interface {
    by a : int = default
}

supTrait ::= interface myTrait {                 # 一个父
    by c : int = default
}

masterTrait ::= interface supTrait [IEnumerable] {   # 父 + 要求；`{ () }` 是空体
    ()
}
```

- **父是继承**：`masterTrait` 的槽是 `a`/`c`（父的 + 自己的；同名以**自己写的**为准），
  `masterTrait <: supTrait`、`<: myTrait` 都成立；实现了子接口也就实现了父接口
  （`is` / 注解 / `foreach` / 两个查询全认）。
- **要求是前置条件**：实现 `masterTrait` 的类必须**已经**有 `IEnumerable` 的实现
  —— 槽**不**并进来、`<: IEnumerable` 也**不**成立，只在造实现那一步查有没有：

  ```ravel
  masterTrait 某个类 { … }
  # masterTrait 要求 某个类 已经实现了 IEnumerable（先给它 impl/use 一条）
  ```

  （写 `use` 还是 `impl` 由你：要求查的是"当下这个作用域里有没有生效中的实现"。）
- 要求得**跟在父后面**：光写 `interface [IEnumerable] { … }`（没父）不收。

#### 槽里也能放运算符

接口声明一条 `by + := property g s`,实现这个接口的类就有了那个运算符:

```ravel
T ::= interface {
    by a : int = default
    by + := property (() => { (o: object) => { instance.a + 100; }; }) ((v: function) => { (); })
}
C ::= class { x : int = 0 }

use (T C {
    by a = property (() => { instance.x; }) ((v: int) => { instance.x = v; })
    by + = property (() => { (o: object) => { instance.x * 2; }; }) ((v: function) => { (); })
})

u := C ()
u.a = 5
print (u + 1)     # 10 —— 实现里换掉的那条
print (u.+ 7)     # 10 —— 节形式一样走槽
u += 3            # 复合赋值:算子交回同类型的东西才写得回去
```

- 那一格的**值是 property**,所以"用"它是**两级**:读槽(走 getter)拿到运算符函数,再用它收右操作数;
- 实现在块里用 `by + = …` **换掉**它(和普通槽一个写法);类体里直接写 `by * := property …` 也行(不经接口);
- **看得见的东西不一样**:trait 那边是 `instance`,类体里自己那条是 `this`;
- 和槽一样**随作用域在/不在**:出了 `use` 那个作用域,`u + 1` 又回到「类型 C 不支持运算符 '+'」;
- 只有默认值(`by + := default`)时是那对什么都不做的 getter/setter,用起来报「值 () 不是函数」——响亮,不静默。

(裸的 `+ := f` 仍然是**类运算符**那条路,不受这里影响。)

#### 查一个类型现在实现了什么

`T.GetImplements ()`（`Type` 上的方法）给出一份**当下**的快照 —— 接口对象组成的 list：

```ravel
list.GetImplements ()      # [IEnumerable] —— 库加载时就登记了，处处生效
int.GetImplements ()       # []
use (IEnumerable MyThing { by GetEnumerator = property … })
MyThing.GetImplements ()   # [IEnumerable]
```

反过来问一个接口有 `I.GetImplementors ()`：

```ravel
IEnumerable.GetImplementors ()     # [Seq Dict Set List] —— 库里那三条 + 上面 use 的 Seq
MyIface.GetImplementors ()         # [MyThing]
```

两边都是一份"当下"的快照，同一项只列一次，顺序照查找来（由内到外、后 `use` 的先）——
所以 `GetImplements ()` 打头的是**当下生效**的那个。出了那个作用域、或者 `Dispose ()`
之后再问就没了 —— 接口是"在这个作用域里生效"的东西，不是一个烙在类型上的标记。
`GetImplements` 里子类也算数（`实例 is 接口` 的判据本来就把子类收进来了）；
`GetImplementors` 里普通类永远是空的（没人拿它当接口）。

---

## 八、模块

### 8.1 创建模块

```ravel
ravel "MyMath"
pi := 3.14
ravel ""
print (MyMath.Pi)
```

### 8.2 导入文件

```ravel
ReferencesPath = ["/path/to/libs/"]
using "other.rav"
```

`using` 只加载一次，循环引用报错。

### 8.3 System 模块

内置模块，解释器启动时创建。PascalCase 类型名在此：

```ravel
System.Integer  System.String  System.Bool
System.WriteLine "hello"
System.ReadLine ()
```

小写别名在 `predefined.rav` 中定义。

类型名在 `predefined.rav` 里都有小写别名（`int` / `string` / `object` …），
`Ravel` 是**所有模块的类对象** —— 模块是它的实例、不是子类，所以：

```ravel
using "math.rav"
print (Math is Ravel)      # true —— 这就是"它是不是个模块"
print (5 is Ravel)         # false
```

`System` 里装的是**语言本身要的**(类型、控制流、`eval`、反射)和**进程边界**
(输出、文件、环境变量、子进程、时间) —— 一句话,是"这个语言"和"这台机器",不是"某个库"。
那六个库的本机半边(`Hash` / `Crypto` / `Http` / `Regex` / `Sqlite` / `Random`)不在这儿,
在官方扩展的 `Native` 模块里(见 6.19)。

### 8.4 Math 模块

函数体是 C# 造的（落到 `System.Math` 上），但要**显式引用**才有 ——
和 6.19 那些一样，它住在官方扩展里（`Ravel.Extensions/MathNative.cs`）：

```ravel
using "math.rav"
Math.Pi            # 3.141592653589793
Math.Sin 0         # 0（三角函数收弧度）
Math.Sqrt 16       # 4
Math.Round 2.5     # 3（四舍五入，不是银行家舍入）
Math.Abs (-5)      # 5（还是 int —— 保型的几个交回原始实参）
Math.Clamp 15 [0..10] # 10
```

常量 `pi` / `e` / `tau`，其余见「十、内置函数速查」。`Math` 收任何数值
（int/float/bigint/fraction），内部按 double 算。

`lib/math.rav` 另外补了 Ravel 能表达的几个（同一次 `using` 一起到位）：

```ravel
Math.Square 5      # 25
Math.Deg Math.Pi   # 180（角度↔弧度）
```

`using "math.rav"` 之前 `Math` 不是一个名字 —— 会报「未定义的变量 'Math'」。

### 8.5 命令行参数与环境变量

两样都是**进程外面给进来**的东西，都在 `System` 里，和文件那批一个规矩：只做 syscall，
要不要先问一句由你自己决定。

```bash
dotnet out/ravel.dll greet.rav 小明 --loud
```

```ravel
System.Args ()            # [小明 --loud] —— **脚本名之后**那些
```

`Args ()` 给的是一个 `list`（全是 `string`）。**解释器自己不读命令行**：谁把脚本跑起来的、
命令行长什么样，那是 CLI 的事，所以 REPL 和 `ravel test` 里它一直是空的 `[]`。

环境变量五条：

```ravel
System.Env "HOME"                  # 没有这个变量就报错
System.EnvOr "HOME" "/tmp"         # 没有就给默认值（不报错的那条）
System.SetEnv "MODE" "dev"         # 只动**本进程**那份（新起的子进程看得见）
System.UnsetEnv "MODE"             # 删掉；删一个本来就没有的不算错
System.EnvAll ()                   # 名字 → 值（按名字排序，输出才可期待）
```

- 空串等于删掉（.NET 那套 `SetEnvironmentVariable` 的规矩）：`SetEnv "X" ""` 之后
  `Env "X"` 照样报错。
- 变量名空着当场拦下，不会静默变成一次什么都没做的调用。
- 只写**本进程**那份。`.NET` 还能写用户级/机器级的环境变量，那是"装环境"，不该由一句赋值顺手做掉。

### 8.6 给自己的库写断言（`Test`）

`Test` 是给"写在 `.rav` 里、想跟着程序一起跑"的检查用的（仓库自己的用例是另一套：
一对 golden 文件，见 CONTEXT.md）。**要显式引用**：

```ravel
using "test.rav"

Test.Check "除以零要报错" { 1 / 0; }              # 跑完不报错就算过
Test.Equal "摊平一层" (Seqs.Flatten [[1 2] [3]]) [1 2 3]   # 显示形式一样就算过
Test.Fails "除零确实报错" { 1 / 0; }              # 反过来：必须报错才算过
Test.Report ()                                    # 汇总；有没过就抛出去
```

三条断言各自收一段**会跑出结果的东西**（块、lambda 都行）。报错在这儿是**值**——
`Expect argCount f` 把一次调用包成 `Expected`（见 5.8），于是"怎么断言"和"报错怎么办"
是同一件事，不用另学一套。`Check` 顺便把"过没过"当值交回，`if` 里能直接用。

逐条输出按 TAP 那个样子，一行一条、过与不过都打，下面缩进的是原因：

```
ok 1 - 除以零要报错
not ok 2 - 这条也没过
    期望 [1 3]，得到 [1 2]
```

`Report ()` 打一行 `5 passed, 3 failed`，**有没过就抛出去**——CLI 于是以非零码结束，
所以 `dotnet out/ravel.dll mytests.rav` 可以直接当 CI 的一道关卡用（不想让它抛就别调它，
逐条的输出早就打完了）。每条的记录也留着：`Test.Results`（`{name, ok, msg}` 的 list）、
`Test.PassCount` / `Test.FailCount`。

**用例见 tests/250。**

---

## 九、异常

`Ex` 是**自带的**（`predefined.rav` 末尾那段 `ravel "Ex"` 建的），不用 `using`，
而且有小写别名 `try` / `throw`（`Ex.Try` / `Ex.Throw` 一样能用）：

```ravel
try {
    throw (Exception "oops")
} (e: Exception) => {
    print "caught"        # caught
}

print (try { 2 + 3; } (e: Exception) => { 0; })    # 5 —— 没出错时值就是体的值

# 未捕获
exit "fatal error"
```

**`Exception` 就是个普通类**（实例有 `Message` 字段，`string e` 打出来就是它）。所以你可以
继承它、按类型接、把消息读出来：

```ravel
IoErr ::= class Exception {                    # 不写 init 也行:Message 照旧落在这个实例上
    Path: string = ""
    init = (msg: string path: string) => { Path = path; Message = msg; this; }
}

handle := ((e: IoErr) => { print ("io: " + e.Message + " @ " + e.Path); }) | ((e: Exception) => { print ("base: " + e.Message); })

try { throw (IoErr "no file" "/tmp/x"); } handle      # io: no file @ /tmp/x
try { throw (Exception "plain"); } handle             # base: plain
```

几个 handler 用 `|` 拼起来就**按参数类型分派**（和 `|` 造自定义函数是同一套机制）——
挑不上第一个就试下一个，都不收才报错。两条写法上的讲究：

- **每个 handler 各自加括号**：`|` 比应用松，不括的话第一个 handler 会被 `try` 先吃掉；
- **`|` 两边得在同一行** —— 整条交替写一行里，换行它就断了（先把 handler 绑个名字也一样，能"随便换行"的是**外面**那句 `try … handle`）。

`e.Message` 可读可写 ✓（`init` 里想改就 `Message = msg` ✓）；`e == 别的异常` 按**身份**比
（要问内容就比 `e.Message`）。不写 `init` 的异常子类也有 `Message`（它继承基类那条构造器 ✓）。

**引擎报的错自带分类** —— `Exception` 底下挂着一族（在 **C# 侧**建，见
`Runtime/BuiltinClasses.Errors.cs`），报错那一刻由引擎挑：

| 类 | 什么时候报 |
|---|---|
| `TypeError` | 类型不对：运算符不吃这个操作数、赋值类型不符、转换不了、构造不了 |
| `NameError` | 未定义的变量 |
| `AttributeError` | 对象没有这个字段 / 类型没有这个方法 |
| `IndexError` | 下标越界、空集合取 `First` |
| `KeyError` | 键不存在、环境变量没有 |
| `ZeroDivisionError` | 除零 |
| `AssertionError` | `assert` 不成立 |
| `AccessError` | 读写被挡：只读 / 私有 / 受保护 / 核心字段 |
| `ArgumentError` | 实参的形状不对（要代码块、要字符串、要类型对象…） |
| `ValueError` | 值本身不对：数值超范围、解析不动、JSON 转不了 |
| `IoError` | 文件 / 目录 / 命令那批 |
| `RegexError` | 正则：模式写错、匹配超时 |

```ravel
try { 1 + "a"; } (e: TypeError) => { print "类型错: " + e.Message; }
try { nope; } (e: NameError) => { print "名字找不到"; }

# 按类型分派:挑不上就试下一个
handle := ((e: TypeError) => { …; }) | ((e: Exception) => { …; })
```

库那一半也对齐了这一族：取不到第 n 个（`First` / `Last` / `At` / 空集合）是 `IndexError`，
`Option.Value ()` 在 `None` 上是 `ValueError` —— 同一个"取不到"，不管它是容器方法还是库方法，
接住的都是同一类。

它们**都是普通类**：`try (e: Exception)` 照旧接得住全部（老的写法一个都没坏），
可以继承（`MyErr ::= class TypeError { 0; }`）、可以自己 `throw (TypeError "…")`。
没归类的错误落基类 `Exception`。**用例见 tests/259。**

### 9.1 报错长什么样

运行时错误会带**位置**和**调用栈**，跨文件时也能看出是哪一层、在哪个文件。
`tests/152_error_report.rav` 的全文与输出：

```ravel
helper := (n: int) => { n + missing; }
helper 1
```

```
Error: 未定义的变量 'missing'
  --> tests/152_error_report.rav:4:29
  4 | helper := (n: int) => { n + missing; }
    |                             ^
  调用栈 (2 层):
    在 tests/152_error_report.rav:4:20
    在 tests/152_error_report.rav:1:1
```

插入符指向出错的那个表达式；调用栈每层是该函数**定义处**的位置
（Ravel 的帧链就是调用栈，所以这个信息是白捡的）。
路径取相对 cwd、分隔符统一成 `/`，所以期望输出跨平台一致。

**语法/词法错误走同一套渲染**，只是没有调用栈（源码根本没解析成功）。`tests/01_core.rav`
里那条 `eval "$ 1"`：

```
Error: '$' 只用在字符串里的插值 `${…}`（要把右边封成一个实参,用 '<|'）
  --> <repl>:1:1
  1 | $ 1
    | ^
```

词法错误也一样。`$` 这一条是特意写清楚的：它在代码里没有意义（只在字符串里当插值），
直说"该改用什么"比"未预期的字符"有用 —— 和 `?` 那条一个路子。

---

## 十、内置函数速查

| 函数 | 说明 |
|------|------|
| `print x` | 输出 x 并换行 |
| `input ()` | 读一行 |
| `typeof x` | 返回 x 的类型 |
| `f.Body ()` | 函数的体（Block；没有体的给空块） |
| `f.Scope ()` | 捕获作用域（Scope；类对象没有，给空 Scope） |
| `NaN` `Inf` | 特殊浮点值（和 `true` 同款：System 里的值 + 全局别名） |
| `x is T` | 类型判定（`isnot` 取反；`x.is` / `is.T` 也成立） |
| `exit msg` | 退出程序 |
| `eval "code"` | 执行字符串 |
| `Io.File p` / `Io.Dir p` | 文件系统条目（`lib/io.rav`，见 5.11） |
| `System.Args ()` | 脚本名之后的命令行参数（list，见 8.5） |
| `System.WarnForgotCall b` | 开关：「是不是忘了调用?」的提醒（见 11 章那条坑） |
| `System.Env n` / `EnvOr n d` | 环境变量（`SetEnv` / `UnsetEnv` / `EnvAll` 见 8.5） |
| `f <| a b` / `x |> .g ()` | 一个封右、一个封左：`<|` 把右边整个当一个实参，`|>` 把左边封口让成员接着挂 |
| `callcc fn` | 续延（拿到的类型是 `Continuation`，见 4.5） |
| `with obj { }` | 进到 obj 的成员表里跑一段（**不拷**、**不推层**：块里的 `:=` 留在 obj 上；要副本用 `Copy ()`）|
| `o.MemberScope ()` | 这个对象的**成员表**（一个 `Scope`）—— 按动态名字读写成员时用它 |
| `assert cond msg` | 断言（**两个实参一次写完、别跨行**，见 11.x 那条）|
| `use impl` | 在**当前作用域**启用一个接口实现（`实现.Dispose ()` 取消；见 7.11） |
| `impl 实现` | 同上，但**全局**生效（登记在全局作用域上） |
| `try { … } handler` | 捕获异常（`throw e` 抛出；`Ex.Try` / `Ex.Throw` 是同一样东西，见第九章） |

### Math（`using "math.rav"` 之后可用）

| 函数 | 说明 |
|------|------|
| `Math.Pi` `Math.E` `Math.Tau` | 常量 |
| `Math.Sin x` `cos` `tan` `asin` `acos` `atan` | 三角（弧度） |
| `Math.Atan2 y x` | 两参数反正切 |
| `Math.Sinh` `cosh` `tanh` `asinh` `acosh` `atanh` | 双曲 |
| `Math.Sqrt` `cbrt` `exp` `log` `log2` `log10` | 幂与对数 |
| `Math.Pow x y` `Math.LogBase x b` `Math.Hypot x y` | 两参数 |
| `Math.Floor` `ceil` `trunc` `round` | 取整（`roundTo x n` 保留 n 位） |
| `Math.Abs` `sign` `min` `max` `clamp` | `min`/`max`/`clamp` 交回原始实参；`clamp` **收一个区间**：`Math.Clamp x [0..1]` |
| `Math.MinMagnitude` `maxMagnitude` `fma` | 按绝对值比 / `a*b+c` |

同一次 `using "math.rav"` 还带来 `square` `cube` `deg` `rad`。

## 十一、常见陷阱

### 单行代码块需要 `;`

单行的 `{ ... }` 不写 `;` 就会被当成 Set/Dict，不是代码块：

```ravel
if { x > 0; } { print 1; } { print 0; }   # ✅
if { x > 0 } { print 1 } { print 0 }       # ❌ → Set
X := { 1 2 }        # Set（不是块）
X := { "a": 1 }     # Dict（键是表达式）
```

**这条规则不看位置，lambda 体一样适用**：

```ravel
f := (x: int) => { x * 2; }    # ✅
g := (x: int) => { x * 2 }     # ❌ 报「lambda body 需要代码块」
```

多行的块不用 `;`——换行本身就是 Newline token。`;` 只是「单行里充当换行」。

### 实参吃到运算符为止（转换式调用要括实参）

调用比运算符松（3.7），所以 `string x + " 个"` 里的 `string` 把 `+ " 个"` 一起吃了：

```ravel
print ("共 " + string n + " 个")      # ❌ → string (n + " 个") → 类型错误
print ("共 " + string (n) + " 个")    # ✅
print ((typeof x) == int)            # ✅ —— 判断"是不是 int"，`typeof x == int` 不是
print ((xs.At (0)) + 1)              # ✅ —— 先算调用，再加一
print (xs.At (0) + 1)                # ❌ —— 现在是 `xs.At ((0) + 1)`：取第 1 个
print ((xs.Count ()) == 0)           # ✅ —— 拿调用结果去运算/比较，就把它括起来
```

同一条也解释了为什么 `(Some 5).Map f` 那对括号不能省 —— 或者写 `Some 5 |> .Map f`。

### `xs.Sort ()` 不是排自己

`Sort ()` **交回一份排好的新表**，原表一个元素都不动：

```ravel
xs := ["b" "a"]
xs.Sort ()         # ❌ 交回 [a b] 就丢了 —— 这一句等于什么都没做，xs 还是 [b a]
ys := xs.Sort ()   # ✅ 接住它
print ys           # [a b]
```

这门语言里**没有原地排序**（容器自己的顺序只有 `Add` / `Remove` 那几条改得动）。
所以 `names.Sort ()` 后面跟一句 `foreach names …` 是**静默的错**：Windows 上目录枚举
本来就近似有序，看着像对的，换台机器就露出随机顺序 —— 库里真栽过（`lib/io.rav` 的 `Dir.List`）。

### 链式调用需要临时变量

应用语法 `f a b` 的左结合会让「点号接在字面量后面」被吃掉：`s.At 0.Count ()`
解析成 `s.At (0.Count ())`，先算 `0.Count ()` 再报 `'Integer' 没有方法 'Count'`。

```ravel
s := [[1 2] [3]]
s.At 0.Count ()      # ❌ 报「'Integer' 没有方法 'Count'」

v := s.At 0          # ✅ 用临时变量断开
v.Count ()           # 2
```

`()` 也是个字面量，所以 `xs.GetEnumerator ().MoveNext ()` 是 `xs.GetEnumerator (().MoveNext ())` ——
报「'Void' 没有方法 'MoveNext'」。同样落到变量上（这就是 `foreach` 里为什么先 `e := …`）：

```ravel
e := xs.GetEnumerator ()     # ✅ 先拿到手
while { e.MoveNext (); } { f (e.Current); }
```

### `:=` vs `=` vs `::=`

```ravel
a := 42     # 定义（类型推断）
a: int = 42 # 定义（类型标注）
a = 42      # 赋值（变量已存在）
a ::= fn    # 定义+自动命名（仅函数/类）
```

### `if` 少给一块 = 那一块**永远不跑**

`if` 是**三参**库函数,柯里化到"收满三块"才算完。只给两块不是报错,是拿到一个**半成品** ——
那两块里的第二块**静默地什么都不做**:

```ravel
if { x > 0; } { print "正"; }            # ❌ 少 else:整句是个半成品,打印不执行
if { x > 0; } { print "正"; } { 0; }     # ✅ 三块给齐
(x > 0) { print "正"; } { 0; }           # ✅ 或者直接让 bool 吃两块(见 4.1)
```

同一个坑在 `assert` 上也踩过:`assert (条件)` 和消息**分行**写,消息就变成第二个实参,
这一句成了半成品 —— 断言等于没断(条件为假也不响)。**`assert (条件) "消息"` 写一行里**。

**怎么把这类坑揪出来**:开 `--warn`(`ravel --warn 脚本.rav`,或脚本里写
`System.WarnForgotCall true`)。一条语句的值是个函数、又**没被用掉**的时候它就提醒 ——
半成品(少给了实参)和"光写了个名字、压根没调用"都在内:

```
警告: 这一句的值是个函数，却没被调用 —— 是否遗忘了调用？ 它是 <function (e: function) => { cond: bool := c (); cond t e; } applied c=<block> t=<block>>
  --> app.rav:12:5
  12 |     if { x > 0; } { print "正"; }
     |     ^
```

它只管**不是最后一条**的语句(块的值就是最后一条 —— `() => { … }` 那种"交回一个函数"
是正常写法),同一个位置也只提醒一次(循环体里的那一句不会刷屏)。
细节见 CONTEXT 的「诊断」那一节。

### `??` / `??=` 只认 `None`

`0` / `""` / `[]` / `false` / `()` 都是**值**，不是空 —— `x ??= 默认` 在这些上面什么都不做：

```ravel
n: int = 0
n ??= 7              # 不写：0 是个值
print n              # 0

o: object = ()       # `()` 同样不是空（它是 Void 那个值）
o ??= 1              # 也不写
```

想"没设过才写"就用 `Cache: Option = None` 打底 —— `None` 是**唯一**的"空"。

还有一条：**`?.` 和 `.` 一个位置**（primary 之后、或 `|>` 之后）——
`.成员` 比并列的调用绑得紧，`f ().g ()` 要写 `f () |> .g ()`，`f ()?.g ()` 同理。

### `:=` 是定义不是覆盖（同一个作用域里同名会报错）

```ravel
x := 1
x := 2          # ❌ 'x' 在这个作用域里已经定义过
x = 2           # ✅ 改值
```

脚本、函数体、类体**一个规矩**。（不同作用域里同名 = 遮蔽，照旧允许。）

类体那条尤其要紧：各层类体**平铺进同一个实例作用域**，所以子类重声明父类已声明的字段
是**报错**而不是覆盖；构造器同理 —— `init := …` 会撞上从 `Object` 继承来的那条默认构造，
要换就写 `init = …`。唯一放行的是「**同一条定义语句重跑**」（续延重入、同一个块被反复
执行；`while` 就靠它）。用例见 tests/148。

### init 是构造器的名字

```ravel
init = () => { x = 0; }        # ✅ 构造器就是名字叫 init 的变量
init ctor := () => { x = 0; }   # ❌ 没有 init 修饰符这种写法了
```

旧写法会被专门拦下来：

```
Error: 构造器不再用 init 修饰符，直接写 `init = () => { ... }`
```

还有一条比它更安静：**`init` 必须以 `this` 收尾**，否则构造交出的是块的值
（最后一条语句的值），悄悄变成 `()`。见 7.2。

### 元类要收得下「带父类」和「不带父类」两种写法

`MyMeta { ... }` 的父类默认是 `object`；想要别的父类得写 `MyMeta Base { ... }`，
而这条路走的是 `init` 里 `(parent: type body: function)` 那一支。只写一支就会：

```ravel
MyMeta ::= class type {
    private parentInit := init;
    init = (body: function) => { parentInit object body; }   # 少了收 parent 的那一支
}
Base ::= class { ... }
Sub ::= MyMeta Base { ... }     # Error: class 需要代码块参数
```

再加一条：`|` 要跟在第一个 lambda 的 `}` 后面（同一行），另起一行以 `|` 开头会报
`Error: 需要表达式，但得到'|'`。
