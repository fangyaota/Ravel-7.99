# 5、函数

## 5.1 单参数

体只有一条算式时，直接跟在 `=>` 后面：

#### 实例

```ravel
double := (x: int) => x * 2
print (double 5)
```

执行以上程序会输出如下结果：

```
10
```

体是一整块（多行，或者单行加 `;` 收尾）才写花括号：

#### 实例

```ravel
f := (x: int) => { y := x * 2; y + 1; }
print (f 3)
```

执行以上程序会输出如下结果：

```
7
```

**单参数连括号也能省**：`x => x * 2`。只收"光一个名字"，**带注解就得把括号写回来**：

#### 实例

```ravel
inc := x => x + 1
print (inc 5)
print (([1 2 3].Map x => x * 10).ToList ())
```

执行以上程序会输出如下结果：

```
6
[10 20 30]
```

`x: int => …` **不行** —— 那个 `x: int` 首先是**类型判断**（两种读法挤在一行），
要带类型就写 `(x: int) => …`。当实参也不必再包一层：`Map (x => …)` 和 `Map x => …` 一样。

**分岔只有一个：`=>` 后面见不见 `{`。** 见了就走块那条路，没见就是简写 ——
体读到换行或收尾符为止。所以**简写交不回一个块**（也写不了第二条语句），
想交回集合/字典就把括号带上、把 `{` 和 `=>` 隔开：`() => ({1 2 3})`、`() => ({"a" -> 1})`。

**注解可以省**：`(x) => …` 就是 `(x: object) => …`（谁都收得下）。只知道"有东西来了"的地方不必硬写 —— 回调尤其常见：

#### 实例

```ravel
show := (x) => { print x; }
print ([[1 2] [3 4]].Map ((p) => { (p.At 0) + (p.At 1); }))
```

执行以上程序会输出如下结果：

```
[3 7]
```

省了注解就**不在调用时挑类型**了 —— 要挑就写注解，或者在体里自己过一手（`n: int = x`）。两种写法可以混在同一个参数表里（`(a b: int)`）。

空参数是 `() => { 42; }`，调用写 `f ()`。

**参数前面能写修饰符**：`(private x: int) => …`，表就是变量定义那张（`readonly` / `public` /
`private` / `protected` …）。判据是"这个词**后面跟不跟得上一个参数**"，所以 `(private)` 收的
是一个**叫 `private` 的参数**，而不是"带修饰符的、没写名字的参数"。

和变量定义**不一样的有两处**：参数默认是 `private` 的（定义默认 `public`）—— 它是这个函数的私事；
`readonly` 在这儿是**真有检查**的。写上去的那些**读得回来**：`(currentScope ()).Lookup "x"`
交回的正是挂这个绑定的那枚 property，`.Attrs ()` 就是那张表（见 9.3）。

#### 实例

```ravel
f := (readonly y: int) => { y = 9; y; }
print (try { f 7; } (e: Exception) => { e.Message; })
```

执行以上程序会输出如下结果：

```
无法给只读变量 'y' 赋值
```

参数位解构时，修饰符跟着**拆出来的每个名字**走（`(readonly [a b]) => …` 里 `a` / `b` 各带一份）；
守卫上不能写（守卫是给已经声明过的那个参数加条件，它自己什么都不声明）。

**`by` 也在这张表里**：那个参数就是**槽**，收的是一份 `property` —— 和 `by x := property …`
一个规矩，读走 getter、写走 setter（没给 property 的话，第一次读写当场报错）。
注解管的是**写进来的值**（`(by y: int) => …`），不是那份 property 自己。

### 两条写法的坑

**圆括号 / 方括号里的换行不算数** —— 没闭合的 `(` / `[` 里，换行不当语句结束，所以表和长参数列表能写好看。**花括号不在**这条里：块就是靠换行分语句的。

**单行的块要用 `;` 收尾。** 没换行时 `{ x }` 是集合、`{"a" -> 1}` 是字典 —— `;`（和换行一样都是 Newline 记号）才是"这是块"的标记。这条**不看位置**，lambda 体同样适用：`=> { x * 2 }` 是错的（单行花括号是集合，不是"块的一种写法"），`=> { x * 2; }` 才对，而只想交回一个值就写 `=> x * 2`。多行块自然不用 `;`。

**`_ => 体` 是「通配参数」** —— 收什么都行、不绑东西，和 `(_) => 体` 一样：

#### 实例

```ravel
ignore := _ => 7
print (ignore 99)
print (ignore [1 2])
print (_ => 7)
```

执行以上程序会输出如下结果：

```
7
7
<function (_: object) => { 7; }>
```

**但体里那个 `_` 仍旧是洞**：`_ => _ + 1` 里第二层的 `_` 是隐式 lambda 的占位符，喂两次才出值
（`((_ => _ + 1) 5) 6` 是 7）。要一个能用的参数就起个名字：`x => x + 1`。

**`~x` 是 `{x;}` 的糖** —— 只吃**一个原子**，吃完整成一个块：

#### 实例

```ravel
print ((~5) ())
print (x => ~x)
print ((try { f := x => ~x + 1; } (e: Exception) => { e.Message; }))
```

执行以上程序会输出如下结果：

```
5
<function (x: object) => { x; }>
类型 Function 不支持运算符 '+'
```

吃的是**一个操作数**：原子加它那串 `.成员` / `?.` —— 所以 `~p.y?.z` 就是 `{p.y?.z;}`（`?.` 和 `.`
是一条链，自然一起进来）。**并列的实参不吃**：调用是"应用"、不是后缀，`~f 1` 是 `{f;} 1`。
块是块、不是它的值（`{5;}` 当一个值用是个**可调用的块**），要值就调它：上面那个 `(~5) ()` 就是。

体那儿最要紧：**块封口、表达式吃尽**。`x => ~x` 和 `x => {x;}` 一样到那块为止，所以 `+ 1`
落在 **lambda** 上（上面第三条）；`x => ({x;})` 走表达式那条，`+ 1` 进体（报的是 `Block` 那句）。

## 5.2 多参数（柯里化）

#### 实例

```ravel
add := (x: int y: int) => { x + y; }
print (add 3 4)
add3 := add 3
print (add3 4)
```

执行以上程序会输出如下结果：

```
7
7
```

`add 3` 交回的是**半成品**（还等着 `y`），它照样能存、能传、能再喂。语法就是 `(x: T1 y: T2) => { body }`。

## 5.3 `_` 占位符

```ravel
inc := _ + 1              # (x: object) => x + 1
both := _ + _             # (x) => (y) => x + y
is_pos := _ > 0           # (x) => x > 0
to_int := int _           # (x) => int x
```

每个 `_` 一个参数，从左到右编号。

注意：`_` 只是一个洞，脱糖后就是普通函数调用 —— 所以**填进去的位置必须真的能吃这个参数**。`print _ "!"` 会变成"先 `print x`、再拿它的返回值当函数用 `"!"`"，调用时报「值 () 不是函数」。

## 5.4 丢弃

```ravel
_ := 42          # 求值但不绑定
_ = f ()         # 调函数、丢弃结果
_ : int = 99     # 带标注地丢弃
```

## 5.5 向函数喂参（`<|` 与 `|>`）

`f <| x` 就是 `f x`（函数在左，右结合）。它**优先级最低**，所以右边整条算式都算它的实参。

#### 实例

```ravel
inc ::= (n: int) => { n + 1; }
print 1 + 2
print (inc 1 * 5)
print (inc <| 1 * 5)
```

执行以上程序会输出如下结果：

```
3
6
6
```

| 写法 | 读作 |
|------|------|
| `inc 1 * 5` | `inc (1 * 5)` —— 实参吃到运算符为止 |
| `(inc 1) * 5` | 想先算调用就自己加括号 |
| `inc <\| 1 * 5` | `inc (1 * 5)` —— `<\|` 把**右边**整个封成一个实参 |
| `xs.Count () \|> .ToString ()` | `(xs.Count ()).ToString ()` —— 右边是 `.成员`：挂到左边那个值上 |
| `5 \|> inc` | `inc 5` —— 右边是别的：左边当**实参**喂进去 |
| `5 \|> add 1` | `add 1 5` —— 右边自己的实参先喂，左边排**最后** |
| `a \|> f \|> g` | `g (f a)` —— 一路往右喂 |
| `x \|> ?.f ()` | `?.` 的空值穿透接在管道上：`None` 短路、`Some x` 拆开走一趟再包回去（见 2.5）|

**`|>` 右边是 `.成员` 时非有不可**：`.成员` 比并列的调用绑得紧，所以 `xs.Count ().ToString ()` 会被读成 `xs.Count ((().ToString ()))`。右边不是 `.成员` 时它就是个"喂进去"的管道（`x |> f` ≡ `f x`）。

注意：喂进去时左边**排在最后** —— `5 |> add 1` 是 `add 1 5` 而不是 `add 5 1`。柯里化的东西多半是"最后那个参数才是主角"，所以这个顺序才对。

#### 实例

```ravel
add ::= (a: int) => { (b: int) => { a + b; }; }
inc ::= (n: int) => { n + 1; }
print (5 |> inc)
print (5 |> inc |> inc)
print (2 |> add 1)
print (5 |> inc |> .ToString () |> .Length ())
print (5 |> +.2)
```

执行以上程序会输出如下结果：

```
6
7
3
1
7
```

注意：`<|` **吃不掉**「实参吃到运算符为止」那条 —— 它是那条规矩**唯一的例外**，所以 `<|` 后面能接一整串调用：

#### 实例

```ravel
add ::= (a: int) => { (b: int) => { a + b; }; }
print (add 1 <| 7)
```

执行以上程序会输出如下结果：

```
8
```

注意：`$` 不是运算符 —— 它只出现在字符串里（插值 `${…}`）。

**`>>` 把两个函数接起来**（先左后右，和 `|>` 同向）：

#### 实例

```ravel
inc := x => x + 1
dbl := x => x * 2
print ((inc >> dbl) 5)
print (1 |> (inc >> dbl))
```

执行以上程序会输出如下结果：

```
12
4
```

`x |> (f >> g)` 和 `x |> f |> g` 是一回事。**和算术右移同名**，但落在不同的类型上
（`8 >> 1` 还是 4）—— 和 `|` 一个路子：整数上是位或、函数上是交替。

它收**一个**实参，组合出来的是一元函数；多参的先部分应用一手（`(add 1) >> f`）。
不部分应用直接组合不会静默——第二层收到一个函数，当场报类型错。

### 函数分派：守卫与多子句

参数表**最后一项**写成一条表达式，它就是这一支的**准入条件**（参数名取它最左边那个标识符）：

#### 实例

```ravel
f := (v == 1) => { "一"; } | (v == 2) => { "二"; } | (_) => { "别"; }
print (f 1)
print (f 9)
```

执行以上程序会输出如下结果：

```
一
别
```

守卫不成立就**拒收** —— `|` 的交替接住它、试下一支。所以**守卫 + 多子句 = 多子句函数**
（不带守卫的多子句也成立，那是按**参数类型**分：`(v: int) => … | (v: string) => …`）。

守卫里**调函数**也行，那一支「不收这个实参」的 TypeError 同样算“不匹配”：

#### 实例

```ravel
IsPrime := (n: int) => { n > 1; }
g := (v |> IsPrime) => { "素数"; } | (_) => { "不是"; }
print (g 7)
print (g "字符串")
```

执行以上程序会输出如下结果：

```
素数
不是
```

要盯**类型**就把**整个参数**括起来写 `|>` 那条（`((v: int) |> IsPrime) => …`）—— 类型落在那一格上，
名字只写一遍。
**参数名不许重**：`(a a) => …` 和 `(x: int x > 10) => …` 都当场报错 ——
后者从前被读成"守卫挂在 `x` 上"的**一个**参数，可写出来就是两个 `x`：读的人当双参、
写的人多半也是手滑。

**边界**：守卫**只能写最后一项**（它是一条表达式，会把后面那一项吞进去）；
里面冒出**非 TypeError** 的错（名字打错之类）照旧当场报出来，不会静默变成“不匹配”。

### 谓词级的与 / 或

`f or g` 折成 `x => (f x) || (g x)`，`f and g` 是 `&&` —— 短路，和运算符那边一个脾气：

#### 实例

```ravel
isPos := _ > 0
isEven := (x: int) => (x % 2) == 0
print (([1 2 3 4].Where (isPos and isEven)).ToList ())
```

执行以上程序会输出如下结果：

```
[2 4]
```

`or` / `and` 是**中缀运算符**，所以括号不是必须的 —— 实参本来就吃运算符：

#### 实例

```ravel
isPos := _ > 0
isEven := (x: int) => (x % 2) == 0
print (([1 2 3 4].Where isPos and isEven).ToList ())
```

执行以上程序会输出如下结果：

```
[2 4]
```

优先级照着 `||` / `&&`：`and` 紧、`or` 松，于是 `a or b and c` 是 `a or (b and c)`；同级左结合。

**`or` / `and` 只有在中缀那个位置才是运算符**，别处照旧是普通名字（`or := 42` 成立，
`o.or`、`(or: int) => …` 也不受影响）。代价就一条：它们**当不了裸实参**了 ——
想传一个叫 `or` 的值，加个括号：`f (or)`。写成 `print or` 会报
「'or' 是中缀运算符，后面得跟个操作数」。

`or` / `and` 不是布尔运算 —— 布尔那对是 `&&` / `||`。`true and false` 拿到的是个**函数**
（要 `(true and false) ()` 才是 `false`），因为它俩收的是谓词。

## 5.6 函数名与打印

`::=` 定义变量，并把变量名填进 `FunctionVal.Name`。

**打印一个函数打出来的是它的代码**，`::=` 命名的带上名字；已经喂过的实参跟在 `applied` 后面。

#### 实例

```ravel
add ::= (x: int) => { x + 1; }
print (typeof add)
add.name = "sum"
print add.name
add2 := (x: int y: int) => { x + y; }
print (add2 3)
```

执行以上程序会输出如下结果：

```
Function
sum
<function (y: int) => { x + y; } applied x=3>
```

签名里是**还等着**的参数 —— 两者合起来就是完整的形状。体太长会截断（它不是给你复制代码用的，是给你认出"这是哪个函数"的）。

## 5.7 缓存函数的返回结果（`Cached`）

`Cached count f` 把 `f` 的结果记下来，同一组实参再来就直接给记下的那个。`count` 是 **`f` 的参数个数**。

#### 实例

```ravel
Calls := 0
slow := (x: int) => { Calls += 1; x * 10; }
c := Cached 1 slow
print (c 1)
print (c 1)
print (c 2)
print Calls
```

执行以上程序会输出如下结果：

```
10
10
20
2
```

最典型的用法是**给递归函数自己套上**：函数体里读的是外层那个名字，调用时才解析，所以把缓存版的名字给它，递归的每一层就都走缓存了。

#### 实例

```ravel
FibCalls := 0
fib: object = 0
fib = Cached 1 ((n: int) => { FibCalls += 1; n < 2 { n; } { (fib (n - 1)) + (fib (n - 2)); } })
print (fib 25)
print FibCalls
```

执行以上程序会输出如下结果：

```
75025
26
```

注意：`fib: object = 0` 那行的标注别省 —— `:=` 会把类型钉在初值的类型上，而这里得先有个名字让它能被递归引用、再换成缓存版。

注意：`(fib (n - 1)) + (fib (n - 2))` 那两对括号**不能省**。按「实参吃到运算符为止」（3.9），`fib (n - 1) + fib (n - 2)` 会被读成 `fib ((n - 1) + fib (n - 2))`。

几条规矩：

| 规矩 | 说明 |
|------|------|
| 喂够 `count` 个才算一次完整调用 | 柯里化喂到一半的不进缓存，继续喂到 `count` 个才查 |
| 实参**逐个**用 `==` 认，而且**先比类型** | 原子值比值（`1` 与 `"1"` 是两个键），对象 / 函数 / 容器按身份 |
| 没有上限、不过期 | 这就是记忆化；要限容在外面自己包一层 |
| 0 当场报错 | 不然那个函数会一直收参数、永远不给结果。无参函数写 `Cached 1`，调用时喂 `()` |

它是**函数**（不是要实例化的类型），拿到手直接调；每次 `Cached …` 都是**独立**一份缓存。

## 5.8 Option（可能没有值的包）

`Some 5` 包着一个值，`None` 什么都没有。两者**类型相同**（都是 `Option`），所以问有没有值要用 `IsSome ()`。

#### 实例

```ravel
print ((Some 5).IsSome ())
print (None.IsSome ())
print ((Some 5).Value ())
print (None.ValueOr 0)
```

执行以上程序会输出如下结果：

```
true
false
5
0
```

链式操作用 `Map` / `Bind` / `Where`，它们**对 `None` 一律原样放过** —— 中间落空一次，后面全不用算：

#### 实例

```ravel
safeDiv := (a: int b: int) => { b == 0 { None; } { Some (a / b); } }
ok := (Some 100).Bind (safeDiv 100)
print ((ok.Bind (safeDiv 10)).Value ())
print ((((Some 100).Bind (safeDiv 0)).Bind (safeDiv 10)).IsSome ())
```

执行以上程序会输出如下结果：

```
10
false
```

| 方法 | 做什么 |
|------|--------|
| `IsSome ()` / `IsNone ()` | 有没有值 |
| `Value ()` | 解包。`None` 上当场报错（不拿 `()` 糊弄）|
| `ValueOr d` | 解包，没有值就给 `d` |
| `OrElse m` | 没有值就换成 `m` |
| `Map f` | 有值就包上 `f` 的结果；`None` 上 `f` 根本不会被调 |
| `Bind f` | 有值就把 `f` 的结果**摊平**接上（`f` 自己得回一个 `Option`）|
| `Where p` | 不满足 `p` 就变成 `None` |
| `Exists p` | 有值且满足 `p` |

注意：`None` 是**一个值**（单例，不用写 `None ()`），所以 `x == None` 成立（对象按身份比）。

### Option 本身就是一串 —— 0 个或 1 个

#### 实例

```ravel
print ((Some 5) : IEnumerable)
foreach (Some 7) (x: int) => { print x; }
foreach None (x: int) => { print "不会到这儿"; }
print (Seqs.Flatten [(Some 1) (None) (Some 3)])
print ((Some 5).Count ())
print ((None).ToList ())
```

执行以上程序会输出如下结果：

```
true
7
[1 3]
1
[]
```

整串方法（`ToList` / `Count` / `First` / `Fold` / `Take` …）由 `IEnumerable` 的**默认实现**给。但 `Map` / `Bind` / `Where` 还是**上面那三条**（类链上先命中）—— 它们在这族的语义是"包着 / 摊平"，`(Some 5).Map f` 交回的仍是 `Option`。

注意括号：`(Some 5).Map f` 这对括号**不能省**（`Some 5.Map f` 是 `Some (5.Map f)`）；`Map` 的结果也要套括号，否则 `.Value` 会贴到 `f` 上。

### Expected（可能出错的那半）

和 `Option` 同一个形状，区别只在"没有"的那半 —— 那边是**没有值**（`None`），这边是**一句报错**（`Err e`），所以多一条 `Error ()`。

主要用途是**给一次调用兜底**：`Expect argCount f` 把函数包起来，成功包成 `Ok v`、报错包成 `Err e` —— 报错不往外抛。

#### 实例

```ravel
add := (a: int b: int) => { a + b; }
safeAdd := Expect 2 add
print ((safeAdd 1 2).Value ())
print ((safeAdd 1 "x").IsErr ())
print ((safeAdd 1 "x").Message ())
```

执行以上程序会输出如下结果：

```
3
true
参数 'b' 需要 Integer，得到 String
```

注意：`argCount` 数的是**喂几口** —— `f := () => { … }` 要 `Expect 1 f`（那一口是 `()`）。给错元数不会静默，那也是一条 `Err`。

## 5.9 do 块

一串 `Bind` 写起来很别扭（嵌套括号 + 给 lambda 标类型），所以有 `do`。`:<` 读作「从这个 Monad 里取出值、给它绑个名字」。

#### 实例

```ravel
print (do { v :< Some 10; Some (v * 2); }.Value ())
```

执行以上程序会输出如下结果：

```
20
```

整块等价于 `(Some 10).Bind ((v: object) => { Some (v * 2); })`。

几条规矩：

| 规矩 | 说明 |
|------|------|
| **最后一条语句的值就是整块的值** | 通常是 `Some …`；以 `:<` 收尾会报错（那样整块没有值）|
| 中间任意一条落空，后面全不算 | 这正是 `Bind` 的行为 |
| 块里可以有普通语句 | 定义、赋值都行，落在同一个 lambda 体里，还能改绑来的名字 |
| 绑出来的名字标 `object` | 想要具体类型就在块里自己过一手：`n: int = x` |
| `do` 是个普通表达式 | 当参数、当返回值、跟 `Map` 串都行 |

注意：`do` **只是语法糖**，运行时不为它添任何东西 —— 打印一个含 `do` 的函数会看到脱糖后的 `Bind` 链。

注意：`:<` 是词法里的**一个** token。挑这个形状而不是 do-notation 常见的 `<-` 是有意的：`<-` 一进 token 表，`x < -1` 那类写法就再也切不出词了。

### 序列也是 monad：`do` 块就是列表推导

`IEnumerable` 也在这一族里（`Bind` 是"每个元素交回一串、接起来"，即 flatMap），于是 `do` 在序列上就是列表推导。

#### 实例

```ravel
pairs := do {
    x :< [1 2]
    y :< [10 20]
    [x y];
}
print (Seqs.Gather pairs)
print (Seqs.Gather (do { x :< [1 2 3]; [x * x]; }))
```

执行以上程序会输出如下结果：

```
[1 10 1 20 2 10 2 20]
[1 4 9]
```

注意：最后一条语句得交回**一串**（`Bind` 要的就是"每个元素变一串"），单层时那正是 `Map`。

注意：**两边都可以是无限的**，只要下游 `Take` 得住：

#### 实例

```ravel
Nats := Generator (y: function) => { n := 0; while { true; } { y n; n += 1 } }
sums := do { a :< Nats; b :< Nats; [a + b]; }
print (Seqs.Gather (sums.Take 5))
```

执行以上程序会输出如下结果：

```
[0 1 2 3 4]
```

反方向（一串 → Option）用 `xs.TryFirst ()` / `xs.TryFind p`，不用去 `try` 里接 `First` / `Find` 那句错。

## 5.10 IO Monad（把效果做成值）

`IoMonad.PutStrLn "hi"` **什么都不打印** —— 它交出的是一份**说明书**。直到 `Perform ()` 那一刻，效果才真的发生。

#### 实例

```ravel
using "iomonad.rav"
Hello := IoMonad.PutStrLn "hello"
print "还没跑"
Hello.Perform ()
print "跑完了"
```

执行以上程序会输出如下结果：

```
还没跑
hello
跑完了
```

说明书可以传、可以拼、可以放着不跑，也可以跑第二遍。`do { … }` 拼出来的那一长串，在 `Perform ()` 之前一个字符都没输出。

| 名字 | 做什么 |
|------|--------|
| `IoMonad.Action f` | 造一份说明书（`f` 收 `()`，**在 `Perform` 时才被调用**）|
| `.Perform ()` | 跑它。效果世界的边界 |
| `.Map f` / `.Bind f` | 结果过一道纯函数 / 接上另一份说明书（`Bind` 摊平）|
| `IoMonad.Return v` | 不做效果，只交出 `v` |
| `IoMonad.PutStrLn s` / `PutStr s` | 打印（带 / 不带换行）|
| `IoMonad.GetLine` | 读一行。它是**一个值**，每次 `Perform` 都真读一次 |
| `IoMonad.Foreach xs f` | 对表里每个元素造一份说明书并依次执行 |
| `.Then next` | 接着做下一份，交回**后一份**的结果 |
| `.Attempt ()` | 跑它：成功 `Some 结果`、失败 `None` |
| `.Catch handler` | 出错了换一份说明书接着做 |
| `IoMonad.Sequence xs` | 一串说明书 → 一份，交出结果的表 |
| `IoMonad.When c body` / `Unless c body` | 条件到**那一刻**再算（所以 `c` 是个函数）|
| `PutStrLnErr s` / `PutStrErr s` | 写到**标准错误** |

注意：IO 里**没有「落空」这回事** —— 每一步都跑，值一路往下传（和 `Option` 的短路正好相反）。

注意：`.Perform` 要贴给调用的**结果**时，用 `|>` 或者括号：`… |> .Perform ()`。

## 5.11 文件系统（`Io`）

**要显式引用**：`using "io.rav"` 之后 `Io.xxx` 才是一个名字。

```ravel
d := Io.Dir "notes"
d.Create ()                       # 建目录（已有就不管）
f := d.Child "a.txt"              # 子条目：不存在就当一个文件
f.Write "第一行
第二行
"
print (f.Read ())                 # 整个文件一个字符串
print (f.Lines ())                # 按行切开
f.Append "尾巴"
print (f.Size ())                 # 字节数
print ((d.List ()).Keys ())       # 列目录：名字 → 条目，按名字排序
d.Mkdir "sub"
f.CopyTo "notes/b.txt"            # 复制 / 移动 / 改名：`MoveTo` / `Rename`
f.Delete ()
```

**报错**是中文、带路径的；`Exists ()` 是**问**，不报错：

```
读文件: 找不到文件 —— notes/nope.txt
```

路径基准是进程当前目录（`Io.Current ()` / `Io.ChDir p`），没有沙箱。路径的活交给 `Io.Join` / `Io.DirName` / `Io.BaseName` / `Io.Ext`。

### "文件"是个接口

`IEntry` / `IFile` / `IDir`（**全局名**）才是本体：磁盘上的、内存里的、zip 条目、远程的，都只是实现。**实现子接口也就实现了父接口**（`IFile ::= interface IEntry`）。

```ravel
describe := (f: IFile) => { f.Name () + " = " + f.Read (); }
```

库里**已经有四种**：

| 实现 | 是什么 |
|------|--------|
| `Io.File` / `Io.Dir` | 磁盘 |
| `Terminal.Stdout` / `Stderr` / `Stdin` | 控制台 |
| `Io.MemFile` / `Io.MemDir` | 内存 |
| `Io.JsonNode`（`Io.JsonFile "conf.json"`）| 一棵 JSON 树当目录用 |
| `Http.Url "https://…"` | 远程（要 `using "http.rav"`）|

`Io.CopyTo` / `Io.EachDir` / `Io.Lines` 就是**对着接口写的**那三段：任何实现都吃。`Io.Lines f` 是个**生成器**，边要边给。

注意：控制台那三个只写 / 只读，报错说人话（"这是只写的（stdout），读不了"）；`Terminal.Stdin.Read ()` 是**读到 EOF**（终端上要 Ctrl+Z / Ctrl+D 收），测试里别碰它。

### 什么时候值得用 `IoMonad` 包一层

`Io` 那几条是**直接做**的：`f.Read ()` 那一刻就读了。想要"先拼好一串要做的效果、之后再 `Perform ()`"（日志、事务那种），用它对应的 **Action 版本** —— 做的是同一件事，只是先攒着：

```ravel
job := Io.ReadAction f |> .Map ((t: string) => { t.Length (); })   # 到这儿什么都没读
print (job.Perform ())
```

## 5.12 比较与排序

比大小的协议是 **`CompareTo`**：收对方、交回 int —— 负数=我小、0=一样、正数=我大（和 C# 同约定）。

**每个值都有这条**（引擎在 `Object` 上放了一条，口径就是内建那把尺子），自己写的类型想比就在类里写一条盖掉它、再登记一下：

```ravel
Rec ::= class {
    K: int = 0
    CompareTo := (o: Rec) => { K < o.K { -1; } { K > o.K { 1; } { 0; } } }
}
impl (IComparable Rec { () })
```

注意：这里**不能用** `by CompareTo = property …` 现装 —— 这个名字 `Object` 上已经有了，成员查找是**类链先说话**。

| 写法 | 意思 |
|---|---|
| `Sorting.Compare a b` | 谁大谁小 |
| `Sorting.Sort xs` | 排一个表，**稳定** |
| `Sorting.SortBy xs key` | 按 `key` 算出来的键排 |
| `Sorting.Max xs` / `Min xs` | 最大 / 最小（**空表报错**）|
| `Sorting.MaxBy xs key` / `MinBy xs key` | 按 key 挑 |

注意：引擎另有一套 `xs.Sort ()` / `xs.Min ()` / `xs.Max ()`，走的是 `<` 那把尺子 —— **只认内建标量**，碰见对象只会说「比不了 Rec 与 Rec」。库里这套按 `CompareTo` 比。

## 5.13 键与查找

字典的键**只能是值类型**（数、字符串），而且比的就是值自己：

#### 实例

```ravel
d := {}
d.Set 1 "int"
d.Set "1" "str"
d.Set 1.0 "real"
print ((d.Keys ()).Count ())
print (d.Get 1)
```

执行以上程序会输出如下结果：

```
3
int
```

注意：`->` 左边是**键**、右边是值，两边都是**表达式** —— `{"a"-> 1}` 的键就是那个字符串（引号不能省）；`{k -> 1}` 的键是变量 `k` 求出来的值，`k` 没定义就报「未定义的名字 'k'」。

**值语义的键**靠 `IKey`：协议是 `Key ()` —— 交回"我当键时等于什么"（一个值类型）。自己写的类型在类里写一条盖掉它、再登记：

#### 实例

```ravel
Rec ::= class {
    X: int = 0
    init = (x: int) => { X = x; this; }
    Key := () => { X; }
}
impl (IKey Rec {
    ()
})
d := {}
d.Set (Rec 1) "one"
print (d.Get (Rec 1))
```

执行以上程序会输出如下结果：

```
one
```

**为什么分两层**：`d.Get` / `d.Set` / `d.Has` / `d.Remove` / `d.GetOr` 是库里**注入**给 `dict` 的（`lib/keys.rav` 的 `IDict` 那五条槽），它们先把键**规范**成值类型、再转到引擎那套 `SysGet` / `SysSet` 上。引擎那层做不到不是口味问题，是**够不着** —— 它是同步的 C#，而 `Key ()` 是 Ravel 函数，且查表那一步发生在 .NET 字典内部，插不进帧。

**要把原对象拿回来，用 `Keyed`**（它多记一张"规范键 → 原键"，也登记了 `IDict`）：

#### 实例

```ravel
Rec ::= class {
    X: int = 0
    N: string = ""
    init = (x: int n: string) => { X = x; N = n; this; }
    Key := () => { X; }
}
impl (IKey Rec {
    ()
})
t := Keyed ()
t.Set (Rec 1 "甲") "one"
print (t.Get (Rec 1 "别的"))
print ((t.Keys ()).Map ((r: Rec) => { r.N; }) |> .ToList ())
```

执行以上程序会输出如下结果：

```
one
[甲]
```

注意：**`Key` 要交回稳定的值类型**（`() => { X; }` 这样）。交回一个新造的容器（`[X]`）不行，`Keys.Of` 当场报错。

注意：**不能现装**（`by Key = property …` 用不了）—— 和 `CompareTo` 同一个道理。

## 5.14 跑外部命令（`cmd`）

```ravel
r := cmd "git status --short"
print ((r.Get "out").Trim ())
(r.Get "code") != 0 { print ("失败了:" + (r.Get "err")); }
```

走**系统 shell**（Windows 是 `cmd.exe /c`，别处 `/bin/sh -c`）—— 管道、重定向、通配符都归它管。交回一张 dict：`out` / `err` / `code`。

- **非零退出码不是错误** —— 程序失败是常事，`code` 原样交给你判断；真正起不来进程才报错。
- 输出**先试 UTF-8、不合法退回控制台编码**（git / python 吐 UTF-8，`dir` 那类走控制台那套）。
- 命令本身可以是算出来的：`cmd ("echo " + string n)`。
- **不做沙箱**。

## 5.15 JSON

#### 实例

```ravel
j := Json {"a"-> [1 2.5 ()] "b"-> {"c"-> true}}
print (j)
print ((j.Get "a").Count ())
print (((j.Get "a").At 1).Extract ())
k := Json.FromString "{\"x\": 1}"
print ((k.Get "x") |> .Extract ())
```

执行以上程序会输出如下结果：

```
{"a":[1,2.5,null],"b":{"c":true}}
3
2.5
1
```

`Json` 里包着的是一棵**还没转成原生值**的树，所以可以先看再转：

- **看**：`Kind ()` / `IsNull ()` / `Get k` / `GetOr k dflt` / `At i` / `Count ()` / `Keys ()`（保 JSON 原顺序）—— 交回的还是 **Json**，可以一层层往下走。
- **转**：`Extract ()` 一次拿原生值 —— `dict` / `list` / `int`（太大退 `bigint`）/ `real` / `string` / `bool` / **`()`**（JSON 的 null）。
- **写**：`Json v` → `j.Text ()`（紧凑）或 `j.Text 2`（缩进两格）。JSON 里没有的类型（自有类、分数…）**当场报错**，不悄悄降级。
- **改**：`Set k v` / `SetAt i v` / `Add v` / `Remove k` / `RemoveAt i` —— 直接改那棵树。

注意：`Get` / `At` 交回的是**那棵树的窗口**，改它父的那份也跟着变。

注意：**`null` 转出来是 `()`** —— 转完就分不清"值是 null"和"函数没返回值"了，要问就在 Json 那层问 `IsNull ()`。

注意：`At i` 后面接 `.Extract` 要加括号（`.成员` 比并列调用紧）：`((j.Get "a").At 1).Extract ()`，或者 `(j.Get "a").At 1 |> .Extract ()`。

## 5.16 时间（`Time`）

一个时刻就是**毫秒数**（1970-01-01 UTC 起，`bigint`），库把它包成 `Time`。

#### 实例

```ravel
t := TimeFrom "2026-10-01 12:34:56" IsoFormat
print (t.Text ())
print (t.Text "yyyy/MM/dd")
print (t.Year ())
print (t.WeekdayName ())
print ((t.AddDays 1).Text ())
print (t < (t.AddDays 1))
```

执行以上程序会输出如下结果：

```
2026-10-01 12:34:56
2026/10/01
2026
周四
2026-10-02 12:34:56
true
```

- 比大小比**毫秒数**（`CompareTo`），登记进了 `IComparable` —— `Sorting.Sort` / `Max` / `Min` 直接吃。
- 时区**本地**；加减就是毫秒算术（不跟夏令时那套，简单可预期）。
- `print t` 打的是**字段快照**（`Ms` 和一袋零件），不是格式化后的样子 —— 要文本用 `t.Text ()`。
