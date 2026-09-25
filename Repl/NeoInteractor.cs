namespace Ravel.Repl;

using Ravel.Runtime;

using Spectre.Console;

using System.Text;

/// <summary>REPL 外壳:多页编辑缓冲 + 光标 + 主菜单(编辑/运行/读写文件/普通 REPL)。
/// 视图渲染在 <see cref="ReplView"/>,会话持久化在 <see cref="ReplSession"/>。</summary>
public class NeoInteractor
{
    public List<List<string>> Paras { get; set; }
    public List<string> Lines
    {
        get => Paras[Cursor_z];
        set => Paras[Cursor_z] = value;
    }
    public Dictionary<int, string> RenderedLines { get; set; } = new();
    private string CurrentLine
    {
        get => Lines[Cursor_y];
        set => Lines[Cursor_y] = value;
    }
    public int Cursor_x { get; set; }
    public int Cursor_y { get; set; }
    public int Cursor_z { get; set; }
    public bool AutoTab { get; set; } = true;
    public bool OutPut { get; set; } = true;
    public Interpreter Global { get; }

    public NeoInteractor(Interpreter global)
    {
        Global = global;
        Paras = ReplSession.Load();
    }

    public void Run()
    {
        EvaluateShowRavel();
        Interact();
    }

    public string GetText() => string.Join('\n', Lines);

    /// <summary>重算第 index 行的渲染缓存(高亮 + 光标)</summary>
    public void FlushRenderedLine(int index)
    {
        if (index >= Lines.Count) return;
        int? cursorX = Cursor_y == index ? Cursor_x : null;
        RenderedLines[index] = ReplView.RenderRow(index, Lines[index], cursorX);
    }

    public void FlushRenderedLineAll()
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            FlushRenderedLine(i);
        }
    }

    public string GetRenderText(int middle)
    {
        StringBuilder sb = new();

        var left = Math.Clamp(middle - 8, 0, Lines.Count);
        var right = Math.Clamp(left + 16, 0, Lines.Count);
        left = Math.Clamp(right - 16, 0, Lines.Count);
        sb.AppendLine($"------[purple]第{Cursor_z}页[/]------");
        if (Lines.Count == 0)
        {
            sb.AppendLine("   | <空>");
        }
        else
        {
            for (int i = left; i < right; i++)
            {
                if (!RenderedLines.ContainsKey(i))
                {
                    FlushRenderedLine(i);
                }
                sb.Append(RenderedLines[i]);
            }
            if (right - left <= 8)
            {
                sb.AppendJoin('\n', Enumerable.Repeat("   |", 8 - (right - left)));
            }
        }
        return sb.ToString();
    }

    private void EvaluateShowRavel()
    {
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("""
             ____                                   ___
            /\  _`\                                /\_ \
            \ \ \L\ \      __      __  __     __   \//\ \
             \ \ ,  /    /'__`\   /\ \/\ \  /'__`\   \ \ \
              \ \ \\ \  /\ \L\.\_ \ \ \_/ |/\  __/    \_\ \_
               \ \_\ \_\\ \__/.\_\ \ \___/ \ \____\   /\____\
                \/_/\/ / \/__/\/_/  \/__/   \/____/   \/____/
                        _____           ___           ___
                       /\  ___\        /'__`\        /'__`\
                       \ \ \__/       /\ \/\ \      /\ \/\ \
                        \ \___``\     \ \ \ \ \     \ \ \ \ \
                         \/\ \L\ \ __  \ \ \_\ \ __  \ \ \_\ \
                          \ \____//\_\  \ \____//\_\  \ \____/
                           \/___/ \/_/   \/___/ \/_/   \/___/
            Ravel 7.99
            """);
        Pause();
        AnsiConsole.Clear();
    }

    private void EvaluateOutPut()
    {
        var result = AnsiConsole.Confirm("[yellow]开关显示执行结果吗？[/]", !OutPut);
        OutPut = result;
        AnsiConsole.WriteLine(OutPut ? "已开启显示" : "已关闭显示");
        Console.ReadKey(true);
    }

    private void EvaluateShowAutoTab()
    {
        var result = AnsiConsole.Confirm("[yellow]开关自动对齐文本吗？[/]", !AutoTab);
        AutoTab = result;
        AnsiConsole.WriteLine(AutoTab ? "已开启自动对齐文本" : "已关闭自动对齐文本");
    }

    private void Interact()
    {
        int index = 0;
        Dictionary<int, (string description, Action action)> map = new()
        {
            [index++] = ("编辑", DynamicInput),
            [index++] = ("运行", EvaluateTryCompile),
            [index++] = ("普通 Repl", RunRepl),
            [index++] = ("读取", EvaluateLoad),
            [index++] = ("保存", EvaluateSave),
            [index++] = ("退出", EvaluateExit),
            [index++] = ("作者", EvaluateShowRavel),
            [index++] = ("开关自动对齐", EvaluateShowAutoTab),
            [index++] = ("开关结果显示", EvaluateOutPut),
        };
        var prompt = new SelectionPrompt<int>()
            .Title("------主菜单------")
            .EnableSearch()
            .PageSize(6)
            .MoreChoicesText("[grey]（往下滑获取更多选项）[/]")
            .SearchPlaceholderText("[grey]（输入序号快速跳转：）[/]")
            .UseConverter(x => $"{x}: {map[x].description}")
            .AddChoices(map.Keys);
        while (true)
        {
            ClearAndRender();

            var result = AnsiConsole.Prompt(prompt);
            map[result].action();
        }
    }

    private void EvaluateLoad()
    {
        string path = AnsiConsole.Ask("选择代码文件", "temp.ravel");
        FileInfo file = new(path);
        if (!file.Exists)
        {
            AnsiConsole.MarkupLine($"[red]错误：文件<{file.Name}>不存在[/]");
        }
        else
        {
            int index = AnsiConsole.Ask("输入插入页码", Cursor_z);
            while (index < 0 || index >= Paras.Count)
            {
                AnsiConsole.MarkupLine("[red]错误：页码不合规[/]");
                index = AnsiConsole.Ask("输入插入页码", Cursor_z);
            }
            Paras.Insert(index, File.ReadAllLines(path).ToList());
            if (index == Cursor_z)
            {
                FlushRenderedLineAll();
            }
            AnsiConsole.MarkupLine($"已加载至[purple]第{index}页[/]");
        }
        Pause();
    }

    private void EvaluateSave()
    {
        string path = AnsiConsole.Ask("选择代码文件", "temp.ravel");
        FileInfo file = new(path);
        if (file.Exists)
        {
            AnsiConsole.MarkupLine($"[yellow]警告：文件<{file.Name}>已存在,正在覆盖...[/]");
        }
        File.WriteAllLines(path, Lines);
        AnsiConsole.MarkupLine($"已保存[purple]第{Cursor_z}页[/]");
        Pause();
    }

    private void EvaluateExit()
    {
        ReplSession.Save(Paras);
        Environment.Exit(0);
    }

    private void EvaluateTryCompile()
    {
        string source = GetText();
        try
        {
            var program = Parser.ParseSource(source);

            var oldOut = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            RuntimeValue result;
            try
            {
                result = Global.Interpret(program);
            }
            finally
            {
                Console.SetOut(oldOut);
            }

            if (OutPut)
            {
                var output = sw.ToString().TrimEnd();
                if (output.Length > 0)
                {
                    AnsiConsole.MarkupLine(output.EscapeMarkup());
                }
                if (result is not VoidVal)
                {
                    AnsiConsole.Markup("[yellow]==>[/]");
                    AnsiConsole.MarkupLine(result.ToString().EscapeMarkup());
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message.EscapeMarkup()}");
        }
        Pause();
    }

    private void RunRepl()
    {
        AnsiConsole.Clear();
        Console.WriteLine("普通 Repl（:exit 返回菜单）");
        string buffer = "";
        while (true)
        {
            Console.Write(buffer.Length == 0 ? ">>> " : "... ");
            var input = Console.ReadLine();
            if (input is null) return;
            if (buffer.Length == 0 && input.Trim() is ":exit" or ":q" or ":quit") return;

            buffer += input + "\n";
            if (!IsBalanced(buffer)) continue;

            try
            {
                var result = Global.Interpret(Parser.ParseSource(buffer));
                if (result is not VoidVal)
                    Console.WriteLine($"==> {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            buffer = "";
        }
    }

    private static bool IsBalanced(string src)
    {
        int depth = 0;
        bool inString = false;
        for (int i = 0; i < src.Length; i++)
        {
            char c = src[i];
            if (c == '"') inString = !inString;
            if (inString) continue;
            if (c == '#')
            {
                while (i < src.Length && src[i] != '\n') i++;
                continue;
            }
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
        }
        return depth <= 0;
    }

    private static void Pause()
    {
        AnsiConsole.MarkupLine("[green]按任意键继续[/]");
        Console.ReadKey(true);
    }

    private void RenderText()
    {
        AnsiConsole.Write(new Panel(GetRenderText(Cursor_y)));
    }

    private void ClearAndRender()
    {
        var h = Console.CursorTop;
        var w = Console.WindowWidth;
        var blank = new string(' ', w);
        AnsiConsole.Cursor.SetPosition(0, 0);
        for (int i = 0; i < h; i++)
        {
            AnsiConsole.Write(blank);
        }
        AnsiConsole.Cursor.SetPosition(0, 0);
        RenderText();
    }

    private void RenderEditTips()
    {
        AnsiConsole.MarkupLine("""
            ------快捷栏------

              [yellow]Ins [/] : 清除该页
              [yellow]PgUp[/] : 上一条记录
              [yellow]PgDn[/] : 下一条记录
              [yellow]Home[/] : 跳至开头
              [yellow]End [/] : 跳至结尾
              [yellow]Del [/] : 向后删除
              [yellow]Esc [/] : 退出编辑
            """);
    }

    private void DynamicInput()
    {
        RenderEditTips();
        Console.CursorVisible = false;
        while (true)
        {
            var input = Console.ReadKey(true);
            int last_y = Cursor_y;
            if (CursorEdit(input))
            {
                FlushRenderedLine(last_y);
                if (Cursor_y != last_y)
                {
                    FlushRenderedLine(Cursor_y);
                }
                ClearAndRender();
                RenderEditTips();
                last_y = Cursor_y;
                continue;
            }
            switch (input.Key)
            {
                case ConsoleKey.Escape:
                    return;
            }
            if (char.IsControl(input.KeyChar))
            {
                continue;
            }
            InsertNewLineIfEmpty();
            CurrentLine = CurrentLine.Insert(Cursor_x, input.KeyChar.ToString());
            Cursor_x++;
            CursorClamp();

            FlushRenderedLine(last_y);
            FlushRenderedLine(Cursor_y);
            ClearAndRender();
            RenderEditTips();
        }
    }

    private bool InsertNewLineIfEmpty()
    {
        if (Lines.Count == 0)
        {
            Lines.Insert(Cursor_y, "");
            return true;
        }
        return false;
    }

    private bool CursorEdit(ConsoleKeyInfo input)
    {
        switch (input.Key)
        {
            case ConsoleKey.PageUp:
                Cursor_x = 0;
                Cursor_y = 0;
                Cursor_z--;
                CursorClamp();
                FlushRenderedLineAll();
                return true;
            case ConsoleKey.PageDown:
                if (Cursor_z == Paras.Count - 1 && Lines.Count != 0)
                {
                    Paras.Add(new());
                }
                Cursor_x = 0;
                Cursor_y = 0;
                Cursor_z++;
                CursorClamp();
                FlushRenderedLineAll();
                return true;
            case ConsoleKey.UpArrow:
                Cursor_y--;
                CursorClamp();
                return true;
            case ConsoleKey.DownArrow:
                Cursor_y++;
                CursorClamp();
                return true;
            case ConsoleKey.LeftArrow:
                Cursor_x--;
                CursorClamp();
                return true;
            case ConsoleKey.RightArrow:
                Cursor_x++;
                CursorClamp();
                return true;
            case ConsoleKey.Insert:
                if (Lines.Count == 0 && Paras.Count >= 2)
                {
                    Paras.RemoveAt(Cursor_z);
                    Cursor_z--;
                    FlushRenderedLineAll();
                }
                else
                {
                    Lines.Clear();
                }
                CursorClamp();
                return true;
            case ConsoleKey.Backspace:
                if (Cursor_x == 0)
                {
                    if (Cursor_y >= 1)
                    {
                        Cursor_x = Lines[Cursor_y - 1].Length;
                        Lines[Cursor_y - 1] += CurrentLine;
                        Lines.RemoveAt(Cursor_y);
                        Cursor_y--;
                        FlushRenderedLineAll();
                    }
                }
                else
                {
                    CurrentLine = CurrentLine.Remove(Cursor_x - 1, 1);
                    Cursor_x--;
                }
                CursorClamp();
                return true;
            case ConsoleKey.Delete:
                if (Cursor_x == CurrentLine.Length)
                {
                    if (Cursor_y < Lines.Count - 1)
                    {
                        CurrentLine += Lines[Cursor_y + 1];
                        Lines.RemoveAt(Cursor_y + 1);
                        FlushRenderedLineAll();
                    }
                }
                else
                {
                    CurrentLine = CurrentLine.Remove(Cursor_x, 1);
                }
                CursorClamp();
                return true;
            case ConsoleKey.Enter:
                if (Lines.Count == 0)
                {
                    Lines.Add("");
                }
                else
                {
                    int i = 0;
                    if (AutoTab)
                    {
                        while (i < CurrentLine.Length)
                        {
                            if (CurrentLine[i] != ' ')
                            {
                                break;
                            }
                            i++;
                        }
                    }
                    Lines.Insert(Cursor_y + 1, new string(' ', i));
                    Lines[Cursor_y + 1] += CurrentLine[Cursor_x..];
                    CurrentLine = CurrentLine[..Cursor_x];
                    Cursor_x = i;
                    FlushRenderedLineAll();
                }
                Cursor_y++;
                CursorClamp();
                return true;
            case ConsoleKey.Tab:
                InsertNewLineIfEmpty();
                CurrentLine = CurrentLine.Insert(Cursor_x, "    ");
                Cursor_x += 4;
                return true;
            case ConsoleKey.Home:
                Cursor_x = 0;
                Cursor_y = 0;
                return true;
            case ConsoleKey.End:
                Cursor_y = Lines.Count - 1;
                Cursor_x = CurrentLine.Length;
                return true;
        }
        return false;
    }

    private void CursorClamp()
    {
        Cursor_z = Math.Clamp(Cursor_z, 0, Paras.Count - 1);
        if (Lines.Count == 0)
        {
            Cursor_y = 0;
            Cursor_x = 0;
            return;
        }
        Cursor_y = Math.Clamp(Cursor_y, 0, Lines.Count - 1);
        Cursor_x = Math.Clamp(Cursor_x, 0, CurrentLine.Length);
    }
}
