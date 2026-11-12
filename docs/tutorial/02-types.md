# 2、类型系统

## 2.1 内置类型

| 类型 | 字面量 |
|------|--------|
| Integer | `42`、`-1`、`7i` |
| Real | `3.14`、`5.0`、`2f`、`Inf`、`NaN` |
| Bool | `true`、`false` |
| String | `"hello"` |
| Char | `'a'` |
| List | `[1 2 3]` |
| Set | `{1 2 3}` |
| Dict | `{"a"->1 "b"->2}` |
| Range | `[1..3]`、`(3..5)`、`[1..5)`、`(1..5]`、`[1..]`、`[..1]` |
| Void | `()` |

注意：大括号两种都用 —— `{1 2 3}` 是集合，`{"a"->1}` 是字典（`->` 左边是键，右边是值）。空的两样也分开写：`{}` 是空集合，`{->}` 是空字典（判据就是那个箭头，没有条目也得写出来）。

## 2.2 类型名

类型名是 PascalCase，小写的是别名。`int` 就是 `System.Integer`，`int.name` 是 `"Integer"`。

成员（方法 / 字段）一律 PascalCase：`obj.Fields ()`、`Math.Sin x`、`Ex.Throw e`。

两处例外，都是小写：

| 例外 | 例子 | 说明 |
|------|------|------|
| 全局别名 | `print`、`true`、`typeof` | `predefined.rav` 里的名字，像关键词 |
| 机制成员 | `parent`、`block`、`name`、`init`、`this` | 类对象自己那层的数据 |

注意：`parent` / `block` 是**只读**的。`C.parent = int` 之后，`C ()` 会去跑 `Integer` 的构造器。

### 注解写什么

注解里的名字就是作用域里的那个变量 —— 可以被遮蔽。

#### 实例

```ravel
C ::= class { init = (n: int) => { n; this; } }
f := () => { string := C; z: string = 5; }
f ()
```

执行以上程序会输出如下结果：

```
Error: 这一格要的是 C，得到 Integer
```

遮蔽写在内层作用域里（顶层 `string := C` 报「已经定义过」），之后注解 `z: string` 指的就是 `C`。

注解也可以是一个表达式，求值在定义处（变量）或创建处（参数）做一次。

#### 实例

```ravel
pick := (flag: bool) => { flag { int; } { real; } }
a: (pick true) = 5
b: (pick false) = real 5      # 注解只断言不转换 —— 要 real 就自己写出来
print (typeof a)
print (typeof b)
```

执行以上程序会输出如下结果：

```
Integer
Real
```

写法两种：一个名字（可带成员访问，`x: int`），或括号里的任意表达式（`x: (pick true)`）。

注意：括号**必需**。`(x: int y: int)` 里的 `int y` 会被当成一次调用，所以 `x: System.List` 要写成 `x: (System.List)`。

## 2.3 类型反射

| 写法 | 交回 |
|------|------|
| `int.parent` | 父类型 `Object`（`IValue` 是**接口**，不在继承链上）|
| `int <: IValue` | `true` |
| `object :> int` | `true` |
| `int.Subtypes ()` | `[Every]` |
| `int.Default ()` | `0` |

`<:` / `:>` 两边都得是**类型对象**；左边是**值**时用 `:`（`1: int`）。两者的细节见 3.3。

要看整棵树用 `Types.PrintTree object`（要显式 `using "types.rav"`）。方括号里是那个类型的 `typeof`：类对象显 `Type`，你自己类的实例显它那个类。

## 2.4 类型转换

把类型名当函数用，就是转换：`int "42"`、`string 100`、`real 3`。

注意：`bool` 只认 `bool` 和 `default` —— 数字**没有**到 bool 的转换（`bool 0` 报「无法将 Integer 转换为 bool」）。

#### 实例

```ravel
print (int "42")
print (string 100)
print (real 3)
print (int 3.9)              # 往窄处转要自己说：截断成 3
print (bigint 1.5)
```

执行以上程序会输出如下结果：

```
42
100
3
3
1
```

**注解是断言，不是转换指令。** `x: T = v` 只在"那个值**本来就属于** `T`"时成立 ——
判据是继承链（父子）和当前作用域里生效的实现（接口），和 `x: T` 那条判断完全一样。

数值那几族**彼此是兄弟**（`1: real` 是 false），所以下面这几条当场报：

#### 实例

```ravel
tries := []
try { eval "x: real = 1"; } (e: Exception) => { tries.Add ("1: " + e.Message); }
try { eval "y: bigint = 5"; } (e: Exception) => { tries.Add ("2: " + e.Message); }
try { eval "z: int = 3.9"; } (e: Exception) => { tries.Add ("3: " + e.Message); }
tries.Foreach (t: string) => { print t; }
```

执行以上程序会输出如下结果：

```
1: 这一格要的是 Real，得到 Integer
2: 这一格要的是 BigInt，得到 Integer
3: 这一格要的是 Integer，得到 Real
```

要转就**明写**：`x: real = real 1`。好处是"这里发生了一次转换"永远写在脸上 ——
而且**转换表只有一张**（隐式那条路没了，就不存在"显式走得通、隐式报错"那种两张表各飘各的）。

参数表和注解一个口径（都只问"本来就属于吗"），`default` 是唯一的例外 ——
它要的不是转换，是"按注解造一个本类型的空值"：

## 2.5 空值

`()` 是 Void 类型唯一的值。`default` 是 Every 类型唯一的值，**按注解**变成本类型的空值：

| 写法 | 交回 |
|------|------|
| `int default` | `0` |
| `string default` | `""` |
| `bool default` | `false` |
| `list default` | `[]` |

所以 `n: int = default` 之后 `n + 1` 照常算 —— 它拿到的是 `0`，不是一个万能占位。

各类型的空值：数值 `0`、字符串 `""`、`bool` 假、容器空表、`function` 空函数、`Continuation` 是"还没到手的那一枚"。

### 三条处理"没有"的糖

这里说的「空」是 **monad 里没有元素**（`IMonad`：`Option` / `list` / `dict` / `set` / `string` …）。`()` 不是空 —— 它是 Void 类型那个**值**，和 `0` / `""` / `[]` 平起平坐；`()` 也不是 monad，所以拿它做 `?.` 会当场报错。

| 写法 | 意思 |
|------|------|
| `a ?? b` | monad 空就给 `b`（惰性）、不空**原样交回**左边（不拆包）；左边**不是 monad 直接报错** |

`()` 也算 monad：`impl (IMonad void)` 里三件都是"永远空"的样子 —— `Map` / `Bind` 原样交回 `()`，`SelfOr` 直接调后备。于是 `() ?? b` 得 `b`，`()?.f ()` 得 `()`。
| `a?.b c` | 左边是 monad 就 **`Map`**：`None` 原样短路、`Some x` 拆包走完整条链再包回、`list`/`dict`/`string` 逐元素走；左边**不是 monad 直接报错** |
| `x ??= v` | 空才写；不空一个字符都不碰 |

`??` 落在 `IMonad` 的 `SelfOr` 上：`v.SelfOr fallback` —— 不空交回 `v` 自己，空了才调 `fallback ()`（惰性）。所以 `??` 只问"空不空"，不碰"里面的值"；要取值用 `ValueOr`（`Option` / `Expected` 上那一个）。

#### 实例

```ravel
print (None ?? "默认")
print ((Some 5) ?? "默认")
print ([] ?? "默认")
u := None
print ((u?.Value ()) ?? "无名")
```

执行以上程序会输出如下结果：

```
默认
Option { Has = true, Inner = 5 }
默认
无名
```

注意：`Some 5 ?? v` 交回的是 `Some 5` 本身（**不拆包**，要拆用 `.ValueOr`）；空的那个（`None` / `[]` / `""` / `()`）才换成 `b`。左边不是 monad（`5`、`3`）当场报「'??' 的左边得是个 monad」。

注意：`?.` 左边是 `Option` 时，是在**里面那个值**上继续走，**不是在外层包上走** —— `u?.Value ()` 是"拿 `u` 里面的东西去调 `.Value ()`"，不是"在 Option 上取 Value"。

`?.` 和 `|>` 搭起来是一条"空值管道"：`None` 一路短路，`Some x` 每过一段拆开走一趟再包回去 —— 所以形状**恒是 `Option`**，不会叠成 `Some (Some x)`；末尾可以再用 `??` 给个后备，要拿里面的值则用 `.ValueOr`。

#### 实例

```ravel
print (Some "  hi  " |> ?.Trim ())
print (None |> ?.Trim ())
print (Some "  hi  " |> ?.Trim () |> ?.Length ())
print ((None |> ?.Length ()) ?? -1)
```

执行以上程序会输出如下结果：

```
Option { Has = true, Inner = hi }
Option { Has = false, Inner = () }
Option { Has = true, Inner = 2 }
-1
```

两处别扭的地方：`??` **不能**直接跟在 `|>` 后面（它是运算符，`|>` 右边要跟的是表达式），得把管道结果括起来 —— `(x |> ?.f ()) ?? 默认`。还有 `() ` 后面接 `.成员` 那条例外（见 5.5）：`x |> ?.Trim ().Length ()` 会被读成 `x |> ?.Trim (().Length ())`，要么再来一段 `|>`，要么自己加括号 `(x |> ?.Trim ()).Length ()`。

`??=` 收的是**变量**（或成员），且它得是 monad：`x ??= v` 就是 `x = (x ?? v)` —— 不空时写回原值、连 setter 都不调，空了才写 `v`。要"没设过才写"，就用 `Cache: Option = None` 打底。

`?.` 和 `.` 一个位置：只能在 `.` 能出现的地方写（主表达式之后、或 `|>` 之后）。

## 2.6 类型层次

```
Object (parent = 自身)
├── Integer  Real  String  BigInt  Fraction  BigFraction  Range     (并列,全是值类型)
├── Function → Bool  Block  Type
├── List  Set  Dict
├── Void  Exception  Ravel(模块)  Scope  Property
├── Any (顶类型, parent = 自身, 不在 Object 子树里)
└── Every (底类型 = default 的类, parent = 自身, 不在 Object 子树里)
```

**没有 `Class` 类型**：`class` 就是 `type` 的别名，两者是同一个值 —— 所以 `typeof Person`（用户类）和 `typeof int` 都是 `Type`。

`Any` 和 `Every` 画在最后只是排版方便，它们的父类型是**自己**，`object.Subtypes ()` 里没有它们。

权威快照见 `examples/type_tree.rav`。

## 2.7 BigInt / Fraction / BigFraction

数字后缀能直接在字面量上写类型：`42n`（BigInt）、`42i`（Integer）、`2f` / `2.5f`（Real）。构造器是 `bigint` / `fraction` / `bigfraction`。

注意：**`2.0` 是 real，不是 int**，哪怕小数位是 0。整数装得下 `int` 就是 `int`，装不下（`2147483648`）退化成 `bigint`。

注意：后缀**要吞得干净**才算 —— 后面再粘着标识符字符就不是后缀（`2n` 后面跟 `)` `.` `[` 空格才算数）。`2.5i` / `2.5n` 报错：小数没有"整数后缀"这回事。

## 2.8 关掉类型检查（`#ignoreTypes`）

有些时候（移植一段旧代码、做性能对比、临时跑一段不带注解的东西）不想让注解生效，又不想一路删注解。文件里可以摆两个开关：

```ravel
#ignoreTypes true
x: int = "s"          # 不报：注解只当摆设
f := (a: int) => { a; }
print (f "s")         # s
#ignoreTypes false
```

夹在中间的代码，三处检查全部不发：**变量定义**的注解、**参数**的注解、**模式匹配**里的类型格（含 `(1) => …` 里那道"是不是数字那族"）。注解本身还在树上，所以 `x: int = _` 照样是 `default int`（`_` 问的不是"检不检查"，是"这个类型空着的时候是什么"）。

规矩就两条：

- 一行里只有 `#ignoreTypes true` / `#ignoreTypes false` 才算开关，别的 `#` 行仍是注释。
- 它是**按源码顺序**的开关，从那一行起一直有效到下一个 `false`（或文件结束）——不是块作用域。写在 `{ … }` 里头也一样，出了那个块不会自动恢复。
