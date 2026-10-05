# 十三、HTTP 服务端

`Httpd` 是 `Http` 的对面。**要显式引用**:`using "httpd.rav"`。

服务端和客户端跑在**同一条 OS 线程**上,所以本章每个例子里,服务器和客户端是**同一个
`Tasks.Cycle` 里的两个任务** —— 进程内只能这么打自己。用同步的 `Http.Get` 会把唯一那条
线程占死,服务端的任务再也轮不上。

端口一律绑 `0`(让系统挑一个),真实端口在 `srv.Port` —— 例子**不打印它**(每次都变)。

## 13.1 起一台

| 写法 | 意思 |
|------|------|
| `Httpd.Server port` | 起一台,返回服务器对象(`port` 给 `0` 就是随便挑) |
| `srv.Route method path handler` | 加一条路由,**整条路径相等**才命中 |
| `srv.RunIn g` | 把 accept 循环挂进**你那个**调度组 |
| `srv.Stop ()` | 关掉监听器,循环收摊 |

#### 实例

```ravel
using "httpd.rav"
using "tasks.rav"
using "http.rav"

srv := Httpd.Server 0
srv.Route "GET" "/hi" (req g) => { Httpd.Text "你好"; }
base := "http://127.0.0.1:" + (string (srv.Port))

Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { srv.RunIn g; })
    (Tasks.Task (g: Tasks.TaskGroup) => {
        r := g.Await (Http.GetTask (base + "/hi"))
        print ((string (r.status)) + " " + (r.Text ()))
        srv.Stop ()
    })
]
```

执行以上程序会输出如下结果：

```
200 你好
```

注意:`Run ()` 是 `RunIn` 包一圈 `Tasks.Cycle` —— 给"这台服务器就是整个程序"那种用
(它阻塞到 `Stop ()`)。**别在任务里调 `Run ()`**:它会在你那个调度器里再开一个内层的,
内层把线程占住,外层那些任务就再也轮不上了。

## 13.2 路由与响应

handler 收**两个**参数 —— `req`(这次请求)和 `g`(伺候它的那个调度组)—— 交回一个 `Response`。

`g` 和 `Tasks.Task (group) => …` 里那个是同一个东西:要等 IO 就用它(`g.Await …`)。不等也照写,参数得占位。

响应用这几个构造器造:

| 写法 | 交回 |
|------|------|
| `Httpd.Text s` / `Httpd.Text code s` | `text/plain`,可带状态码 |
| `Httpd.Html s` / `Httpd.Html code s` | `text/html` |
| `Httpd.Json v` | 一个 Ravel 值当 JSON 发出去 |
| `Httpd.Bytes type s` | 自己给类型 |
| `Httpd.Redirect to` | 302 |
| `Httpd.NoContent ()` | 204 |
| `Httpd.NotFound ()` | 404 |
| `Httpd.File g path` | 磁盘上一个文件(读盘走 `g`,**不占线程**) |

要改状态码或者补个头就 `|>` 串(`With` / `Header` 都交回自己):

```ravel
Httpd.Text "没有" |> .With 404
Httpd.Text "你好" |> .Header "x-tag" "hi"
```

#### 实例

```ravel
using "httpd.rav"
using "tasks.rav"
using "http.rav"

srv := Httpd.Server 0
srv.Route "GET" "/t" (req g) => { Httpd.Text "文本"; }
srv.Route "GET" "/h" (req g) => { Httpd.Html "<b>粗</b>"; }
srv.Route "GET" "/j" (req g) => { Httpd.Json {"n"-> 1}; }
srv.Route "GET" "/e" (req g) => { Httpd.Text 404 "自定义的没有"; }
srv.Route "GET" "/r" (req g) => { Httpd.Redirect "/t"; }
base := "http://127.0.0.1:" + (string (srv.Port))

Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { srv.RunIn g; })
    (Tasks.Task (g: Tasks.TaskGroup) => {
        print ((g.Await (Http.GetTask (base + "/t"))).Header "content-type")
        print ((g.Await (Http.GetTask (base + "/h"))).Text ())
        print ((g.Await (Http.GetTask (base + "/j"))).Text ())
        print (string ((g.Await (Http.GetTask (base + "/e"))).status))
        print ((g.Await (Http.GetTask (base + "/r"))).Text ())
        srv.Stop ()
    })
]
```

执行以上程序会输出如下结果：

```
text/plain; charset=utf-8
<b>粗</b>
{"n":1}
404
文本
```

注意:最后一行是重定向**跟到底**的结果(`/r` → 302 → `/t`)。

注意:没命中的路径给的是 `404 没有这条路由`;命中但 handler 里抛了错给 **500**,而且
**不把服务器带走** —— 接住、回一个 500,连接照常收摊。

## 13.3 请求对象

| 成员 | 是 | 意思 |
|------|-----|------|
| `req.Method` `req.Path` `req.Version` | 字段 | 方法 / 路径(已去掉查询串)/ `HTTP/1.1` |
| `req.Query` `req.Headers` `req.Body` | 字段 | 查询串 / 请求头(名一律小写)/ 正文字节表 |
| `req.Param name` | 方法 | 查询串里一个值(没有给空串) |
| `req.Header name` | 方法 | 一个请求头(没有给空串) |
| `req.BodyText ()` | 方法 | 正文按 charset 解成文本 |
| `req.Json ()` | 方法 | 正文当 JSON 解 |
| `req.Peer` | 字段 | 对面的地址 |

**`Body` 是字段** —— 写 `req.Body ()` 会变成"调用一个列表",报「值 \[…\] 不是函数」。

#### 实例

```ravel
using "httpd.rav"
using "tasks.rav"
using "http.rav"

srv := Httpd.Server 0
srv.Route "POST" "/in" (req g) => {
    Httpd.Json {"路径"-> req.Path "方法"-> req.Method "参数"-> (req.Param "q") "内容"-> (req.BodyText ())};
}
base := "http://127.0.0.1:" + (string (srv.Port))

Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { srv.RunIn g; })
    (Tasks.Task (g: Tasks.TaskGroup) => {
        r := g.Await (Http.PostTask (base + "/in?q=%E4%B8%AD") "喂")
        print ((r.Json ()).Text ())
        srv.Stop ()
    })
]
```

执行以上程序会输出如下结果：

```
{"路径":"/in","方法":"POST","参数":"中","内容":"喂"}
```

注意:查询串里的百分号编码已经解开了(`%E4%B8%AD` 是 `中`)。

## 13.4 静态目录

`srv.Static prefix dir` 把磁盘上一个目录挂在 `prefix` 底下,类型按扩展名给。

#### 实例

```ravel
using "httpd.rav"
using "tasks.rav"
using "http.rav"

System.WriteText "tut_pub.css" "body { color: red }"
srv := Httpd.Server 0
srv.Static "/pub" "."
base := "http://127.0.0.1:" + (string (srv.Port))

Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { srv.RunIn g; })
    (Tasks.Task (g: Tasks.TaskGroup) => {
        r := g.Await (Http.GetTask (base + "/pub/tut_pub.css"))
        print ((r.Header "content-type") + " " + ((r.Text ()).Trim ()))
        print (string ((g.Await (Http.GetTask (base + "/pub/../secret"))).status))
        srv.Stop ()
    })
]

System.DeletePath "tut_pub.css"
```

执行以上程序会输出如下结果：

```
text/css; charset=utf-8 body { color: red }
404
```

注意:**`..` 和反斜杠一律挡掉** —— 不挡的话 `/pub/../../secret` 就把整个盘读出去了。

注意:表里没有的扩展名给 `application/octet-stream`。

## 13.5 一个慢 handler 挡不住别的连接

服务器就是一个任务,**一条连接再一个任务** —— 所以 handler 里 `g.Await …`
挂起的时候,调度器去伺候别的连接。

#### 实例

```ravel
using "httpd.rav"
using "tasks.rav"
using "http.rav"

live := 0
peak := 0

srv := Httpd.Server 0
srv.Route "GET" "/hold" (req g) => {
    live += 1
    if { live > peak; } { peak = live; } { 0; }
    g.Await (Tasks.After 200)
    live -= 1
    Httpd.Text "ok";
}
base := "http://127.0.0.1:" + (string (srv.Port))

Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { srv.RunIn g; })
    (Tasks.Task (g: Tasks.TaskGroup) => {
        rs := []
        foreach [1 2 3] (i: int) => { rs.Add (Http.GetTask (base + "/hold")); }
        g.Await (Tasks.All rs)
        print (string peak)
        srv.Stop ()
    })
]
```

执行以上程序会输出如下结果：

```
3
```

注意:三个 handler 各自挂起 200 毫秒,**三个是叠着的**(峰值 3)而不是排队的(那样是 1)。

注意:**返回类型只有一种**(`Response`)。要等 IO 就在 handler 里 `g.Await` —— 读盘不占线程
这件事,`Httpd.File g path` 就是这么做的(它自己在那里面等)。

## 13.6 keep-alive

`HTTP/1.1` 默认一条连接收多个请求,空转 `srv.IdleFor ms` 毫秒(默认 15 秒)没动静才收摊;
客户端说 `Connection: close`、或者 HTTP/1.0 来的,回完就断。

`srv.Conns` / `srv.Requests` 两个计数就是给它看的:**连接数比请求数小,就是复用上了**。

注意:**请求正文只认 `Content-Length`**。碰上 `Transfer-Encoding: chunked` 会**明确报错**,
不猜 —— 猜错就是悄悄把正文读歪,那比报错难查一百倍。

## 13.7 正文上限

`max` 是一次请求正文的**上限**,默认 16 MB(和客户端那条一个数)。超了的请求**正文一个字都不读**,直接回 413 再断。

```ravel
srv := Httpd.Server {"port"-> 8080 "max"-> 65536}
```

#### 实例

```ravel
using "httpd.rav"
using "tasks.rav"
using "http.rav"

srv := Httpd.Server {"port"-> 0 "max"-> 1024}
srv.Route "POST" "/in" (req g) => { Httpd.Text ("收到 " + (string ((req.Body).Count ()))); }
base := "http://127.0.0.1:" + (string (srv.Port))

Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { srv.RunIn g; })
    (Tasks.Task (g: Tasks.TaskGroup) => {
        ok := g.Await (Http.PostTask (base + "/in") "小东西")
        print ((string (ok.status)) + " " + (ok.Text ()))

        st := 0
        why := ""
        try { r := g.Await (Http.PostTask (base + "/in") ("x" * 4000)); st = r.status; why = r.Text (); } (e: Exception) => { st = -1; }
        print ((string st) + " " + why)

        srv.Stop ()
    })
]
```

执行以上程序会输出如下结果：

```
200 收到 9
413 请求正文太大（超过 1 KB，它说 3 KB）
```

注意:**这个顶不能让对面说了算**。少一条上限,一句 `Content-Length: 2000000000` 就能让这台先分配两个 G 再开始读第一个字节 —— 那是最省事的一种打垮法。

注意:超了之后**那条连接不再复用** —— 后半个正文还在路上,接着读就是又回到那条老路。

## 13.8 HTTPS

给 `cert` 就是 HTTPS。证书按 **PEM**(证书 + 私钥两个文件)读,只给一个文件就按 **PFX**
(`password` 是它的口令)。

自签一张(`Native.HttpMakeCert`,**只为本地试一把**):

```ravel
Native.HttpMakeCert {"path"-> "local.pfx" "password"-> "pw" "days"-> 30}
srv := Httpd.Server {"port"-> 0 "cert"-> "local.pfx" "password"-> "pw"}
```

#### 实例

```ravel
using "httpd.rav"
using "tasks.rav"
using "http.rav"

Native.HttpMakeCert {"path"-> "tut_local.pfx" "password"-> "pw" "days"-> 30}
srv := Httpd.Server {"port"-> 0 "cert"-> "tut_local.pfx" "password"-> "pw"}
srv.Route "GET" "/tls" (req g) => { Httpd.Text "加密的你好"; }
base := "https://127.0.0.1:" + (string (srv.Port))

Tasks.Cycle [
    (Tasks.Task (g: Tasks.TaskGroup) => { srv.RunIn g; })
    (Tasks.Task (g: Tasks.TaskGroup) => {
        r := g.Await (Http.RequestTask {"url"-> (base + "/tls") "insecure"-> true})
        print ((string (r.status)) + " " + (r.Text ()))

        # 不带 `insecure` 的照样校验证书 —— 自签的那张谁都不信
        refused := false
        try { g.Await (Http.RequestTask {"url"-> (base + "/tls")}); } (e: Exception) => { refused = true; }
        print refused

        srv.Stop ()
    })
]

System.DeletePath "tut_local.pfx"
```

执行以上程序会输出如下结果：

```
200 加密的你好
true
```

注意:`insecure` **只为连自签的证书**,拿它去连真网站等于把中间人攻击的门开着。

注意:客户端的 `insecure` 和 `srv.IdleFor` 一样,都是**连接建立之前**就得定下来的东西
—— 那是处理器级的开关,没法按请求改。
