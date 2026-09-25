using Ravel;
using Ravel.Repl;
using Ravel.Runtime;
using Ravel.Testing;

if (args.Length == 0)
{
    new NeoInteractor(new Interpreter()).Run();
}
else if (args[0] == "test")
{
    if (!GoldenTestRunner.RunAll()) Environment.ExitCode = 1;
}
else
{
    RunFile(args[0]);
}

static void RunFile(string path)
{
    var source = File.ReadAllText(path);
    Console.WriteLine($"── {path} ──");
    Console.WriteLine(source.Trim());
    Console.WriteLine("── Output ──");
    try
    {
        new Interpreter().Interpret(Parser.ParseSource(source, path));
    }
    catch (RuntimeException ex)
    {
        Console.WriteLine($"Error: {ErrorReport.Format(ex)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }

    Console.WriteLine();
}
