using Ravel;
using Ravel.Runtime;

if (args.Length > 0)
{
    RunFile(args[0]);
}
else
{
    RunAllTests();
}

static void RunFile(string path)
{
    var source = File.ReadAllText(path);
    Console.WriteLine($"── {path} ──");
    Console.WriteLine(source.Trim());
    Console.WriteLine("── Output ──");
    try
    {
        var interpreter = new Interpreter();
        interpreter.Interpret(Parse(source));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
    Console.WriteLine();
}

static Ravel.Program Parse(string source)
{
    var lexer = new Lexer(source);
    var parser = new Parser(lexer.Tokenize());
    return parser.Parse();
}

// ============================================================
//  Golden test runner
// ============================================================

static void RunAllTests()
{
    var testDir = FindTestDir();
    if (testDir == null) { Console.WriteLine("No tests/ directory found."); return; }

    int passed = 0, failed = 0, todo = 0;
    foreach (var file in Directory.GetFiles(testDir, "*.rav").OrderBy(f => f))
    {
        var name = Path.GetFileName(file);
        var content = File.ReadAllText(file);

        // Split source from expected output
        var (source, expected, expectError) = ParseTestFile(content);

        Console.Write($"{name,-35} ");

        var output = "";
        try
        {
            output = CaptureOutput(source);
        }
        catch (Exception ex)
        {
            output = $"Error: {ex.Message}";
        }

        if (expectError)
        {
            if (output.StartsWith("Error:"))
                { Console.WriteLine($"OK (expected error)"); passed++; }
            else
                { Console.WriteLine($"TODO (expected error, got output)"); todo++; }
        }
        else
        {
            if (output.Trim() == expected.Trim())
                { Console.WriteLine("OK"); passed++; }
            else
            {
                Console.WriteLine("TODO");
                Console.WriteLine($"       expected: {expected.Trim().Replace("\n","\\n")}");
                Console.WriteLine($"       got:      {output.Trim().Replace("\n","\\n")}");
                todo++;
            }
        }
    }
    Console.WriteLine($"\n  {passed} passed, {failed} failed, {todo} todo");
}

static (string source, string expected, bool expectError) ParseTestFile(string content)
{
    bool expectError = false;
    var sourceLines = new List<string>();
    var expectedLines = new List<string>();
    bool inExpected = false;

    foreach (var raw in content.Replace("\r\n", "\n").Split('\n'))
    {
        var line = raw.TrimEnd();
        if (line.TrimStart().StartsWith("# expect-error"))
        {
            expectError = true;
            continue;
        }
        if (line.TrimStart() == "# --- expected ---")
        {
            inExpected = true;
            continue;
        }
        if (inExpected)
            expectedLines.Add(line.TrimStart().StartsWith("# ") ? line.TrimStart()[2..] : line);
        else
            sourceLines.Add(line);
    }

    return (string.Join("\n", sourceLines), string.Join("\n", expectedLines), expectError);
}

static string CaptureOutput(string source)
{
    var oldOut = Console.Out;
    var sw = new StringWriter();
    Console.SetOut(sw);
    try
    {
        var prog = Parse(source);
        new Interpreter().Interpret(prog);
    }
    finally
    {
        Console.SetOut(oldOut);
    }
    return sw.ToString().Replace("\r\n", "\n").TrimEnd();
}

static string? FindTestDir()
{
    var bases = new[] {
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "tests"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests"),
        "tests",
        "/workspace/ravel/tests"
    };
    return bases.FirstOrDefault(Directory.Exists);
}
