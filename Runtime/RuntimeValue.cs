namespace Ravel.Runtime;

public abstract record RuntimeValue
{
    public abstract RuntimeType Type { get; }

    public record IntVal(int Value) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Int;
        public override string ToString() => Value.ToString();
    }

    public record FloatVal(double Value) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Float;
        public override string ToString() => Value.ToString("G");
    }

    public record BigIntVal(System.Numerics.BigInteger Value) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.BigInt;
        public override string ToString() => Value.ToString();
    }

    public record FractionVal(int Num, int Den) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Fraction;
        public override string ToString() => $"{Num}/{Den}";
    }

    public record BigFractionVal(System.Numerics.BigInteger Num, System.Numerics.BigInteger Den) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.BigFraction;
        public override string ToString() => $"{Num}/{Den}";
    }

    public record BoolVal(bool Value) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Bool;
        public override string ToString() => Value.ToString().ToLower();
    }

    public record StringVal(string Value) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.String;
        public override string ToString() => Value;
    }

    public record VoidVal : RuntimeValue
    {
        public static readonly VoidVal Instance = new();
        public override RuntimeType Type => RuntimeType.Void;
        public override string ToString() => "()";
        private VoidVal() { }
    }

    public record ClassMeta(
    RuntimeType Type,
    FunctionVal Init,
    string[] Fields,
    Variable? ThisVar = null,
    Variable? BaseVar = null,
    ClassMeta? Parent = null,
    Scope? ClassScope = null,
    RuntimeType? MetaType = null);

    /// <summary>函数值——Scope 绑定在实例上，可随时更换</summary>
    public record FunctionVal : RuntimeValue
    {
        Scope _scope;
        Func<Scope, RuntimeValue[], Step> _rawBody;

        public Func<RuntimeValue[], RuntimeValue>? Direct { get; set; }
        public Func<RuntimeValue[], Step> Trampolined { get; set; }
        public ClassMeta? Meta { get; set; }
        public string? Name { get; set; }

        public Scope Scope {
            get => _scope;
            set => _scope = value;
        }

        public override RuntimeType Type => Meta?.MetaType ?? Meta?.Type ?? RuntimeType.Function;
        public override string ToString() => Name!=null ? "<function "+Name+">" : "<function>";

        public FunctionVal(Scope scope, Func<Scope, RuntimeValue[], Step> rawBody)
        {
            _scope = scope;
            _rawBody = rawBody;
            Trampolined = args => rawBody(_scope, args);
        }

        public FunctionVal() { }

        public static FunctionVal FromDirect(Func<RuntimeValue[], RuntimeValue> f)
            => new FunctionVal(null!, (_, args) => new Done(f(args)));

        public static FunctionVal FromTrampolined(Func<RuntimeValue[], Step> f, ClassMeta? meta = null)
        {
            var fn = new FunctionVal(null!, (_, args) => f(args));
            fn.Meta = meta;
            return fn;
        }

        public FunctionVal CloneWithScope(Scope newScope)
        {
            return new FunctionVal(newScope, _rawBody) { Meta = Meta, Direct = Direct };
        }

        /// <summary>前置执行一个块，再执行本函数（参数原样转发）</summary>
        public FunctionVal Prepend(BlockVal prefix)
        {
            var original = this;
            var bindScope = prefix.CaptureScope ?? Scope;
            return new FunctionVal(bindScope, (scope, args) =>
            {
                var saved = Interpreter.Current!.CurrentScope;
                Interpreter.Current.CurrentScope = scope;
                var pre = Interpreter.Current.EvalBlockExec(prefix.Block);
                return Interpreter.Then(pre, _ =>
                {
                    Interpreter.Current.CurrentScope = saved;
                    return original.Trampolined(args);
                });
            });
        }

        /// <summary>执行本函数后，追加执行一个块</summary>
        public FunctionVal Append(BlockVal suffix)
        {
            var original = this;
            var bindScope = suffix.CaptureScope ?? Scope;
            return new FunctionVal(bindScope, (scope, args) =>
            {
                var saved = Interpreter.Current!.CurrentScope;
                Interpreter.Current.CurrentScope = scope;
                var body = original.Trampolined(args);
                return Interpreter.Then(body, result =>
                {
                    var post = Interpreter.Current.EvalBlockExec(suffix.Block);
                    return Interpreter.Then(post, _ =>
                    {
                        Interpreter.Current.CurrentScope = saved;
                        return Interpreter.ToDone(result);
                    });
                });
            });
        }
    }

    public record ListVal(List<RuntimeValue> Elements) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.List;
        public override string ToString()
            => "[" + string.Join(" ", Elements) + "]";
    }

    public record SetVal(HashSet<RuntimeValue> Elements) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Set;
        public override string ToString()
            => "{" + string.Join(" ", Elements) + "}";
    }

    public record DictVal(Dictionary<string, RuntimeValue> Entries) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Dict;
        public override string ToString()
        {
            var pairs = new List<string>();
            foreach (var kv in Entries) pairs.Add(kv.Key + ": " + kv.Value);
            return "{" + string.Join(" ", pairs) + "}";
        }
    }

    public record BlockVal : FunctionVal
    {
        public BlockExpr Block { get; init; }
        public Scope CaptureScope { get; init; }
        public Interpreter Interp { get; init; }

        public BlockVal(BlockExpr block, Scope captureScope, Interpreter interp)
        {
            Block = block;
            CaptureScope = captureScope;
            Interp = interp;
            Scope = captureScope;
            Trampolined = _ => Invoke();
        }

        public Step Invoke()
        {
            var saved = Interp.CurrentScope;
            Interp.CurrentScope = CaptureScope;
            return new More(() =>
            {
                var r = Interp.EvalBlockExec(Block);
                return new More(() => { Interp.CurrentScope = saved; return r; });
            });
        }

        public BlockVal Prepend(BlockVal prefix)
        {
            var stmts = prefix.Block.Statements.Concat(this.Block.Statements).ToList();
            return new BlockVal(
                new BlockExpr(stmts) { Line = Block.Line, Column = Block.Column },
                CaptureScope, Interp);
        }

        public BlockVal Append(BlockVal suffix)
        {
            var stmts = this.Block.Statements.Concat(suffix.Block.Statements).ToList();
            return new BlockVal(
                new BlockExpr(stmts) { Line = Block.Line, Column = Block.Column },
                CaptureScope, Interp);
        }

        public override string ToString() => "<block>";
    }

    public record ContinuationVal(Func<RuntimeValue, Step> Impl) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Function;
        public override string ToString() => "<continuation>";
    }

    public record ExceptionVal(string Message) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Exception;
        public override string ToString() => Message;
    }

    /// <summary>Ravel 模块实例</summary>
    public record ModuleVal(RuntimeType ModType, Scope ModuleScope) : RuntimeValue
    {
        public override RuntimeType Type => ModType;
        public override string ToString() => $"<module {ModType.Name}>";
    }

    public record ScopeVal(Scope Scope) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.ScopeType;
        public override string ToString() => "<scope>";
    }

    /// <summary>Property = getter + setter 对</summary>
    public record PropertyVal(FunctionVal Getter, FunctionVal Setter, List<string>? Attrs=null) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Property;
        public override string ToString() => "<property>";
    }

    public record BaseRef(ObjectVal Instance, ClassMeta ParentMeta) : RuntimeValue
    {
        public override RuntimeType Type => ParentMeta.Type;
        public override string ToString() => $"<base:{ParentMeta.Type.Name}>";
    }

    public record ThisRef(RuntimeValue Value) : RuntimeValue
    {
        public override RuntimeType Type => Value.Type;
        public override string ToString() => Value.ToString();
    }

    public record ObjectVal(RuntimeType ClassType, Dictionary<string, RuntimeValue> Fields, ObjectVal? Parent = null, ClassMeta? Meta = null, Scope? InstanceScope = null) : RuntimeValue
    {
        public override RuntimeType Type => ClassType;
        public override string ToString() => $"<{ClassType.Name}>";
    }

    public record DefaultVal : RuntimeValue
    {
        public static readonly DefaultVal Instance = new();
        public override RuntimeType Type => RuntimeType.Every;
        public override string ToString() => "default";
        private DefaultVal() { }
    }

    public record TypeVal(RuntimeType Value) : RuntimeValue
    {
        public override RuntimeType Type => RuntimeType.Type;
        public override string ToString() => Value.Name;
    }

    public bool HasType(RuntimeType expected)
        => Type.IsAssignableTo(expected);

    public RuntimeValue Expect(RuntimeType expected, string context)
    {
        if (!HasType(expected))
            throw new RuntimeException($"{context} 类型错误: 期望 {expected}，实际 {Type}");
        return this;
    }
}

public class RuntimeException : Exception
{
    public RuntimeException(string message) : base(message) { }
}

/// <summary>exit 专用异常——不被 EvalCall 捕获，直接向上抛出</summary>
public class ExitException : Exception
{
    public ExitException(string message) : base(message) { }
}
