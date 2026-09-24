using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

return await Driver.RunAsync(args);

internal static class Driver
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: al check FILE [--json] | al run FILE | al test");
            return 2;
        }

        if (args[0] == "test") return TestFixtures();
        if (args[0] is not ("check" or "run") || args.Length < 2)
        {
            Console.Error.WriteLine("Usage: al check FILE [--json] | al run FILE | al test");
            return 2;
        }

        var file = Path.GetFullPath(args[1]);
        if (!File.Exists(file))
        {
            PrintDiagnostics([new Diagnostic("E_SYNTAX", "Source file does not exist", file, new Range(1, 1, 1, 1))], args.Contains("--json"));
            return 1;
        }

        var result = Compiler.Check(file, File.ReadAllText(file));
        PrintDiagnostics(result.Diagnostics, args.Contains("--json"));
        if (result.Diagnostics.Count != 0) return 1;
        if (args[0] == "check") return 0;
        return await RunCheckedAsync(result.Program!, file);
    }

    private static void PrintDiagnostics(IReadOnlyList<Diagnostic> diagnostics, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schemaVersion = 1, diagnostics }, JsonOptions));
            return;
        }
        foreach (var d in diagnostics)
            Console.Error.WriteLine($"{d.File}:{d.Range.StartLine}:{d.Range.StartColumn}: {d.Code}: {d.Message}");
    }

    private static async Task<int> RunCheckedAsync(CheckedProgram program, string sourceFile)
    {
        if (!program.Functions.Any(f => f.Name == "main" && f.Parameters.Count == 0 && f.ReturnType == "i32"))
        {
            Console.Error.WriteLine("al run requires fn main() -> i32");
            return 1;
        }

        var output = Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "out", "al-generated");
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Generated.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(output, "Program.cs"), Emitter.Emit(program));
        var localHost = Path.Combine(FindRoot() ?? Directory.GetCurrentDirectory(), ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var dotnet = Environment.GetEnvironmentVariable("AL_DOTNET")
            ?? (File.Exists(localHost) ? localHost : "dotnet");
        var build = await ExecAsync(dotnet, ["build", Path.Combine(output, "Generated.csproj"), "--nologo", "-v:q"]);
        if (build != 0) return build;
        return await ExecAsync(dotnet, [Path.Combine(output, "bin", "Debug", "net10.0", "Generated.dll")]);
    }

    private static async Task<int> ExecAsync(string executable, IReadOnlyList<string> arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = executable;
        foreach (var arg in arguments) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.UseShellExecute = false;
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static int TestFixtures()
    {
        var root = FindRoot();
        if (root is null) { Console.Error.WriteLine("Cannot find fixtures/manifest.json"); return 2; }
        var cases = JsonSerializer.Deserialize<FixtureCase[]>(File.ReadAllText(Path.Combine(root, "fixtures", "manifest.json")), JsonOptions) ?? [];
        var failures = 0;
        foreach (var item in cases.Where(c => c.Status == "active"))
        {
            var file = Path.Combine(root, "fixtures", item.File);
            var result = Compiler.Check(file, File.ReadAllText(file));
            var actual = result.Diagnostics.Select(d => d.Code).ToArray();
            var okay = item.ExpectedCode is null ? actual.Length == 0 : actual.Contains(item.ExpectedCode);
            Console.WriteLine($"{(okay ? "PASS" : "FAIL")} {item.File} expected={item.ExpectedCode ?? "valid"} actual={string.Join(',', actual)}");
            if (!okay) failures++;
        }
        Console.WriteLine($"{cases.Count(c => c.Status == "active")} active, {cases.Count(c => c.Status == "pending")} pending, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    private static string? FindRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "fixtures", "manifest.json"))) return directory.FullName;
        return null;
    }
}

internal sealed record FixtureCase(string File, string Status, string? ExpectedCode);
internal sealed record Range(int StartLine, int StartColumn, int EndLine, int EndColumn);
internal sealed record Diagnostic(string Code, string Message, string File, Range Range)
{
    public string Severity => "error";
}
internal sealed record Token(string Kind, string Text, int Line, int Column)
{
    public Range Range => new(Line, Column, Line, Column + Math.Max(Text.Length, 1));
}

internal static class Lexer
{
    public static List<Token> Scan(string source, string file, List<Diagnostic> diagnostics)
    {
        var tokens = new List<Token>();
        var i = 0; var line = 1; var column = 1;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\r') { i++; continue; }
            if (c == '\n') { i++; line++; column = 1; continue; }
            if (char.IsWhiteSpace(c)) { i++; column++; continue; }
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') { i++; column++; }
                continue;
            }
            var start = i; var startColumn = column;
            if (char.IsLetter(c) || c == '_')
            {
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) { i++; column++; }
                tokens.Add(new Token("id", source[start..i], line, startColumn));
                continue;
            }
            if (char.IsDigit(c))
            {
                while (i < source.Length && char.IsDigit(source[i])) { i++; column++; }
                tokens.Add(new Token("number", source[start..i], line, startColumn));
                continue;
            }
            if (c == '-' && i + 1 < source.Length && source[i + 1] == '>')
            {
                tokens.Add(new Token("->", "->", line, column)); i += 2; column += 2; continue;
            }
            if (".;:,(){}=+*-".Contains(c))
            {
                tokens.Add(new Token(c.ToString(), c.ToString(), line, column)); i++; column++; continue;
            }
            diagnostics.Add(new Diagnostic("E_UNSUPPORTED", $"Character '{c}' is not in the implemented language slice", file, new Range(line, column, line, column + 1)));
            i++; column++;
        }
        tokens.Add(new Token("eof", "", line, column));
        return tokens;
    }
}

internal abstract record Expr(Token At);
internal sealed record NumberExpr(Token At, int Value) : Expr(At);
internal sealed record NameExpr(Token At, string Name) : Expr(At);
internal sealed record BinaryExpr(Token At, string Op, Expr Left, Expr Right) : Expr(At);
internal sealed record CallExpr(Token At, string Name, List<Expr> Arguments) : Expr(At);
internal abstract record Stmt(Token At);
internal sealed record LetStmt(Token At, string Name, string Type, Expr Value) : Stmt(At);
internal sealed record ReturnStmt(Token At, Expr Value) : Stmt(At);
internal sealed record Parameter(string Name, string Type, Token At);
internal sealed record Function(string Name, bool Public, List<Parameter> Parameters, string ReturnType, List<Stmt> Body, Token At);
internal sealed record ParsedProgram(string Module, List<Function> Functions);
internal sealed record CheckedProgram(string Module, List<Function> Functions);
internal sealed record CheckResult(CheckedProgram? Program, List<Diagnostic> Diagnostics);

internal sealed class ParseFailure : Exception;

internal sealed class Parser(List<Token> tokens, string file, List<Diagnostic> diagnostics)
{
    private int _position;
    private Token Current => tokens[Math.Min(_position, tokens.Count - 1)];
    private Token Take() { var t = Current; _position++; return t; }
    private bool Is(string text) => Current.Text == text;
    private Token Expect(string text)
    {
        if (Is(text)) return Take();
        Fail(Current, "E_SYNTAX", $"Expected '{text}', found '{Current.Text}'");
        throw new ParseFailure();
    }
    private Token ExpectId()
    {
        if (Current.Kind == "id") return Take();
        Fail(Current, "E_SYNTAX", "Expected identifier");
        throw new ParseFailure();
    }
    private void Fail(Token token, string code, string message)
    {
        diagnostics.Add(new Diagnostic(code, message, file, token.Range));
        throw new ParseFailure();
    }

    public ParsedProgram? Parse()
    {
        try
        {
            Expect("module");
            var module = ExpectId().Text;
            while (Is(".")) { Take(); module += "." + ExpectId().Text; }
            Expect(";");
            var functions = new List<Function>();
            while (Current.Kind != "eof") functions.Add(ParseFunction());
            return new ParsedProgram(module, functions);
        }
        catch (ParseFailure) { return null; }
    }

    private Function ParseFunction()
    {
        var isPublic = Is("pub");
        if (isPublic) Take();
        if (!Is("fn")) Fail(Current, "E_UNSUPPORTED", $"Declaration '{Current.Text}' is not implemented yet");
        Take();
        var name = ExpectId();
        Expect("(");
        var parameters = new List<Parameter>();
        while (!Is(")"))
        {
            var p = ExpectId(); Expect(":"); var type = ExpectId();
            parameters.Add(new Parameter(p.Text, type.Text, p));
            if (!Is(")")) Expect(",");
        }
        Expect(")"); Expect("->"); var returnType = ExpectId().Text;
        Expect("effects"); Expect("{");
        if (!Is("}")) Fail(Current, "E_UNSUPPORTED", "Nonempty effects are not implemented yet");
        Take(); Expect("{");
        var body = new List<Stmt>();
        while (!Is("}"))
        {
            if (Current.Kind == "eof") Fail(Current, "E_SYNTAX", "Unclosed function body");
            if (Is("let"))
            {
                var at = Take(); var local = ExpectId(); Expect(":"); var type = ExpectId(); Expect("=");
                var value = ParseExpr(); Expect(";");
                body.Add(new LetStmt(at, local.Text, type.Text, value));
            }
            else if (Is("return"))
            {
                var at = Take(); var value = ParseExpr(); Expect(";");
                body.Add(new ReturnStmt(at, value));
            }
            else Fail(Current, "E_UNSUPPORTED", $"Statement '{Current.Text}' is not implemented yet");
        }
        Take();
        return new Function(name.Text, isPublic, parameters, returnType, body, name);
    }

    private Expr ParseExpr(int minPrecedence = 0)
    {
        Expr left;
        var token = Current;
        if (token.Kind == "number")
        {
            Take();
            if (!int.TryParse(token.Text, out var value)) Fail(token, "E_TYPE_MISMATCH", "Integer literal is outside i32 range");
            left = new NumberExpr(token, value);
        }
        else if (token.Kind == "id")
        {
            Take();
            if (Is("("))
            {
                Take(); var arguments = new List<Expr>();
                while (!Is(")"))
                {
                    arguments.Add(ParseExpr());
                    if (!Is(")")) Expect(",");
                }
                Take(); left = new CallExpr(token, token.Text, arguments);
            }
            else left = new NameExpr(token, token.Text);
        }
        else if (Is("("))
        {
            Take(); left = ParseExpr(); Expect(")");
        }
        else { Fail(token, "E_UNSUPPORTED", $"Expression '{token.Text}' is not implemented yet"); throw new ParseFailure(); }

        while (true)
        {
            var precedence = Current.Text is "*" ? 2 : Current.Text is "+" or "-" ? 1 : 0;
            if (precedence < minPrecedence || precedence == 0) break;
            var op = Take();
            var right = ParseExpr(precedence + 1);
            left = new BinaryExpr(op, op.Text, left, right);
        }
        return left;
    }
}

internal static class Compiler
{
    public static CheckResult Check(string file, string source)
    {
        var diagnostics = new List<Diagnostic>();
        var tokens = Lexer.Scan(source, file, diagnostics);
        if (diagnostics.Count != 0) return new CheckResult(null, diagnostics);
        var program = new Parser(tokens, file, diagnostics).Parse();
        if (program is null) return new CheckResult(null, diagnostics);
        var functions = new Dictionary<string, Function>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (!functions.TryAdd(function.Name, function)) Add("E_NAME_DUPLICATE", $"Function '{function.Name}' is already declared", function.At);
            if (function.ReturnType != "i32") Add("E_UNSUPPORTED", $"Return type '{function.ReturnType}' is not implemented yet", function.At);
            var locals = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in function.Parameters)
            {
                if (parameter.Type != "i32") Add("E_UNSUPPORTED", $"Parameter type '{parameter.Type}' is not implemented yet", parameter.At);
                if (!locals.Add(parameter.Name)) Add("E_NAME_DUPLICATE", $"Parameter '{parameter.Name}' is already declared", parameter.At);
            }
            if (function.Body.Count == 0 || function.Body[^1] is not ReturnStmt) Add("E_TYPE_MISMATCH", "Function must end with a return value", function.At);
        }
        foreach (var function in program.Functions)
        {
            var locals = function.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var statement in function.Body)
            {
                switch (statement)
                {
                    case LetStmt let:
                        if (let.Type != "i32") Add("E_UNSUPPORTED", $"Local type '{let.Type}' is not implemented yet", let.At);
                        CheckExpr(let.Value, locals);
                        if (!locals.Add(let.Name)) Add("E_NAME_DUPLICATE", $"Local '{let.Name}' is already declared", let.At);
                        break;
                    case ReturnStmt ret: CheckExpr(ret.Value, locals); break;
                }
            }
        }
        return diagnostics.Count == 0
            ? new CheckResult(new CheckedProgram(program.Module, program.Functions), diagnostics)
            : new CheckResult(null, diagnostics);

        void Add(string code, string message, Token token) => diagnostics.Add(new Diagnostic(code, message, file, token.Range));
        void CheckExpr(Expr expression, HashSet<string> locals)
        {
            switch (expression)
            {
                case NameExpr name:
                    if (!locals.Contains(name.Name)) Add("E_NAME_UNRESOLVED", $"Name '{name.Name}' is not in scope", name.At);
                    break;
                case BinaryExpr binary:
                    CheckExpr(binary.Left, locals); CheckExpr(binary.Right, locals); break;
                case CallExpr call:
                    if (!functions.TryGetValue(call.Name, out var callee)) Add("E_NAME_UNRESOLVED", $"Function '{call.Name}' is not declared", call.At);
                    else if (callee.Parameters.Count != call.Arguments.Count) Add("E_TYPE_MISMATCH", $"Function '{call.Name}' expects {callee.Parameters.Count} arguments, got {call.Arguments.Count}", call.At);
                    foreach (var arg in call.Arguments) CheckExpr(arg, locals);
                    break;
            }
        }
    }
}

internal static class Emitter
{
    public static string Emit(CheckedProgram program)
    {
        var source = new StringBuilder("using System;\ninternal static class Generated {\n");
        foreach (var function in program.Functions)
        {
            source.Append("    internal static int Fn_").Append(function.Name).Append('(');
            source.Append(string.Join(", ", function.Parameters.Select(p => "int Var_" + p.Name)));
            source.AppendLine(") {");
            foreach (var statement in function.Body)
            {
                switch (statement)
                {
                    case LetStmt let:
                        source.Append("        int Var_").Append(let.Name).Append(" = ").Append(EmitExpr(let.Value)).AppendLine(";"); break;
                    case ReturnStmt ret:
                        source.Append("        return ").Append(EmitExpr(ret.Value)).AppendLine(";"); break;
                }
            }
            source.AppendLine("    }");
        }
        source.AppendLine("    public static void Main() { Console.WriteLine(Fn_main()); }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static string EmitExpr(Expr expression) => expression switch
    {
        NumberExpr number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        NameExpr name => "Var_" + name.Name,
        BinaryExpr binary => $"checked({EmitExpr(binary.Left)} {binary.Op} {EmitExpr(binary.Right)})",
        CallExpr call => $"Fn_{call.Name}({string.Join(", ", call.Arguments.Select(EmitExpr))})",
        _ => throw new InvalidOperationException("Unchecked expression reached emitter")
    };
}
