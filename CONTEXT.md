# Ravel — 上下文文档

## 项目概述

Ravel 是一个**显式持久帧栈**解释型编程语言（原 CPS trampoline 已移植替换，见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)）。C# 实现，测试全绿、**todo 清零**——具体计数看 `dotnet out/ravel.dll test` 的输出，别写死在这里。

## 编译运行

```bash
bash build.sh                            # 下面那几步收成一条命令:发布到 out/ + 全量测试 + 冒烟
bash build.sh --rebuild --warn           # 再顺带 C# 警告检查(-t:Rebuild)、--warn test

rm -rf out && DOTNET_GCHeapHardLimit=0x10000000 dotnet publish Ravel.csproj -c Debug -o out
     # 先删 out/:增量 publish 有时不更新它,会跑到陈旧产物、得出假的结论
     # 指定 .csproj 而不是 .sln:"-o" 配 sln 会报 NETSDK1194
     # 发布产物是**自洽**的:插件 dll 在 out/plugins/、标准库在 out/lib/、例子在 out/examples/
     # (见 Ravel.csproj 的 CopyPlugins / CopyLib / CopyExamples / CopyDocs),搜索路径里有"程序集目录"那两格
     # (Runtime/ModuleSearchPath.cs),所以 out/ 那一份换到哪个工作目录都跑得起来
     # **发布出去的 lib/ 不带注释**(`StripLibComments` 在 publish 之后跑一遍 `ravel strip`):
     # 那些注释是写给改这门语言的人看的,不是写给跑它的人看的 —— 仓库里那份留着,`bin/` 那份也留着
dotnet out/ravel.dll test                # 全量测试(有 FAIL 时退出码 1)
dotnet out/ravel.dll path/file.rav      # 单文件
dotnet out/ravel.dll path/file.rav a b  # 脚本名之后那些进 System.Args ();见「System 模块」
dotnet out/ravel.dll                    # REPL
```

**退出码**:测试用例有 FAIL、脚本报错、解释器自己有 bug —— 三种都是 1
(报错那条本来就打一行 `Error:`;说没说话和退出码走同一条线,所以 `Test.Report ()`
那种"打一行汇总再抛出去"的脚本能让 CI 真的红掉)。`exit ""` 是"什么都不说就结束" → 0。

VS Code 里：`Ctrl+Shift+B` 跑当前 `.rav`（会先编译）、`F5` 跑当前文件并可下断点、
命令面板搜 `Ravel` 还有「运行全量测试」「打开 REPL」;`.rav` 文件上**右键 → 在命令行中运行**
则会**弹一个独立的系统命令行窗口**出来跑(`cmd /c start … cmd /k`,跑完窗口留着;不是 VS Code
里那个终端面板、也不是输出面板)—— 要交互的脚本(`input ()`、`examples/repl.rav` 那种)得走那条。
语法高亮靠 `vscode-ravel/` 扩展
（见下），它调用的同样是 `bin/Debug/net10.0/ravel.dll`。

扩展还提供**折叠**：按 `{}` / `[]` / `()` 分块（不是按缩进 —— Ravel 按括号分块，缩进对不上）、
外加连着两行以上的整行注释段。判据在 `vscode-ravel/folding.js`（纯函数，
`node vscode-ravel/test/folding.test.js` 直接跑），字符串与 `${…}` 插值里的括号不算。

还有**跳转定义 / 悬停**（`Ctrl+点击` 一个名字）。这一格在这门语言上**特别好做**，值得记一笔：
标准库全在 `lib/*.rav`、而且**是可读的 Ravel 源码**，定义又只有几种固定的写法
（`名字 :=` / `名字 ::=` / `名字 : 类型 =`），于是**不用类型推断、不用符号表、不用 LSP**，
按文本认就够（`vscode-ravel/definitions.js`，纯函数，`node vscode-ravel/test/definitions.test.js`）。
`Seqs.Map` 这种带模块名的按 `ravel "Seqs"` 那句找文件；裸名字当前文件优先、且优先**光标之前
最近的那一处**（局部定义常有好几处，取第一处会跳到文件顶上那个同名的）。
换成一门库是编译产物的语言，这一步就得先有一个语言服务器 —— 这是"库用自己写"白拿的一份红利。

`language-configuration.json` 的 `brackets` 里**多写了两对交叉括号**（`[`↔`)`、`(`↔`]`）：
区间的括号**各带一半意思**（`[1..5)` 左闭右开、`(1..5]` 左开右闭），不写这两对的话
VS Code 会把 `[1..5)` 的 `[` 判成"没闭合"、`)` 判成"多余的"，括号对着色与匹配提示全红。
`folding.js` 里那份 `CLOSERS` 是同一套五对。

## 文件结构

```
Runtime/                         求值器按职责拆成多个 partial class 文件
  Interpreter.cs          入口/CheckFieldAccess/As<T>/Show
  Interpreter.Stack.cs    帧栈推进循环(StepOnce/Return/PushChild + 块执行)
  Interpreter.Nodes.cs    节点状态机(每 AST 节点一个 NodeFrame,按 Results.Count 分阶段)
  Interpreter.Call.cs     CallInto 调用分派 + 合成控制帧的推帧助手
  Interpreter.Control.cs  控制帧状态机(with/callcc/using/eval/类初始化/交替/合成…)
  Interpreter.Modules.cs  模块路径解析与加载(ReferencesPath + 搜索目录、循环引用检测);
                          **名字可以省后缀**(`seqs` / `Ravel.Extensions`,两种写法都命中就报错);
                          每个模块还记着**自己写着的那些 `using`**(见 `EnterModule`)
                          **`ravel "X"` 建的模块,父作用域是当时那一块**(`AmbientScope ()`),
                          不是硬挂全局。一个文件常在 `ravel "X"` **之前**先摆几个名字
                          (`io.rav` 先写 `IFile` / `IDir`,`ravel "Io"` 之后才 `impl (IFile File …)`
                          把它们接到类上),那些名字落在"这个文件跑在哪个作用域里";
                          硬挂全局的话,X 就看不见它们,那句 `impl` 会报「未定义的变量 'IFile'」
                          —— 而它明明就在上几行定义过。**从顶层 `using` 时这一块就是全局**,
                          和从前一模一样;只有"从**模块里** `using` 一个模块文件"那条路不一样,
                          而那条从前是坏的(被 `io.rav` 总被先加载过一遍掩盖着)。用例 `tests/305`。
  Builtins/SysAttribute.cs / SysRegistry.cs
                          **`[Sys]` 那套机制**:特性本身 + 扫描(扫**整个程序集**,
                          不列名单)+ 绑委托(每实例一次,`CreateDelegate`,热路上不留反射)
  Builtins/SysModule.cs   System 模块的装配:三张**数据**表(类型别名 / 常量 / 控制内建)
                          + 让 `SysRegistry` 登记 `[Sys]` 那些函数。**就剩这一个** ——
                          从前还有一个 `MathModule`(靠 `CSharpModules` 表,`ravel "Math"` 时
                          填成员),那个机制和那张表都随 Math 搬进官方扩展删掉了
  Builtins/Sys{Output,Core,Reflection,Files,Cmd,Time,Env}.cs
                          **一个主题一个类**,装那一批 `[Sys]` 方法 —— 加一个内置函数 =
                          在对应主题里写一个 `static` 方法,没有第二处要改(从前是"挑一段、
                          找对位置、再补一行 `DefFn`" —— 那一个文件一千行就是这么散的)。
                          **它们不是 `Interpreter` 的一部分**:要用引擎的就把解释器当
                          **第一个参数**收(`Interpreter self`),用不着的就是纯函数
  Builtins/SysKit.cs      内置那一族共用的小工具(`Fs` / `PathOf` / `NeedFile` / `BytesOf`…)
                          —— 各主题都 `using static`,调用点还是老样子。
                          **别和 `BuiltinClasses*.cs` 混**:那是内置**类**的树(类型那一侧),
                          这里是内置**函数**
  ModuleSearchPath.cs     模块搜索目录(单一定义,predefined.rav 与 using 共用):
                          `./` → `lib/` → `plugins/` → … → 程序集目录那两份(`lib/` 与 `plugins/`)
  Frame.cs / RList.cs     帧链(不可变持久) / 持久化单链表;
                          Frame.cs 还有 ControlFrame.Arg<T> 和 ArgNames(控制帧参数的类型化取值)
  RuntimeValue.cs         值基类(含 IsClosure) + 全部 Ravel 层异常:
                          RuntimeException / TypeMismatchException / ExitException /
                          SyntaxException + SourceSpot(位置)
  Attr.cs                 修饰符名常量(readonly/public/private/…/core),解析器和门禁共用;
                           **表里的每一个都得有地方读它** —— `override`/`new` 因为无人读
                           已连同修饰符一起删(`public` 是唯一例外:它是默认行为)
                           `lib/` 的 API 都标 readonly(语言级别名/模块函数);
                           **状态**故意不标(Ex.HandlerStack / ReferencesPath)。
                           readonly 连 `:=` 一起挡(`Scope.DefineOrReplace` 里查本层):
                           `:=` 换掉整个 Variable,attrs 会跟着老的那个没
  ErrorReport.cs          错误渲染(位置 + 源码行 + 插入符 + 调用栈);
                          `Format(Exception)` 一个入口管运行时/语法两种错误
                          (CLI/REPL/测试运行器共五处调用点因此各少一条重复的 catch)
  BuiltinClasses.cs              内置**类对象**树(建树分两趟)+ 预设类体 + 默认建类逻辑
  BuiltinClasses.{Methods,Operators,Initializers}.cs   内置方法/运算符/转换器的注册
                                 (成员直接进各自类对象的 Scope,没有单独的"方法表")
  BuiltinClasses.Interfaces.cs   接口与实现(interface / use / impl / Dispose)
  BuiltinClasses.Sequences.cs    三种容器共用的序列方法(引擎按类型挂的那份;接口那层
                              另有一份给别的实现者,见「接口」里的 `IEnumerable`)
                                 (收函数的那批是控制帧,见 Interpreter.Control 的 StepSeqOp)
  Scope.cs / Variable.cs         作用域
  BoxedValue.cs                  成员访问
  Values/                        
    ClassVal.cs                  **类对象**;`ClassVal : FunctionVal`,自己就是可调用的那个东西
                                 (没有 IFunction 这类"可调用"接口了 —— 判据就是"是不是函数")
    ObjectVal.cs                 **非原子值的基类**:带一张真实的成员表(Scope)
    FunctionVal.cs               `: ObjectVal`;Body 即「参数→结果」,CaptureScope 是捕获作用域
    MemberView.cs                **取成员的唯一入口**:自己那层 → 沿类对象 parent 链兜底
    ISelfBinding.cs              自绑定成员(内置方法/类运算符工厂):读出来要绑接收者
    NativeClosure.cs             **体是 C#、但按调用点作用域跑** —— 补 LambdaVal 和
                                 FunctionVal 中间那格(`type` 的 init 靠它看见 `this`)
    MethodVal.cs                 内置方法 / 类运算符工厂 / 绑好的类运算符
    ControlFunction/ComposeVal/PartialCtor/ContinuationVal/...   各基础值
                                 BoolVal 也继承 FunctionVal(类型表里 Bool <: Function,见「两个坑」)
                                 FractionVal/BigFractionVal 构造即约分
Syntax/                         前端:词法 / 递归下降 / AST。**文件夹只是归类** ——
                                命名空间仍然是 `Ravel`(和 `Runtime/` 那些各层共用),
                                C# 不看文件夹,所以这一趟搬家一个字符的代码都没动
  Lexer.cs / Ast.cs / Token.cs / TokenType.cs
                                **`(` / `[` 里的换行不当语句结束**(`_groups` 那个括号栈盯着):
                                表和长参数列表因此能写好看;花括号不在其内 —— 块靠换行分语句。
                                (从前另有一条 `ScanState` —— "括号合上了吗、末尾在不在字符串里",
                                给 C# 那版 REPL 判"这行写完没有"用的,其中也包括"多行原始字符串
                                不算没写完"。REPL 搬进库、C# 那一版删掉之后它没有调用者了,
                                一并删掉;那条规矩现在只有 `lib/repl.rav` 的 `Balanced` 一份。)
                                Token.Length 是**源码跨度**(字符串含引号、带转义),
                                和 Lexeme(给人看的文本)不是一回事
                                字符串没收到尾的引号 = 语法错误(从前静默吞掉后面全部源码)
                                `"""…"""` 是**原始字符串**:里面一个字符都不动(不转义、
                                不插值、`#` 不是注释),只有连着三个引号才收;空白按 C# 那套
                                (去开头换行 + 剥收尾引号那一行的缩进,对不齐就报错)。
  Parser.cs                     入口 + token 辅助(Peek/Consume/ParseError)
    Parser.Statements.cs        语句:定义/赋值/运算符定义/`x :< m`(只在 do 里放行)
    Parser.Expressions.cs       优先级链(管道→逻辑→比较→加减→乘除)
    Parser.Atoms.cs             基本单元 + 括号/块/集合/字典 + `do { … }` 折成 Bind 链
    Parser.Holes.cs             `_` 占位符消糖那趟 AST 改写
                                `do` 是**纯语法糖**:解析期就地折成 `m.Bind (…)`,
                                运行时不为它添任何东西(`BindStatement` 活不到求值期)
  CommentStripper.cs            剥注释**再收空行** —— **发布产物里的标准库用它**(见 `Cli/Strip.cs`
                                与 Ravel.csproj 的 `StripLibComments`)。**问词法器要区间**
                                (`Lexer.Comments`),不拿正则扫:字符串、原始字符串、字符
                                字面量里的 `#` 都不是注释(`lib/repl.rav` 那张 ASCII 大图就是
                                原始字符串)。空行也收(连着两行以上只留一行),**但多行字符串
                                里面一个字不碰** —— 按 token 跨度把那些行标出来。剥完自验:
                                再词一遍,和原文的 token 逐个比(种类/文本/**列**/跨度;
                                行不比 —— 空行收掉了,行号本来就往前挪,列才是不动的那个)
Cli/                            **引擎外面那个程序**:三种跑法 + 测试运行器
  Program.cs                    CLI 入口(REPL / test / 单文件 / strip);`RunRepl` 就两行 ——
                                `using "repl.rav"` + `Repl.Run ()`,REPL 本体在库里
  Strip.cs                      `ravel strip <文件或目录>` —— 就地剥注释(发布时用),
                                **不进解释器**:这是源码加工,不跑代码
  Testing/GoldenTestRunner.cs   golden test 运行器(解析/执行/比对/汇报)
  Testing/LoopbackServer.cs     自己拿 `TcpListener` 说 HTTP,给的是**定死的字节**

lib/
  predefined.rav          **启动时第一个加载的文件**:别名 + 控制流(`if` / `while` / `callcc`),
                          再用 `using` 把下面几个子模块拉进来 —— 名字都落在**全局作用域**上,
                          和从前全写在一个文件里一样,用户看得见的名字一个没变。
                          `callcc` 也在这儿(库函数:包装 System.CallCC,拍/还原两份控制状态)。
                          空值那三条糖(`?.` / `??` / `??=`)的落点也在这儿:`IsNothing`
                          (「空」**只有 `None`**)/ `NilChain` / `NilOr` / `NilFill` ——
                          引擎只在解析期脱糖(见「语法糖」那一节),语义在这四条里。
  numbers.rav             `INumber`(空槽接口:谁是数)+ 五种数值类型各实现一条
  iterator.rav            `IEnumerable`(继承 `IMonad`,体内一整套**默认实现**:
                          Count/Map/Where/Bind/Take/First… —— 凡实现者都有,见下)
                          / `IEnumerator`(继承 `IEnumerable`:枚举器**自己就是自己的枚举器**,
                          单遍、走一遍就消耗掉)/ `Enumerator` + 几种容器的实现 + `foreach`
  seqs.rav                `Seqs` 模块:摊平 / 切块 / 拉链 / 分组 / 计数
                          (五件都对着 `IEnumerable` 写,交回当场算好的 list / dict)
  time.rav                `Time`(毫秒 + 一袋零件:Year/Month/… /WeekdayName/Text/AddDays…)
                          + `Now` / `TimeFrom` / `IsoFormat`;登记进 `IComparable`,Sorting 直接吃
                          (上面那五条 `System.*Time*` 是它的原语)
  generator.rav           `Generator f` —— 把"往外送值"的一段代码包成 `IEnumerable`
                          (体的参数 `y` 是投喂口:`y v` 送出并挂起;惰性,可无限流)。
                          实现是 227 那个"两枚续延"原型,状态收进 `GeneratorCursor` 的字段
  docsite.rav             模块 `DocSite` —— **教程 → 静态站的渲染器**(`DocSite.Build out`)。
                          零依赖(纯静态 HTML + 一个 CSS,没有 JS、没有 CDN)。住在 `lib/`
                          而不是 `examples/`:**REPL 要用它**(菜单里那条「教程」),标准库反过来
                          依赖 `examples/` 就把方向搞反了。它的输入就是 `docs/tutorial/`
                          那一格:`*.md` 是内容、`style.css` 是样子,**两样都从磁盘读**
                          (样式从前是这文件里一个一百多行的 `"""`,改颜色得动 Ravel 源码)。
                          两样都少了就**当场报错**,不生成一团没样子的 HTML。`docs/` 是跟着
                          发布走的(见 `Ravel.csproj` 的 `CopyDocs`),所以发布出去那份也能就地生成。
  tasks.rav               `Tasks` 模块 —— **协作式任务**(要显式 `using "tasks.rav"`)。
                          一个 OS 线程,任务只在**挂起点**换人:`TaskGroup`(Run / Await / Add)
                          跑一列任务。**任务那一族分三层**:
                          `ITask`(组认的那张脸:`Done`/`Ok`/`Err`/`Claimed`/`IsDone`/`Value`/
                          `WaitOn` —— 存句柄也注解成它,组的参数也收它,于是"传了个不是任务的
                          东西"在**调用点**就报)、`TaskBase`(共用的状态 + `IsDone`/`Value` 的体,
                          顺带一句 `impl (ITask TaskBase …)` —— 子类跟着认)、
                          `Task`(自创,`Tasks.Task (group) => {…}`)/ `SysTask`(自动推进,
                          `Tasks.After ms`,底下是 `System.Waitable` 句柄)。
                          子类自己只写一件事:**怎么被等**(`WaitOn`),于是调度器那一句
                          `t.WaitOn this` 就是全部的分派 —— 没有"是哪种"这种真假标记。
                          **也在 `IMonad` 那一族里**(`lib/monad.rav`)—— `Map` / `Bind` 挂在
                          `TaskBase` 上,外加构造子 `Tasks.Return`,于是任务能进 `do { … }`:
                          `g.Await (do { a :< t1; b :< t2; Tasks.Return (a + b); })`。
                          (这是全套库里**唯一一处两个库自动接起来**的地方 —— `do` 是解析期
                          就地长成 `.Bind` 链的。注意最后一句要 `Tasks.Return`:裸值不行。)
                          `All` **是并发的**(先把几个都 `Schedule` 进队再收 —— 自创任务的体
                          到第一次被 `Await` 才跑,所以"一个个等"天然串行),而 `do { }` **是
                          串行的**(monad 的 `Bind` 一环扣一环):前者用于互不依赖的几件事,
                          后者用于"后一件要前一件的结果"。
                          **`Signal` / `All` / `Any` / `Chan`** 是上面那套的常用件:
                          闸(有人等、有人开)、`All`(一起跑,按交进去的顺序收结果)、
                          `Any`(等最先完成的,交回那个任务)、`Chan`(能等的队列,
                          空了就睡、`Close` 放人)。
                          **第三种"会完"的东西是 `Signal`**(一只闸:有人等、有人开):
                          `Task` 等"那段代码跑完"、`After` 等"那个计时器走完",闸等"**别人跟
                          我说一声**"。它也是**外面写新挂起点时唯一该用的封装** ——
                          有它,写能等的队列 / 事件 / 信号量的人不碰调度内脏
                          (`examples/signal.rav` 拿两版队列对着看)。
                          组的 `Add` 只收自创的那种(`Task`):自动推进的加进就绪队列没意义
                          (它的完成不靠谁跑它),要等它 `Await` 就够 —— 这一条现在写在签名上。
                          **任务是一次性的**:同一个 `Task` 跑第二遍(或加第二遍)会被 `Add`
                          拦下来报错 —— 它的续延是多发的,再跑一遍就是跳回上一次留下的那条帧链,
                          实测**死循环**。(`Generator` 那一族相反:体是配方,每次遍历从头。)
                          **任务失败会带上出错位置**:位置和调用栈是引擎的内部状态,只在
                          handler 体里活着,所以 `Start` 是**趁那一刻**用 `System.FormatError`
                          渲染下来挂在任务上的(`Task.Where`),收尾那句"没人接的错"才报得出
                          在哪一行 —— 那一条是唯一通道,不趁早存就永远看不到。
                          `IsDone` / `Value` **都不阻塞**:`Value` 是"检查过的读",没算完就报错,
                          让调用方去 `group.Await` —— **"等"只有 `group.Await` 一个入口**
                          (藏在 by-property 里的让出,读代码的人看不见)。
                          挂起/恢复照抄 `generator.rav` 的 `GeneratorCursor`(三条规矩见文件头)。
                          用例:`tests/task/297_tasks.rav`
  cached.rav              `Cached count f` —— 记忆化:按实参把 f 的结果记下来。**是个函数**
                          (不是类型),直接交出包装函数;缓存本身是它捕获的两个 list
                          (所以每调一次是独立的一份,也不进 AllTypes)
  monad.rav               **Monad 这一族**:`IMonad` 接口(Map + Bind 两条槽 —— 能进 `do { … }` 的形状)
                          + 一个实例 `Option`(Some/None:Has/Inner + IsSome/Value/Bind/Map/Where…)
                          —— 它**也是 `IEnumerable`**(0 个或 1 个的一串,impl 在 iterator.rav 末尾)
                          + 构造子 `Some` / `None`
  match.rav               `match v [每对 条件/结果] 默认` —— 按顺序试谓词,第一个为真的胜出
                          (条件多半就是 `is.int` / `<.0` 这种**运算符节**;结果与默认值都得是
                          **可调用的**——推荐写成块,命中那一支才跑;普通值当场报错)
  expected.rav            同族的另一个实例 `Expected`(Ok/Err:Has/Inner/Err + IsOk/Value/Error/Message
                          + Map/Bind/Exists)+ 构造子 `Ok` / `Err`
                          + `Expect argCount f` —— 把一个函数包成"调用返回 Expected"的那种
                          (喂满 argCount 口才真调用,报错包成 Err,不往外抛)
  sorting.rav             `IComparable`(一条槽:`CompareTo`)+ 六种内建标量的空登记
                          + `Sorting` 模块(Compare / Sort / SortBy / Max / Min / MaxBy / MinBy)
  keys.rav                `IKey`(一条槽:`Key`)+ 值类型那六种的空登记 + `IDict`
                          (**把 `Get`/`Set`/`Has`/`Remove`/`GetOr` 注入给 `dict`** ——
                          引擎那套同步的留在 `Sys*` 上)+ `Keys.Of`(规范键)
                          + `Keyed`(以任意值为键、连原键一起记着的表)
  exceptions.rav          `Ex` 模块(HandlerStack / Throw / Try)+ **错误钩子**(引擎冒泡时叫的
                          就是它:`System.SetErrorHook`)+ 小写别名 `try` / `throw`。
                          引擎不认识 HandlerStack,那块状态归这儿管 —— 从前是单独的 try.rav,
                          并进来之后又拆成这个文件。
  io.rav                  文件系统:接口 `IEntry` / `IFile` / `IDir`(全局名)+ 四种实现
                          (磁盘 `File`/`Dir`、控制台 `Stdout`/`Stderr`/`Stdin`、内存
                          `MemFile`/`MemDir`、JSON 树 `JsonNode`/`JsonFile`)+ 通用件。
                          要显式 `using "io.rav"`;详见「文件系统」一节
  test.rav                `Test` 模块 —— 给自己的库写断言用(`Check` / `Equal` / `Fails` /
                          `Report`),要显式 `using "test.rav"`。断言跑的是"一段东西报不报错"
                          (建在 `Expected` / `Expect` 上),`Report ()` 打汇总、有没过就抛出去
                          —— 于是脚本以非零退出码结束。仓库自己的用例仍是 golden 那一套
  random.rav              `Random` 模块 —— **可选择的**随机数生成器:`Random.Shared ()` /
                          `Random.Make seed`(同种子同序列)/ `Random.Crypto ()`,取数那面
                          (Int/Below/Float/Choice/Shuffle/Sample)是 `IRandom` 的默认实现。
                          `using "random.rav"` 引入(引擎那三条原语见「System 模块」)
  regex.rav               `Regex` 模块 —— 正则表达式:`Regex.C pattern` / `Regex.With pattern flags`
                          / `Regex.Escape s`,命中的那段是 `RegexMatch`(`Value`/`At`/`Groups`/
                          `Group 1`/`Group "名字"`)。**要显式引用**;引擎那六条原语见「System 模块」
  dataclass.rav           `dataclass` —— **数据类**的元类(父类是 `type`,造出来的是类):
                          `Point ::= dataclass { x: int = 0; y: int = 0 }` 之后
                          `Text ()` / `Values ()` / `Eq` / `==` / `!=` / 按位置收的 `init`
                          全都自动有(用户自己写了同名的就以用户的为准)。
                          字段是"自己那层里值不是函数的那些",**到用的时候才算**,
                          所以拿它当父类也认得出子类新加的字段。**要显式引用**
  enum.rav                `enum` —— **枚举**的元类(父类同样是 `type`):
                          `myEnum ::= enum { A := 1; B := 2 }` 之后
                          `myEnum.A` 是一枚 `myEnum` 类型的值(`.Value` / `.Name` / `Text ()`),
                          类上挂 `Values ()` / `Of v`。**元类只管固定的那一套**,`A`/`B`/`C`
                          一个都不写死 —— 它建一个**空类**,拿普通实例当落脚点把那份
                          块跑一遍(靠"`with` 不推层"),再逐个换成值挂到类上。
                          **继承**:父类得是 enum **一族**(`<: enum`,普通类当场报错),
                          父类那几枚**迁过来**(重做成这个枚举的值),这一层的块接着填新的。
                          **要显式引用**
                          `flags` —— **按位枚举**,`enum` 的子类:`perms ::= flags { Read := 1;
                          Write := 2 }`。声明那一套照旧,多出来的是位运算(`|` 并 / `&` 交 /
                          `^` 对称差 / `-` 差)、`Has b`(有没有 `b` 那几位的全部)、`Names ()`
                          (有哪几枚声明过的成员)、`Combine`。另边可以给**同一个 flags 的值**
                          或**整数**。**组合值不是成员**:`Read|Exec` 是现拼的一枚(不在
                          `Values ()` 里、`Name` 空),`Text ()` 现算成 `Read|Exec`;`Of v` 找不着
                          成员就现拼,正好等于某枚成员时交回**那一枚**。`==` **按值比**
                          (身份比的话组合每次都是新对象,永远不等),但仍限同一个 flags。
                          实现上两处靠"子类盖父类那份 `Members` / 包一层 `init`"——
                          名字是**动态解析**的,覆盖在取别名**之后**才生效。
  format.rav              `Format` 模块 —— 拼串与排版:`Fmt "{} 有 {} 个" [a b]`(占位/位置复用/
                          给 dict 就按名字取)、`PadL`/`PadR`/`Pad`/`Center`(对齐与填充)、
                          `Num x 2`(定小数位)、`Thousands`(千分位)、`Hex`/`Bin`/`Oct`、
                          `Bytes n`(1536 → "1.5 KB")。**要显式引用**
  text.rav                `Text` 模块 —— 按行/按词那层:`Lines`(顺带去掉 Windows 的 `
`)/
                          `Words`(空白折成一个分隔)/ `Wrap n`(按宽度折行,长词不硬切)/
                          `Indent` / `Dedent`(去公共缩进)/ `Truncate` / `Quote`(转义成看得见的样子)/
                          `IsBlank` / `Tree root children label`(任意结构画成 `├── / └──` 那棵树 ——
                          `Types.PrintTree` 与 `examples/refgraph.rav` 用的都是它,画树只有这一份)。
                          **要显式引用**
  csv.rav                 `Csv` 模块 —— 逗号分隔(RFC 4180):`Parse`(首行当表头 → 一行行 dict;
                          列少了补空串、多了丢掉)/ `Grid`(纯格子,不认表头)/ `Render`(交 dict
                          或 list 都行,表头按键**第一次出现**的先后)/ `Quote`(单个格子怎么转义)。
                          转义就一条:含 `,` / `"` / 换行的格子整体包引号、里面的 `"` 写成 `""`
                          —— 所以引号里的逗号和换行都是**内容**。**要显式引用**
  args.rav                `Args` 模块 —— 命令行参数:`Parse argv`(不看规格)/
                          `ParseWith argv spec`(名 → 类型:`flag`/`int`/`float`/`string`/`list`)/
                          `Get a name dflt` / `Has` / `Positional` / `Usage spec`。
                          认 `--x=v` / `--x v` / `--x` / `-x` / `-abc`(合并的短开关,**不带值**)/
                          `--`(之后全算位置参数);`-5` 和光杆 `-` 当位置参数。
                          **没给规格时光杆一律当开关**(不带 `=` 就不吃后面那个实参),
                          给了规格就只在声明成带值的名字上吃 —— 不猜。结果是一张 dict:
                          选项进同名的键,位置参数进 `"_"`。**要显式引用**
  table.rav               `Table` 模块 —— 打表格:`Render rows`(一串 dict → 表头取键;
                          一串 list → 不带表头)/ `RenderWith rows opts`(`cols`/`header`/
                          `align`/`style`:box 框线 or plain 光板)/ `Width s`。
                          **最要紧的是宽度**:算的是"终端里占几格"(东亚宽/全角/emoji 算 2),
                          不是 `s.Length ()` —— `Format` 那几条数的是字符数,拿来对齐中文会歪。
                          数值列自动右对齐,整列混着字和数就不硬凑。**要显式引用**
  log.rav                 `Log` 模块 —— 分级日志:`Log.New {"level" "file" "tag" "stamp" "err"}`
                          → `Logger`(`Debug`/`Info`/`Warn`/`Error`/`Write level msg`/`Line`/
                          `SetLevel`/`Enabled`);不拎 logger 就用默认那台 `Log.Info "…"`
                          (级别看 `RAVEL_LOG`,没设就 info)。四个级别 **debug < info < warn <
                          error**,松的一律不落笔(过滤在拼串之前)。落点:给了 `"file"` 追加到文件、
                          `"err": true` 走 stderr,否则 stdout;落盘失败**当场报错**。
                          **要显式引用**
  terminal.rav                `Terminal` 模块 —— 终端:`Render`(渲染标记,当前是终端就上色)/
                          `Plain`(**强制不上色**,要确定性的输出用它)/ `Strip` / `Write` /
                          `Line` / `ToErr` / `Tty ()` / `Width ()` / `Ask` / `Confirm` /
                          `Choose` / 几条糖(`Red` / `Good` / `Warn` / `Bad` …)/ `Bar` /
                          `Rule` / `Tick`。标记就是 **Spectre.Console 那套**
                          (`[red]…[/]`,`[[` 是方括号),不是新发明的一套。
                          **渲染那两条是纯函数**(交回一串,不往屏幕上写)—— Spectre 那台静态
                          `AnsiConsole` 会把第一次见到的 `Console.Out` 抓住,从引擎那边直接写
                          会绕开 `Console.SetOut`(测试运行器换的就是它)。本机那几条在官方扩展里。
                          **要显式引用**
  xml.rav                 `Xml` 模块 —— XML:`Xml.Parse text` → 一棵 `XmlNode` 树 /
                          `Xml.Render n` / `Xml.Element name` / `Xml.Text s`。节点上是
                          `Kind` / `Name` / `Attrs` / `Attr` / `AttrOr` / `Text` / `AllText` /
                          `Child` / `Find` / `All` / `Kids` / `Elements` / `Add` / `AttrPut` /
                          `Delete`,而且它**既是 `IFile` 又是 `IDir`**(孩子就是子元素)——
                          和 `io.rav` 的 `JsonNode` 同一个模型:节点是一层包装,底下是
                          普通的 dict / list。三条取舍:**命名空间不参与**(前缀与 `xmlns:`
                          都丢掉,按局部名认)、**空白文本节点不收**、属性是有序表。
                          本机两条在官方扩展里(`XmlParse` / `XmlRender`)。**要显式引用**
  html.rav                `Html` 模块 —— **拼 HTML**(只生成,不解析)。分两层:
                          **元素层** `El name attrs kids` / `Text` / `Raw` / `Frag` /
                          `Render`(**紧凑**) / `Pretty`(缩进) / `Doc lang head body`(doctype +
                          `<html lang>` + head/body,自动补 `<meta charset>`) / `Doctype`;
                          **块级层** `H1`~`H6` / `P` / `Blockquote` / `Code` / `Pre` / `Ul` /
                          `Ol` / `Table headers rows` / `A` / `Img` / `Hr ()` / `Br ()` /
                          `Style css` / `CssLink href` / `Page opts body`(opts 认 `"title"` /
                          `"lang"` / `"css"` / `"head"`) / `BaseCss`(一份能看的默认样式,
                          **opt-in**,不自动带)。
                          节点就是**普通 dict / list** —— 和 `xml.rav` 底下那棵树**同一个形状**
                          (`kind` / `name` / `attrs` / `kids`,`kind` / `text`),所以自己手拼一棵、
                          把别处拿到的树喂进来都认;两层交回的是同一棵树,可以混着写。
                          转义默认开:**文本**躲 `&` `<` `>`(引号不转,`<pre>` 里的代码
                          免得变成 `&quot;`)、**属性值**再过一层 `Encoding.HtmlEscape`
                          (那里引号得转,不然属性提前收尾);属性 `None`
                          不写 / `true` 光写名字 / list 按空格拼(`class`);void 元素那 14 个
                          不写收尾标签,给了孩子**当场报错**。`Pretty` 只在"自己块级、
                          孩子里**也有**块级"时才拆行(少了后半条,段里那些行内元素会被拆开、
                          多出空格 —— 那是改内容);`<style>` 的内容走 `Raw`(那两个标签里
                          实体不解码)。**要显式引用**
  glob.rav                `Glob` 模块 —— 通配符:`*`(不跨 `/`)/ `?` / `**`(跨 `/`,零层也算)/
                          `[...]`(`[!...]` 取反)。`Match pat path`(**整串比**)/ `Filter` /
                          `Split`(分成相符与不相符两堆)/ `Find pat`(从当前目录递归找)/
                          `FindIn dir pat` / `Under dir pat` / `ToRegex pat`(翻成正则,自己拿去用)。
                          翻成**正则**再比 —— 通配符本来就是正则的真子集,不必再写一台匹配机。
                          磁盘走出来的路径 Windows 上带反斜杠,比之前先归一成 `/`。**要显式引用**
  zip.rav                 `Zip` 模块 —— **ZIP 归档**,而且它**是一个文件系统**:
                          `Zip.Open path` / `Zip.Create path` 交回一个归档(它是 `IDir`),
                          成员是**只读**的 `IFile`(`Read` / `Bytes` / `Size` / `Packed`)——
                          于是 `Io.EachDir` / `Io.Lines` / `Io.CopyTo` 那些对着接口写的一律照吃。
                          `Add` / `AddFile`(流式)/ `AddDir` / `Extract`(流式)/ `Close ()`。
                          归档里的**目录是推出来的**(`logs/a.txt` 意味着有个 `logs/`),
                          名字认两头:先当完整路径、再当这一层的名字。**要显式引用**
                          (本机六条在官方扩展里,见「官方扩展」一节)
  native.rav              **官方扩展的 Ravel 那一半** —— 就一行 `using "Ravel.Extensions"`,
                          把 `Native` 模块装进来(`Hash`/`Crypto`/`Http`/`Regex`/`Sqlite`/`Random`
                          那几个库的本机半边都在那儿)。要用扩展的库都 `using "native.rav"`,
                          dll 的名字**只有这一处**(后缀和 `plugins/` 都省了,见「插件」一节)。
  hash.rav                `Hash` 模块 —— 摘要与校验:`Sha256`/`Sha512`/`Sha1`/`Md5`(字符串按
                          UTF-8 进、交回小写十六进制;要字节表用 `...Bytes`)、`Hmac algo key data`、
                          `File path algo`(**流式**,多大的文件都不进内存)、`Crc32`(纯 Ravel 算的)、
                          `Equal a b`(**常数时间**比,防时序攻击)、`Token n`(随机十六进制串)。
                          底层三条原语在**官方扩展**里(`Native.HashBytes`/`HashFile`/`HmacBytes`,
                          收字节表交字节表 —— 见「官方扩展」一节)。
                          **要显式引用**
  crypto.rav              `Crypto` 模块 —— **加密与口令**(和 `hash.rav` 是两件事:那边是
                          "防篡改",这边是"藏起来")。`Seal key data` / `Open key text`
                          (AES-256-GCM,nonce 现取、随在密文前面,交回/收下 base64 串;
                          要字节表走 `...Bytes`)/ `Key password salt rounds`(PBKDF2-SHA256 →
                          32 字节)/ `Salt ()` / `HashPassword password` /
                          `CheckPassword password stored`(**存的是"怎么校验",不是口令本身**——
                          格式 `pbkdf2$sha256$轮数$盐$哈希`,轮数跟着一起存,以后加轮数老串照样校验;
                          校验走常数时间比)/ `Equal` / `Token`。底层三条原语在**官方扩展**里
                          (`Native.AesSeal`/`AesOpen`/`Pbkdf2`,收字节表交字节表)。**要显式引用**
  uuid.rav                `Uuid` 模块 —— `V4 ()`(随机)/ `V7 ()`(头 6 字节是毫秒时间戳,
                          先造的排前面,拿来当数据库主键不捅索引)/ `Nil ()` / `IsValid s` /
                          `Version s` / `Bytes s` / `FromBytes bs` / `Time s`。**不占引擎**:
                          随机数是 `Native.RandomBytes`(在官方扩展里,见「插件」)、时间是 `System.NowMs`,剩下的就是
                          摆位(RFC 9562:第 7 字节高 4 位是版本、第 9 字节高 2 位是 variant)
                          和格式化。**要显式引用**
  sqlite.rav              `Sqlite` 模块 —— 一个文件就是一个库:`Sqlite.Open path`(不存在就建)/
                          `Sqlite.Memory ()`。`db.Exec sql args`(建表/增删改,**参数按名字**:
                          `@n` ← `{"n": …}`)/ `All`(一行一个 dict)/ `One` / `Value` / `Each`
                          (逐行)/ `LastId ()`(bigint)/ `Tx { … }`(出错自己回滚)/ `Close ()`。
                          换算:`()` 就是 **NULL**、布尔存 0/1、字节表是 BLOB、`bigint` 要装得下
                          64 位。**要显式引用**(引擎那五条原语见「数据库」一节)
  http.rav                `Http` 模块 —— 网络那一层(HTTP 客户端):`Get`/`Post`/`Put`/`Patch`/
                          `Delete`/`Head`、`Request`(全参数:头/正文/重试/超时/跟不跟重定向)、
                          `Download`(流式落盘)/ `Upload`(multipart)、`Query`(拼查询串)、
                          `Expect`(非 2xx 就抛);交回 `Response`(status / reason / headers /
                          body,`Text ()` 按 charset 解、`Json ()` 直接当 Json 用)。
                          **同步**:一个请求等一个(要显式 `using "http.rav"`)。想**重叠**用同名 + `Task` 后缀那一族
                          (`GetTask` / `RequestTask` / `DownloadTask` …):交回一个任务,
                          `group.Await` 它的时候别的任务接着跑;底下和同步那条是**同一份实现**,
                          所以报错文案一字不差(见「并发」那节)
  httpd.rav               `Httpd` 模块 —— HTTP **服务端**:`Server`(Route / Static / Run /
                          RunIn / Stop / Conns / Requests)、`Request`(Method / Path / Query /
                          Headers / Body / BodyText () / Param)、响应构造器(Text / Json /
                          Html / Bytes / Redirect / File g / NotFound)、`Serve` 那条糖。
                          handler 一律 `(req g) => Response`(`g` 是这次请求的调度组)。
                          原生半边是 `Native.HttpListen` 那六条(见「网络」一节;要显式
                          `using "httpd.rav"`)。**没有裸 TCP**:和客户端一样只做到 HTTP 这一层
  bits.rav                `Bits` 模块 —— 位那一套:`Test`/`Set`/`Clear`/`Toggle`/`Not`(单个位)、
                          `Count`(popcount)/`Width`/`High`/`Low`/`ZerosHigh`/`ZerosLow`(数位)、
                          `Mask`/`Field`/`PutField`(一段位)、`Bytes`/`FromBytes` 与 `...LE`
                          (和字节表互转,大端/小端)、`Shr`(**逻辑**右移 —— `>>` 是算术的)、
                          `Unsigned`/`Signed`(32 位位型 ↔ 非负数)。位下标越界**当场报错**。
                          **要显式引用**
  encoding.rav            `Encoding` 模块 —— UTF-8 / Base64 / 十六进制 / URL 转义 / HTML。
                          字节表 = "一串 0..255 的 int"(`Random.Bytes` 交回的正是它)——
                          Ravel 的 `string` 是 UTF-16,装不下任意字节,所以**要文本就先
                          `Utf8Text`,要编码就先进字节表**。**要显式引用**
  math.rav                Math 模块 —— **本机那一整套在官方扩展里**(见「官方扩展」一节),
                          这个文件只往上补 Ravel 说得清的那四个(square/cube/deg/rad)。
                          `using "math.rav"` 引入
  types.rav               Types 模块:`PrintTree` 打印类型树(沿 Subtypes () 取直接子类,
                          **接口那一支再挂一句 `实现 ← …`**(`GetImplementors ()`,当下那份快照),
                          `├──/└──` 那套缩进是 `Text.Tree` 画的 —— 这一条只是"子类是谁 /
                          这一行写什么"两枚 lambda),`using "types.rav"` 引入(它会带上 text.rav);
                          examples/type_tree.rav 打的就是它
  iomonad.rav             IoMonad 模块(IO Monad —— 把效果做成值的那种,不是文件/终端 IO):
                          `Action`(Effect/Perform/Map/Bind/Then/Discard/Attempt/Catch)、
                          `Return` / `PutStrLn` / `PutStr` / `PutStrLnErr` / `PutStrErr` / `GetLine` /
                          `Foreach` / `Sequence` / `When` / `Unless`;`Action` 把"要做的效果"做成值,
                          `Perform ()` 才真跑;Return/PutStrLn/PutStr/GetLine/Foreach。
                          `using "iomonad.rav"` 引入 —— `Action` 也实现了 `IMonad`
                          (它和 `Option` 是**同一个形状的两个实例**:各自那份 Map/Bind 就是形状本身,
                          `impl (IMonad Action { () })` 只是登记一下;`impl` 是全局的,所以
                          `x is IMonad` 在哪儿都成立)。两者的 `Bind` 各干各的:这边真跑效果,
                          那边没有值就短路
  app.rav                 示例脚本(math + try 的冒烟),手动跑:
                          dotnet out/ravel.dll lib/app.rav

tests/                    golden test(普通 + expect-error + todo + fixture),按类型分子目录
                          (lang / class / module / diag / lib / net / task),个数以目录为准

docs/tutorial/            教程的 **markdown 源**(一章一个文件)—— 那个静态站就是拿它生成的
docs/adr/                 几条大决定
examples/site.rav         壳:生成到 `site/`
examples/serve.rav        一键:生成 + `Httpd` 起服务器,浏览器里看
                          (两个都从 `docs/tutorial/` 读,而 `docs/` 是**跟着发布走**的
                          —— 见 `Ravel.csproj` 的 `CopyDocs`;不带上的话从 `out/` 跑会
                          **静默生成一个空站**,所以 `Build` 里现在一句 `Chapters.IsEmpty ()` 就抛)

.vscode/                  VS Code 工作区配置
  tasks.json              Ctrl+Shift+B 跑当前 .rav(默认)、ravel: 全量测试
  launch.json             F5 跑当前 .rav(可下断点) / 原来的 REPL 配置

vscode-ravel/             VS Code 扩展:语法高亮(TextMate) + 运行命令
                          装法:复制成 ~/.vscode/extensions/ravel.ravel-language-0.1.0/
```

## 求值器架构(显式帧栈)

见 [ADR-0002](adr/0002-explicit-stack-evaluator.md)。要点：

- 整个程序一个块根帧,`StepOnce()` 扁平循环逐帧推进,C# 栈恒平。
- 每节点状态机按 `Results.Count` 推进;`Return` 把结果交给父帧。
  **块那一格是例外**:`BlockExecFrame.Index` 是**显式**的"下一条语句",不在 `Results` 上做文章 ——
  `Results` 只存跑完的那些值。为什么单拎出来:位置一旦从"结果表有多长"推,"往帧上追加一个结果"
  就等于"块往前走一句",两件事被绑死在一起,而**续延恢复**干的正是前者
  (`_top = k.Captured.WithResult(arg)`)—— 于是"恢复"和"推语句"共用同一个动作。
  **这一条本身不改可观察行为**(2026-10-02 拿 11 个用例对过,改前改后逐字相同);
  别的语言里位置本来就显式(C# 的 async 状态机、Go/Lua 的协程栈)。
- 调用分派(`Interpreter.Call.cs` 的 `CallInto`):`ControlFunction`(控制帧)/`LambdaVal`(推 body 帧)/`NativeClosure`(同上,但体是 C#)/`BlockVal`/`ClassVal`(**类对象,调它 = 实例化**)/`ComposeVal`(prepend/append)/`BoundClassOp`(类运算符)/`PartialCtor`(半成品构造器→CtorApply 帧)/`ContinuationVal`(还原帧链);其余 `FunctionVal` 走默认分支,同步调 `Body(arg)` 把值塞回 sink
(被分派掉的那些值,`Body` 是 `FunctionVal.PlaceholderBody` —— **真被调到就报错**,
不再静默交出 `()`,也不让硬转抛 C# 异常:漏一个 `case` 要当场出声)。同步函数一律是「参数→结果」,没有 Step 包装(旧 CPS 的 `Done` 壳已删除)。
- 控制内建(`with`/`callcc`/`using`/`eval`)= `ControlFunction(Kind, Arity, Args)` 纯数据,收满参数推控制帧。求值器内部还会合成 `Alternate`/`ClassInit`/`Compose`/`ClassOp`/`CallAssign`/`CallReturn`/`CtorApply` 控制帧。`ControlKind` 因此只有 11 个值。
- **构造器调用与普通函数同一条柯里化路径**:`Point 3 4` ≡ `((Point 3) 4)`。`ClassInit` 建好对象、跑完类体后把参数喂给 `init`;**交出的是 `init` 的返回值**(约定 `this`),`init` 还返回函数(参数没收齐)就交出 `PartialCtor` 半成品,由 `CtorApply` 帧继续喂。
  判"还没收齐"那句是 `HalfCtor(...)`(`Interpreter.Control.cs`):`IsClosure` 再排掉**可调用但调用起来不是"接着收参数"**的那几种。除 `Bool`/类对象(`IsClosure` 里已经排掉)之外,**续延也得排** —— 调续延是跳转,不是喂参数;不排的话 `Continuation f` 一造出来就被包成半成品,`typeof` 立刻看不出它是续延。
- callcc 只有一套语义:续延 = callcc 之后的剩余计算;调用它 = 丢弃当前帧链、从捕获点继续(详见「控制流」)。
  交出去的那枚**类型是 `Continuation`**(`<: Function`,自己也挂 `Function` 下面):库里的 `callcc` 用 `Continuation f` 把"先还原控制状态、再跳"那层也包成续延,所以用户手里那枚类型上就是它(`ContinuationVal` 的两个字段:`Captured` = 引擎交出来的那种,`Jump` = 包出来的那种)。`default` 是**还没到手的那一枚**,一调就报错 —— 它是跳转,没有目的地就该响,不能做成"什么都不做"。
- 深度递归 20 万层安全(原 CPS ~4k 层爆栈)——但那是 **C# 栈**安全,不是内存安全:
  每层留 5~6 个帧(BlockExecFrame + 各语句/表达式),合起来约 1KB,
  20 万层要 250MB 上下。所以它在默认堆下跑得通,在测试用的
  DOTNET_GCHeapHardLimit=0x10000000(256MB)下会 OOM。没有尾调用优化,长递归就是吃内存。

## 数字字面量

`NumberLiteral` 存的是**原始文本**（`Lexeme`），不是 `double`——double 只有 15~17 位有效数字，
大整数中转一手就丢精度。求值时才定类型（`Interpreter.MakeNumber`）：

- 有小数点 → `Float`（所以 **`2.0` 是 float**——形状说了算，不是值）
- 没有小数点且装得下 int32 → `Integer`
- 没有小数点但超了 → `BigInt`（所以 `typeof 2147483648` 是 `BigInt`）

**后缀**（`NumberLiteral.Suffix`，词法那一步挑出来）是**明说类型**，不再猜：

| 写法 | 是什么 | 备注 |
|---|---|---|
| `2n` | `BigInt` | 放不放得下 int 都走 bigint |
| `2i` | `Integer` | 装不下 int 当场报错，**不悄悄升级**——这正是它和"没后缀"的区别 |
| `2f` / `2.5f` | `Float` | 没小数点的也能强制成浮点 |
| `2.5i` / `2.5n` | 报错 | 小数没有"整数后缀"这回事 |

后缀**要吞得干净才算**（`Lexer.ReadNumber`）：后面再粘着标识符字符就不是后缀，
所以 `0if`、`2not` 照旧读成"数字 + 标识符"，一个字都没变；`2n` 后面跟 `)` `.` `[` 空格才算数。

后缀 `bigint` / `int` / `float` 那些是**构造器**（`bigint 123` 把值转成 BigInt），和字面量的类型是两回事。

**int 算术溢出报错,不静默回绕**（`Narrow`）：`int × int` 一律在 `long` 里算再收窄，
超出 int32 就报「`100000 * 100000` 超出 int 范围（int 是 32 位，大数用 bigint）」。
不检查的话 C# 的 unchecked 会让 `100000 * 100000` 得 1410065408、
`2147483647 + 1` 得 -2147483648——「算出来了但是错的」比崩溃难查得多。
一元 `-` 同理（`-int.MinValue` 翻不过来）。要更宽就写 `bigint`：
一边是 bigint 时按宽度升级,根本不进 int 那条路。

一元 `-` 按数值类型逐个翻转（`Negate`），不走 `0 - x`——Int 的 `-` 只认 Float 右操作数。
Int 与右操作数的二元运算按宽度升级（`IntOp`）：`float > bigint > int`，
所以 `1 + bigint 2` 和 `bigint 2 + 1` 都得到 BigInt。

## 类型层次

```
Object (parent=自己)
├── ValueType
│   ├── Integer / Float / String / Char / BigInt / Fraction / BigFraction   (并列,不是链)
│   └── Range         ← 区间(`[1..3]`)：不可变、按值比,自己不吃糖也在这一支下
├── Function
│   ├── Bool          ← true/false 可调用:收两个块返回选中那个的结果
│   ├── Block         ← 没有 Ravel 别名(block 在 ReservedWords 里)
│   ├── Continuation  ← callcc 交出来的那枚续延(ContinuationVal):能调、能存进字段,
│   │                   但 `typeof` 说得出它是续延。`default` = "还没到手的那一枚",
│   │                   一调就报错(调用是跳转,没有目的地就该响,不能静默什么都不做)
│   └── Type          ← 用户类挂这下面(类自己没名字,显示成 class)
│       └── Interface ← `interface` 这个**工厂/元类**(和 `type` 之于类同一个位置):
│                       `typeof 某接口` 就是它。它自己**不是**接口 —— 接口对象挂在
│                       `BaseInterface` 下面那一支
├── List / Set / Dict   ← 直接挂在 Object 下,不经过 Function
├── BaseInterface [Interface]  ← **所有接口的基类**,它自己**也是**一个接口;`interface { … }`
│                              造出来的接口都挂在这下面(它类体里那份 `init` 就这么继承下去)
├── Void / Exception / Ravel(模块) / Scope / Property
│                      └── 错误那一族:TypeError / NameError / AttributeError / IndexError /
│                          KeyError / ZeroDivisionError / AssertionError / AccessError / ArgumentError /
│                          ValueError / IoError / RegexError(见 BuiltinClasses.Errors.cs)
├── Any (顶类型, parent=自己)
└── Every (底类型, parent=自己)
```

**`Ravel` 是个光杆**:它下面不挂东西 —— 模块(`System` / `Math` / 各库的模块)是它的
**实例**,不是子类(所以 `Math is Ravel` 为真,`Math <: Ravel` 不成立 —— 后者要两边都是类型)。
从前每建一个模块就现造一个 `Ravel` 的子类(`NewModuleClass`),
那份类对象只为 `print` / `typeof` 打得出名字,却**不登记进 `AllTypes`** ——
类型树底下于是挂着一堆看不见的子类,树和现实对不上。现在一个 `Ravel` 就够,
名字改由 `ModuleVal.Label` 担 —— 报错里也一样:模块单说成**「模块 'System'」**
(`RuntimeValue.KindName`),不混进"类型 'X'"里。照类型报的话全指向那个共用的 `Ravel`,
会打出「类型 'Ravel' 没有方法 'Class'」—— 用户写的那行里根本没有 Ravel 这个词。

`parent` 是类对象 Scope 里的一个普通成员(不是 C# 字段),所以链到头的方式是**自引用**
(`object`/`Every`/`Any` 的 parent 是自己)—— 遍历这些链的地方都要在 `t.Parent == t` 处停。

这棵树在 C# 侧全是 `ClassVal`(`ClassVal : FunctionVal : ObjectVal`,见下节);
`Bool`/`Block` 的元类分别是 `Bool`/`Block`,不是 `Function`。

`Bool <: Function` 是为了 lisp 式的条件:`true {a} {b}` 执行 a、`false {a} {b}` 执行 b，
于是 `if` 退化成 `(c ()) t e`（见下面「控制流」）。

## System 模块

内置模块，解释器启动时创建。包含所有类型和核心函数：

**类型**: Integer String Char Bool Float BigInteger Fraction BigFraction Scope Range
        List Set Dict **Waitable** Object **Ravel** Void Function Continuation Type Interface
        BaseInterface Any Every Exception ValueType Json

(`Waitable` 是"一个还在做的本机活儿"那个句柄类型 —— `System.Sleepable` 交回它,
`WaitAny` 收它。它自己不带调度策略:怎么排队全在 `lib/tasks.rav`。)

(每个类型在 `predefined.rav` 里都有一条全局别名 —— `int` / `string` / `object` /
`Ravel` …。**`MyMod.References ()`** —— 这个模块**自己写着的那些 `using`**,交回的是**模块本身**
(`using "io.rav"` 给的是 `Io`,不是那串路径)。挂在 `Ravel` 上(所有模块都是它的实例),
所以每个模块白拿。那个文件要是**没写模块名**(`native.rav` / `enum.rav` 这种只往全局
落名字的),一律算到 `<global>` 那一枚上 —— 它们落的就是同一个地方,所以只用一枚替它们站位。
**不是**全局的已加载表、也**不是传递闭包**:`Repl.References ()` 给的是它自己那三条,
不会把 `iterator.rav` 那些也带上。`using` 是运行时构造,所以函数体里、`eval` 出来的也算
—— 但那种本来就是这个模块的一部分代码。(管**搜索目录**的那个全局叫 `ReferencesPath`,
和这一条不是一回事:那个说的是"去哪儿找文件",这个说的是"我引了谁"。)

`Scope` 是从前那个**叫不出来的类型**(只在 `typeof (x.Scope ())` 里露过名字)——
现在 `System.Scope` 是个名字,于是 `impl (IDict System.Scope …)` / `impl (IEnumerable System.Scope …)`
写得出来了(`lib/keys.rav` / `lib/iterator.rav` 各一条);顺带给它补了 `Remove` / `Keys` / `Values` / `Count`
—— 作用域从此和别的表一视同仁。

`Ravel` 是**所有模块的类对象**:模块是它的**实例**不是子类,
所以"这是不是个模块"就一句 `Math is Ravel`。它和 `Scope` 是树里仅有的两个
"光杆"(下面什么都不挂)。)

**函数**(和类型一样,这就是**全部**,一个不多一个不少 —— 钉在 tests/271):

    语言本身   WriteLine Write ReadLine WriteErr WriteLineErr ReadAllInput WriteBytes ReadBytes
              Assert TypeOf Eval CallCC Exit With RavelMod Using Use Impl Unsafe
              Property CurrentScope LoadingState RestoreLoading SetErrorHook WarnForgotCall Unhandled
              FormatError                       ← 把一个异常渲染成 CLI 那份报告
              CaptureStart CaptureEnd           ← 把 stdout 收进字符串的那一对(只有 stdout,
                                                 stderr 不动)。**要成对用**:失败那条路也得收,
                                                 不然 stdout 一直被抓着
    终端       ReadKey ScreenClear CursorTo CursorShow
                                              ← 写编辑器绕不开的那四格(见「REPL 也是用
                                                Ravel 写的」一节):读单个键、清屏、定位、藏光标
    进程边界   Args Env EnvOr SetEnv UnsetEnv EnvAll              ← 命令行与环境变量
              Cmd                                               ← 子进程
              NowMs TimeParts MakeTime FormatTime ParseTime Sleep ← 时间(predefined 加载 time.rav 就要用)
              FileExists DirExists ReadText WriteText AppendText DeletePath CreateDir ListDir
              PathSize PathTime CopyPath MovePath CurrentDir ChDir SplitLines
              PathClean PathJoin PathDir PathBase PathExt          ← 文件那 22 条(syscall 层)
    任务       WaitAny HandleValue Sleepable             ← "等一个本机任务"那三条:等着谁先完成、
                                                        取结果、交回一个"多久之后会好的"句柄。
                                                        调度策略(`Tasks` 那一整套)在
                                                        `lib/tasks.rav`,引擎这半边只认句柄
              ReadTextTask WriteTextTask AppendTextTask  ← 文件那三条的"交回句柄"版(同名 + `Task`)
              ReadBytesTask WriteBytesTask CmdTask       ← 字节那两条 + 跑命令那条,同上。
                                                        和同步那条共用一句报错,见「并发」那节

**`Hash` / `Crypto` / `Net` / `Regex` / `Sqlite` / `Random` 那六族原语不在这张表里** ——
它们是那几个库的本机半边,住在**官方扩展**里,`using "native.rav"` 之后在 `Native`
模块下(钉在 tests/280)。见下面「插件」那节。

上面那份名单**不是手抄的**(分组是我分的,名字不是):谁在 `System` 里,看的是
**方法上的 `[Sys("名字")]`** ——
写在 `Runtime/Builtins/Sys*.cs` 里(**一个主题一个类**,扫的是整个程序集,不列名单),
扫一遍、按名字排、绑成委托(见 `SysRegistry`)。**加一个内置 = 写一个 `static` 方法**:
不用挑分组、不用找位置、也不用记得回来补一行登记 —— 从前那种"漏了一行,静默少个成员"
是这类文件最容易出的错(这一路真漏过一回,58 个成员没了,用例全红才发现)。
那几族类**不持有状态**:要用引擎的把它当第一个参数收(`Interpreter self`),
用不着的就是纯函数 —— 签名对不对在建表时就报。类型别名 / 常量 / 控制内建**不走这条路**:
那是数据,`SysModule` 里三张表一眼看全比撒在各处好读。
**成员表整份钉在 `tests/lib/271_system_members.rav`** —— 加了内置就顺手更新那一行。

**哪一条该留在这儿,尺子是明着的**:要么是**语言本身**要的(类型、控制流、`eval`、反射、
状态钩子),要么是**进程边界**(输出、文件、环境变量、子进程、时间)。剩下的一律是
"某个库的本机半边",去官方扩展(见下面「插件」那节)—— 那道线不按"好不好写"划,
按"这句话说的是这个语言,还是说的一台机器"划。

（文件与进程那几条 —— `FileExists` / `ReadText` / `ListDir` / `Cmd` … —— 见「文件系统」
与「跑外部命令」两节。它们一律**只做 syscall**,策略在库里。）

时间同理,五条原语全在 `System` 里:`NowMs ()`(1970 年起的**毫秒**,bigint)、
`TimeParts ms`(一袋零件:year/month/day/hour/minute/second/millisecond/weekday)、
`MakeTime parts`、`FormatTime ms fmt`、`ParseTime text fmt`(都按 .NET 那套格式串,
时区本地)。"一个时刻怎么显示、怎么比大小"是 `lib/time.rav` 那个 `Time` 的事。）

**进程外面给进来的两样东西**（命令行参数与环境变量）也在这儿，同样只做 syscall：
`Args ()` 给的是**脚本名之后**那些参数（由 CLI 填进 `Interpreter.ScriptArgs` 那一个口子；
REPL 和 `ravel test` 没读命令行，它俩那里是空的 `[]`）；
`Env name`（没有就报错）/ `EnvOr name dflt` / `SetEnv name value` / `UnsetEnv name` / `EnvAll ()`。
这几条只动**本进程**那份（新起的子进程看得见），写空串等于删掉（.NET 那套）。

**随机数的"源头"**也在这儿，四条：`NewRandom seed` / `SharedRandom ()` / `XoshiroRandom seed`
各交回**一枚函数**
（`() => int`，0 .. 2^30-1 —— 包着 `new Random(seed)` / `Random.Shared`），
`RandomBytes n` 交回 `list`（元素 0..255，走 `RandomNumberGenerator`）。
**交回函数而不是新造一个值类型**：和 `Cached` 一个路子（"是个函数，不是要实例化的类型"），
引擎面最小、也不必动类型树。四台生成器（共享 / .NET 带种子 / **xoshiro256\*\*** / 加密）、`Int`/`Below`/`Float`（也能收区间）/
`Bool`/`Choice`/`Shuffle`/`Sample`/`Choices`（有放回）/`Weighted`（按权重）/`Normal`（箱–穆勒）/
`Bytes` 那些都在库里
（`lib/random.rav` 的 `IRandom` 默认实现）—— 取数的那一面**只有**这一处（从前那个全局
`randint lo hi` 已经删掉：它的上界不含是 .NET 的老规矩，`Int lo hi` 改成含两端）。

**区间**`Range` 是个**内置值类型**（不是库里的类）：`[1..3]`（两端含）/ `(3..5)`（两端不含）/
`[1..5)` / `(1..5]` —— 那一对括号**各带一半的意思**，所以四个组合都认（收尾两种右括号都收，
混着写合法）。词法上 `..` 是一个 token（`Lexer` 里排在单个 `.` 前面；`ReadNumber` 只在 `.`
后面跟数字时才当小数点，所以 `1..3` 与 `1.5` 互不干扰）；语法上**只在 `[` / `(` 里认**
（裸的 `a..b` 不成立），两处入口都是"记下 `_pos` 试读一把、不成再退回去走原路"——
先用 `HasDotDotAhead ()`（纯 token 扫描）挡一道，免得 1000 层括号那种嵌套把试读
一层套一层地放大（tests/152 压着这条）。求值见 `Interpreter.StepRange`，
值在 `Runtime/Values/RangeVal.cs`。

**端点收任何数值**（int / bigint / float / fraction，混着写也行），而**元素是"区间里的整数"**：
`[1.5..3.5]` 里是 2、3，`[0.1..0.9]` 一个是空的（这一个读法让"任意数值端点"和"能枚举"
同时成立）。所以 `Start ()` / `End ()`（写出来那两个数，可能是小数）和 `First ()` / `Last ()`
（区间里真有的头一个/末一个整数）是两回事；"里头有没有"和"落不落在这段里"也是两条 ——
`Contains` 按**元素**算（`[1..10].Contains 2.5` 是 false）、`Covers` 按**端点**算
（`[1..10].Covers 2.5` 是 true；Ruby 的 `include?` / `cover?` 也是这么分的）。
界一律用 bigint 算（端点可能是 bigint、个数也可能超出 int —— `Count ()` 装不下就给 bigint），
比较走 `TryAsDouble`（和 `<` 一个口径），NaN 当空区间、±∞ 报错。

它同时也是一个 `IEnumerable`（impl 在 `lib/iterator.rav`）：枚举器是**生成器的光标**，
所以 `[1..1000000000].Take 3` 秒回、不会先铺一张表；`Count` / `Contains` / `ToList` /
`First` / `Last` 走引擎那几条（类链先命中，前四条 O(1)）。`==` 是**按内容**比（record 的
`Equals`）。

**方向由两端自己说了算**：起点在终点**后面**就**倒着数**（`[5..1]` → 5 4 3 2 1，
`(5..1)` → 4 3 2 —— 开闭跟着方向走，`First ()` 是**起点**那头、`Last ()` 是终点那头；
`Slice` 也跟着倒着切）。于是"空"只剩一种情形：**区间里一个整数都没有**
（`(3..3)`、`[0.1..0.9]`、NaN 那几种）。

**它替换掉的"两个数字当范围"**：`String.Slice from to`（引擎那条 `Slice` 现在**只收区间**
—— `s.Slice [1..3)`，开闭由括号说了算；`SliceBounds` 把区间折成切片要的半开 `[from, to)`）；
`Math.Clamp x lo hi`（现在只收区间：`Clamp x [0..1]`）；库里的 `Random.Int lo hi` 直接**改成收区间**（`r.Int [1..6]`，
它才一天大，没有兼容包袱）；序列那一族新添了 `IEnumerable.Slice range`（惰性的
`Skip` + `Take`，从前根本没有这个 API —— 两个数字当区间写在参数里太丑）。
`Math.Clamp` 也收区间（`Clamp x [0..1]`，端点收任何数值、**保型**照旧）—— 它的柯里化是
**手写**的：第二个实参得当场分流，不然 `Clamp 5 [0..3]` 会先变成一个"还差一个参数"的函数。

**正则**那六条也是原语（`lib/regex.rav` 把它们包成 `Regex` 模块）：模式、文本、选项串
（`i` 不分大小写 / `m` 多行 / `s` 让 `.` 吃换行 / `x` 忽略模式里的空白）三样进去，数据出来
（匹配交回普通 dict，由库拼成 `RegexMatch`）。用的是 **.NET 那组静态重载**
（`Regex.IsMatch (input, pattern, options, timeout)`）—— 它们带 .NET 内部那张模式缓存，
同一个模式不必反复编译。**两条兜底在引擎这层**：① 超时（默认没有，会挂死进程 ——
所以给了 `RegexTimeout` = 2 秒，超了报普通的 Ravel 错误）；② 模式写错抛的
`ArgumentException` 接住换成说人话的错误（和 `Fs` 那条老规矩一样，不让 C# 异常漏出去）。

（`if`/`while`/`foreach`/`Cached`/`Some`/`None` 不在 System 模块里——它们在
`lib/predefined.rav` 用 Ravel 写。那里还定义了这几个类型：
`Option`(可能没有值的包)、`Expected`(那次调用有没有出错)。）

**值**: True False Default NaN Inf（特殊浮点值；`-Inf` 用一元 `-`）

## Math 模块

成员是 C# 造的（`Ravel.Extensions/MathNative.cs`），但**不像 System 那样启动就有** ——
`Math` 要**显式引用**（`using "math.rav"`）之后才是一个名字。

它走的是**和 `Hash` / `Http` 那批一样的路**:挂在官方扩展里(`[RavelModule("Math")]`),
`lib/math.rav` 第一行 `using "native.rav"` 把成员摆好,第二行那句 `ravel "Math"` 只是
**切进**那个已经建好的模块(那四个 Ravel 函数 `square` / `cube` / `deg` / `rad` 就是这时候补上去的)。
—— 从前这儿是**另一条路**:`EnterModule` 见模块第一次被 `ravel` 到时,查一张
`CSharpModules` 表、调 `MathModule.Fill` 往里填。Math 是那条路最后一个用户,
搬走之后表和查表一起删了,`EnterModule` 回到"只管建模块、切作用域"。

**常量**: pi e tau

**三角/双曲**: sin cos tan asin acos atan atan2
              sinh cosh tanh asinh acosh atanh

**幂/对数**: sqrt cbrt pow exp log log2 log10 logBase hypot

**取整**: floor ceil trunc round roundTo（`round` 是四舍五入，不是银行家舍入）

**保型的那几个**: abs sign min max clamp minMagnitude maxMagnitude —— 交回**原始实参**
（`abs -5` 还是 int、`max 3 bigint …` 还是 bigint），其余一律给 float。

参数收**任何数值**（int/float/bigint/fraction），内部按 double 算 —— 和 `<` 那批运算符
同一个口径。`lib/math.rav` 只在上面补 Ravel 说得清楚的几个（square/cube/deg/rad）。

## 文件系统（`lib/io.rav`，要显式 `using "io.rav"`）

分三层，**"文件"先是个形状**，磁盘只是其中一个实现：

1. **引擎侧原语**（`System`）：`ReadText` / `WriteText` / `AppendText` / `DeletePath` /
   `CreateDir` / `ListDir`(名字→是不是目录) / `PathSize` / `PathTime` / `CopyPath` / `MovePath` /
   `CurrentDir` / `ChDir` / `FileExists` / `DirExists` / `PathJoin` / `PathDir` / `PathBase` /
   `PathExt` / `PathClean` / `SplitLines`。**只做 syscall、不做判断**：失败报 Ravel 错误（中文、
   带路径，路径按用户写的那串打、不转绝对路径），"要不要先问一句"交给 `FileExists` / `DirExists`
   探针（它们不报错）；外面再套一层兜底，**不让 C# 异常漏到顶层**（这批原语一条道理）。
   路径基准 = 进程当前目录；**不做沙箱** —— 和 `using` 找模块一个待遇。
2. **接口**（`IEntry` / `IFile` / `IDir`，**全局名**，和 `IEnumerable` / `IMonad` 一个待遇）：
   `Exists` / `IsDir` / `Delete` / `Name`；`IFile` 加 `Read` / `Write` / `Append` / `Size`；
   `IDir` 加 `List`（`名字 → IEntry`，实现自己排序）。`IFile ::= interface IEntry` ——
   **实现了 IFile 也就实现了 IEntry**（照 C#；登记只写最具体那条）。磁盘特有的（路径、修改时间、
   改名、复制）**不进接口**。实现有两条路：① 类自己就有那几条成员 + `impl (IFile 那个类 { () })`
   登记；② `impl (IFile 某类 { by Read = property … })` 现装。只读实现（zip 条目那种）让
   `Write` / `Delete` 抛一句人话即可（这轮不拆 `IReadOnlyFile`）。
3. **磁盘实现 + 通用件**（模块 `Io`）：`Io.File "a.txt"` / `Io.Dir "sub"` 造条目
   （`Child` 对不存在的名字**按文件算** —— 写新文件是常事；建目录走 `Mkdir`）；
   `List` / `Files` / `Dirs` 按名字排序；`CopyTo` / `MoveTo` / `Rename` / `LastWrite` 是磁盘特有的。
   `Io.Lines (f: IFile)`（**生成器**：边要边给，读到第几行就收工都行）/ `Io.CopyTo (from to)` /
   `Io.EachDir (d f)`（递归走一遍，每见一个条目叫一次
   `f (路径, 条目)`）**对着接口写**，任何实现都吃 —— 以后加内存文件 / zip / 远程文件就是照这个缝插。

**控制台也是文件**：`Terminal.Stdout` / `Terminal.Stderr` / `Terminal.Stdin` 三个单例值实现 `IFile`
（`Terminal` 模块里 `ConsoleOut` / `ConsoleIn` 两个类各登记一条 —— **住在 `Terminal` 而不是 `Io`**：
"控制台"整个归 `Terminal`，`io.rav` 只管文件系统，读的人不必在两处犹豫；`IFile` 那个接口仍是 `io.rav` 的）。
另一头 `Terminal.Write` / `Line` / `ToErr` 是"先渲染标记再写"，和这三个的"裸去路"分得清。于是对着接口写的代码直接能用 ——
`Io.CopyTo f Terminal.Stdout` 把文件倒进终端、`Io.CopyTo Terminal.Stdin f` 把输入倒进文件。取舍写明白：
只写的那两个 `Read` 报错、只读的那个 `Write` 报错、控制台没有 `Size` 也删不掉（报错说人话）。
`Stdin.Read ()` 是**读到 EOF**（终端上 Ctrl+Z/D 收），读一行用 `ReadLine ()`（就是 `input`）；
`print` / `input` 照旧是日常那两个，这里是"抽象的视角"。

**内存里的**：`Io.MemFile "名字" "内容"` / `Io.MemDir "名字"`（`Add` 用条目自己的 `Name ()` 当键、
`List ()` 按名字排）。内容就是一个字符串（`Exists` 恒真、`Delete` 是清空 —— 它不在谁里面，
"删掉"只能这么算）；`MemDir` 另给 `Files ()` / `Dirs ()`，和磁盘那个 `Io.Dir` 对齐。

**一棵 JSON 树也是文件系统**：`Io.JsonNode v` / `Io.JsonFile "conf.json"`。模型一句话 ——
**每个节点是一个文件，内容是它的 JSON 文本**：`Read ()` 给紧凑 JSON（字符串叶子也带引号），
`Write (t)` 把 `t` **当 JSON 解析**了换掉那棵子树（所以 `Write (Read ())` 正好是原样）。
对象和数组都算目录（数组的名字是 `"0"` `"1"`），`Child name` / `List ()` / `Delete ()` 齐活，
于是 `Io.EachDir` 能把一棵配置树走一遍、`Io.CopyTo 树 (Io.File "conf.json")` 就落盘。
它改的是**那棵 Json 值本身**（窗口，不是副本）；想要原生值那是 `Extract ()` 的事。
（`io.rav` 能直接引用 `Json` —— 那是 predefined 的全局名，不用 `using`。）

**一个 URL 也是文件**：`Http.Url "https://…"`（见「网络」一节）照同一条缝插进来 ——
它自己就有 `IFile` 那几条成员，再 `impl (IFile Url { () })` 登记一下。于是
`Io.CopyTo (Http.Url …) (Io.File "a.txt")` 抓下来存着、`Io.Lines (Http.Url …)` 一行行读远程日志、
`Io.CopyTo (Http.Url …) Terminal.Stdout` 直接倒进终端 —— **对着接口写的那几件一个都不用改**。
`Write` / `Delete` 发的是 PUT / DELETE，`Append` 报错（HTTP 没有追写这回事），
`Exists` / `Size` 走 HEAD（不下载正文）。
（`http.rav` 因此 `using "io.rav"`；依赖方向是 网络 → 文件，不是反过来。）

**和 `IoMonad` 搭台**：`Io.ReadAction` / `WriteAction` / `AppendAction` / `WriteLineAction` /
`EachLineAction` 把文件操作包成 `Action`（"先拼好、之后 `Perform ()`"）。所以 `io.rav` 开头
`using "iomonad.rav"` —— 反过来不行（`iomonad.rav` 由 predefined 加载,那时 `IFile` 还不存在,
而参数注解建 lambda 时就要值）。

链式调用用 `|>`（或括号）：`x.f () |> .g ()` ≡ `(x.f ()).g ()` —— `.成员` 比并列的调用绑得紧。

两个坑（写在 `lib/io.rav` 里）：① 类体里给方法起名 `File` / `Dir` 会把**类名遮住**，
`File (…)` 变成调自己（实测无限递归）；② 实参位置上的 `raw.Get n` 会被读成 `((f …) raw.Get) n`。

测试 —— 229（磁盘：读写/列目录/复制移动改名/报错文案/换目录、临时目录跑完删干净）、
230（抽象那一层：测试里现写一个内存实现 + `Io.CopyTo` 两边跑 + `Io.EachDir` 递归）。

## 全局变量

仅 1 个：`System`（模块自身）。

其他所有变量通过 `predefined.rav` 别名定义（`int := System.Integer` 等）。

## 类型与对象：类就是 ClassVal

**没有 `RuntimeType`、没有 `TypeVal`、也没有单独的 Class 类型。** 值阶层（`Runtime/Values/`）：

```
RuntimeValue                          MemberScope（虚）→ 伪 / 真 Scope
├── IntVal FloatVal BigIntVal FractionVal BigFractionVal
│   StringVal CharVal VoidVal DefaultVal            ← 原子值：无字段，MemberScope = 伪 Scope
│   RangeVal                                        ← 区间(`[1..3]` / `(3..5)` …)：不可变、
│                                                     按值比（record 的 Equals + Object 的 `==`）
│                                                    （**没有 ExceptionVal**：`Exception`
│                                                     是个普通类，实例就是 ObjectVal）
└── ObjectVal                         Scope 字段 = 真实成员表（取成员的落点）
    ├── ListVal SetVal DictVal ModuleVal PropertyVal ScopeVal
    └── FunctionVal                   + CaptureScope（捕获作用域）
        ├── ClassVal                  ← **类对象就是它**
        └── LambdaVal BlockVal BoolVal NativeClosure BuiltinMethodVal …
```

**一个类是 `ClassVal`，而 `ClassVal : FunctionVal`** —— 于是"能不能调"和"是不是类"
都是**类型关系**，不再是"某个成员名在不在"：

- **能不能调用** ⟺ `is FunctionVal`。类对象自己就是那个可调用的东西：`CallInto` 见
  `case ClassVal` 就推 ClassInit 帧。没有 `call` 成员、也没有中间的 `BoundCall` 转发
  （那套连同用户自定义 `call` 的能力一起删掉了）。
- **`IsClass`**（我本身是不是一个类）⟺ **沿 `parent` 往上能查到 `type`**。
  注意它和"实例化造出来的是什么"走**两条不同的链**：`IsClass` 走**元类**链，
  而 `StepClassInit` 造 `ClassVal` 还是 `ObjectVal` 看的是**被实例化的那个类**的
  **parent** 链（`type { body }` 里 `type <: type` 自反 → 造出来的就是类对象）。

类对象的成员（类性由 Scope 里的普通成员表达）：

| 成员 | 含义 |
|---|---|
| `parent` | 父类对象（原型链上游；自引用 = 链到头） |
| `block` | 类体——实例化时重跑的配方 |
| `name` | 类名（`C := class {...}` 的**成员值是空串**，只有 `::=` 会命名；但 `C.name` 读出来是显示名，空名字退化成 `class` —— 读写不对称，见 BoxedValue 的 `name` 伪成员） |

`RuntimeValue.Type` 返回**元类**（创建它的那个类对象），`typeof X` 就是取它。
`type` 的元类是它自己（自指，链的起点）。

### 原子值与伪 Scope：取成员只有一条路

`Value.MemberScope.LookupField(name)` —— 由 `MemberView` 一次走完两段：

```
① 值自己那层（对象是它的实例作用域，扁平）
② 沿**类对象**的 parent 链兜底（方法住在类那层，`Fields` 就定义在 `object` 上）
```

- **非原子值**（对象、容器、函数、模块…）有**自己真实的成员表**（`ObjectVal.Scope`），
  两段都有。表是**每个实例一张**的空表：`l.tag := 1` 挂得上，又不会像"共用类型那张表"
  那样一改就改掉整个类型。
- **原子值**（`IntVal`/`StringVal`/… 这些按值比较的）**没有自己那层** —— 它们是 record，
  多一个字段就毁掉值相等。它们取到的是**伪 Scope**：只有第 ② 段，而那段是**算出来的视图**，
  不落地。所以 `IntVal(1) == IntVal(1)` 照旧成立，集合去重也照常。

第 ② 段**只认方法名**、且只收 `FunctionVal`：类对象的成员表里还躺着
`parent`/`block`/`name`/`this`/`init`，那些是**类自己的数据**，不是"这个值的成员"。
不挡的话 `(5).parent` 会从报错变成返回 `ValueType`。

⚠️ **运算符是个例外：它问的是"类型"那张表**（`值.Type.MemberScope`）。
运算符的语义是「这个**类型**怎么把两个操作数合起来」，和"这个值有哪些成员"不是同一个问题 ——
混起来的话 `typeof 1 == int` 会撞上 `Integer` 那层的数值 `==`（把类对象当数字算）。

### 四条统一规则

```
① 查成员   X.member   →  X.MemberScope 一次查找（自己那层 → 沿类对象 parent 链兜底）
② 判类型   A <: B     →  沿 A 的 parent 链能否走到 B（Every 全局特判）
③ 实例化   X args     →  新建 scope（this = 新 ClassVal / ObjectVal，ClassType = X）
                        沿 X 的 parent 链逐层跑各层 block
                        在 scope 里 LookupField("init") 找构造器
                        调它，并把【它返回什么就是什么】作为结果
④ 建类     X := class { body }        ← 父类默认 object
           X := class Parent { body }  ← 父类 Parent(必须已经存在)
           ≡ 把 class 换成 type 完全等价(两者是同一个值)
```

### 模块的顶层就是它的成员表

模块（`ravel "M"`）的**作用域就是成员表** —— 一个对象两个名字，读成员走的 `MemberView`
就是"本层 + 类链"。所以顶层那几句 `:=` **不许用类链上本来就有的名字**：

```ravel
ravel "M"
References := () => { … }     # ✗ `References` 是 `Ravel` 的成员 —— 蒙掉它不等于新开一个变量
References =  () => { … }     # ✓ 覆盖走 `=`
```

这条和类体那边是**同一条规矩**（各层类体**平铺进同一个实例作用域**，继承来的 `init` 就摆在本层，
再 `:=` 自然撞上）；模块的成员是**动态往下查**的（`MemberView`），平铺不了，所以在
`Scope.Define` 里补了一道。`=` 那一侧落在**本层**（开一格盖住继承来的），不去改类对象上那一格
—— 那是**所有模块共用的同一份**。

**只管最外层**：`try` / `eval` / 函数体里跑在自己的子作用域里，在那儿 `:=` 是"内层遮蔽外层"，
本来就允许（和"内层作用域里 `true := 1` 遮蔽外层那个只读的"一个道理）。

库里有两条撞上了，因此改名：`Io.Copy` → `Io.CopyTo`（撞 `Object.Copy`）、
`Crypto.Key` → `Crypto.DeriveKey`（撞 `Object.Key` 那个键协议）。

### 内置类也有类体

内置类的成员是 C# 造好的值，所以它们的**预设类体**用 `LiteralExpr`（AST 节点：直接求值成
一个 C# 值）拼出来，里面只定义 `init`：

| 类对象 | 预设类体 |
|---|---|
| `object` | `init := () => { this; }`（**默认构造器**，见下）|
| `type` | `init := (parent: type body) => 装 parent/block 并返回 this` \| `(body) => parent 默认 object` |
| `Integer` / `String` / … | `init := <CastToXxx>` |
| `List` / `Set` / `Dict` | `init := MakeDefaultCaster`（只收 `default`） |
| 用户类 | 用户写的 block |

`object` 那份跑在**最前**（`CollectBodies` 顶祖先优先），所以任何类只要不写 `init` 就继承它 ——
"什么都不做、把对象交出来"该是默认，和 `function default` 给空函数是一回事。
它是 `NativeClosure`（要交出的 `this` 只在调用点作用域里）。
**`StepClassInit` 调 init 前必须 `cf.Scope = inst.Scope`**（改在帧自己身上，不能只 `with` 一份）：
`CallInto` 里取调用点作用域的两条路（NativeClosure / 类运算符）走的是 `_top.Scope`。

于是**没有"转换器"这个特设概念**：`int 42` 得 42，走的就是「跑类体 → 找 init → 调它 →
交出返回值」，和用户类一模一样。`CallClassInto` 也只有一条路。

### 构造交出 init 的返回值

`init` 约定以 `this` 收尾，所以普通类拿到的还是实例；而**元类的 init 可以建出一个类再交出来**。
init 若还返回函数（多参构造器只喂了一部分）就交 `PartialCtor` 半成品，和普通函数一样柯里化。

⚠️ **忘写 `this` 会静默拿到 `()`**（块的值就是最后一条语句的值，赋值语句的值是 `()`）。
这是这条规则的代价。

### 元类

**一个父类是 `type` 的类就是元类**：它继承了「建类」那层的 init，所以它建出来的东西是**类**。

```ravel
MyMeta ::= class type {
    private parentInit := init;          # 覆盖之前先把继承来的那层存下来
    init = (parent: type body: function) => { parentInit parent body }
         | (body: function) => { parentInit object body }
}
MyClass ::= MyMeta { init := () => { 0; this; }; x: int = 42; }
```

- **不需要 `base`**：super 调用退化成「覆盖之前先把继承来的 `init` 取出来」。
- 两分支让 `MyMeta { ... }`（不带父类）和 `MyMeta parent { ... }` 都能用。
- **`init` 在类体里读到的是继承来的那个**：靠"预设类体先跑、把 `init` 落进同一个实例 scope"。
  而 `init = ...` 改的是**这个实例 scope 的副本**（预设类体每次实例化都重跑），`type` 本身不受影响。
- **引擎找构造器用 `LookupField`（一层、不走原型链）**：各层类体都跑进同一个实例 scope，
  谁写了 `init` 谁就在那一格里，所以拿到的是**最具体**的那份（没写就是继承来的）。
  它不会掉进 `type` 那层的建类逻辑 —— 那层只在 `type` 及其子类被实例化时才收集。
- **`object` 的类体里有一份默认构造器**（`init := () => { this; }`）：每个类的祖先链都到它，
  所以谁都没写就落回它，`C ()` 不再报「没有构造器」。它跑在最前（祖先优先），
  内建那些更具体的（`int` 的转换器、`List` 的 default 构造器）照样覆盖它。
  没有自己类体的类型（Void/Every/Any/Scope/Property/…）仍然不能当构造器调 ——
  那道护栏在 `CallClassInto`，看的是被实例化的那个类**自己**有没有类体。
- 用例：`tests/98`（基本）、`118`（挂钩建类过程）、`121`（拿到类再交出去）、
  `122`（typeof 链）、`193`（用户手写的那份）、`211`（默认构造器）。

### 其余

- **继承是平铺的**：实例化时沿 parent 链从顶祖先到自身依次跑每层类体，所有层的字段落在
  **同一个 instance scope** 里，所以字段查找只需一层（`Scope.LookupField` 不走链、不走词法链）。
  实例 scope 的父是**类定义处的词法作用域**（类体里能引用外部变量），不是父类的实例 scope。
  而**每一层**类体跑在自己的 `BodyScope` 上（`Runtime/BodyScope.cs`：词法父 = **那一层**写在哪，
  `Define` / `DefineOrReplace` 转发回实例表，`LookupHere` 先问实例表本层）—— 所以各层的自由名字
  **各按各的写法处**解析，定义仍旧平铺在同一个实例表里。从前只有一个父（最具体那个类的写法处），
  父类写在别的作用域时它的类体就看不见自己那儿的名字（`tests/242`）。
- **类体可以**拼**出来**：`type` 那层收的"代码块"允许是 `body.Append { … }`
  (`Prepend` 也行)——`Install` 把它摊平成一串块(`BuiltinClasses.BodyPieces`)，第一块是主类体、
  其余记进 `ObjectVal.BodyExtras`。实例化时**同属一层**的块按顺序接着跑：各自一步(`CollectBodies`
  的 `BodyStep`)，`Lexical` 都指着**主块**的写法处(拼上来的块自由名字也照那一处解析，
  和"每层各按各的写法处"一个道理)，定义照旧平铺进同一个实例表。类体里那句运算符定义
  (`== := …`)是**扫语句**装进实例表的(`Install`)，所以拼上来的块也一起扫。
  元类靠这个注入：`dataclass` 就是 `parentInit parent (body.Append inject)`——
  拼上来的块跑在类体之后，于是"注入"和"自己写几条成员"没有任何区别。
- **`:=` 是定义不是覆盖**：平铺之后「同名」就是同一个变量，所以在同一个作用域里再 `:=` 一次
  会当场报错（`Scope.Define` 那句）—— 包括**子类重声明父类已声明的字段**。要改值、要换掉
  继承来的那条，写 `=`。唯一的放行是「**同一条定义语句重跑**」：续延重入、同一个块被反复执行
  （`lib/predefined.rav` 的 `while` 就靠这条活着）—— 类的**层**不算「重跑」，那是不同的语句。
- **父类的 init 不会自动调用**：要换就写 `init = …`（覆盖，父类那条**不会被调用**），
  初始值更该写在字段声明上（`a: int = 1`）；父类 init 里写的赋值不生效。
  子类写 `init := …` 是重复定义 → 报错（`Object` 那条默认构造早就占了这个名字）。
- 用户类会被登记进 `AllTypes`，`Subtypes ()` 才反射得到它们。`AllTypes` 是静态表，
  而一个进程里会跑多个 Interpreter，所以每个 Interpreter 构造时调 `ResetUserTypes ()`
  清掉上一个留下的——否则上一个建过的类会出现在下一个的 `Subtypes ()` 里。
- **`parent` / `block` / `name` / `init` / `this` 是机制成员**。注意两个问题它们是两个答案：
  - **查找**时它们**不沿类链继承**（`ObjectVal.IsMethodName`）：不挡的话 `(5).parent` 会从报错
    变成返回 `ValueType`。
  - **`Fields ()`** 不按名字挡 —— 它列的就是"这个 Scope 里的成员",`init` / `parent` / `block`
    都在里面。唯一排掉的是 `this`：它不是成员，是**这个值自己**的别名。
  - **`parent` / `block` 是 `readonly`**（装它们的三个地方 —— `Install` / `Link` /
    `ObjectVal.ClassBody` —— 挂上去的）：它们是"这个类是什么"的定义，
    改它等于把类换一个（`C.parent = int` 之后 `C ()` 就去跑 `Integer` 的构造器了）。
    装类因此都是**写一次**（一个类的 `block` 就是**它那一层**用户写的类体，各层各存各的）；
    接口从前"先装上、再 `trait.ClassBody = …` 换一份（把父的声明抄进来）"是往同一格写第二次——
    那一层现在**整个删了**：继承不再靠抄，靠跑（见下面接口那节的 `StepImplMake`）。
    `name` **故意可写**：那是它本来的用法（`F.name = "x"`、`::=` 命名）。
  - **`init` 是 `protected`**（`PresetCtor` 给它挂的）：**类体系里照旧** —— 子类那句
    `init = …` 是普通赋值、不走成员门禁；外面 `obj.init` / `obj.init = …` 读不到也写不了
    （「变量/字段 'init' 是受保护的」）。构造器本来也不该是从外面拨的开关。
  `print obj` 是另一回事：它是**数据快照**，方法（含 `init`）不出现在里面（显示选择，不是成员定义）。
  两条容易漏的：**`init`** —— 类体就跑在类对象自己的实例作用域里，所以类对象的 Scope 里
  **装着它自己的构造器**；**`this`** —— 类对象就是 `type` 的实例，而实例化时那句
  `instanceScope.Define("this", …)` 写进去的**正是这个类对象的成员表**。
- **一个类对象有「两张表」**（`ClassVal.InstanceTable`）—— 分工是**结构**的，不靠标记、不靠过滤：
  - **类自己那张**（`ClassVal.Scope`，就是基类那个 `ObjectVal.Scope`）：类对象是元类 `type` 的
    实例，建类时平铺进来的 `name` / `parent` / `block` / `init` / `==` / `!=`，用户事后挂上去的
    （`C.func := …`），以及**类那一侧**的 API（`Json.FromString`）。从**类对象**上读成员读的就是这张。
  - **给实例的那张**（`ClassVal.InstanceTable`）：内置方法（`Add` / `Map` / `Kind`…）、类运算符、
    序列方法、`GetImplements ()` 这类查询。**只有引擎往里写**（`BuiltinClasses.EngineMember`：
    `DefineMethod` / `DefineOp` / `DefineSeq` / `TraitQuery`；用户类体里那句 `+ := f` 也由
    `Install` 扫出来落这儿，它不吃 readonly），**只有实例读得到**
    （`MemberView.LookupInClassChain` 沿 `parent` 链读的就是它）。
  于是两件事都成了结构决定：**类上挂的东西实例看不到**（落①，实例读②）；**`list.Add 2` 从类那一侧
  也读不到**（`Add` 在②里，而类对象自己的查找——第 1 段用它的 `Scope`、第 2 段沿**元类**链——
  两条都不经过它），报的是干净的「类型 'Type' 没有方法 'Add'」。从前这两件事靠
  `forInstance` + `private` 两个标记去挡（外加 `CheckFieldAccess` 的开口子、`DefineOp` 那三处
  `isPrivate: false`、`Fields ()` 的一层过滤），而那两个标记的判据其实就是"住哪张表"——
  拆表之后五个补丁一起退了场，`forInstance` 修饰符本身也删了（写 `Parser.Statements.cs` 那句
  「已删除」）。`Fields ()` 和查找共用同一份判据（列的就是这一侧读得到的）：`list.Fields ()` 里
  没有 `Add` 那批、`[1 2].Fields ()` 照旧整串、`Json.FromString` 在类那一侧照旧列。
  用例：`tests/242`（`205` 钉"修饰符已删除"）、`tests/241`（`Json.Fields ()` 那一行）。
- **运算符和方法走同一条路**：都在 `MemberScope` 里找（值自己那层 → 沿类的 `parent` 链读实例表）、
  都按 `ISelfBinding` 绑接收者、都按 `is BuiltinMethodVal` 分"当场算 / 推帧"。于是：
  `Integer` 的 `+` 找 `1` 的实例表、`C1 == C2` 找 `Type` 的、`c1 == c2` 找 `C` 的
  ——「类型之间」和「实例之间」两个 `==` 是**不同表**上的两条，值类型的运算符也在这条路上。
  - 引擎挂的成员是 `self → 剩下`，要 `BindMethod` 绑接收者；**用户类体里写的那份是普通 lambda**
    （收的是右操作数、接收者靠捕获的作用域）—— `BindMethod` 会去读它的 `Body`，
    而 `LambdaVal.Body` 是"求值器漏了 case"的哨兵，所以那边必须 `is ISelfBinding` 才绑
    （`ClassOp` 帧里 `CallInto(cf, impl, arg)` 也是这么调的，两边一致）。
  - **"任何值都有"的那批写在 `object` 上**（`is` / `isnot` / `<:` / `:>` / `==` / `!=`）：
    往上一层层查，每个类都到 `object`，所以谁都摸得到 —— 自指的 `Every` / `Any` 也一样
    （`MemberView.ClassChain` 里那条"自指的不是 `object` 就接上 `object`"）。就这一条规则，
    **没有第二条链**：类对象是值、它的类是它的元类，从类对象往上查照样是"往上"。
    `==` / `!=` 从前挂在 `Type` 上（靠"类对象往上会经过 `Type`"蒙到它们），改走 `MemberScope`
    之后挂 `object` —— 否则普通实例、`property`、`()` 这些值往上都不经过 `Type`，`==` 会找不到。
- **`private` 回到一件事**：实例侧的门禁（"当前作用域在不在这个对象的类里"）。内置方法不带它
  ——它们在实例表里、谁都读得到；借去当"方向"的那套（`MemberView` 的 `classSide` / `ReadOwn`）
  随两张表一起删了。用户写在自己类里的 `private` 字段照旧只认"本对象内部"那条老规矩。
- **自绑定成员**（`ISelfBinding`：`BuiltinMethodVal`、`ClassOperatorFactory`）读出来要先
  绑接收者 —— 漏了的话 `C.Fields ()` 会把未绑定的内置方法当结果返回(`self` 是 `()`)。
  它和"同步快路径"标记（`BuiltinMethodVal`）**不是一回事**：类运算符工厂也要绑，
  但绑完是 `BoundClassOp`（推帧的标记），不能直接算 —— 合并会让 `a + 5` 交出标记而不是数。

## 插件（`using "x.dll"`）

`using` 除了 `.rav` 还能吃 **`.dll`**:一个编译好的扩展程序集,扫里面带特性的类和函数,
按特性上那个名字填进模块(见 `Runtime/Builtins/PluginApi.cs`)。

```csharp
[RavelModule("Structures")]                       // ← 模块名就在特性上
[RavelClass("Stack")]                             // 一个 Ravel 类(类型树上的节点)
internal static class StackClass
{
    [ClassCtor] public static RuntimeValue New(RuntimeValue arg) => …;
    [ClassMethod("Push")] public static RuntimeValue Push(RuntimeValue self, RuntimeValue v) => …;
    [RavelFn("Make")] public static RuntimeValue Make(RuntimeValue arg) => …;   // 模块里的函数
}
```

于是 `using "x.dll"` 之后:`Structures.Stack ()` / `Structures.Make 1`。

**目录和后缀都可以省**(`plugins/` 也在搜索目录里,见 `Runtime/ModuleSearchPath.cs`;
补后缀的规矩见 `Interpreter.FindModuleFiles`):

```ravel
using "Ravel.Structures"     # ≡ using "plugins/Ravel.Structures.dll"
using "structures.rav"       # 库里那半边,照旧
```

- **`[RavelModule("")]` 是特例**:进**全局作用域**(不建模块,直接叫名字)。
- 函数那几条的签名规矩和 `[Sys]` **同一套**(1~3 个 `RuntimeValue`,开头可以是
  `Interpreter`)—— 绑定只有一处(`ClassRegistry.Fn`)。
- 插件类也进 `AllTypes`(`BuiltinClasses.AddType` 顺手把 `_builtinCount` 往前推:
  那个数切的是"装进来的"和"用户现写的",插件属于前者,新建解释器时不该被清掉)。
- **幂等**:类对象是进程级的(和内置类一个待遇),同一个 dll 被不同的解释器各 `using`
  一次不会重复建类。

**为什么外置的那一套特性是 `public` 的**:引擎自己用的 `[Sys]` / `BuiltinClasses` 全是
`internal` —— 同一个程序集里随便用,外部 dll **看不见**。所以 `PluginApi.cs` 把
"写扩展需要的东西"重新公开一遍:三个特性 + `PluginKit`(比大小 / 取元素 / 键规范 /
拿类对象 / 说人话的错误 / 取值收束 / 路径与错误兜底 / 造原生闭包)—— 都不新造,
背后就是引擎里那几处,只是换个 `public` 的门。

**`Runtime/Values` 里那些类型引擎不认识**:插件的值类型(如 `StackVal`)不在主项目里,
`ElementsOf` 于是多了一条**按名字**的路 —— 谁有 `ToList ()` 谁就是容器,`Impl` 直接调
(插件登记的方法就是那个)。

**插件自带依赖**:托管那几件由 `LoadFrom` 从插件目录解出来;**本机库要自己兜**
(`PluginLoader.HookDependencies` 挂在 `ResolvingUnmanagedDll` 上,按当前 RID 探
`runtimes/<rid>/native/`)—— 默认探测看的是**主程序**的 `deps.json`,插件带的包不在里面。

**扫出来的函数按名字排**(先模块后名字)。反射给的先后取决定元数据顺序、没有保证,
而成员表是按登记先后列的(`Native.Fields ()` 打的就是它)—— 和 `SysRegistry` 同一条理由。

一个提醒:**插件的 Ravel 那一半得另写**(`lib/structures.rav`)—— dll 只能给"类和函数",
而 `foreach` / `Map` 那套是 Ravel 的 `IEnumerable` 接口给的,得有人在 Ravel 里 `impl` 一次。

### 官方扩展（`Ravel.Extensions` → `plugins/Ravel.Extensions.dll`）

**那几个库的本机半边住在里面,不在引擎里**:`Hash` / `Crypto` / `Net` / `Regex` / `Sqlite` /
`Random` / `Zip` / `Xml` / `Terminal`(对外叫 `Native.HashBytes` 那几条,同一个 `[RavelModule("Native")]`)。
留下来的那批照的是一条明着的尺子 —— **要么是语言本身要的,要么是进程边界**;
这六个两样都不沾,它们说的是"这台机器能干什么",而 `Hash.Sha256` 那个库才是"这门语言里
摘要是什么"。

- Ravel 那一半是 `lib/native.rav`(就一行 `using "Ravel.Extensions"`)——
  dll 的名字**只有那一处**;要用扩展的库都 `using "native.rav"`,不自己碰 dll。
- **模块名不止 `Native` 一个**:`Math` 整份也在这儿(`[RavelModule("Math")]`)——
  它没有"库面/本机面"之分,`lib/math.rav` 只往上补四个 Ravel 函数。
  这就是 `[RavelModule]` 上那个名字的用处:往**哪个**模块里装,由它说。
- `Microsoft.Data.Sqlite` 那个包也跟着搬了 —— 主项目从此不引用它。所以插件目录里除了
  本 dll,还躺着 `Microsoft.Data.Sqlite.dll` 和 `SQLitePCLRaw.*.dll`(见 `Ravel.csproj`
  的 `CopyPlugins`),本机的 `e_sqlite3.dll` 在 `plugins/runtimes/<rid>/native/`。
- `Xoshiro256` 那个算法也一起搬了过去(只有 `Random` 用它)。
- **常量走 `[RavelConst]`**(挂在 `static readonly` 字段上)——`Math.Pi` 是个**值**,
  不是"要写 `Math.Pi ()` 才拿到数"的函数;类型槽用值自己的类型(和 `System.True` 一个写法)。
- **装成员用 `DefineOrReplace` 不是 `Define`**:模块可能是用户早就自己建过的
  (先 `ravel "Math"` 建一个、之后才 `using "math.rav"`),装库这一刻该**以库为准**;
  `Define` 撞名就报「已经定义过」是 `:=` 的规矩,不是装库的规矩。(`readonly` 的那些照拦。)
- **合同钉在两处**:`tests/271` 是"引擎自带那张表",`tests/280` 是"扩展带来那张表"
  (`Native` 58 条 + `Math` 49 条),两张合起来正好是搬之前的全集。

## REPL 也是用 Ravel 写的（`lib/repl.rav`）

**REPL 本体就在这儿** —— `ravel` 不带参数进的就是它:`Cli/Program.cs` 的 `RunRepl` 只跑
`using "repl.rav"` + `Repl.Run ()` 两行,不多做别的(不去查模块、不摸它的内部 —— 那样等于
把入口又编回 C# 里)。多页缓冲、三维光标、按词上色的行渲染、按键编辑、主菜单
(**连菜单项的顺序都一样**)、启动那张 ASCII 大图、运行整页、读写文件、会话存盘,全在这一份。

它**从前是 C# 那版(`Cli/Repl/NeoInteractor.cs`)的一比一复刻,那一版后来删了** ——
两边行为一模一样,留着等于同一件事写两遍。当初照它写一遍是为了回答一个问题 ——
**这门语言自己够不够用**。答案:够,而且只多要了**六样原语**,全落在"进程边界"那一类
(`ReadKey` / `ScreenClear` / `CursorTo` / `CursorShow` / `FormatError`,外加
`CaptureStart` + `CaptureEnd` 这一对)。C# 那版连同专为它写的 `Lexer.ScanState` 一起删掉:
那条"括号合上了吗、末尾在不在字符串里"的规矩,现在只有 `lib/repl.rav` 一份。

- **高亮的扫描器是库自己写的**(`Segments`):高亮只要"编辑器那种粗细"(字符串 / 数字 /
  词 / 别的),不必是完整的词法 —— 而"哪些算一个词"本来就该由库说。
- `Balanced`(这句写完了吗)的规矩是从引擎那条 `ScanState` 抄来的(它已删):深度只做加减
  不校验(多一个右括号让解析器去报),`'a'` 里的括号也算进去。当初为什么不能让 REPL
  自己发明规则:C# 那版从前抄的那份不认 `\` 转义,`"a\"b"` 就把字符串状态判反、
  后面整行的括号跟着数错。
- **抓输出靠那对新原语**(`CaptureStart` / `CaptureEnd`)—— 就是 C# 那两句
  `Console.SetOut` 的原语化,只动 stdout 不动 stderr。「显示结果」那个开关于是和 C# 一样:
  **关掉照样求值**,只是输出和 `==>` 都不显示。为它加原语是划算的 ——
  "把这段代码说的话收起来"是库自己写不出来的能力,而 C# 那版天生就有。
- **菜单里那条「教程」是后加的**,和 C# 那版无关:生成静态站 + 用默认浏览器打开。
  生成走 `lib/docsite.rav`(模块 `DocSite`),所以 REPL 一加载就把它也拉进来了 ——
  引用图上看得见(`tests/module/292` 那份列表里多了个 `<module DocSite>`,菜单那串也多一项,
  `tests/lib/286` 钉着)。**它不起服务器**:那个站是纯静态的,`file://` 打开和服务器看
  一模一样;而起服务器要阻塞到 `Stop ()`,菜单就再也回不来了。打开那一步是 `System.Open`
  —— 交给系统去开,库里不必判平台(见「System 模块」那节)。
- **看不见终端就不当编辑器**:管道里把喂进来的整段当程序跑完就走 ——
  `echo 'print 1 + 1' | dotnet out/ravel.dll` 给 `2`(C# 那版没有这一路:接管道时它照样
  想当编辑器,画出来的东西没人看,喂进去的东西也没人吃)。
- **`Terminal.Panel` / `Terminal.Menu` / `AskDefault` / `ConfirmDefault`** 是为这一版新加的
  扩展面:方框和"带搜索的主菜单"交给 Spectre 那台现成的控件(和 C# 用的是**同一个**),
  两边才长得一模一样 —— 自己拼那圈边框得先算每行的**纯文本**宽度,不划算。

## 数据结构（插件 `Ravel.Structures`,见「插件」一节）

栈 / 队列 / 双端队列 / 堆 / 有序字典 / 有序集合 **+ 图那一族**(`Graph` / `Digraph` / `Weighted`)
—— **一个独立的项目**,编成 `plugins/Ravel.Structures.dll`,`using "structures.rav"` 装进来
(那个文件顺手登记 `IEnumerable` / `IDict`)。用的时候名字在 `Structures` 模块下:
`Structures.Stack ()`。

- 数据在**值**身上(`Ravel.Structures/*Val.cs`),类只放"有哪些成员"
  (`*Class.cs`,挂 `[RavelModule("Structures")]` + `[RavelClass("…")]`)。
- 共用小工具 `Structure.cs` 转发到 `PluginKit` —— 播种走 `foreach` 那个口径、
  键走 `dict` 那个规矩、比大小走 Ravel 的 `<`(于是这一族和语言里别的东西同一套脾气)。
- 它是**样板**:再加一族库,照它的样子开一个项目就行(构建那两步见 `Ravel.csproj`
  的 `CopyPlugins` 目标)。

**图**:`Graph`(无向无权)/ `Digraph`(有向无权)/ `Weighted`(带权,`{"directed": true}`
定方向)三个类只差**构造那一下**,方法只写一遍 —— 挂在基类 `GraphMethods` 上、三个壳继承它
(`InstallClass` 扫方法时带着 `FlattenHierarchy`,不然反射不返回基类的 static,继承来的
`[ClassMethod]` 一个都装不上)。顶点是**值类型**(数 / 字符串,和 `dict` 一个规矩),
权重收数值、**负数在 `AddEdge` 就挡下**(最短路走 Dijkstra,负权它不管)。
算法:BFS / DFS / 拓扑排序(Kahn)/ 连通分量(有向按**弱**连通)/ 最短路(无权 BFS、带权 Dijkstra)。
用例见 `tests/295`。

## 控制流

`if`/`while`/`foreach`（还有 `match`）是**库函数**（`lib/predefined.rav` / `lib/match.rav`），不是 C# 内建。它们靠两个机制写出来：

- **`Bool <: Function`**：`true {a} {b}` 执行 a 并返回其结果，`false {a} {b}` 执行 b。
  于是 `if {c} {t} {e}` ≡ `c {t} {e}`，`if` 只是 `(c t e) => { (c ()) t e; }`。
  实现上不需要新的 `ControlKind`：`CallInto` 里给 `BoolVal` 加一个分支，收到第一个块返回 `PartialBool` 半成品，收到第二个块就执行选中的那个。
- **callcc 当跳转**：续延被调用时丢弃当前帧链、从捕获点继续，所以可以拿它做循环：

  ```ravel
  while := (c: function body: function) => {
      again := (x: int) => { x; }
      callcc (k: function) => { again = k; }     # 独立语句:恢复时只是「这条语句完成」,不重跑它
      cond := c ()                               # 必须在恢复点之后,否则 again 0 跳回来时不重求
      assert (typeof cond == bool) ("while 的条件必须是 bool，得到 " + string (typeof cond))
      if { cond; } { body (); again 0; } { 0; }  # again 0 跳回 callcc 之后
  }
  ```

  这样帧链不随迭代增长（100 万轮实测跑得完）。**写成 `again := callcc (k) => { k; }` 不行**：恢复时会重跑那条赋值、`again` 被覆盖成 `0`，循环建立不起来。

`with`/`using`/`eval` 仍是 C# 内建——`with` 需要「换掉接下来这段代码的成员表」(不拷：见 `StepWith`)，`using`/`eval` 需要文件 IO 与词法/语法分析，Ravel 层做不到。

**控制状态跟着续延走,而"什么时候拍、什么时候还原"是库的策略**：帧链之外还有两样状态 ——
`Ex.HandlerStack` 与模块加载栈 `_loading`。它们都是**副作用式**的、不在帧链里:帧链一丢,
"跑完收尾去摘 handler / 退 `_loading`"那一步就再也不会执行。逃出 `try` 体会留下**僵尸 handler**
(之后没人接的错误被它接住,而它的续延停在早被丢弃的链上;和 tests/155 修过的正常收尾泄漏同类),
逃出**模块体**会让那个模块永远"正在加载"(之后 `using` 被误报「检测到循环引用」),而且它已记进
`_loaded`,于是 `using` 变成**静默空操作** —— 所以被中断的那个加载还要从 `_loaded` 里摘掉。
反过来也顺:调一个在 `try` 体里捕获的续延,那个 try 的 handler 回来,重进的那段照样受保护。
**普通数据不回滚**(累加器、字段原地改):恢复 = 接着从中断处往下算。

分层:**引擎只管它自己那两样** —— `_loading` / `_loaded`(原语 `System.LoadingState ()` 拍、
`System.RestoreLoading snap` 还原,并在还原时把"被甩掉的那些加载"从 `_loaded` 里摘掉;
快照只收 `_loading`,**不拷 `_loaded`** —— 被中断的加载按"此刻还在 `_loading` 里、快照里没有"算出来,
否则每次 `callcc` 都要拷一遍已加载表)。**`Ex.HandlerStack` 不归引擎管**:它是库的状态、
就在 Ravel 里那个 list 上,库自己拍自己还原 —— 引擎**任何地方**都不认识它,连报错冒泡那条路
也只是一句"把注册的钩子调起来"(见下)。
**策略在库里**:`predefined.rav` 的 `callcc` 是**库函数**(包装 `System.CallCC`,写在 `Ex` 那一段之后):
捕获时拍两份快照(自己的 handler 栈 + 引擎的模块加载状态),交给用户的续延被调时先还原这两份、再跳。
于是控制流整套(`while` / `if` / `try` / `callcc`)都住在库里,引擎不知道 `try` 是什么。
代价三条:**① 直接用 `System.CallCC` 就没这层保护**(库的 `callcc` 才是入口);
**② 还原那两句必须是一条内置调用** —— 别在"续延被调时要跑的代码"里用 `while` / `foreach` / `try`
(它们自己都要经过续延,会互相踩,实测死循环);**③ 每次续延跳转都要走一遍还原**(循环里就是每次迭代
一次),实测 20 万轮 `while` 偏慢几个百分点(噪声内)。
测试 223(try)、224(循环/枚举/多发射/累加器)、225(eval/with/接口槽的 `instance`)、226(模块)。

**异常处理整套也在库里**。引擎只有一个"错误钩子":冒泡上来一个 `RuntimeException` 时,它把
`System.SetErrorHook` 注册的那个函数调起来(库启动时注册的是 predefined.rav 的 `onError`),
剩下的事库自己定 —— 有人接就弹掉 `Ex.HandlerStack` 栈顶那个 handler 再调它(`try` 的 handler 会
escape 回它那个 callcc),没人接就 `System.Unhandled e` 把控制交回引擎去报告。
交给钩子的那个值是 `BuiltinClasses.NewException(message, kind)` 现造的 **Ravel 对象**:`typeof e` 就是
那一族(`TypeError` / `NameError` / …),`e.Message` 是原话,所以 `try { … } (e: TypeError) => …` 接得准。(引擎**原样**抛出
原来那个异常,位置与调用栈都不变,也不会混进库的帧)。非函数的栈顶(手写 `Ex.HandlerStack = [1]`)
照样当"没人接"。于是引擎对"handler 栈"这件事**零知识** —— 和它不知道 `while` / `try` 是什么一样。

**callcc 只有一套语义**：续延 = callcc 之后的剩余计算；调用它 = 丢弃当前帧链、把值当作 callcc 的返回值、从捕获点继续。因此 **callcc 之后的语句会被重新执行**——`x := 1 + callcc (k) => { saved = k; 0; }` 之后再 `saved 10`，会让 `x` 变成 11 并继续往下走。想「跳出去」就调用续延（旧的 escape 用例行为不变）。

## 运算符

类体里**直接用符号**定义，符号本身就是实例里的成员名：

```ravel
Vec := class {
    init := () => { 0; }
    x: int = 0
    + := (o: Vec) => { ... }    # 定义
    == := (o: Vec) => { ... }
}
```

- `+ := f` **定义**；`+ = f` **覆盖**从父类层继承来的那个（父类自己不受影响）。旧写法 `operator+ add := ...` 已废弃，会报语法错误。
- 可用符号见 `Syntax/Ast.cs` 的 `OperatorSymbols.All`（`+ - * / % ** == != < > <= >= & | ^ << >> <<< >>>`，**词形运算符** `is` / `isnot`，以及类型之间的 `<:` / `:>`），与 `BuiltinClasses` 注册的内置一致。一元 `!`、短路 `&&`/`||` 是求值器特判的，不能自定义。
  （加一个符号要动四处：`TokenType`、`Lexer.MultiCharOps`、`Parser.Statements.IsOperatorToken`（类体里 `** := f` 这么写要认得出），以及上面这张表。`**` 那次就是四处一起改的。）

### 乘方 `**`

**右结合，而且比一元负号紧** —— 它是**唯一**比一元紧的二元运算符（`-2 * 3` 仍然是 `(-2) * 3`）。
照着数学写法定的：写 `-2 ** 2` 的人要的是 -4 的多。

| 写法 | 读作 | 交回 |
|---|---|---|
| `2 ** 3 ** 2` | `2 ** (3 ** 2)` | `512` |
| `-2 ** 2` | `-(2 ** 2)` | `-4`（要 4 就写 `(-2) ** 2`）|
| `2 ** -1` | `2 ** (-1)` | `0.5` —— **float** |

- **左边是什么类型就在那个类型里算**：`int ** int` 还是 int（装不下照旧报「大数用 bigint」，
  和 `*` 一样，不悄悄换成 bigint）；掺了 float 就全程 double。
- **整数的负次幂交回 float** —— 整数装不下 `1/2`，而"报错让人自己去转"太不划算。
  **分数不受这条影响**：上下颠倒就行，`(fraction 1 2) ** -1` 是 `2/1`。
- **涨得太快的先拦一道**：`int` 那条在 `|底| >= 2 && 指数 > 31` 时直接报，不去算；
  不拦的话 `2 ** 1000000000` 会真的去算那个几十万位的数（而不是报错），一按就挂住。
  `fraction` 同理。`bigint` **不拦** —— 它本来就是"要多少位有多少位"的类型。
  但指数本身得装进 32 位：`.NET` 的 `BigInteger.Pow` 只收 `int`，装不下报「指数太大」。
- 优先级 / 结合性在 `Parser.Expressions.ParsePower` 里，**不是靠优先级表** ——
  是让 `ParseCall` 那个一元分支的**操作数走 `ParsePower`**（而不是它自己），
  于是"先把整个乘方收完、再套负号"。`ParsePower` 夹在 `ParseFactor`（`* / %`）和
  `ParseCall`（一元 / 成员链 / 调用）中间。
- `**=`：`Interpreter.Binary` 里那两处把 `"**="` 归约成基础运算符的地方**不能用
  `Op[..1]`**（那会切成 `*`，静默算错）—— 走 `BaseOp ()`，`**` 单独认。

### 移位与循环移位

`<<` / `>>` 是**位型**操作，和 `&` `|` `^` 一伙：只看那 32 个格子，**不检查数值溢出**——
所以 `1 << 31` 是 -2147483648（位型就是 0x80000000），不报错。

| 写法 | 意思 | 例 |
|---|---|---|
| `<<` | 左移，高位**丢出去就没了** | `1 << 32` 是 0 |
| `>>` | **算术**右移（保符号） | `(0 - 8) >> 1` 是 -4、`(0 - 1) >> 40` 是 -1 |
| `<<<` / `>>>` | **循环**移位：移出去的从另一头回来（32 位转圈） | `(0 - 1) <<< 1` 还是 -1、`5 >>> 1` 是 -2147483646 |

- 优先级**照 C 的脾气**：比加减低、比比较高——`1 + 2 << 3` 是 `(1 + 2) << 3`，
  `2 << 1 == 4` 是 `(2 << 1) == 4`（`Parser.Expressions.ParseShift`，夹在
  `ParseComparison` 和 `ParseTerm` 中间）。
- 移位量收成一个**非负的 int**（`ShiftCount`）：负数、装不下的 bigint、别的东西，各报各的。
- **bigint 没有固定宽度**：`<<` 就是乘 2^k（要多少位有多少位），循环移位对它**不成立**，
  那两条明确报错（而不是偷偷当普通移位）。`>>>` 是**循环**右移，不是"无符号右移"——
  要逻辑右移（补零）用 `lib/bits.rav` 的 `Shr`。
- **`<:` / `:>` 是类型之间的关系**：`A <: B`（A 是不是 B 的子类型）/ `A :> B`（父类型），
  **两边都得是类型对象**（接口也是类型）——值那一边用 `is`。两个都注册在 `Object` 上，
  这样 `1 <: int` 报的是「'<:' 的左边得是个类型，得到 Integer 的实例」而不是「类型不支持运算符」。
  判据 = `IsAssignableTo`；接口那半（`myClass <: myTrait`）在求值器里补，见「接口与实现」一节。
- **`is` / `isnot` 是词形运算符**：不是标点，所以解析器在**运算符位置**按词认
  （`Parser.IsWordOperator` / `IsInfixWordOperator`），别处照样能当标识符与成员名用 ——
  于是 `1.is`（等右操作数）和 `is.int`（等左操作数）与 `a.+` / `+.2` 完全对称。
  实现挂在 `Object` 上（每个类的 parent 链都到它），判据就是 `IsAssignableTo`：
  `1 is int` ✓、`1 is float` ✗（兄弟）、`1 is object` ✓、`int is type` ✓。
  节的脱糖必须**就地**做（`ParsePrimary` 里 `DesugarHoles`），否则 `isnot.string "a"`
  会变成 `(_0) => { (_0 isnot string) "a"; }` —— 把实参也吞进体里，而不是"应用节"。
- 分派是**两跳**：`a + b` 先在**类型**那张表里找（`a.Type.MemberScope`）；类运算符那格装的是 `ClassOperatorFactory`，绑完得 `BoundClassOp`，于是推 `ClassOp` 帧到**实例作用域**里按符号名找实现（各层类体平铺在同一 scope、子类覆盖父类，所以只有一个）。
- 成员访问同构：`a.+` 取到绑好 self 的函数，`1.+` 取内置的。

## by 属性

```ravel
by age := property (() => { _age; }) ((v: int) => { _age = v; })
```

`by` 是**两半拼**的：`Variable` 上的一个 attr（门禁问的是它）+ 值是个 `PropertyVal`，
靠两个约定成员名 `Get` / `Set` 接上（`ObjectVal.GetterMember` / `SetterMember`，**不在作用域里**，
`BoxedValue.GetMember` 对 `PropertyVal` 特判）。所以 `by` 和 `property` 互相独立：
只写 `property` 不写 `by`，读出来就是那个 `<property>` 值本身。

三条路、三件事（别混）：

| 写法 | 干什么 |
|---|---|
| `by a := property g s` / `by a: int = …` | **定义槽**：`a` 从此是个属性（注解管写进来的值）|
| 同上，但外层已经有同名变量 | `:=` 是**定义**：在这层建新槽把外层遮住。从前会去找外层那个名字当"要换的槽"，
于是 `by Any := …`（`Any` 是全局的类型别名）会报「标了 by 但值不是 property」`=` 那种（没 `:=`）才是"必须已有槽可换" |
| `a = v` / `obj.a = v` | 给属性赋值：过 setter |
| `by a = X` / `by a.x = X`（`SlotAssign`，语句）| **换掉槽里的那份 property**：`Variable.ReplaceSlot`，不过旧 setter、不查约束 |
| `by a.x := X`（同上，`Define`）| **在那个对象上建槽**：`DefineOrReplace` + `SetAttr(By)`，成员不存在也行（`:=` 定义 / `=` 换，和别处一个规矩）|
| `by a` / `by a.x`（`SlotExpr`，表达式）| **取出那份 property 本身**：不过 getter，拿到的是普通值 |

后两条是"属性本身"那半边（绕开 getter/setter），前两条是"属性值"那半边。

**`default` 是"属性的默认值"**（`BuiltinClasses.DefaultProperty`，五个写入点共用
`Nodes.cs` 的 `SlotValue`）：一对什么都不做的 `FunctionVal` —— `Get = () => { (); }`、
`Set = (_: object) => { (); }`。和 `int default` 给 0、`function default` 给空函数同一个道理。
所以 `by a: int = default` 的槽里是个**能读能写、都不做事**的属性（读 `()`、写丢掉），
它**没有状态**，因此 `CopyScope` 让副本和原件共享同一个是对的。别的非 `property` 值
（`by bad := 5`）不在此列 —— 第一次读/写照旧当场报错。
`SlotExpr` **不能把整条路径当普通表达式求** —— `by c.n` 求 `c.n` 就走 getter 了，
那正是要绕开的；所以 `by a` 直接查变量、`by a.x` 只求接收者 `c`。门禁和成员读同一套。

四条读写路各问一次那个 attr：裸读（`StepIdent`）、成员读（`TryGetByGetter`）、
写（`WriteVariable`）、复合赋值（`StepByCompoundAssign`，先过 getter 读、算完过 setter 写）。
**取函数一律走 `Interpreter.PropertyGetter/Setter`**：标了 `by` 而值不是 `property`
就是报错（从前四条路三个答案，其中裸读会静默交出那个值、裸写什么都不发生）。

attrs 只有一份，在 `Variable` 上（`PropertyVal.Var` 指回去）——`Attrs ()` 也读那一份。
**`readonly` 对 by 槽也管**：`Variable.CheckWritable` 是写入侧的公共门，
`Assign` / `ReplaceSlot` / `WriteVariable` 的 by 分支 / `StepByCompoundAssign` 的写那步
四处都问它（by 那半走 setter，不经过 `Assign`，漏一处"只读"就漏一个口子）。

**`SlotAssign` 那条路只接 `by` 一个修饰符**（`Parser.Statements.cs` 的
`attrs.Count == 1 && attrs.Contains(Attr.By) && IsSlotAssignStart()`）：混着别的修饰符
（`readonly by r := property …`）会落回 `ParseDefinition`，那边才把 attrs 一个个装上 ——
曾经不查这一条时，`readonly` 会被 SlotAssign 静默丢掉。

**类型注解**（`by n: int = …`）管的是**写进来的值**：`StepVarDef` 对 `by` 声明**不**拿注解去比
那份值（比了就是「无法将 Property 赋值给 Integer」），而是把注解记成约束，由写入那侧
（`WriteVariable` / `StepByCompoundAssign` 过 `Variable.CheckAssignable`）执行 ——
和普通字段一样，约束在赋值侧，读侧不管（Ravel 从不检查函数返回什么）。
`by a := …` 不带注解时约束记 `Any`（别把 `PropertyVal` 自己的 `Property` 当约束，那会把写入全挡回去）。

## 接口(interface)与实现(use)

**库里已经在用的四个:`INumber`** —— 最简单的那个,**一个槽都没有**,只是"这个类型是数"的标记
(五种数值类型各 `impl` 一条,于是 `(x: INumber)` 收得下 `int 5` 也收得下 `float 5.0`;
`lib/math.rav` 那四个函数用它,从前标的是 `ValueType` —— 那个连 String 都收)。

**`IComparable`**(`lib/sorting.rav`)—— 一条槽 `CompareTo`,"这个类型自己讲了怎么比大小"。
底层那条协议**每个值都有**:引擎在 `Object` 上放了一条 `CompareTo`(内建那把尺子,数值/字符串),
要换比法就在类里自己写一条盖掉它(和 `ToString` 一个规矩)。详见「比较与排序」一节。

**`IEnumerable` / `IEnumerator`**(`lib/iterator.rav`)。形状照 C#:

```ravel
IEnumerable ::= interface IMonad { by GetEnumerator : function = default; …一整套默认实现… }
IEnumerator ::= interface { by MoveNext : function = default
                            by Current  : object   = default }
```

**接口体里那一整套 `by X := property …` 是默认实现**(接口的类体会在**每个实现对象上
跑一遍**,所以实现体不填就用它):Count / Map / Where / Bind / Fold / Take / First / …。
名字与口径照容器上那批(Linq 的译法),分界线是"交回**一串**的惰性(交回 `Generator`)、
要**一个值 / 容器**的当场算"。于是凡是实现了 `IEnumerable` 的东西(`Generator`、字符串、
用户自己写的类)都有整套方法 —— 从前只有三种容器有(引擎按类型挂的,见
`BuiltinClasses.Sequences.cs`)。**两层同名是有意的**:容器类链上那几个先命中,
接口这份只补"本来要报没有方法"的(`TraitSlot` 见到类链上有名字就退回去)。

写默认实现有一条死规矩:**函数体要写在 getter 里面** —— `Activate` 只把那条 getter
自己的捕获作用域换成"这一次服务谁"的激活格,所以只有 getter 体内创建的闭包才认得出
`instance`;外面造好的函数塞进来会捕错作用域。

序列这一族**也是 `IMonad`**(`IEnumerable ::= interface IMonad`):`Map` 逐个映射、
`Bind` = "每个元素交回一串、接起来"(flatMap)→ `do { … }` 在序列上就是列表推导。
`HasTrait` 判的是 `impl.Type.IsAssignableTo(trait)`,而实现对象的类型**就是那个接口**
—— 所以接口的 parent 一挂上,list / set / dict / string / Generator / 用户类全都
`is IMonad`。(`lib/predefined.rav` 里 `monad.rav` 因此排在 `iterator.rav` **前面**。)

和 `Option` 那半通着:**`Option` 自己也 impl 了 `IEnumerable`** —— 它是"0 个或 1 个的一串"
(`Some x` 走一次、`None` 一次不走):`foreach (Some 5) …`、`Seqs.Flatten [(Some 1) (None) (Some 3)]`
都成立,整串方法照旧走默认实现。**`Map` / `Bind` / `Where` 仍是 Option 类体里那三条**
(类链先命中)—— 那是这一族的"包着 / 摊平",`(Some 5).Map f` 交回的还得是 Option。
反方向:`xs.TryFirst ()` / `xs.TryFind p`(→ `Option`)。

`GetEnumerator ()` 交回一个**枚举器**;枚举器 `MoveNext ()` 往前走一步(返回还有没有),
`Current` 是当前那个(C# 里是属性,这边也做成属性)。库里的 `Enumerator` 就是"拿一串值"的
通用枚举器,谁有现成的一串值谁就能拿它当枚举器。

**枚举器自己也是一串**(`IEnumerator <: IEnumerable`):`IEnumerator` 体里把 `GetEnumerator`
覆盖成"交回**自己**"—— 和 C# 里 `yield` 生成的那个类一个形状(它的 `GetEnumerator ()` 也是
`this`)。于是 `Enumerator` / `GeneratorCursor` / 用户写的游标都白拿整套方法,也能直接
`foreach`。两条语义跟着来:遍历的是**还没走完的那一段**、走一遍就**消耗掉**
(`e.Count ()` 之后再 `e.Count ()` 是 0)。

三种容器各 `impl` 一遍(只填 `GetEnumerator`,其余走默认实现)
(用 `impl` 而不是 `use`:全局登记,库加载时就生效),于是:

- `[1 2 3] is IEnumerable` / `{1 2 3} is IEnumerable` / `{"a": 1} is IEnumerable` 都成立;
- `(xs: IEnumerable) => …` 收得下它们(注解也认接口);
- **`foreach` 改走这条接口**:`e := xs.GetEnumerator ()` + `while { e.MoveNext (); } { f e.Current }`
  —— 就是 C# 里那个循环。从前它只吃 list(`assert (typeof xs == list)`),现在 set / dict
  一样能遍历(字典遍历的是值);**每次进来新开一个枚举器**,所以嵌套遍历同一串值互不打扰;
- 用户自己的类实现一条 `use (IEnumerable MyClass { by GetEnumerator = property … })` 就能进 `foreach`。
- **接口继承一个接口,外加一串要求**:

  ```ravel
  supTrait    ::= interface myTrait { by c : int = default }   # 一个父(继承)
  masterTrait ::= interface supTrait [IEnumerable] { () }      # 父 + 要求
  ```

  **父是继承**:槽取并集(父的 + 自己的;同名以自己写的为准),`<:` 沿着继承走
  (`masterTrait <: myTrait` 成立),而**实现了子接口就等于实现了它的父接口**:
  `u is supTrait` / 注解 / `foreach` / 两个查询全认。
  **要求是前置条件**:实现这个接口的类必须**已经**有那些接口的实现 —— 槽**不并**进来、
  `<:` 也**不**成立(`masterTrait <: IEnumerable` 是 false),只在造实现那一步查有没有
  (`StepImplMake`,报「`masterTrait` 要求 C 已经实现了 IEnumerable（先给它 impl/use 一条）」)。
  所以要求者和实现者的关系是"你得自己另外 impl 一条",不是"我替你带上"。

  实现上:链上挂那一个父(`parent`,类型树/`Subtypes ()`/成员查找/实例化全照旧),
  父的**声明**在造子接口时抄进它的类体(`BakeInterfaceInit`),于是"接口的形"自足 ——
  `StepImplMake` 那两段照旧;`interface` 的 init 走 `Alternate`:接口与类合成一支
  (都声明 `Type`,进到体里靠"谁在造"再分;排在代码块那支前面,不然 `ClassVal : FunctionVal`
  会把它们吃掉)、代码块一支、光有要求的一支(专门说"要求得跟在父后面")、兜底一支
  —— 接口那支交出去的又是**一个小分流器**(要求表 / 代码块):先后两次应用,不是嵌套。

- **两个方向的查询**(都在 `Type` 上,所以任何类型对象、接口对象都有):
  - `T.GetImplements ()` —— 这个类型**现在**实现了哪些接口(接口对象组成的 list);
  - `I.GetImplementors ()` —— **现在**哪些类型实现了这个接口(目标类组成的 list);
  - 都是"当下"的快照:沿当前作用域找生效中的实现,判据同 `u is I`(目标收得下 `T` /
    trait 就是 `I`),所以出了作用域 / `Dispose` 之后就列不出来。`GetImplements` 里子类算
    (实例收得下目标);`GetImplementors` 里普通类是空的(接口槽是"实现"挂上去的,
    而实现的 trait 只能是接口)—— **只有 `object` 例外**:判据是"`impl.Type` 收得下 trait 吗",
    而谁都收得下 `object`,于是它会把**所有**实现者倒出来(实测 19 条,`type` 是 0 条)。
    真要问"谁实现了这个接口"得先自己问一句 `x is interface`(`Types.PrintTree` 里那道闸
    就是干这个的)。同一项只列一次;顺序照查找来(由内到外、后 `use` 的先),
    所以 `GetImplements` 打头的是**当下生效**的那个。
  - 入口在 `CallInto` 的 `BoundTraitQuery` 一格 —— 这活儿要当前作用域,而内置方法的体
    拿不到解释器(和 `is` / 注解那两处同一个理由)。成员值本身是 `TraitQuery`(带方向):
    和 `ClassOperatorFactory` 一个路子,读出来先绑接收者。

**接口槽的接收者可以是任何值** —— 包括**标量**(数 / 字符串 / 字符)。从前 `TraitSlot` 只接
`ObjectVal`,于是标量只能「登记」空槽接口(`INumber` 那种),`by …` 的槽够不着;现在两条路都通
(`foreach "abc"` 就是靠这个:字符串自己实现了 `IEnumerable`)。

**`Interface` 自己不是接口** —— 它是**接口的工厂**(元类,和 `type` 之于类同一个位置):
`typeof 某接口` 就是它,而 `interface { … }` 那下是在**调它**造一个新接口。
接口对象的 parent 挂 `BaseInterface` —— 而 `BaseInterface` **自己就是个接口**
(`BaseInterface is interface` 成立),它是接口那一支的**根**,子接口从它往下继承。

**两份 `init`,各干各的**(挂在不同类体上,所以**不用**判「谁在造」):

- `Interface.ClassBody` 里那份 = **造接口**:`interface [父] [要求表] { … }` 的语法形状
  (`interface { … }` 就是在实例化 `Interface`);
- `BaseInterface.ClassBody` 里那份 = **造实现**:`某接口 某个类 { … }` 是在实例化**那个接口**,
  而「谁在被实例化」就是造出来的实现的类型 —— 推 `ImplMake` 帧跑那两段类体。
  接口的子接口都继承这一份(它们自己的类体里没有 `init`)。

**接口对象的 parent 是它继承的那个接口**(没写父则是 `BaseInterface`),类型是 `interface`
—— `IEnumerable.parent` 是 `IMonad`、`IMonad.parent` 是 `BaseInterface`,
而 `typeof IEnumerable` 是 `Interface`(`is interface` 成立)。
`BaseInterface` 下面挂的就是所有接口(`Subtypes ()` / 类型树上看得见),它类体里那份
**默认 `init`** 是所有接口共用的 —— `myTrait myClass { … }` 是实例化 `myTrait`,而
`StepClassInit` 沿类体链从具体往上一找就到它(`CollectBodies`)。

从前没有这一层:接口的 parent 直接是 `object`,继承不到 `init`,于是每个接口的类体里都**烤**
一份(`BakeInterfaceInit`);后来"父的声明抄进来"也是同一路数(`BakeParentDeclarations`)。
现在两份都没有了:**接口的类体就是用户写的那份**(和普通 class 一样,各层各存各的),
**跑**也照普通那一套分两趟:
- **实例化那趟**(`StepClassInit`,就是说 `某接口 某个类 { … }` 时"实例化那个接口")
  沿 `CollectBodies` 依次跑进它建的那个对象的 scope —— 于是"先摆槽(P 的体)、再摆槽(Q 的体)"
  是**跑**出来的顺序(同名后写的说了算),不是抄语句排出来的;
- **实现帧**(`StepImplMake`)只追加**实现块**(`by a = property …` 那些"换槽"),
  跑之前把那个 scope 的词法父换成**实现块写在哪**(`Reparent`)—— 它是实例化那趟建的,
  父本来是**接口定义**处,而实现块的自由名字得在实现处解析(`lib/keys.rav` 里 `IDict` 的
  实现块引用的 `Of` 就只在那儿可见)。
  **接口那半不用 `BodyScope`**(见 `StepClassInit` 里的 `LayerScope`):接口体是"给实现用的"
  (槽要落在实现身上),它得和实现块**共用一个环境** —— 实现写在哪,接口体就在哪解析,
  这样 `use` 在哪个作用域里登记的实现在槽体里也看得见(`tests/218` 的"随作用域在/不在")。
  类的每层就不一样:它们各写各的,各按各的。

代价是 `BaseInterface` 那份默认 `init` 也会落进实现 scope(`u.Fields ()` 里看得见一个 `init`)
—— 和普通 class 的实例一样(`c.Fields ()` 里也有),算"和 class 一致"。
那条 `Scope.Reparent` 全库只此一处调用(只为接口那半留着)。

**接口只承诺它自己那几条槽**。序列方法(`Map`/`Where`/`Fold`…)挂在**具体容器**上,所以通用
函数里按接口的契约写(`GetEnumerator` / `MoveNext` / `Current`),或先落到那串值再往下用 ——
这样任何实现者都吃得住(`tests/217` 就是这么写的)。

库里的用例先放上面,下面从形状讲起:

```ravel
myTrait ::= interface {
    by a : int = default
    by b : function = default
}
myImplement := myTrait myClass {
    by a = property (() => { instance.x; }) ((v: int) => { instance.x = v; })
}
use myImplement          # 只在这个作用域里生效
u := myClass ()
u.a = 1
myImplement.Dispose ()   # 提前取消
```

机制全在 `Runtime/BuiltinClasses.Interfaces.cs`,**求值器只多了一个控制帧**:

- `interface` 是内置类对象、`parent` 是 `type`(`Link(Interface, Type, Type)`)⇒ `interface is type`,
  而 `interface { … }` 造出来的是**类对象** `myTrait`(元类是 `Interface`)。它的**类体**就是接口的
  "形"(那批 `by a : int = default`)。`Interface.ClassBody` 的 `init` 是 `Alternate(twoArg, oneArg)`,
  **twoArg 必须排在前面**:`ClassVal : FunctionVal` 且 `Type <: Function`,反了的话
  `(body: Function)` 会先把 `myTrait myClass` 里的类对象吃掉(然后报「class 需要代码块参数」)。
- `myTrait myClass { … }` 推 `ControlKind.ImplMake` 帧(`StepImplMake`):**接口类体与实现块依次跑在
  同一个 scope 里**(先摆槽、再换槽),那个 scope 就是实现对象的成员表 —— **槽住在实现里**,
  建一次一直用。收尾(`FinishImplementation`)在那个 scope 里装上一条 `instance` **by 槽**
  (`Attr.By` + `Attr.Readonly`),它的 getter 是原生闭包:**读的那一刻**现场解析"这一次调用在服务谁"。
  两段都要推帧才能跑,而原生闭包的体是同步的 —— 这就是它非要一个控制帧的原因。
- 读写 `x.a` 的兜底在 `BuiltinClasses.TraitSlot`:常规成员表(`MemberScope`,含类链)里没有这个名字时
  才去沿 `CurrentScope` 的词法链找生效中的实现(同 scope 里后 use 的先试、目标类收得下就认),
  找到返回一份**命中**(哪条槽、哪个实现、这次服务谁)。真正要用它的调用点再走 `Activate`:**临场开一层
  作用域**(`instance$active` = 这次服务谁,`impl$active` = 哪个实现),把槽的 getter/setter 绑到那层上。
  `TraitSlot` 自己**不建**激活格 —— 有一半调用点只是问一句"有没有"(`HasTraitOperator`,每次二元运算
  类型表落空都会问)。落点:读 `TryGetByGetter`;写 `StepMemberAssign`(count2)/ `StepCompoundAssign` /
  `StepTraitOp` 的第二支;`StepSlot` / `StepSlotAssign` 那两支**只取槽、不绑**(`by` 取的就是槽本身)。
  **`myClass` 这个名字一个字没动**:不建子类、不换绑定、不往它身上加成员
  (所以 `x : myClass = u`、`print u`、`u.Fields ()` 照旧,`Fields ()` 里也**没有** a/b)。
- **`instance` 是"这一次调用"的事,不是实现身上的一格共享变量**(从前是 —— 于是留存下来的闭包,
  比如 getter 返回的那个,会跟着后一次访问改意思,静默错值)。读它走两层:**先**沿当前调用点作用域链
  找最近的激活格(槽体自己、以及槽体里造的闭包 —— 闭包捕获创建处的 scope,靠这一层记住自己属于谁),
  **再**沿帧链往外找(槽体里调用的辅助函数,它的词法链上没有激活格但调用链上有)。
  两格都只认**本次那个实现**(`impl$active` 记的是它的**号**,`impl$id`:拿值当身份证而不是拿实现
  对象本身,所以 `Copy ()` 出来的副本 —— 另一个对象、同一个实现 —— 认的是同一枚,照旧好使);
  跨实现边界宁可报「没有正在被服务的实例」也不错绑。
  没有激活格就报错、`instance = x` 报只读(`Attr.Readonly`)。它**不进**"`u.x` 能读到什么":
  `TraitSlot` 见到这个名字直接返回 null,否则 `u.instance` / `(5).instance`(全局 INumber 实现的
  目标类就是 int)都成了能读的新成员。
  那条槽自己还挂着 **`protected`**(两道闸各管一半):`im.instance` / `by im.instance` 从外面碰报
  「变量 'instance' 是受保护的」("实现这一族的东西"),实现自己的代码照旧读得到;而 `u.instance`
  报的仍是「类型 'C' 没有方法 'instance'」—— 它的语义是"u 身上没这个成员",那是名字那道闸给的。
  两道判据是分开的:同一个接口的**另一个实现**在可见性上让过(protected),但它那次分发不在这儿,
  于是落到「此刻没有正在被服务的实例」。
- `use impl`(`System.Use`)/ `impl 实现`(`System.Impl`)把实现登记进**一个作用域**的成员里
  (键 `use$impls`;`$` 不在标识符字符集里,用户写不出这个名字,永远不会撞):`use` 给的是
  **当前作用域**(随作用域在/不在),`impl` 给的是**全局作用域**(每个作用域链都到全局,于是处处生效 ——
  顶层 `use` 和 `impl` 等价,差别只在函数体/模块里写的时候)。登记在 scope 上而不是类上,所以效果
  **随作用域在/不在**。
  表里每条是 `[实现, 登记时代号]`;实现的成员 `generation` 每 `Dispose` 一次 +1,于是"取消"是
  O(1) 的作废(老条目全失效),之后在哪 `use` 就在哪重新登记一条(那个作用域又活了)—— 不用记
  (实现 × 作用域) 那笔账,也就不怕在循环里 `use`。
- `x is myTrait` 的兜底是 `BuiltinClasses.HasTrait`,同一个判据。它挂在 `StepBinaryOp` 里而**不是**
  运算符的 C# 体里 —— 内置运算符的体是纯 C#,拿不到解释器也就拿不到当前作用域;只接**内置**那一支,
  类里写过 `is := f` 的照旧走自己的实现。
- **接口也算类型,判定与类型检查同一个判据**(`BuiltinClasses.HasTrait`:当前作用域里有生效中的
  实现、目标类收得下这个值)。挂点四处:读写成员的兜底 `TraitSlot`;`is`/`isnot`(在
  `StepBinaryOp` 里 —— 内置运算符的体是纯 C#,拿不到解释器);**注解**与**参数**
  (解释器的 `Accepts`,落在 `StepVarDef` / `CallInto` 两处);**写入口**
  (`ViaTrait` 当 `Variable.CheckAssignable` / `Assign` / `Scope.Assign` 的补充判据)。
  于是 `x : myTrait = u`、`(v: myTrait) => …`、`by h : myTrait = property …` 在实现生效期间
  都通得过,出了作用域(或 `Dispose` 之后)照旧报「无法将 … 赋值给 myTrait」。
  这**不动 `IsAssignableTo`**(纯函数,拿不到解释器也就拿不到当前作用域)。
  **`myClass <: myTrait`(类型那一侧)也认**:同一个 `HasTrait`,判据是"这个类的实例在作用域里
  都算那个接口";`(typeof x) <: myTrait` 就够不着了(类型推断不出是哪个实例),
  那一侧用 `x is myTrait`。
- 接口里 `= default` 的槽,实现没填就是那个"什么都不做"的默认属性(读 `()`、写丢掉)—— 和 `default`
  本来的语义一致。
- **槽里能放运算符**:`by + := property g s`。那一格的**值是 property**,所以用它是**两级**
  —— 先读槽(推 getter)拿到运算符函数,再拿它收右操作数(`ControlKind.TraitOp`,和类运算符的
  `ClassOp` 分开:两者阶段数不一样)。挂点只有一处:`BindOperator` 在**类型表落空**之后问一句
  "这个接收者身上有没有这个符号的 `by` 槽"(自己那层 → 生效中的接口实现),有就交给 `TraitOp` 帧。
  于是 `u + v`、`u.+ v`、`u += v` 三条路都通(复合赋值那条经 `CallAssign` 写回)。
  两边看得见的不同:trait 那边是 `instance`,类体里自己声明的那条是 `this`。
  **只认 `by` 槽**:裸的 `+ := f`(类运算符)走原来的 `ClassOperatorFactory` 那条路,不变。
- **槽上的访问控制照旧**(`private` / `protected` / `readonly` / …):判据就是现成那一套,只是 owner 是
  **接收者**(读 `TryGetByGetter` 的 `CheckObjectReadAccess`、写 `StepMemberAssign` 的
  `CheckMemberAccess`)—— 所以 `private` 的含义是"**这个类自己的代码**能碰"(实现体在类外面,碰不到),
  `protected` 是"子类的代码也能"。措辞沿用原样:读报「变量 'a' 是私有的」、写报「字段 'a' 是私有的」。
  可见性写在**接口那次声明**上;实现体**换槽**(`by a = …`)时再写修饰符解析器直接拒
  (「修饰符后需要 ':='」),**新建**槽(`private by a := …`)可以带 —— 和类体一个规矩。
  接口声明成 `readonly` 的槽实现体换不了它(换槽要过 `CheckWritable`)—— 等于那种槽形同没法实现,
  **现状如此**(测试 222 钉着);要改的话有两条路:声明时就拒掉,或者"实现体填一次、之后只读"。
- 已知代价:① 实现 scope 的词法父是**实现块**的捕获作用域(接口体与实现体通常写在同一处);
  ② 留存下来的闭包各自拎着那次调用的激活作用域 —— 被引用的实例不会提前释放(从前实现身上只留
  最后绑定的那一个,但那个语义是错的);③ `by u.a` 取出的是**槽本身**,它没有"这一次"可言,
  之后再 `.Get ()` / `.Set v` 会报「此刻没有正在被服务的实例」;④ 同理,实现体里别的方法
  (`impl.someMethod ()`)直接读 `instance` 也报这个 —— "这一次调用"指的就是**槽体那一次**
  (槽体里调它没事:帧链那一半认得)。

测试 —— 213(端到端:读写、`is`、`Fields`、类型约束、Dispose)、214(作用域、叠加、子类、`use 5`)、
221(`instance` 是"这一次调用"的事:留存闭包、一次实现服务两个实例、辅助函数、没有激活时报错)、
222(槽上的访问控制:private / protected / 换槽不许带修饰符 / readonly 槽的现状)、
examples/type_tree.rav(类型树上多一个 `Interface`;它是**例子不是用例**,不进 `tests/` 那套 ——
每加一个类型都要重钉,不值当)。

## 多参数 lambda

```ravel
add := (x: int y: int) => { x + y; }
add 3 4    # 柯里化
```

**注解可省**:`(x) => …` ≡ `(x: object) => …`（逐个参数都行,混着写也行 —— `(a b: int)`）。
省了就不在调用点挑类型,也正是 `do` 绑定的变量、占位符消糖用的那个类型。
解析上这步落在 `ParseParen` 的 `LooksLikeLambdaParams()`:它要**一路确认到 `) =>`** 才认
参数表（`(a + b)` 那种分组里也有名字,看到名字就当参数会把分组吃成参数表）。

## _ 占位符 / 运算符节

```ravel
add1 := _ + 1    # (x: object) => x + 1
add2 := +.1      # 同上,运算符节写法:符号在前表示左操作数留空
```

`+.1` 与 `_ + 1` 脱糖成同一个 lambda。右操作数**只吃一个 primary**（含成员访问），
复杂式要自己加括号：`+.(2 * 3)`。

**括号界定 section 的范围**：`(+.1) 41` 先把 `(+.1)` 变成 lambda、再让 `41` 应用上去 = 42。
（占位符消糖本来只作用于整条语句，那会把 `(+.1) 41` 整个包成 `(_0) => (_0 + 1) 41`，
所以 `ParseParen` 在括号内就地收口。）

消糖那趟把 **lambda / 块当闭包边界**：里面的 `_` 归内层，不往外收。**唯一钻进去的是
语法糖生成的那些 lambda**（`LambdaExpr.Sugar`，`.?` / `??` / `??=` 脱糖时那几枚）——
它们只是把表达式挪个地方，不引入自己的 `_` 作用域，所以 `_ ?? 1` 里那个 `_` 照旧是
外层语句的洞（不钻的话会一路带到求值器报「无法求值的节点类型: HoleExpr」）。

## 字符串插值（`"${…}"`）

**只有 `${表达式}` 一种写法**（没有 `$名字` 那种短的）。它不是新语义:解析器把它拼成
`"" + "段" + string (表达式) + …` —— 和手写 `"a" + (string x)` 一模一样,于是求值、报错、
显示全走现成那条路(`string` 那个转换器管着"什么都转得成字符串":数、字符、对象都行)。

- 词法只负责**切开**:`Lexer.ReadString` 把串切成 `Token.Parts`(字面量文本 / 待解析的源码
  + 它在原文件里的行列);每段表达式由 `Parser.ParseExpressionSnippet` **另起一次**词法+语法
  —— 所以报错指得对地方(`${noSuch}` 报「未定义的变量」时插入符正落在那一段上)。
- 段里可以再放字符串(`"${"abc".ToUpper ()}"`):扫 `${…}` 时会跳过引号里的内容。
- `$` 后面不是 `{` 就是**字面量**(`"$5"` / `"$name"` 都不用转义);要写 `${` 本身用 `\${`。
  没闭合的 `${` 报「插值没有收尾的 '}'」。
- **代码里没有 `$` 这个运算符** —— 它只在字符串**里面**有意义(插值,或者一个字面量)。
  想在代码里"把右边封成一个实参"用 `<|`(见下面那节)。
- **原始字符串(`"""…"""`)不插值**,连转义也不做 —— 那才是"原始"。要 `${…}` 就用普通
  字符串。(`$"""…"""` 那种"原始的 + 还插值"的变体**没做**:它要先把 `$` 摆在引号外面,
  而那正好是"代码里不该有 `$`"这条定下来的地方。)

用例 `tests/lang/237_char_string.rav`;原始字符串见 `tests/287`(快乐路径)+ `288` / `289`
(缩进对不齐、没收到尾,两条 `# expect-error`)。

## 空值那三条（`?.` / `??` / `??=`）

**全是解析期脱糖，运行时不为它们添任何东西**（和 `do` 一个路子）：引擎只认符号，
"空是什么意思"在 `lib/predefined.rav` 那四条里。

| 写法 | 折成 |
|---|---|
| `a?.b c` | `NilChain (() => { a; }) (v) => { v.b c; }` |
| `a ?? b` | `NilOr (() => { a; }) (() => { b; })` |
| `x ??= v` | `NilFill (() => { x; }) ((w) => { x = w; }) (() => { v; })` |

- **「空」只有 `None` 一样**：`()` 不是空 —— 它是 Void 类型那个**值**，和 `0` / `""` / `[]`
  平起平坐（`Some ()` 更不是）。`IsNothing` 那一条定义；要"没设过"就明写 `None`
  （`Cache: Option = None`）。
  `?.` 空则**原样**短路，`Some x` 拿着 `x` 走完再**包回去**（形状不变）；`??` 空给后备、
  `Some x` 给 `x`、别的值原样（`()` 也照交）；`??=` 空才写（不空连 setter 都不调），
  交回写完之后的值。
- 折成的闭包参数名是 `_nil{n}`（`Parser._nilCount`）—— 和占位符消糖的 `_n` 分开，
  两种糖落在同一条语句里也不会互相遮蔽。
- **`?.` 把后面整条链接管**（成员 + 并列调用都进那枚 lambda），所以 `u?.Name ()` 是
  "有 u 才调 Name"。`a?.b?.c` 一条链只包**一层**（第二个 `?.` 当普通 `.` 使），
  各包各的会变成 `Some (Some (…))`。
- 位置规则和 `.` 一样：**只在 `.` 能出现的地方**（primary 之后、`|>` 之后）——
  `?.` 咬不到"调用之后再取成员"，那本来就该写 `|>`（见上一节）。
- `??=` 的左边只能是变量或成员；成员那条把接收者**先求一次**再包进闭包
  （`((r) => { … }) (f ())`），否则 `f ().b ??= v` 会把 `f` 跑两遍。
- 单独的 `?` 不是这套语言里的东西（没有三目）：词法器当场报
  「'?' 只用在 '?.' / '??' / '??=' 里」。测试 258。

## `<|` / `|>`

并列调用是左嵌套（`f a b` ≡ `(f a) b`）；`<|` 把**右边**封成一个实参，`|>` **把左边交给右边**：

- `<|`：**最低优先级、右结合**（在 `ParsePipe` 那一层）。`f <| a + b` ≡ `f (a + b)`。
- `|>`：**右边是什么，决定了交法**：
  - `.成员` / `?.成员` —— 挂到左边那个值上：`x.f () |> .g ()` ≡ `(x.f ()).g ()`；
  - 别的 —— 左边当**实参喂给右边**：`x |> f` ≡ `f x`、`x |> f 1` ≡ `f 1 x`
    （右边自己的实参先喂，**左边排最后** —— 柯里化的东西多半最后那个参数才是主角）、
    `a |> f |> g` ≡ `g (f a)`。

  右边那半**不吃下一个 `|>`**（`ParsePostfixRest` 的 `stopAtPipe`）：吃了的话
  `a |> f |> g` 会成 `a |> (f |> g)`，而我们一路往右喂要的是 `g (f a)`。
  尾巴上多出来的 `|>` 报错（`a |> .f ()` 是挂成员，`a |> g` 是把 a 喂给 g）。
  运算符节也认（`5 |> +.2` ≡ `(+.2) 5`）—— 那个开头是个 `+`，`StartsPrimary` 网不到它。

- **`.成员` 那一支是后来让位的**：它最早只有一个意思（"封口"，`a |> b c` 和 `a b c` 一样）。
  改成"喂进去"之后，`|>` 才真的像个管道；不需要它的地方照旧写并列调用。

**`<|` 有一处非它不可**：喂给一个**柯里化了一半**的调用 —— `add 1 <| 7` ≡ `(add 1) 7`。
这要求实参**吃不掉 `<|`**：实参走的是 `ParseAssignment`，而 `<|` 在它上面的 `ParsePipe` 那一层。
这是"实参吃到运算符为止"那条规矩**唯一的例外**，写明白了放在 `ParsePostfixRest` 里。

> `$` 曾经是这个角色，后来**撤了**：它长得像 Haskell 的 `$`、行为却不一样（Haskell 的 `$` 是最低
> 优先级，而这里的 `$` 住在应用那一层），`1 + f $ 2` 会被读成 `1 + (f 2)` —— 和那个直觉正好拧着。
> 现在 `$` 只剩字符串里的插值；代码里写到它就是一句**指得着路**的词法错（见 `Syntax/Lexer.cs` 里那条）。

`|>` 的动机：`.成员` 比并列的调用绑得紧，`x.f ().g ()` 会被读成 `x.f ((().g ()))`（零参调用后面接链
全废）。测试 231。

**并列的调用比运算符松**:实参吃到运算符为止 —— `print 1 + 2` ≡ `print (1 + 2)`,
`inc 1 * 5` ≡ `inc (1 * 5)`,`f a b` 照旧是两个实参(柯里化不受影响)。
实现就是 `ParseCall` 里实参那一档用 `ParseExpression(allowCall: false)`(运算符链、不吃并列的实参)。
**没有例外**,括号开头的实参也一样:`f (1) + 2` ≡ `f ((1) + 2)`(从前那条"括号组自己完整"的
特判已经删掉 —— 库里 `x.Count () == 0` 那一类写法全改成了带括号的 `(x.Count ()) == 0`)。
代价:运算符要作用在调用**结果**上时自己加括号;`string x + " 个"` 里 `string` 会把 `+ " 个"`
一起吃掉(类型错误,响亮),转换器式调用(`string x` / `bigint n` / `typeof x`)后面还要接东西
也得把实参括起来 —— 教程 3.9 与常见陷阱、测试 233。
## ::= 命名

```ravel
add ::= (x: int) => { x + 1; }
add.name   # "add"
```

## 集合/字典

```ravel
{1 2 3}          # Set  (无换行)
{"a": 1 "b": 2}  # Dict (无换行 + 第一个元素后面跟 `:`)
{}               # 空字典(单行;空块本来就禁止,所以没有歧义)
{a; b;}          # Block (有分号/换行)
```

**字典的键是表达式**(从前那条"标识符即字符串"的糖已经去掉,别写成 `{a: 1}` ——
那个 `a` 是**变量**):字符串键要写引号,`{1: "x"}`、`{k: v}`、`{"a" + "b": 1}` 都行,
键求出来不是**值类型**就报「字典的键得是值类型（数 / 字符串），得到 A」。
判据在 `ParseBrace`:**先读第一个元素,看它后面跟的是不是 `:`**(那一次解析的结果
字典、集合两条路共用 —— `_` 的序号是单调计数器,读两遍会让洞的编号对不上)。

**字典键的相等性**:进表的键**只有值类型**(`d.Set` 会先把键规范一道,"见键与查找"),
所以比的就是值自己 —— `1` / `"1"` / `1.0` 是**三个**格子(`d.Keys ()` 交回它们本身;
打印出来都像 `1`,那是显示的老规矩:`print [1 "1" 1.0]` 也一样)。两条边角:
**`0` 与 `0.0` 是两个键**(不同类型),**`NaN` 与 `NaN` 是同一个键**(`Double.Equals` 说 NaN
等于 NaN,而 `NaN == NaN` 是 false —— 表和 `==` 本来就两套)。

**集合元素的相等性**:标量(`int`/`string`/…)比**值**(所以 `{1 2 2}` 是 2 个元素);
`list`/`set`/`dict` 比**身份**(record 的自动相等对 `List<T>`/`HashSet<T>`/`Dictionary`
字段退化成引用比较),所以 `{[1] [1]}` 是 2 个元素、`{ [1] }.Contains [1]` 是 false。
集合类型**没有 `==`/`!=`**:可变集合上的值相等说不清(放进去再改,哈希就失效了)。
这是有意的取舍,不是漏实现——要改就得先解决循环引用下的深度相等(`ShowDepth` 只管显示)。

**「有 `;`/换行才是 Block」这条规则不看位置,lambda 体一样适用**:
`() => { x + 1 }` 是语法错误,得写 `() => { x + 1; }`。`;` 和换行都是 Newline token,
它是「这是块」的标记——没换行时 `{ x }` 是 Set、`{ a: 1 }` 是 Dict,
没有别的信息能区分。`ParseMandatoryBlock`(`=>` 后面那个块)也要过这条检查。

(这里曾经去掉过检查,理由是「`=>` 后面块是强制的、没有歧义」——那是按解析器好不好写
在想问题。语言规则该由语言定;当时改完连 `tests/58`/`72` 都从「被语法错误抢先报错」
变成了「靠语法错误通过」,两个用例始终没测到自己要测的 readonly/unreadable。)

## 序列方法(三种容器共用的一层)

`list` / `set` / `dict` 上那些**只跟元素顺序有关**的方法(`Count` / `IsEmpty` / `Any` /
`Contains` / `First` / `Last` / `ToList` / `ToSet` / `Distinct` / `Reverse` / `Take` / `Skip` /
`Concat` / `Join` / `Sum` / `Min` / `Max`,以及收函数的 `Each` / `Map` / `Where` / `Fold` /
`All` / `Any p` / `Find` / `SortBy`)**只有一份实现**,注册给三种容器
(`Runtime/BuiltinClasses.Sequences.cs`),每种容器只回答一件事:**怎么按枚举顺序把元素取出来**
(`items` —— 字典给的是值)。

这一层是**引擎按类型**挂的,C# 侧当场算(所以快);接口那一层(`lib/iterator.rav` 里
`IEnumerable` 体内的默认实现)是**另一份**,给"类链上没有这些方法"的实现者用
(`Generator`、字符串、用户自己的类)—— 两份同名不是重复犯错,而是快路径 + 兜底:
`TraitSlot` 见到类链上有就退回去,所以容器永远走引擎那份。
两者口径一致(名字、急切/惰性分界、报错措辞),`ElementsOf` 是引擎侧的"接受任何可枚举"。

两批的分界是**要不要调用户函数**:

- 不调的:普通内置方法,纯 C# 当场算;
- 调的(`Each`/`Map`/`Where`/`Fold`/`All`/`Any`/`Find`/`SortBy`):成员值是 `SeqMethod`
  (`ISelfBinding`),读出来绑好接收者就交出 `ControlFunction` → 求值器推 `ControlKind.SeqOp` 帧。
  **非这样不可**:原生闭包是同步的 C# 调用,而调 Ravel 函数 = 往帧栈上推帧
  (见 `Interpreter.Control.cs` 的 `StepSeqOp`,一个元素一步)。

规矩(都照 C# / Linq 对齐):

- 变换与查询**交回新的 list**(今天"一串值"就是 list),要 set 自己 `ToSet ()`;
- 顺序类的(`First`/`Last`/`Take`/`Skip`/`Reverse`)按**枚举顺序** —— set / dict 的顺序是
  它们枚举器给的,不是插入顺序;
- 比大小一律走 `<`(`Less`),比不了**报错并说出两边**(`[1 "a"].Max ()` → 「比不了 Integer 与 String」)。
  (自己的类型要比大小 / 排序走的是**另一条协议** `CompareTo` —— 见「比较与排序」。)
  排序经 `SortByKey`:先拿第一个量一遍再排 —— .NET 会把比较器抛的异常包成
  `InvalidOperationException`,那不是 RuntimeException,Ravel 的 `try` 接不住;
- 字典的"元素"是**值**(按键找用 `Has` / `Keys ()`);
- 空容器:`First` / `Last` / `Min` / `Max` / `Find`(没找到)**报错**,`Sum ()` 给 0。

## 字符串与字符

`string` 的方法在 `Runtime/BuiltinClasses.Methods.cs`(`RegisterStringMethods`),全是**同步的
C# 调用** —— 它们不收用户函数,所以不走 `SeqMethod` 那套控制帧。

| 归类 | 方法 |
|---|---|
| 问长度 | `Length ()`(UTF-16 码元数)· `IsEmpty ()` |
| 取字符 | `At i`(**越界报错**)· `Chars ()`(→ 一串 `char`) |
| 找 | `Contains x` · `StartsWith x` · `EndsWith x` · `IndexOf x`(找不到**报错**)· `Find x`(→ `-1`)· `LastIndexOf x` |
| 切 | `Slice [1..3)`(**收区间**)· `Take n` · `Skip n` |
| 变 | `Trim ()` · `ToUpper ()` · `ToLower ()` · `Replace a b` · `Repeat n` · `Reverse ()` · `Split sep`(→ 一串 string) |

收 `x` 的地方**字符串和字符都收**(`"abc".Contains 'b'` 一样通)。大小写转换**不跟区域设置走**
(`ToUpperInvariant`):土耳其语的 i/İ 那种会让同一段程序换台机器就换个结果。

**`char`** —— 一个 **UTF-16 码元**,和 `Length` / `At` 一个口径(`"😀".Length` 是 2,
一个 `'…'` 装不下它)。它是**值类型**:能当字典的键、能排序、能进集合去重 ——
`IComparable` / `IKey` 都登记过(`lib/sorting.rav` / `lib/keys.rav`)。
字面量 `'a'`,转义和字符串那套一样外加 `'`;`'ab'` / `''` 当场报错(那是写错了,
不是"两个字符的 char")。

- `'a' + 'b'` → `"ab"`(拼接)· `'-' * 5` / `"ab" * 3` → 重复(0 或负数给空串)
- `int 'A'` → 65(码位)· `char 97` / `char "x"` → 字符 · `'A'.Code ()` 同上
- 字符自己那几个:`IsDigit` / `IsLetter` / `IsUpper` / `IsLower` / `IsSpace` / `ToString ()`

**字符串是可枚举的**:`"ab" is IEnumerable` 成立,`foreach "abc" (c: char) => …` 直接能跑
(`lib/iterator.rav` 里登记的一条 `impl`,元素是**字符**)。

⚠️ 这一条是**后来才通的**:接口槽从前只服务 `ObjectVal`,而字符串和数一样是**标量**
(没有自己的成员表,方法都挂在类对象上)—— 于是 `is` 说 true、`"ab".GetEnumerator ()` 却说
「没有方法」。现在 `TraitSlot` / `ActiveInstance` 的接收者放宽到 `RuntimeValue` 了:
**任何值都能挂接口槽**,不管它是不是对象。

用例 `tests/lang/237_char_string.rav`。

## 比较与排序（`lib/sorting.rav`；predefined 加载，所以 `IComparable` 是全局名）

**协议**：`CompareTo other` 交回 -1 / 0 / 1（和 C# 的 IComparable 同约定；负=我小）。
它**每个值都有** —— 引擎在 `Object` 上注册了一条（`Runtime/BuiltinClasses.Methods.cs`），
体就是引擎那把尺子 `Less`（也就是 `<` 的口径），所以 `3.CompareTo 5` / `"a".CompareTo "b"`
拿起来就能用，不必谁登记。比不了照样说人话（「比不了 Integer 与 String」）。

**换比法**：在自己类里写一条 `CompareTo` —— 它盖掉 `Object` 那条（和 `ToString` 一个规矩）：

```ravel
Rec ::= class {
    K: int = 0
    CompareTo := (o: Rec) => { if { K < o.K; } { -1; } { if { K > o.K; } { 1; } { 0; } } }
}
impl (IComparable Rec { () })
```

`impl` 是"**这个类型自己讲了怎么比**"这个类型层面的事实：登记之后 `x is IComparable`、
注解 `(x: IComparable)`、`GetImplementors ()` 全认（和 `INumber` 一个规矩；六种内建标量
也空登记了一遍）。**现装那条路（`by CompareTo = property …`）在这儿用不了**：这个名字
`Object` 上已经有了，而成员查找是**类链先说话**、槽还没轮到 —— 和 `IFile` 那种"类里本来
没有的名字"不同。

**`Sorting` 模块**：`Compare a b`（就是 `a.CompareTo b` 的名字）· `Sort xs` · `SortBy xs key` ·
`Max xs` / `Min xs` / `MaxBy xs key` / `MinBy xs key`。

- 排序是库里自己写的**稳定归并**（`SortOn` / `Merge`），比的是 `CompareTo` —— 于是
  **实现了接口的类型也排得了**。引擎那个 `xs.Sort ()` / `xs.Min ()` / `xs.Max ()` 走 `Less`
  （只认内建标量），碰见对象只会说「比不了 X 与 Y」。库里这套与它**并存**，不是替掉它。
- 稳定性：键相等的保持原来的先后（`Merge` 里 `<= 0` 取左边）。
- `Max` / `Min` 空表**报错**（和 `First` / `Last` 一个规矩），相等时留先出现的那个。

用例 `tests/class/217_ienumerable.rav`。

## JSON（内置 `Json` 类，底层 Newtonsoft.Json）

**先看再转**：`Json v` 把原生值包成 Json，`Json.FromString s` 解析字符串，
中间那棵树（Newtonsoft 的 `JToken`）**一直留着** —— 想看就问 `Kind` / `IsNull` / `Get` /
`At` / `Count` / `Keys`，想好了再 `Extract ()` 拿原生值。这样 JSON 的 null、整数与小数的
写法、键的顺序都不用在解析那一刻被迫选一次。

```ravel
j := Json {"a": [1 2.5 ()] "b": {"c": true}}
print (j)                                  # {"a":[1,2.5,null],"b":{"c":true}}（print 就是紧凑 JSON）
print ((j.Get "a").Count ())               # 3
print (((j.Get "a").At 1).Extract ())      # 2.5（float）
d := j.Extract ()                          # 一次转成 dict / list / 数 / string / bool / ()
k := Json.FromString "{\"x\": 1}"          # 解析（唯一的入口）
print ((j.Text ()))                        # 紧凑；`j.Text 2` 缩进两格
```

**两个方向**：`Json v`（原生 → Json）与 `Json.FromString s`（字符串 → Json）进来，
`Extract ()` 出去。

**改**：`Set key v` / `SetAt i v` / `Add v` / `Remove key` / `RemoveAt i` —— 直接改那棵树。
加这几条是因为"改配置里一个字段"从前得 `Extract ()` 整棵出去、改完再 `Json` 包回来：
底下那棵树（`JObject` / `JArray`）本来就是可变的，不给一条路只是白白绕远。

- 写进去的值走**同一张换算表**（`ToJson`，和 `Json v` 一样），也可以直接给一个 Json 值；
  表外的类型（自有类、分数……）当场报错那套照旧。
- `Remove` 交回"有没有删掉"（和 `dict.Remove` 一个口径）；`SetAt` / `RemoveAt` 越界当场报错。
- 改的是**这一个 Json 值自己**（和 dict / list 一样按身份看）：`Json v` 建的时候就把树拷出来了，
  动不到那份原始 dict。
- `Get` / `At` 交回的是**那棵树的窗口，不是副本** —— 改子节点，父的那份跟着变
  （要一份独立的就 `Extract ()` 出来再 `Json` 包回去）。

| 原生 | JSON |
|---|---|
| `dict` | object（键本来就是字符串，保插入序）|
| `list` / `set` | array |
| `int` / `float` / `bigint` | number（**`bigint` 原样写数字，不经过 double、不丢精度**）|
| `string` / `char` | string |
| `bool` | true / false |
| **`()`** | **null**（反方向也回 `()`）|
| 别的（自有类、分数、函数……）| **当场报错**：「先自己转成 dict / list / 数 / 字符串 / 布尔 / ()」|

`Extract ()` 反着走：`JObject` → `dict`、`JArray` → `list`、整数装得下 `int`、太大退 `bigint`、
带小数/指数是 `float`、`null` → **`()`**。

**`null` 为什么是 `()`**：`Extract` 是引擎里的一次递归，而引擎造不出 `None`（那是
`lib/monad.rav` 的，按名去全局取库值全仓零先例、也会让引擎依赖库的加载）；`()` 是语言级、
永远存在的值。代价是转完就分不出"值是 null"和"函数没返回值"——所以**要问就在 Json 那层问**
（`IsNull ()` / `Kind ()`），`Extract` 是"我确定要原生值"的那一步。

两个坑（都写在 `Runtime/BuiltinClasses.Json.cs` 里）：

- **不用 `JToken.Parse`**，改走 `JsonTextReader` + `JToken.ReadFrom`：好把
  `DateParseHandling.None` 关上（不关的话 `"2020-01-01"` 会被悄悄变成日期）、并且能查
  "第一个值后面还有没有东西"（`"1 2"` 报错）。
- **`JsonReaderException` 不在 `Fs` 的 catch 白名单里** —— 自己 catch 转 `RuntimeException`，
  否则它会当成"解释器内部错误"把程序打穿、`try` 接不住。报错带行列。
- 嵌套上限 64 层（防"容器包含自己"把栈转爆）。

**实例方法挂在类上**（`RegisterJsonMethods`：那九个 `Json.DefineMethod (…)`），
走 `DefineMethod` 落进 `Json` 的**实例表**：`j.Kind ()` ✓、`Json.Kind` 是干净的
「类型 'Type' 没有方法 'Kind'」✓（见上面「一个类对象有两张表」那条）。
`JsonVal` 自己那层留给用户挂的东西（`j.tag := 1` 只影响那一个值）——和 `ListVal`/`DictVal` 同形状。
（从前是**每个 Json 值一张表**：那时类那一侧拦不住，`Json.Kind` 会被解析成"类上的方法"，
调用时 `self` 是那个 `ClassVal`、`((JsonVal)s)` 当场炸成「`!!` 解释器内部错误」。
标记齐了这条理由就没了，而每个值一张表是要付钱的：`Get` / `At` 每取一个子节点就是一个新 Json 值。）

**`Copy ()` 得记一笔**：`JsonVal` 要进 `CopyValue` 的 switch（浅拷那棵树、拷一份自己那层的成员表
—— 用户挂的 `j.tag` 别丢），
不然会退化成普通 `ObjectVal`、`Token` 全丢（新加一个 `ObjectVal` record 都要记得这条 ——
`ListVal` / `SetVal` / `DictVal` 都在那儿）。

依赖：`Ravel.csproj` 里的 `Newtonsoft.Json` 13.0.3（第二个包依赖，另一个是 Spectre.Console）。

用例 `tests/lib/241_json.rav`。

## 跑外部命令（`cmd`）

`cmd "命令"` —— 走**系统 shell**(Windows 上是 `cmd.exe /c`,别处 `/bin/sh -c`),管道、
重定向、通配符都归它管,引擎不解析命令行。交回一张 dict:

```ravel
r := cmd "git status --short"
print ((r.Get "out").Trim ())
if { (r.Get "code") != 0; } { print ("失败了:" + (r.Get "err")); }
```

- **非零退出码不是错误** —— 程序失败是常事,原样放在 `code` 里交给调用方判断;
  真正起不来进程(比如 shell 本身)才抛 Ravel 错误。
- 两路输出**各起一个线程同时抽走** —— 顺序读会在大输出时死锁。
- 编码:**先按 UTF-8 试,不合法就退回控制台编码**。两边都常见(git / python 吐 UTF-8,
  `dir` 那类走控制台那套),而合法的 UTF-8 里出现 GBK 字节的概率极低,判据够用。
- 只做 syscall,**不做沙箱**(和文件那几条一个待遇):跑什么由调用方负责。

引擎里的实现是 `System.Cmd`(`Runtime/Builtins/SysCmd.cs`),`predefined.rav` 给全局别名
`cmd`。用例在 `tests/lib/229_io_fs.rav` 里。

### `System.Open`（交给系统去打开）

`System.Open x` —— 文件用默认程序打开(网页 → 默认浏览器),网址也一样。是"打开它"不是
"跑它",所以**不等它关掉**。REPL 菜单里那条「教程」用它开生成的静态站。

**为什么要引擎里出一条**:这件事每台机器做法都不一样(Windows `start` / macOS `open` /
Linux `xdg-open`),库里要判平台只能去嗅环境变量,判错了就是"什么都没发生"——静默失败。
.NET 那一条 `UseShellExecute = true` 正好是"让系统自己决定拿谁开"的跨平台说法,而且
**不走 shell**:路径里的空格、中文、`&` 都不用自己转义(拿 `cmd /c start` 去开就会)。

本机的目标**先自己看一眼在不在**,不在就报「没有这个东西」—— 交给系统的话回来的是它那句
"系统找不到指定的文件",那是跟着系统语言变的,而报错文案要被用例钉住。网址不查
(`http://…` 本来就不该在本地存在)。

## 网络（`lib/http.rav`，要显式 `using "http.rav"`）

**官方扩展只给"发一个请求"四条原语**,URL 拼装、响应对象、重试、上传体全是 `Http` 模块的事 ——
和正则、随机数一个分工。(这四条住在 `plugins/Ravel.Extensions.dll` 里,成员名叫 `Native.HttpReq`
那几条 —— 它们是这个库的本机半边,不是语言的一部分,所以和 `Hash` / `Regex` / `Sqlite` / `Random`
一起搬出了 `System`。见下面「插件」那节。C# 那一侧:`Ravel.Extensions/NetNative.cs`。)都**收一个 dict、交回一个 dict**:加字段不用改签名。
(`…Task` 那几条交回的是句柄,不是 dict —— 那是给任务用的。)
`lib/http.rav` 里同步那几条**一根毫毛没动**,想重叠就用同名 + `Task` 后缀的那一族
(`Http.GetTask` 那种),验收在 `tests/task/298_task_http.rav`。

| 原语 | 收 | 交回 |
|---|---|---|
| `Native.HttpReq` | `url` / `method` / `headers` / `body`(字节表)/ `bodyFile` / `follow` / `timeout` / `max` | `status` / `reason` / `headers` / `body`(字节表)/ `url`(跟完重定向停在哪儿) |
| `Native.HttpDownload` | 上面的 + `path` | 同上,但把 `body` 换成 `bytes`(写了多少) |
| `Native.HttpUpload` | 上面的 + `path` / `field` | 同上(multipart,文件**流式**发出去) |
| `Native.DecodeText` | 字节表 + 字符集名 | 字符串 |

**每条各有一副"交回句柄"的面孔**:`HttpReqTask` / `HttpDownloadTask` / `HttpUploadTask` 交回一个
`Waitable`,任务里 `group.Await` 它,网络在那边跑、别的任务接着跑。**两条路压的是同一个实现**
(底下全是 `await`,`HttpReq` 只是 `GetAwaiter().GetResult()` 一下),所以超时、封顶、连不上
那些话两条路**一个字不差** —— 判据那份也共用(`IsNetFault` / `HttpAsync`)。

- **正文一律是字节表**(0..255,和 `Encoding` / `Random.Bytes` / `Bits` 那套一个形状),
  解码是 Ravel 的事:响应对象 `Text ()` 按 `content-type` 里的 charset 解。
- **下载和上传全程流式**,几百 MB 也不经过 Ravel 堆;普通请求**默认 16 MB 封顶**
  (超了报错并指向 `Http.Download`)—— 堆是有上限的(测试里 256 MB)。
- 一处**进程级共享的 `HttpClient`**(连接池在它身上,一次请求一个新的话会把本机端口耗光),
  两台只差"跟不跟重定向"—— 那是处理器级的开关,没法按请求改。
- **错误消息不能说 .NET 那句**:那是跟着系统语言变的,而报错文案要被用例钉住。
  `Why()` 把常见的几类翻成固定中文(域名解析不了 / 连接被拒绝 / TLS 没过…),
  网址先自己验一遍(要 `http://` 或 `https://` 开头)。
- **4xx / 5xx 不是错误** —— 那是响应,自己看 `status`(`Http.Expect` 是"非 2xx 就抛"那条糖)。
- 老编码(GBK / Big5)靠 `CodePagesEncodingProvider`,**在 `Interpreter` 的静态构造里注册一次**
  (进程级的一件能力:扩展里那条 `DecodeText` 和测试的回环服务器都要它);
  认不出来的 charset 退回 UTF-8,不报错。
- 配套加的还有 `System.ReadBytes` / `System.WriteBytes`(字节表和文件来回 ——
  从前只有文本那三条,字节表存不下来也读不回来)和 `System.Sleep ms`(重试退避、限速)。

**用例**:`tests/net/268_http.rav`(离线:查询串、响应对象、报错文案)、
`tests/net/269_http_live.rav`(真发请求 —— 运行器按 `# net` 标记起一台**回环服务器**,
见下)。例子 `examples/http.rav` 打的是真网络。

### 服务端（`lib/httpd.rav`）

**自己拿 `TcpListener` 说 HTTP/1.1,不用 `HttpListener`** —— 和用例里那台回环服务器
(`Cli/Testing/LoopbackServer.cs`)同一个理由,那儿写着:后者在 Windows 上走 http.sys、
要管理员配 URL 前缀,而且响应字节不完全可控。自己写就没有权限问题,发出的每一颗字节
也都是我们说了算。

原语六条(`Ravel.Extensions/ServerNative.cs`),**收 dict 交 dict**,句柄照 `SqliteNative`
那套存号(监听器和连接都是要 Dispose 的本机东西,Ravel 值没有析构 —— 所以**没加新的值类型**):

| 原语 | 收 | 交回 |
|---|---|---|
| `Native.HttpListen` | `port`(0 = 随便挑)/ `host`(默认 `127.0.0.1`)/ `backlog` / `max`(正文上限)/ `cert` / `key` / `password` | `{handle, port, url}` |
| `Native.HttpAccept` | `handle` | **句柄** → `{conn, peer, tls}`;监听器关了交**空 dict** |
| `Native.HttpRead` | `conn` / `idle`(毫秒) | **句柄** → `{method, path, query, version, headers, body, keepAlive}`;对面关了交**空 dict** |
| `Native.HttpAnswer` | `conn` / `status` / `headers` / `body` | **句柄** → 写出去多少字节 |
| `Native.HttpClose` | 号(连接或监听器) | `()`,幂等 |
| `Native.HttpMakeCert` | `path` / `password` / `names` / `days` | 自签证书落成 PFX |

**服务端就是一个任务**:`Run ()` 内部是 `Tasks.Cycle`,`RunIn g` 是把 accept 循环挂进
**调用方那个组**(进程内自测只能走它 —— `Run` 会在任务里再开一个内层调度器,把线程占死)。
accept 循环一个任务,**一条连接再一个任务**;挂起点是 accept / 读 / 回,外加 handler 里
自己 `g.Await` 的。于是慢 handler 挡不住别的连接 —— `tests/301` 钉的就是这个
(**服务端自己数的并发峰值**,和 298 一个规矩:钉整数不钉墙钟)。

- **两条绕不开的约束**,都写进文档了:(1) 服务端和客户端在**同一条 OS 线程**上,
  进程内用**同步** `Http.Get` 打自己 = 死锁;(2) 客户端那个任务要是抛了错,服务器任务
  还在等连接,`Cycle` 就**永远不结束** —— 看着像卡死,其实是"没人喊停"。
- **"没有下一步了"交回的是空 dict,不是 `()`**。Ravel 里 `Void` 跟非 `Void` **不能比**
  (`() == ()` 倒是 `true`,见 3.2 "值跟值比、对象跟对象比"),判一句"交回来的是不是空"
  得绕成 `(typeof a) == System.Void`。空 dict 直接 `IsEmpty ()`,而**真请求 / 真连接
  永远不会是空的**(至少带 `method` / `conn`),认不错。更要紧的是:用 `()` 的话,库那边
  只能靠一个"我喊过停"的标志去收摊,而那个标志**反映不了"监听器因为别的原因没了"**
  —— accept 会一圈圈空转。现在退出条件就是交回来的东西,**监听器怎么没的都能收摊**。
- **handler 收 `(req g)` 两个参数**、**只交回 `Response`**。`g` 是伺候这个请求的那个调度组
  —— 和 `Tasks.Task (group) => …` 一个规矩,要等 IO 就 `g.Await`(不等也照写,参数占位)。
  早先的写法是收一个参数、再交回"响应**或**任务",由 `Settle` 那头现场 `is Tasks.ITask`
  嗅探:那个形状让 `Request` 背着调度器(不再是纯数据)、让签名说了假话,而且**藏了个
  真 bug** —— 交回的任务是等到 `Settle` 才跑的,它的失败落在 `Answer` 的 `try` **外面**,
  把连接任务整个带走、对面**干等到超时**。给 handler 发 `g` 之后"交回任务"这条路就没用了,
  两处一起没了。
- handler 里出的错**不把服务器带走**:`Answer` 接住,回 500 —— 等 IO 的错也在那个 `try` 里
  (它就在 handler 身上发生)。`tests/300` 的 `/boom` 与 `/boomTask` 钉住两种长相。
- **keep-alive** 默认开(HTTP/1.1 最多收 N 个请求,空转 `Idle` 毫秒让位);`Conns` / `Requests`
  两个计数就是给它看的 —— 一条连接收 N 个请求时,连接数比请求数小。
- **请求正文只认 `Content-Length`**,`Transfer-Encoding: chunked` 明确报错:猜错就是
  悄悄把正文读歪,那比报错难查得多。
- **正文有上限**(`max`,默认 16 MB,和客户端那条一个数)。**这个顶不能让对面说了算**:
  一句话 `Content-Length: 2000000000` 就是先分配两个 G 再读第一个字节。超了**一个字都不读**
  —— 读完再丢等于"对面说多大就读多大",顶就白设了 —— 交回一个带 `oversize` 的请求,
  库那头据此回 **413** 再断(不是在这儿抛:抛了对面只看到断线,连句人话都收不到)。
  **断之前要先把对面还在路上的正文收掉一小段**(`Drain`,最多 1 MB / 250 毫秒)—— 关一个
  "收下过东西却没读它"的连接,内核发的是 **RST** 而不是 FIN,对面于是看不到刚写出去的那句
  413,只看到"连接被重置"(`HttpClient` 直接抛异常)。**只收一小段**:真去把 2 GB 收完,
  上面那个顶就白设了 —— 所以是尽力而为,常见的那点超限够用,恶意的大肚子照样吃 RST。
- **静态目录挡 `..` 和反斜杠**(`SendFile`)—— 不挡的话 `/pub/../../secret` 把整个盘读出去。
  MIME 表在 Ravel 这边(策略),认不出的扩展名给 `application/octet-stream`。
- **TLS 的握手在 `HttpAccept` 里就地做完**,握手失败**只丢这一条连接**,不让 accept 循环塌掉。
  证书按 **PEM**(证书 + 私钥两个文件)或 **PFX** 读。
- 客户端为它加了一个 `insecure`(不校验证书),**只为连自签的**:`SocketsHttpHandler` 那开关
  是处理器级的、没法按请求改,所以宁可多养两台 `HttpClient`。`tests/302` 同时钉住两半 ——
  带 `insecure` 通、不带**照样报错**。

**用例**:`tests/300`(路由 / 查询串 / JSON / 静态 / 穿越防护)、`301`(keep-alive + 并发峰值)、
`302`(TLS)。例子 `examples/httpd.rav`(自己起一台、自己打自己、打完收摊)。

### 一个 URL 就是一个文件

`Http.Url "https://…"` 交回的东西有 `IFile` 那一套成员(而且 `impl (IFile Url { () })` 登记过),
于是**对着接口写的那几件直接能用** —— `Io.CopyTo` / `Io.Lines` / `Io.EachDir` 那些一个都不用改:

```ravel
Io.CopyTo (Http.Url "https://…/a.txt") (Io.File "a.txt")   # 抓下来存着
Io.Lines (Http.Url "https://…/log")                       # 一行行读远程日志(惰性)
Io.CopyTo (Http.Url "…") Terminal.Stdout                          # 直接倒进终端
```

- 每次 `Read ()` 都是**一次请求**(和 `Io.File.Read ()` 每次都去读盘一样,不藏缓存 ——
  藏了就会拿到旧内容还以为是最新的);
- `Write` / `Delete` 发 **PUT / DELETE**;`Append` 报错(HTTP 没有追写这回事,
  和 `Terminal.Stdout` 删不掉一个待遇);`Exists` / `Size` 走 **HEAD**(不下载正文,
  服务器不认 HEAD 就退回 GET);
- 落点那两条(`Download` / `Upload`)也收**磁盘条目**(`Io.File "a.zip"`),和 `Io.CopyTo dest` 一样两种都收。

依赖方向是 **网络 → 文件**(`http.rav` 里 `using "io.rav"`):URL 要去登记成文件那边的接口,
反过来不该由文件系统去认识 HTTP。

### 数据库（`lib/sqlite.rav`）

**连接不进 Ravel 堆**:那是个要 `Dispose` 的本机句柄,而 Ravel 值没有析构那一套 ——
所以引擎这边存着(一张 `号 → 连接` 的表),交给 Ravel 的是个 **int 号**,库里那个 `Db`
拿着号,`Close ()` 时还回来。五条原语都收这个号。

- `SqliteClose` 里那记 **`ClearPool` 不能省**:Microsoft.Data.Sqlite 默认**连接池**着,
  光 `Dispose` 只是还给池子 —— 库文件的句柄还开着,Windows 上接着 `DeletePath` 会报
  「正被占用」。(用例 `tests/274` 末尾那段真删文件,就是为了钉住这条。)
- `last_insert_rowid()` 是**问一句 SQL**,不是读属性 —— 这个版本的
  `Microsoft.Data.Sqlite` 没把它挂成属性。
- SQL 出错报 `ValueError`(消息是 SQLite 自己那句,英文但**稳定**),
  开关连接失败报 `IoError`。

### 测试怎么不飘:`# net` 与回环服务器

网络那几条用例**不碰真网络**(会断、会限流、对面内容会变),而是让运行器起一台
`Cli/Testing/LoopbackServer.cs` —— 自己拿 `TcpListener` 说 HTTP,给的是**定死的字节**
(`/hello` `/json` `/gbk` `/redirect` `/notfound` `/slow` `/big` `/flaky` `/echo` `/upload`)。
端口自己挑,基址塞进环境变量 `RAVEL_TEST_HTTP`;用例里**不打印 URL**,所以期望输出钉得住。
单独跑那条用例(不经运行器)会因为没接上服务器而不同 —— 和别的 golden 用例一样,只在
`ravel test` 下比对。

### 并发:调度器 + IO 都接上了

`lib/tasks.rav`(`Tasks` 模块)是那个调度器 —— 协作式任务,见上面文件地图那一格和
`tests/task/297_tasks.rav`。引擎这一侧只多了三样原语(`System.WaitAny` / `HandleValue` /
`Sleepable`,`Runtime/Builtins/SysTask.cs` + `Waitable` 值类型),**调度策略整个在库里**。

**会等的那些操作各有一副"交回任务"的面孔**,命名统一是**同步名 + `Task` 后缀**:

| 同步 | 交回任务 | 在哪儿 |
|---|---|---|
| `Http.Get` / `Request` / `Download` … | `Http.GetTask` / `RequestTask` / `DownloadTask` … | `lib/http.rav` |
| `cmd` | `cmdTask` | `lib/tasks.rav` 末尾(摆成全局,见那儿那段) |
| `Io.File.Read` / `Write` / `Append` | `f.ReadTask ()` / `WriteTask` / `AppendTask` | `lib/io.rav` |
| `System.ReadText` / `WriteText` … | `System.ReadTextTask` / … | `Runtime/Builtins/SysFiles.cs` |
| `System.Sleep` | `System.Sleepable` → `Tasks.After` | `Runtime/Builtins/SysTask.cs` |

**同步那几条一根毫毛没动**,而且和交回任务那条**压的是同一份实现** —— 所以失败文案
两条路一字不差(`Fs` / `FsAsync` 判的是同一份 `IsFsFault`;`Http` / `HttpAsync` 判的是
`IsNetFault`;`System.HandleValue` 对自己人抛的 `RuntimeException` 直接放行,不再裹一层
"那个活儿没干成")。

验收:`tests/task/298_task_http.rav`(**钉服务端并发计数,不钉墙钟** —— 三个 `/hold` 并发跑,
服务端报的峰值 ≥ 2 就是真重叠)、`tests/task/299_task_io.rav`(cmd 与文件读写)。
用法(`examples/tasks.rav`,八节:交替、收成、跑着再加、文件、错在哪现形、没人接的失败、
互相等、一个任务只能跑一遍)。

## 键与查找（`lib/keys.rav`；predefined 加载，所以 `IKey` 是全局名、`Keys` 直接可用）

**协议**：`Key ()` 交回"**我当键时等于什么**" —— 一个**值类型**（数 / 字符串）。它每个值都有：
引擎在 `Object` 上注册了一条，值类型交回**它自己**，别的类型当场报错（「`Key`: `A` 没有默认的键
（只有值类型有：数、字符串）。要拿它当键，就在类里写一条 `Key`，交回一个值类型的键」）。
所以 `5.Key ()` / `"a".Key ()` 拿起来就能用；`Keys.Of k` 就是它，外加一道
"交回的得是值类型"的确认（免得下游只报「dict.Set 的键得是值类型」看不出是自己那条 `Key` 的问题）。

**分两层，不是设计口味，是够不着**：`DictVal.Entries` 是 `Dictionary<RuntimeValue, RuntimeValue>`，
入口 `KeyArg` 把关，所以**引擎那一层只吃值类型键**（数 / 字符串）。原因在 .NET 的字典要一个
**同步**比较器：用户写的 `Key ()` 是 Ravel 函数（调它要推帧），而**查表那一步还发生在
`Dictionary` 的内部**，那儿更插不进帧。于是：

- 引擎那套叫 **`SysGet` / `SysSet` / `SysHas` / `SysRemove` / `SysGetOr`**（同步、只吃值类型；
  `Keys` / `Values` / `HasValue` / `Count` / `Clear` 不吃键，就留在这一层）；
- **日常敲的 `d.Get` / `d.Set` / `d.Has` / `d.Remove` / `d.GetOr` 是库注入的**：`lib/keys.rav`
  的 `IDict` 接口槽，先把键**规范**成值类型、再转到 `Sys*` 上。**名字腾出来是前提** ——
  成员查找先看类链，类链上还占着 `Get` 时接口槽**够不着而且静默**（`TraitSlot` 的规矩，
  见「接口」那一节）。形状照 `lib/iterator.rav` 那三条 `impl (IEnumerable list/set/dict …)`。

所以对象要当键，走的就是注入那条路：

```ravel
Rec ::= class { X: int = 0
    Key := () => { X; }        # 我当键时 = 我的 X（一个 int，值类型）
}
impl (IKey Rec { () })

d := {}
d.Set (Rec 1 "甲") "one"       # 走注入：先把 Rec 规范成 1，再进表
d.Get (Rec 1 "别的")           # "one" —— 另一个对象、X 一样 → 同一条
d.Keys ()                      # [1] —— 交回的是**规范键**，原对象没记着
d.SysGet (Rec 1 "甲")          # 报「dict.SysGet 的键得是值类型」—— 引擎那层不认对象
```

**`Keys.Of k`** 是那条"规范键"的公开名字（`k.Key ()`，外加一道"交回的得是值类型"的确认——
所以 `d.Set ([1 2]) "x"` 报的是「`Key` 要交回一个值类型的键」，而不是下游那句）。
注入的那五条槽就是 `Sys*` 外面套一层 `Of`，没有别的魔法。

**`IDict`** 就是那五条槽的形状（`Get` / `Set` / `Has` / `Remove` / `GetOr`）：普通 `dict` 靠**注入**
满足它（`impl (IDict dict { by Get = property … })`），`Keyed` 靠**自己的成员**满足它。
于是 `(d: IDict)` 这样的注解、`x is IDict`、对着接口写的通用代码，两种表都吃得住。
注意**不往里加** `Keys` / `Values` / `Count` / `Clear`：那些名字还在类链上，
按 `TraitSlot` 的规矩，类链上有名字时接口槽够不着而且**静默** —— 加了也只是死槽。

**`Keyed`**：以任意值为键、**还能把原对象拿回来**的表（两张 dict：规范键→原键、规范键→值）：
`Set` / `Get` / `Has` / `GetOr` / `Remove` / `Keys ()`（交回原对象）/ `Values ()` / `Count ()` /
`Clear ()`。普通 `dict` 在这一点上做不到 —— 表里只有规范键。

**两层的分工**：`Sys*` 是"表"本身（同值同键、对象进不去），`d.Get` / `d.Set` 那套是"日常的门"
（先规范再进表）。内建那些两边结果一样（键本来就是值类型），自己的类型只有注入那条路。

**现装那条路（`by Key = property …`）在这儿用不了** —— 和 `CompareTo` 同一个理由：
`Key` 这个名字 `Object` 上已经有了，成员查找是**类链先说话**，接口槽还没轮到。

用例 `tests/class/217_ienumerable.rav`。

## core 字段与 unsafe ()

`core` 修饰的字段读写都要先 `unsafe ()`。**`unsafe ()` 按作用域生效**：
它把**调用它的那个作用域**记进 `Interpreter.UnsafeScopes`，检查时沿作用域链往上找标记。
标记随调用帧走，函数返回后自然失效。

从前是个只增不减的计数器（`UnsafeDepth++`，没有对应的 `--`），于是**任何一次**
`unsafe ()` 之后整个程序余下的 core 检查全关掉：在某个无关函数里调一次、哪怕它早就返回了，
外部就能直接读写核心字段——core 修饰符等于不存在。

```ravel
f := () => { unsafe (); 0; }
f ()                 # 跟 secret 毫无关系
print c.secret       # 现在照样报「字段 'secret' 是核心字段，需要 unsafe」
```

读的门禁统一在 `BoxedValue.GateRead`（裸标识符和成员访问共用一份）——
从前 `StepIdent` 自己抄了一份、漏抄 core 那条，于是 `c.secret` 被拦住、
类体里直接写 `secret` 却读得到。

## 关键API

```ravel
# 类型反射
int.name          # "Integer"
int.parent        # ValueType
int <: ValueType  # true(类型之间:`<:` 子类型 / `:>` 父类型,两边都得是类型)
1 is ValueType    # true(值的说法;`isnot` 取反,`1.is` / `is.int` 也行)
T.GetImplements () # 这个类型**现在**实现了哪些接口(见「接口与实现」一节)
I.GetImplementors () # 反过来:**现在**哪些类型实现了这个接口
int.Subtypes ()   # [Every]  (Integer 没有自己的子类;子类型看 ValueType.Subtypes ())

# 对象
obj.Fields ()     # **这个值有哪些成员**:自己那层照单全收(字段和方法一视同仁),
                  # 再并上类型链上的方法名。只排掉 `this`(它是值自己,不是成员)
obj.Copy ()       # 浅拷贝
f.Body ()         # 函数/类的体(Block);没有体的给**空块** —— 类型恒定,不用 `()` 顶替
f.Scope ()        # 捕获作用域(Scope);类对象没有,同样给空 Scope
obj.field := v    # 定义/覆盖字段(不存在就新建);obj.field = v 只改已存在的
# 类型注解是个表达式(求值在定义处/参数创建处):
#   `x: int = v`            一个名字(可带 . 成员访问)
#   `x: (pick ()) = v`      括号里的任意表达式 —— 括号必需,否则 `f ()` 会和下一个参数撞
#   `x: int = default`      **按注解变成本类型的那个空值**(0/""/空表/空函数/空续延…)。
#                           `default` 的类型是底类型 `Every`(谁都收得下),所以那条
#                           "类型够不够宽"的捷径会把它整段放过 —— 求值器单独认了一下,
#                           不然 `n: int = default` 会把 `default` 原样存下来,`n + 1` 报错
obj.field += v    # 成员复合赋值(+= -= *= /= %=),左操作数只求一次
obj.+             # 取绑定好 self 的运算符函数(符号就是成员名)

# 打印函数:打出它的**源码**(AstPrinter 把 AST 还原回去)+ 柯里化**applied的实参**。
# 签名里是还等着的那几个,所以两者合起来就是完整形状:
#   <function add (x: int) => { x + 1; }>
#   <function (y: int) => { x + y; } applied x=3>


# 模块
ravel "M"
using "file.rav"
```

## 错误报告

运行时错误带**位置**和 **Ravel 层调用栈**，跨文件也分得清：

```
Error: 未定义的变量 'missing'
  --> tests/diag/152_error_report.rav:4:29
  4 | helper := (n: int) => { n + missing; }
    |                             ^
  调用栈 (2 层):
    在 tests/diag/152_error_report.rav:4:20
    在 tests/diag/152_error_report.rav:1:1
```

（取自 `tests/diag/152_error_report.rav` 的实际输出——它是精确比对用例，所以这段不会漂。）

- **抛出点只管给消息**(和**哪一族**)：180 多处 `throw new RuntimeException("...")` 不用操心位置。
  族 = `ErrorKind`(类型 / 名字 / 成员 / 下标 / 键 / 除零 / 断言 / 访问 / 参数 / 值 / IO / 正则),
  由 `HandToRavelHandler` 那**一处**翻成 Ravel 的对应类(见 `Runtime/BuiltinClasses.Errors.cs`)——
  引擎报错是引擎的事,分类也归引擎;库里再定义一遍就成了两处各管一半。没归类的落基类 `Exception`。
  位置和栈由求值器在冒泡时补（`Interpreter.Stack.cs` 的 `StepOnce` → `Locate`）——
  `catch (...) when (!ex.Located)` 保证只有**最内层**补，外层不覆盖成更外侧的位置。
- **帧链就是调用栈**，沿 `Parent` 收集即可，这是显式帧栈架构白捡的好处。
  只取 `BlockExecFrame`（每次块执行 = 一次调用），节点帧只是栈帧内部的步骤；
  最多列 12 层（深递归时帧链可能有几十万层）。
- **文件名挂在 `BlockExpr.Source` 上**（主文件 / `using` 的模块 / `eval` 片段），
  节点本身只有行列。调用栈里每层用**块**的位置（≈ 函数定义处），
  出错位置则精确到当前求值的节点。
- 渲染在 `Runtime/ErrorReport.cs`：路径取相对 cwd、分隔符统一 `/`——报告短，
  且让 `tests/diag/152_error_report.rav` 能精确比对（不是 `# expect-error` 那样只看前缀）。
- **语法错误也走这份渲染**（`SyntaxException`，见 `RuntimeValue.cs`）：位置来自 token、
  没有调用栈，但同样画 `--> file:line:col` 和插入符（`tests/diag/152_error_report.rav`）。
  它以前是个裸的 `System.Exception`，于是 CLI / 测试运行器分不清「用户代码写错了」
  和「解释器有 bug」——两者都落在同一个 `catch (Exception)` 里。现在三个类型各归各位：
  `RuntimeException`（求值期）/ `SyntaxException`（词法语法期）/ `ExitException`（exit 解栈），
  落到兜底 `catch (Exception)` 的一律打 `!! 解释器内部错误`。
- **`eval` 里的语法错误对 Ravel 层是可接的**：`StepOnce` 另有一个 `catch (SyntaxException)`，
  把它转成 `RuntimeException` 再走同一套 handler 分发。不转的话
  `Ex.Try { eval "1 +" } {...}` 不生效——handler 只认 `RuntimeException`——
  eval 一段用户输入就能撂倒整个程序。没人接时仍抛原异常，CLI 按语法错误渲染。
  `eval` 的块带合成名 `<eval>`（`ErrorReport.ShortPath` 认这种虚拟名，不当路径解析）。

## 诊断:是不是忘了调用?（`--warn` / `System.WarnForgotCall`）

`lib/predefined.rav` 里 `if` 是**三参**库函数，`if { c } { t }` 少给最后一块**不报错** ——
只交回一个**半成品函数**，然后被当普通语句丢掉，那一整块静默不跑。这类坑靠读代码很难看见，
所以有一个开关专门盯它：语句的值求出来是个函数、而且这份值**没被用掉**，就在 stderr 上提醒一句
（位置 + 源码行 + 插入符，和错误报告同一套渲染 —— `ErrorReport.Warning`）。

- **开**：CLI 的 `--warn`（`ravel --warn 脚本.rav`，REPL 也一样）；脚本里随时开关：
  `System.WarnForgotCall true`。`ravel --warn test` 则把整个用例库当样本过一遍筛子
  （只开开关、**不动比对** —— 警告走 stderr，比的是 stdout）。
- **只管两种形状**（其余都是这门语言的常态，不是错）：
  1. **半成品** —— `FunctionVal.IsPartial`：调了，实参没给够。`if { c } { t }`、
     `assert 条件`（消息没给）、`true { A }`（第二个块没给）、`Point 3`（还差一个）。
  2. **光写了个名字，压根没调用**：`foo` / `obj.method` 这么一句。
- **只认不是最后一条的语句**：块的值就是最后一条，`() => { … }` 那种"交回一个函数"是正常写法。
- **同一个位置只报一次**（`_warnedForgotCall`）：循环体里那一句跑几千遍也只说一句。
- **`IsPartial` 是专门为这件事加的一枚标**，打在三处：柯里化的 lambda 交回内层时
  （`BlockExpr.Curried` → `BlockExecFrame.Curried` → `CallInto`）、内置的柯里化函数交回内层时
  （`FunctionVal.From` 的 `Partial`）、以及本身就是半成品的 `PartialCtor` / `PartialBool`。
  **别拿 `LambdaVal.Applied ()` 当这个标**：那一条走的是"闭包捕获了定义处的实参"，
  在一个被调用过的函数里定义的 lambda 一律非空（`Try` 里的 `mine` 就是），分不出半成品。
- **不响的两处**（都是为了不误报）：赋值语句（`x = f` / `a.b := f` 的值是"赋进去的那个"
  这个副产物，事已经做了 —— 见 `OperatorSymbols.AssignOps`）；完整的函数流转
  （`body ()` 交回循环体的值、`HandlerStack.Remove 0` 交回被摘掉的那个 handler ——
  块的值一路飘出来落在哪一句上纯属碰巧）。
- 排掉的 `bool` / 续延和 `RuntimeValue.IsClosure` 排掉的那两样**不是一回事**：那边排的是
  "数据值"（bool、类对象），这边排的是"可调用但不是等着实参的东西"；类对象要留着
  （`Point` 光写个名字正是"忘了调用"）。

用例见 `tests/diag/264_warn_forgot_call.rav`（顶上的 `# warn` 是给运行器的标记：把 stderr
一起收进比对里，否则警告一条也钉不住）。

## 两个「类型说有、值却没有」的坑

这两处都栽过，加新类型/新内建时留意：

- **`Bool <: Function` 要求 `BoolVal : FunctionVal`**。true/false 可调用
  （`true {a} {b}`），类型表里挂在 Function 下；值这边不跟上，从 Function 继承来的方法
  拿到 self 是 BoolVal，`(FunctionVal)s` 直接抛 C# 的 InvalidCastException——
  它不是 RuntimeException，Ravel 的 try 接不住，程序被打掉。`BlockVal` 一直是
  `: FunctionVal(...)` 这么接的，`BoolVal` 是漏掉的那个。
- 反过来，**不能拿 `is FunctionVal` 当「这是方法/闭包」的判据**，因为落在 Function
  类型下的有两种**数据值**：`BoolVal`（`Bool <: Function`）和 `ClassVal`（类对象）。
  用 `RuntimeValue.IsClosure`：`Fields ()`、`print obj`、
  `StepClassInit`/`StepCtorApply` 的半成品构造器判断都走它。用错会让
  `flag: bool = true` 从 `Fields ()`/`print obj` 里消失，会让
  `init := () => { true; }` 的对象被当成半成品构造器交出去，
  还会让 `class {…}` 拿到一个 `PartialCtor` 而不是类。

## 性能:时间花在哪儿

一句话:**单步很便宜,步数很贵**。用 CLR 侧的步数计数器量过(加个 `StepOnce` 计数器就行):

| 量 | 数 |
|---|---|
| 一次 `StepOnce`(含帧分配) | ~0.24 µs |
| `xs.Each (…)` 走 2 万个元素(`List`,`SeqMethod` 那条引擎路径) | ~14 µs/元素 ≈ 60 步 |
| `[1..N].Each (…)`(`Range` 走 `IEnumerable` 的**库**实现:foreach + Generator 光标) | ~175 µs/元素 ≈ **754 步** |
| `while { i < N; } { sum = sum + 1; i += 1; }` 一轮 | ~45 µs ≈ **190 步** |

所以引擎本身没有"慢函数"可抠(步进循环就是个 switch + 一次帧拷贝),**开销都在
"一个动作要跑多少步"上** —— 而步数来自库:控制流(`if`/`while`/`foreach`)、
序列默认实现、`Generator` 光标全是 Ravel 写的,每一次都是完整的调用帧。

于是优化只有两个方向,都得先想清楚语义:

1. **少跑几步**(库那一侧):`while` 每轮都要拼一遍 `"while 的条件必须是 bool，得到 " + string (typeof cond)`
   这条消息(字符串拼接在热路径上);把它改成"只在失败时才拼"能省掉约 15% 的循环时间。
   `Range` 的 `GetEnumerator` 走 `Generator`,而 `Generator` 是续延实现的光标 ——
   给它一个引擎原生的枚举器能把 `Range` 那条路拉近 `List` 那条(差 12 倍)。
   两条都改的是**可观察的行为边界**(错误消息的构造时机 / `GetEnumerator ()` 交回什么),
   所以没做 —— 要做先想清楚值不值。
2. **每步更便宜**:帧是**不可变**的(`callcc` 要能把帧链整个拍下来),所以每步一次 record 拷贝 +
   一个 `RList` 节点,这部分动不了(除非改成"可变帧 + 捕获时快照",那是个大改)。
   已经抠掉的是这几处:取成员的类链缓存(`MemberView.ClassChain`)、
   字面量只解析一次、`BoxedValue` 改结构体、运算符不预先绑 `self`。

## 测试

`tests/` 下的 golden test。`# expect-error` 预期异常，`# --- expected ---` 预期输出，`# todo` 等待实现
（**当前没有 todo 了**：最后 4 个是元类，随「类就是 ObjectVal」那一轮落地）。计数不写在这里——跑 `dotnet out/ravel.dll test` 看，或者按目录数。早期把基础特性合并过几个大文件（`01_core`(基础/运算符/列表/位运算/_)·`11_control_flow`·`13_functions`·`40_callcc`·`75_modules`·`98_types`·`99_collections`），后面按特性一个用例一个文件。expect-error 与 todo 因语义必须独立。

**挑子集跑**（迭代时别每次全量）：

```
ravel test http           # 只跑**相对路径里带 `http`** 的（不区分大小写，**子目录名也算**）
ravel test 29 30          # 给几个就是"含其中任意一个"
ravel test --fast         # 跳过带 `# slow` 的
```

挑选词打在**相对路径**上，所以用例按文件夹分好之后 `ravel test http` 挑的就是那一摞 ——
运行器本来就递归子目录（`SearchOption.AllDirectories`）。一个词都没命中的话它明说
"没有一条对得上"，不会让人对着"0 passed"发愣。

**按类型分文件夹**（用例本身也照这个摆）：

| 目录 | 装什么 |
|---|---|
| `tests/lang/` | 语言本身：语法、类型、控制流、函数、续延、运算符、字符串 |
| `tests/class/` | 类 / 接口 / 元类 / 属性 / 成员表 |
| `tests/module/` | 模块与 `using`（含互相 `using` 那几对：129↔130、75→132、95→129） |
| `tests/diag/` | 报错与诊断长什么样（`# expect-error` / `# warn` 那批） |
| `tests/lib/` | 标准库（io / regex / json / time / text / 表格 / zip / xml …） |
| `tests/net/` | 网络：`Http` 客户端 + `Httpd` 服务端 |
| `tests/task/` | 协作式任务 |

**互相 `using` 的用例必须待在同一个文件夹** —— `ReferencesPath = ["tests/<那个目录>/"]` 指的是
目录，跨目录就得写两格。搬文件时**顺带要改两样**：`tests/NNN_x.rav` 那类完整路径（全仓库 90 处），
以及 `ReferencesPath` 里那个 `tests/`；还有几处用例的**期望输出里钉着自己的路径**
（`--> tests/152_error_report.rav:4:29` 那种，18 个文件），它们跟着同一张改名表走。

`# slow`（和 `# net` / `# warn` 并列，`GoldenTestRunner` 顶上有表）标的是**跑起来费时间**的，
不是"不重要"：全量照跑，`--fast` 才跳。目前只有 `40_callcc`（近 600 行续延，一条 20 秒，
占全量的一半）。**跳过了会在汇总那一行说一声** —— `--fast` 下的全绿不等于全量绿。

- `expect-error` 只看 `output.StartsWith("Error:")`，所以**解释器自己漏出来的 C# 异常不算数**：
  `CaptureOutput` 给非 `RuntimeException`/`SyntaxException`/`ExitException` 的异常加了
  `!! C# 异常 …` 前缀，它不以 `Error:` 开头，会直接把用例判 FAIL。加这个前缀当场就抓到过 6 个
  漏出的异常（其中 4 个是语法错误没有类型、2 个是 exit 没被单独接住）——现在两者都有正式类型了。
