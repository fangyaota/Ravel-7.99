using Microsoft.Data.Sqlite;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

/// <summary>SQLite 的原语 —— 库(`lib/sqlite.rav`)管策略,这里只负责"开一个连接、说一句 SQL"。
/// 它住在**官方扩展**(`plugins/Ravel.Extensions.dll`)里,对外是 `Native.SqliteOpen` 那几条;
/// `Microsoft.Data.Sqlite` 这个包也跟着搬进来了 —— 主项目从此不引用它。
///
/// **连接不进 Ravel 堆**:那是个要 Dispose 的本机句柄,而 Ravel 值没有析构那一套。
/// 所以这一格存着(一张 号 → 连接 的表),交给 Ravel 的是个 **int 号** ——
/// 库里那个 `Db` 对象拿着号,`Close ()` 时把号还回来。这也是为什么这几条原语的第一个
/// 参数都是 `handle`。
///
/// 参数一律用**名字**(`@名字` + 一张 dict),不做 `?` 那种按位置的替换:
/// SQL 里 `?` 也可能是字符串里的一个字面量,靠数问号来绑是给自己埋雷。
/// SQL 执行失败报 `ValueError`(你给的 SQL 不对),开关连接失败报 `IoError`(库文件打不开)。</summary>
[RavelModule("Native")]
internal static class SqliteNative
{
    private static readonly Dictionary<int, SqliteConnection> Opened = [];
    private static int _next;

    [RavelFn("SqliteOpen")]
    public static RuntimeValue Open(RuntimeValue path) => Io("开数据库", () =>
    {
        var p = PathOf(path, "SqliteOpen");
        var conn = new SqliteConnection($"Data Source={p}");
        conn.Open();
        var id = ++_next;
        Opened[id] = conn;
        return new IntVal(id);
    });

    [RavelFn("SqliteClose")]
    public static RuntimeValue Close(RuntimeValue handle) => Io("关数据库", () =>
    {
        var id = Int(handle, "SqliteClose 的句柄");     // 已经关了就当"关过了"(幂等)
        if (Opened.Remove(id, out var conn))
        {
            // **ClearPool 不能省**:Microsoft.Data.Sqlite 默认**连接池**着,光 Dispose 只是
            // 还给池子 —— 库文件那个句柄还开着,Windows 上接着 `DeletePath` 会报"正被占用"。
            // 显式 Close 的意思就是"我真要它关",所以顺手把这个连接串的池子清掉。
            SqliteConnection.ClearPool(conn);
            conn.Dispose();
        }
        return Void;
    });

    /// <summary>说一句不取结果的 SQL(建表 / 增删改 / `begin`/`commit`),交回**影响了几行**</summary>
    [RavelFn("SqliteExec")]
    public static RuntimeValue Exec(RuntimeValue handle, RuntimeValue sql, RuntimeValue args) => Sql("执行", () =>
    {
        using var cmd = Command(handle, sql, args);
        return new IntVal(cmd.ExecuteNonQuery());
    });

    /// <summary>取结果:`{columns: [列名…] rows: [[值…]…]}` —— 列名跟着来,库那边才拼得出一行行 dict</summary>
    [RavelFn("SqliteQuery")]
    public static RuntimeValue Query(RuntimeValue handle, RuntimeValue sql, RuntimeValue args) => Sql("查询", () =>
    {
        using var cmd = Command(handle, sql, args);
        using var reader = cmd.ExecuteReader();

        var columns = new List<RuntimeValue>();
        for (var i = 0; i < reader.FieldCount; i++) columns.Add(new StringVal(reader.GetName(i)));

        var rows = new List<RuntimeValue>();
        while (reader.Read())
        {
            var row = new List<RuntimeValue>();
            for (var i = 0; i < reader.FieldCount; i++) row.Add(FromSql(reader.GetValue(i)));
            rows.Add(new ListVal(row));
        }

        return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("columns")] = new ListVal(columns),
            [new StringVal("rows")] = new ListVal(rows),
        });
    });

    /// <summary>刚插进去那一行的 rowid(bigint —— 主键可以是 64 位)。
    /// **问一句 SQL** 而不是读某个属性:`Microsoft.Data.Sqlite` 这个版本没把
    /// `last_insert_rowid()` 挂成属性,而它本来就是一条 SQL 函数。</summary>
    [RavelFn("SqliteLastId")]
    public static RuntimeValue LastId(RuntimeValue handle) => Sql("取 last_insert_rowid", () =>
    {
        using var cmd = Connection(handle, "SqliteLastId").CreateCommand();
        cmd.CommandText = "select last_insert_rowid()";
        return new BigIntVal(Convert.ToInt64(cmd.ExecuteScalar()));
    });

    // ── 下面是自己人 ──

    private static SqliteConnection Connection(RuntimeValue h, string what)
    {
        var id = Int(h, $"{what} 的句柄");
        return Opened.TryGetValue(id, out var c)
            ? c
            : throw Fail($"这个数据库已经关了（Close () 过,或者压根没开成）：句柄 {id}", ErrorKind.Value);
    }

    /// <summary>拼一条命令:SQL + 按**名字**绑的参数(一张 dict;值经 `ToSql` 换算)。
    /// 参数名写的时候就带 `@`(`@id`),和 SQL 里那个写法一模一样 —— 不再自己加前缀。</summary>
    private static SqliteCommand Command(RuntimeValue handle, RuntimeValue sql, RuntimeValue args)
    {
        var cmd = Connection(handle, "执行").CreateCommand();
        cmd.CommandText = Text(sql, "SQL").Value;

        if (args is DictVal d)
            foreach (var (k, v) in d.Entries)
            {
                var name = Text(k, "参数名").Value;
                cmd.Parameters.AddWithValue(name.StartsWith('@') ? name : "@" + name, ToSql(v) ?? DBNull.Value);
            }

        return cmd;
    }

    /// <summary>Ravel 值 → SQLite 能收的东西。`()` 是 **NULL**(和 Json 那边一个口径);
    /// 字节表是 BLOB;大整数装不下 64 位就报错(不静默截断)。</summary>
    private static object? ToSql(RuntimeValue v) => v switch
    {
        VoidVal => null,
        IntVal i => i.Value,
        FloatVal f => f.Value,
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        BoolVal b => b.Value ? 1 : 0,
        BigIntVal g => (long)g.Value,
        ListVal l => Bytes(l, "SQL 参数"),
        _ => throw Fail(
            $"SQL 参数存不进 SQLite: {v.Type}（先转成 数 / 字符串 / 布尔 / ()，二进制用字节表）", ErrorKind.Type),
    };

    /// <summary>SQLite 交回的东西 → Ravel 值。INTEGER 装得下 int 就给 int、否则 bigint
    /// (和数字字面量、`Json.Extract` 一个口径)。</summary>
    private static RuntimeValue FromSql(object? o) => o switch
    {
        null or DBNull => Void,
        long l => l is >= int.MinValue and <= int.MaxValue ? new IntVal((int)l) : new BigIntVal(l),
        double d => new FloatVal(d),
        string s => new StringVal(s),
        byte[] b => BytesList(b),
        _ => new StringVal(o.ToString() ?? ""),
    };

    /// <summary>SQL 不对 / 数据库关着 —— 说人话,别把 `SqliteException` 漏到顶层
    /// (它接不住,会把程序打掉 —— 这批原语一条老规矩)</summary>
    private static RuntimeValue Sql(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (SqliteException ex)
        {
            throw Fail($"{what}失败: {ex.Message}", ErrorKind.Value);
        }
        catch (InvalidOperationException ex)
        {
            throw Fail($"{what}失败: {ex.Message}", ErrorKind.Value);
        }
    }

    private static RuntimeValue Io(string what, Func<RuntimeValue> body)
    {
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException
                                   or ArgumentException or UnauthorizedAccessException)
        {
            throw Fail($"{what}失败: {ex.Message}", ErrorKind.Io);
        }
    }
}
