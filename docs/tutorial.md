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
| Integer | 整数 | `42` `-1` |
| Float | 浮点 | `3.14` `5.0` `Inf` `NaN` |
| Bool | 布尔 | `true` `false` |
| String | 字符串 | `"hello"` |
| List | 列表 | `[1 2 3]` |
| Set | 集合 | `{1 2 3}` |
| Dict | 字典 | `{a:1 b:2}` |
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

- **全局别名** —— `print`、`true`、`if`、`typeof`、`randint`…… 它们是 `predefined.rav` 里的名字，
  更像"语言的关键词"而不是谁的成员。
- **机制成员** —— `parent` / `block` / `name` / `init` / `this`。它们和同类的方法
  （`Parent` / `Name`）**靠大小写区分**：`C.parent` 是原型链指针，`C.Parent ()` 是那个方法。

**注解里的名字就是作用域里的那个变量**（`int := System.Integer` 只是 `predefined.rav`
里的普通赋值），所以：

```ravel
C ::= class { init := (n: int) => { n; } }
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
int.Parent ()          # ValueType  — 父类型
int.Is ValueType       # true       — 子类型检查
int.Is string          # false
int.Subtypes ()        # [Every]  — 所有子类型(Integer 没有自己的子类)
int.Default ()         # 0           — 默认值
```

值的类型判定用 **`is` / `isnot`** 运算符（下面 3.3 有）——`1 is int`，
它和 `int.Is` 是同一个判据，只是站在值这边说。

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

### 2.6 类型层次

```
Object (parent = 自身)
├── ValueType → Integer Float String BigInt Fraction BigFraction   (并列)
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
（`object.Subtypes ()` 里没有它们）。权威快照见 `tests/125_type_tree.rav`。

`Bool` 挂在 `Function` 下是有意的：`true`/`false` 可以像函数一样调用，
收两个代码块、返回选中那个的结果。于是 `if` 就是 `c {t} {e}`——见「四、控制流」。

### 2.7 BigInt / Fraction / BigFraction

```ravel
bigint 12345678901234567890
fraction 3 4          # 3/4
bigfraction 123 456   # 大数分数
```

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
要比"是不是这个类型"用类对象上的 `Is`：`(typeof 1).Is int`。

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
    init := () => { x = 0; }
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

所以 `if { c; } { t; } { e; }` 等价于 `c { t; } { e; }`。一个类里也常见
`init := (cond) => { ... }` 这种把条件当值传递的写法。

### 4.2 while

```ravel
i := 0
while { i < 5; } {
    print i;
    i += 1;
}
```

### 4.3 早期退出

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

它**不是**生成器那种「一次产出多个值」——重复调用同一个续延会不断从捕获点重来，
所以调用点本身也在被重跑的代码里时，要像上面那样加个守卫，否则会一直转下去。

---

## 五、函数

### 5.1 单参数

```ravel
double := (x: int) => { x * 2; }
double 5    # 10
```

`()` 空参数：`() => { 42; }`，调用：`f ()`。

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

`<|` 和 F#/Haskell 的 `$`、`<|` 一样是**函数在左**：`f <| x` ≡ `f x`。
它存在的意义是省掉一层括号——`f <| a + b` 不用写成 `f (a + b)`。
（以前这里写的是 `5 <| double`，和实现对不上。）

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

### 5.7 缓存函数的返回结果（cacher）

`cacher count f` 把 `f` 的返回结果记下来，同一组实参再来就直接给记下的那个 —— 不再重算。
`count` 是 **`f` 的参数个数**。

最典型的用法是给递归函数自己套上缓存（函数体里读的是外层那个名字，调用时才解析，
所以把缓存版的名字给它，递归的每一层就都走缓存了）：

```ravel
FibCalls := 0
fib: object = 0
fib = cacher 1 ((n: int) => { FibCalls += 1; if { n < 2; } { n; } { fib (n - 1) + fib (n - 2); } })
print (fib 25)          # 75025
print FibCalls          # 26 —— 没缓存是 242785 次
```

（`fib: object = 0` 那行的标注别省：`:=` 会把类型钉在初值的类型上，而这里得先有个名字
让它能被递归引用、再换成缓存版。）

基本用法（注释标出哪几次是命中）：

```ravel
Calls := 0
slow := (x: int) => { Calls += 1; x * 10; }
c := cacher 1 slow
print (c 1)          # 10   算
print (c 1)          # 10   命中
print (c 2)          # 20   另一个实参,算
print Calls          # 2

add := (a: int b: int) => { a + b; }
c2 := cacher 2 add   # 两个参数
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
- 参数个数写成 0 当场报错（`cacher 的参数个数至少是 1，得到 0`），
  不然那个函数会一直收参数、永远不给结果。无参函数写 `cacher 1`，调用时喂 `()`。

**它是个类型**（`Cacher`，`cacher` 是小写别名，和 `int` / `print` 一样）。
缓存就是实例上的三个字段 —— `Count`（参数个数）、`Keys`（算过哪几组实参）、
`Vals`（各得了什么结果），而不是藏在闭包里的局部变量。

但 `init` 交出来的**不是 `this`，而是那个包装函数**：

```ravel
print (typeof (cacher 1 slow))   # Function —— 拿到手就能直接调
print (Cacher.name)              # "Cacher"
```

所以 `cacher 2 add` 可以直接 `(…) 1 2`。包装函数捕获了那块实例作用域，
缓存因此活得和它一样长（实例本身退场了，Scope 被函数拎着走）。
每次 `cacher …` 都是一份**独立**的缓存，包同一个函数互不干扰。

### 5.8 Monad（可能没有值的包）

`Some 5` 包着一个值，`None` 什么都没有。要问有没有值用 `IsSome ()` —— 两者**类型相同**
（都是 `Monad`），`is Monad` 分不出来：

```ravel
(Some 5).IsSome ()     # true
None.IsSome ()         # false
(Some 5).Value ()      # 5 —— 解包
None.Value ()          # 报错：Monad.Value: 这个 Monad 里没有值（…）
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
| `Bind f` | 有值就把 `f` 的结果**摊平**接上（`f` 自己得回一个 Monad），链起来不套娃 |
| `Where p` | 不满足 `p` 就变成 `None` |
| `Exists p` | 有值且满足 `p` |

`None` 是**一个值**（单例，大家共用它），不用写 `None ()`；`(Some 5).Where (…)` 落空拿到的
就是它，所以 `== None` 成立（对象按身份比）。

**括号提醒**（柯里化那条规则的正常结果：实参只吃「主表达式 + 取成员」）：

```ravel
(Some 5).Map f              # 这对括号不能省：Some 5.Map f 是 Some (5.Map f)
((Some 5).Map f).Value ()   # Map 的结果也要套括号，否则 .Value 会贴到 f 上
```

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
- 绑定出来的名字只能标 `object`（lambda 的参数必须有注解，而这里对拿到什么一无所知）；
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

### 5.10 IO（把效果做成值）

`Io.PutStrLn "hi"` **什么都不打印** —— 它交出的是一份**说明书**（一个 `Io.Action`）。
直到 `Perform ()` 那一刻，效果才真的发生：

```ravel
using "io.rav"

Hello := Io.PutStrLn "hello"
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
    name :< Io.GetLine
    _ :< Io.PutStrLn ("你好 " + name)
    Io.PutStrLn "再见"
}
print "还没跑"
Main.Perform ()
```

这就是 IO Monad：`Action` 是说明书的类型，`Bind` 把两份说明书订成一份，所以 `:<` 直接能用。
注意「Monad」在这里是**形状**（有 `Bind` / `Map`）而不是类型 —— `Action` 和 5.8 那个
`Monad` 没有继承关系，只是都能进 `do` 块。

| 名字 | 做什么 |
|------|--------|
| `Io.Action f` | 造一份说明书（`f` 收 `()`、交出一个值，**在 `Perform` 时才被调用**）|
| `.Perform ()` | 跑它。效果世界的边界 |
| `.Map f` / `.Bind f` | 结果过一道纯函数 / 接上另一份说明书（`Bind` 摊平）|
| `Io.Return v` | 不做效果，只交出 `v` |
| `Io.PutStrLn s` / `Io.PutStr` | 打印（带 / 不带换行）|
| `Io.GetLine` | 读一行。它是**一个值**，每次 `Perform` 都真读一次 |
| `Io.Foreach xs f` | 对表里每个元素造一份说明书并依次执行 |

IO 里**没有「落空」这回事**：每一步都跑，值一路往下传（和 `Monad` 的短路正好相反）。
括号规则和 5.8 一样 —— `.Perform` 要贴给调用的**结果**，就得把那个调用括起来：

```ravel
(Io.Foreach [1 2 3] ((x: int) => { Io.PutStrLn (string x); })).Perform ()
r := (Io.Return 20).Map ((x: int) => { x + 1; })    # 或者先绑个名字
print (r.Perform ())                                # 21
```

---

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
d := {a: 1 b: 2}
d.Count ()        # 2
d.Get "a"         # 1
d.Set "c" 3
d.Has "a"         # true
```

### 6.4 集合的相等性是「同一个对象」

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

### 6.5 代码块 vs 集合

```ravel
{1 2 3}        # Set（单行无分号）
{a: 1 b: 2}    # Dict（单行 + IDENT:）
{ print 1; }   # Block（有分号/换行）
{
    print 1     # Block（多行）
}
```

---

## 七、类

### 7.1 类就是一个对象

```ravel
Person ::= class {
    init := () => { this; }
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
| `parent` | 父类对象（链的上游） | `print (Person.Parent ())` → `Object` |
| `block` | 类体——实例化时重跑的配方 | `typeof Person.block` 是 `Block` |
| `name` | 类名 | `Person.name` → `"Person"` |

类对象本身就是**可调用的东西**（不是靠某个成员表示"我能被调用"），所以写得出
`Person ()`；而普通实例不是函数，`p ()` 会报「值 … 不是函数，不能调用」。

`typeof X` 取的是**创建 X 的那个类对象**（元类）：`typeof p` 是 `Person`，
`typeof Person` 是 `Type`；`type` 的元类是它自己，链在那里到头。所以「类是实例的类、
类自己也有类」不需要另一套机制——**建类就是调用一个类对象**，和普通构造调用走同一条路
（见 7.10）。

### 7.2 init 是构造器：必须以 this 收尾

构造器就是类体里那个名字叫 `init` 的变量。一个类最多一个，没有按参数类型重载。

**`init` 交出的返回值就是构造的结果**，所以约定以 `this` 收尾：

```ravel
Good ::= class {
    init := () => {
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
    init := () => { x = 1; }     # 最后一句是赋值,没有 this
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
Error: 构造器不再用 init 修饰符，直接写 `init := () => { ... }`
```

### 7.3 构造器与参数

```ravel
Point ::= class {
    init := (x0: int y0: int) => {
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

构造器调用和普通函数走**同一条柯里化路径**（`tests/147_ctor_curry.rav`）：
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
    init := (v: object) => {
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
    init := () => { name = "from-init"; this; }
    name: string = "?"
}
Dog ::= class Animal {
    init := () => { breed = "husky"; this; }
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

  整条链都没写就落回 **`object` 那份默认构造器**（`init := () => { this; }`，什么都不做、
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
Named ::= class { init := () => { this; } }
print (Named.name)      # Named

Anon := class { init := () => { this; } }
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
    init := () => { this; }
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
`if` / …）、`Math` 与 `Ex` 的函数。**状态**没标（`Ex.HandlerStack` / `References`）——
那些本来就该能改。

为什么连 `:=` 也挡：`:=` 换掉的是**整个 Variable**，attrs 跟着老的那个一起没 ——
不挡的话 `true = 1` 报错、`true := 1` 静默成功，同一个「只读」两条路两个答案。

**`override` / `new` 已删除**：它们从前被解析器接受、记进 attrs，而全库没有一处读它们
（语言里既没有重载也没有重定义检查），写上去等于没写。现在写出来会明确报
「'override' 修饰符已删除」。

**`init` 不在表里**——它只是构造器的名字，不是修饰符（见 7.2）。

```ravel
C ::= class {
    init := () => { _age = 0; this; }
    public name: string = "n"
    private _age: int = 0
    readonly id: int = 1
}
```

### 7.7 by 属性

```ravel
Person ::= class {
    init := () => { _name = ""; this; }
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
    init := () => { 0; this; }
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

它没有状态，所以 `with` / `Copy ()` 让副本和原件共享同一个也无害。

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

### 7.8 with（浅拷贝修改）

```ravel
Person ::= class {
    init := () => { _name = ""; this; }
    _name: string = ""
    by name := property (() => { _name; }) ((v: string) => { _name = v; })
}
p := Person ()
p.name = "Alice"
p2 := with p { name = "Bob"; }
print (p2.name)     # Bob
print (p.name)      # Alice —— 原对象没动
```

`with` 浅拷贝对象，在副本上执行块，返回副本。

### 7.9 object 方法

```ravel
Person ::= class {
    init := () => { this; }
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
    init := () => { 0; this; }
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

在 `init` 里挂钩建类过程（`tests/118_metaclass_print.rav`）：

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
    init := () => { 0; this; }
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
C := M { init := () => { 0; this; }; y: int = 2 }
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
A ::= MyMeta object { init := () => { 0; this; }; }
B ::= class { init := () => { 0; this; }; }
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

1. **`interface { … }` 造出一个类型**（接口本身）。`myTrait is type` 为真：接口继承于 `type`，
   类型树上就挂在 `Type` 下面。
2. **`myTrait myClass { … }` 造一个实现**。块里用 `by a = property …` 把接口声明的槽**换掉**
   （所以写 `=`，不是 `:=`）；块里有个 `instance`，就是"当下这个实例"。
3. **`use impl` 把它登记在当前位置的作用域里**。从此这里 `u.a` / `u.b` 都走实现那条槽；
   `myClass` 本身一个字没动 —— 换个作用域写 `myClass ()` 照旧没有 a/b。

#### 槽住在实现里

实现块在**它自己的作用域**里跑一遍：接口声明的槽先摆好，再由实现块把它们换掉。那些槽就住在
实现身上，实例身上没有 —— 所以 `u.Fields ()` 里不列 a/b。

读 `u.a` 的时候，引擎把实现里的 `instance` 换成 `u`，那条 property 的 getter/setter 看的就是它：

```ravel
v := myClass ()
v.a = 99
print u.a      # 1 —— 还是 u 自己的
print v.a      # 99
```

`instance` 只有一个，所以同一时刻一个实现只服务一个实例（上面两行是顺序来的，不会串）。
直接读 `myImplement.a` 读到的也是它 —— 那一刻 `instance` 是谁就看谁，所以正常用法是 `u.a`。

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

Ex.Try { w := myClass (); print w.a; } (e: Exception) => { print (string e); }
# 类型 'myClass' 没有方法 'a' —— 外面没 use 过
```

`Dispose ()` 提前取消（在哪个作用域调都一样，取消的是这个实现）；已经造出来的实例不残留什么
（槽从来没写到实例上过）。取消了之后想再用，`use` 一次就行：

```ravel
myImplement.Dispose ()
# …这里 u.a 又报没有方法了…
use myImplement          # 在你 use 的这个作用域里重新生效
```

#### 只有 `is` 认接口

类型注解走的是继承链，接口不在这条链上，所以 `x : myTrait = u` 会报「无法将 myClass 赋值给 myTrait」。
要按接口判定就写 `u is myTrait`（实现本身反过来天然成立：`myImplement is myTrait` 为真）。

#### 叠几个实现

同一个作用域里 `use` 两次：**后 use 的先试**，它没有那个名字时再回头试前一个。
接口里写了 `= default` 而实现没填的槽，就是那个"什么都不做"的默认属性 —— 读出来是 `()`。

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
References = ["/path/to/libs/"]
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

### 8.4 Math 模块

函数体是 C# 造的（落到 `System.Math` 上），但要**显式引用**才有：

```ravel
using "math.rav"
Math.Pi            # 3.141592653589793
Math.Sin 0         # 0（三角函数收弧度）
Math.Sqrt 16       # 4
Math.Round 2.5     # 3（四舍五入，不是银行家舍入）
Math.Abs (-5)      # 5（还是 int —— 保型的几个交回原始实参）
Math.Clamp 15 0 10 # 10
```

常量 `pi` / `e` / `tau`，其余见「十、内置函数速查」。`Math` 收任何数值
（int/float/bigint/fraction），内部按 double 算。

`lib/math.rav` 另外补了 Ravel 能表达的几个（同一次 `using` 一起到位）：

```ravel
Math.Square 5      # 25
Math.Deg Math.Pi   # 180（角度↔弧度）
```

`using "math.rav"` 之前 `Math` 不是一个名字 —— 会报「未定义的变量 'Math'」。

---

## 九、异常

```ravel
Ex.Try {
    Ex.Throw (Exception "oops")
} (e: Exception) => {
    print "caught"
}

# 未捕获
exit "fatal error"
```

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

**语法/词法错误走同一套渲染**，只是没有调用栈（源码根本没解析成功）。
`tests/174_syntax_error_report.rav`：

```
Error: 未预期的字符 '$'
  --> tests/174_syntax_error_report.rav:4:10
  4 | print (x $ 2)
    |          ^
```

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
| `callcc fn` | 续延 |
| `with obj { }` | 浅拷贝修改 |
| `assert cond` | 断言 |
| `use impl` | 在**当前作用域**启用一个接口实现（`impl.Dispose ()` 取消；见 7.11） |

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
| `Math.Abs` `sign` `min` `max` `clamp` | `min`/`max`/`clamp` 交回原始实参 |
| `Math.MinMagnitude` `maxMagnitude` `fma` | 按绝对值比 / `a*b+c` |

同一次 `using "math.rav"` 还带来 `square` `cube` `deg` `rad`。

## 十一、常见陷阱

### 单行代码块需要 `;`

单行的 `{ ... }` 不写 `;` 就会被当成 Set/Dict，不是代码块：

```ravel
if { x > 0; } { print 1; } { print 0; }   # ✅
if { x > 0 } { print 1 } { print 0 }       # ❌ → Set
X := { 1 2 }        # Set（不是块）
X := { a: 1 }       # Dict（IDENT : 开头）
```

**这条规则不看位置，lambda 体一样适用**：

```ravel
f := (x: int) => { x * 2; }    # ✅
g := (x: int) => { x * 2 }     # ❌ 报「lambda body 需要代码块」
```

多行的块不用 `;`——换行本身就是 Newline token。`;` 只是「单行里充当换行」。

### 链式调用需要临时变量

应用语法 `f a b` 的左结合会让「点号接在字面量后面」被吃掉：`s.At 0.Count ()`
解析成 `s.At (0.Count ())`，先算 `0.Count ()` 再报 `'Integer' 没有方法 'Count'`。

```ravel
s := [[1 2] [3]]
s.At 0.Count ()      # ❌ 报「'Integer' 没有方法 'Count'」

v := s.At 0          # ✅ 用临时变量断开
v.Count ()           # 2
```

### `:=` vs `=` vs `::=`

```ravel
a := 42     # 定义（类型推断）
a: int = 42 # 定义（类型标注）
a = 42      # 赋值（变量已存在）
a ::= fn    # 定义+自动命名（仅函数/类）
```

### init 是构造器的名字

```ravel
init := () => { x = 0; }        # ✅ 构造器就是名字叫 init 的变量
init ctor := () => { x = 0; }   # ❌ 没有 init 修饰符这种写法了
```

旧写法会被专门拦下来：

```
Error: 构造器不再用 init 修饰符，直接写 `init := () => { ... }`
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
