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
| Float | 浮点 | `3.14` `5.0` |
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

### 2.3 类型反射

```ravel
int.Parent ()          # ValueType  — 父类型
int.Is ValueType       # true       — 子类型检查
int.Is string          # false
int.Subtypes ()        # [Every]  — 所有子类型(Integer 没有自己的子类)
int.Default ()         # 0           — 默认值
```

### 2.4 类型转换

```ravel
int "42"       # 42
string 100     # "100"
float 3        # 3
bool default   # false
```

`bool` 只认 bool 和 `default`——数字**没有**到 bool 的转换，`bool 0` 会报「无法转换为 bool」。

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

### 3.3 逻辑

```ravel
true && false    # false  (短路与)
true || false    # true   (短路或)
!true            # false
```

`&&` `||` 是短路特殊结构，**不可重载**。

### 3.4 位/逻辑运算

```ravel
3 & 1    # 1    (位与 / 逻辑与)
3 | 1    # 3    (位或 / 逻辑或 / 函数交替)
3 ^ 1    # 2    (位异或 / 逻辑异或)
```

`&` `|` `^` 可重载。

### 3.5 字符串拼接与转义

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

### 3.6 比较 Bool / String

```ravel
true == true    # true
"a" != "b"      # true
```

Bool 只支持 `==` `!=`；String 支持 `==` `!=`，另外还有 `+`（拼接，见 3.5）。

### 3.7 复合赋值

```ravel
x := 10
x += 5     # x = 15
x -= 3     # x = 12
x *= 2     # x = 24
x /= 4     # x = 6
x %= 4     # x = 2
```

### 3.8 成员运算符访问与运算符节

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

### 3.9 给类定义运算符

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

### 5.6 函数名

```ravel
add ::= (x: int) => { x + 1; }
add.name         # "add"
add.name = "sum" # 改名
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

  整条链都没有则报「没有构造器」：

  ```ravel
  Bare ::= class { x: int = 1; }
  Bare ()     # Error: 类型 Bare 没有构造器（init）
  ```
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
| `readonly` | `=` 赋值时报「无法给只读变量赋值」（`:=` 重定义仍可绕过，和变量的 `x := v` 一致）|
| `unreadable` | 读取时报「变量 'x' 不可读取」|
| `outdated` | 读取时往 stderr 打一行 `[outdated] 'x' is deprecated` |
| `core` | 读写都需要先 `unsafe ()`，见「十一、常见陷阱」 |
| `by` | 属性（getter/setter），见 7.7 |
| `override` / `new` | 解析器接受，但求值器不做任何检查（纯注解）|

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
（不走原型链），所以 `Bare ()` 报的是「类型 Bare 没有构造器（init）」，
不会掉进 `type` 那层的建类逻辑。

---

## 八、模块

### 8.1 创建模块

```ravel
ravel "MyMath"
pi := 3.14
ravel ""
print (MyMath.pi)
```

### 8.2 导入文件

```ravel
references = ["/path/to/libs/"]
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

---

## 九、异常

```ravel
Ex.try {
    Ex.throw (Exception "oops")
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
| `exit msg` | 退出程序 |
| `eval "code"` | 执行字符串 |
| `callcc fn` | 续延 |
| `with obj { }` | 浅拷贝修改 |
| `assert cond` | 断言 |

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
