# 十、内置函数速查

| 函数 | 说明 |
|------|------|
| `print x` | 输出 x 并换行 |
| `input ()` | 读一行 |
| `typeof x` | 返回 x 的类型 |
| `f.Body ()` | 函数的体（`Block`；没有体的给空块）|
| `f.Scope ()` | 捕获作用域（`Scope`；类对象没有，给空 `Scope`）|
| `NaN` `Inf` | 特殊浮点值（和 `true` 同款：`System` 里的值 + 全局别名）|
| `x is T` | 类型判定（`isnot` 取反；`x.is` / `is.T` 也成立）|
| `exit msg` | 退出程序 |
| `eval "code"` | 执行字符串 |
| `Io.File p` / `Io.Dir p` | 文件系统条目（`lib/io.rav`，见 5.11）|
| `System.Args ()` | 脚本名之后的命令行参数（list，见 8.5）|
| `System.WarnForgotCall b` | 开关：「是不是忘了调用？」的提醒（见 11 章那条坑）|
| `System.Env n` / `EnvOr n d` | 环境变量（`SetEnv` / `UnsetEnv` / `EnvAll` 见 8.5）|
| `f <\| a b` / `x \|> f` | `<\|` 把右边封成一个实参；`\|>` 右边是 `.成员` 就挂上去、否则**把左边喂给它**（见 5.5）|
| `return v` | 函数里提前交回（**要开 `--more-control-flow`**，默认关；上下文关键字，出的是最近一层**用户写的**函数，见 4.5）|
| `callcc fn` | 续延（拿到的类型是 `Continuation`，见 4.5）|
| `with obj { }` | 进到 obj 的成员表里跑一段（**不拷**、**不推层**；要副本用 `Copy ()`，见 7.8）|
| `o.MemberScope ()` | 这个对象的**成员表**（一个 `Scope`）—— 按动态名字读写成员时用它 |
| `assert cond msg` | 断言（**两个实参一次写完、别跨行**，见 11 章那条）|
| `use impl` | 在**当前作用域**启用一个接口实现（`实现.Dispose ()` 取消，见 7.11）|
| `impl 实现` | 同上，但**全局**生效 |
| `try { … } handler` | 捕获异常（`throw e` 抛出，见第九章）|

## Math（`using "math.rav"` 之后可用）

| 函数 | 说明 |
|------|------|
| `Math.Pi` `Math.E` `Math.Tau` | 常量 |
| `Math.Sin x` `cos` `tan` `asin` `acos` `atan` | 三角（弧度）|
| `Math.Atan2 y x` | 两参数反正切 |
| `Math.Sinh` `cosh` `tanh` `asinh` `acosh` `atanh` | 双曲 |
| `Math.Sqrt` `cbrt` `exp` `log` `log2` `log10` | 幂与对数 |
| `Math.Pow x y` `Math.LogBase x b` `Math.Hypot x y` | 两参数 |
| `Math.Floor` `ceil` `trunc` `round` | 取整（`roundTo x n` 保留 n 位）|
| `Math.Abs` `sign` `min` `max` `clamp` | `min` / `max` / `clamp` 交回**原始实参**；`clamp` **收一个区间**：`Math.Clamp x [0..1]` |
| `Math.MinMagnitude` `maxMagnitude` `fma` | 按绝对值比 / `a*b+c` |

同一次 `using "math.rav"` 还带来 `square` `cube` `deg` `rad`。

## 别的库

`System` 那一整套（类型名、控制流、文件、环境变量、子进程、时间）见 CONTEXT 的「System 模块」。六个官方扩展（`Hash` / `Crypto` / `Http` / `Regex` / `Sqlite` / `Random`）见 6.16 ~ 6.25。
