# 8、类

## 8.1 类就是一个对象

#### 实例

```ravel
Person ::= class {
    init = () => { this; }
    name: string = ""
    age: int = 0
}
p := Person ()
p.name = "Alice"
print (p.name)
print (Person.name)
print (typeof p)
print (typeof Person)
```

执行以上程序会输出如下结果：

```
Alice
Person
Person
Type
```

**没有 `Class` 类型，也没有单独的「类」表示。** `class` 和 `type` 是**同一个值**，所以 `Person ::= class { … }` 和 `Person ::= type { … }` 建出来的是同一个东西。

类对象自己那几个成员：

| 成员 | 含义 |
|------|------|
| `parent` | 父类对象。**只读** |
| `block` | 类体 —— 实例化时重跑的配方。**只读** |
| `name` | 类名（`Person.name` 是 `"Person"`）|

类对象本身就是**可调用的东西**，所以写得出 `Person ()`；而普通实例不是函数（`p ()` 报「值 … 不是函数」）。

`typeof X` 取的是**创建 X 的那个类对象**：`typeof p` 是 `Person`，`typeof Person` 是 `Type`；`type` 的元类是它自己，链在那里到头。所以"类是实例的类、类自己也有类"不需要另一套机制 —— **建类就是调用一个类对象**。

## 8.2 `init` 是构造器：必须以 `this` 收尾

`init` 就是类体里那个名字叫 `init` 的变量。一个类**最多一个**，没有按参数类型重载。

**写 `init = …`，不是 `init := …`** —— 每个类都已经从 `Object` 那儿继承了一条默认构造器，`:=` 是"在同一个名字上又来一条定义"，当场报错；`=` 是覆盖。父类那条**不会被调用**（没有 `super`）。

它是 **`protected`** 的：类体系里随便用，外面 `obj.init` 报「受保护的」。

**`init` 交出的返回值就是构造的结果**，所以约定以 `this` 收尾。

#### 实例

```ravel
Good ::= class {
    init = () => { x = 1; this; }
    x: int = 0
}
print ((Good ()).x)
Bad ::= class {
    init = () => { x = 1; }
    x: int = 0
}
print (typeof (Bad ()))
```

执行以上程序会输出如下结果：

```
1
Integer
```

注意：忘了写 `this` 拿到的是**最后一条语句的值**，不是那个对象 —— 上面 `Bad()` 交回的是 `1`（赋值是个表达式，值就是赋进去的那个，见 1.2）。**不报错，只是对象不见了。** 同一个坑还有两种长相：`init` 以 `print` 收尾（交回 `()`）、以及元类的 `init` 忘了交出 `parentInit` 建出来的那个类。

注意：`init` **是变量名，不是修饰符**。旧写法（`init ctor := …`）会被专门拦下来。

## 8.3 构造器与参数

#### 实例

```ravel
Point ::= class {
    init = (x0: int y0: int) => { x = x0; y = y0; this; }
    x: int = 0
    y: int = 0
}
p := Point 3 4
print (p.x)
half := Point 10
q := half 20
print (q.y)
```

执行以上程序会输出如下结果：

```
3
20
```

构造器调用和普通函数走**同一条柯里化路径**：对象在第一次调用时就建好（类体已跑、`this` 已绑），`init` 每收到一个参数就往下走一层。

注意：**同一个半成品被调用多次，会作用在同一个对象上。**

#### 实例

```ravel
Point ::= class {
    init = (x0: int y0: int) => { x = x0; y = y0; this; }
    x: int = 0
    y: int = 0
}
half := Point 10
q := half 20
r := half 30
q.y = 99
print (r.y)
```

执行以上程序会输出如下结果：

```
99
```

要独立对象就写 `Point 10 20` 和 `Point 10 30`，别复用同一个 `half`。

注意：一个类只有一个构造器，**没有按参数类型重载** —— 要按参数分派就在 `init` 里自己判断。

## 8.4 继承

#### 实例

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
print (d.name)
print (d.breed)
```

执行以上程序会输出如下结果：

```
?
husky
```

继承是**平铺**的：沿 parent 链从顶祖先到自己**依次跑每一层类体**，所有层的字段落在**同一个实例作用域**里，所以子类能直接读写父类的字段。

| 规矩 | |
|---|---|
| **父类的 `init` 不会自动调用** | 初始值要写在**字段声明**上（`name: string = "?"`）—— 写在父类 `init` 里的赋值不生效（上面 `d.name` 是 `?`，不是 `from-init`）|
| **只调用最具体的那一层 `init`** | 子类没写就落回父类的；整条链都没写就落回 `object` 那份默认构造器（什么都不做、把对象交出来）|
| **子类重声明同名字段就是覆盖** | |
| **没有 `base`** | 父类实例不由子类手工构造 |

所以任何类都构造得出来，不用非得写一个空的 `init`：`Bare ::= class { x: int = 1; }` 之后 `Bare ()` 就是 `Bare { x = 1 }`。

## 8.5 `::=` 命名

`::=` 定义变量、顺手把变量名写进那个类对象；`:=` 建的类**没有名字**，显示和报错时退化成 `class`（真改名要写 `Anon.name = "Foo"`，那是往成员里写）。内置类型都自带名字。

## 8.6 字段与修饰符

字段用 `:=` 定义、用 `=` 改值。对象建好之后**也能加字段** —— `obj.field := v` 是定义语句的成员版。

#### 实例

```ravel
P ::= class {
    init = () => { this; }
    age: int = 0
}
p := P ()
p.nickname := "Al"
print (p.nickname)
p.age += 5
print (p.age)
```

执行以上程序会输出如下结果：

```
Al
5
```

注意：`=` 只能改**已存在**的字段，给不存在的字段赋值报「对象没有字段」。新字段的类型约束来自值的类型。

| 修饰符 | 作用 |
|--------|------|
| `public` | 外部可访问（默认）|
| `private` | 仅本对象内部可访问 |
| `protected` | 类内 + 子类实例可访问 |
| `internal` | **只能直接写**（`x = v`）—— 成员写法 `c.x = v` / `this.x = v` 一律报错；读不受限 |
| `readonly` | `=` 和 `:=` **都**报错（块里的 `:=` 是另开局部变量，不受影响）|
| `unreadable` | 读取时报「变量 'x' 不可读取」|
| `outdated` | 读取时往 stderr 打一行 `[outdated] 'x' is deprecated` |
| `core` | 读写都需要先 `unsafe ()` |
| `by` | 属性（getter / setter），见 8.7 |

注意：`readonly` 连 `:=` 也挡 —— 因为 `:=` 换掉的是**整个 Variable**，attrs 跟着老的那个一起没。不挡的话 `true = 1` 报错、`true := 1` 静默成功，同一个"只读"两条路两个答案。

`internal` 关的是**写法的形状**，不是"谁能" —— 它和 `readonly` 不是一条路上的两档，和 `private` 也不是：

| | 裸名字 `x = v` | 成员写法 `c.x = v` / `this.x = v` |
|---|---|---|
| 无 | 可 | 可 |
| `internal` | 可 | **不可** |
| `private` | 可 | 看"当前作用域在不在这个类里" |
| `readonly` | **不可** | **不可** |

所以 `private` 问的是"**你**是谁"，`internal` 问的是"**怎么写的**"。`this.x = v` 也挡是有意的：判据只看写法的形状，不去算"这段代码在不在这个类里"（那要沿作用域链走一趟）—— 想改就在自己那份代码里写裸名字，一眼看得出。

#### 实例

```ravel
C ::= class {
    internal n: int = 0
    init = () => { this; }
    Bump := () => {
        n = n + 1          # 裸名字：写得进
        n
    }
}
c := C ()
print (c.Bump ())
print (c.n)                # 读一侧不挡
print (try { c.n = 1; } (e: Exception) => { e.Message; })
print c.n                  # 上面那句没落进去
```

执行以上程序会输出如下结果：

```
1
1
字段 'n' 是 internal，不能这样写（只能在它自己的代码里直接写 'n = …'）
1
```

模块成员同一条规矩：`ravel "M"` 里写 `internal x`，模块体（裸名字）写得进，外面 `M.x = …` 挡。

注意：**`override` / `new` 已删除**，写出来会明确报错（语言里既没有重载也没有重定义检查，它们从前只是被记进 attrs 没人读）。

### 一类两张表

**挂在类上的东西，实例看不到。**

#### 实例

```ravel
C ::= class {
    n: int = 7
}
c := C ()
C.func := (x: int) => { x + 1; }
print (C.func 5)
print (c.n)
print (c.Fields ())
```

执行以上程序会输出如下结果：

```
6
7
[init n : == != <: :> ToString Copy Fields MemberScope CompareTo Key]
```

注意：类体里写的字段 / 方法在实例化时落进**每个实例自己的表**；而事后往**类对象**上挂的只进类自己那张表。原因是一个类对象**有两张表**：一张"类自己的"（`name` / `parent` / `block` / 用户挂上去的），一张"**给实例的**"（内置方法、类运算符、序列方法）。实例读的是后者。

反方向同一件事：内置的**实例方法**住在"给实例的那张"里，**类那一侧**读不到 —— `list.Add 2` 报「类型 'Type' 没有方法 'Add'」，而 `Json.FromString` 那种类侧 API 照旧。

## 8.7 `by` 属性

#### 实例

```ravel
Person ::= class {
    init = () => { _name = ""; this; }
    _name: string = ""
    by name := property (() => { _name; }) ((v: string) => { _name = v; })
}
p := Person ()
p.name = "Alice"
print (p.name)
```

执行以上程序会输出如下结果：

```
Alice
```

`property getter setter` 两个参数都是函数。`by` 和 `property` 是**两半**：`by` 是标记，值得由 `property` 造出来 —— **或者 `default`**。

注意：别的值（比如 `by v := 5`）读写都当场报错（`'v' 标了 by，但它的值不是 property`）—— 从前裸读会把那个值悄悄交出去、裸写什么都不发生。

`by` 就是修饰符那张表里的一个，所以**模式和解构上也写**：`by [u v] := [p q]` 拆出来的两个名字都是槽，`(by y: int) => …` 那个参数是槽（见 5.1、6.1）。

### `= default` 是"还没实现"的占位

**属性的默认值**就是一对什么都不做的函数：读出来是**空函数**、写进去**静默丢掉**（不报错）。这正好是"先声明、之后再装实现"要的形状 —— 没实现时读出来的是个**能调**的东西（调了什么都不做、给你 `()`），和用普通成员写同一条槽（`a : function = default`）一个读法。

#### 实例

```ravel
Trait ::= class {
    init = () => { 0; this; }
    by a: int = default
}
t := Trait ()
print (t.a)
t.a = 1
by t.a = property (() => { 100; }) ((v: int) => { (); })
print (t.a)
```

执行以上程序会输出如下结果：

```
<function>
100
```

注意：它**不是**带初值的自动属性 —— 想要存值就自己写 `_a` + `property (() => { _a; }) ((v) => { _a = v; })`。

### 取 / 换槽里的 property

四件事分清楚：

```ravel
by n := property g s     # 定义**槽**：n 由此成为一个属性
n = 1                    # 给属性赋值：过 setter
by n = property g2 s2    # 换掉**槽里的那份 property**：不过旧 setter
p := by n                # 取出**槽里的那份 property 本身**：不过 getter
```

后两个都绕开那半边（tests/138 用计数钉着）。`by a` / `by a.x` 拿出来的是个**普通值**，所以存得进表、传得出去，也能自己调（`p.Get ()` / `p.Set 9`）。

能用 `=` / `:=` 给**别的对象**换 / 建槽（和语言里别处一个规矩）：

```ravel
by o.q = property g s    # 换：o 身上那个槽得已经在
by o.q := property g s   # 建：没有也行
```

注意：`by` 声明必须写在**类体一级** —— 写在 `init` 里面挂不到对象上（`init` 是个 lambda，它的块有自己的局部作用域）。

注意：`readonly` 对 by 槽一样管（`r = 1`、`r += 1`、`by r = …` 三条路都报）。

## 8.8 `with`

`with (对象: 块)` 做且只做一件事：**换一下"接下来这段代码算谁的成员"**。

#### 实例

```ravel
Person ::= class {
    init = () => { _name = ""; this; }
    _name: string = ""
    by name := property (() => { _name; }) ((v: string) => { _name = v; })
}
p := Person ()
p.name = "Alice"
p2 := with p { name = "Bob"; }
print (p2.name)
print (p.name)
```

执行以上程序会输出如下结果：

```
Bob
Bob
```

| 规矩 | |
|---|---|
| 块里的**赋值**沿作用域链落回原对象的字段 | 改的就是它本身 |
| 块里的**定义**（`tmp := 1`）**也落在那个对象上** | 块结束也不没，它成了对象的一个字段（`enum` 那种元类要的就是这条）|
| 交回的是那个对象**本身** | 上面 `p2` 和 `p` 是同一个东西 |

**要副本请明说**：`p3 := p.Copy ()`（浅拷贝）。从前 `with` 自带一份浅拷贝，那既是每用一次整份拷一次的开销，又让"改副本、原件不动"藏在括号里。

## 8.9 object 方法

| 写法 | 意思 |
|------|------|
| `p.Copy ()` | 浅拷贝 |
| `p.ToString ()` | 字符串表示 |
| `p.Fields ()` | 这个值的作用域里**有哪些成员** |

注意：字段和方法一视同仁（`name` 和 `init` 都在里面），**连 `Object` 那一族机制成员也一并列出来**（`is` / `==` / `Copy` / `CompareTo` …）。只排掉 `this`。模块的成员排在最前。

注意：列的永远是"**这一侧**读得到的" —— 从**类对象**上问（`list.Fields ()`），里面就没有那些内置实例方法（它们住在"给实例的那张"里，类那一侧读不到）。

`print p` 是另一回事 —— 它是**数据快照**，方法不出现在里面。

## 8.10 元类

**父类是 `type` 的类就是元类**：它继承了 `type` 那层「建类」的 init，所以它建出来的东西是**类**，不是实例。

#### 实例

```ravel
MyMeta ::= class type {
    private parentInit := init;
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
print (typeof MyMeta)
print (typeof MyClass)
print ((MyClass ()).x)
```

执行以上程序会输出如下结果：

```
Type
MyMeta
42
```

**不需要 `base`**：super 调用退化成「覆盖之前先把继承来的 `init` 取出来」—— `private parentInit := init;` 就是这个惯用法；`parentInit parent body` 就是"走默认那套建类逻辑"。

**两分支 `|`** 让"带父类"和"不带父类"两种写法都能用，靠**参数类型**分流。

`typeof` 链：

```
typeof 实例  →  它的类
typeof 类    →  建它的那个类（元类）
typeof type  →  type 自己（自指，链的起点）
```

注意：元类的 `init` 同样**交出返回值**，所以最后一句得是 `parentInit …` 或 `this` —— 以 `print` 收尾就会拿到 `()`。

注意：**可以往类体上拼一块**（`parentInit parent (body.Append { … })`）—— 拼上来的块和类体**同属一层**，于是"注入几条成员"和用户自己写在那儿的没有区别。`lib/dataclass.rav` 就是靠这个把 `Text` / `==` / 按位置构造注入进去的。

`class class { … }` 是同一件事的简写（第二个 `class` 是父类）。

## 8.11 接口与实现（`interface` / `use`）

接口是**一套具名的槽**；实现是**把它们接到某个类上**的一段代码。接口**不是**类树上的父类 —— 它是一层"套在实例上的视图"。

#### 实例

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
use myImplement
u := myClass ()
u.a = 1
print u.x
u.b ()
print (u : myTrait)
```

执行以上程序会输出如下结果：

```
1
b!
true
```

三件事：`interface { … }` 造出一个**类型**；`myTrait myClass { … }` 造一个**实现**（块里用 `by a = property …` 把槽**换掉**，所以写 `=` 不是 `:=`）；`use impl` 把它**登记在当前位置的作用域里**。

**槽住在实现里**（实例身上没有，所以 `u.Fields ()` 里不列 a/b）。读 `u.a` 时引擎把槽**绑到这一次的接收者**上 —— getter / setter 里那个 `instance` 就是 `u`。

注意：**`instance` 是"这一次调用"的事** —— 它不落在实现身上，每次分发临场绑一层。所以留存下来的闭包永远指它自己那个实例（不会跟着漂）。也因此 `by u.a` 取出来的那份槽事后 `.Get ()` 会报「此刻没有正在被服务的实例」—— **响亮**，不给一个陈旧的值。

### 接口体里的普通成员

接口体就是**类体**（接口是类对象），所以普通函数、字段都写得进去 —— 它们在**目标值**上也读得到。

**函数成员自己就绑接收者** —— 交出去之前引擎把它的捕获作用域换到"这一次服务谁"上（`BindPlain`，和 `by` 槽走的是同一个动作），所以体里直接写 `instance` 就行。**只读**用 `internal` 标（外面 `u.f = …` 写不进去）。

> 从前每一条都得裹成 `by f := property (() => { () => … }) (…)` —— 那层 `property` 只为绑接收者（getter 在**读**的时候跑，体里得再裹一个函数交出手），setter 还是空的。现在不用了。

**非函数的普通成员**（字段那种）值就是它自己 —— 写下去落在**实现那一格**上（每个实现各一份）。

#### 实例

```ravel
Has ::= interface {
    who := () => { "没人"; }
    by n : int = default
}
C ::= class { init = () => { this; } }
impl1 := Has C {
    by n = property (() => { 1; }) ((v: int) => { (); })
}
use impl1
c := C ()
print (c.who ())
print c.n
```

执行以上程序会输出如下结果：

```
没人
1
```

**运算符不在此列** —— 它要的是 `by` 槽（getter 取、setter 写），普通成员给不出：`i + 1` 仍旧报「类型 X 不支持运算符 '+'」。
引擎自己装的成员（`target` / `generation` / …）**不转发** —— 那是"这次服务谁、是哪个实现"，不该当成员读。

### 作用域就是它生效的范围

`use` 登记在**它所在的那个作用域**上，管的是"词法上在这个作用域里的代码"。要**处处生效**就用 `impl`（它登记在全局作用域上）。`myImplement.Dispose ()` 提前取消。

（顶层写 `use` 和写 `impl` 是一回事 —— 顶层那个作用域就是全局。差别只在函数体、模块里写的时候。）

### 注解也认接口

在实现生效期间，接口可以当注解使：`typed : myTrait = u`、`(v: myTrait) => …`。判定和类型检查用的是同一个判据。

注意：`<:` / `:>` 问的是**类型之间**的关系，而"这个类在当前作用域里算不算那个接口"要靠**实例**才能问 —— `(typeof x) <: myTrait` 是 false，而 `x: myTrait` 是 true。

### 接口体里也能写实现（默认实现）

接口的**类体**会在**每个实现对象上跑一遍**，所以写在接口体里的成员就是"实现体不填就用它" —— 实现块里写 `X = …` 能盖掉它。

#### 实例

```ravel
IThing ::= interface {
    by Name : string = default
    internal Loud := () => { (instance.Name).ToUpper (); }
}
C ::= class {
    n: string = "hi"
}
impl (IThing C {
    by Name = property (() => { instance.n; }) ((v: string) => { (); })
})
print ((C ()).Loud ())
```

执行以上程序会输出如下结果：

```
HI
```

`IEnumerable` 就是这么给 `Generator`、字符串、用户类带上整套 `Map` / `Where` / `Count` 的。

注意：实现体里可以直接写 `instance`（「这一次服务谁」）—— 函数成员交出去之前会绑一次。

注意：类链上已经有同名成员时**它先命中**（接口那份只补"本来要报没有方法"的）。

### 接口继承接口（外加一串要求）

```ravel
myTrait ::= interface {
    by a : int = default
}
supTrait ::= interface myTrait { by c : int = default; }
masterTrait ::= interface supTrait [IEnumerable] {
    ()
}   # 父 + 要求
```

- **父是继承**：槽是父的 + 自己的（同名以自己写的为准），`masterTrait <: supTrait` 成立。
- **要求是前置条件**：实现它的类必须**已经**有 `IEnumerable` 的实现 —— 槽**不**并进来、`<: IEnumerable` 也**不**成立，只在造实现那一步查。
- 要求得**跟在父后面**（光写 `interface [IEnumerable] { … }` 不收）。

### 槽里也能放运算符

接口声明一条 `by + := property g s`，实现这个接口的类就有了那个运算符。**那一格的值是 property**，所以"用"它是**两级**：读槽（走 getter）拿到运算符函数，再用它收右操作数。

（裸的 `+ := f` 仍然是**类运算符**那条路，不受这里影响。）

### 查一个类型现在实现了什么

| 写法 | 交回 |
|------|------|
| `T.GetImplements ()` | 这个类型当下实现了哪些接口 |
| `I.GetImplementors ()` | 谁当下实现了这个接口 |

两边都是**当下**的快照 —— 出了那个作用域、或者 `Dispose ()` 之后再问就没了。接口是"在这个作用域里生效"的东西，不是一个烙在类型上的标记。

注意：`GetImplementors` 里普通类是空的 —— **只有 `object` 例外**（谁都收得下 `object`，于是所有实现者都算它的）。要列"谁实现了这个接口"，自己先问一句 `x: interface`。
