# 9、模块

## 9.1 创建模块

一个文件把自己写成模块，用 `ravel "名字"` 开头、`ravel ""` 收尾（中间那些定义就落在那个模块里）：

```ravel
ravel "MyMath"
pi := 3.14
ravel ""
print (MyMath.Pi)
```

## 9.2 导入文件

```ravel
using "other.rav"
```

**后缀可以省** —— 不歧义的时候（`.rav` 和 `.dll` 是两种不同的东西）：

```ravel
using "seqs"              # ≡ using "seqs.rav"
using "Ravel.Extensions"  # ≡ using "plugins/Ravel.Extensions.dll"
```

搜索目录是 **当前目录 → `lib/` → `plugins/` → …解释器自己那一份的旁边**，`ReferencesPath` 里加的排在最前面。

找的时候三种写法（原样 / 补 `.rav` / 补 `.dll`）各找一遍，**每种写法只认第一个搜到的目录**（同名文件在两个目录里都有 = 先到先得，和 `PATH` 一个意思）。但**两种写法同时命中就报错**，不替你挑：

```
`using "amb"` 有歧义：`amb.rav` `amb.dll` 都对得上 —— 把后缀写全再试
```

写全了照旧，而且**省不省认的是同一个文件** —— `using "seqs"` 之后再来一句 `using "seqs.rav"` 不会加载两遍。

注意：`using` **只加载一次**，循环引用报错。

## 9.3 System 模块

内置模块，解释器启动时创建，装的是**语言本身要的**（类型、控制流、`eval`、反射）和**进程边界**（输出、文件、环境变量、子进程、时间）—— 一句话：是"这个语言"和"这台机器"，不是"某个库"。

| 写法 | 交回 |
|------|------|
| `System.Integer` / `System.String` / `System.Bool` | PascalCase 的类型名 |
| `System.WriteLine "hello"` | 输出 |
| `int` / `string` / `object` | 小写别名（在 `predefined.rav` 里定义）|

`Ravel` 是**所有模块的类对象** —— 模块是它的**实例**、不是子类，所以"这是不是个模块"就一句：

#### 实例

```ravel
using "math.rav"
print (Math : Ravel)
print (5 : Ravel)
```

执行以上程序会输出如下结果：

```
true
false
```

那六个库的本机半边（`Hash` / `Crypto` / `Http` / `Regex` / `Sqlite` / `Random`）不在 `System` 里，在官方扩展的 `Native` 模块。

### 作用域（`System.Scope`）

`currentScope ()` 给你**当前这一层作用域**（在顶层就是模块那一层 —— 全库所有全局名都在这层里，所以 `Count ()` 是三位数）。`f.Scope ()` 是函数的捕获作用域，`o.MemberScope ()` 是对象的成员表，都交回同一个 `Scope` 类型。

| 方法 | 交回 |
|------|------|
| `s.Push ()` | 往下开一层（新的 `Scope`；`Keys` / `Count` 那些说的都是**这一层**）|
| `s.Define "n"` | **柯里化**：再喂一个**类型**才落下 —— 本层没这个名字就按这个类型新建，有就改值 |
| `s.Lookup "n"` | 沿链找那个变量，交回**挂它的那个 property**（读 `.Get ()`、写 `.Set v`、`.Attrs ()` 看修饰符）|
| `s.Remove "n"` | 删**本层**的名字（找不到不报错）|
| `s.Keys ()` / `s.Values ()` | 本层的名字 / 值（都是 list）|
| `s.Count ()` | 本层名字个数 |
| `s.Variables ()` | 名字 → 值的 **dict**（跳过 `this` / `block`）|

`Scope` 自己**不是** `IDict` / `IEnumerable` —— 那两条 `impl` 写在 `lib/keys.rav` / `lib/iterator.rav` 里，
所以 `s.Has "n"` / `s.Get "n"` / `s.Foreach …` / `s.Where …` 要先把它们 `using` 进来
（枚举出来的是**变量的值**，和 `Foreach` 一个 dict 一个口径；要名字用 `Keys ()`）。

#### 实例

```ravel
using "keys.rav"
readonly b := 2
s := currentScope ()
print ((s.Lookup "b").Attrs ())
print (s.Has "b")
print ((s.Count ()) > 100)
```

执行以上程序会输出如下结果：

```
[readonly]
true
true
```

## 9.4 Math 模块

**要显式引用**；`using "math.rav"` 之前 `Math` 不是一个名字（报「未定义的变量 'Math'」）。

#### 实例

```ravel
using "math.rav"
print (Math.Pi)
print (Math.Sin 0)
print (Math.Sqrt 16)
print (Math.Round 2.5)
print (Math.Abs (-5))
print (Math.Square 5)
print (Math.Deg Math.Pi)
```

执行以上程序会输出如下结果：

```
3.141592653589793
0
4
3
5
25
180
```

注意：`Math.Round 2.5` 是 `3` —— **四舍五入**，不是银行家舍入。

注意：`Math` 收任何数值（int / real / bigint / fraction），内部按 double 算；三角函数收**弧度**。

## 9.5 命令行参数与环境变量

两样都是**进程外面给进来**的东西，都在 `System` 里，和文件那批一个规矩：只做 syscall。

```bash
dotnet out/ravel.dll greet.rav 小明 --loud
```

`System.Args ()` 给的是**脚本名之后**那些（全是 `string`）。**解释器自己不读命令行** —— 谁把脚本跑起来的、命令行长什么样，那是 CLI 的事，所以 REPL 和 `ravel test` 里它一直是 `[]`。

环境变量五条：

| 写法 | 意思 |
|------|------|
| `System.Env "HOME"` | 没有这个变量就**报错** |
| `System.EnvOr "HOME" "/tmp"` | 没有就给默认值（不报错的那条）|
| `System.SetEnv "MODE" "dev"` | 只动**本进程**那份 |
| `System.UnsetEnv "MODE"` | 删掉；删一个本来就没有的不算错 |
| `System.EnvAll ()` | 名字 → 值（按名字排序）|

#### 实例

```ravel
System.SetEnv "RAVEL_DEMO" "dev"
print (System.Env "RAVEL_DEMO")
print (System.EnvOr "RAVEL_NOPE" "默认值")
System.UnsetEnv "RAVEL_DEMO"
print (System.EnvOr "RAVEL_DEMO" "没了")
```

执行以上程序会输出如下结果：

```
dev
默认值
没了
```

注意：**空串等于删掉**（.NET 那套 `SetEnvironmentVariable` 的规矩）—— `SetEnv "X" ""` 之后 `Env "X"` 照样报错。

注意：只写**本进程**那份。写用户级 / 机器级的环境变量是"装环境"，不该由一句赋值顺手做掉。

## 9.6 给自己的库写断言（`Test`）

`Test` 是给"写在 `.rav` 里、想跟着程序一起跑"的检查用的（仓库自己的用例是另一套：golden 文件）。**要显式引用**。

#### 实例

```ravel
using "test.rav"
Test.Check "1 加 1 得 2" { assert (1 + 1) == 2 ("1+1 应该是 2"); }
Test.Equal "自己的检查" [1 2] [1 2]
Test.Fails "除零确实报错" { 1 / 0; }
Test.Report ()
```

执行以上程序会输出如下结果：

```
ok 1 - 1 加 1 得 2
ok 2 - 自己的检查
ok 3 - 除零确实报错
3 passed, 0 failed
```

| 写法 | 意思 |
|------|------|
| `Test.Check name body` | 跑完**不报错**就算过（名字要照着这个写）|
| `Test.Equal name actual expected` | 显示形式一样就算过 |
| `Test.Fails name body` | 反之：必须报错才算过 |
| `Test.Report ()` | 汇总；**有没过就抛出去** |

注意：报错在这儿是**值**（`Expect argCount f` 把一次调用包成 `Expected`），所以"怎么断言"和"报错怎么办"是同一件事。`Check` 顺便把"过没过"交回，条件那一格能直接用。

注意：`Report ()` 抛出去 ⟹ CLI 以非零码结束，所以 `dotnet out/ravel.dll mytests.rav` 可以直接当 CI 的一道关卡。每条的记录也留着：`Test.Results` / `Test.PassCount` / `Test.FailCount`。
