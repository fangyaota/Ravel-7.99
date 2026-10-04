# 六、集合与标准库

## 6.1 List

#### 实例

```ravel
lst := [1 2 3]
print (lst.At 0)
print (lst.Count ())
lst.Add 4
lst.Remove 1
lst.Insert 0 99
lst.Set 0 100
print lst
```

执行以上程序会输出如下结果：

```
1
3
[100 1 3 4]
```

注意：这四个方法都是**就地改**；只有 `Remove` 把删掉的元素交回来，`Add` / `Insert` / `Set` 交回 `()`。

## 6.2 Set

```ravel
s := {1 2 3}
s.Add 4
s.Remove 2
s.Contains 3      # true
```

## 6.3 Dict

```ravel
d := {"a"-> 1 "b"-> 2}      # 键是**表达式**：字符串要写引号
d.Get "a"          # 1
d.Set "c" 3
d.Has "a"          # true
```

注意：`{a: 1}` 里的 `a` 是**变量**，没定义就报「未定义的变量 'a'」。

## 6.4 序列方法（三种容器共用）

这些方法只跟**元素顺序**有关，所以 list / set / dict 都有同一份（字典的元素是**值**）。名字照 C# 的集合 / Linq API 起。

| 方法 | 说明 |
|------|------|
| `Count ()` `IsEmpty ()` `Any ()` | 个数 / 空不空 / 有没有 |
| `Contains v` | 元素里有它（原子值按值比、容器按身份比）|
| `First ()` `Last ()` | 头一个 / 末一个（空容器**报错**）|
| `Take n` `Skip n` | 前 n 个 / 跳过前 n 个 |
| `Distinct ()` `Reverse ()` `Concat ys` | 去重 / 倒过来 / 接上另一串 |
| `Sum ()` `Min ()` `Max ()` | 求和 / 最小 / 最大 |
| `Join sep` | 拼成字符串 |
| `ToList ()` `ToSet ()` | 换容器 |
| `Each f` | 每个跑一遍（结果丢掉）|
| `Map f` | 变换（C# 的 `Select`）|
| `Where p` | 过滤（`p` 得交回 bool）|
| `Fold init f` | 折叠（`f 累积值 元素`）|
| `All p` `Any p` | 是不是都满足 / 有没有满足的 |
| `Find p` | 第一个满足的（没有就**报错**）|
| `SortBy f` | 按键排（**稳定**）|

三条规矩：

- **变换和查询交回新的 list**，原容器不动；要 set 就 `.ToSet ()`。
- 顺序类的按**枚举顺序** —— set / dict 的顺序是它们枚举器给的，别当插入顺序用。
- 比大小一律走 `<`：数值之间能混着比、字符串按序数比，别的类型**当场报错**。

**list 还有**带下标的：`At i` / `Set i v` / `Insert i v` / `RemoveAt i` / `IndexOf v` / `AddRange xs` / `Sort ()` / `Clear ()`。

**set 还有**集合代数：`Union` / `Intersect` / `Except` / `IsSubsetOf`。

**dict 还有**：`Get k`（没有就报错）/ `GetOr k fallback` / `Set k v` / `Has k` / `HasValue v` / `Remove k` / `Keys ()` / `Values ()`。

## 6.4.1 解构：一次绑好几个名字

右边是什么形状，左边就照着写，一次把名字全绑出来：

```ravel
[x y] := [10 20]
print (x + y)

[ka _ kc ..krest] := [1 2 3 4 5]
print ka
print kc
print (krest.ToList ())
```

执行以上程序会输出如下结果：

```
30
1
3
[4 5]
```

`[… ]` 按**位置**取，`{…}` 从对象上取**同名成员**：

```ravel
Point ::= class { px: int = 0; py: int = 0 }
p := Point ()
p.px = 3
p.py = 4
{px py} := p
print (px + py)
```

执行以上程序会输出如下结果：

```
7
```

两边能嵌套，凡是能 `foreach` 的都能解（list / set / dict / 字符串 / `Generator` / Option 都行）：

```ravel
[v0 v1 ..tail] := "abcdef"
print v0
print ((tail.ToList ()).Join "")

[[m n] [q]] := [[1 2] [3]]
print (m + n + q)
```

执行以上程序会输出如下结果：

```
a
cdef
6
```

三条和别处**不一样**的：

- **落点是「定义」** —— `[x y] := e` 绑的是**新名字**。光一个 `=`（`[x y] = e`）**不认**：
  那和 `x = v`（赋值给已经有的变量）撞，得自己说清是哪一个。注解可省，写法跟着
  `x : T = v` 走：`[x y] : list = e`。
- **`_` 在这儿是「跳过」**，不是 5.3 那个占位符洞；而且**跳过 ≠ 不取**，游标照样走一格。
  `..rest` 把剩下的全给它（一个 `IEnumerable`），只能写最后一项。
- **注解管的是右边那个值**（当场验）；前缀修饰符跟着**每个**拆出来的名字走 ——
  `private [a b] := e` 写在类体里就是两个私有成员。

右边那个值**只求一次**（`{a b} := f ()` 不会把 `f` 跑两遍）。序列比模式短会报越界
（`list.At 的索引 2 越界`），不静默给 `None`。

## 6.5 集合的相等性是「同一个对象」

`int` / `string` / `bool` / `float` / `bigint` / `fraction` 比的是**值本身**，所以 `{1 2 2}` 只有 2 个元素。但 `list` / `set` / `dict` 比的是**身份**。

#### 实例

```ravel
print ({[1] [1]}.Count ())
s := {[1]}
print (s.Contains [1])
```

执行以上程序会输出如下结果：

```
2
false
```

这也是为什么集合类型**没有** `==` / `!=` 运算符 —— 可变集合上的值相等很难说清。要比内容就自己写循环。

## 6.6 代码块 vs 集合

```ravel
{1 2 3}              # Set（单行无分号）
{"a"-> 1 "b"-> 2}      # Dict（单行，第一个元素后面跟 `->`）
{ print 1; }         # Block（有分号或换行）
{
    print 1          # Block（多行）
}
```

## 6.7 字符与字符串

**`char` 是一个字符** —— 精确说是一个 UTF-16 **码元**，和 `s.Length` / `s.At i` 一个口径（`"😀".Length` 是 2，一个 `'…'` 装不下它）。字面量用单引号。

它是**值类型**：能比大小、能当字典的键、能进集合去重。

| 写法 | 交回 |
|------|------|
| `'a' + 'b'` | `"ab"` —— 拼成字符串 |
| `'-' * 5` | `"-----"` —— 重复（0 或负数给空串）|
| `int 'A'` | `65` —— 码位 |
| `'7'.IsDigit ()` | `true` |
| `'A'.IsUpper ()` | `true` |

**字符串的方法**（`s.At i` 给的是**字符**，不是长度 1 的串）：

| 归类 | 方法 |
|---|---|
| 长度 | `Length ()` · `IsEmpty ()` |
| 取 | `At i`（越界报错）· `Chars ()`（一串字符）|
| 找 | `Contains` · `StartsWith` · `EndsWith` · `IndexOf`（找不到**报错**）· `Find`（给 `-1`）· `LastIndexOf` |
| 切 | `Slice [1..3)`（**收一个区间**）· `Take n` · `Skip n` |
| 变 | `Trim ()` · `ToUpper ()` · `ToLower ()` · `Replace a b` · `Repeat n` · `Reverse ()` · `Split sep` |

#### 实例

```ravel
s := "Hello, World"
print (s.At 0)
print (s.Contains 'H')
print (s.Slice [0..4])
print ("a,b,,c".Split ",")
```

执行以上程序会输出如下结果：

```
H
true
Hello
[a b  c]
```

注意：**字符串能 `foreach`**，元素是**字符**。想拿一串字符交给序列方法，用 `s.Chars ()`。

注意：大小写转换**不跟区域设置走**（invariant）—— 同一段程序换台机器结果一样。

## 6.8 字符串插值

#### 实例

```ravel
name := "小明"
n := 3
print "你好 ${name}!"
print "共 ${n} 个,${n * 2} 个才对"
```

执行以上程序会输出如下结果：

```
你好 小明!
共 3 个,6 个才对
```

**只有 `${表达式}` 一种写法**（没有 `$名字` 那种短的）。它不是新语义 —— 上面那句就是 `"" + "你好 " + string (name) + "!"`。

三条小事：

- **`$` 后面不是 `{` 就是字面量**：`"$5"`、`"$name"` 都不用转义；要写 `${` 本身用 `\${`。
- 没闭合的 `${` 报「插值没有收尾的 `}`」。
- **和代码里的 `$` 不冲突** —— 代码里根本没有 `$` 这个运算符（3.9 那个"把右边封口"现在写 `<|`）。

## 6.9 `Seqs`：摊平 / 切块 / 拉链 / 分组 / 计数

全都**对着 `IEnumerable` 写**，交回的是**当场算好的** `list` / `dict`。

#### 实例

```ravel
print (Seqs.Flatten [[1 2] [3] [4 5]])
print (Seqs.Chunk [1 2 3 4 5] 2)
print (Seqs.Zip [1 2 3] ["a" "b"])
print (Seqs.Frequency ["a" "b" "a"])
```

执行以上程序会输出如下结果：

```
[1 2 3 4 5]
[[1 2] [3 4] [5]]
[[1 a] [2 b]]
{a: 2 b: 1}
```

- `Group` / `Frequency` 的键走 **`.Key ()` 那套规范**（和 `dict` 的键同一个规矩）。
- `Chunk` 的每一摞是**各自独立**的 list；`n` 至少是 1。
- 要**惰性**就用 `Generator`（见 4.3）；这五件都是"一次算完"的。

## 6.10 随机数（`Random`）

`using "random.rav"` 之后有四台，**换台子只换一行构造子**：

| 写法 | 可复现吗 |
|------|----------|
| `Random.Shared ()` | 不可复现（进程共享）|
| `Random.Make 42` | **同一个运行时**里可复现 |
| `Random.Xoshiro 42` | **算法定死**：跨版本跨机器都一样 |
| `Random.Crypto ()` | 不可复现，也没有种子 |

`Make` 走 .NET 的 `Random(seed)`（换个 .NET 版本就可能变）；`Xoshiro` 是自己实现的 xoshiro256\*\*，算法固定 —— 要把具体的数写死就用它。

#### 实例

```ravel
using "random.rav"
print (Random.Xoshiro 42 |> .Below 1000000)
print ((Random.Xoshiro 7 |> .Below 100) == (Random.Xoshiro 7 |> .Below 100))
```

执行以上程序会输出如下结果：

```
47179
true
```

取数那一面是一套方法（写成接口 `IRandom` 的**默认实现**，三台都白拿）：`Int [1..6]`（掷骰子）/ `Below n` / `Float ()` / `Bool ()` / `Choice xs` / `Shuffle xs` / `Sample n xs` / `Choices n xs` / `Normal μ σ` / `Weighted xs ws` / `Bytes n`。

注意：`Int` **收一个区间** —— 开闭全看那对括号，不必再记"上界含不含"。

## 6.11 区间（`Range`）

一个内置值类型：从 a 到 b 的一串整数。那一对括号**各带一半的意思**。

| 写法 | 是哪些 |
|------|--------|
| `[1..3]` | `1 2 3` —— 两端都含 |
| `(3..5)` | `4` —— 两端都不含 |
| `[1..5)` | `1 2 3 4` |
| `(1..5]` | `2 3 4 5` |

**方向由两端自己说了算**：起点在终点后面就是**倒着数**（`[5..1]` → `5 4 3 2 1`），开闭跟着方向走。

「里面有没有」和「落不落在这段里」是**两个问题，各有一条**：

#### 实例

```ravel
print ([1..10].Contains 2)
print ([1..10].Contains 2.5)
print ([1..10].Covers 2.5)
print ([1..10).Covers 10)
```

执行以上程序会输出如下结果：

```
true
false
true
false
```

注意：**端点收任何数值**（int / bigint / float / fraction），而**元素是「区间里的整数」** —— `[1.5..3.5]` 的元素是 `2 3`。所以 `Start ()` / `End ()`（写出来那个数）和 `First ()` / `Last ()`（区间里真有的那个整数）是**两回事**。

注意：**它是惰性的** —— `([1..1000000000]).Take 3` 秒回，`Count ()` / `Contains n` 走引擎那几条（O(1)）。

注意：区间**按内容比**（`[1..3] == [1..3]` 成立）—— 它挂在 `ValueType` 那一支下。

**切一段**给任何一串都用 `Slice`，**收一个区间**：`xs.Slice [1..3]` → `[20 30 40]`（对 `[10 20 30 40 50]`）。

## 6.12 正则（`Regex`）

字符串那批只认**字面量**；凡是"形状"就用正则。**要显式引用**。

```ravel
using "regex.rav"
re := Regex.C "(\d+)-(\d+)"
re.IsMatch "12-34"               # true
(re.Match "a 12-34 b").Value ()  # "12-34"
re.Replace "12-34" "$2-$1"       # "34-12"（组引用）
Regex.Escape "a.b"               # "a\.b"
```

命中的那段是 `RegexMatch`：`Value ()` / `At ()` / `Length ()` / `Groups ()` / `Group 1` / `Group "名字"`。**没参与匹配的组给 `()`** —— 和"匹配到空串"分得开。

**选项**：第二个参数（`Regex.With pattern "i"`）或写在模式里（`(?i)abc`）—— `i` 不分大小写、`m` 多行、`s` 让 `.` 也吃换行、`x` 忽略模式里的空白。

## 6.13 格式化 / 文本 / 编码

三件"每天都用、手搓不值当"的事，各自一个模块，**都要显式引用**。

#### 实例

```ravel
using "format.rav"
print (Format.Fmt "{} 有 {} 个" ["苹果" 3])
print ("[" + (Format.PadL "7" 3) + "]")
print ("[" + (Format.PadR "7" 3) + "]")
print (Format.Num 3.14159 2)
print (Format.Thousands 1234567)
print (Format.Hex 255)
print (Format.Bytes 1536)
```

执行以上程序会输出如下结果：

```
苹果 有 3 个
[7  ]
[  7]
3.14
1,234,567
ff
1.5 KB
```

注意：`PadL` / `PadR` 的名字读起来**是反的** —— `PadL "7" 3` 是 `"7  "`（补右边），`PadR "7" 3` 是 `"  7"`（补左边）。名字里那个 L / R 说的是**原来那个值靠哪边**。

`Text` 那批：`Lines` / `Words` / `Wrap` / `Indent` / `Dedent` / `Truncate` / `Quote`。

**任意结构都能画成一棵树** —— 给它"子节点是谁""这一行写什么"两枚函数：

#### 实例

```ravel
using "text.rav"
Kids := (x: string) => { if { x == "a"; } { ["b" "c"]; } { []; } }
print (Text.Tree "a" Kids ((x: string) => { x; }))
```

执行以上程序会输出如下结果：

```
a
├── b
└── c
```

`Encoding` 那批：`Base64Text` / `Base64` / `Hex` / `Unhex` / `UrlEncode` / `HtmlEscape`。

注意：**字节表**是"一串 `0..255` 的 int"。Ravel 的 `string` 是 UTF-16，**装不下任意字节** —— 要编码就先进字节表。

注意：`Format.PadL` 这类补出来的空格**在行尾**，写 golden 用例时要套一层方括号才钉得住（跑测试那套会把行尾空白裁掉）。

## 6.14 数据类（`dataclass`）

一堆字段 +「打印、相等、构造」三件事每次都手写太啰嗦，`dataclass` 一个都不用手写。**要显式引用**。

#### 实例

```ravel
using "dataclass.rav"
Point ::= dataclass {
    x: int = 0
    y: int = 0
}
p := Point 1 2
print (p.Text ())
print (p.Values ())
print (p == (Point 1 2))
print ((Point 1 ()).Text ())
```

执行以上程序会输出如下结果：

```
Point(x=1, y=2)
[1 2]
true
Point(x=1, y=0)
```

自动拿到的成员：`Text ()` / `Values ()` / `Eq` / `==` / `!=` / 按位置收的 `init`。**用户自己写了同名的就以用户的为准**。

注意：字段表**到用的时候才算**，所以拿数据类当父类也认得出子类新加的字段。反过来，`{ }` 是**空字典** —— 一个字段都没有的数据类要写 `dataclass { 0; }`。

## 6.14.1 枚举（`enum`）

一串名字和值，每个名字拿到一枚**这个枚举的**值。

#### 实例

```ravel
using "enum.rav"
myEnum ::= enum {
    A := 1
    B := 2
}
print (myEnum.A.Value)
print (myEnum.A.Name)
print (myEnum.Values () |> .Map ((v: object) => { v.Text (); }) |> .ToList ())
print ((myEnum.Of 2).Text ())
print (myEnum.A == myEnum.A)
```

执行以上程序会输出如下结果：

```
1
A
[A B]
B
true
```

注意：`Values ()` / `Of k` 交回的是**枚举值本身**，直接打印出来是**字段快照**（`myEnum { Value = 1, Name = A }`）—— 要 `A` 那样的文本用 `.Text ()`。别的库对象同此（`Time` / `Json` 那几条也一样）。

注意：值是**任何东西**都行（`Red := "红"` 也成立）。元类只管固定的那一套（`Value` / `Name` / `Text ()` / `Values ()` / `Of`）。

注意：**继承时父类必须也是个 enum**（拿普通类当父类当场报错）。父类那几枚会**迁过来**，而且迁过来的是"**这个**枚举的值"：`sub.A == base.A` 是 `false` —— 同名不同枚。

## 6.14.2 按位枚举（`flags`）

`enum` 的**子类**：每枚成员占一位，能拼起来。声明、成员、`Values ()` / `Text ()` 那一套全照 `enum`。

```ravel
using "enum.rav"
perms ::= flags {
    Read := 1
    Write := 2
    Exec := 4
}
p := (perms.Read | perms.Exec)
print (p.Value)
print (p.Text ())
print (p.Has (perms.Write))
print (p.Names ())
print ((perms.Of 5).Text ())
```

执行以上程序会输出如下结果：

```
5
Read|Exec
false
[Read Exec]
Read|Exec
```

| 写法 | 意思 |
|------|------|
| `a \| b` / `a & b` / `a ^ b` | 并 / 交 / 对称差 |
| `a - b` | 差：把 `b` 里有的那几位去掉 |
| `a.Has b` | `a` 里**有没有** `b` 那几位的**全部** |
| `a.Names ()` | `a` 里有哪几枚**声明过的**成员（按声明的先后）|
| `a.Text ()` | 单枚就是名字，组合就是 `Read\|Exec` |
| `Perm.Of v` | 按值找成员；找不着就**现拼一枚**组合值（不再报错）|

位运算的另一边可以是**同一个 flags 的值**，也可以**直接给整数**（解配置里那个掩码时好使）；别的类型当场报错。

三条要留意的：

- **值该是二的幂**（1 / 2 / 4 / 8…）。`Has` / `Names` 都按位算，写个 3 进去那一枚就同时占两位。**不拦**你（确实有想要 `All := 7` 那种组合成员的），但心里得有数。
- **组合值不是成员**：`Read | Exec` 交回的是**新的一枚**（值 5），不在 `Values ()` 里、`Name` 是空的。拼出来正好等于某枚成员时，交回的就是**那一枚**。
- **`==` 比的是值**（不是身份）—— 不然拼出来的组合每次都是新对象、永远不相等。但两枚还得是**同一个** flags 的：`sub.Read == base.Read` 照旧 `false`（6.14.1 那条「同名不同枚」）。

## 6.14.3 单例（`singleton`）

它建出来的类，**每次实例化都交回同一枚值**。

```ravel
using "singleton.rav"
Config ::= singleton {
    init = () => { name = "默认"; this; }
    name: string = ""
}
a := Config ()
b := Config ()
print (a == b)
a.name = "改了"
print (b.name)
```

执行以上程序会输出如下结果：

```
true
改了
```

和 `enum` / `flags` 一样是个**元类**（`class type`），所以 `Config` 照旧是个类 —— `a is Config` 成立，成员、继承、运算符一个不少。也能从普通类继承过来：`Sub ::= singleton Base { … }`。

做法是**在类体里把 `init` 盖掉**（类体每次实例化都会跑，谁最后定义 `init` 谁说了算），存着的那枚值放在闭包的一张表里，每个类一份。

两条要知道的：

- **`init` 不收参数** —— 只在第一次用得上，而"第一次是谁"没有意义。真写成带参数的，第一次实例化就会报「参数 'x' 需要 Integer，得到 Void」，不会静默拿 `()` 去凑。
- **第一次之外，那枚实例照造不误、只是被扔掉** —— 引擎每次都是"先造一枚实例、再跑 `init`"（`init` 交回别的值是允许的，见 7.3）。所以第 N 次的开销和第 1 次一样，连类体里那些**字段初始值也会一次次重算**。别把有副作用的活儿写在字段初始值里。

注意：`Config () == a` 要写成 `(Config ()) == a` —— 实参吃到运算符为止（3.9）。

## 6.15 位（`Bits`）

运算符那边已经有 `&` `|` `^` `<<` `>>` `<<<` `>>>`；这个模块补的是它们给不了的。**要显式引用**。

| 写法 | 交回 |
|------|------|
| `Bits.Test 5 0` | `true` —— 第 0 位是不是 1 |
| `Bits.Set 5 1` | `7` —— 原值不动，交回新的 |
| `Bits.Count 255` | `8` —— popcount |
| `Bits.Width 255` | `8` —— 要几位才装得下 |
| `Bits.Mask 4` | `15` —— 低 4 位全 1 |
| `Bits.Shr 5 1` | `2` —— **逻辑**右移（`>>` 是算术的）|
| `Bits.Bytes 16909060` | `[1 2 3 4]` —— 大端（`BytesLE` 是小端）|
| `Bits.Unsigned (0 - 1)` | `4294967295` |

注意：位下标一律 **0..31**，**越界当场报错** —— 静默"什么都没做"是最难查的那种。

## 6.16 网络（`Http`）

#### 实例

```ravel
using "http.rav"
r := Http.Get "https://example.com"
print (r.status)
print (r.Ok ())
```

执行以上程序会输出如下结果：

```
200
true
```

（这条要出网 —— 用例里跑的是运行器起的**回环服务器**；`examples/http.rav` 才是真出网那份。）

| 写法 | 意思 |
|------|------|
| `r.Text ()` / `r.Bytes ()` / `r.Json ()` | 正文（按 charset 解）/ 原样字节 / 当 Json |
| `r.Header k` / `r.HeaderOr k dflt` | 响应头 |
| `r.Save "page.html"` | 正文写进文件 |
| `Http.Post url body` | `body` 给 dict / list 就**自动当 JSON**（连 `content-type` 一起补）|
| `Http.Request {…}` | 全参数（`headers` / `retries` / `backoff` / `timeout` / `follow`）|
| `Http.Query url {"q": "中文"} ` | 拼查询串（值先转义）|
| `Http.Download url path` | **边收边写**，不进内存 |
| `Http.Upload url path` | multipart，字段名默认 `file` |

几条规矩：

- **4xx / 5xx 不是错误**，那是响应 —— 自己看 `status`。想"非 2xx 就抛"用 `Http.Expect r`。
- **连不上 / 超时 / 域名解析不了**才是错误（`IoError`，消息里带 URL 和原因）。
- 老编码认：响应头写着 `charset=gbk` 的老网页照样解得对。

**一个 URL 就是一个文件** —— `Http.Url "…"` 交回的东西有 `IFile` 那一套成员，所以**对着接口写的东西直接能用**：`Io.CopyTo (Http.Url "…") (Io.File "a.txt")`。

## 6.17 摘要 / 标识 / 数据库

**`Hash`**：`Sha256` / `Sha256Bytes` / `Hmac` / `File`（流式，多大都不进内存）/ `Crc32` / `Equal`（常数时间比）/ `Token n`。

**`Uuid`**：`V4 ()`（随机）/ `V7 ()`（按时间排，当数据库主键不捅索引）/ `IsValid` / `Version` / `Time`。

**`Sqlite`**：

```ravel
using "sqlite.rav"
db := Sqlite.Open "app.db"        # 或 Sqlite.Memory ()
db.Exec "insert into t (name) values (@n)" {"n": "ada"}   # 参数按名字绑
rows := db.All "select * from t" {}                       # [{id: 1 name: "ada"}]
db.Tx { … }                                               # 事务：出错自己回滚再抛
db.Close ()
```

注意：**参数那格永远要给**（没有参数就写 `{}`）—— Ravel 没有默认参数。存得进的是 数 / 字符串 / 字符 / 布尔 / `()`（就是 NULL）/ 字节表（BLOB）。

## 6.18 数据结构（`Structures`）

**是个插件**：类和函数在独立项目里（`Ravel.Structures/`），编成 `plugins/Ravel.Structures.dll`。

| 写法 | 是什么 |
|------|--------|
| `Structures.Stack [1 2 3]` | 后进先出：`Push` / `Pop` / `Peek` |
| `Structures.Queue [1 2 3]` | 先进先出：`Enqueue` / `Dequeue` / `Peek` |
| `Structures.Deque [2 3]` | 两头都能进能出：`PushFront` / `PopBack` |
| `Structures.Heap [3 1 4]` | 每次弹出**最小**的（`Heap true` 是大顶堆）|
| `Structures.SortedDict ()` | 键永远有序 |
| `Structures.SortedSet [3 1 2]` | 升序、去重 |
| `Structures.Graph ["a" "b"]` | 无向无权：`AddEdge` / `Bfs` / `Path` |
| `Structures.Digraph ()` | 有向：`Neighbors` 是出边、`InNeighbors` 是入边 |
| `Structures.Weighted ()` | 带权（`{"directed": true}` 是有向带权），最短路走 Dijkstra |

几条规矩：

- **比大小的结构**按 Ravel 的 `<` 排 —— 比不了当场报错。相等也按 `<` 算：`1` 与 `1.0` **是同一个**（`dict` / `set` 那边按类型分，不一样）。
- **空结构上 `Pop` / `Peek` / `Min` 报错**（不是给 `()` ——「没有」和「是空值」不该长得一样）。
- 图里顶点是**值类型**；**负权在 `AddEdge` 就挡下**；**不连通当场报错**（要问"通不通"用 `HasPath`）。

## 6.19 官方扩展（`Native`）

六个库的本机半边**不在引擎里**，在一个官方扩展 dll 里：`Hash` / `Crypto` / `Http` / `Regex` / `Sqlite` / `Random`（`Math` 整个模块也在那儿）。

它们是"这台机器能干什么"，不是"这门语言是什么" —— 所以搬出了 `System`。

```ravel
using "native.rav"
print (Native.HashBytes "sha256" (Encoding.Utf8 "abc"))
```

平时用不着碰这一层：`Hash.Sha256` / `Http.Get` 那些库面才是给人用的。

## 6.20 ZIP 归档（`Zip`）

**归档是一个文件系统** —— 这是 `IFile` 那条缝许诺过的那一格：

```ravel
using "zip.rav"
z := Zip.Open "a.zip"                  # 归档是 IDir
z.Entry "a/one.txt" |> .Read ()         # 成员是只读的 IFile
Io.EachDir z (p: string e: object) => { print (e.Name ()); }   # 对着接口写的一律照吃
```

## 6.21 通配符（`Glob`）

```ravel
using "glob.rav"
Glob.Match "src/**/*.cs" "src/a/b.cs"    # true
Glob.Find "src/**/*.cs"                  # 递归找，交回相对路径（一律正斜杠）
```

四样：`*`（这一层任意个，**不跨 `/`**）、`?`、`**`（跨 `/`，零层也算）、`[...]`（`[!...]` 取反）。

注意：**按整串比** —— `*.txt` 不配 `sub/a.txt`，要"哪儿都算"就写 `**/*.txt`。

## 6.22 XML（`Xml`）

```ravel
using "xml.rav"
x := Xml.Parse "<config><port>8080</port></config>"
x.Name ()                 # config
(x.Child "port").Text ()  # 8080
Xml.Render x              # 打回文本
```

注意：`print x` 给的是**字段表** —— 要内容得显式取（`Text ()` / `Xml.Render`）。

## 6.23 终端（`Terminal`）

标记就是 **Spectre.Console 那套**（`[red]…[/]`、`[[` 表示一个方括号），不是新发明的一套。

| 写法 | 意思 |
|------|------|
| `Terminal.Red s` / `Green` / `Yellow` … | 糖：交回**标记串**，并且把你给的文本转义掉 |
| `Terminal.Render s` / `Plain s` / `Strip s` | 上色 / 不上色 / 去掉标记 |
| `Terminal.Ask "问什么"` / `Confirm` / `Choose` / `Menu` | 交互 |
| `Terminal.Panel lines` / `Bar n total width` | 面板 / 进度条 |

注意：糖**套糖不行**（里层会被当字面量转义掉）—— 要嵌套就直接写标记。

注意：上色与否看 `Terminal.Tty ()`。要确定性的输出（测试、写文件）用 `Terminal.Plain`，别用 `Render`。

## 6.24 REPL 是用 Ravel 写的（`Repl`）

多页缓冲、三维光标、按词上色、按键编辑、主菜单、会话存盘，**全用 Ravel 写**。

```ravel
using "repl.rav"
Repl.Run ()
```

**`ravel` 不带参数进的就是它**（CLI 那边就 `Repl.Run ()` 一行）。当初照 C# 那版写一遍是为了看看这门语言自己够不够用 —— 结论是够：**引擎只多了六样原语**（读单个键、清屏、定位光标、藏光标、把异常渲染成报告，再加"把 stdout 收进字符串"那一对），其余全在库里。

接管道时它不当编辑器，把喂进来的整段跑完就走：

```bash
echo 'print 1 + 1' | dotnet out/ravel.dll
```

## 6.25 小工具四件

**`Crypto`** —— 加密与口令。和 `Hash` 是两件事：那边是"防篡改"，这边是"藏起来"。

```ravel
using "crypto.rav"
key := Crypto.DeriveKey "口令" (Crypto.Salt ()) 600000   # PBKDF2-SHA256 → 32 字节
c   := Crypto.Seal key "秘密"                            # AES-256-GCM → base64
Crypto.Open key c                                        # "秘密"；拆不开就报错
stored := Crypto.HashPassword "hunter2"                  # "pbkdf2$sha256$600000$盐$哈希"
Crypto.CheckPassword "hunter2" stored                    # true（常数时间比）
```

注意：两条路分得很开 —— **藏数据**用 `Seal` / `Open`（丢了密钥就没了）；**存口令**用 `HashPassword` / `CheckPassword`（存的是"怎么校验"，**没有**"解密"那一半，那是故意的）。

**`Args`** —— 认 `--x=v` / `--x v` / `--x` / `-x` / `-abc`（合并的短开关，**不带值**）/ `--`（之后全算位置参数）。**没给规格时光杆一律当开关** —— 不然 `-v a.txt` 里的 `a.txt` 会被 `v` 吞掉。结果是张 dict：选项进同名的键，位置参数进 `"_"`。

**`Table`** —— 最要紧的是**宽度**：算的是"终端里占几格"（汉字 2、其余 1），不是 `s.Length ()` —— 拿来对齐中文会歪。

**`Log`** —— 分级日志：`Log.New {"level": "debug" "file": "app.log" "tag": "db"}`，然后 `lg.Info "…"`。

## 6.26 拼 HTML（`Html`）

```ravel
using "html.rav"

card := (title: string body: string) => {
    Html.El "div" {"class"-> "card"} [
        (Html.El "h2" {} [title])
        (Html.El "p" {} [body])
    ]
}
print (Html.Pretty (card "标题" "a < b"))
```

执行以上程序会输出如下结果：

```
<div class="card">
  <h2>标题</h2>
  <p>a &lt; b</p>
</div>
```

节点就是**普通 dict / list**（和 6.22 的 `Xml` 底下那棵树同一个形状），`Render` 收的是**数据** —— 自己手拼一棵、或者把别处拿到的树喂进去，都认。

| 写法 | 意思 |
|------|------|
| `Html.El name attrs kids` | 元素；`kids` 收一个 list（给单个也行，自动包一层）|
| `Html.Text s` / 孩子里直接写字符串 | 文本，**渲染时转义** |
| `Html.Raw s` | 原样标记，**不转义**（安全自己保证）|
| `Html.Frag kids` | 一串节点当一串用 |
| `Html.Render n` / `Html.Pretty n` | 紧凑一行 / 缩进 |
| `Html.Doc lang head body` | 整篇：doctype + `<html lang>` + `<head>`（自动补 `<meta charset="utf-8">`）+ `<body>` |

#### 块级：写正文那一层

上面那些是"元素"，这一层是"正文" —— 交回的**还是同一棵树**，两层随便混着写。

| 写法 | 意思 |
|------|------|
| `Html.H1 s` … `H6` / `P` / `Blockquote` / `Code` | 标题 / 段落 / 引用 / 行内代码 |
| `Html.Ul items` / `Ol items` | 列表，一项一个 `<li>` |
| `Html.Table headers rows` | 表格；`headers` 给 `[]` 就没有表头（不硬塞一个空 `<thead>`）|
| `Html.Pre s` | 代码块（`<pre><code>`，内容照原样，转义是渲染那一步做的）|
| `Html.A href kids` / `Img src alt` / `Hr ()` / `Br ()` | 链接 / 图 / 分隔线 / 换行 |
| `Html.Style css` / `CssLink href` | 内联 `<style>` / 外链样式表 |
| `Html.Page opts body` | 整篇；`opts` 认 `"title"` / `"lang"` / `"css"` / `"head"` |
| `Html.BaseCss` | 随库带的一份默认样式（系统字体、正文限宽、表格带框、代码浅底）—— **不会自动带上** |

```ravel
using "html.rav"
print (Html.Pretty (Html.Page {"title"-> "报表" "css"-> ["h1 { color: teal; }"]} [
    (Html.H1 "报表")
    (Html.P "正文里 < 会转义")
    (Html.Ul ["甲" "乙"])
    (Html.Table ["名称" "数量"] [["甲" 12] ["乙" 7]])
    (Html.Pre "if (a < b) { x (); }")
]))
```

执行以上程序会输出如下结果：

```
<!DOCTYPE html>
<html lang="zh">
  <head>
    <meta charset="utf-8">
    <title>报表</title>
    <style>h1 { color: teal; }</style>
  </head>
  <body>
    <h1>报表</h1>
    <p>正文里 &lt; 会转义</p>
    <ul>
      <li>甲</li>
      <li>乙</li>
    </ul>
    <table>
      <thead>
        <tr>
          <th>名称</th>
          <th>数量</th>
        </tr>
      </thead>
      <tbody>
        <tr>
          <td>甲</td>
          <td>12</td>
        </tr>
        <tr>
          <td>乙</td>
          <td>7</td>
        </tr>
      </tbody>
    </table>
    <pre><code>if (a &lt; b) { x (); }</code></pre>
  </body>
</html>
```

`<style>` 里的内容走 `Raw`（那两个标签里 HTML 实体**不解码**，转了反而是错字符），所以**别把用户输入拼进 CSS**。挂外部样式表用 `CssLink`，塞进 `Page` 的 `"head"`。

**属性**：`None` 整个不写（"有没有看情况"就用它）、`true` 光写名字（布尔属性）、list 按空格拼（`class` 最常见）、别的 `name="转义过的值"`。先后照你写的先后。

**`Pretty` 的换行规矩**：只有"自己是块级、孩子里**也有**块级"才拆行。少了后半条，`<p>行内 <code>x</code> 和 <a>链接</a></p>` 会被拆成三行 —— 行内元素之间的换行在 HTML 里**会折成一个空格**，那是改了内容。`<pre>` 自己块级、里面是 `<code>`，于是天然收成一行。发给浏览器的一律用 `Render`。

两条 Ravel 自己的坑：`[…]` 里**空格是元素分隔符**，所以孩子里每个调用得自己加括号（`[(El "a" {} []) (El "b" {} [])]`）；void 元素（`br` / `img` / …，标准里那 14 个）**给了孩子就当场报错**，不静默丢掉。

组件就是**普通函数** —— 上面那个 `card` 直接嵌进别人的孩子里。这门语言不需要另造一套模板语法。
