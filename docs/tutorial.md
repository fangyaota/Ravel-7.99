# Ravel 语言教程

一章一个文件，在 [docs/tutorial/](tutorial/) 下。**面向有经验的程序员** —— 只讲"这门语言和别家不一样"的地方，不解释通用概念。

想当网页看：`ravel examples/site.rav`，然后打开 `site/index.html`（左边目录、右边正文、上一篇/下一篇）。

| 章 | |
|---|---|
| [一、起步](tutorial/01-start.md) | 定义 / 赋值 / 自动命名 / 注释 / 打印和输入 |
| [二、类型系统](tutorial/02-types.md) | 内置类型 / 类型名 / 注解 / 反射 / 转换 / 空值 / 类型层次 / 大数 |
| [三、运算符](tutorial/03-operators.md) | 算术 / 比较 / `is` / 位 / 字符串 / **调用比运算符松** |
| [四、控制流](tutorial/04-control.md) | `if` / `while` / `foreach` / 枚举器 / `Generator` / `match` / 早期退出 |
| [五、函数](tutorial/05-functions.md) | 柯里化 / `_` / `<\|` 与 `\|>` / `Cached` / `Option` / `do` / `IoMonad` / 文件 / 排序 / JSON / 时间 |
| [六、集合与标准库](tutorial/06-collections.md) | 三种容器 / `Random` / `Range` / `Regex` / `dataclass` / `enum` / `Http` / `Sqlite` / 数据结构 / ZIP / XML / 终端 … |
| [七、类](tutorial/07-classes.md) | `init` / 继承 / 修饰符 / `by` 属性 / `with` / 元类 / 接口与实现 |
| [八、模块](tutorial/08-modules.md) | `ravel` / `using` / `System` / `Math` / 命令行与环境变量 / `Test` |
| [九、异常](tutorial/09-exceptions.md) | `try` / `throw` / 异常那一族 / 报错长什么样 |
| [十、内置函数速查](tutorial/10-cheatsheet.md) | 一张表 |
| [十一、常见陷阱](tutorial/11-pitfalls.md) | 十条最容易栽的 |
| [十二、并发](tutorial/12-tasks.md) | `Tasks`：协作式任务、挂起点、`Signal` / `All` / `Any` / `Chan` |
| [十三、HTTP 服务端](tutorial/13-server.md) | `Httpd`：路由 / 请求对象 / 响应 / 静态目录 / 一个慢 handler 挡不住别的连接 / keep-alive / 正文上限 / HTTPS |

## 想深挖

- **[CONTEXT.md](../CONTEXT.md)** —— 这个语言**为什么**长这样（求值器架构、每一处取舍的来龙去脉）。教程只管"怎么用"。
- **[docs/adr/](adr/)** —— 几条大决定（类与类型系统、显式帧栈）。
- **[examples/](../examples/)** —— 能跑的例子：`tasks.rav`（协作式任务八节）、`signal.rav`（闸：两版能等的队列对着看）、`http.rav`（真出网）、`refgraph.rav`（画模块引用图）、`type_tree.rav`（画类型树）。
- **`tests/`** —— golden 用例，一份源码 + 一份期望输出。想找某个特性的**边界**，那儿最全。

## 每一段代码都跑过

教程里带「执行以上程序会输出如下结果：」的段落，输出都是**真跑出来的**。写这版的时候靠这条逮到过好几个错（有文档写错的，也有库本身的）。
