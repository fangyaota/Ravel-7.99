# 二、类型系统

## 2.1 内置类型

| 类型 | 字面量 |
|------|--------|
| Integer | `42`、`-1`、`7i` |
| Float | `3.14`、`5.0`、`2f`、`Inf`、`NaN` |
| Bool | `true`、`false` |
| String | `"hello"` |
| Char | `'a'` |
| List | `[1 2 3]` |
| Set | `{1 2 3}` |
| Dict | `{"a":1 "b":2}` |
| Range | `[1..3]`、`(3..5)`、`[1..5)`、`(1..5]` |
| Void | `()` |

注意：大括号两种都用 —— `{1 2 3}` 是集合，`{"a":1}` 是字典。

## 2.2 类型名

类型名是 PascalCase，小写的是别名。`int` 就是 `System.Integer`，`int.name` 是 `"Integer"`。

成员（方法 / 字段）一律 PascalCase：`obj.Fields ()`、`Math.Sin x`、`Ex.Throw e`。

两处例外，都是小写：

| 例外 | 例子 | 说明 |
|------|------|------|
| 全局别名 | `print`、`true`、`if`、`typeof` | `predefined.rav` 里的名字，像关键词 |
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
Error: 类型不匹配: 无法将 Integer 赋值给 C —— 无法将 Integer 转换为 C
```

遮蔽写在内层作用域里（顶层 `string := C` 报「已经定义过」），之后注解 `z: string` 指的就是 `C`。

注解也可以是一个表达式，求值在定义处（变量）或创建处（参数）做一次。

#### 实例

```ravel
pick := (flag: bool) => { if { flag; } { int; } { float; } }
a: (pick true) = 5
b: (pick false) = 5
print (typeof a)
print (typeof b)
```

执行以上程序会输出如下结果：

```
Integer
Float
```

写法两种：一个名字（可带成员访问，`x: int`），或括号里的任意表达式（`x: (pick true)`）。

注意：括号**必需**。`(x: int y: int)` 里的 `int y` 会被当成一次调用，所以 `x: System.List` 要写成 `x: (System.List)`。

## 2.3 类型反射

| 写法 | 交回 |
|------|------|
| `int.parent` | 父类型 `ValueType` |
| `int <: ValueType` | `true` |
| `object :> int` | `true` |
| `int.Subtypes ()` | `[Every]` |
| `int.Default ()` | `0` |

`<:` / `:>` 两边都得是**类型对象**；左边是**值**时用 `is`（`1 is int`）。两者的细节见 3.3。

要看整棵树用 `Types.PrintTree object`（要显式 `using "types.rav"`）。方括号里是那个类型的 `typeof`：类对象显 `Type`，你自己类的实例显它那个类。

## 2.4 类型转换

把类型名当函数用，就是转换：`int "42"`、`string 100`、`float 3`。

注意：`bool` 只认 `bool` 和 `default` —— 数字**没有**到 bool 的转换（`bool 0` 报「无法将 Integer 转换为 bool」）。

**标注位置上会隐式转换**：`x: int = <别的类型>` 走的就是 `int <别的类型>`，转得动就转、转不动才报「类型不匹配」。

#### 实例

```ravel
x: int = 3.9
y: bigint = "123"
z: int = fraction 7 2
bad: int = NaN
```

执行以上程序会输出如下结果：

```
Error: 类型不匹配: 无法将 Float 赋值给 Integer — NaN 不能转换为 int
```

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

这里的「空」**只有 `None` 一样**。`()` 不是空 —— 它是 Void 类型那个**值**，和 `0` / `""` / `[]` 平起平坐。

| 写法 | 意思 |
|------|------|
| `a ?? b` | `None` 给 `b`；`Some x` 给 **`x`（解包）**；别的值原样。右边惰性 |
| `a?.b c` | `None` 原样短路；`Some x` 拿着 `x` 走完整条链，**再包回去** |
| `x ??= v` | 空才写；不空一个字符都不碰 |

#### 实例

```ravel
print (None ?? "默认")
print ((Some 5) ?? "默认")
print (() ?? "默认")
u := None
print ((u?.Value ()) ?? "无名")
```

执行以上程序会输出如下结果：

```
默认
5
()
无名
```

注意：`Some 5 ?? v` 给的是 `5`（拆了包），而 `() ?? v` 给的是 `()`。

注意：`?.` 拆包之后是在**里面那个值**上继续走，**不是在外层包上走** —— `u?.Value ()` 是"拿 `u` 里面的东西去调 `.Value ()`"，不是"在 Option 上取 Value"。

`??=` 收的是**变量**，写进去的东西要符合变量声明的类型。要"没设过才写"，就用 `Cache: Option = None` 打底。

`?.` 和 `.` 一个位置：只能在 `.` 能出现的地方写（主表达式之后、或 `|>` 之后）。

## 2.6 类型层次

```
Object (parent = 自身)
├── ValueType → Integer Float String BigInt Fraction BigFraction Range   (并列)
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

数字后缀能直接在字面量上写类型：`42n`（BigInt）、`42i`（Integer）、`2f` / `2.5f`（Float）。构造器是 `bigint` / `fraction` / `bigfraction`。

注意：**`2.0` 是 float，不是 int**，哪怕小数位是 0。整数装得下 `int` 就是 `int`，装不下（`2147483648`）退化成 `bigint`。

注意：后缀**要吞得干净**才算 —— 后面再粘着标识符字符就不是后缀（`2n` 后面跟 `)` `.` `[` 空格才算数）。`2.5i` / `2.5n` 报错：小数没有"整数后缀"这回事。
