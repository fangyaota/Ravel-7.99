# 9、异常

`Ex` 是**自带的**，不用 `using`，而且有小写别名 `try` / `throw`。

#### 实例

```ravel
try {
    throw (Exception "oops")
} (e: Exception) => {
    print "caught"
}
print (try { 2 + 3; } (e: Exception) => { 0; })
```

执行以上程序会输出如下结果：

```
caught
5
```

注意：`try` 没出错时，整个表达式的值**就是体的值**（上面那块是 `5`）。

## 9.1 `Exception` 就是个普通类

实例有 `Message` 字段（可读可写），`string e` 打出来就是它。所以你可以继承它、按类型接、把消息读出来：

#### 实例

```ravel
IoErr ::= class Exception {
    Path: string = ""
    init = (msg: string path: string) => { Path = path; Message = msg; this; }
}
handle := ((e: IoErr) => { print ("io: " + e.Message + " @ " + e.Path); }) | ((e: Exception) => { print ("base: " + e.Message); })
try { throw (IoErr "no file" "/tmp/x"); } handle
try { throw (Exception "plain"); } handle
```

执行以上程序会输出如下结果：

```
io: no file @ /tmp/x
base: plain
```

几个 handler 用 `|` 拼起来就**按参数类型分派**（和 `|` 造自定义函数是同一套机制）—— 挑不上第一个就试下一个，都不收才报错。

两条写法上的讲究：

- **每个 handler 各自加括号** —— `|` 比应用松，不括的话第一个 handler 会被 `try` 先吃掉；
- **`|` 两边得在同一行** —— 换行它就断了。（能"随便换行"的是**外面**那句 `try … handle`。）

注意：`e == 别的异常` 按**身份**比 —— 要问内容就比 `e.Message`。

## 9.2 引擎报的错自带分类

`Exception` 底下挂着一族（在 **C# 侧**建，见 `Runtime/BuiltinClasses.Errors.cs`），报错那一刻由引擎挑：

| 类 | 什么时候报 |
|---|---|
| `TypeError` | 类型不对：运算符不吃这个操作数、赋值类型不符、转换不了 |
| `NameError` | 未定义的变量 |
| `AttributeError` | 对象没有这个字段 / 类型没有这个方法 |
| `IndexError` | 下标越界、空集合取 `First` |
| `KeyError` | 键不存在、环境变量没有 |
| `ZeroDivisionError` | 除零 |
| `AssertionError` | `assert` 不成立 |
| `AccessError` | 读写被挡：只读 / 私有 / 受保护 / 核心字段 |
| `ArgumentError` | 实参的形状不对（要代码块、要字符串、要类型对象…）|
| `ValueError` | 值本身不对：数值超范围、解析不动、JSON 转不了 |
| `IoError` | 文件 / 目录 / 命令那批 |
| `RegexError` | 正则：模式写错、匹配超时 |

#### 实例

```ravel
try { 1 + "a"; } (e: TypeError) => { print "类型错: " + e.Message; }
try { nope; } (e: NameError) => { print "名字找不到: " + e.Message; }
```

执行以上程序会输出如下结果：

```
类型错: 运算符 '+' 不支持 String 操作数
名字找不到: 未定义的变量 'nope'
```

注意：它们**都是普通类** —— `try (e: Exception)` 照旧接得住全部，可以继承（`MyErr ::= class TypeError { 0; }`）、可以自己 `throw (TypeError "…")`。没归类的错误落基类 `Exception`。

注意：库那一半也对齐了这一族 —— 取不到第 n 个（`First` / `Last` / `At` / 空集合）是 `IndexError`，`Option.Value ()` 在 `None` 上是 `ValueError`。同一个"取不到"，不管它是容器方法还是库方法，接住的都是同一类。

## 9.3 报错长什么样

运行时错误带**位置**和**调用栈**，跨文件时也能看出是哪一层、在哪个文件：

```ravel
helper := (n: int) => { n + missing; }
helper 1
```

```
Error: 未定义的变量 'missing'
  --> tests/diag/152_error_report.rav:4:29
  4 | helper := (n: int) => { n + missing; }
    |                             ^
  调用栈 (2 层):
    在 tests/diag/152_error_report.rav:4:20
    在 tests/diag/152_error_report.rav:1:1
```

插入符指向出错的那个表达式；调用栈每层是该函数**定义处**的位置（Ravel 的帧链就是调用栈，所以这个信息是白捡的）。路径取相对 cwd、分隔符统一成 `/`，所以期望输出跨平台一致。

**语法 / 词法错误走同一套渲染**，只是没有调用栈（源码根本没解析成功）：

```
Error: '$' 只用在字符串里的插值 `${…}`（要把右边封成一个实参,用 '<|'）
  --> <repl>:1:1
  1 | $ 1
    | ^
```
