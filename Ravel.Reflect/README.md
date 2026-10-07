# Ravel.Reflect —— 让 Ravel 能反射 .NET

```ravel
using "plugins/Ravel.Reflect.dll"

t  := Reflect.Type "System.Console"
m  := Reflect.Type "System.Math"
print (Reflect.Call m "Sqrt" [16])            # 静态:第一个实参给**类型**

sb := Reflect.New (Reflect.Type "System.Text.StringBuilder") []
Reflect.Call sb "Append" ["hi"]               # 实例:第一个实参给**对象**
print (Reflect.Text sb)

print (Reflect.Call "hello" "Length" [])      # 属性也走 Call
```

## 门面:2 个类 + 5 个函数

| | |
|---|---|
| `DotNetType` | 一个 `System.Type`。`.Name ()` / `.FullName ()` / `.IsValue ()` / `.IsInterface ()` / `.Members ()` / `.Text ()` / `.AsObject ()` |
| `DotNetObject` | 一个任意 .NET 对象。`.TypeOf ()` / `.Text ()` / `.Unwrap ()` |
| `Reflect.Type name` | 名字 → `DotNetType`（核心库写 `"System.Console"`；别的程序集要**带程序集的全名**）|
| `Reflect.New t args` | 构造 |
| `Reflect.Call obj name args` | 调方法 / 读属性 |
| `Reflect.Text v` | `ToString ()`，Ravel 值也行 |
| `Reflect.Unwrap v` | 脱壳:.NET → Ravel（认识的就化成 Ravel 值）|

**两个类都别跟 Ravel 自己的名字撞**:`Type` 被占了（`x.Type` 是"它的类"），
所以一个叫 `DotNetType`、方法叫 `TypeOf`。挂错名字**不报错**，只是读不到。

## 规矩

**`.AsObject ()` 是那道"把类型当对象看"的门。** `DotNetType` 的意思是**静态访问的把手**
（`Reflect.Call t "Max" […]` 在它身上找的是**静态**成员）—— 而 `Type` 自己的那些
**实例**成员（`GetMethods` / `GetParameters` / `IsSpecialName`…）就够不着了。
想扫一个类型的全部方法，要的恰恰是它们：

```ravel
o := (Reflect.Type "System.Math").AsObject ()
print ((Reflect.Unwrap (Reflect.Call o "GetMethods" [])).Count ())    # 124
```

**别自己走 `System.Type.GetType`。** 查不到的时候它交回 **null**，紧接着
`null.GetMethods ()` 就是一句 NRE —— 那不是 Ravel 的异常，**`try` 接不住**，
一路打成"解释器内部错误"。`Reflect.Type` 报的是人话（核心库写全名 / 别的程序集先
`using` 进来再写带程序集的全名），而且它还会在**已经加载的**程序集里再找一遍。

**重载按"参数个数 + 绑得上"挑**:先按个数筛,再**依次试** —— 绑不上就换下一个。
`"3.7".Replace ["3" "4"]` 就是靠这条:它有两个 2 参数的重载 `(char, char)` 和
`(string, string)`,前者排在前面。

**被调方真抛的不吞**(`TargetInvocationException` 翻成人话);`ArgumentException`
才是"这个签名绑不上",那才换下一个。

## 值桥

| 方向 | 规矩 |
|---|---|
| Ravel → .NET | 认识的转:`int` `real` `bool` `string` `char` `()` `default` `list`(→ `object[]`);认不出就报错 |
| .NET → Ravel | **一律 `DotNetObject`,一个都不化** |

第二条是**故意不猜**。先前是"认识的(int / string / 一串…)就化成 Ravel 值",踩到的坑是
**身份会掉**:`"a,b,c".ToCharArray ()` 交回来的 `char[]` 被化成 Ravel 的 `list`,
再想传给 `String.Split (char[])` 就只剩 `object[]` —— **自己刚造出来的东西自己交不出去**。

现在:要看用 `Reflect.Text`,`5` 那种数要算就明说 `.Unwrap ()`:

```ravel
print (Reflect.Call "hello" "Length" [])              # DotNetObject 5
print ((Reflect.Call "hello" "Length" []).Unwrap ())  # 5
```

于是**数组、容器、自己造的对象全都保得住身份**,可以一路传下去:

```ravel
ca := Reflect.Call "a,b,c" "ToCharArray" []           # DotNetObject 包着真的 char[]
print (Reflect.Text (Reflect.Call "a,b,c" "Split" [ca]))   # System.String[]
```

## 不在里面的(这一份是"最基本"的)

- **按参数类型逐个转**:Ravel 的 `list` 只会变成 `object[]`,不会变成参数要的那个形状。
  要 `char[]` 就**自己造一个再传**(上面那两条);要别的容器同理。报错会说清
  给的是什么、要的是什么。
- **泛型类型实参**:`List<int>` 在 Ravel 层没有写法。
- **字典**:`dict` 不往 .NET 转（.NET 的 `IDictionary` 也不往回转,会兜成对象）。
- **委托方向 —— 硬边界**:引擎里写着三遍"原生闭包调不了 Ravel 函数(那是帧栈的活)",
  所以**把 Ravel 的 lambda 交给 C# 做不到**,只有反过来(.NET 委托 → Ravel 函数)才行。
  踩到这条时给的消息是专门的。
- **安全**:开了这个 = 任意 .NET(文件/网络/进程)。
