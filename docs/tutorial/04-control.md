# 四、控制流

## 4.1 if

```ravel
if { x > 0; } {
    print "positive";
} {
    print "non-positive";
}
```

`if` 接受三个块：条件、then、else。

**它不是内建，是库函数** —— `Bool` 挂在 `Function` 下，`true` / `false` 本身就能选块：

```ravel
print (true  { "then"; } { "else"; })
print (false { "then"; } { "else"; })
print ((1 < 2) { "yes"; } { "no"; })
```

执行以上程序会输出如下结果：

```
then
else
yes
```

所以 `if { c; } { t; } { e; }` 等价于 `c { t; } { e; }` —— 后者少进一次 `if`、少造一个块，**热路径上写后者**（`while` 和 `foreach` 内部就是直调版）。

## 4.2 while

```ravel
i := 0
while { i < 5; } {
    print i;
    i += 1;
}
```

## 4.3 foreach

```ravel
foreach [1 2 3] (x: int) => { print x; }
```

`foreach` 也是库函数，吃的是 **`IEnumerable`**（`lib/iterator.rav` 里照 C# 那个形状写的接口）。三种容器都实现了它：

| 写法 | 迭代的是 |
|------|----------|
| `foreach [1 2 3] (x) => { … }` | 元素 |
| `foreach {1 2 3} (x) => { … }` | 元素（集合）|
| `foreach {"a": 1} (v) => { … }` | **值**（不是键）|

不是 `IEnumerable` 的东西当场报错：

```ravel
foreach 5 (x: int) => { print x; }
```

执行以上程序会输出如下结果：

```
Error: foreach 需要 IEnumerable（能按顺序交出一串的东西：list / set / dict / string / Generator / 枚举器 / Option…），得到 Integer
```

`foreach` 展开就是枚举器那个循环：

```ravel
e := xs.GetEnumerator ()
while { e.MoveNext (); } { f (e.Current); }
```

**每次进来都新开一个枚举器**，所以嵌套遍历同一串值互不打扰（和 C# 一样）。

### 枚举器自己也是一串 —— 走一遍就消耗掉

`IEnumerator <: IEnumerable`，它交回的枚举器就是它自己（像 C# 里 `yield` 生成的那个类）。于是**两条语义**：遍历的是「还没走完的那一段」，而且走一遍就把它**吃掉**了。

#### 实例

```ravel
e := Enumerator [1 2 3]
print (e.Count ())
print (e.Count ())
```

执行以上程序会输出如下结果：

```
3
0
```

生成器的光标也一样：

#### 实例

```ravel
c := (Generator (y: function) => { y 1; y 2; y 3; }).GetEnumerator ()
print (c.First ())
print (c.ToList ())
```

执行以上程序会输出如下结果：

```
1
[2 3]
```

### 实现了 `IEnumerable`，就白拿一整套

接口的类体里写着整套方法的默认实现（接口的类体会在每个实现对象上跑一遍，所以"实现体不填就用它"）：

```ravel
G := Generator (y: function) => { y 1; y 2; y 3; }
G.Count ()      # 3
G.Sum ()        # 6
G.Join "-"      # 1-2-3
G.Max ()        # 3
G.Fold 0 (acc: int x: int) => { acc + x; }   # 6
```

**分界线**：交回**一串**的惰性，要**一个值 / 容器**的当场算。

| 惰性（一串进、一串出） | 急切（要值 / 容器） |
|---|---|
| `Map` `Where` `Bind` `Take` `Skip` `Concat` `Distinct` `Reverse` `SortBy` | `Count` `IsEmpty` `First` `Last` `At` `Contains` `Sum` `Min` `Max` `Join` `ToList` `ToSet` `ToGenerator` `Each` `All` `Any` `Find` `Fold` `TryFind` `TryFirst` |

无限流上只有惰性那批用得起来：

#### 实例

```ravel
Nats := Generator (y: function) => { n := 0; while { true; } { y n; n += 1 } }
print (Seqs.Gather (Nats.Take 4))
doubled := Nats.Map ((n: int) => { n * 2; })
print (Seqs.Gather (doubled.Take 3))
```

执行以上程序会输出如下结果：

```
[0 1 2 3]
[0 2 4]
```

注意：容器上那几个**同名方法**是引擎给的（在类链上先命中），行为一样、只是**急切** —— `[1 2 3].Map f` 交回 `list`，`G.Map f` 交回 `Generator`。要转就 `ToList ()` / `ToGenerator ()`。

自己的类交回一个枚举器（库里的 `Enumerator` 拿来就能用）也能进 `foreach`：

```ravel
use (IEnumerable MyThing {
    by GetEnumerator = property (() => { () => { Enumerator [1 2 3]; }; }) ((v: function) => { (); })
})
```

### 自己造一串：`Generator`

把「往外送值」的那段代码交给 `Generator`，体的参数 `y` 是投喂口：**`y v` 送出一个值并挂起**，消费者再要下一个才接着跑。

#### 实例

```ravel
Nats := Generator (y: function) => {
    n := 0
    while { true; } { y n; n += 1 }
}
five := callcc (stop: function) => {
    i := 0
    out := []
    foreach Nats (x: int) => {
        if { i >= 5; } { stop out; } { 0; }
        out.Add x
        i += 1
    }
}
print five
```

执行以上程序会输出如下结果：

```
[0 1 2 3 4]
```

注意：**每次 `foreach` 都从头跑一遍体**（体是配方，和类体一个规矩）—— 同一个 `Generator` 遍历两次，两次各自从头。要"一次性"就在外面用变量兜住。

注意：能 `foreach` 的东西都能包成生成器（`Generator (y) => { foreach xs (x) => { y x; } }`），生成器也能套生成器做扁平化。

## 4.4 match

`match v cases dflt` 从头试每一对 `[条件 结果]`，第一对**条件为真**的交出它的结果；一对都不真就给 `dflt`。

条件是**谓词** —— `is.int` 这种**运算符节**就是现成的那个。

#### 实例

```ravel
kind := (v: object) => {
    match v [
        [:.int    {"整数";}]
        [:.bool   {"布尔";}]
        [:.string {"字符串";}]
    ] {"别的";}
}
print (kind 3)
print (kind true)
print (kind 1.5)
```

执行以上程序会输出如下结果：

```
整数
布尔
别的
```

想**比值**也这么写（节就是"左操作数留空"的函数）：

#### 实例

```ravel
sign := (n: int) => { match n [ [<.0 {"负";}] [==.0 {"零";}] ] {"正";} }
print (sign (0 - 5))
print (sign 0)
print (sign 5)
```

执行以上程序会输出如下结果：

```
负
零
正
```

注意：**结果和默认值都得是「可调用的东西」**，推荐写成块 —— 只有命中那一支（或默认那支）会跑，和 `if` 一个规矩。拿**普通值**糊弄会当场报错（「match 第 1 对的结果要写成块」），不悄悄当成"进 `match` 之前就算好的值"。

注意：条件是普通函数也行（`[[IsLong {"长";}]]`）；每对必须**正好两项**，不是就当场报错（说清是第几对）。

## 4.5 早期退出

### `return`：函数里提前交回一个值

函数里"提前交回"太常见，所以有专门的糖。**它归一个开关管，默认关着**（`break` / `continue` 同理）：

- 命令行：`ravel --more-control-flow 你的脚本.rav`（整次运行都开）
- 文件头：`#program --more-control-flow=true`（只这个文件开）

```ravel
#program --more-control-flow=true
f := (n: int) => {
    if { n < 0; } { return 0; } { 0; }
    n * 2
}
print (f 5)
print (f (-3))
```

执行以上程序会输出如下结果：

```
10
0
```

它是**上下文关键字** —— 只在**语句开头**认：`return := 5` 还是定义，`x.return` 还是成员。裸写 `return` 交回 `()`。

出的是**最近一层用户写的函数**。两条推论：`foreach` 的体本身就是一个 lambda，所以在那儿写 `return` 只出那一趟的体；而 `do` / `?.` 那些**内部消糖**造的 lambda 不算一层（它们只是把表达式挪个地方），`return` 不会被截住。

关着的时候，`return` / `break` / `continue` 就是三个**普通名字**（能拿来做变量、做成员），和从前一模一样 —— 加了三个糖，但不动老代码的写法。

它脱糖成对 `__return` 的一次调用 + 在函数体外面包一层 `callcc` —— 也就是下面那个通用做法，只是不用你手写。要跳出循环、或者跳出好几层，还是得自己用 `callcc`。

### `break` / `continue`：循环里的两个跳转

同一个开关（`--more-control-flow`）下还有这两个。语法是熟的：`break` 跳出循环，`continue` 跳过这一轮。

```ravel
#program --more-control-flow=true
i := 0
while { i < 10; } {
    i += 1
    if { i == 3; } { break; } { 0; }
}
print i

s := 0
foreach [1 2 3 4 5] (x: int) => {
    if { (x % 2) == 0; } { continue; } { 0; }
    s += x
}
print s
```

执行以上程序会输出如下结果：

```
3
9
```

**要跳出好几层就给标签** —— `@标签 <语句>`，然后 `break 标签` / `continue 标签`：

```ravel
#program --more-control-flow=true
@outer while { true; } {
    foreach [1 2 3] (x: int) => {
        if { x == 2; } { break outer; } { 0; }
        print x
    }
}
print "跳出来了"
```

执行以上程序会输出如下结果：

```
1
跳出来了
```

注意：**`while` / `foreach` 是库函数，所以解析器只按名字认它们** —— 它知道"哪个调用算循环"的**唯一依据**就是这两个名字。自己写的循环函数（比如 `Repeat n { … }`）不在内，那种要用标签指。

注意：`continue 标签` 跳的是**那个循环的这一轮**，`break 标签` 跳的是**整个那条语句**。标签没人指会当场报错。

注意：`foreach` 的体是个 lambda，所以在那儿写 `return` 出的是**那一趟的体**，`continue` 才是"跳过这一轮"。

### 通用做法：`callcc`

调一个续延会**丢弃当前帧链**、把值当作 `callcc` 表达式的返回值从捕获点继续 —— 所以它是逃出多层嵌套的办法。

#### 实例

```ravel
five := callcc (exit: function) => {
    i := 0
    while { true; } {
        if { i >= 5; } { exit i; } { 0; }
        i += 1
    }
}
print five
```

执行以上程序会输出如下结果：

```
5
```

### 续延是它自己的类型

`Continuation <: function`，起手那枚是 `default`。

#### 实例

```ravel
esc: Continuation = default
print (typeof esc)
callcc (k: function) => { esc = k; "先给个值"; }
print (typeof esc)
print (esc : Continuation)
print ((typeof esc) == function)
print (esc : function)
```

执行以上程序会输出如下结果：

```
Continuation
Continuation
true
false
true
```

注意：**要和普通函数区分开就 `is Continuation`**；`(typeof k) == function` 在续延上**不成立**了（它现在报 `Continuation`）—— 那种写法要改成 `k is function`。（括号不能省：`typeof k == function` 是 `typeof (k == function)`。）

注意：`default` 那枚是**哨兵** —— 照样能存、能传，一调**当场报错**。做成"什么都不做"不行：那会静默地把控制权留在原地，调用方还以为跳走了。

注意：`Continuation f` 是把一枚函数**当成**续延（调它 = 调那枚函数）。库里的 `callcc` 就是用它把"先还原控制状态、再跳回去"那层包成续延的。

### 续延调用是「从捕获点继续」

**`callcc` 之后的语句会被重新执行。**

#### 实例

```ravel
saved := (x: int) => { x; }
n := 0
n = 1 + callcc (k: function) => { saved = k; 0; }
print n
if { n < 10; } { saved 10; } { 0; }
```

执行以上程序会输出如下结果：

```
1
11
```

注意：**同一枚**续延反复调用不是"一次产出多个值" —— 它每次都从捕获点重来，所以调用点本身也在被重跑的代码里时要加守卫，否则会一直转下去。**两枚**续延来回跳就能写生成器（每吐一个值就把控制权交给对方，两边每轮各自重抓自己的续延 —— 见 `tests/40`）。

注意：**不跟**着续延走的是普通那点数据 —— 列表、字段、对象上的改动都是原地改的，恢复不回滚它们。
