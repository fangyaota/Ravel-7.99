using Microsoft.Data.Sqlite;

namespace Ravel.Extensions;

using Ravel.Runtime;
using static Ravel.Runtime.PluginKit;

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
        return IntVal.Of(id);
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

    [RavelFn("SqliteExec")]
    public static RuntimeValue Exec(RuntimeValue handle, RuntimeValue sql, RuntimeValue args) => Sql("执行", () =>
    {
        using var cmd = Command(handle, sql, args);
        return IntVal.Of(cmd.ExecuteNonQuery());
    });

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

    private static object? ToSql(RuntimeValue v) => v switch
    {
        VoidVal => null,
        IntVal i => i.Value,
        RealVal f => f.Value,
        StringVal s => s.Value,
        CharVal c => c.Value.ToString(),
        BoolVal b => b.Value ? 1 : 0,
        BigIntVal g => (long)g.Value,
        ListVal l => Bytes(l, "SQL 参数"),
        _ => throw Fail(
            $"SQL 参数存不进 SQLite: {v.Type}（先转成 数 / 字符串 / 布尔 / ()，二进制用字节表）", ErrorKind.Type),
    };

    private static RuntimeValue FromSql(object? o) => o switch
    {
        null or DBNull => Void,
        long l => l is >= int.MinValue and <= int.MaxValue ? IntVal.Of((int)l) : new BigIntVal(l),
        double d => new RealVal(d),
        string s => new StringVal(s),
        byte[] b => BytesList(b),
        _ => new StringVal(o.ToString() ?? ""),
    };

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
