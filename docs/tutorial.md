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
add ::= (x: int) => { x + 1 }
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
int.Subtypes ()        # [String BigInt ...]  — 所有子类型
int.Default ()         # 0           — 默认值
```

### 2.4 类型转换

```ravel
int "42"       # 42
string 100     # "100"
bool 0         # false
float 3        # 3
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
Object
├── ValueType → Integer Float Bool String BigInt Fraction BigFraction
├── Function → Type → Class → List Set Dict Ravel (及用户类)
├── Void Exception
├── Any (顶类型)
└── Every (底类型，default 的类)
```

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

### 3.5 字符串拼接

```ravel
"hello " + "world"   # "hello world"
"ab" * 3   # 报错！字符串不支持 *
```

### 3.6 比较 Bool / String

```ravel
true == true    # true
"a" != "b"      # true
```

Bool 和 String 只支持 `==` `!=`。

### 3.7 复合赋值

```ravel
x := 10
x += 5     # x = 15
x -= 3     # x = 12
x *= 2     # x = 24
x /= 4     # x = 6
x %= 4     # x = 2
```

### 3.8 成员运算符访问

```ravel
1.operator+     # 返回函数: (rhs) => 1 + rhs
true.operator^  # 返回函数: (rhs) => true ^ rhs
```

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

`if` 接受三个块作为参数：条件、then、else。块需要 `;` 或换行。

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

续延可以被保存并多次调用。

---

## 五、函数

### 5.1 单参数

```ravel
double := (x: int) => { x * 2 }
double 5    # 10
```

`()` 空参数：`() => { 42 }`，调用：`f ()`。

### 5.2 多参数（柯里化）

```ravel
add := (x: int y: int) => { x + y }
add 3 4          # 7

# 部分应用
add3 := add 3    # (y: int) => { 3 + y }
add3 4           # 7
```

语法：`(x: T1 y: T2) => { body }`。

### 5.3 _ 占位符

```ravel
inc := _ + 1              # (x: object) => x + 1
both := _ + _             # (x: object) => (y: object) => x + y
is_pos := _ > 0           # (x: object) => x > 0
greet := print _ "!"      # (x: object) => print x "!"
```

每个 `_` 一个参数，从左到右编号。

### 5.4 丢弃

```ravel
_ := 42          # 求值但不绑定
_ = f ()         # 调函数丢弃结果
_ : int = 99     # 类型标注丢弃
```

### 5.5 管道

```ravel
5 <| double    # double 5 → 10
```

### 5.6 函数名

```ravel
add ::= (x: int) => { x + 1 }
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
lst.Add 4         # [1 2 3 4]
lst.Remove 1      # 删除索引 1 → 2
lst.Insert 0 99   # [99 1 2 3]
lst.Set 0 100     # [100 1 2 3]
```

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

### 6.4 代码块 vs 集合

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

### 7.1 基本定义

```ravel
Person ::= class {
    init := () => {
        name = ""
        age = 0
    }
    name: string = ""
    age: int = 0
}
p := Person ()
p.name = "Alice"
print (p.name)   # Alice
```

构造器就是名字叫 `init` 的那个变量。一个类最多一个；类里不写就向上找父类的，
整条继承链都没有则报「没有构造器」。写 `init ctor := ...`（旧写法）是语法错误。

### 7.2 构造器与参数

```ravel
Point ::= class {
    init := (x0: int y0: int) => {
        x = x0
        y = y0
    }
    x: int = 0
    y: int = 0
}
p := Point 3 4          # 和普通函数一样柯里化：((Point 3) 4)
print (p.x)             # 3

half := Point 10        # 少参：拿到一个「还要参数的构造器」
q := half 20            # 补上参数才得到对象
print (q.x)             # 10
print (q.y)             # 20
```

构造器调用和普通函数走同一条柯里化路径：对象在第一次调用时就建好（类体已跑、`this` 已绑），
`init` 每收到一个参数就往下走一层；`init` 还没应用完就返回一个「半成品构造器」，
应用完了才把对象交出来。所以：

- `Point 3 4` 一次写全 —— 正常用法
- `half := Point 10` 再 `half 20` —— 也合法，等价于 `Point 10 20`
- ⚠️ 同一个半成品被调用多次会作用在**同一个对象**上（它捕获的是第一次调用时建的那个对象），
  要独立对象就写 `Point 10 1` 和 `Point 10 2`，而不是复用一个 `half`

一个类只有一个构造器，**没有按参数类型重载**。若确实要按参数分派，在 `init` 里自己判断：

```ravel
Flex ::= class {
    init := (v: object) => {
        x = 0
        if { typeof v == int; } { x = int v; } { 0; }
    }
    x: int = 0
}
```

### 7.3 继承

```ravel
Animal ::= class {
    init := () => { 0; }
    name: string = "?"
}
Dog ::= class Animal {
    init := () => {
        name = "dog"
        breed = "husky"
    }
    breed: string = ""
}
d := Dog ()
print (d.name)     # dog
print (d.breed)    # husky
print ((Animal ()).name)  # ?  —— 父类实例不受影响
```

继承是**平铺**的，不是链式查找：`Dog ()` 时会依次跑 `Animal` 的类体、再跑 `Dog` 的类体，
所有字段落在同一个作用域里，所以子类能直接读写父类的字段。

几条规则：

- **父类的 init 不会自动调用**。初始值写在字段声明上（`name: string = "?"`），
  写在父类 init 里的赋值不会生效。
- **只调用最具体的那一层 init**。`Dog` 自己写了 init 就调 `Dog` 的；
  `Dog` 没写就向上找，用 `Animal` 的（整条链都没有才报「没有构造器」）。
- **子类重声明同名字段是覆盖**：`:=` 就是定义。
- 想改从父类继承来的字段，直接赋值即可（`name = "dog"`）。

没有 `base`。父类实例不需要（也无法）由子类手工构造。

### 7.4 修饰符

| 修饰符 | 作用 |
|--------|------|
| `init` | 标记为构造器 |
| `public` | 外部可访问 |
| `private` | 仅类内可访问 |
| `protected` | 仅类内+子类可访问 |
| `readonly` | 不可修改 |
| `by` | 属性（getter/setter）|

```ravel
class {
    init := () => { ... }
    public name: string = ""
    private _age: int = 0
    readonly id: int = 1
}
```

### 7.5 by 属性

```ravel
Person ::= class {
    init := () => {
        _name := ""
        by name := property (() => { _name; }) ((v: string) => { _name = v; })
    }
}
p := Person ()
p.name = "Alice"    # setter
print (p.name)      # getter → "Alice"
```

`property getter setter` 两个参数都是函数。

### 7.6 with（浅拷贝修改）

```ravel
p := Person ()
p2 := with p { name = "Bob"; }
```

`with` 浅拷贝对象，在副本上执行块，返回副本。

### 7.7 object 方法

```ravel
p.Copy ()      # 浅拷贝
p.ToString ()  # 字符串表示
```

`p.Fields ()` 列出的是**类型方法名**（`ToString`/`Copy`/`Fields`…），不是实例字段名。
只有模块（`ravel`/`using` 建立的）会额外列出作用域里的变量。

---

## 八、metaclass

> ⚠️ **本章描述的是目标语义，尚未实现。** 元类构造器里创建类的 `base.init parent block`
> 依赖已移除的 `base`，目前没有替代手段，对应用例（117/118/121/122）都是 `# todo`。
> `typeof` 反射本身可用（8.1），但用它*创建*类还不行。

### 8.1 概念

metaclass 是"类的类"。`typeof` 返回创建类的 metaclass：

```ravel
typeof Person     # Class    (Person 由 class 创建)
typeof MyClass    # MyMeta   (MyClass 由 MyMeta 创建)
typeof int        # Type     (内置类型由 type 创建)
```

### 8.2 创建 metaclass

```ravel
LoggedMeta ::= class class {
    init := (parent: type block: function) => {
        print "creating..."
        base.init parent block
        this
    }
}
```

- `class class` 继承 `Class`，创建新的类构造器
- `base.init parent block` 调 `Class` 的 init 创建实际类
- `this` 指向正在被创建的类构造器

### 8.3 使用 metaclass

```ravel
MyClass ::= LoggedMeta {
    init := () => { 0; }
    x: int = 42
}
typeof MyClass   # LoggedMeta
c := MyClass ()
print (c.x)      # 42
```

### 8.4 无 init 的 metaclass

```ravel
M ::= class class { x: int = 1 }
C ::= M { init := () => { 0; }; }
```

继承 `Class` 的默认 init，行为同普通 class。

### 8.5 接口（实验性）

```ravel
IEnumerable := interface {
    count: function = () => { 0; }
}
```

`interface` 是内置 metaclass。

---

## 九、模块

### 9.1 创建模块

```ravel
ravel "MyMath"
pi := 3.14
ravel ""
print (MyMath.pi)
```

### 9.2 导入文件

```ravel
references = ["/path/to/libs/"]
using "other.rav"
```

`using` 只加载一次，循环引用报错。

### 9.3 System 模块

内置模块，解释器启动时创建。PascalCase 类型名在此：

```ravel
System.Integer  System.String  System.Bool
System.WriteLine "hello"
System.ReadLine ()
```

小写别名在 `predefined.rav` 中定义。

---

## 十、异常

```ravel
Ex.try {
    Ex.throw (Exception "oops")
} (e: Exception) => {
    print "caught"
}

# 未捕获
exit "fatal error"
```

---

## 十一、内置函数速查

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

## 十二、常见陷阱

### 单行代码块需要 `;`

```ravel
if { x > 0; } { print 1; } { print 0; }   # ✅
if { x > 0 } { print 1 } { print 0 }       # ❌ → Set
```

### 链式调用需要临时变量

```ravel
# ❌ 解析为 s.At (0.Parent()) 
s.At 0.Parent ()

# ✅ 临时变量
v := s.At 0
v.Parent ()
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
init := () => { ... }        # ✅ 构造器就是名字叫 init 的变量
init ctor := () => { ... }   # ❌ 没有 init 修饰符这种写法了
```

### metaclass 的父类型

```ravel
# 父类型 = Object，base.init 走 Object
MyMeta ::= MetaMeta object { ... }

# 父类型 = MetaMeta，base.init 走 MetaMeta
MyMeta ::= MetaMeta MetaMeta { ... }
```
