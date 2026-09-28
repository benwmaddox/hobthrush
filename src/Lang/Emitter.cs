using System.Globalization;
using System.Text;
using System.Text.Json;

internal static class Emitter
{
    public static string Emit(
        CheckedProgram program,
        bool executable = true,
        WebDatabaseOptions? webDatabaseOptions = null)
    {
        var entry = program.EntryFunctionId is int entryId
            ? program.Functions.FirstOrDefault(function =>
                function.Id == entryId && function.Name == "main" && function.TypeParameters.Count == 0 &&
                function.Parameters.Count == 0 &&
                (function.ReturnType.IsI32 || function.ReturnType.IsBool || function.ReturnType.IsText))
            : null;
        var entryCommand = program.EntryCommandId is int commandId
            ? program.Commands.FirstOrDefault(command => command.Id == commandId)
            : null;
        var webHost = executable && program.Routes.Count != 0;
        if (executable && entry is null && entryCommand is null && !webHost)
            throw new InvalidOperationException("Executable emission requires a selected valid entry function");

        var emitter = new SourceEmitter(program, webDatabaseOptions: webDatabaseOptions);
        return emitter.Emit(entry, executable, command: entryCommand, webHost: webHost);
    }

    public static string EmitOpenApi(CheckedProgram program)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("openapi", "3.1.0");
            writer.WriteStartObject("info");
            writer.WriteString("title", program.EntryModule ?? "Lang API");
            writer.WriteString("version", "1.0.0");
            writer.WriteEndObject();

            writer.WriteStartObject("paths");
            foreach (var pathGroup in program.Routes
                         .OrderBy(route => route.Path, StringComparer.Ordinal)
                         .ThenBy(route => RouteMethodOrder(route.Method))
                         .GroupBy(route => route.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject(pathGroup.Key);
                foreach (var route in pathGroup.OrderBy(route => RouteMethodOrder(route.Method)))
                    WriteOpenApiOperation(writer, program, route);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();

            var componentStructIds = OpenApiComponentStructIds(program);
            if (componentStructIds.Count != 0)
            {
                writer.WriteStartObject("components");
                writer.WriteStartObject("schemas");
                foreach (var structure in program.Structs.Where(item => componentStructIds.Contains(item.Id)).OrderBy(item => item.Id))
                {
                    writer.WriteStartObject(OpenApiStructName(structure.Id));
                    writer.WriteString("title", structure.Name);
                    writer.WriteString("type", "object");
                    writer.WriteStartObject("properties");
                    foreach (var field in structure.Fields.OrderBy(field => field.Index))
                    {
                        writer.WritePropertyName(field.Name);
                        WriteOpenApiTypeSchema(writer, field.Type);
                    }
                    writer.WriteEndObject();
                    writer.WriteStartArray("required");
                    foreach (var field in structure.Fields.OrderBy(field => field.Index))
                        writer.WriteStringValue(field.Name);
                    writer.WriteEndArray();
                    writer.WriteBoolean("additionalProperties", false);
                    writer.WriteEndObject();
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static int RouteMethodOrder(string method) => method switch
    {
        "GET" => 0,
        "POST" => 1,
        _ => 2
    };

    private static string OpenApiStructName(int id) => "Struct_" + id.ToString(CultureInfo.InvariantCulture);

    private static void WriteOpenApiOperation(Utf8JsonWriter writer, CheckedProgram program, CheckedRoute route)
    {
        writer.WriteStartObject(route.Method.ToLowerInvariant());
        if (route.BodyType is not null)
        {
            writer.WriteStartObject("requestBody");
            writer.WriteBoolean("required", true);
            writer.WriteStartObject("content");
            writer.WriteStartObject("application/json");
            writer.WritePropertyName("schema");
            WriteOpenApiTypeSchema(writer, route.BodyType);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteStartObject("responses");
        foreach (var statusGroup in route.Responses.OrderBy(response => response.StatusCode)
                     .ThenBy(response => response.VariantId).GroupBy(response => response.StatusCode))
        {
            var responses = statusGroup.OrderBy(response => response.VariantId).ToArray();
            writer.WriteStartObject(statusGroup.Key.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("description", string.Join(", ", responses.Select(response => response.VariantName)));

            if (!IsBodyForbiddenStatus(statusGroup.Key))
            {
                var jsonPayloads = responses
                    .Where(response => response.ContentKind == CheckedRouteContentKind.Json && response.PayloadType is not null)
                    .Select(response => response.PayloadType!)
                    .Distinct()
                    .ToArray();
                var htmlPayloads = responses
                    .Where(response => response.ContentKind == CheckedRouteContentKind.Html)
                    .ToArray();
                if (jsonPayloads.Length != 0 || htmlPayloads.Length != 0)
                {
                    writer.WriteStartObject("content");
                    if (jsonPayloads.Length != 0)
                    {
                        writer.WriteStartObject("application/json");
                        writer.WritePropertyName("schema");
                        if (jsonPayloads.Length == 1)
                            WriteOpenApiTypeSchema(writer, jsonPayloads[0]);
                        else
                        {
                            writer.WriteStartObject();
                            writer.WriteStartArray("oneOf");
                            foreach (var payload in jsonPayloads)
                                WriteOpenApiTypeSchema(writer, payload);
                            writer.WriteEndArray();
                            writer.WriteEndObject();
                        }
                        writer.WriteEndObject();
                    }
                    if (htmlPayloads.Length != 0)
                    {
                        writer.WriteStartObject("text/html");
                        writer.WriteStartObject("schema");
                        writer.WriteString("type", "string");
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static bool IsBodyForbiddenStatus(int statusCode) =>
        statusCode is >= 100 and < 200 or 204 or 205 or 304;

    private static void WriteOpenApiTypeSchema(Utf8JsonWriter writer, LangType type)
    {
        switch (type.Kind)
        {
            case LangTypeKind.I32:
                writer.WriteStartObject();
                writer.WriteString("type", "integer");
                writer.WriteString("format", "int32");
                writer.WriteEndObject();
                break;
            case LangTypeKind.Bool:
                writer.WriteStartObject();
                writer.WriteString("type", "boolean");
                writer.WriteEndObject();
                break;
            case LangTypeKind.Text:
                writer.WriteStartObject();
                writer.WriteString("type", "string");
                writer.WriteEndObject();
                break;
            case LangTypeKind.Struct:
                writer.WriteStartObject();
                writer.WriteString("$ref", "#/components/schemas/" + OpenApiStructName(type.StructId));
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException("Unsupported type in checked OpenAPI schema");
        }
    }

    private static HashSet<int> OpenApiComponentStructIds(CheckedProgram program)
    {
        var result = new HashSet<int>();
        var pending = new Stack<int>();
        foreach (var route in program.Routes)
        {
            if (route.BodyType?.Kind == LangTypeKind.Struct)
                pending.Push(route.BodyType.StructId);
            foreach (var response in route.Responses.Where(response => response.ContentKind == CheckedRouteContentKind.Json))
                if (response.PayloadType?.Kind == LangTypeKind.Struct)
                    pending.Push(response.PayloadType.StructId);
        }

        while (pending.Count != 0)
        {
            var id = pending.Pop();
            if (!result.Add(id)) continue;
            var structure = program.Structs.Single(item => item.Id == id);
            foreach (var field in structure.Fields)
                if (field.Type.Kind == LangTypeKind.Struct)
                    pending.Push(field.Type.StructId);
        }

        return result;
    }

    public static string EmitCommandSchema(CheckedProgram program)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 2);
            writer.WriteStartArray("commands");
            foreach (var command in program.Commands.OrderBy(command => command.Id))
            {
                writer.WriteStartObject();
                writer.WriteString("name", command.Name);
                writer.WriteString("help", command.Help);
                writer.WriteString("handler", command.HandlerReference);
                writer.WriteString("error_formatter", command.ErrorReference);
                writer.WriteStartArray("capabilities");
                foreach (var capability in command.Capabilities.OrderBy(capability => capability.HandlerParameterIndex))
                    writer.WriteStringValue(CapabilityName(capability.Kind));
                writer.WriteEndArray();
                WriteCommandInputs(writer, command, CheckedCommandInputKind.Argument, includeDefault: false);
                WriteCommandInputs(writer, command, CheckedCommandInputKind.Option, includeDefault: true);
                WriteCommandInputs(writer, command, CheckedCommandInputKind.Flag, includeDefault: false);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static string CapabilityName(CheckedCapabilityKind kind) => kind switch
    {
        CheckedCapabilityKind.FsRead => "fs.read",
        CheckedCapabilityKind.FsWrite => "fs.write",
        CheckedCapabilityKind.DbRead => "db.read",
        CheckedCapabilityKind.DbWrite => "db.write",
        _ => throw new InvalidOperationException("Unknown checked capability")
    };

    private static void WriteCommandInputs(
        Utf8JsonWriter writer,
        CheckedCommand command,
        CheckedCommandInputKind kind,
        bool includeDefault)
    {
        var propertyName = kind switch
        {
            CheckedCommandInputKind.Argument => "arguments",
            CheckedCommandInputKind.Option => "options",
            CheckedCommandInputKind.Flag => "flags",
            _ => throw new InvalidOperationException("Unknown checked command input kind")
        };

        writer.WriteStartArray(propertyName);
        foreach (var input in command.Inputs.Where(input => input.Kind == kind).OrderBy(input => input.FieldIndex))
        {
            writer.WriteStartObject();
            writer.WriteString("name", input.Name);
            writer.WriteString("type", CommandTypeName(input.Type));
            if (includeDefault)
            {
                writer.WritePropertyName("default");
                WriteCommandLiteral(writer, input.Default);
            }
            writer.WriteString("help", input.Help);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteCommandLiteral(Utf8JsonWriter writer, CheckedCommandLiteral? literal)
    {
        if (literal is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (literal.Kind)
        {
            case CheckedCommandLiteralKind.Text:
                writer.WriteStringValue(literal.TextValue);
                break;
            case CheckedCommandLiteralKind.I32:
                writer.WriteNumberValue(literal.IntegerValue!.Value);
                break;
            case CheckedCommandLiteralKind.Boolean:
                writer.WriteBooleanValue(literal.BooleanValue!.Value);
                break;
            default:
                throw new InvalidOperationException("Unknown checked command literal kind");
        }
    }

    private static string CommandTypeName(LangType type) => type.Kind switch
    {
        LangTypeKind.FilePath => "FilePath",
        LangTypeKind.Text => "Text",
        LangTypeKind.I32 => "i32",
        LangTypeKind.Bool => "bool",
        _ => throw new InvalidOperationException("Unsupported checked command input type")
    };

    public static string EmitTests(CheckedProgram program, string? rootPackageId = null)
    {
        var tests = program.Tests
            .Where(test => rootPackageId is null || test.PackageId == rootPackageId)
            .ToArray();
        var testFunctionIds = tests.Select(test => test.FunctionId).ToHashSet();
        var emitter = new SourceEmitter(program, testFunctionIds);
        return emitter.Emit(entry: null, executable: false, tests: tests);
    }

    private sealed class SourceEmitter(
        CheckedProgram program,
        IReadOnlySet<int>? includedTestFunctionIds = null,
        WebDatabaseOptions? webDatabaseOptions = null)
    {
        private readonly StringBuilder _source = new();
        private CheckedFunction? _emittingFunction;
        private readonly HashSet<int> _testFunctionIds = program.Tests
            .Select(test => test.FunctionId)
            .ToHashSet();
        private readonly IReadOnlySet<int> _includedTestFunctionIds = includedTestFunctionIds ?? new HashSet<int>();
        private readonly WebDatabaseOptions? _webDatabaseOptions = webDatabaseOptions;

        private IEnumerable<CheckedFunction> EmittedFunctions => program.Functions.Where(function =>
            !_testFunctionIds.Contains(function.Id) || _includedTestFunctionIds.Contains(function.Id));

        public string Emit(
            CheckedFunction? entry,
            bool executable,
            IReadOnlyList<CheckedTest>? tests = null,
            CheckedCommand? command = null,
            bool webHost = false)
        {
            _source.AppendLine("using System;");
            _source.AppendLine("using System.Globalization;");
            if (webHost)
            {
                _source.AppendLine("using System.IO;");
                _source.AppendLine("using System.Diagnostics;");
                _source.AppendLine("using System.Text;");
                _source.AppendLine("using System.Text.Json;");
                _source.AppendLine("using System.Threading;");
                _source.AppendLine("using System.Threading.Tasks;");
                _source.AppendLine("using Microsoft.AspNetCore.Builder;");
                _source.AppendLine("using Microsoft.AspNetCore.Http;");
                _source.AppendLine("using Microsoft.Extensions.Logging;");
                if (UsesDatabase)
                    _source.AppendLine("using Microsoft.Data.Sqlite;");
            }
            else
            {
                if (UsesAsyncFunctions)
                {
                    _source.AppendLine("using System.Threading;");
                    _source.AppendLine("using System.Threading.Tasks;");
                }
                if (UsesDatabase)
                    _source.AppendLine("using Microsoft.Data.Sqlite;");
            }
            if (UsesFsReadText || UsesFsReadTextAsync || UsesFsWriteText)
            {
                if (!webHost) _source.AppendLine("using System.IO;");
                _source.AppendLine("using System.Security;");
                if (!webHost) _source.AppendLine("using System.Text;");
            }
            else if (!webHost && (UsesTextLength || UsesHtmlBuilders || command is not null))
            {
                _source.AppendLine("using System.Text;");
            }
            _source.AppendLine();
            _source.AppendLine("public static class LangModule");
            _source.AppendLine("{");

            EmitBuiltinTypes();
            if (NeedsFilePathType) EmitFilePathType();
            if (NeedsHtmlType || webHost) EmitHtmlType();
            if (NeedsFsReadType) EmitFsReadType();
            if (NeedsFsWriteType) EmitFsWriteType();
            if (NeedsFsErrorType) EmitFsErrorType();
            if (NeedsDbReadType) EmitDbReadType();
            if (NeedsDbWriteType) EmitDbWriteType();
            if (NeedsDbTransactionType) EmitDbTransactionType();
            if (NeedsDbErrorType) EmitDbErrorType();
            foreach (var union in program.Unions) EmitUnion(union);
            foreach (var structure in program.Structs) EmitStruct(structure);
            foreach (var function in EmittedFunctions) EmitFunction(function);
            if (UsesFsReadText) EmitFsReadTextHelper();
            if (UsesFsReadTextAsync) EmitFsReadTextAsyncHelper();
            if (UsesFsWriteText) EmitFsWriteTextHelper();
            if (UsesTextLength) EmitTextLengthHelper();
            if (UsesListGet) EmitListGetHelper();
            if (UsesTextSplit) EmitTextSplitHelper();
            if (UsesHtmlBuilders) EmitHtmlHelpers();
            if (UsesDatabase) EmitDatabaseHelpers();
            EmitArithmeticHelpers();
            if (tests is not null)
                EmitTestEntryPoint(tests);
            else if (command is not null)
                EmitCommandEntryPoint(command);
            else if (webHost)
                EmitWebHost();
            else if (executable)
                EmitEntryPoint(entry!);

            _source.AppendLine("}");
            return _source.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private void EmitBuiltinTypes()
        {
            _source.AppendLine("    public abstract record Option<T>");
            _source.AppendLine("    {");
            _source.AppendLine("        public sealed record Some(T Value) : Option<T>;");
            _source.AppendLine("        public sealed record None() : Option<T>;");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    public abstract record Result<T, E>");
            _source.AppendLine("    {");
            _source.AppendLine("        public sealed record Ok(T Value) : Result<T, E>;");
            _source.AppendLine("        public sealed record Err(E Error) : Result<T, E>;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsReadType()
        {
            _source.AppendLine("    public sealed class FsRead");
            _source.AppendLine("    {");
            _source.AppendLine("        internal FsRead() : this(System.Threading.CancellationToken.None) { }");
            _source.AppendLine("        internal FsRead(System.Threading.CancellationToken cancellationToken) => CancellationToken = cancellationToken;");
            _source.AppendLine("        internal System.Threading.CancellationToken CancellationToken { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsWriteType()
        {
            _source.AppendLine("    public sealed class FsWrite");
            _source.AppendLine("    {");
            _source.AppendLine("        internal FsWrite() { }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFilePathType()
        {
            _source.AppendLine("    public sealed class FilePath");
            _source.AppendLine("    {");
            _source.AppendLine("        internal FilePath(string value) => Value = value;");
            _source.AppendLine("        internal string Value { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitHtmlType()
        {
            _source.AppendLine("    public sealed class Html");
            _source.AppendLine("    {");
            _source.AppendLine("        internal Html(string value) => Value = value;");
            _source.AppendLine("        internal string Value { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsErrorType()
        {
            _source.AppendLine("    public abstract record FsError");
            _source.AppendLine("    {");
            _source.AppendLine("        public sealed record NotFound() : FsError;");
            _source.AppendLine("        public sealed record PermissionDenied() : FsError;");
            _source.AppendLine("        public sealed record InvalidPath() : FsError;");
            _source.AppendLine("        public sealed record InvalidText() : FsError;");
            _source.AppendLine("        public sealed record Io() : FsError;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitDbReadType()
        {
            _source.AppendLine("    public sealed class DbRead");
            _source.AppendLine("    {");
            _source.AppendLine("        internal DbRead(string connectionString, System.Threading.CancellationToken cancellationToken) { ConnectionString = connectionString; CancellationToken = cancellationToken; }");
            _source.AppendLine("        internal string ConnectionString { get; }");
            _source.AppendLine("        internal System.Threading.CancellationToken CancellationToken { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitDbWriteType()
        {
            _source.AppendLine("    public sealed class DbWrite");
            _source.AppendLine("    {");
            _source.AppendLine("        internal DbWrite(string connectionString, System.Threading.CancellationToken cancellationToken) { ConnectionString = connectionString; CancellationToken = cancellationToken; }");
            _source.AppendLine("        internal string ConnectionString { get; }");
            _source.AppendLine("        internal System.Threading.CancellationToken CancellationToken { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitDbTransactionType()
        {
            _source.AppendLine("    public sealed class DbTransaction : IDisposable");
            _source.AppendLine("    {");
            _source.AppendLine("        private readonly SqliteConnection _connection;");
            _source.AppendLine("        private readonly SqliteTransaction _transaction;");
            _source.AppendLine("        private readonly System.Threading.CancellationToken _cancellationToken;");
            _source.AppendLine("        private bool _failed;");
            _source.AppendLine("        private bool _committed;");
            _source.AppendLine("        private bool _disposed;");
            _source.AppendLine("        internal DbTransaction(SqliteConnection connection, SqliteTransaction transaction, System.Threading.CancellationToken cancellationToken) { _connection = connection; _transaction = transaction; _cancellationToken = cancellationToken; }");
            _source.AppendLine("        internal SqliteConnection Connection => _connection;");
            _source.AppendLine("        internal SqliteTransaction Transaction => _transaction;");
            _source.AppendLine("        internal System.Threading.CancellationToken CancellationToken => _cancellationToken;");
            _source.AppendLine("        internal bool CanExecute => !_failed && !_committed && !_disposed;");
            _source.AppendLine("        internal bool CanCommit => !_failed && !_committed && !_disposed;");
            _source.AppendLine("        internal void MarkFailed() => _failed = true;");
            _source.AppendLine("        internal void MarkCommitted() => _committed = true;");
            _source.AppendLine("        public void Dispose()");
            _source.AppendLine("        {");
            _source.AppendLine("            if (_disposed) return;");
            _source.AppendLine("            _disposed = true;");
            _source.AppendLine("            // Rollback ignores request cancellation so cleanup still runs after an aborted request.");
            _source.AppendLine("            try { if (!_committed) _transaction.Rollback(); }");
            _source.AppendLine("            finally");
            _source.AppendLine("            {");
            _source.AppendLine("                try { _transaction.Dispose(); }");
            _source.AppendLine("                finally { _connection.Dispose(); }");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitDbErrorType()
        {
            _source.AppendLine("    public abstract record DbError");
            _source.AppendLine("    {");
            _source.AppendLine("        public sealed record Statement() : DbError;");
            _source.AppendLine("        public sealed record RowShape() : DbError;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitUnion(CheckedUnion union)
        {
            var accessibility = union.Public ? "public" : "private";
            _source.Append("    ").Append(accessibility).Append(" abstract record Union_").Append(union.Id).AppendLine();
            _source.AppendLine("    {");
            foreach (var variant in union.Variants)
            {
                _source.Append("        public sealed record Variant_")
                    .Append(union.Id).Append('_').Append(variant.Id).Append('(');
                for (var i = 0; i < variant.Fields.Count; i++)
                {
                    if (i != 0) _source.Append(", ");
                    _source.Append(EmitType(variant.Fields[i].Type)).Append(" Payload_").Append(i);
                }
                _source.Append(") : Union_").Append(union.Id).AppendLine(";");
            }
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitStruct(CheckedStruct structure)
        {
            _source.Append("    ").Append(structure.Public ? "public" : "private")
                .Append(" sealed record Struct_").Append(structure.Id.ToString(CultureInfo.InvariantCulture)).Append('(');
            for (var i = 0; i < structure.Fields.Count; i++)
            {
                if (i != 0) _source.Append(", ");
                var field = structure.Fields[i];
                _source.Append(EmitType(field.Type)).Append(" Field_")
                    .Append(field.Index.ToString(CultureInfo.InvariantCulture));
            }
            _source.AppendLine(");");
            _source.AppendLine();
        }

        private void EmitFunction(CheckedFunction function)
        {
            _emittingFunction = function;
            _source.Append("    ").Append(function.Public ? "public" : "private").Append(" static ")
                .Append(function.IsAsync ? "async Task<" + EmitType(function.ReturnType) + ">" : EmitType(function.ReturnType))
                .Append(" Function_").Append(function.Id);
            if (function.TypeParameters.Count != 0)
            {
                var typeParameterNames = function.TypeParameters.Select((_, index) => TypeParameterName(index));
                _source.Append('<').Append(string.Join(", ", typeParameterNames)).Append('>');
            }
            _source.Append('(');
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (i != 0) _source.Append(", ");
                var parameter = function.Parameters[i];
                _source.Append(EmitType(parameter.Type)).Append(" Local_").Append(parameter.LocalId);
            }
            if (function.IsAsync)
            {
                if (function.Parameters.Count != 0) _source.Append(", ");
                _source.Append("CancellationToken cancellationToken");
            }
            _source.AppendLine(")");
            _source.AppendLine("    {");
            if (function.IsAsync)
                _source.AppendLine("        await Task.CompletedTask;");
            EmitStatements(function.Body, 2);
            _source.AppendLine("    }");
            _source.AppendLine();
            _emittingFunction = null;
        }

        private void EmitStatements(IEnumerable<TypedStmt> statements, int indent)
        {
            foreach (var statement in statements) EmitStatement(statement, indent);
        }

        private void EmitStatement(TypedStmt statement, int indent)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    Indent(indent);
                    _source.Append(EmitType(let.Type)).Append(" Local_").Append(let.LocalId)
                        .Append(" = ").Append(EmitExpr(let.Value)).AppendLine(";");
                    break;
                case TypedAssignStmt assignment:
                    Indent(indent);
                    _source.Append("Local_").Append(assignment.LocalId).Append(" = ")
                        .Append(EmitExpr(assignment.Value)).AppendLine(";");
                    break;
                case TypedReturnStmt ret:
                    Indent(indent);
                    _source.Append("return ").Append(EmitExpr(ret.Value)).AppendLine(";");
                    break;
                case TypedIfStmt conditional:
                    Indent(indent);
                    _source.Append("if (").Append(EmitExpr(conditional.Condition)).AppendLine(")");
                    Indent(indent);
                    _source.AppendLine("{");
                    EmitStatements(conditional.ThenBody, indent + 1);
                    Indent(indent);
                    _source.AppendLine("}");
                    if (conditional.ElseBody is not null)
                    {
                        Indent(indent);
                        _source.AppendLine("else");
                        Indent(indent);
                        _source.AppendLine("{");
                        EmitStatements(conditional.ElseBody, indent + 1);
                        Indent(indent);
                        _source.AppendLine("}");
                    }
                    break;
                case TypedForStmt loop:
                    Indent(indent);
                    _source.Append("foreach (var Local_").Append(loop.Item.LocalId.ToString(CultureInfo.InvariantCulture))
                        .Append(" in ").Append(EmitExpr(loop.Collection)).AppendLine(")");
                    Indent(indent);
                    _source.AppendLine("{");
                    EmitStatements(loop.Body, indent + 1);
                    Indent(indent);
                    _source.AppendLine("}");
                    break;
                case TypedWithTransactionStmt transaction:
                    Indent(indent);
                    _source.Append("using (var Local_")
                        .Append(transaction.TransactionLocalId.ToString(CultureInfo.InvariantCulture))
                        .Append(" = DatabaseBeginTransaction(").Append(EmitExpr(transaction.Database)).AppendLine("))");
                    Indent(indent);
                    _source.AppendLine("{");
                    EmitStatements(transaction.Body, indent + 1);
                    Indent(indent);
                    _source.AppendLine("}");
                    break;
                default:
                    throw new InvalidOperationException("Unknown typed statement in emitter");
            }
        }

        private string EmitExpr(TypedExpr expression) => expression switch
        {
            TypedNumberExpr number => number.Value.ToString(CultureInfo.InvariantCulture),
            TypedBoolExpr boolean => boolean.Value ? "true" : "false",
            TypedTextExpr text => JsonSerializer.Serialize(text.Value),
            TypedListExpr list => EmitList(list),
            TypedLocalExpr local => "Local_" + local.LocalId.ToString(CultureInfo.InvariantCulture),
            TypedBinaryExpr binary => EmitBinary(binary),
            TypedCompareExpr comparison => EmitComparison(comparison),
            TypedCallExpr { IsAsync: true } => throw new InvalidOperationException("Async calls must be emitted beneath a checked await expression"),
            TypedCallExpr call => EmitCall(call),
            TypedDatabaseCallExpr databaseCall => EmitDatabaseCall(databaseCall),
            TypedTransactionCommitExpr commit =>
                "DatabaseTransactionCommit(Local_" + commit.TransactionLocalId.ToString(CultureInfo.InvariantCulture) + ")",
            TypedTextLengthExpr length => "TextLength(" + EmitExpr(length.Target) + ")",
            TypedTextTrimExpr trim => "(" + EmitExpr(trim.Target) + ").Trim()",
            TypedListLengthExpr length => "(" + EmitExpr(length.Target) + ").Length",
            TypedListGetExpr get => "ListGet(" + EmitExpr(get.Target) + ", " + EmitExpr(get.Index) + ")",
            TypedListAppendExpr append => "(" + EmitExpr(append.Target) + ").Add(" + EmitExpr(append.Value) + ")",
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.FsReadTextAsync } => throw new InvalidOperationException("Async intrinsics must be emitted beneath a checked await expression"),
            TypedIntrinsicCallExpr intrinsic => EmitIntrinsicCall(intrinsic),
            TypedAwaitExpr awaited => EmitAwait(awaited),
            TypedBuiltinConstructExpr builtin => EmitBuiltinConstruct(builtin),
            TypedUnionConstructExpr variant => EmitUnionConstruct(variant),
            TypedStructConstructExpr structure => EmitStructConstruct(structure),
            TypedFieldAccessExpr field => "(" + EmitExpr(field.Target) + ").Field_" +
                field.FieldIndex.ToString(CultureInfo.InvariantCulture),
            TypedMatchExpr match => EmitMatch(match),
            _ => throw new InvalidOperationException("Unchecked expression reached emitter")
        };

        private string EmitList(TypedListExpr expression)
        {
            var itemType = EmitType(expression.Type.Arguments[0]);
            const string immutableArray = "global::System.Collections.Immutable.ImmutableArray";
            if (expression.Items.Count == 0)
                return immutableArray + "<" + itemType + ">.Empty";
            return immutableArray + ".Create<" + itemType + ">(" +
                string.Join(", ", expression.Items.Select(EmitExpr)) + ")";
        }

        private string EmitDatabaseCall(TypedDatabaseCallExpr call)
        {
            var parameters = EmitExpr(call.Parameters);
            var parameterStructId = call.Operation.ParameterStructId.ToString(CultureInfo.InvariantCulture);
            var bindMethod = "BindDatabaseParameters_" + parameterStructId;
            var sql = JsonSerializer.Serialize(call.Operation.Sql);
            if (call.Operation.Kind == CheckedDatabaseOperationKind.QueryOne)
            {
                var rowStructId = call.Operation.RowStructId?.ToString(CultureInfo.InvariantCulture)
                    ?? throw new InvalidOperationException("Checked database query has no row struct");
                return "DatabaseQueryOne(" + EmitExpr(call.Receiver) + ", " + sql + ", " + parameters +
                    ", " + bindMethod + ", GetDatabaseColumnOrdinals_" + rowStructId +
                    ", ReadDatabaseRow_" + rowStructId + ")";
            }

            if (call.Operation.Kind == CheckedDatabaseOperationKind.Execute)
                return "DatabaseExecute(" + EmitExpr(call.Receiver) + ", " + sql + ", " + parameters + ", " + bindMethod + ")";

            if (call.Operation.Kind == CheckedDatabaseOperationKind.TransactionExecute)
                return "DatabaseTransactionExecute(" + EmitExpr(call.Receiver) + ", " + sql + ", " + parameters + ", " + bindMethod + ")";

            throw new InvalidOperationException("Unknown checked database operation");
        }

        private string EmitCall(TypedCallExpr call)
        {
            var functionName = "Function_" + call.FunctionId.ToString(CultureInfo.InvariantCulture);
            if (call.TypeArguments.Count != 0)
                functionName += "<" + string.Join(", ", call.TypeArguments.Select(EmitType)) + ">";
            var arguments = call.Arguments.Select(EmitExpr).ToList();
            if (call.IsAsync)
            {
                if (_emittingFunction?.IsAsync != true)
                    throw new InvalidOperationException("An async call has no enclosing async function token");
                arguments.Add("cancellationToken");
            }
            return functionName + "(" + string.Join(", ", arguments) + ")";
        }

        private string EmitAwait(TypedAwaitExpr expression) => expression.Value switch
        {
            TypedCallExpr { IsAsync: true } call => "await " + EmitCall(call),
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.FsReadTextAsync } intrinsic =>
                "await " + EmitIntrinsicCall(intrinsic),
            _ => throw new InvalidOperationException("Await expression has no checked async target")
        };

        private string EmitIntrinsicCall(TypedIntrinsicCallExpr expression) => expression.Intrinsic switch
        {
            BuiltinIntrinsic.FsReadText when expression.Arguments.Count == 2 =>
                EmitFsReadText(expression),
            BuiltinIntrinsic.FsReadText =>
                throw new InvalidOperationException("FsRead.read_text requires a receiver and a path"),
            BuiltinIntrinsic.FsReadTextAsync when expression.Arguments.Count == 2 =>
                EmitFsReadTextAsync(expression),
            BuiltinIntrinsic.FsReadTextAsync =>
                throw new InvalidOperationException("FsRead.read_text_async requires a receiver and a path"),
            BuiltinIntrinsic.FsWriteText when expression.Arguments.Count == 3 =>
                EmitFsWriteText(expression),
            BuiltinIntrinsic.FsWriteText =>
                throw new InvalidOperationException("FsWrite.write_text requires a receiver, path, and value"),
            BuiltinIntrinsic.HtmlText when expression.Arguments.Count == 1 =>
                "HtmlText(" + EmitExpr(expression.Arguments[0]) + ")",
            BuiltinIntrinsic.HtmlHeading when expression.Arguments.Count == 1 =>
                "HtmlHeading(" + EmitExpr(expression.Arguments[0]) + ")",
            BuiltinIntrinsic.HtmlParagraph when expression.Arguments.Count == 1 =>
                "HtmlParagraph(" + EmitExpr(expression.Arguments[0]) + ")",
            BuiltinIntrinsic.HtmlConcat when expression.Arguments.Count == 2 =>
                "HtmlConcat(" + EmitExpr(expression.Arguments[0]) + ", " + EmitExpr(expression.Arguments[1]) + ")",
            BuiltinIntrinsic.HtmlDocument when expression.Arguments.Count == 2 =>
                "HtmlDocument(" + EmitExpr(expression.Arguments[0]) + ", " + EmitExpr(expression.Arguments[1]) + ")",
            BuiltinIntrinsic.TextSplit when expression.Arguments.Count == 2 =>
                "TextSplit(" + EmitExpr(expression.Arguments[0]) + ", " + EmitExpr(expression.Arguments[1]) + ")",
            _ => throw new InvalidOperationException("Unknown builtin intrinsic")
        };

        private string EmitFsReadText(TypedIntrinsicCallExpr expression)
        {
            var receiver = EmitExpr(expression.Arguments[0]);
            var path = expression.Arguments[1];
            var emittedPath = path.Type.IsFilePath
                ? "(" + EmitExpr(path) + ").Value"
                : EmitExpr(path);
            return "ReadText(" + receiver + ", " + emittedPath + ")";
        }

        private string EmitFsReadTextAsync(TypedIntrinsicCallExpr expression)
        {
            var receiver = EmitExpr(expression.Arguments[0]);
            var path = expression.Arguments[1];
            var emittedPath = path.Type.IsFilePath
                ? "(" + EmitExpr(path) + ").Value"
                : EmitExpr(path);
            return "ReadTextAsync(" + receiver + ", " + emittedPath + ")";
        }

        private string EmitFsWriteText(TypedIntrinsicCallExpr expression)
        {
            var receiver = EmitExpr(expression.Arguments[0]);
            var path = expression.Arguments[1];
            var emittedPath = path.Type.IsFilePath
                ? "(" + EmitExpr(path) + ").Value"
                : EmitExpr(path);
            return "WriteText(" + receiver + ", " + emittedPath + ", " + EmitExpr(expression.Arguments[2]) + ")";
        }

        private string EmitBinary(TypedBinaryExpr expression)
        {
            var helper = expression.Op switch
            {
                "+" => "CheckedAdd",
                "-" => "CheckedSubtract",
                "*" => "CheckedMultiply",
                _ => throw new InvalidOperationException("Unknown checked arithmetic operator")
            };
            return helper + "(" + EmitExpr(expression.Left) + ", " + EmitExpr(expression.Right) + ")";
        }

        private string EmitComparison(TypedCompareExpr expression)
        {
            var left = EmitExpr(expression.Left);
            var right = EmitExpr(expression.Right);
            if (expression.Left.Type.IsText)
            {
                if (expression.Op is not ("==" or "!="))
                    throw new InvalidOperationException("Text comparison only supports equality");
                var equals = "string.Equals(" + left + ", " + right + ", StringComparison.Ordinal)";
                return expression.Op == "==" ? equals : "!" + equals;
            }

            return expression.Op switch
            {
                "==" or "!=" or "<" or "<=" or ">" or ">=" =>
                    "(" + left + " " + expression.Op + " " + right + ")",
                _ => throw new InvalidOperationException("Unknown comparison operator")
            };
        }

        private string EmitBuiltinConstruct(TypedBuiltinConstructExpr expression)
        {
            var variant = expression.Variant switch
            {
                BuiltinVariant.Some => "Some",
                BuiltinVariant.None => "None",
                BuiltinVariant.Ok => "Ok",
                BuiltinVariant.Err => "Err",
            BuiltinVariant.FsErrorNotFound or
            BuiltinVariant.FsErrorPermissionDenied or
            BuiltinVariant.FsErrorInvalidPath or
            BuiltinVariant.FsErrorInvalidText or
            BuiltinVariant.FsErrorIo or
            BuiltinVariant.DbErrorStatement or
            BuiltinVariant.DbErrorRowShape => throw new InvalidOperationException("Builtin error variants cannot be constructed from source"),
                _ => throw new InvalidOperationException("Unknown builtin union variant")
            };
            return "new " + EmitType(expression.Type) + "." + variant + "(" +
                string.Join(", ", expression.Arguments.Select(EmitExpr)) + ")";
        }

        private string EmitUnionConstruct(TypedUnionConstructExpr expression) =>
            "new Union_" + expression.UnionId.ToString(CultureInfo.InvariantCulture) + ".Variant_" +
            expression.UnionId.ToString(CultureInfo.InvariantCulture) + "_" +
            expression.VariantId.ToString(CultureInfo.InvariantCulture) + "(" +
            string.Join(", ", expression.Arguments.Select(EmitExpr)) + ")";

        private string EmitStructConstruct(TypedStructConstructExpr expression) =>
            "new Struct_" + expression.StructId.ToString(CultureInfo.InvariantCulture) + "(" +
            string.Join(", ", expression.Fields.Select(field =>
                "Field_" + field.FieldIndex.ToString(CultureInfo.InvariantCulture) + ": " + EmitExpr(field.Value))) + ")";

        private string EmitMatch(TypedMatchExpr expression)
        {
            var arms = expression.Arms.Select(arm =>
                EmitPattern(arm.Pattern, expression.Value.Type) + " => " + EmitExpr(arm.Body));
            var fallback = expression.Arms.Any(arm => arm.Pattern is TypedWildcardPattern)
                ? ""
                : "_ => throw new InvalidOperationException(\"Invalid union value\")";
            var allArms = string.IsNullOrEmpty(fallback) ? arms : arms.Append(fallback);
            return "(" + EmitExpr(expression.Value) + ") switch { " + string.Join(", ", allArms) + " }";
        }

        private string EmitPattern(TypedPattern pattern, LangType scrutineeType)
        {
            if (pattern is TypedWildcardPattern) return "_";
            if (pattern is not TypedVariantPattern variant)
                throw new InvalidOperationException("Unknown checked pattern in emitter");

            string variantType;
            if (variant.Shape.Builtin is not null)
            {
                var variantName = variant.Shape.Builtin.Value switch
                {
                    BuiltinVariant.Some => "Some",
                    BuiltinVariant.None => "None",
                    BuiltinVariant.Ok => "Ok",
                    BuiltinVariant.Err => "Err",
                    BuiltinVariant.FsErrorNotFound => "NotFound",
                    BuiltinVariant.FsErrorPermissionDenied => "PermissionDenied",
                    BuiltinVariant.FsErrorInvalidPath => "InvalidPath",
                    BuiltinVariant.FsErrorInvalidText => "InvalidText",
                    BuiltinVariant.FsErrorIo => "Io",
                    BuiltinVariant.DbErrorStatement => "Statement",
                    BuiltinVariant.DbErrorRowShape => "RowShape",
                    _ => throw new InvalidOperationException("Unknown builtin pattern")
                };
                variantType = EmitType(scrutineeType) + "." + variantName;
            }
            else
            {
                var unionId = variant.Shape.UnionId!.Value;
                variantType = "Union_" + unionId.ToString(CultureInfo.InvariantCulture) + ".Variant_" +
                    unionId.ToString(CultureInfo.InvariantCulture) + "_" +
                    variant.Shape.VariantId.ToString(CultureInfo.InvariantCulture);
            }

            if (variant.Bindings.Count == 0) return variantType + " { }";
            return variantType + "(" + string.Join(", ", variant.Bindings.Select(binding =>
                "var Local_" + binding.LocalId.ToString(CultureInfo.InvariantCulture))) + ")";
        }

        private void EmitArithmeticHelpers()
        {
            _source.AppendLine("    private static int CheckedAdd(int left, int right) => checked(left + right);");
            _source.AppendLine("    private static int CheckedSubtract(int left, int right) => checked(left - right);");
            _source.AppendLine("    private static int CheckedMultiply(int left, int right) => checked(left * right);");
            _source.AppendLine();
        }

        private void EmitTextLengthHelper()
        {
            _source.AppendLine("    private static int TextLength(string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        var length = 0;");
            _source.AppendLine("        foreach (var rune in value.EnumerateRunes())");
            _source.AppendLine("            length = checked(length + 1);");
            _source.AppendLine("        return length;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitListGetHelper()
        {
            _source.AppendLine("    private static Option<T> ListGet<T>(global::System.Collections.Immutable.ImmutableArray<T> items, int index)");
            _source.AppendLine("    {");
            _source.AppendLine("        if ((uint)index >= (uint)items.Length) return new Option<T>.None();");
            _source.AppendLine("        return new Option<T>.Some(items[index]);");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitTextSplitHelper()
        {
            _source.AppendLine("    private static global::System.Collections.Immutable.ImmutableArray<string> TextSplit(string value, string separator)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (separator.Length == 0) return global::System.Collections.Immutable.ImmutableArray.Create<string>(value);");
            _source.AppendLine("        return global::System.Collections.Immutable.ImmutableArray.CreateRange<string>(value.Split(new[] { separator }, StringSplitOptions.None));");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsReadTextHelper()
        {
            _source.AppendLine("    private static Result<string, FsError> ReadText(FsRead receiver, string path)");
            _source.AppendLine("    {");
            _source.AppendLine("        ArgumentNullException.ThrowIfNull(receiver);");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            var bytes = File.ReadAllBytes(path);");
            _source.AppendLine("            return new Result<string, FsError>.Ok(new UTF8Encoding(false, true).GetString(bytes));");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (FileNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (DirectoryNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (SecurityException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (UnauthorizedAccessException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (DecoderFallbackException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidText());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (ArgumentException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (NotSupportedException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (PathTooLongException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (IOException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.Io());");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsReadTextAsyncHelper()
        {
            _source.AppendLine("    private static async Task<Result<string, FsError>> ReadTextAsync(FsRead receiver, string path)");
            _source.AppendLine("    {");
            _source.AppendLine("        ArgumentNullException.ThrowIfNull(receiver);");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            var bytes = await File.ReadAllBytesAsync(path, receiver.CancellationToken);");
            _source.AppendLine("            return new Result<string, FsError>.Ok(new UTF8Encoding(false, true).GetString(bytes));");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (FileNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (DirectoryNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (SecurityException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (UnauthorizedAccessException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (DecoderFallbackException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidText());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (ArgumentException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (NotSupportedException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (PathTooLongException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (IOException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<string, FsError>.Err(new FsError.Io());");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitFsWriteTextHelper()
        {
            _source.AppendLine("    // Paths use the host OS filesystem resolution rules. FsWrite does not confine writes to a package root or provide a filesystem sandbox.");
            _source.AppendLine("    // The temporary file is staged beside the destination and renamed over its directory entry where the host OS supports replacement.");
            _source.AppendLine("    private static Result<bool, FsError> WriteText(FsWrite receiver, string path, string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        ArgumentNullException.ThrowIfNull(receiver);");
            _source.AppendLine("        string? temporaryPath = null;");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            if (path.IndexOf('\\0') >= 0)");
            _source.AppendLine("                return new Result<bool, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("            var fullPath = Path.GetFullPath(path);");
            _source.AppendLine("            var directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException(\"Destination has no parent directory\", nameof(path));");
            _source.AppendLine("            var fileName = Path.GetFileName(fullPath);");
            _source.AppendLine("            var bytes = new UTF8Encoding(false, true).GetBytes(value);");
            _source.AppendLine("            var temporaryName = \".\" + (fileName.Length == 0 ? \"lang\" : fileName) + \".\" + Guid.NewGuid().ToString(\"N\") + \".tmp\";");
            _source.AppendLine("            temporaryPath = Path.Combine(directory, temporaryName);");
            _source.AppendLine("            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))");
            _source.AppendLine("            {");
            _source.AppendLine("                stream.Write(bytes, 0, bytes.Length);");
            _source.AppendLine("                stream.Flush(true);");
            _source.AppendLine("            }");
            _source.AppendLine("            File.Move(temporaryPath, fullPath, overwrite: true);");
            _source.AppendLine("            temporaryPath = null;");
            _source.AppendLine("            return new Result<bool, FsError>.Ok(true);");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (EncoderFallbackException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.InvalidText());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (FileNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (DirectoryNotFoundException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.NotFound());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (SecurityException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (UnauthorizedAccessException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.PermissionDenied());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (ArgumentException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (NotSupportedException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (PathTooLongException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (IOException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<bool, FsError>.Err(new FsError.Io());");
            _source.AppendLine("        }");
            _source.AppendLine("        finally");
            _source.AppendLine("        {");
            _source.AppendLine("            if (temporaryPath is not null)");
            _source.AppendLine("            {");
            _source.AppendLine("                try { File.Delete(temporaryPath); }");
            _source.AppendLine("                catch (IOException) { }");
            _source.AppendLine("                catch (UnauthorizedAccessException) { }");
            _source.AppendLine("                catch (SecurityException) { }");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitHtmlHelpers()
        {
            _source.AppendLine("    private static string EscapeHtml(string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        var escaped = new StringBuilder(value.Length);");
            _source.AppendLine("        foreach (var character in value)");
            _source.AppendLine("        {");
            _source.AppendLine("            switch (character)");
            _source.AppendLine("            {");
            _source.AppendLine("                case '&': escaped.Append(\"&amp;\"); break;");
            _source.AppendLine("                case '<': escaped.Append(\"&lt;\"); break;");
            _source.AppendLine("                case '>': escaped.Append(\"&gt;\"); break;");
            _source.AppendLine("                case '\"': escaped.Append(\"&quot;\"); break;");
            _source.AppendLine("                case '\\'': escaped.Append(\"&#x27;\"); break;");
            _source.AppendLine("                default: escaped.Append(character); break;");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("        return escaped.ToString();");
            _source.AppendLine("    }");
            _source.AppendLine("    private static Html HtmlText(string value) => new Html(EscapeHtml(value));");
            _source.AppendLine("    private static Html HtmlHeading(string value) => new Html(\"<h1>\" + EscapeHtml(value) + \"</h1>\");");
            _source.AppendLine("    private static Html HtmlParagraph(string value) => new Html(\"<p>\" + EscapeHtml(value) + \"</p>\");");
            _source.AppendLine("    private static Html HtmlConcat(Html left, Html right) => new Html(left.Value + right.Value);");
            _source.AppendLine("    private static Html HtmlDocument(string title, Html content) => new Html(\"<!doctype html><html><head><meta charset=\\\"utf-8\\\"><title>\" + EscapeHtml(title) + \"</title></head><body>\" + content.Value + \"</body></html>\");");
            _source.AppendLine();
        }

        private void EmitDatabaseHelpers()
        {
            if (_webDatabaseOptions is not null)
            {
                _source.AppendLine("    private static string DatabaseReadConnectionString = string.Empty;");
                _source.AppendLine("    private static string DatabaseWriteConnectionString = string.Empty;");
                EmitDatabaseInitialization(_webDatabaseOptions);
            }

            _source.AppendLine("    private sealed class DatabaseRowShapeException : Exception { }");

            _source.AppendLine("    private static object DatabaseParameterValue(int value) => value;");
            _source.AppendLine("    private static object DatabaseParameterValue(bool value) => value ? 1 : 0;");
            _source.AppendLine("    private static object DatabaseParameterValue(string value) => value;");
            _source.AppendLine("    private static object DatabaseParameterValue<T>(Option<T> value, Func<T, object> convert) => value switch");
            _source.AppendLine("    {");
            _source.AppendLine("        Option<T>.Some(var item) => convert(item),");
            _source.AppendLine("        Option<T>.None => DBNull.Value,");
            _source.AppendLine("        _ => throw new InvalidOperationException(\"Invalid Option value\")");
            _source.AppendLine("    };");

            foreach (var id in DatabaseParameterStructIds())
                EmitDatabaseParameterBinder(program.Structs.Single(structure => structure.Id == id));

            if (DatabaseCalls.Any(call => call.Operation.Kind == CheckedDatabaseOperationKind.QueryOne))
            {
                _source.AppendLine("    private static int ReadDatabaseInt32(SqliteDataReader reader, int ordinal)");
                _source.AppendLine("    {");
                _source.AppendLine("        var value = reader.GetValue(ordinal);");
                _source.AppendLine("        if (value is not long integer || integer < int.MinValue || integer > int.MaxValue) throw new DatabaseRowShapeException();");
                _source.AppendLine("        return (int)integer;");
                _source.AppendLine("    }");
                _source.AppendLine("    private static bool ReadDatabaseBool(SqliteDataReader reader, int ordinal)");
                _source.AppendLine("    {");
                _source.AppendLine("        var value = reader.GetValue(ordinal);");
                _source.AppendLine("        if (value is not long integer) throw new DatabaseRowShapeException();");
                _source.AppendLine("        return integer switch { 0 => false, 1 => true, _ => throw new DatabaseRowShapeException() };");
                _source.AppendLine("    }");
                _source.AppendLine("    private static string ReadDatabaseText(SqliteDataReader reader, int ordinal)");
                _source.AppendLine("    {");
                _source.AppendLine("        var value = reader.GetValue(ordinal);");
                _source.AppendLine("        return value as string ?? throw new DatabaseRowShapeException();");
                _source.AppendLine("    }");
                _source.AppendLine("    private static Option<T> ReadDatabaseOptional<T>(SqliteDataReader reader, int ordinal, Func<SqliteDataReader, int, T> convert)");
                _source.AppendLine("    {");
                _source.AppendLine("        if (reader.IsDBNull(ordinal)) return new Option<T>.None();");
                _source.AppendLine("        return new Option<T>.Some(convert(reader, ordinal));");
                _source.AppendLine("    }");

                foreach (var id in DatabaseRowStructIds())
                    EmitDatabaseRowReader(program.Structs.Single(structure => structure.Id == id));

                _source.AppendLine("    private static Result<Option<TRow>, DbError> DatabaseQueryOne<TParameters, TRow>(DbRead database, string sql, TParameters parameters, Action<SqliteCommand, TParameters> bindParameters, Func<SqliteDataReader, int[]> getColumnOrdinals, Func<SqliteDataReader, int[], TRow> readRow)");
                _source.AppendLine("    {");
                _source.AppendLine("        var cancellationToken = database.CancellationToken;");
                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.AppendLine("            using var connection = new SqliteConnection(database.ConnectionString);");
                _source.AppendLine("            connection.OpenAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            using (var queryOnlyCommand = connection.CreateCommand())");
                _source.AppendLine("            {");
                _source.AppendLine("                queryOnlyCommand.CommandText = \"PRAGMA query_only = ON\";");
                _source.AppendLine("                queryOnlyCommand.ExecuteNonQueryAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            }");
                _source.AppendLine("            using var command = connection.CreateCommand();");
                _source.AppendLine("            command.CommandText = sql;");
                _source.AppendLine("            bindParameters(command, parameters);");
                _source.AppendLine("            using var reader = command.ExecuteReaderAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            var columnOrdinals = getColumnOrdinals(reader);");
                _source.AppendLine("            if (!reader.ReadAsync(cancellationToken).GetAwaiter().GetResult())");
                _source.AppendLine("                return new Result<Option<TRow>, DbError>.Ok(new Option<TRow>.None());");
                _source.AppendLine("            var row = readRow(reader, columnOrdinals);");
                _source.AppendLine("            // query_one has strict zero-or-one cardinality; a second row is a row-shape error.");
                _source.AppendLine("            if (reader.ReadAsync(cancellationToken).GetAwaiter().GetResult()) throw new DatabaseRowShapeException();");
                _source.AppendLine("            return new Result<Option<TRow>, DbError>.Ok(new Option<TRow>.Some(row));");
                _source.AppendLine("        }");
                _source.AppendLine("        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }");
                _source.AppendLine("        catch (DatabaseRowShapeException) { return new Result<Option<TRow>, DbError>.Err(new DbError.RowShape()); }");
                _source.AppendLine("        catch (SqliteException) { return new Result<Option<TRow>, DbError>.Err(new DbError.Statement()); }");
                _source.AppendLine("    }");
            }

            if (DatabaseCalls.Any(call => call.Operation.Kind == CheckedDatabaseOperationKind.Execute))
            {
                _source.AppendLine("    private static Result<int, DbError> DatabaseExecute<TParameters>(DbWrite database, string sql, TParameters parameters, Action<SqliteCommand, TParameters> bindParameters)");
                _source.AppendLine("    {");
                _source.AppendLine("        var cancellationToken = database.CancellationToken;");
                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.AppendLine("            using var connection = new SqliteConnection(database.ConnectionString);");
                _source.AppendLine("            connection.OpenAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            using var command = connection.CreateCommand();");
                _source.AppendLine("            command.CommandText = sql;");
                _source.AppendLine("            bindParameters(command, parameters);");
                _source.AppendLine("            var affectedRows = command.ExecuteNonQueryAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            return new Result<int, DbError>.Ok(affectedRows);");
                _source.AppendLine("        }");
                _source.AppendLine("        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }");
                _source.AppendLine("        catch (SqliteException) { return new Result<int, DbError>.Err(new DbError.Statement()); }");
                _source.AppendLine("    }");
            }

            if (TransactionScopes.Count != 0)
            {
                _source.AppendLine("    private static DbTransaction DatabaseBeginTransaction(DbWrite database)");
                _source.AppendLine("    {");
                _source.AppendLine("        var cancellationToken = database.CancellationToken;");
                _source.AppendLine("        var connection = new SqliteConnection(database.ConnectionString);");
                _source.AppendLine("        SqliteTransaction? transaction = null;");
                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.AppendLine("            connection.OpenAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            transaction = (SqliteTransaction)connection.BeginTransactionAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            return new DbTransaction(connection, transaction, cancellationToken);");
                _source.AppendLine("        }");
                _source.AppendLine("        catch");
                _source.AppendLine("        {");
                _source.AppendLine("            try { transaction?.Dispose(); }");
                _source.AppendLine("            finally { connection.Dispose(); }");
                _source.AppendLine("            throw;");
                _source.AppendLine("        }");
                _source.AppendLine("    }");

                if (DatabaseCalls.Any(call => call.Operation.Kind == CheckedDatabaseOperationKind.TransactionExecute))
                {
                    _source.AppendLine("    private static Result<int, DbError> DatabaseTransactionExecute<TParameters>(DbTransaction database, string sql, TParameters parameters, Action<SqliteCommand, TParameters> bindParameters)");
                    _source.AppendLine("    {");
                    _source.AppendLine("        var cancellationToken = database.CancellationToken;");
                    _source.AppendLine("        if (!database.CanExecute) return new Result<int, DbError>.Err(new DbError.Statement());");
                    _source.AppendLine("        try");
                    _source.AppendLine("        {");
                    _source.AppendLine("            using var command = database.Connection.CreateCommand();");
                    _source.AppendLine("            command.Transaction = database.Transaction;");
                    _source.AppendLine("            command.CommandText = sql;");
                    _source.AppendLine("            bindParameters(command, parameters);");
                    _source.AppendLine("            var affectedRows = command.ExecuteNonQueryAsync(cancellationToken).GetAwaiter().GetResult();");
                    _source.AppendLine("            return new Result<int, DbError>.Ok(affectedRows);");
                    _source.AppendLine("        }");
                    _source.AppendLine("        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
                    _source.AppendLine("        {");
                    _source.AppendLine("            database.MarkFailed();");
                    _source.AppendLine("            throw;");
                    _source.AppendLine("        }");
                    _source.AppendLine("        catch (SqliteException)");
                    _source.AppendLine("        {");
                    _source.AppendLine("            database.MarkFailed();");
                    _source.AppendLine("            return new Result<int, DbError>.Err(new DbError.Statement());");
                    _source.AppendLine("        }");
                    _source.AppendLine("    }");
                }

                _source.AppendLine("    private static Result<bool, DbError> DatabaseTransactionCommit(DbTransaction database)");
                _source.AppendLine("    {");
                _source.AppendLine("        var cancellationToken = database.CancellationToken;");
                _source.AppendLine("        if (!database.CanCommit) return new Result<bool, DbError>.Err(new DbError.Statement());");
                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.AppendLine("            database.Transaction.CommitAsync(cancellationToken).GetAwaiter().GetResult();");
                _source.AppendLine("            database.MarkCommitted();");
                _source.AppendLine("            return new Result<bool, DbError>.Ok(true);");
                _source.AppendLine("        }");
                _source.AppendLine("        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
                _source.AppendLine("        {");
                _source.AppendLine("            database.MarkFailed();");
                _source.AppendLine("            throw;");
                _source.AppendLine("        }");
                _source.AppendLine("        catch (SqliteException)");
                _source.AppendLine("        {");
                _source.AppendLine("            database.MarkFailed();");
                _source.AppendLine("            return new Result<bool, DbError>.Err(new DbError.Statement());");
                _source.AppendLine("        }");
                _source.AppendLine("    }");
            }
            _source.AppendLine();
        }

        private void EmitDatabaseInitialization(WebDatabaseOptions options)
        {
            _source.AppendLine("    private static async Task InitializeDatabaseAsync(CancellationToken cancellationToken)");
            _source.AppendLine("    {");
            _source.AppendLine("        var overridePath = Environment.GetEnvironmentVariable(\"LANG_SQLITE_PATH\");");
            _source.Append("        var configuredPath = string.IsNullOrWhiteSpace(overridePath) ? ")
                .Append(JsonSerializer.Serialize(options.RelativePath)).AppendLine(" : overridePath;");
            _source.AppendLine("        var databasePath = Path.GetFullPath(configuredPath, AppContext.BaseDirectory);");
            _source.AppendLine("        var databaseDirectory = Path.GetDirectoryName(databasePath);");
            _source.AppendLine("        if (!string.IsNullOrEmpty(databaseDirectory)) Directory.CreateDirectory(databaseDirectory);");
            _source.AppendLine("        var writeConnectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();");
            _source.AppendLine("        var readConnectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString();");
            _source.AppendLine("        await using var connection = new SqliteConnection(writeConnectionString);");
            _source.AppendLine("        await connection.OpenAsync(cancellationToken);");
            _source.AppendLine("        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);");
            _source.AppendLine("        await using var command = connection.CreateCommand();");
            _source.AppendLine("        command.Transaction = transaction;");
            _source.Append("        command.CommandText = ").Append(JsonSerializer.Serialize(options.SchemaText)).AppendLine(";");
            _source.AppendLine("        await command.ExecuteNonQueryAsync(cancellationToken);");
            _source.AppendLine("        await transaction.CommitAsync(cancellationToken);");
            _source.AppendLine("        DatabaseReadConnectionString = readConnectionString;");
            _source.AppendLine("        DatabaseWriteConnectionString = writeConnectionString;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private IEnumerable<int> DatabaseParameterStructIds() => DatabaseCalls
            .Select(call => call.Operation.ParameterStructId)
            .Distinct()
            .OrderBy(id => id);

        private IEnumerable<int> DatabaseRowStructIds() => DatabaseCalls
            .Where(call => call.Operation.Kind == CheckedDatabaseOperationKind.QueryOne)
            .Select(call => call.Operation.RowStructId ?? throw new InvalidOperationException("Checked database query has no row struct"))
            .Distinct()
            .OrderBy(id => id);

        private void EmitDatabaseParameterBinder(CheckedStruct structure)
        {
            var id = structure.Id.ToString(CultureInfo.InvariantCulture);
            _source.Append("    private static void BindDatabaseParameters_").Append(id)
                .Append("(SqliteCommand command, Struct_").Append(id).AppendLine(" value)");
            _source.AppendLine("    {");
            foreach (var field in structure.Fields.OrderBy(field => field.Index))
            {
                var emittedValue = field.Type.Kind == LangTypeKind.Option
                    ? "DatabaseParameterValue(value.Field_" + field.Index.ToString(CultureInfo.InvariantCulture) + ", item => DatabaseParameterValue(item))"
                    : "DatabaseParameterValue(value.Field_" + field.Index.ToString(CultureInfo.InvariantCulture) + ")";
                _source.Append("        command.Parameters.AddWithValue(")
                    .Append(JsonSerializer.Serialize("$" + field.Name)).Append(", ").Append(emittedValue).AppendLine(");");
            }
            _source.AppendLine("    }");
        }

        private void EmitDatabaseRowReader(CheckedStruct structure)
        {
            var id = structure.Id.ToString(CultureInfo.InvariantCulture);
            _source.Append("    private static int[] GetDatabaseColumnOrdinals_").Append(id).AppendLine("(SqliteDataReader reader)");
            _source.AppendLine("    {");
            _source.AppendLine("        var columns = new Dictionary<string, int>(StringComparer.Ordinal);");
            _source.AppendLine("        for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)");
            _source.AppendLine("            if (!columns.TryAdd(reader.GetName(ordinal), ordinal)) throw new DatabaseRowShapeException();");
            _source.Append("        if (columns.Count != ").Append(structure.Fields.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(") throw new DatabaseRowShapeException();");
            _source.Append("        var ordinals = new int[").Append(structure.Fields.Count.ToString(CultureInfo.InvariantCulture)).AppendLine("];");
            foreach (var field in structure.Fields.OrderBy(field => field.Index))
            {
                var index = field.Index.ToString(CultureInfo.InvariantCulture);
                _source.Append("        if (!columns.TryGetValue(").Append(JsonSerializer.Serialize(field.Name))
                    .Append(", out var column_").Append(index).AppendLine(")) throw new DatabaseRowShapeException();");
                _source.Append("        ordinals[").Append(index).Append("] = column_").Append(index).AppendLine(";");
            }
            _source.AppendLine("        return ordinals;");
            _source.AppendLine("    }");
            _source.Append("    private static Struct_").Append(id).Append(" ReadDatabaseRow_").Append(id).AppendLine("(SqliteDataReader reader, int[] ordinals)");
            _source.AppendLine("    {");
            foreach (var field in structure.Fields.OrderBy(field => field.Index))
            {
                var index = field.Index.ToString(CultureInfo.InvariantCulture);
                _source.Append("        var field_").Append(index).Append(" = ")
                    .Append(DatabaseReadValue(field.Type, "ordinals[" + index + "]")).AppendLine(";");
            }
            _source.Append("        return new Struct_").Append(id).Append('(')
                .Append(string.Join(", ", structure.Fields.OrderBy(field => field.Index).Select(field =>
                    "Field_" + field.Index.ToString(CultureInfo.InvariantCulture) + ": field_" +
                    field.Index.ToString(CultureInfo.InvariantCulture))))
                .AppendLine(");");
            _source.AppendLine("    }");
        }

        private static string DatabaseReadValue(LangType type, string ordinal) => type.Kind switch
        {
            LangTypeKind.I32 => "ReadDatabaseInt32(reader, " + ordinal + ")",
            LangTypeKind.Bool => "ReadDatabaseBool(reader, " + ordinal + ")",
            LangTypeKind.Text => "ReadDatabaseText(reader, " + ordinal + ")",
            LangTypeKind.Option when type.Arguments[0].Kind == LangTypeKind.I32 =>
                "ReadDatabaseOptional(reader, " + ordinal + ", ReadDatabaseInt32)",
            LangTypeKind.Option when type.Arguments[0].Kind == LangTypeKind.Bool =>
                "ReadDatabaseOptional(reader, " + ordinal + ", ReadDatabaseBool)",
            LangTypeKind.Option when type.Arguments[0].Kind == LangTypeKind.Text =>
                "ReadDatabaseOptional(reader, " + ordinal + ", ReadDatabaseText)",
            _ => throw new InvalidOperationException("Unsupported checked database row field")
        };

        private void EmitWebHost()
        {
            _source.AppendLine("    private const int MaxRequestBodyBytes = 1048576;");
            _source.AppendLine("    private const int MaxTransportRequestBodyBytes = MaxRequestBodyBytes + 65536;");
            _source.AppendLine("    public static async Task Main(string[] args)");
            _source.AppendLine("    {");
            _source.AppendLine("        var builder = WebApplication.CreateBuilder(args);");
            _source.AppendLine("        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxTransportRequestBodyBytes);");
            _source.AppendLine("        var app = builder.Build();");
            if (_webDatabaseOptions is not null)
            {
                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.AppendLine("            await InitializeDatabaseAsync(app.Lifetime.ApplicationStopping);");
                _source.AppendLine("        }");
                _source.AppendLine("        catch");
                _source.AppendLine("        {");
                _source.AppendLine("            await app.DisposeAsync();");
                _source.AppendLine("            throw;");
                _source.AppendLine("        }");
            }
            _source.AppendLine("        var parentIdText = Environment.GetEnvironmentVariable(\"LANG_PARENT_PROCESS_ID\");");
            _source.AppendLine("        var hasWrapperParent = int.TryParse(parentIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var wrapperParentId) && wrapperParentId > 0;");
            _source.AppendLine("        using var standardInputMonitorCancellation = new CancellationTokenSource();");
            _source.AppendLine("        if (hasWrapperParent) StartStandardInputMonitor(app, standardInputMonitorCancellation.Token);");
            foreach (var route in program.Routes.OrderBy(route => route.Id))
            {
                _source.Append("        app.MapMethods(").Append(JsonSerializer.Serialize(route.Path))
                    .Append(", new[] { ").Append(JsonSerializer.Serialize(route.Method)).Append(" }, ")
                    .Append("(HttpContext context) => HandleRoute_")
                    .Append(route.Id.ToString(CultureInfo.InvariantCulture)).AppendLine("(context, app.Logger));");
            }
            _source.AppendLine("        using var parentMonitorCancellation = new CancellationTokenSource();");
            _source.AppendLine("        var parentMonitor = hasWrapperParent");
            _source.AppendLine("            ? MonitorParentProcessAsync(app, parentMonitorCancellation.Token, wrapperParentId)");
            _source.AppendLine("            : Task.CompletedTask;");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            await app.RunAsync();");
            _source.AppendLine("        }");
            _source.AppendLine("        finally");
            _source.AppendLine("        {");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                await app.DisposeAsync();");
            _source.AppendLine("            }");
            _source.AppendLine("            finally");
            _source.AppendLine("            {");
            if (_webDatabaseOptions is not null)
                _source.AppendLine("                SqliteConnection.ClearAllPools();");
            _source.AppendLine("                try");
            _source.AppendLine("                {");
            _source.AppendLine("                    standardInputMonitorCancellation.Cancel();");
            _source.AppendLine("                }");
            _source.AppendLine("                finally");
            _source.AppendLine("                {");
            _source.AppendLine("                    try");
            _source.AppendLine("                    {");
            _source.AppendLine("                        parentMonitorCancellation.Cancel();");
            _source.AppendLine("                    }");
            _source.AppendLine("                    finally");
            _source.AppendLine("                    {");
            _source.AppendLine("                        await parentMonitor;");
            _source.AppendLine("                    }");
            _source.AppendLine("                }");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();

            EmitParentProcessMonitor();
            EmitStandardInputMonitor();
            EmitRequestRuntime();
            foreach (var route in program.Routes.OrderBy(route => route.Id))
                EmitRouteHandler(route);
            EmitJsonResponseRuntime();
        }

        private void EmitParentProcessMonitor()
        {
            _source.AppendLine("    private static async Task MonitorParentProcessAsync(WebApplication app, CancellationToken cancellationToken, int parentId)");
            _source.AppendLine("    {");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            using var parent = Process.GetProcessById(parentId);");
            _source.AppendLine("            await parent.WaitForExitAsync(cancellationToken);");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }");
            _source.AppendLine("        catch (ArgumentException) { }");
            _source.AppendLine("        catch (InvalidOperationException) { }");
            _source.AppendLine("        catch (System.ComponentModel.Win32Exception) { }");
            _source.AppendLine("        if (cancellationToken.IsCancellationRequested) return;");
            _source.AppendLine("        app.Lifetime.StopApplication();");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }");
            _source.AppendLine("        if (!cancellationToken.IsCancellationRequested) Environment.Exit(0);");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitStandardInputMonitor()
        {
            _source.AppendLine("    private static void StartStandardInputMonitor(WebApplication app, CancellationToken cancellationToken)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (!Console.IsInputRedirected) return;");
            _source.AppendLine("        _ = Task.Run(async () =>");
            _source.AppendLine("        {");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                _ = Console.In.ReadLine();");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (IOException) { }");
            _source.AppendLine("            catch (ObjectDisposedException) { }");
            _source.AppendLine("            app.Lifetime.StopApplication();");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }");
            _source.AppendLine("            if (!cancellationToken.IsCancellationRequested) Environment.Exit(0);");
            _source.AppendLine("        });");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitRequestRuntime()
        {
            _source.AppendLine("    private sealed class RequestTooLargeException : Exception { }");
            _source.AppendLine("    private static async Task<byte[]> ReadLimitedRequestBodyAsync(HttpRequest request)");
            _source.AppendLine("    {");
            _source.AppendLine("        var exceedsLimit = request.ContentLength is long contentLength && contentLength > MaxRequestBodyBytes;");
            _source.AppendLine("        using var output = new MemoryStream();");
            _source.AppendLine("        var buffer = new byte[8192];");
            _source.AppendLine("        var total = 0;");
            _source.AppendLine("        while (true)");
            _source.AppendLine("        {");
            _source.AppendLine("            var read = await request.Body.ReadAsync(buffer.AsMemory(), request.HttpContext.RequestAborted);");
            _source.AppendLine("            if (read == 0) break;");
            _source.AppendLine("            var remainingBufferCapacity = Math.Max(0, MaxRequestBodyBytes - total);");
            _source.AppendLine("            var buffered = Math.Min(read, remainingBufferCapacity);");
            _source.AppendLine("            if (buffered != 0) output.Write(buffer, 0, buffered);");
            _source.AppendLine("            total += read;");
            _source.AppendLine("            if (total > MaxRequestBodyBytes) exceedsLimit = true;");
            _source.AppendLine("        }");
            _source.AppendLine("        if (exceedsLimit) throw new RequestTooLargeException();");
            _source.AppendLine("        return output.ToArray();");
            _source.AppendLine("    }");
            _source.AppendLine();

            var requestStructIds = RequestBodyStructIds();
            foreach (var id in requestStructIds.OrderBy(id => id))
                EmitRequestStructDecoder(program.Structs.Single(structure => structure.Id == id));
            if (requestStructIds.Count != 0)
            {
                _source.AppendLine("    private static int DecodeJsonI32(JsonElement value)");
                _source.AppendLine("    {");
                _source.AppendLine("        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result)) throw new JsonException();");
                _source.AppendLine("        return result;");
                _source.AppendLine("    }");
                _source.AppendLine("    private static bool DecodeJsonBool(JsonElement value)");
                _source.AppendLine("    {");
                _source.AppendLine("        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new JsonException();");
                _source.AppendLine("        return value.GetBoolean();");
                _source.AppendLine("    }");
                _source.AppendLine("    private static string DecodeJsonText(JsonElement value)");
                _source.AppendLine("    {");
                _source.AppendLine("        if (value.ValueKind != JsonValueKind.String) throw new JsonException();");
                _source.AppendLine("        return value.GetString() ?? throw new JsonException();");
                _source.AppendLine("    }");
                _source.AppendLine();
            }
        }

        private HashSet<int> RequestBodyStructIds()
        {
            var result = new HashSet<int>();
            var pending = new Stack<int>(program.Routes
                .Where(route => route.Method == "POST" && route.BodyType?.Kind == LangTypeKind.Struct)
                .Select(route => route.BodyType!.StructId));
            while (pending.Count != 0)
            {
                var id = pending.Pop();
                if (!result.Add(id)) continue;
                var structure = program.Structs.Single(item => item.Id == id);
                foreach (var field in structure.Fields)
                    if (field.Type.Kind == LangTypeKind.Struct)
                        pending.Push(field.Type.StructId);
            }
            return result;
        }

        private void EmitRequestStructDecoder(CheckedStruct structure)
        {
            var id = structure.Id.ToString(CultureInfo.InvariantCulture);
            _source.Append("    private static Struct_").Append(id).Append(" DecodeJsonStruct_").Append(id).AppendLine("(JsonElement element)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (element.ValueKind != JsonValueKind.Object) throw new JsonException();");
            foreach (var field in structure.Fields.OrderBy(field => field.Index))
            {
                var index = field.Index.ToString(CultureInfo.InvariantCulture);
                var defaultValue = field.Type.Kind switch
                {
                    LangTypeKind.I32 => "0",
                    LangTypeKind.Bool => "false",
                    LangTypeKind.Text => "string.Empty",
                    LangTypeKind.Struct => "default!",
                    _ => throw new InvalidOperationException("Unsupported checked JSON request field")
                };
                _source.Append("        ").Append(EmitType(field.Type)).Append(" field_").Append(index)
                    .Append(" = ").Append(defaultValue).AppendLine(";");
                _source.Append("        var seen_").Append(index).AppendLine(" = false;");
            }
            _source.AppendLine("        foreach (var property in element.EnumerateObject())");
            _source.AppendLine("        {");
            _source.AppendLine("            switch (property.Name)");
            _source.AppendLine("            {");
            foreach (var field in structure.Fields.OrderBy(field => field.Index))
            {
                var index = field.Index.ToString(CultureInfo.InvariantCulture);
                _source.Append("                case ").Append(JsonSerializer.Serialize(field.Name)).AppendLine(":");
                _source.Append("                    if (seen_").Append(index).AppendLine(") throw new JsonException();");
                _source.Append("                    seen_").Append(index).AppendLine(" = true;");
                _source.Append("                    field_").Append(index).Append(" = ").Append(DecodeJsonValue(field.Type, "property.Value")).AppendLine(";");
                _source.AppendLine("                    break;");
            }
            _source.AppendLine("                default: throw new JsonException();");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            foreach (var field in structure.Fields.OrderBy(field => field.Index))
                _source.Append("        if (!seen_").Append(field.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(") throw new JsonException();");
            _source.Append("        return new Struct_").Append(id).Append('(')
                .Append(string.Join(", ", structure.Fields.OrderBy(field => field.Index).Select(field =>
                    "Field_" + field.Index.ToString(CultureInfo.InvariantCulture) + ": field_" +
                    field.Index.ToString(CultureInfo.InvariantCulture))))
                .AppendLine(");");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private static string DecodeJsonValue(LangType type, string value) => type.Kind switch
        {
            LangTypeKind.I32 => "DecodeJsonI32(" + value + ")",
            LangTypeKind.Bool => "DecodeJsonBool(" + value + ")",
            LangTypeKind.Text => "DecodeJsonText(" + value + ")",
            LangTypeKind.Struct => "DecodeJsonStruct_" + type.StructId.ToString(CultureInfo.InvariantCulture) + "(" + value + ")",
            _ => throw new InvalidOperationException("Unsupported checked JSON request field")
        };

        private void EmitRouteHandler(CheckedRoute route)
        {
            var routeId = route.Id.ToString(CultureInfo.InvariantCulture);
            var handler = program.Functions.Single(function => function.Id == route.HandlerFunctionId);
            var union = program.Unions.Single(item => item.Id == route.ReplyUnionId);
            _source.Append("    private static async Task HandleRoute_").Append(routeId)
                .AppendLine("(HttpContext context, ILogger logger)");
            _source.AppendLine("    {");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            if (route.Method == "POST")
            {
                var bodyType = route.BodyType ?? throw new InvalidOperationException("Checked POST route has no body type");
                if (bodyType.Kind != LangTypeKind.Struct)
                    throw new InvalidOperationException("Checked POST route body is not a struct");
                _source.AppendLine("            var requestBytes = await ReadLimitedRequestBodyAsync(context.Request);");
                _source.AppendLine("            using var requestJson = JsonDocument.Parse(requestBytes, new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });");
                _source.Append("            var requestValue = DecodeJsonStruct_").Append(bodyType.StructId.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("(requestJson.RootElement);");
                var arguments = new List<string> { "requestValue" };
                arguments.AddRange(EmitRouteCapabilities(route));
                if (route.HandlerIsAsync) arguments.Add("context.RequestAborted");
                _source.Append("            var reply = ");
                if (route.HandlerIsAsync) _source.Append("await ");
                _source.Append("Function_").Append(handler.Id.ToString(CultureInfo.InvariantCulture))
                    .Append('(').Append(string.Join(", ", arguments)).AppendLine(");");
            }
            else
            {
                var arguments = EmitRouteCapabilities(route);
                if (route.HandlerIsAsync) arguments = arguments.Append("context.RequestAborted");
                _source.Append("            var reply = ");
                if (route.HandlerIsAsync) _source.Append("await ");
                _source.Append("Function_").Append(handler.Id.ToString(CultureInfo.InvariantCulture))
                    .Append('(').Append(string.Join(", ", arguments)).AppendLine(");");
            }

            _source.AppendLine("            switch (reply)");
            _source.AppendLine("            {");
            foreach (var response in route.Responses.OrderBy(response => response.VariantId))
            {
                var variant = union.Variants.Single(item => item.Id == response.VariantId);
                var unionId = union.Id.ToString(CultureInfo.InvariantCulture);
                var variantType = "Union_" + unionId + ".Variant_" + unionId + "_" +
                                  variant.Id.ToString(CultureInfo.InvariantCulture);
                if (variant.Fields.Count == 0)
                {
                    _source.Append("                case ").Append(variantType).AppendLine(":");
                    _source.Append("                    await WriteNoContentAsync(context, ")
                        .Append(response.StatusCode.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
                }
                else
                {
                    var payloadType = response.PayloadType ?? throw new InvalidOperationException("Checked response payload type is missing");
                    var valueName = "responseValue_" + response.VariantId.ToString(CultureInfo.InvariantCulture);
                    _source.Append("                case ").Append(variantType).Append("(var ").Append(valueName).AppendLine("):");
                    if (response.ContentKind == CheckedRouteContentKind.Html)
                    {
                        _source.Append("                    await WriteHtmlResponseAsync(context, ")
                            .Append(response.StatusCode.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(valueName).AppendLine(");");
                    }
                    else if (response.ContentKind == CheckedRouteContentKind.Json)
                    {
                        _source.Append("                    await WriteJsonResponseAsync(context, ")
                            .Append(response.StatusCode.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(valueName)
                            .Append(", (Action<Utf8JsonWriter, ").Append(EmitType(payloadType)).Append(">)WriteJson);").AppendLine();
                    }
                    else
                    {
                        throw new InvalidOperationException("Checked payload response has no content kind");
                    }
                }
                _source.AppendLine("                    return;");
            }
            _source.AppendLine("                default:");
            _source.AppendLine("                    await WriteErrorAsync(context, 500, \"{\\\"error\\\":\\\"internal_server_error\\\"}\", includeRequestId: true);");
            _source.AppendLine("                    return;");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (RequestTooLargeException)");
            _source.AppendLine("        {");
            _source.AppendLine("            await WriteErrorAsync(context, 413, \"{\\\"error\\\":\\\"payload_too_large\\\"}\");");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (BadHttpRequestException error) when (error.StatusCode == 413)");
            _source.AppendLine("        {");
            _source.AppendLine("            await WriteErrorAsync(context, 413, \"{\\\"error\\\":\\\"payload_too_large\\\"}\");");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (JsonException)");
            _source.AppendLine("        {");
            _source.AppendLine("            await WriteErrorAsync(context, 400, \"{\\\"error\\\":\\\"invalid_request\\\"}\");");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (BadHttpRequestException)");
            _source.AppendLine("        {");
            _source.AppendLine("            await WriteErrorAsync(context, 400, \"{\\\"error\\\":\\\"invalid_request\\\"}\");");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)");
            _source.AppendLine("        {");
            _source.AppendLine("            return;");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (Exception error)");
            _source.AppendLine("        {");
            _source.AppendLine("            logger.LogError(error, \"Unhandled request fault {RequestId}\", context.TraceIdentifier);");
            _source.AppendLine("            await WriteErrorAsync(context, 500, \"{\\\"error\\\":\\\"internal_server_error\\\"}\", includeRequestId: true);");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private IEnumerable<string> EmitRouteCapabilities(CheckedRoute route)
        {
            foreach (var capability in route.Capabilities.OrderBy(capability => capability.HandlerParameterIndex))
            {
                if (capability.Kind is CheckedCapabilityKind.DbRead or CheckedCapabilityKind.DbWrite &&
                    _webDatabaseOptions is null)
                    throw new InvalidOperationException("Checked route database capability reached emission without web database options");

                yield return capability.Kind switch
                {
                    CheckedCapabilityKind.FsWrite => "new FsWrite()",
                    CheckedCapabilityKind.DbRead => "new DbRead(DatabaseReadConnectionString, context.RequestAborted)",
                    CheckedCapabilityKind.DbWrite => "new DbWrite(DatabaseWriteConnectionString, context.RequestAborted)",
                    _ => throw new InvalidOperationException("Unsupported checked route capability")
                };
            }
        }

        private void EmitJsonResponseRuntime()
        {
            _source.AppendLine("    private static bool IsBodyForbidden(int statusCode) => statusCode is >= 100 and < 200 or 204 or 205 or 304;");
            _source.AppendLine("    private static Task WriteNoContentAsync(HttpContext context, int statusCode)");
            _source.AppendLine("    {");
            _source.AppendLine("        context.Response.StatusCode = statusCode;");
            _source.AppendLine("        return Task.CompletedTask;");
            _source.AppendLine("    }");
            _source.AppendLine("    private static async Task WriteJsonResponseAsync<T>(HttpContext context, int statusCode, T value, Action<Utf8JsonWriter, T> writeValue)");
            _source.AppendLine("    {");
            _source.AppendLine("        context.Response.StatusCode = statusCode;");
            _source.AppendLine("        if (IsBodyForbidden(statusCode)) return;");
            _source.AppendLine("        context.Response.ContentType = \"application/json; charset=utf-8\";");
            _source.AppendLine("        var bodyWriter = context.Response.BodyWriter;");
            _source.AppendLine("        using (var writer = new Utf8JsonWriter(bodyWriter))");
            _source.AppendLine("        {");
            _source.AppendLine("            writeValue(writer, value);");
            _source.AppendLine("            writer.Flush();");
            _source.AppendLine("        }");
            _source.AppendLine("        await bodyWriter.FlushAsync(context.RequestAborted);");
            _source.AppendLine("    }");
            _source.AppendLine("    private static async Task WriteHtmlResponseAsync(HttpContext context, int statusCode, Html value)");
            _source.AppendLine("    {");
            _source.AppendLine("        context.Response.StatusCode = statusCode;");
            _source.AppendLine("        if (IsBodyForbidden(statusCode)) return;");
            _source.AppendLine("        context.Response.ContentType = \"text/html; charset=utf-8\";");
            _source.AppendLine("        await context.Response.WriteAsync(value.Value, Encoding.UTF8, context.RequestAborted);");
            _source.AppendLine("    }");
            _source.AppendLine("    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string body, bool includeRequestId = false)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (context.Response.HasStarted) { context.Abort(); return; }");
            _source.AppendLine("        context.Response.Clear();");
            _source.AppendLine("        context.Response.StatusCode = statusCode;");
            _source.AppendLine("        context.Response.ContentType = \"application/json; charset=utf-8\";");
            _source.AppendLine("        if (includeRequestId) context.Response.Headers[\"X-Request-Id\"] = context.TraceIdentifier;");
            _source.AppendLine("        await context.Response.WriteAsync(body, Encoding.UTF8, context.RequestAborted);");
            _source.AppendLine("    }");
            _source.AppendLine();

            _source.AppendLine("    private static void WriteJson(Utf8JsonWriter writer, int value) => writer.WriteNumberValue(value);");
            _source.AppendLine("    private static void WriteJson(Utf8JsonWriter writer, bool value) => writer.WriteBooleanValue(value);");
            _source.AppendLine("    private static void WriteJson(Utf8JsonWriter writer, string value) => writer.WriteStringValue(value);");
            foreach (var id in JsonResponseStructIds().OrderBy(id => id))
                EmitJsonStructWriter(program.Structs.Single(structure => structure.Id == id));
            _source.AppendLine();
        }

        private HashSet<int> JsonResponseStructIds()
        {
            var result = new HashSet<int>();
            var pending = new Stack<int>(program.Routes
                .SelectMany(route => route.Responses)
                .Where(response => response.ContentKind == CheckedRouteContentKind.Json && response.PayloadType?.Kind == LangTypeKind.Struct)
                .Select(response => response.PayloadType!.StructId));
            while (pending.Count != 0)
            {
                var id = pending.Pop();
                if (!result.Add(id)) continue;
                var structure = program.Structs.Single(item => item.Id == id);
                foreach (var field in structure.Fields)
                    if (field.Type.Kind == LangTypeKind.Struct)
                        pending.Push(field.Type.StructId);
            }
            return result;
        }

        private void EmitJsonStructWriter(CheckedStruct structure)
        {
            var id = structure.Id.ToString(CultureInfo.InvariantCulture);
            _source.Append("    private static void WriteJson(Utf8JsonWriter writer, Struct_").Append(id).AppendLine(" value)");
            _source.AppendLine("    {");
            _source.AppendLine("        writer.WriteStartObject();");
            foreach (var field in structure.Fields.OrderBy(field => field.Index))
            {
                _source.Append("        writer.WritePropertyName(").Append(JsonSerializer.Serialize(field.Name)).AppendLine(");");
                _source.Append("        WriteJson(writer, value.Field_").Append(field.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
            }
            _source.AppendLine("        writer.WriteEndObject();");
            _source.AppendLine("    }");
        }

        private void EmitEntryPoint(CheckedFunction entry)
        {
            if (entry.IsAsync)
            {
                _source.AppendLine("    public static async Task<int> Main()");
                _source.AppendLine("    {");
                _source.AppendLine("        using var cancellation = new CancellationTokenSource();");
                _source.AppendLine("        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };");
                _source.AppendLine("        Console.CancelKeyPress += cancelHandler;");
                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.AppendLine("            try");
                _source.AppendLine("            {");
                _source.AppendLine("                Console.OutputEncoding = new System.Text.UTF8Encoding(false);");

                var asyncCall = "(await Function_" + entry.Id.ToString(CultureInfo.InvariantCulture) + "(cancellation.Token))";
                if (entry.ReturnType.IsI32)
                    _source.Append("                Console.WriteLine(").Append(asyncCall).AppendLine(".ToString(CultureInfo.InvariantCulture));");
                else if (entry.ReturnType.IsBool)
                    _source.Append("                Console.WriteLine(").Append(asyncCall).AppendLine(" ? \"true\" : \"false\");");
                else
                    _source.Append("                Console.WriteLine(").Append(asyncCall).AppendLine(");");
                _source.AppendLine("                return 0;");
                _source.AppendLine("            }");
                _source.AppendLine("            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)");
                _source.AppendLine("            {");
                _source.AppendLine("                return 130;");
                _source.AppendLine("            }");
                _source.AppendLine("            catch (Exception)");
                _source.AppendLine("            {");
                _source.AppendLine("                Console.Error.WriteLine(\"Runtime fault\");");
                _source.AppendLine("                return 70;");
                _source.AppendLine("            }");
                _source.AppendLine("        }");
                _source.AppendLine("        finally");
                _source.AppendLine("        {");
                _source.AppendLine("            Console.CancelKeyPress -= cancelHandler;");
                _source.AppendLine("        }");
                _source.AppendLine("    }");
                return;
            }

            _source.AppendLine("    public static int Main()");
            _source.AppendLine("    {");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.OutputEncoding = new System.Text.UTF8Encoding(false);");

            var call = "Function_" + entry.Id.ToString(CultureInfo.InvariantCulture) + "()";
            if (entry.ReturnType.IsI32)
                _source.Append("            Console.WriteLine(").Append(call).AppendLine(".ToString(CultureInfo.InvariantCulture));");
            else if (entry.ReturnType.IsBool)
                _source.Append("            Console.WriteLine(").Append(call).AppendLine(" ? \"true\" : \"false\");");
            else
                _source.Append("            Console.WriteLine(").Append(call).AppendLine(");");
            _source.AppendLine("            return 0;");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (Exception)");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.Error.WriteLine(\"Runtime fault\");");
            _source.AppendLine("            return 70;");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
        }

        private void EmitCommandEntryPoint(CheckedCommand entryCommand)
        {
            var hasAsyncHandlers = program.Commands.Any(command => command.HandlerIsAsync);
            _source.AppendLine(hasAsyncHandlers
                ? "    public static async Task<int> Main(string[] args)"
                : "    public static int Main(string[] args)");
            _source.AppendLine("    {");
            if (hasAsyncHandlers)
            {
                _source.AppendLine("        using var cancellation = new CancellationTokenSource();");
                _source.AppendLine("        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };");
                _source.AppendLine("        Console.CancelKeyPress += cancelHandler;");
            }
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.OutputEncoding = new System.Text.UTF8Encoding(false);");
            _source.AppendLine("            if (args.Length == 1 && args[0] == \"--help\")");
            _source.AppendLine("            {");
            _source.Append("                Console.WriteLine(").Append(JsonSerializer.Serialize(TopLevelHelp())).AppendLine(");");
            _source.AppendLine("                return 0;");
            _source.AppendLine("            }");
            _source.AppendLine("            if (args.Length == 0)");
            _source.AppendLine("                return CliUsageError(\"CLI_MISSING_ARGUMENT\", \"command\");");
            _source.AppendLine("            if (args[0].StartsWith(\"--\", StringComparison.Ordinal))");
            _source.AppendLine("                return CliUsageError(\"CLI_UNKNOWN_OPTION\", args[0]);");
            _source.AppendLine("            switch (args[0])");
            _source.AppendLine("            {");
            foreach (var command in program.Commands.OrderBy(command => command.Id))
            {
                _source.Append("                case ").Append(JsonSerializer.Serialize(command.Name)).AppendLine(":");
                _source.AppendLine("                    if (args.Length == 2 && args[1] == \"--help\")");
                _source.AppendLine("                    {");
                _source.Append("                        Console.WriteLine(").Append(JsonSerializer.Serialize(CommandHelp(command))).AppendLine(");");
                _source.AppendLine("                        return 0;");
                _source.AppendLine("                    }");
                _source.Append("                    return ");
                if (command.HandlerIsAsync) _source.Append("await ");
                _source.Append("RunCommand_").Append(command.Id.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(command.HandlerIsAsync
                        ? "(args[1..], cancellation.Token);"
                        : "(args[1..]);");
            }
            _source.AppendLine("                default:");
            _source.AppendLine("                    return CliUsageError(\"CLI_UNKNOWN_COMMAND\", args[0]);");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            if (hasAsyncHandlers)
            {
                _source.AppendLine("        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)");
                _source.AppendLine("        {");
                _source.AppendLine("            return 130;");
                _source.AppendLine("        }");
            }
            _source.AppendLine("        catch (Exception)");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.Error.WriteLine(\"Runtime fault\");");
            _source.AppendLine("            return 70;");
            _source.AppendLine("        }");
            if (hasAsyncHandlers)
            {
                _source.AppendLine("        finally");
                _source.AppendLine("        {");
                _source.AppendLine("            Console.CancelKeyPress -= cancelHandler;");
                _source.AppendLine("        }");
            }
            _source.AppendLine("    }");
            _source.AppendLine();

            foreach (var command in program.Commands.OrderBy(command => command.Id))
                EmitCommandParser(command);
            EmitCliUsageError();
        }

        private void EmitCommandParser(CheckedCommand command)
        {
            var inputs = command.Inputs.OrderBy(input => input.FieldIndex).ToArray();
            var hasOptionInputs = inputs.Any(input => input.Kind is CheckedCommandInputKind.Option or CheckedCommandInputKind.Flag);
            _source.Append(command.HandlerIsAsync
                    ? "    private static async Task<int> RunCommand_"
                    : "    private static int RunCommand_")
                .Append(command.Id.ToString(CultureInfo.InvariantCulture))
                .AppendLine(command.HandlerIsAsync
                    ? "(string[] args, CancellationToken cancellationToken)"
                    : "(string[] args)");
            _source.AppendLine("    {");
            foreach (var input in inputs)
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                if (input.Kind == CheckedCommandInputKind.Flag)
                {
                    _source.Append("        var input_").Append(index).AppendLine(" = false;");
                    _source.Append("        var seen_").Append(index).AppendLine(" = false;");
                }
                else
                {
                    _source.Append("        string? raw_").Append(index).AppendLine(" = null;");
                    _source.Append("        var seen_").Append(index).AppendLine(" = false;");
                }
            }
            _source.AppendLine("        var positionalIndex = 0;");
            _source.AppendLine("        var positionalOnly = false;");
            _source.AppendLine("        for (var argumentIndex = 0; argumentIndex < args.Length; argumentIndex++)");
            _source.AppendLine("        {");
            _source.AppendLine("            var token = args[argumentIndex];");
            _source.AppendLine("            if (!positionalOnly && token == \"--\")");
            _source.AppendLine("            {");
            _source.AppendLine("                positionalOnly = true;");
            _source.AppendLine("                continue;");
            _source.AppendLine("            }");
            _source.AppendLine("            if (!positionalOnly && token.StartsWith(\"--\", StringComparison.Ordinal))");
            _source.AppendLine("            {");
            _source.AppendLine("                var equalsIndex = token.IndexOf('=');");
            _source.AppendLine("                var optionName = equalsIndex < 0 ? token : token[..equalsIndex];");
            _source.AppendLine("                var hasInlineValue = equalsIndex >= 0;");
            _source.AppendLine("                var inlineValue = hasInlineValue ? token[(equalsIndex + 1)..] : string.Empty;");
            _source.AppendLine("                switch (optionName)");
            _source.AppendLine("                {");
            foreach (var input in inputs.Where(input => input.Kind is CheckedCommandInputKind.Option or CheckedCommandInputKind.Flag))
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                var optionName = JsonSerializer.Serialize("--" + input.Name);
                _source.Append("                    case ").Append(optionName).AppendLine(":");
                _source.Append("                        if (seen_").Append(index).AppendLine(")");
                _source.AppendLine("                            return CliUsageError(\"CLI_DUPLICATE_OPTION\", optionName);");
                _source.Append("                        seen_").Append(index).AppendLine(" = true;");
                if (input.Kind == CheckedCommandInputKind.Flag)
                {
                    _source.AppendLine("                        if (hasInlineValue)");
                    _source.AppendLine("                            return CliUsageError(\"CLI_INVALID_VALUE\", optionName);");
                    _source.Append("                        input_").Append(index).AppendLine(" = true;");
                }
                else
                {
                    _source.AppendLine("                        if (hasInlineValue)");
                    _source.AppendLine("                        {");
                    _source.Append("                            raw_").Append(index).AppendLine(" = inlineValue;");
                    _source.AppendLine("                        }");
                    _source.AppendLine("                        else");
                    _source.AppendLine("                        {");
                    _source.AppendLine("                            if (argumentIndex + 1 >= args.Length || args[argumentIndex + 1].StartsWith(\"--\", StringComparison.Ordinal))");
                    _source.AppendLine("                                return CliUsageError(\"CLI_MISSING_VALUE\", optionName);");
                    _source.Append("                            raw_").Append(index).AppendLine(" = args[++argumentIndex];");
                    _source.AppendLine("                        }");
                }
                _source.AppendLine("                        break;");
            }
            _source.AppendLine("                    default:");
            _source.AppendLine("                        return CliUsageError(\"CLI_UNKNOWN_OPTION\", optionName);");
            _source.AppendLine("                }");
            if (hasOptionInputs)
                _source.AppendLine("                continue;");
            _source.AppendLine("            }");
            _source.AppendLine("            switch (positionalIndex++)");
            _source.AppendLine("            {");
            var argumentOrdinal = 0;
            foreach (var input in inputs.Where(input => input.Kind == CheckedCommandInputKind.Argument))
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                _source.Append("                case ").Append(argumentOrdinal++.ToString(CultureInfo.InvariantCulture)).AppendLine(":");
                _source.Append("                    raw_").Append(index).AppendLine(" = token;");
                _source.Append("                    seen_").Append(index).AppendLine(" = true;");
                _source.AppendLine("                    break;");
            }
            _source.AppendLine("                default:");
            _source.AppendLine("                    return CliUsageError(\"CLI_INVALID_VALUE\", token);");
            _source.AppendLine("            }");
            _source.AppendLine("        }");

            foreach (var input in inputs)
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                if (input.Kind == CheckedCommandInputKind.Argument)
                {
                    _source.Append("        if (!seen_").Append(index).AppendLine(")");
                    _source.Append("            return CliUsageError(\"CLI_MISSING_ARGUMENT\", ")
                        .Append(JsonSerializer.Serialize(input.Name)).AppendLine(");");
                }
            }

            foreach (var input in inputs.Where(input => input.Kind == CheckedCommandInputKind.Option))
            {
                var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
                _source.Append("        if (!seen_").Append(index).AppendLine(")");
                _source.AppendLine("        {");
                _source.Append("            raw_").Append(index).Append(" = ")
                    .Append(JsonSerializer.Serialize(CommandDefaultString(input))).AppendLine(";");
                _source.AppendLine("        }");
            }

            foreach (var input in inputs.Where(input => input.Kind != CheckedCommandInputKind.Flag))
                EmitCommandInputConversion(input);

            var fields = string.Join(", ", inputs.Select(input =>
                "Field_" + input.FieldIndex.ToString(CultureInfo.InvariantCulture) + ": input_" +
                input.FieldIndex.ToString(CultureInfo.InvariantCulture)));
            var resultType = "Result<string, " + EmitType(command.ErrorType) + ">";
            _source.Append(command.HandlerIsAsync ? "        var result = await Function_" : "        var result = Function_")
                .Append(command.HandlerFunctionId.ToString(CultureInfo.InvariantCulture))
                .Append("(new Struct_").Append(command.ArgsStructId.ToString(CultureInfo.InvariantCulture))
                .Append('(').Append(fields).Append(')');
            foreach (var capability in command.Capabilities.OrderBy(capability => capability.HandlerParameterIndex))
            {
                _source.Append(capability.Kind switch
                {
                    CheckedCapabilityKind.FsRead => command.HandlerIsAsync
                        ? ", new FsRead(cancellationToken)"
                        : ", new FsRead(System.Threading.CancellationToken.None)",
                    CheckedCapabilityKind.FsWrite => ", new FsWrite()",
                    _ => throw new InvalidOperationException("Unsupported checked command capability")
                });
            }
            if (command.HandlerIsAsync) _source.Append(", cancellationToken");
            _source.AppendLine(");");
            _source.Append("        if (result is ").Append(resultType).AppendLine(".Ok success)");
            _source.AppendLine("        {");
            _source.AppendLine("            Console.WriteLine(success.Value);");
            _source.AppendLine("            return 0;");
            _source.AppendLine("        }");
            _source.Append("        if (result is ").Append(resultType).AppendLine(".Err failure)");
            _source.AppendLine("        {");
            _source.Append("            var message = Function_").Append(command.ErrorFunctionId.ToString(CultureInfo.InvariantCulture))
                .AppendLine("(failure.Error);");
            _source.AppendLine("            Console.Error.WriteLine(message);");
            _source.AppendLine("            return 3;");
            _source.AppendLine("        }");
            _source.AppendLine("        throw new InvalidOperationException(\"Invalid command handler result\");");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitCommandInputConversion(CheckedCommandInput input)
        {
            var index = input.FieldIndex.ToString(CultureInfo.InvariantCulture);
            switch (input.Type.Kind)
            {
                case LangTypeKind.Text:
                    _source.Append("        var input_").Append(index).Append(" = raw_").Append(index)
                        .AppendLine(" ?? string.Empty;");
                    break;
                case LangTypeKind.I32:
                    _source.Append("        if (!int.TryParse(raw_").Append(index)
                        .AppendLine(", NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed_" + index + "))");
                    _source.Append("            return CliUsageError(\"CLI_INVALID_VALUE\", ")
                        .Append(JsonSerializer.Serialize(input.Name)).AppendLine(");");
                    _source.Append("        var input_").Append(index).Append(" = parsed_").Append(index).AppendLine(";");
                    break;
                case LangTypeKind.FilePath:
                    _source.Append("        var value_").Append(index).Append(" = raw_").Append(index).AppendLine(" ?? string.Empty;");
                    _source.Append("        if (value_").Append(index).Append(".Length == 0 || value_").Append(index)
                        .AppendLine(".IndexOf('\\0') >= 0)");
                    _source.Append("            return CliUsageError(\"CLI_INVALID_VALUE\", ")
                        .Append(JsonSerializer.Serialize(input.Name)).AppendLine(");");
                    _source.Append("        var input_").Append(index).Append(" = new FilePath(value_").Append(index).AppendLine(");");
                    break;
                default:
                    throw new InvalidOperationException("Unsupported checked command input type");
            }
        }

        private static string CommandDefaultString(CheckedCommandInput input)
        {
            if (input.Default is null)
                throw new InvalidOperationException("Checked command option is missing its default");
            return input.Default.Kind switch
            {
                CheckedCommandLiteralKind.Text => input.Default.TextValue ?? string.Empty,
                CheckedCommandLiteralKind.I32 => input.Default.IntegerValue!.Value.ToString(CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException("Unsupported checked command default")
            };
        }

        private string TopLevelHelp()
        {
            var lines = new List<string> { "Usage: <program> <command> [arguments]", "Commands:" };
            foreach (var command in program.Commands.OrderBy(command => command.Id))
                lines.Add("  " + command.Name + "  " + command.Help);
            lines.Add("  --help  Show this help.");
            return string.Join("\n", lines);
        }

        private static string CommandHelp(CheckedCommand command)
        {
            var lines = new List<string> { "Usage: " + command.Name + " <arguments>", command.Help };
            var arguments = command.Inputs.Where(input => input.Kind == CheckedCommandInputKind.Argument)
                .OrderBy(input => input.FieldIndex).ToArray();
            if (arguments.Length != 0)
            {
                lines.Add("Arguments:");
                lines.AddRange(arguments.Select(input => "  " + input.Name + "  " + input.Help));
            }

            var options = command.Inputs.Where(input => input.Kind == CheckedCommandInputKind.Option)
                .OrderBy(input => input.FieldIndex).ToArray();
            if (options.Length != 0)
            {
                lines.Add("Options:");
                lines.AddRange(options.Select(input => "  --" + input.Name + " <" + CommandTypeName(input.Type) + ">  " + input.Help));
            }

            var flags = command.Inputs.Where(input => input.Kind == CheckedCommandInputKind.Flag)
                .OrderBy(input => input.FieldIndex).ToArray();
            if (flags.Length != 0)
            {
                lines.Add("Flags:");
                lines.AddRange(flags.Select(input => "  --" + input.Name + "  " + input.Help));
            }

            lines.Add("  --help  Show this help.");
            return string.Join("\n", lines);
        }

        private void EmitCliUsageError()
        {
            _source.AppendLine("    private static int CliUsageError(string code, string subject)");
            _source.AppendLine("    {");
            _source.AppendLine("""        Console.Error.WriteLine(code + ": \"" + EscapeCliSubject(subject) + "\"");""");
            _source.AppendLine("        return 2;");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    private static string EscapeCliSubject(string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        var escaped = new StringBuilder(value.Length);");
            _source.AppendLine("        foreach (var character in value)");
            _source.AppendLine("        {");
            _source.AppendLine("            switch (character)");
            _source.AppendLine("            {");
            _source.AppendLine("""                case '\\': escaped.Append("\\\\"); break;""");
            _source.AppendLine("""                case '"': escaped.Append("\\\""); break;""");
            _source.AppendLine("                case '\\n': escaped.Append(\"\\\\n\"); break;");
            _source.AppendLine("                case '\\r': escaped.Append(\"\\\\r\"); break;");
            _source.AppendLine("                case '\\t': escaped.Append(\"\\\\t\"); break;");
            _source.AppendLine("                case '\\0': escaped.Append(\"\\\\0\"); break;");
            _source.AppendLine("                default:");
            _source.AppendLine("                    var category = char.GetUnicodeCategory(character);");
            _source.AppendLine("                    if (char.IsControl(character) || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)");
            _source.AppendLine("                        escaped.Append(\"\\\\u\").Append(((int)character).ToString(\"X4\", CultureInfo.InvariantCulture));");
            _source.AppendLine("                    else");
            _source.AppendLine("                        escaped.Append(character);");
            _source.AppendLine("                    break;");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine("        return escaped.ToString();");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitTestEntryPoint(IReadOnlyList<CheckedTest> tests)
        {
            _source.AppendLine("    public static int Main()");
            _source.AppendLine("    {");
            _source.AppendLine("        Console.OutputEncoding = new System.Text.UTF8Encoding(false);");
            _source.AppendLine("        var passed = 0;");
            _source.AppendLine("        var failed = 0;");

            foreach (var test in tests)
            {
                var functionName = "Function_" + test.FunctionId.ToString(CultureInfo.InvariantCulture);
                var module = EscapeDisplayField(test.Module);
                var name = EscapeDisplayField(test.Name);
                var file = EscapeDisplayField(test.At.File);
                var passLabel = JsonSerializer.Serialize("PASS " + module + " :: " + name);
                var failureLabel = JsonSerializer.Serialize(
                    "FAIL " + module + " :: " + name + " (" +
                    file + ":" +
                    test.At.Line.ToString(CultureInfo.InvariantCulture) + ":" +
                    test.At.Column.ToString(CultureInfo.InvariantCulture) + ")");

                _source.AppendLine("        try");
                _source.AppendLine("        {");
                _source.Append("            if (").Append(functionName).AppendLine("())");
                _source.AppendLine("            {");
                _source.AppendLine("                passed++;");
                _source.Append("                Console.WriteLine(").Append(passLabel).AppendLine(");");
                _source.AppendLine("            }");
                _source.AppendLine("            else");
                _source.AppendLine("            {");
                _source.AppendLine("                failed++;");
                _source.Append("                Console.WriteLine(").Append(failureLabel).AppendLine(");");
                _source.AppendLine("            }");
                _source.AppendLine("        }");
                _source.AppendLine("        catch (Exception)");
                _source.AppendLine("        {");
                _source.AppendLine("            failed++;");
                _source.Append("            Console.WriteLine(").Append(failureLabel)
                    .AppendLine(" + \" Runtime fault\");");
                _source.AppendLine("        }");
            }

            _source.AppendLine(
                "        Console.WriteLine(passed.ToString(CultureInfo.InvariantCulture) + " +
                "\" passed, \" + failed.ToString(CultureInfo.InvariantCulture) + \" failed\");");
            _source.AppendLine("        return failed == 0 ? 0 : 1;");
            _source.AppendLine("    }");
        }

        private static string EscapeDisplayField(string value)
        {
            var escaped = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    case '\0':
                        escaped.Append("\\0");
                        break;
                    default:
                        var category = char.GetUnicodeCategory(character);
                        if (char.IsControl(character) ||
                            category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                        {
                            escaped.Append("\\u")
                                .Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(character);
                        }
                        break;
                }
            }

            return escaped.ToString();
        }

        private string EmitType(LangType type) => type.Kind switch
        {
            LangTypeKind.I32 => "int",
            LangTypeKind.Bool => "bool",
            LangTypeKind.Text => "string",
            LangTypeKind.List => "global::System.Collections.Immutable.ImmutableArray<" + EmitType(type.Arguments[0]) + ">",
            LangTypeKind.FilePath => "FilePath",
            LangTypeKind.Html => "Html",
            LangTypeKind.Union => "Union_" + type.UnionId.ToString(CultureInfo.InvariantCulture),
            LangTypeKind.Struct => "Struct_" + type.StructId.ToString(CultureInfo.InvariantCulture),
            LangTypeKind.Option => "Option<" + EmitType(type.Arguments[0]) + ">",
            LangTypeKind.Result => "Result<" + EmitType(type.Arguments[0]) + ", " + EmitType(type.Arguments[1]) + ">",
            LangTypeKind.TypeParameter => EmitTypeParameter(type),
            LangTypeKind.FsRead => "FsRead",
            LangTypeKind.FsWrite => "FsWrite",
            LangTypeKind.FsError => "FsError",
            LangTypeKind.DbRead => "DbRead",
            LangTypeKind.DbWrite => "DbWrite",
            LangTypeKind.Transaction => "DbTransaction",
            LangTypeKind.DbError => "DbError",
            _ => throw new InvalidOperationException("Error type reached emitter")
        };

        private string EmitTypeParameter(LangType type)
        {
            var ordinal = type.TypeParameterOrdinal;
            if (_emittingFunction is null ||
                type.TypeParameterOwnerId != _emittingFunction.Id ||
                ordinal < 0 || ordinal >= _emittingFunction.TypeParameters.Count)
                throw new InvalidOperationException("Type parameter reached emitter outside its defining function");
            return TypeParameterName(ordinal);
        }

        private static string TypeParameterName(int ordinal) =>
            "T" + ordinal.ToString(CultureInfo.InvariantCulture);

        private bool NeedsFilePathType => UsesTypeKind(LangTypeKind.FilePath);

        private bool NeedsHtmlType => UsesTypeKind(LangTypeKind.Html);

        private bool NeedsFsReadType => UsesTypeKind(LangTypeKind.FsRead) || UsesFsReadText ||
            program.Commands.Any(command => command.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsRead));

        private bool NeedsFsWriteType => UsesTypeKind(LangTypeKind.FsWrite) || UsesFsWriteText ||
            program.Commands.Any(command => command.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsWrite)) ||
            program.Routes.Any(route => route.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsWrite));

        private bool NeedsFsErrorType => UsesTypeKind(LangTypeKind.FsError) || UsesFsReadText || UsesFsReadTextAsync || UsesFsWriteText;

        private bool NeedsDbReadType => UsesTypeKind(LangTypeKind.DbRead) || UsesDatabase;

        private bool NeedsDbWriteType => UsesTypeKind(LangTypeKind.DbWrite) || UsesDatabase;

        private bool NeedsDbTransactionType => UsesTypeKind(LangTypeKind.Transaction) ||
            TransactionScopes.Count != 0 || TransactionCommits.Count != 0;

        private bool NeedsDbErrorType => UsesTypeKind(LangTypeKind.DbError) || UsesDatabase;

        private bool UsesDatabase => _webDatabaseOptions is not null || DatabaseCalls.Count != 0 ||
            TransactionScopes.Count != 0 || TransactionCommits.Count != 0;

        private IReadOnlyList<TypedDatabaseCallExpr> DatabaseCalls => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedDatabaseCallExpr>()
            .ToArray();

        private IReadOnlyList<TypedWithTransactionStmt> TransactionScopes => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .OfType<TypedWithTransactionStmt>()
            .ToArray();

        private IReadOnlyList<TypedTransactionCommitExpr> TransactionCommits => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedTransactionCommitExpr>()
            .ToArray();

        private bool UsesAsyncFunctions => EmittedFunctions.Any(function => function.IsAsync);

        private bool UsesFsReadText => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedIntrinsicCallExpr>()
            .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.FsReadText);

        private bool UsesFsReadTextAsync => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedIntrinsicCallExpr>()
            .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.FsReadTextAsync);

        private bool UsesFsWriteText => EmittedFunctions.Any(function =>
            function.InferredEffects.Contains("fs.write", StringComparer.Ordinal) ||
            EnumerateStatements(function.Body)
                .SelectMany(StatementExpressions)
                .SelectMany(EnumerateExpressions)
                .OfType<TypedIntrinsicCallExpr>()
                .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.FsWriteText));

        private bool UsesTextLength => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .Any(expression => expression is TypedTextLengthExpr);

        private bool UsesListGet => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .Any(expression => expression is TypedListGetExpr);

        private bool UsesTextSplit => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedIntrinsicCallExpr>()
            .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.TextSplit);

        private bool UsesHtmlBuilders => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedIntrinsicCallExpr>()
            .Any(expression => expression.Intrinsic is BuiltinIntrinsic.HtmlText or BuiltinIntrinsic.HtmlHeading or
                BuiltinIntrinsic.HtmlParagraph or BuiltinIntrinsic.HtmlConcat or BuiltinIntrinsic.HtmlDocument);

        private bool UsesTypeKind(LangTypeKind kind) => EnumerateDeclaredTypes()
            .Any(type => ContainsTypeKind(type, kind));

        private IEnumerable<LangType> EnumerateDeclaredTypes()
        {
            foreach (var union in program.Unions)
            foreach (var variant in union.Variants)
            foreach (var field in variant.Fields)
                yield return field.Type;

            foreach (var structure in program.Structs)
            foreach (var field in structure.Fields)
                yield return field.Type;

            foreach (var function in EmittedFunctions)
            {
                foreach (var parameter in function.Parameters)
                    yield return parameter.Type;
                yield return function.ReturnType;

                foreach (var statement in EnumerateStatements(function.Body))
                {
                    if (statement is TypedLetStmt let)
                        yield return let.Type;
                    if (statement is TypedWithTransactionStmt)
                        yield return LangType.Transaction;
                }

                foreach (var statement in EnumerateStatements(function.Body))
                foreach (var expression in StatementExpressions(statement).SelectMany(EnumerateExpressions))
                {
                    yield return expression.Type;
                    if (expression is TypedCallExpr call)
                    foreach (var typeArgument in call.TypeArguments)
                        yield return typeArgument;
                }
            }
        }

        private static IEnumerable<TypedStmt> EnumerateStatements(IEnumerable<TypedStmt> statements)
        {
            foreach (var statement in statements)
            {
                yield return statement;
                switch (statement)
                {
                    case TypedIfStmt conditional:
                        foreach (var nested in EnumerateStatements(conditional.ThenBody)) yield return nested;
                        if (conditional.ElseBody is not null)
                            foreach (var nested in EnumerateStatements(conditional.ElseBody)) yield return nested;
                        break;
                    case TypedForStmt loop:
                        foreach (var nested in EnumerateStatements(loop.Body)) yield return nested;
                        break;
                    case TypedWithTransactionStmt transaction:
                        foreach (var nested in EnumerateStatements(transaction.Body)) yield return nested;
                        break;
                }
            }
        }

        private static IEnumerable<TypedExpr> StatementExpressions(TypedStmt statement)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    yield return let.Value;
                    break;
                case TypedAssignStmt assignment:
                    yield return assignment.Value;
                    break;
                case TypedReturnStmt ret:
                    yield return ret.Value;
                    break;
                case TypedIfStmt conditional:
                    yield return conditional.Condition;
                    break;
                case TypedForStmt loop:
                    yield return loop.Collection;
                    break;
                case TypedWithTransactionStmt transaction:
                    yield return transaction.Database;
                    break;
            }
        }

        private static IEnumerable<TypedExpr> EnumerateExpressions(TypedExpr expression)
        {
            yield return expression;
            switch (expression)
            {
                case TypedListExpr list:
                    foreach (var item in list.Items)
                    foreach (var nested in EnumerateExpressions(item)) yield return nested;
                    break;
                case TypedBinaryExpr binary:
                    foreach (var nested in EnumerateExpressions(binary.Left)) yield return nested;
                    foreach (var nested in EnumerateExpressions(binary.Right)) yield return nested;
                    break;
                case TypedCompareExpr comparison:
                    foreach (var nested in EnumerateExpressions(comparison.Left)) yield return nested;
                    foreach (var nested in EnumerateExpressions(comparison.Right)) yield return nested;
                    break;
                case TypedCallExpr call:
                    foreach (var argument in call.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedAwaitExpr awaited:
                    foreach (var nested in EnumerateExpressions(awaited.Value)) yield return nested;
                    break;
                case TypedDatabaseCallExpr databaseCall:
                    foreach (var nested in EnumerateExpressions(databaseCall.Receiver)) yield return nested;
                    foreach (var nested in EnumerateExpressions(databaseCall.Parameters)) yield return nested;
                    break;
                case TypedTextLengthExpr length:
                    foreach (var nested in EnumerateExpressions(length.Target)) yield return nested;
                    break;
                case TypedTextTrimExpr trim:
                    foreach (var nested in EnumerateExpressions(trim.Target)) yield return nested;
                    break;
                case TypedListLengthExpr length:
                    foreach (var nested in EnumerateExpressions(length.Target)) yield return nested;
                    break;
                case TypedListGetExpr get:
                    foreach (var nested in EnumerateExpressions(get.Target)) yield return nested;
                    foreach (var nested in EnumerateExpressions(get.Index)) yield return nested;
                    break;
                case TypedListAppendExpr append:
                    foreach (var nested in EnumerateExpressions(append.Target)) yield return nested;
                    foreach (var nested in EnumerateExpressions(append.Value)) yield return nested;
                    break;
                case TypedIntrinsicCallExpr intrinsic:
                    foreach (var argument in intrinsic.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedBuiltinConstructExpr builtin:
                    foreach (var argument in builtin.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedUnionConstructExpr variant:
                    foreach (var argument in variant.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedStructConstructExpr structure:
                    foreach (var field in structure.Fields)
                    foreach (var nested in EnumerateExpressions(field.Value)) yield return nested;
                    break;
                case TypedFieldAccessExpr field:
                    foreach (var nested in EnumerateExpressions(field.Target)) yield return nested;
                    break;
                case TypedMatchExpr match:
                    foreach (var nested in EnumerateExpressions(match.Value)) yield return nested;
                    foreach (var arm in match.Arms)
                    foreach (var nested in EnumerateExpressions(arm.Body)) yield return nested;
                    break;
            }
        }

        private static bool ContainsTypeKind(LangType type, LangTypeKind kind) =>
            type.Kind == kind || type.Arguments.Any(argument => ContainsTypeKind(argument, kind));

        private void Indent(int level) => _source.Append(' ', level * 4);
    }
}
