using System.Globalization;

namespace Ravel.Runtime;

using static Ravel.Runtime.Interpreter;
using static Ravel.Runtime.SysKit;

/// <summary>`System` 的时间原语。
///
/// 一个时刻就是"1970-01-01 00:00:00 UTC 起的**毫秒数**"(bigint —— 毫秒那个数量级 int 装不下)。
/// 原语只做换算,不做判断:显示成什么样、哪天算一周开头,都是库(lib/time.rav)的事。
/// 时区一律**本地**;格式串按 .NET 那一套(`yyyy-MM-dd HH:mm:ss`),这几条只负责转交。</summary>
internal static class SysTime
{
    [Sys("NowMs")]
    public static RuntimeValue NowMs(RuntimeValue _)
        => Fs("取当前时间", () => new BigIntVal(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

    [Sys("TimeParts")]
    public static RuntimeValue TimeParts(RuntimeValue a) => Fs("拆时间", () =>
    {
        var t = LocalTime(a, "TimeParts");
        return new DictVal(new Dictionary<RuntimeValue, RuntimeValue>
        {
            [new StringVal("year")] = IntVal.Of(t.Year),
            [new StringVal("month")] = IntVal.Of(t.Month),
            [new StringVal("day")] = IntVal.Of(t.Day),
            [new StringVal("hour")] = IntVal.Of(t.Hour),
            [new StringVal("minute")] = IntVal.Of(t.Minute),
            [new StringVal("second")] = IntVal.Of(t.Second),
            [new StringVal("millisecond")] = IntVal.Of(t.Millisecond),
            // 0 = 周日(和 .NET 的 DayOfWeek 一个口径;翻成中文名是库那边的事)
            [new StringVal("weekday")] = IntVal.Of((int)t.DayOfWeek),
        });
    });

    [Sys("MakeTime")]
    public static RuntimeValue MakeTime(RuntimeValue a) => Fs("拼时间", () =>
    {
        var d = As<DictVal>(a, "MakeTime 的 parts");
        int Part(string k, int dflt) =>
            d.Entries.TryGetValue(new StringVal(k), out var v) && v is IntVal i ? i.Value : dflt;
        var t = new DateTime(Part("year", 1970), Part("month", 1), Part("day", 1),
                             Part("hour", 0), Part("minute", 0), Part("second", 0),
                             DateTimeKind.Local).AddMilliseconds(Part("millisecond", 0));
        return new BigIntVal(new DateTimeOffset(t).ToUnixTimeMilliseconds());
    });

    /// <summary>睡一会儿。0 或负数就是"什么都不等"(`retries` 退避算出来是 0 很正常,别为这个报错),
    /// 拿它限速也是这个用法 —— 「别把人家服务器打疼了」。</summary>
    [Sys("Sleep")]
    public static RuntimeValue Sleep(RuntimeValue a) => Fs("等待", () =>
    {
        var ms = BuiltinClasses.IntArg(a, "Sleep 的毫秒数");
        if (ms > 0) Thread.Sleep(ms);
        return VoidVal.Instance;
    });

    [Sys("FormatTime")]
    public static RuntimeValue FormatTime(RuntimeValue a, RuntimeValue b) => Fs("格式化时间", () =>
        new StringVal(LocalTime(a, "FormatTime").ToString(
            As<StringVal>(b, "FormatTime 的格式串").Value, CultureInfo.InvariantCulture)));

    [Sys("ParseTime")]
    public static RuntimeValue ParseTime(RuntimeValue a, RuntimeValue b) => Fs("解析时间", () =>
    {
        var text = As<StringVal>(a, "ParseTime 的文本").Value;
        var fmt = As<StringVal>(b, "ParseTime 的格式串").Value;
        // 用 TryParseExact 而不是 Parse:FormatException 不在 Fs 的白名单里,
        // 漏出去就是"解释器内部错误"、Ravel 的 try 接不住(和 Json 那边同款说明)
        if (!DateTime.TryParseExact(text, fmt, CultureInfo.InvariantCulture,
                                    DateTimeStyles.None, out var t))
            throw new RuntimeException($"ParseTime: 读不动 —— '{text}' 对不上格式 '{fmt}'", ErrorKind.Value);
        return new BigIntVal(new DateTimeOffset(t).ToUnixTimeMilliseconds());
    });

    /// <summary>把 Ravel 那个"毫秒数"收成 <see cref="DateTime"/>(本地时区)。
    /// 收 int 也收 bigint —— 手写 `Time 0` 那种小常量不该被迫写 `bigint 0`。</summary>
    public static DateTime LocalTime(RuntimeValue v, string what) => v switch
    {
        BigIntVal b => DateTimeOffset.FromUnixTimeMilliseconds((long)b.Value).LocalDateTime,
        IntVal i => DateTimeOffset.FromUnixTimeMilliseconds(i.Value).LocalDateTime,
        _ => throw new RuntimeException($"{what} 的毫秒数需要 bigint，得到 {v.Type}", ErrorKind.Type),
    };
}
