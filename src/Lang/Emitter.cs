using System.Globalization;
using System.Text;
using System.Text.Json;

internal sealed record ProcessRunnerRuntimeOptions(string ArtifactFileName, string Sha256);

internal static class Emitter
{
    public static string Emit(
        CheckedProgram program,
        bool executable = true,
        WebDatabaseOptions? webDatabaseOptions = null,
        string? httpOrigin = null,
        ProcessRunnerRuntimeOptions? processRunnerOptions = null)
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

        var emitter = new SourceEmitter(
            program,
            webDatabaseOptions: webDatabaseOptions,
            httpOrigin: httpOrigin,
            processRunnerOptions: processRunnerOptions);
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
        var routeBindings = route.Bindings.OrderBy(binding => binding.HandlerParameterIndex).ToArray();
        if (routeBindings.Length != 0)
        {
            writer.WriteStartArray("parameters");
            foreach (var binding in routeBindings)
            {
                writer.WriteStartObject();
                writer.WriteString("name", binding.WireName);
                writer.WriteString("in", binding.Kind switch
                {
                    CheckedRouteBindingKind.Path => "path",
                    CheckedRouteBindingKind.Query => "query",
                    _ => throw new InvalidOperationException("Unknown checked route binding kind")
                });
                writer.WriteBoolean("required", binding.Kind == CheckedRouteBindingKind.Path || !binding.IsOptional);
                writer.WritePropertyName("schema");
                var parameterType = RouteBindingValueType(binding);
                if (parameterType.Kind is not (LangTypeKind.I32 or LangTypeKind.Text))
                    throw new InvalidOperationException("Unsupported checked route parameter type");
                WriteOpenApiTypeSchema(writer, parameterType);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
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

    private static LangType RouteBindingValueType(CheckedRouteBinding binding)
    {
        if (binding.IsOptional)
        {
            if (binding.Kind != CheckedRouteBindingKind.Query || binding.Type.Kind != LangTypeKind.Option || binding.Type.Arguments.Count != 1)
                throw new InvalidOperationException("Invalid checked optional route binding");
            return binding.Type.Arguments[0];
        }

        if (binding.Type.Kind == LangTypeKind.Option)
            throw new InvalidOperationException("Required route binding has an optional type");
        return binding.Type;
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
            writer.WriteNumber("schema_version", 4);
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
        CheckedCapabilityKind.HttpClient => "net.client",
        CheckedCapabilityKind.Config => "env.read",
        CheckedCapabilityKind.Secrets => "secret.reveal",
        CheckedCapabilityKind.Logger => "log.write",
        CheckedCapabilityKind.ProcessRunner => "process.spawn",
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
        WebDatabaseOptions? webDatabaseOptions = null,
        string? httpOrigin = null,
        ProcessRunnerRuntimeOptions? processRunnerOptions = null)
    {
        private readonly StringBuilder _source = new();
        private CheckedFunction? _emittingFunction;
        private CheckedStruct? _emittingStruct;
        private CheckedUnion? _emittingUnion;
        private CheckedTrait? _emittingTrait;
        private LangType? _emittingTraitSelfTarget;
        private readonly Dictionary<LangType, string> _nominalTypeParameterEqualityNames = [];
        private Dictionary<(LangTypeKind Kind, int Id), int[]>? _nominalEqualityParameterOrdinals;
        private readonly HashSet<int> _testFunctionIds = program.Tests
            .Select(test => test.FunctionId)
            .ToHashSet();
        private readonly IReadOnlySet<int> _includedTestFunctionIds = includedTestFunctionIds ?? new HashSet<int>();
        private readonly WebDatabaseOptions? _webDatabaseOptions = webDatabaseOptions;
        private readonly string? _httpOrigin = httpOrigin;
        private readonly ProcessRunnerRuntimeOptions? _processRunnerOptions = processRunnerOptions;
        private int _structuralEqualityTemporaryId;

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
            if (UsesFsReadText || UsesFsReadTextAsync || UsesFsWriteText || UsesFsWriteTextAsync)
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
            if (UsesResultPropagation) EmitResultPropagationRuntime();
            if (NeedsBytesType) EmitBytesType();
            if (NeedsBytesErrorType) EmitBytesErrorType();
            if (NeedsConfigSnapshot) EmitConfigSnapshotRuntime();
            if (NeedsSecretTextType) EmitSecretTextType();
            if (NeedsConfigType) EmitConfigType();
            if (NeedsSecretsType) EmitSecretsType();
            if (NeedsLoggerType) EmitLoggerType();
            if (NeedsFilePathType) EmitFilePathType();
            if (NeedsHtmlType || webHost) EmitHtmlType();
            if (NeedsFsReadType) EmitFsReadType();
            if (NeedsFsWriteType) EmitFsWriteType();
            if (NeedsFsErrorType) EmitFsErrorType();
            if (NeedsDbReadType) EmitDbReadType();
            if (NeedsDbWriteType) EmitDbWriteType();
            if (NeedsDbTransactionType) EmitDbTransactionType();
            if (NeedsDbErrorType) EmitDbErrorType();
            if (NeedsHttpClientType) EmitHttpClientType();
            if (NeedsHttpResponseType) EmitHttpResponseType();
            if (NeedsHttpErrorType) EmitHttpErrorType();
            if (NeedsProcessRunnerRuntime) EmitProcessRunnerRuntime();
            foreach (var union in program.Unions) EmitUnion(union);
            foreach (var structure in program.Structs) EmitStruct(structure);
            foreach (var newtype in program.Newtypes) EmitNewtype(newtype);
            foreach (var trait in program.Traits) EmitTrait(trait);
            foreach (var implementation in program.TraitImpls) EmitTraitImpl(implementation);
            if (UsesStructuralEquality) EmitStructuralEqualityHelpers();
            foreach (var function in EmittedFunctions) EmitFunction(function);
            if (UsesFsReadText) EmitFsReadTextHelper();
            if (UsesFsReadTextAsync) EmitFsReadTextAsyncHelper();
            if (UsesFsWriteText) EmitFsWriteTextHelper();
            if (UsesFsWriteTextAsync) EmitFsWriteTextAsyncHelper();
            if (UsesHttpGetTextAsync) EmitHttpGetTextAsyncHelper();
            if (UsesTextLength) EmitTextLengthHelper();
            if (UsesListGet) EmitListGetHelper();
            if (UsesMapGet) EmitMapGetHelper();
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

        private void EmitResultPropagationRuntime()
        {
            _source.AppendLine("    private sealed class ResultPropagationSignal<E> : Exception");
            _source.AppendLine("    {");
            _source.AppendLine("        public ResultPropagationSignal(object activation, E error)");
            _source.AppendLine("        {");
            _source.AppendLine("            Activation = activation;");
            _source.AppendLine("            Error = error;");
            _source.AppendLine("        }");
            _source.AppendLine("        public object Activation { get; }");
            _source.AppendLine("        public E Error { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    private static TValue PropagateResult<TValue, TError>(Result<TValue, TError> result, object activation)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (result is Result<TValue, TError>.Ok ok) return ok.Value;");
            _source.AppendLine("        if (result is Result<TValue, TError>.Err error)");
            _source.AppendLine("            throw new ResultPropagationSignal<TError>(activation, error.Error);");
            _source.AppendLine("        throw new InvalidOperationException(\"Result value has an unknown variant\");");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitBytesType()
        {
            _source.AppendLine("    public sealed class Bytes");
            _source.AppendLine("    {");
            _source.AppendLine("        private static readonly Bytes EmptyValue = new(global::System.Collections.Immutable.ImmutableArray<byte>.Empty);");
            _source.AppendLine("        private readonly global::System.Collections.Immutable.ImmutableArray<byte> _octets;");
            _source.AppendLine("        private Bytes(global::System.Collections.Immutable.ImmutableArray<byte> octets) => _octets = octets;");
            _source.AppendLine("        internal static Bytes Empty() => EmptyValue;");
            _source.AppendLine("        internal static Result<Bytes, BytesError> Append(Bytes receiver, int octet)");
            _source.AppendLine("        {");
            _source.AppendLine("            if (octet < 0 || octet > byte.MaxValue)");
            _source.AppendLine("                return new Result<Bytes, BytesError>.Err(new BytesError.InvalidOctet());");
            _source.AppendLine("            return new Result<Bytes, BytesError>.Ok(new Bytes(receiver._octets.Add((byte)octet)));");
            _source.AppendLine("        }");
            _source.AppendLine("        internal int Length => _octets.Length;");
            _source.AppendLine("        internal Option<int> Get(int index)");
            _source.AppendLine("        {");
            _source.AppendLine("            if (index < 0 || index >= _octets.Length)");
            _source.AppendLine("                return new Option<int>.None();");
            _source.AppendLine("            return new Option<int>.Some(_octets[index]);");
            _source.AppendLine("        }");
            _source.AppendLine("        internal bool SequenceEquals(Bytes other)");
            _source.AppendLine("        {");
            _source.AppendLine("            if (_octets.Length != other._octets.Length) return false;");
            _source.AppendLine("            for (var index = 0; index < _octets.Length; index++)");
            _source.AppendLine("                if (_octets[index] != other._octets[index]) return false;");
            _source.AppendLine("            return true;");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitBytesErrorType()
        {
            _source.AppendLine("    public abstract record BytesError");
            _source.AppendLine("    {");
            _source.AppendLine("        private protected BytesError() { }");
            _source.AppendLine("        public sealed record InvalidOctet() : BytesError;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitConfigSnapshotRuntime()
        {
            _source.AppendLine("    internal sealed class ConfigSnapshot");
            _source.AppendLine("    {");
            foreach (var (field, index) in program.ConfigFields.Select((field, index) => (field, index)))
                _source.Append("        internal ").Append(ConfigFieldRuntimeType(field)).Append(" Field_")
                    .Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(" { get; }");

            var constructorParameters = program.ConfigFields.Select((field, index) =>
                ConfigFieldRuntimeType(field) + " field_" + index.ToString(CultureInfo.InvariantCulture));
            _source.Append("        internal ConfigSnapshot(").Append(string.Join(", ", constructorParameters)).AppendLine(")");
            _source.AppendLine("        {");
            foreach (var (_, index) in program.ConfigFields.Select((field, index) => (field, index)))
                _source.Append("            Field_").Append(index.ToString(CultureInfo.InvariantCulture)).Append(" = field_")
                    .Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();

            _source.AppendLine("    private static bool TryCreateConfigSnapshot(out ConfigSnapshot snapshot, out string missingConfiguration)");
            _source.AppendLine("    {");
            foreach (var (field, index) in program.ConfigFields.Select((field, index) => (field, index)))
            {
                var suffix = index.ToString(CultureInfo.InvariantCulture);
                var rawName = "rawConfig_" + suffix;
                _source.Append("        var ").Append(rawName).Append(" = Environment.GetEnvironmentVariable(")
                    .Append(JsonSerializer.Serialize(field.EnvironmentName)).AppendLine(");");
            }

            foreach (var (field, index) in program.ConfigFields.Select((field, index) => (field, index)))
            {
                var suffix = index.ToString(CultureInfo.InvariantCulture);
                var rawName = "rawConfig_" + suffix;
                var valueName = "configField_" + suffix;
                if (field.Required)
                {
                    _source.Append("        if (").Append(rawName).AppendLine(" is null)");
                    _source.AppendLine("        {");
                    _source.Append("            missingConfiguration = ")
                        .Append(JsonSerializer.Serialize(field.Name + " (" + field.EnvironmentName + ")")).AppendLine(";");
                    _source.AppendLine("            snapshot = null!;");
                    _source.AppendLine("            return false;");
                    _source.AppendLine("        }");
                    if (field.Kind == ConfigFieldKind.SecretText)
                    {
                        if (field.HasDefault)
                            throw new InvalidOperationException("Secret config fields cannot have defaults");
                        _source.Append("        var ").Append(valueName).Append(" = CreateSecretText(").Append(rawName).AppendLine("!);");
                    }
                    else
                    {
                        if (field.HasDefault)
                            throw new InvalidOperationException("Required config fields cannot also have defaults");
                        _source.Append("        var ").Append(valueName).Append(" = ").Append(rawName).AppendLine("!;");
                    }
                }
                else if (field.HasDefault && field.Kind == ConfigFieldKind.Text)
                {
                    _source.Append("        var ").Append(valueName).Append(" = ").Append(rawName).Append(" ?? ")
                        .Append(JsonSerializer.Serialize(field.DefaultValue ??
                            throw new InvalidOperationException("Text config default is missing its literal"))).AppendLine(";");
                }
                else
                {
                    throw new InvalidOperationException("Checked config fields must be required or have a text default");
                }
            }

            _source.Append("        snapshot = new ConfigSnapshot(")
                .Append(string.Join(", ", program.ConfigFields.Select((_, index) =>
                    "configField_" + index.ToString(CultureInfo.InvariantCulture))))
                .AppendLine(");");
            _source.AppendLine("        missingConfiguration = string.Empty;");
            _source.AppendLine("        return true;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitSecretTextType()
        {
            _source.AppendLine("    private sealed class SecretTextContents");
            _source.AppendLine("    {");
            _source.AppendLine("        internal SecretTextContents(string value) => Value = value;");
            _source.AppendLine("        internal string Value { get; }");
            _source.AppendLine("    }");
            _source.AppendLine("    private static readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<SecretText, SecretTextContents> SecretTextValues = new();");
            _source.AppendLine("    public sealed class SecretText : IFormattable");
            _source.AppendLine("    {");
            _source.AppendLine("        internal SecretText() { }");
            _source.AppendLine("        public override string ToString() => \"[REDACTED]\";");
            _source.AppendLine("        string IFormattable.ToString(string? format, IFormatProvider? formatProvider) => \"[REDACTED]\";");
            _source.AppendLine("    }");
            _source.AppendLine("    private static SecretText CreateSecretText(string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        ArgumentNullException.ThrowIfNull(value);");
            _source.AppendLine("        var secret = new SecretText();");
            _source.AppendLine("        SecretTextValues.Add(secret, new SecretTextContents(value));");
            _source.AppendLine("        return secret;");
            _source.AppendLine("    }");
            _source.AppendLine("    private static string RevealSecretText(SecretText value)");
            _source.AppendLine("    {");
            _source.AppendLine("        ArgumentNullException.ThrowIfNull(value);");
            _source.AppendLine("        if (!SecretTextValues.TryGetValue(value, out var contents)) throw new InvalidOperationException(\"Unknown secret value\");");
            _source.AppendLine("        return contents.Value;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitConfigType()
        {
            _source.AppendLine("    public sealed class Config");
            _source.AppendLine("    {");
            _source.AppendLine("        private readonly ConfigSnapshot _snapshot;");
            _source.AppendLine("        internal Config(ConfigSnapshot snapshot) => _snapshot = snapshot;");
            foreach (var (field, index) in program.ConfigFields.Select((field, index) => (field, index)))
            {
                var suffix = index.ToString(CultureInfo.InvariantCulture);
                var resultType = ConfigFieldRuntimeType(field);
                var methodName = field.Kind == ConfigFieldKind.SecretText ? "ReadSecretText_" : "ReadText_";
                _source.Append("        internal ").Append(resultType).Append(' ').Append(methodName).Append(suffix)
                    .Append("() => _snapshot.Field_").Append(suffix).AppendLine(";");
            }
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitSecretsType()
        {
            _source.AppendLine("    public sealed class Secrets");
            _source.AppendLine("    {");
            _source.AppendLine("        internal Secrets() { }");
            _source.AppendLine("        internal string RevealText(SecretText value) => RevealSecretText(value);");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitLoggerType()
        {
            _source.AppendLine("    public sealed class Logger");
            _source.AppendLine("    {");
            _source.AppendLine("        private readonly string? _requestId;");
            _source.AppendLine("        internal Logger(string? requestId) => _requestId = requestId;");
            _source.AppendLine("        internal bool Info(string eventName, string detail)");
            _source.AppendLine("        {");
            _source.AppendLine("            ArgumentNullException.ThrowIfNull(eventName);");
            _source.AppendLine("            ArgumentNullException.ThrowIfNull(detail);");
            _source.AppendLine("            WriteInfoLog(eventName, detail, _requestId);");
            _source.AppendLine("            return true;");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine("    private static readonly object LogWriteLock = new();");
            _source.AppendLine("    private static readonly global::System.IO.Stream LogErrorStream = Console.OpenStandardError();");
            _source.AppendLine("    private static void WriteInfoLog(string eventName, string detail, string? requestId)");
            _source.AppendLine("    {");
            _source.AppendLine("        using var buffer = new global::System.IO.MemoryStream();");
            _source.AppendLine("        using (var writer = new global::System.Text.Json.Utf8JsonWriter(buffer))");
            _source.AppendLine("        {");
            _source.AppendLine("            writer.WriteStartObject();");
            _source.AppendLine("            writer.WriteString(\"level\", \"info\");");
            _source.AppendLine("            writer.WriteString(\"event\", eventName);");
            _source.AppendLine("            writer.WriteString(\"detail\", detail);");
            _source.AppendLine("            if (!string.IsNullOrEmpty(requestId)) writer.WriteString(\"request_id\", requestId);");
            _source.AppendLine("            writer.WriteEndObject();");
            _source.AppendLine("            writer.Flush();");
            _source.AppendLine("        }");
            _source.AppendLine("        buffer.WriteByte((byte)'\\n');");
            _source.AppendLine("        var record = buffer.ToArray();");
            _source.AppendLine("        lock (LogWriteLock)");
            _source.AppendLine("        {");
            _source.AppendLine("            LogErrorStream.Write(record);");
            _source.AppendLine("            LogErrorStream.Flush();");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private static string ConfigFieldRuntimeType(CheckedConfigField field) => field.Kind switch
        {
            ConfigFieldKind.Text => "string",
            ConfigFieldKind.SecretText => "SecretText",
            _ => throw new InvalidOperationException("Unknown checked config field kind")
        };

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
            _source.AppendLine("        internal FsWrite() : this(System.Threading.CancellationToken.None) { }");
            _source.AppendLine("        internal FsWrite(System.Threading.CancellationToken cancellationToken) => CancellationToken = cancellationToken;");
            _source.AppendLine("        internal System.Threading.CancellationToken CancellationToken { get; }");
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

        private void EmitHttpClientType()
        {
            _source.AppendLine("    public sealed class HttpClientCapability");
            _source.AppendLine("    {");
            _source.AppendLine("        internal HttpClientCapability(string origin, global::System.Threading.CancellationToken cancellationToken)");
            _source.AppendLine("        {");
            _source.AppendLine("            if (!global::System.Uri.TryCreate(origin, global::System.UriKind.Absolute, out var validatedOrigin) || validatedOrigin is null ||");
            _source.AppendLine("                (validatedOrigin.Scheme != global::System.Uri.UriSchemeHttp && validatedOrigin.Scheme != global::System.Uri.UriSchemeHttps) ||");
            _source.AppendLine("                validatedOrigin.UserInfo.Length != 0 || validatedOrigin.AbsolutePath != \"/\" ||");
            _source.AppendLine("                validatedOrigin.Query.Length != 0 || validatedOrigin.Fragment.Length != 0)");
            _source.AppendLine("                throw new global::System.ArgumentException(\"Invalid configured HTTP origin\", nameof(origin));");
            _source.AppendLine("            Origin = validatedOrigin;");
            _source.AppendLine("            CancellationToken = cancellationToken;");
            _source.AppendLine("        }");
            _source.AppendLine("        internal global::System.Uri Origin { get; }");
            _source.AppendLine("        internal global::System.Threading.CancellationToken CancellationToken { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitHttpResponseType()
        {
            _source.AppendLine("    public sealed class HttpResponse");
            _source.AppendLine("    {");
            _source.AppendLine("        internal HttpResponse(int status, string body) { Field_0 = status; Field_1 = body; }");
            _source.AppendLine("        public int Field_0 { get; }");
            _source.AppendLine("        public string Field_1 { get; }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitHttpErrorType()
        {
            _source.AppendLine("    public abstract record HttpError");
            _source.AppendLine("    {");
            _source.AppendLine("        private protected HttpError() { }");
            _source.AppendLine("        public sealed record InvalidTarget() : HttpError;");
            _source.AppendLine("        public sealed record Transport() : HttpError;");
            _source.AppendLine("        public sealed record Timeout() : HttpError;");
            _source.AppendLine("        public sealed record ResponseTooLarge() : HttpError;");
            _source.AppendLine("        public sealed record InvalidText() : HttpError;");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitProcessRunnerRuntime()
        {
            _source.AppendLine("    public sealed record ProcessOutput(int Field_0, string Field_1, string Field_2);");
            _source.AppendLine("    public abstract record ProcessError");
            _source.AppendLine("    {");
            _source.AppendLine("        private protected ProcessError() { }");
            _source.AppendLine("        public sealed record InvalidArgument() : ProcessError;");
            _source.AppendLine("        public sealed record InputTooLarge() : ProcessError;");
            _source.AppendLine("        public sealed record OutputTooLarge() : ProcessError;");
            _source.AppendLine("        public sealed record InvalidText() : ProcessError;");
            _source.AppendLine("        public sealed record StartFailed() : ProcessError;");
            _source.AppendLine("        public sealed record TimedOut() : ProcessError;");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    public sealed class ProcessRunner");
            _source.AppendLine("    {");
            _source.AppendLine("        private const int MaximumArgumentCount = 128;");
            _source.AppendLine("        private const int MaximumArgumentBytes = 16 * 1024;");
            _source.AppendLine("        private const int MaximumInputBytes = 1024 * 1024;");
            _source.AppendLine("        private const int MaximumOutputBytes = 1024 * 1024;");
            _source.AppendLine("        private static readonly global::System.Text.UTF8Encoding StrictUtf8 = new(false, true);");
            _source.AppendLine("        private readonly string _artifactDirectory;");
            _source.AppendLine("        private readonly string _executablePath;");
            _source.AppendLine("        private readonly string _expectedSha256;");
            _source.AppendLine();
            _source.AppendLine("        internal ProcessRunner(string artifactFileName, string expectedSha256)");
            _source.AppendLine("        {");
            _source.AppendLine("            _artifactDirectory = global::System.IO.Path.GetFullPath(global::System.AppContext.BaseDirectory);");
            _source.AppendLine("            _executablePath = global::System.IO.Path.GetFullPath(global::System.IO.Path.Combine(_artifactDirectory, artifactFileName));");
            _source.AppendLine("            _expectedSha256 = expectedSha256;");
            _source.AppendLine("        }");
            _source.AppendLine();
            _source.AppendLine("        internal async global::System.Threading.Tasks.Task<Result<ProcessOutput, ProcessError>> RunTextAsync(");
            _source.AppendLine("            global::System.Collections.Generic.List<string> arguments,");
            _source.AppendLine("            string stdin,");
            _source.AppendLine("            global::System.Threading.CancellationToken cancellationToken)");
            _source.AppendLine("        {");
            _source.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("            if (arguments is null || arguments.Count > MaximumArgumentCount)");
            _source.AppendLine("                return Error(new ProcessError.InvalidArgument(), cancellationToken);");
            _source.AppendLine();
            _source.AppendLine("            var argumentBytes = 0;");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                foreach (var argument in arguments)");
            _source.AppendLine("                {");
            _source.AppendLine("                    if (argument is null || argument.IndexOf('\\0') >= 0)");
            _source.AppendLine("                        return Error(new ProcessError.InvalidArgument(), cancellationToken);");
            _source.AppendLine("                    if (argument.Length > MaximumArgumentBytes)");
            _source.AppendLine("                        return Error(new ProcessError.InvalidArgument(), cancellationToken);");
            _source.AppendLine("                    var currentArgumentBytes = StrictUtf8.GetByteCount(argument);");
            _source.AppendLine("                    if (currentArgumentBytes > MaximumArgumentBytes - argumentBytes)");
            _source.AppendLine("                        return Error(new ProcessError.InvalidArgument(), cancellationToken);");
            _source.AppendLine("                    argumentBytes += currentArgumentBytes;");
            _source.AppendLine("                }");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.Text.EncoderFallbackException)");
            _source.AppendLine("            {");
            _source.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                return Error(new ProcessError.InvalidText(), cancellationToken);");
            _source.AppendLine("            }");
            _source.AppendLine();
            _source.AppendLine("            if (stdin is null)");
            _source.AppendLine("                return Error(new ProcessError.InvalidText(), cancellationToken);");
            _source.AppendLine("            if (stdin.Length > MaximumInputBytes)");
            _source.AppendLine("                return Error(new ProcessError.InputTooLarge(), cancellationToken);");
            _source.AppendLine("            byte[] inputBytes;");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                if (StrictUtf8.GetByteCount(stdin) > MaximumInputBytes)");
            _source.AppendLine("                    return Error(new ProcessError.InputTooLarge(), cancellationToken);");
            _source.AppendLine("                inputBytes = StrictUtf8.GetBytes(stdin);");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.Text.EncoderFallbackException)");
            _source.AppendLine("            {");
            _source.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                return Error(new ProcessError.InvalidText(), cancellationToken);");
            _source.AppendLine("            }");
            _source.AppendLine();
            _source.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("            var startInfo = new global::System.Diagnostics.ProcessStartInfo");
            _source.AppendLine("            {");
            _source.AppendLine("                FileName = _executablePath,");
            _source.AppendLine("                WorkingDirectory = _artifactDirectory,");
            _source.AppendLine("                UseShellExecute = false,");
            _source.AppendLine("                RedirectStandardInput = true,");
            _source.AppendLine("                RedirectStandardOutput = true,");
            _source.AppendLine("                RedirectStandardError = true,");
            _source.AppendLine("                CreateNoWindow = true");
            _source.AppendLine("            };");
            _source.AppendLine("            startInfo.Environment.Clear();");
            _source.AppendLine("            foreach (var argument in arguments)");
            _source.AppendLine("                startInfo.ArgumentList.Add(argument);");
            _source.AppendLine();
            _source.AppendLine("            using var process = new global::System.Diagnostics.Process { StartInfo = startInfo };");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                if (!string.Equals(ComputeSha256(_executablePath), _expectedSha256, global::System.StringComparison.Ordinal))");
            _source.AppendLine("                {");
            _source.AppendLine("                    cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                    return Error(new ProcessError.StartFailed(), cancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
            _source.AppendLine("            {");
            _source.AppendLine("                throw;");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.Exception)");
            _source.AppendLine("            {");
            _source.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                return Error(new ProcessError.StartFailed(), cancellationToken);");
            _source.AppendLine("            }");
            _source.AppendLine();
            _source.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                if (!process.Start())");
            _source.AppendLine("                {");
            _source.AppendLine("                    cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                    return Error(new ProcessError.StartFailed(), cancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.Exception)");
            _source.AppendLine("            {");
            _source.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                return Error(new ProcessError.StartFailed(), cancellationToken);");
            _source.AppendLine("            }");
            _source.AppendLine();
            _source.AppendLine("            global::System.Threading.Tasks.Task<CapturedOutput>? stdoutTask = null;");
            _source.AppendLine("            global::System.Threading.Tasks.Task<CapturedOutput>? stderrTask = null;");
            _source.AppendLine("            global::System.Threading.Tasks.Task? stdinTask = null;");
            _source.AppendLine("            global::System.Threading.Tasks.Task? exitTask = null;");
            _source.AppendLine("            global::System.Threading.Tasks.Task? completionTask = null;");
            _source.AppendLine("            var outputOverflow = new global::System.Threading.Tasks.TaskCompletionSource<bool>(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);");
            _source.AppendLine("            var processCleanupStarted = false;");
            _source.AppendLine("            async global::System.Threading.Tasks.Task StopProcessAsync()");
            _source.AppendLine("            {");
            _source.AppendLine("                if (processCleanupStarted) return;");
            _source.AppendLine("                processCleanupStarted = true;");
            _source.AppendLine("                await StopAndReapAsync(process);");
            _source.AppendLine("            }");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                stdoutTask = ReadBoundedAsync(process.StandardOutput.BaseStream, outputOverflow);");
            _source.AppendLine("                stderrTask = ReadBoundedAsync(process.StandardError.BaseStream, outputOverflow);");
            _source.AppendLine("                stdinTask = WriteInputAndCloseAsync(process, inputBytes);");
            _source.AppendLine("                exitTask = process.WaitForExitAsync();");
            _source.AppendLine("                completionTask = global::System.Threading.Tasks.Task.WhenAll(stdinTask, stdoutTask, stderrTask, exitTask);");
            _source.AppendLine("                var hostCancellation = new global::System.Threading.Tasks.TaskCompletionSource<bool>(global::System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);");
            _source.AppendLine("                using var cancellationRegistration = cancellationToken.Register(static state =>");
            _source.AppendLine("                    ((global::System.Threading.Tasks.TaskCompletionSource<bool>)state!).TrySetResult(true), hostCancellation);");
            _source.AppendLine("                var timeoutTask = global::System.Threading.Tasks.Task.Delay(global::System.TimeSpan.FromSeconds(10));");
            _source.AppendLine("                await global::System.Threading.Tasks.Task.WhenAny(completionTask, outputOverflow.Task, timeoutTask, hostCancellation.Task);");
            _source.AppendLine();
            _source.AppendLine("                if (cancellationToken.IsCancellationRequested)");
            _source.AppendLine("                {");
            _source.AppendLine("                    await StopProcessAsync();");
            _source.AppendLine("                    throw new global::System.OperationCanceledException(cancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine("                if (outputOverflow.Task.IsCompleted)");
            _source.AppendLine("                {");
            _source.AppendLine("                    await StopProcessAsync();");
            _source.AppendLine("                    cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                    return Error(new ProcessError.OutputTooLarge(), cancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine("                if (!completionTask.IsCompleted && timeoutTask.IsCompleted)");
            _source.AppendLine("                {");
            _source.AppendLine("                    await StopProcessAsync();");
            _source.AppendLine("                    cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                    return Error(new ProcessError.TimedOut(), cancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine();
            _source.AppendLine("                try");
            _source.AppendLine("                {");
            _source.AppendLine("                    await completionTask;");
            _source.AppendLine("                }");
            _source.AppendLine("                catch (global::System.Exception)");
            _source.AppendLine("                {");
            _source.AppendLine("                    await StopProcessAsync();");
            _source.AppendLine("                    cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                    return Error(new ProcessError.StartFailed(), cancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine();
            _source.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                string stdout;");
            _source.AppendLine("                string stderr;");
            _source.AppendLine("                try");
            _source.AppendLine("                {");
            _source.AppendLine("                    stdout = StrictUtf8.GetString(stdoutTask.Result.Bytes);");
            _source.AppendLine("                    stderr = StrictUtf8.GetString(stderrTask.Result.Bytes);");
            _source.AppendLine("                }");
            _source.AppendLine("                catch (global::System.Text.DecoderFallbackException)");
            _source.AppendLine("                {");
            _source.AppendLine("                    cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                    return Error(new ProcessError.InvalidText(), cancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine();
            _source.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                return new Result<ProcessOutput, ProcessError>.Ok(new ProcessOutput(process.ExitCode, stdout, stderr));");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.OperationCanceledException) when (cancellationToken.IsCancellationRequested)");
            _source.AppendLine("            {");
            _source.AppendLine("                await StopProcessAsync();");
            _source.AppendLine("                throw;");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.Exception)");
            _source.AppendLine("            {");
            _source.AppendLine("                await StopProcessAsync();");
            _source.AppendLine("                cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                return Error(new ProcessError.StartFailed(), cancellationToken);");
            _source.AppendLine("            }");
            _source.AppendLine("            finally");
            _source.AppendLine("            {");
            _source.AppendLine("                ObserveTask(stdinTask);");
            _source.AppendLine("                ObserveTask(stdoutTask);");
            _source.AppendLine("                ObserveTask(stderrTask);");
            _source.AppendLine("                ObserveTask(exitTask);");
            _source.AppendLine("                ObserveTask(completionTask);");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine();
            _source.AppendLine("        private sealed record CapturedOutput(byte[] Bytes);");
            _source.AppendLine();
            _source.AppendLine("        private static Result<ProcessOutput, ProcessError> Error(");
            _source.AppendLine("            ProcessError error,");
            _source.AppendLine("            global::System.Threading.CancellationToken cancellationToken)");
            _source.AppendLine("        {");
            _source.AppendLine("            cancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("            return new Result<ProcessOutput, ProcessError>.Err(error);");
            _source.AppendLine("        }");
            _source.AppendLine();
            _source.AppendLine("        private static string ComputeSha256(string path)");
            _source.AppendLine("        {");
            _source.AppendLine("            using var stream = global::System.IO.File.OpenRead(path);");
            _source.AppendLine("            return global::System.Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();");
            _source.AppendLine("        }");
            _source.AppendLine();
            _source.AppendLine("        private static async global::System.Threading.Tasks.Task<CapturedOutput> ReadBoundedAsync(");
            _source.AppendLine("            global::System.IO.Stream stream,");
            _source.AppendLine("            global::System.Threading.Tasks.TaskCompletionSource<bool> overflowSignal)");
            _source.AppendLine("        {");
            _source.AppendLine("            using var captured = new global::System.IO.MemoryStream();");
            _source.AppendLine("            var buffer = new byte[8192];");
            _source.AppendLine("            while (true)");
            _source.AppendLine("            {");
            _source.AppendLine("                var count = await stream.ReadAsync(buffer.AsMemory());");
            _source.AppendLine("                if (count == 0)");
            _source.AppendLine("                    return new CapturedOutput(captured.ToArray());");
            _source.AppendLine("                if (captured.Length + count > MaximumOutputBytes)");
            _source.AppendLine("                {");
            _source.AppendLine("                    overflowSignal.TrySetResult(true);");
            _source.AppendLine("                    return new CapturedOutput(global::System.Array.Empty<byte>());");
            _source.AppendLine("                }");
            _source.AppendLine("                captured.Write(buffer, 0, count);");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine();
            _source.AppendLine("        private static async global::System.Threading.Tasks.Task WriteInputAndCloseAsync(");
            _source.AppendLine("            global::System.Diagnostics.Process process,");
            _source.AppendLine("            byte[] inputBytes)");
            _source.AppendLine("        {");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                await process.StandardInput.BaseStream.WriteAsync(inputBytes.AsMemory());");
            _source.AppendLine("                await process.StandardInput.BaseStream.FlushAsync();");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.IO.IOException)");
            _source.AppendLine("            {");
            _source.AppendLine("                // A child may close stdin before consuming all input.");
            _source.AppendLine("            }");
            _source.AppendLine("            finally");
            _source.AppendLine("            {");
            _source.AppendLine("                try { process.StandardInput.Close(); }");
            _source.AppendLine("                catch (global::System.Exception) { }");
            _source.AppendLine("            }");
            _source.AppendLine("        }");
            _source.AppendLine();
            _source.AppendLine("        private static async global::System.Threading.Tasks.Task StopAndReapAsync(");
            _source.AppendLine("            global::System.Diagnostics.Process process)");
            _source.AppendLine("        {");
            _source.AppendLine("            try { process.StandardInput.Close(); }");
            _source.AppendLine("            catch (global::System.Exception) { }");
            _source.AppendLine("            try { process.Kill(entireProcessTree: true); }");
            _source.AppendLine("            catch (global::System.Exception) { }");
            _source.AppendLine("            try");
            _source.AppendLine("            {");
            _source.AppendLine("                await process.WaitForExitAsync().WaitAsync(global::System.TimeSpan.FromSeconds(2));");
            _source.AppendLine("            }");
            _source.AppendLine("            catch (global::System.Exception) { }");
            _source.AppendLine("        }");
            _source.AppendLine();
            _source.AppendLine("        private static void ObserveTask(global::System.Threading.Tasks.Task? task)");
            _source.AppendLine("        {");
            _source.AppendLine("            if (task is null) return;");
            _source.AppendLine("            _ = task.ContinueWith(");
            _source.AppendLine("                static completed => { _ = completed.Exception; },");
            _source.AppendLine("                global::System.Threading.CancellationToken.None,");
            _source.AppendLine("                global::System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted | global::System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously,");
            _source.AppendLine("                global::System.Threading.Tasks.TaskScheduler.Default);");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
        }

        private void EmitUnion(CheckedUnion union)
        {
            _emittingUnion = union;
            var accessibility = union.Public ? "public" : "private";
            _source.Append("    ").Append(accessibility).Append(" abstract record Union_").Append(union.Id);
            if (union.TypeParameters.Count != 0)
                _source.Append('<').Append(string.Join(", ", union.TypeParameters.Select((_, index) => TypeParameterName(index)))).Append('>');
            _source.AppendLine();
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
                _source.Append(") : Union_").Append(union.Id);
                if (union.TypeParameters.Count != 0)
                    _source.Append('<').Append(string.Join(", ", union.TypeParameters.Select((_, index) => TypeParameterName(index)))).Append('>');
                _source.AppendLine(";");
            }
            _source.AppendLine("    }");
            _source.AppendLine();
            _emittingUnion = null;
        }

        private void EmitStruct(CheckedStruct structure)
        {
            _emittingStruct = structure;
            _source.Append("    ").Append(structure.Public ? "public" : "private")
                .Append(" sealed record Struct_").Append(structure.Id.ToString(CultureInfo.InvariantCulture));
            if (structure.TypeParameters.Count != 0)
                _source.Append('<').Append(string.Join(", ", structure.TypeParameters.Select((_, index) => TypeParameterName(index)))).Append('>');
            _source.Append('(');
            for (var i = 0; i < structure.Fields.Count; i++)
            {
                if (i != 0) _source.Append(", ");
                var field = structure.Fields[i];
                _source.Append(EmitType(field.Type)).Append(" Field_")
                    .Append(field.Index.ToString(CultureInfo.InvariantCulture));
            }
            _source.AppendLine(");");
            _source.AppendLine();
            _emittingStruct = null;
        }

        private void EmitNewtype(CheckedNewtype newtype)
        {
            _source.Append("    ").Append(newtype.Public ? "public" : "private")
                .Append(" sealed record Newtype_").Append(newtype.Id.ToString(CultureInfo.InvariantCulture))
                .Append('(').Append(EmitType(newtype.Representation)).AppendLine(" Value);");
            _source.AppendLine();
        }

        private void EmitTrait(CheckedTrait trait)
        {
            _emittingTrait = trait;
            _emittingTraitSelfTarget = null;
            _source.Append("    ").Append(trait.Public ? "public" : "private")
                .Append(" interface Trait_").Append(trait.Id.ToString(CultureInfo.InvariantCulture)).AppendLine("<TSelf>");
            _source.AppendLine("    {");
            foreach (var method in trait.Methods)
            {
                _source.Append("        static abstract ").Append(EmitType(method.ReturnType)).Append(" Method_")
                    .Append(method.Id.ToString(CultureInfo.InvariantCulture)).Append('(');
                for (var index = 0; index < method.Parameters.Count; index++)
                {
                    if (index != 0) _source.Append(", ");
                    _source.Append(EmitType(method.Parameters[index].Type)).Append(" Local_")
                        .Append(method.Parameters[index].LocalId.ToString(CultureInfo.InvariantCulture));
                }
                _source.AppendLine(");");
            }
            _source.AppendLine("    }");
            _source.AppendLine();
            _emittingTrait = null;
        }

        private void EmitTraitImpl(CheckedTraitImpl implementation)
        {
            var trait = program.Traits.Single(item => item.Id == implementation.TraitId);
            _emittingTrait = trait;
            _emittingTraitSelfTarget = implementation.Target;
            _source.Append("    private readonly struct Impl_").Append(implementation.Id.ToString(CultureInfo.InvariantCulture))
                .Append(" : Trait_").Append(trait.Id.ToString(CultureInfo.InvariantCulture)).Append('<')
                .Append(EmitType(implementation.Target)).AppendLine(">");
            _source.AppendLine("    {");
            foreach (var method in trait.Methods)
            {
                var binding = program.Functions.Single(function => function.Id == implementation.BindingFunctionIds[method.Id]);
                _source.Append("        public static ").Append(EmitType(method.ReturnType)).Append(" Method_")
                    .Append(method.Id.ToString(CultureInfo.InvariantCulture)).Append('(');
                for (var index = 0; index < method.Parameters.Count; index++)
                {
                    if (index != 0) _source.Append(", ");
                    _source.Append(EmitType(method.Parameters[index].Type)).Append(" Local_")
                        .Append(method.Parameters[index].LocalId.ToString(CultureInfo.InvariantCulture));
                }
                var arguments = method.Parameters.Select(parameter =>
                    "Local_" + parameter.LocalId.ToString(CultureInfo.InvariantCulture)).ToArray();
                _source.Append(") => ").Append(binding.AdapterBinding is null
                    ? "Function_" + binding.Id.ToString(CultureInfo.InvariantCulture) + "(" + string.Join(", ", arguments) + ")"
                    : EmitManagedAdapterCall(binding.AdapterBinding, arguments)).AppendLine(";");
            }
            _source.AppendLine("    }");
            _source.AppendLine();
            _emittingTraitSelfTarget = null;
            _emittingTrait = null;
        }

        private void EmitFunction(CheckedFunction function)
        {
            if (function.AdapterBinding is not null)
                return;

            _emittingFunction = function;
            var propagatesResult = FunctionUsesResultPropagation(function);
            if (propagatesResult && function.ReturnType.Kind != LangTypeKind.Result)
                throw new InvalidOperationException("A checked propagation function must return Result<T, E>");
            _source.Append("    ").Append(function.Public ? "public" : "private").Append(" static ")
                .Append(function.IsAsync ? "async Task<" + EmitType(function.ReturnType) + ">" : EmitType(function.ReturnType))
                .Append(" Function_").Append(function.Id);
            var genericParameterNames = function.TypeParameters
                .Select((_, index) => TypeParameterName(index))
                .Concat(function.TypeParameterBounds
                    .SelectMany((bounds, parameter) => bounds.Select((_, bound) => TraitWitnessTypeParameterName(parameter, bound))))
                .ToArray();
            if (genericParameterNames.Length != 0)
            {
                _source.Append('<').Append(string.Join(", ", genericParameterNames)).Append('>');
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
            for (var parameter = 0; parameter < function.TypeParameterBounds.Count; parameter++)
                for (var bound = 0; bound < function.TypeParameterBounds[parameter].Count; bound++)
                {
                    var traitId = function.TypeParameterBounds[parameter][bound].TraitId;
                    _source.Append("        where ").Append(TraitWitnessTypeParameterName(parameter, bound))
                        .Append(" : struct, Trait_").Append(traitId.ToString(CultureInfo.InvariantCulture))
                        .Append('<').Append(TypeParameterName(parameter)).AppendLine(">");
                }
            _source.AppendLine("    {");
            if (propagatesResult)
            {
                _source.AppendLine("        var resultPropagationActivation = new object();");
                _source.AppendLine("        try");
                _source.AppendLine("        {");
                if (function.IsAsync)
                    _source.AppendLine("            await Task.CompletedTask;");
                EmitStatements(function.Body, 3);
                _source.AppendLine("        }");
                _source.Append("        catch (ResultPropagationSignal<")
                    .Append(EmitType(function.ReturnType.Arguments[1]))
                    .AppendLine("> signal) when (global::System.Object.ReferenceEquals(signal.Activation, resultPropagationActivation))");
                _source.AppendLine("        {");
                _source.Append("            return new ").Append(EmitType(function.ReturnType)).AppendLine(".Err(signal.Error);");
                _source.AppendLine("        }");
            }
            else
            {
                if (function.IsAsync)
                    _source.AppendLine("        await Task.CompletedTask;");
                EmitStatements(function.Body, 2);
            }
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
            TypedNumberExpr number => EmitNumericLiteral(number),
            TypedUnitExpr => "default(global::System.ValueTuple)",
            TypedBoolExpr boolean => boolean.Value ? "true" : "false",
            TypedTextExpr text => JsonSerializer.Serialize(text.Value),
            TypedListExpr list => EmitList(list),
            TypedMapEmptyExpr map => EmitMapEmpty(map),
            TypedMapSetExpr map => "(" + EmitExpr(map.Target) + ").SetItem(" + EmitExpr(map.Key) + ", " + EmitExpr(map.Value) + ")",
            TypedMapGetExpr map => "MapGet(" + EmitExpr(map.Target) + ", " + EmitExpr(map.Key) + ")",
            TypedMapKeysExpr map => "global::System.Collections.Immutable.ImmutableArray.CreateRange<string>((" + EmitExpr(map.Target) + ").Keys)",
            TypedMapLengthExpr map => "(" + EmitExpr(map.Target) + ").Count",
            TypedLocalExpr local => "Local_" + local.LocalId.ToString(CultureInfo.InvariantCulture),
            TypedLambdaInvokeExpr lambda => EmitLambdaInvoke(lambda),
            TypedBinaryExpr binary => EmitBinary(binary),
            TypedUnaryExpr unary => EmitUnary(unary),
            TypedCompareExpr comparison => EmitComparison(comparison),
            TypedCallExpr { IsAsync: true } => throw new InvalidOperationException("Async calls must be emitted beneath a checked await expression"),
            TypedCallExpr call => EmitCall(call),
            TypedTraitCallExpr call => EmitTraitCall(call),
            TypedDatabaseCallExpr databaseCall => EmitDatabaseCall(databaseCall),
            TypedTransactionCommitExpr commit =>
                "DatabaseTransactionCommit(Local_" + commit.TransactionLocalId.ToString(CultureInfo.InvariantCulture) + ")",
            TypedTextLengthExpr length => "TextLength(" + EmitExpr(length.Target) + ")",
            TypedTextTrimExpr trim => "(" + EmitExpr(trim.Target) + ").Trim()",
            TypedListLengthExpr length => "(" + EmitExpr(length.Target) + ").Length",
            TypedListGetExpr get => "ListGet(" + EmitExpr(get.Target) + ", " + EmitExpr(get.Index) + ")",
            TypedListAppendExpr append => "(" + EmitExpr(append.Target) + ").Add(" + EmitExpr(append.Value) + ")",
            TypedBytesEmptyExpr => "Bytes.Empty()",
            TypedBytesLengthExpr length => "(" + EmitExpr(length.Target) + ").Length",
            TypedBytesGetExpr get => "(" + EmitExpr(get.Target) + ").Get(" + EmitExpr(get.Index) + ")",
            TypedBytesAppendExpr append => "Bytes.Append(" + EmitExpr(append.Target) + ", " + EmitExpr(append.Octet) + ")",
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.FsReadTextAsync or BuiltinIntrinsic.FsWriteTextAsync or
                BuiltinIntrinsic.HttpGetTextAsync or BuiltinIntrinsic.ProcessRunTextAsync } => throw new InvalidOperationException("Async intrinsics must be emitted beneath a checked await expression"),
            TypedIntrinsicCallExpr intrinsic => EmitIntrinsicCall(intrinsic),
            TypedAwaitExpr awaited => EmitAwait(awaited),
            TypedResultPropagateExpr propagated => EmitResultPropagate(propagated),
            TypedBuiltinConstructExpr builtin => EmitBuiltinConstruct(builtin),
            TypedUnionConstructExpr variant => EmitUnionConstruct(variant),
            TypedStructConstructExpr structure => EmitStructConstruct(structure),
            TypedNewtypeConstructExpr newtype =>
                "new Newtype_" + newtype.NewtypeId.ToString(CultureInfo.InvariantCulture) + "(" + EmitExpr(newtype.Value) + ")",
            TypedNewtypeProjectExpr newtype => "(" + EmitExpr(newtype.Target) + ").Value",
            TypedFieldAccessExpr field => "(" + EmitExpr(field.Target) + ").Field_" +
                field.FieldIndex.ToString(CultureInfo.InvariantCulture),
            TypedMatchExpr match => EmitMatch(match),
            _ => throw new InvalidOperationException("Unchecked expression reached emitter")
        };

        private static string EmitNumericLiteral(TypedNumberExpr number)
        {
            if (number.Type.IsF64)
                return "global::System.BitConverter.Int64BitsToDouble(unchecked((long)0x" + number.Value + "UL))";
            if (number.Type.IsI32)
                return number.Value == int.MinValue.ToString(CultureInfo.InvariantCulture)
                    ? "int.MinValue"
                    : number.Value;
            if (number.Type.IsI64)
                return number.Value == long.MinValue.ToString(CultureInfo.InvariantCulture)
                    ? "long.MinValue"
                    : number.Value + "L";
            if (number.Type.IsU32)
                return number.Value + "U";
            if (number.Type.IsU64)
                return number.Value + "UL";
            throw new InvalidOperationException("Unknown checked numeric literal type");
        }

        private string EmitUnary(TypedUnaryExpr expression) => expression.Op switch
        {
            "-" when expression.Type.IsF64 => "(-" + EmitExpr(expression.Operand) + ")",
            _ => throw new InvalidOperationException("Unknown checked unary operator")
        };

        private string EmitLambdaInvoke(TypedLambdaInvokeExpr expression)
        {
            var parameter = "Local_" + expression.ParameterLocalId.ToString(CultureInfo.InvariantCulture);
            return "((global::System.Func<" + EmitType(expression.ParameterType) + ", " + EmitType(expression.Type) + ">)(" +
                parameter + " => " + EmitExpr(expression.Body) + "))(" + EmitExpr(expression.Argument) + ")";
        }

        private string EmitList(TypedListExpr expression)
        {
            var itemType = EmitType(expression.Type.Arguments[0]);
            const string immutableArray = "global::System.Collections.Immutable.ImmutableArray";
            if (expression.Items.Count == 0)
                return immutableArray + "<" + itemType + ">.Empty";
            return immutableArray + ".Create<" + itemType + ">(" +
                string.Join(", ", expression.Items.Select(EmitExpr)) + ")";
        }

        private string EmitMapEmpty(TypedMapEmptyExpr expression)
        {
            var keyType = EmitType(expression.Type.Arguments[0]);
            var valueType = EmitType(expression.Type.Arguments[1]);
            if (keyType != "string")
                throw new InvalidOperationException("Checked map keys must lower to Text");
            return "global::System.Collections.Immutable.ImmutableSortedDictionary<string, " + valueType +
                ">.Empty.WithComparers(global::System.StringComparer.Ordinal)";
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
            var targetFunction = program.Functions.Single(function => function.Id == call.FunctionId);
            if (targetFunction.AdapterBinding is { } adapterBinding)
                return EmitManagedAdapterCall(adapterBinding, call.Arguments);

            var functionName = "Function_" + call.FunctionId.ToString(CultureInfo.InvariantCulture);
            var typeArguments = call.TypeArguments.Select(EmitType)
                .Concat(call.TraitWitnesses.Select(EmitTraitWitnessType)).ToArray();
            if (typeArguments.Length != 0)
                functionName += "<" + string.Join(", ", typeArguments) + ">";
            var arguments = call.Arguments.Select(EmitExpr).ToList();
            if (call.IsAsync)
            {
                if (_emittingFunction?.IsAsync != true)
                    throw new InvalidOperationException("An async call has no enclosing async function token");
                arguments.Add("cancellationToken");
            }
            return functionName + "(" + string.Join(", ", arguments) + ")";
        }

        private string EmitTraitCall(TypedTraitCallExpr call)
        {
            var witnessType = EmitTraitWitnessType(call.Witness);
            return witnessType + ".Method_" + call.MethodId.ToString(CultureInfo.InvariantCulture) +
                "(" + string.Join(", ", call.Arguments.Select(EmitExpr)) + ")";
        }

        private static string EmitTraitWitnessType(TypedTraitWitness witness) => witness switch
        {
            TypedConcreteTraitWitness concrete => "Impl_" + concrete.ImplId.ToString(CultureInfo.InvariantCulture),
            TypedForwardedTraitWitness forwarded => TraitWitnessTypeParameterName(forwarded.TypeParameterOrdinal, forwarded.BoundOrdinal),
            _ => throw new InvalidOperationException("Unknown checked trait witness")
        };

        private static string TraitWitnessTypeParameterName(int typeParameterOrdinal, int boundOrdinal) =>
            "W" + typeParameterOrdinal.ToString(CultureInfo.InvariantCulture) + "_" + boundOrdinal.ToString(CultureInfo.InvariantCulture);

        private string EmitManagedAdapterCall(
            CheckedManagedAdapterBinding binding,
            IReadOnlyList<TypedExpr> arguments) =>
            binding.OperationId switch
            {
                "sha256.text.hash_utf8" when arguments.Count == 1 =>
                    "global::Lang.ManagedAdapters.Sha256Text.HashUtf8(" + EmitExpr(arguments[0]) + ")",
                "sha256.text.hash_utf8" =>
                    throw new InvalidOperationException("sha256.text.hash_utf8 requires one checked argument"),
                _ => throw new InvalidOperationException($"Unknown checked managed adapter operation '{binding.OperationId}'")
            };

        private static string EmitManagedAdapterCall(
            CheckedManagedAdapterBinding binding,
            IReadOnlyList<string> arguments) =>
            binding.OperationId switch
            {
                "sha256.text.hash_utf8" when arguments.Count == 1 =>
                    "global::Lang.ManagedAdapters.Sha256Text.HashUtf8(" + arguments[0] + ")",
                "sha256.text.hash_utf8" =>
                    throw new InvalidOperationException("sha256.text.hash_utf8 requires one checked argument"),
                _ => throw new InvalidOperationException($"Unknown checked managed adapter operation '{binding.OperationId}'")
            };

        private string EmitAwait(TypedAwaitExpr expression) => expression.Value switch
        {
            TypedCallExpr { IsAsync: true } call => "await " + EmitCall(call),
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.FsReadTextAsync } intrinsic =>
                "await " + EmitIntrinsicCall(intrinsic),
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.FsWriteTextAsync } intrinsic =>
                "await " + EmitIntrinsicCall(intrinsic),
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.HttpGetTextAsync } intrinsic =>
                "await " + EmitIntrinsicCall(intrinsic),
            TypedIntrinsicCallExpr { Intrinsic: BuiltinIntrinsic.ProcessRunTextAsync } intrinsic =>
                "await " + EmitIntrinsicCall(intrinsic),
            _ => throw new InvalidOperationException("Await expression has no checked async target")
        };

        private string EmitResultPropagate(TypedResultPropagateExpr expression)
        {
            if (_emittingFunction is null)
                throw new InvalidOperationException("Result propagation must be emitted inside a source function");
            return "PropagateResult<" + EmitType(expression.OkType) + ", " + EmitType(expression.ErrorType) + ">(" +
                EmitExpr(expression.Operand) + ", resultPropagationActivation)";
        }

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
            BuiltinIntrinsic.HttpGetTextAsync when expression.Arguments.Count == 2 =>
                EmitHttpGetTextAsync(expression),
            BuiltinIntrinsic.HttpGetTextAsync =>
                throw new InvalidOperationException("HttpClient.get_text_async requires a receiver and a target"),
            BuiltinIntrinsic.ProcessRunTextAsync when expression.Arguments.Count == 3 =>
                EmitProcessRunTextAsync(expression),
            BuiltinIntrinsic.ProcessRunTextAsync =>
                throw new InvalidOperationException("ProcessRunner.run_text_async requires a receiver, arguments, and stdin"),
            BuiltinIntrinsic.FsWriteText when expression.Arguments.Count == 3 =>
                EmitFsWriteText(expression),
            BuiltinIntrinsic.FsWriteText =>
                throw new InvalidOperationException("FsWrite.write_text requires a receiver, path, and value"),
            BuiltinIntrinsic.FsWriteTextAsync when expression.Arguments.Count == 3 =>
                EmitFsWriteTextAsync(expression),
            BuiltinIntrinsic.FsWriteTextAsync =>
                throw new InvalidOperationException("FsWrite.write_text_async requires a receiver, path, and value"),
            BuiltinIntrinsic.ConfigGetText => EmitConfigRead(expression, ConfigFieldKind.Text),
            BuiltinIntrinsic.ConfigGetSecretText => EmitConfigRead(expression, ConfigFieldKind.SecretText),
            BuiltinIntrinsic.SecretsRevealText when expression.Arguments.Count == 2 =>
                "(" + EmitExpr(expression.Arguments[0]) + ").RevealText(" + EmitExpr(expression.Arguments[1]) + ")",
            BuiltinIntrinsic.SecretsRevealText =>
                throw new InvalidOperationException("Secrets.reveal_text requires a receiver and a Secret<Text> value"),
            BuiltinIntrinsic.LoggerInfo when expression.Arguments.Count == 3 =>
                "(" + EmitExpr(expression.Arguments[0]) + ").Info(" + EmitExpr(expression.Arguments[1]) + ", " +
                EmitExpr(expression.Arguments[2]) + ")",
            BuiltinIntrinsic.LoggerInfo =>
                throw new InvalidOperationException("Logger.info requires a receiver, event, and detail"),
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

        private string EmitConfigRead(TypedIntrinsicCallExpr expression, ConfigFieldKind expectedKind)
        {
            if (expression.Arguments.Count != 2 || expression.Arguments[1] is not TypedTextExpr fieldName)
                throw new InvalidOperationException("Config lookup must retain its checked literal field name");

            var fieldIndex = -1;
            for (var index = 0; index < program.ConfigFields.Count; index++)
            {
                var field = program.ConfigFields[index];
                if (!string.Equals(field.Name, fieldName.Value, StringComparison.Ordinal)) continue;
                if (field.Kind != expectedKind)
                    throw new InvalidOperationException("Checked config lookup field kind does not match its runtime accessor");
                fieldIndex = index;
                break;
            }

            if (fieldIndex < 0)
                throw new InvalidOperationException("Checked config lookup refers to an unknown field");

            var suffix = fieldIndex.ToString(CultureInfo.InvariantCulture);
            var method = expectedKind == ConfigFieldKind.SecretText ? "ReadSecretText_" : "ReadText_";
            return "(" + EmitExpr(expression.Arguments[0]) + ")." + method + suffix + "()";
        }

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

        private string EmitFsWriteTextAsync(TypedIntrinsicCallExpr expression)
        {
            var receiver = EmitExpr(expression.Arguments[0]);
            var path = expression.Arguments[1];
            var emittedPath = path.Type.IsFilePath
                ? "(" + EmitExpr(path) + ").Value"
                : EmitExpr(path);
            return "WriteTextAsync(" + receiver + ", " + emittedPath + ", " + EmitExpr(expression.Arguments[2]) + ")";
        }

        private string EmitHttpGetTextAsync(TypedIntrinsicCallExpr expression) =>
            "HttpGetTextAsync(" + EmitExpr(expression.Arguments[0]) + ", " + EmitExpr(expression.Arguments[1]) + ")";

        private string EmitProcessRunTextAsync(TypedIntrinsicCallExpr expression)
        {
            if (_emittingFunction?.IsAsync != true)
                throw new InvalidOperationException("ProcessRunner.run_text_async has no enclosing async function token");

            return "(" + EmitExpr(expression.Arguments[0]) + ").RunTextAsync(new global::System.Collections.Generic.List<string>(" +
                EmitExpr(expression.Arguments[1]) + "), " + EmitExpr(expression.Arguments[2]) + ", cancellationToken)";
        }

        private string EmitBinary(TypedBinaryExpr expression)
        {
            if (expression.Type.IsF64)
                return "(" + EmitExpr(expression.Left) + " " + expression.Op + " " + EmitExpr(expression.Right) + ")";

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
            if (expression.Op is "==" or "!=")
            {
                var equality = EmitStructuralEquality(expression.Left.Type, left, right);
                return expression.Op == "==" ? equality : "!(" + equality + ")";
            }

            if (expression.Left.Type.IsText)
            {
                throw new InvalidOperationException("Text ordering is not supported");
            }

            return expression.Op switch
            {
                "<" or "<=" or ">" or ">=" =>
                    "(" + left + " " + expression.Op + " " + right + ")",
                _ => throw new InvalidOperationException("Unknown comparison operator")
            };
        }

        private void EmitStructuralEqualityHelpers()
        {
            _source.AppendLine("    private static bool UnitEquals(global::System.ValueTuple left, global::System.ValueTuple right) => true;");
            _source.AppendLine();
            _source.AppendLine("    private static bool SequenceStructuralEquals<T>(global::System.Collections.Immutable.ImmutableArray<T> left, global::System.Collections.Immutable.ImmutableArray<T> right, global::System.Func<T, T, bool> equals)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (left.Length != right.Length) return false;");
            _source.AppendLine("        for (var index = 0; index < left.Length; index++)");
            _source.AppendLine("            if (!equals(left[index], right[index])) return false;");
            _source.AppendLine("        return true;");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    private static bool MapStructuralEquals<T>(global::System.Collections.Immutable.ImmutableSortedDictionary<string, T> left, global::System.Collections.Immutable.ImmutableSortedDictionary<string, T> right, global::System.Func<T, T, bool> equals)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (left.Count != right.Count) return false;");
            _source.AppendLine("        foreach (var entry in left)");
            _source.AppendLine("            if (!right.TryGetValue(entry.Key, out var value) || !equals(entry.Value, value)) return false;");
            _source.AppendLine("        return true;");
            _source.AppendLine("    }");
            _source.AppendLine();

            foreach (var structure in program.Structs.Where(structure => CanEmitStructuralEquality(structure.Type)))
            {
                _emittingStruct = structure;
                var equalityParameterOrdinals = EqualityParameterOrdinals(LangTypeKind.Struct, structure.Id);
                RegisterEqualityParameterNames(structure.TypeParameters, equalityParameterOrdinals);
                var comparison = structure.Fields.Count == 0
                    ? "true"
                    : string.Join(" && ", structure.Fields.Select(field =>
                        EmitStructuralEquality(field.Type,
                            "left.Field_" + field.Index.ToString(CultureInfo.InvariantCulture),
                            "right.Field_" + field.Index.ToString(CultureInfo.InvariantCulture))));
                EmitNominalEqualitySignature(
                    "Struct",
                    structure.Id,
                    structure.TypeParameters,
                    structure.Type,
                    equalityParameterOrdinals,
                    comparison);
                _nominalTypeParameterEqualityNames.Clear();
                _emittingStruct = null;
            }

            foreach (var newtype in program.Newtypes.Where(newtype => CanEmitStructuralEquality(newtype.Type)))
            {
                _source.Append("    private static bool StructuralEqualsNewtype_")
                    .Append(newtype.Id.ToString(CultureInfo.InvariantCulture))
                    .Append("(Newtype_").Append(newtype.Id.ToString(CultureInfo.InvariantCulture))
                    .Append(" left, Newtype_").Append(newtype.Id.ToString(CultureInfo.InvariantCulture))
                    .Append(" right) => ")
                    .Append(EmitStructuralEquality(newtype.Representation, "left.Value", "right.Value"))
                    .AppendLine(";");
            }

            foreach (var union in program.Unions.Where(union => CanEmitStructuralEquality(union.Type)))
            {
                _emittingUnion = union;
                var equalityParameterOrdinals = EqualityParameterOrdinals(LangTypeKind.Union, union.Id);
                RegisterEqualityParameterNames(union.TypeParameters, equalityParameterOrdinals);
                var cases = new List<string>();
                foreach (var variant in union.Variants)
                {
                    var suffix = union.Id.ToString(CultureInfo.InvariantCulture) + "_" + variant.Id.ToString(CultureInfo.InvariantCulture);
                    var leftName = "leftVariant_" + suffix;
                    var rightName = "rightVariant_" + suffix;
                    var variantType = EmitType(union.Type) + ".Variant_" + suffix;
                    var payloadEquality = variant.Fields.Count == 0
                        ? "true"
                        : string.Join(" && ", variant.Fields.Select(field =>
                            EmitStructuralEquality(field.Type,
                                leftName + ".Payload_" + field.Index.ToString(CultureInfo.InvariantCulture),
                                rightName + ".Payload_" + field.Index.ToString(CultureInfo.InvariantCulture))));
                    cases.Add("(" + variantType + " " + leftName + ", " + variantType + " " + rightName + ") => " + payloadEquality);
                }
                cases.Add("_ => false");
                EmitNominalEqualitySignature(
                    "Union",
                    union.Id,
                    union.TypeParameters,
                    union.Type,
                    equalityParameterOrdinals,
                    "(left, right) switch { " + string.Join(", ", cases) + " }");
                _nominalTypeParameterEqualityNames.Clear();
                _emittingUnion = null;
            }

            EmitEmptyUnionStructuralEquality(LangTypeKind.FsError, "FsError",
                ["NotFound", "PermissionDenied", "InvalidPath", "InvalidText", "Io"]);
            EmitEmptyUnionStructuralEquality(LangTypeKind.BytesError, "BytesError", ["InvalidOctet"]);
            EmitEmptyUnionStructuralEquality(LangTypeKind.ProcessError, "ProcessError",
                ["InvalidArgument", "InputTooLarge", "OutputTooLarge", "InvalidText", "StartFailed", "TimedOut"]);
            EmitEmptyUnionStructuralEquality(LangTypeKind.HttpError, "HttpError",
                ["InvalidTarget", "Transport", "Timeout", "ResponseTooLarge", "InvalidText"]);
            EmitEmptyUnionStructuralEquality(LangTypeKind.DbError, "DbError", ["Statement", "RowShape"]);

            if (UsesTypeKind(LangTypeKind.FilePath))
                _source.AppendLine("    private static bool StructuralEqualsFilePath(FilePath left, FilePath right) => global::System.String.Equals(left.Value, right.Value, global::System.StringComparison.Ordinal);");
            if (UsesTypeKind(LangTypeKind.Html))
                _source.AppendLine("    private static bool StructuralEqualsHtml(Html left, Html right) => global::System.String.Equals(left.Value, right.Value, global::System.StringComparison.Ordinal);");
            if (UsesTypeKind(LangTypeKind.HttpResponse))
                _source.AppendLine("    private static bool StructuralEqualsHttpResponse(HttpResponse left, HttpResponse right) => left.Field_0 == right.Field_0 && global::System.String.Equals(left.Field_1, right.Field_1, global::System.StringComparison.Ordinal);");
            if (UsesTypeKind(LangTypeKind.ProcessOutput))
                _source.AppendLine("    private static bool StructuralEqualsProcessOutput(ProcessOutput left, ProcessOutput right) => left.Field_0 == right.Field_0 && global::System.String.Equals(left.Field_1, right.Field_1, global::System.StringComparison.Ordinal) && global::System.String.Equals(left.Field_2, right.Field_2, global::System.StringComparison.Ordinal);");
            _source.AppendLine();
        }

        private void RegisterEqualityParameterNames(IReadOnlyList<LangType> typeParameters, IReadOnlyList<int> ordinals)
        {
            foreach (var ordinal in ordinals)
                _nominalTypeParameterEqualityNames.Add(
                    typeParameters[ordinal],
                    "equals_" + ordinal.ToString(CultureInfo.InvariantCulture));
        }

        private void EmitNominalEqualitySignature(
            string nominalKind,
            int id,
            IReadOnlyList<LangType> typeParameters,
            LangType openType,
            IReadOnlyList<int> equalityParameterOrdinals,
            string comparison)
        {
            _source.Append("    private static bool StructuralEquals").Append(nominalKind).Append('_')
                .Append(id.ToString(CultureInfo.InvariantCulture));
            if (typeParameters.Count != 0)
                _source.Append('<').Append(string.Join(", ", typeParameters.Select((_, index) => TypeParameterName(index)))).Append('>');
            _source.Append('(').Append(EmitType(openType)).Append(" left, ")
                .Append(EmitType(openType)).Append(" right");
            foreach (var index in equalityParameterOrdinals)
            {
                var typeParameter = TypeParameterName(index);
                _source.Append(", global::System.Func<").Append(typeParameter).Append(", ")
                    .Append(typeParameter).Append(", bool> equals_")
                    .Append(index.ToString(CultureInfo.InvariantCulture));
            }
            _source.Append(") => ").Append(comparison).AppendLine(";");
        }

        private void EmitEmptyUnionStructuralEquality(LangTypeKind kind, string typeName, IReadOnlyList<string> variants)
        {
            if (!UsesTypeKind(kind)) return;
            var cases = variants.Select(variant =>
                "(" + typeName + "." + variant + " _, " + typeName + "." + variant + " _) => true").ToList();
            cases.Add("_ => false");
            _source.Append("    private static bool StructuralEquals").Append(typeName)
                .Append('(').Append(typeName).Append(" left, ").Append(typeName)
                .Append(" right) => (left, right) switch { ")
                .Append(string.Join(", ", cases)).AppendLine(" };");
        }

        private string EmitStructuralEquality(LangType type, string left, string right)
        {
            switch (type.Kind)
            {
                case LangTypeKind.I32:
                case LangTypeKind.I64:
                case LangTypeKind.U32:
                case LangTypeKind.U64:
                case LangTypeKind.F64:
                case LangTypeKind.Bool:
                    return "(" + left + " == " + right + ")";
                case LangTypeKind.Unit:
                    return "UnitEquals(" + left + ", " + right + ")";
                case LangTypeKind.Text:
                    return "global::System.String.Equals(" + left + ", " + right + ", global::System.StringComparison.Ordinal)";
                case LangTypeKind.Bytes:
                    return "(" + left + ").SequenceEquals(" + right + ")";
                case LangTypeKind.Struct:
                    {
                        var comparerArguments = new List<string>();
                        foreach (var index in EqualityParameterOrdinals(LangTypeKind.Struct, type.StructId))
                        {
                            if (index >= type.Arguments.Count)
                                throw new InvalidOperationException("A generic struct equality call is missing a type argument");
                            var leftArgument = "structLeft_" + _structuralEqualityTemporaryId.ToString(CultureInfo.InvariantCulture);
                            var rightArgument = "structRight_" + _structuralEqualityTemporaryId.ToString(CultureInfo.InvariantCulture);
                            _structuralEqualityTemporaryId++;
                            comparerArguments.Add("(" + leftArgument + ", " + rightArgument + ") => " +
                                EmitStructuralEquality(type.Arguments[index], leftArgument, rightArgument));
                        }
                        var arguments = left + ", " + right;
                        if (comparerArguments.Count != 0)
                            arguments += ", " + string.Join(", ", comparerArguments);
                        return "StructuralEqualsStruct_" + type.StructId.ToString(CultureInfo.InvariantCulture) + "(" + arguments + ")";
                    }
                case LangTypeKind.Newtype:
                    return "StructuralEqualsNewtype_" + type.NewtypeId.ToString(CultureInfo.InvariantCulture) + "(" + left + ", " + right + ")";
                case LangTypeKind.Union:
                    {
                        var comparerArguments = new List<string>();
                        foreach (var index in EqualityParameterOrdinals(LangTypeKind.Union, type.UnionId))
                        {
                            if (index >= type.Arguments.Count)
                                throw new InvalidOperationException("A generic union equality call is missing a type argument");
                            var leftArgument = "unionLeft_" + _structuralEqualityTemporaryId.ToString(CultureInfo.InvariantCulture);
                            var rightArgument = "unionRight_" + _structuralEqualityTemporaryId.ToString(CultureInfo.InvariantCulture);
                            _structuralEqualityTemporaryId++;
                            comparerArguments.Add("(" + leftArgument + ", " + rightArgument + ") => " +
                                EmitStructuralEquality(type.Arguments[index], leftArgument, rightArgument));
                        }
                        var arguments = left + ", " + right;
                        if (comparerArguments.Count != 0)
                            arguments += ", " + string.Join(", ", comparerArguments);
                        return "StructuralEqualsUnion_" + type.UnionId.ToString(CultureInfo.InvariantCulture) + "(" + arguments + ")";
                    }
                case LangTypeKind.TypeParameter:
                    if (_nominalTypeParameterEqualityNames.TryGetValue(type, out var equalityName))
                        return equalityName + "(" + left + ", " + right + ")";
                    throw new InvalidOperationException("Unresolved type parameter reached structural equality emission");
                case LangTypeKind.Option:
                {
                    var tempId = _structuralEqualityTemporaryId++;
                    var leftSome = "leftSome_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var rightSome = "rightSome_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var optionType = EmitType(type);
                    var someType = optionType + ".Some";
                    var noneType = optionType + ".None";
                    return "(" + left + ", " + right + ") switch { (" + someType + " " + leftSome + ", " + someType + " " + rightSome + ") => " +
                        EmitStructuralEquality(type.Arguments[0], leftSome + ".Value", rightSome + ".Value") +
                        ", (" + noneType + " _, " + noneType + " _) => true, _ => false }";
                }
                case LangTypeKind.Result:
                {
                    var tempId = _structuralEqualityTemporaryId++;
                    var leftOk = "leftOk_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var rightOk = "rightOk_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var leftErr = "leftErr_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var rightErr = "rightErr_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var resultType = EmitType(type);
                    var okType = resultType + ".Ok";
                    var errType = resultType + ".Err";
                    return "(" + left + ", " + right + ") switch { (" + okType + " " + leftOk + ", " + okType + " " + rightOk + ") => " +
                        EmitStructuralEquality(type.Arguments[0], leftOk + ".Value", rightOk + ".Value") +
                        ", (" + errType + " " + leftErr + ", " + errType + " " + rightErr + ") => " +
                        EmitStructuralEquality(type.Arguments[1], leftErr + ".Error", rightErr + ".Error") +
                        ", _ => false }";
                }
                case LangTypeKind.List:
                {
                    var tempId = _structuralEqualityTemporaryId++;
                    var leftItem = "leftItem_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var rightItem = "rightItem_" + tempId.ToString(CultureInfo.InvariantCulture);
                    return "SequenceStructuralEquals(" + left + ", " + right + ", (" + leftItem + ", " + rightItem + ") => " +
                        EmitStructuralEquality(type.Arguments[0], leftItem, rightItem) + ")";
                }
                case LangTypeKind.Map:
                {
                    var tempId = _structuralEqualityTemporaryId++;
                    var leftValue = "leftValue_" + tempId.ToString(CultureInfo.InvariantCulture);
                    var rightValue = "rightValue_" + tempId.ToString(CultureInfo.InvariantCulture);
                    return "MapStructuralEquals(" + left + ", " + right + ", (" + leftValue + ", " + rightValue + ") => " +
                        EmitStructuralEquality(type.Arguments[1], leftValue, rightValue) + ")";
                }
                case LangTypeKind.FilePath:
                    return "StructuralEqualsFilePath(" + left + ", " + right + ")";
                case LangTypeKind.Html:
                    return "StructuralEqualsHtml(" + left + ", " + right + ")";
                case LangTypeKind.HttpResponse:
                    return "StructuralEqualsHttpResponse(" + left + ", " + right + ")";
                case LangTypeKind.ProcessOutput:
                    return "StructuralEqualsProcessOutput(" + left + ", " + right + ")";
                case LangTypeKind.FsError:
                case LangTypeKind.BytesError:
                case LangTypeKind.ProcessError:
                case LangTypeKind.HttpError:
                case LangTypeKind.DbError:
                    return "StructuralEquals" + type.DisplayName + "(" + left + ", " + right + ")";
                default:
                    throw new InvalidOperationException("Unsupported type reached structural equality emission: " + type.DisplayName);
            }
        }

        private bool CanEmitStructuralEquality(LangType type)
        {
            var pending = new Stack<LangType>();
            var visited = new HashSet<LangType>();
            pending.Push(type);

            while (pending.TryPop(out var current))
            {
                switch (current.Kind)
                {
                    case LangTypeKind.TypeParameter:
                    case LangTypeKind.I32:
                    case LangTypeKind.I64:
                    case LangTypeKind.U32:
                    case LangTypeKind.U64:
                    case LangTypeKind.F64:
                    case LangTypeKind.Unit:
                    case LangTypeKind.Bool:
                    case LangTypeKind.Text:
                    case LangTypeKind.Bytes:
                    case LangTypeKind.BytesError:
                    case LangTypeKind.Html:
                    case LangTypeKind.FilePath:
                    case LangTypeKind.HttpResponse:
                    case LangTypeKind.ProcessOutput:
                    case LangTypeKind.FsError:
                    case LangTypeKind.ProcessError:
                    case LangTypeKind.HttpError:
                    case LangTypeKind.DbError:
                        break;
                    case LangTypeKind.Option:
                    case LangTypeKind.List:
                        if (current.Arguments.Count != 1) return false;
                        pending.Push(current.Arguments[0]);
                        break;
                    case LangTypeKind.Map:
                        if (current.Arguments.Count != 2 || !current.Arguments[0].IsText) return false;
                        pending.Push(current.Arguments[1]);
                        break;
                    case LangTypeKind.Result:
                        if (current.Arguments.Count != 2) return false;
                        pending.Push(current.Arguments[0]);
                        pending.Push(current.Arguments[1]);
                        break;
                    case LangTypeKind.Struct:
                        if (current.StructId < 0 || current.StructId >= program.Structs.Count) return false;
                        if (!visited.Add(current)) break;
                        var structure = program.Structs[current.StructId];
                        foreach (var field in structure.Fields)
                            pending.Push(SubstituteNominalTypeParameters(field.Type, LangTypeKind.Struct, structure.Id, current));
                        break;
                    case LangTypeKind.Newtype:
                        if (current.NewtypeId < 0 || current.NewtypeId >= program.Newtypes.Count) return false;
                        if (!visited.Add(current)) break;
                        pending.Push(program.Newtypes[current.NewtypeId].Representation);
                        break;
                    case LangTypeKind.Union:
                        if (current.UnionId < 0 || current.UnionId >= program.Unions.Count) return false;
                        if (!visited.Add(current)) break;
                        var union = program.Unions[current.UnionId];
                        foreach (var field in union.Variants.SelectMany(variant => variant.Fields))
                            pending.Push(SubstituteNominalTypeParameters(field.Type, LangTypeKind.Union, union.Id, current));
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        private static LangType SubstituteNominalTypeParameters(
            LangType type,
            LangTypeKind ownerKind,
            int ownerId,
            LangType instantiatedType)
        {
            if (type.Kind == LangTypeKind.TypeParameter &&
                type.TypeParameterOwnerKind == (ownerKind == LangTypeKind.Struct
                    ? TypeParameterOwnerKind.Struct
                    : TypeParameterOwnerKind.Union) &&
                type.TypeParameterOwnerId == ownerId &&
                type.TypeParameterOrdinal >= 0 && type.TypeParameterOrdinal < instantiatedType.Arguments.Count)
                return instantiatedType.Arguments[type.TypeParameterOrdinal];

            if (type.Arguments.Count == 0) return type;
            var arguments = type.Arguments.Select(argument =>
                SubstituteNominalTypeParameters(argument, ownerKind, ownerId, instantiatedType)).ToArray();
            return type.Kind switch
            {
                LangTypeKind.Option => LangType.Option(arguments[0]),
                LangTypeKind.List => LangType.List(arguments[0]),
                LangTypeKind.Map => LangType.Map(arguments[0], arguments[1]),
                LangTypeKind.Result => LangType.Result(arguments[0], arguments[1]),
                LangTypeKind.Struct => LangType.ForStruct(type.StructId, type.NominalName, arguments),
                LangTypeKind.Union => LangType.ForUnion(type.UnionId, type.NominalName, arguments),
                _ => type
            };
        }

        private int[] EqualityParameterOrdinals(LangTypeKind kind, int id)
        {
            _nominalEqualityParameterOrdinals ??= ComputeNominalEqualityParameterOrdinals();
            return _nominalEqualityParameterOrdinals[(kind, id)];
        }

        private Dictionary<(LangTypeKind Kind, int Id), int[]> ComputeNominalEqualityParameterOrdinals()
        {
            var requiredByNominal = new Dictionary<(LangTypeKind Kind, int Id), HashSet<int>>();
            foreach (var structure in program.Structs)
                requiredByNominal[(LangTypeKind.Struct, structure.Id)] = [];
            foreach (var union in program.Unions)
                requiredByNominal[(LangTypeKind.Union, union.Id)] = [];

            bool changed;
            do
            {
                changed = false;
                foreach (var structure in program.Structs)
                {
                    var required = requiredByNominal[(LangTypeKind.Struct, structure.Id)];
                    foreach (var field in structure.Fields)
                        CollectNominalEqualityParameterOrdinals(
                            field.Type,
                            LangTypeKind.Struct,
                            structure.Id,
                            structure.TypeParameters.Count,
                            required,
                            requiredByNominal,
                            ref changed);
                }
                foreach (var union in program.Unions)
                {
                    var required = requiredByNominal[(LangTypeKind.Union, union.Id)];
                    foreach (var field in union.Variants.SelectMany(variant => variant.Fields))
                        CollectNominalEqualityParameterOrdinals(
                            field.Type,
                            LangTypeKind.Union,
                            union.Id,
                            union.TypeParameters.Count,
                            required,
                            requiredByNominal,
                            ref changed);
                }
            } while (changed);

            return requiredByNominal.ToDictionary(pair => pair.Key, pair => pair.Value.Order().ToArray());
        }

        private void CollectNominalEqualityParameterOrdinals(
            LangType type,
            LangTypeKind ownerKind,
            int ownerId,
            int ownerTypeParameterCount,
            HashSet<int> requiredOrdinals,
            IReadOnlyDictionary<(LangTypeKind Kind, int Id), HashSet<int>> requiredByNominal,
            ref bool changed)
        {
            if (type.Kind == LangTypeKind.TypeParameter)
            {
                var expectedOwnerKind = ownerKind == LangTypeKind.Struct
                    ? TypeParameterOwnerKind.Struct
                    : TypeParameterOwnerKind.Union;
                if (type.TypeParameterOwnerKind == expectedOwnerKind &&
                    type.TypeParameterOwnerId == ownerId &&
                    type.TypeParameterOrdinal >= 0 && type.TypeParameterOrdinal < ownerTypeParameterCount &&
                    requiredOrdinals.Add(type.TypeParameterOrdinal))
                    changed = true;
                return;
            }

            if (type.Kind is LangTypeKind.Struct or LangTypeKind.Union)
            {
                var nestedKind = type.Kind;
                var nestedId = nestedKind == LangTypeKind.Struct ? type.StructId : type.UnionId;
                if (!requiredByNominal.TryGetValue((nestedKind, nestedId), out var nestedRequired)) return;
                foreach (var ordinal in nestedRequired.Order().ToArray())
                {
                    if (ordinal < type.Arguments.Count)
                        CollectNominalEqualityParameterOrdinals(
                            type.Arguments[ordinal], ownerKind, ownerId, ownerTypeParameterCount,
                            requiredOrdinals, requiredByNominal, ref changed);
                }
                return;
            }

            foreach (var argument in type.Arguments)
                CollectNominalEqualityParameterOrdinals(
                    argument, ownerKind, ownerId, ownerTypeParameterCount,
                    requiredOrdinals, requiredByNominal, ref changed);
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
            BuiltinVariant.BytesInvalidOctet or
            BuiltinVariant.DbErrorStatement or
            BuiltinVariant.DbErrorRowShape or
            BuiltinVariant.HttpInvalidTarget or
            BuiltinVariant.HttpTransport or
            BuiltinVariant.HttpTimeout or
            BuiltinVariant.HttpResponseTooLarge or
            BuiltinVariant.HttpInvalidText => throw new InvalidOperationException("Builtin error variants cannot be constructed from source"),
                _ => throw new InvalidOperationException("Unknown builtin union variant")
            };
            return "new " + EmitType(expression.Type) + "." + variant + "(" +
                string.Join(", ", expression.Arguments.Select(EmitExpr)) + ")";
        }

        private string EmitUnionConstruct(TypedUnionConstructExpr expression) =>
            "new " + EmitType(expression.Type) + ".Variant_" +
            expression.UnionId.ToString(CultureInfo.InvariantCulture) + "_" +
            expression.VariantId.ToString(CultureInfo.InvariantCulture) + "(" +
            string.Join(", ", expression.Arguments.Select(EmitExpr)) + ")";

        private string EmitStructConstruct(TypedStructConstructExpr expression) =>
            "new " + EmitType(expression.Type) + "(" +
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
                    BuiltinVariant.BytesInvalidOctet => "InvalidOctet",
                    BuiltinVariant.DbErrorStatement => "Statement",
                    BuiltinVariant.DbErrorRowShape => "RowShape",
                    BuiltinVariant.HttpInvalidTarget => "InvalidTarget",
                    BuiltinVariant.HttpTransport => "Transport",
                    BuiltinVariant.HttpTimeout => "Timeout",
            BuiltinVariant.HttpResponseTooLarge => "ResponseTooLarge",
            BuiltinVariant.HttpInvalidText => "InvalidText",
            BuiltinVariant.ProcessInvalidArgument => "InvalidArgument",
            BuiltinVariant.ProcessInputTooLarge => "InputTooLarge",
            BuiltinVariant.ProcessOutputTooLarge => "OutputTooLarge",
            BuiltinVariant.ProcessInvalidText => "InvalidText",
            BuiltinVariant.ProcessStartFailed => "StartFailed",
            BuiltinVariant.ProcessTimedOut => "TimedOut",
                    _ => throw new InvalidOperationException("Unknown builtin pattern")
                };
                variantType = EmitType(scrutineeType) + "." + variantName;
            }
            else
            {
                var unionId = variant.Shape.UnionId!.Value;
                variantType = EmitType(scrutineeType) + ".Variant_" +
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
            _source.AppendLine("    private static long CheckedAdd(long left, long right) => checked(left + right);");
            _source.AppendLine("    private static uint CheckedAdd(uint left, uint right) => checked(left + right);");
            _source.AppendLine("    private static ulong CheckedAdd(ulong left, ulong right) => checked(left + right);");
            _source.AppendLine("    private static int CheckedSubtract(int left, int right) => checked(left - right);");
            _source.AppendLine("    private static long CheckedSubtract(long left, long right) => checked(left - right);");
            _source.AppendLine("    private static uint CheckedSubtract(uint left, uint right) => checked(left - right);");
            _source.AppendLine("    private static ulong CheckedSubtract(ulong left, ulong right) => checked(left - right);");
            _source.AppendLine("    private static int CheckedMultiply(int left, int right) => checked(left * right);");
            _source.AppendLine("    private static long CheckedMultiply(long left, long right) => checked(left * right);");
            _source.AppendLine("    private static uint CheckedMultiply(uint left, uint right) => checked(left * right);");
            _source.AppendLine("    private static ulong CheckedMultiply(ulong left, ulong right) => checked(left * right);");
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

        private void EmitMapGetHelper()
        {
            _source.AppendLine("    private static Option<T> MapGet<T>(global::System.Collections.Immutable.ImmutableSortedDictionary<string, T> values, string key)");
            _source.AppendLine("    {");
            _source.AppendLine("        if (values.TryGetValue(key, out var value)) return new Option<T>.Some(value);");
            _source.AppendLine("        return new Option<T>.None();");
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

        private void EmitFsWriteTextAsyncHelper()
        {
            _source.AppendLine("    // Paths use the host OS filesystem resolution rules. FsWrite does not confine writes to a package root or provide a filesystem sandbox.");
            _source.AppendLine("    // The temporary file is staged beside the destination and renamed over its directory entry where the host OS supports replacement.");
            _source.AppendLine("    private static async global::System.Threading.Tasks.Task<Result<bool, FsError>> WriteTextAsync(FsWrite receiver, string path, string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        ArgumentNullException.ThrowIfNull(receiver);");
            _source.AppendLine("        string? temporaryPath = null;");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            receiver.CancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("            if (path.IndexOf('\\0') >= 0)");
            _source.AppendLine("                return new Result<bool, FsError>.Err(new FsError.InvalidPath());");
            _source.AppendLine("            var fullPath = Path.GetFullPath(path);");
            _source.AppendLine("            var directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException(\"Destination has no parent directory\", nameof(path));");
            _source.AppendLine("            var fileName = Path.GetFileName(fullPath);");
            _source.AppendLine("            var temporaryName = \".\" + (fileName.Length == 0 ? \"lang\" : fileName) + \".\" + Guid.NewGuid().ToString(\"N\") + \".tmp\";");
            _source.AppendLine("            temporaryPath = Path.Combine(directory, temporaryName);");
            _source.AppendLine("            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))");
            _source.AppendLine("            {");
            _source.AppendLine("                await global::System.Threading.Tasks.Task.Yield();");
            _source.AppendLine("                receiver.CancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                var bytes = new UTF8Encoding(false, true).GetBytes(value);");
            _source.AppendLine("                receiver.CancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                const int chunkSize = 65536;");
            _source.AppendLine("                for (var offset = 0; offset < bytes.Length; offset += chunkSize)");
            _source.AppendLine("                {");
            _source.AppendLine("                    receiver.CancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                    var count = Math.Min(chunkSize, bytes.Length - offset);");
            _source.AppendLine("                    await stream.WriteAsync(bytes.AsMemory(offset, count), receiver.CancellationToken);");
            _source.AppendLine("                }");
            _source.AppendLine("                await stream.FlushAsync(receiver.CancellationToken);");
            _source.AppendLine("                receiver.CancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("                stream.Flush(flushToDisk: true);");
            _source.AppendLine("            }");
            _source.AppendLine("            receiver.CancellationToken.ThrowIfCancellationRequested();");
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

        private void EmitHttpGetTextAsyncHelper()
        {
            _source.AppendLine("    private const int MaximumHttpResponseBytes = 1048576;");
            _source.AppendLine("    private static readonly global::System.Net.Http.HttpClient SharedHttpClient = CreateHttpClient();");
            _source.AppendLine("    private static global::System.Net.Http.HttpClient CreateHttpClient()");
            _source.AppendLine("    {");
            _source.AppendLine("        var handler = new global::System.Net.Http.SocketsHttpHandler");
            _source.AppendLine("        {");
            _source.AppendLine("            AllowAutoRedirect = false,");
            _source.AppendLine("            UseCookies = false,");
            _source.AppendLine("            UseProxy = false,");
            _source.AppendLine("            Credentials = null,");
            _source.AppendLine("            DefaultProxyCredentials = null");
            _source.AppendLine("        };");
            _source.AppendLine("        return new global::System.Net.Http.HttpClient(handler, disposeHandler: true)");
            _source.AppendLine("        {");
            _source.AppendLine("            Timeout = global::System.Threading.Timeout.InfiniteTimeSpan");
            _source.AppendLine("        };");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    private static async global::System.Threading.Tasks.Task<Result<HttpResponse, HttpError>> HttpGetTextAsync(HttpClientCapability receiver, string target)");
            _source.AppendLine("    {");
            _source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(receiver);");
            _source.AppendLine("        global::System.ArgumentNullException.ThrowIfNull(target);");
            _source.AppendLine("        receiver.CancellationToken.ThrowIfCancellationRequested();");
            _source.AppendLine("        if (!TryCreateHttpTarget(receiver.Origin, target, out var targetUri))");
            _source.AppendLine("            return new Result<HttpResponse, HttpError>.Err(new HttpError.InvalidTarget());");
            _source.AppendLine("        using var timeoutCancellation = global::System.Threading.CancellationTokenSource.CreateLinkedTokenSource(receiver.CancellationToken);");
            _source.AppendLine("        timeoutCancellation.CancelAfter(global::System.TimeSpan.FromSeconds(10));");
            _source.AppendLine("        try");
            _source.AppendLine("        {");
            _source.AppendLine("            using var request = new global::System.Net.Http.HttpRequestMessage(global::System.Net.Http.HttpMethod.Get, targetUri);");
            _source.AppendLine("            using var response = await SharedHttpClient.SendAsync(request, global::System.Net.Http.HttpCompletionOption.ResponseHeadersRead, timeoutCancellation.Token).ConfigureAwait(false);");
            _source.AppendLine("            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumHttpResponseBytes)");
            _source.AppendLine("                return new Result<HttpResponse, HttpError>.Err(new HttpError.ResponseTooLarge());");
            _source.AppendLine("            using var bodyStream = await response.Content.ReadAsStreamAsync(timeoutCancellation.Token).ConfigureAwait(false);");
            _source.AppendLine("            using var bodyBytes = new global::System.IO.MemoryStream();");
            _source.AppendLine("            var buffer = new byte[8192];");
            _source.AppendLine("            while (true)");
            _source.AppendLine("            {");
            _source.AppendLine("                var readLimit = global::System.Math.Min(buffer.Length, MaximumHttpResponseBytes + 1 - (int)bodyBytes.Length);");
            _source.AppendLine("                var bytesRead = await bodyStream.ReadAsync(buffer.AsMemory(0, readLimit), timeoutCancellation.Token).ConfigureAwait(false);");
            _source.AppendLine("                if (bytesRead == 0) break;");
            _source.AppendLine("                if (bytesRead > MaximumHttpResponseBytes - (int)bodyBytes.Length)");
            _source.AppendLine("                    return new Result<HttpResponse, HttpError>.Err(new HttpError.ResponseTooLarge());");
            _source.AppendLine("                bodyBytes.Write(buffer, 0, bytesRead);");
            _source.AppendLine("            }");
            _source.AppendLine("            var body = new global::System.Text.UTF8Encoding(false, true).GetString(bodyBytes.ToArray());");
            _source.AppendLine("            return new Result<HttpResponse, HttpError>.Ok(new HttpResponse((int)response.StatusCode, body));");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (global::System.OperationCanceledException) when (receiver.CancellationToken.IsCancellationRequested)");
            _source.AppendLine("        {");
            _source.AppendLine("            throw;");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (global::System.OperationCanceledException) when (timeoutCancellation.IsCancellationRequested)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<HttpResponse, HttpError>.Err(new HttpError.Timeout());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (global::System.OperationCanceledException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<HttpResponse, HttpError>.Err(new HttpError.Transport());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (global::System.Text.DecoderFallbackException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<HttpResponse, HttpError>.Err(new HttpError.InvalidText());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (global::System.Net.Http.HttpRequestException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<HttpResponse, HttpError>.Err(new HttpError.Transport());");
            _source.AppendLine("        }");
            _source.AppendLine("        catch (global::System.IO.IOException)");
            _source.AppendLine("        {");
            _source.AppendLine("            return new Result<HttpResponse, HttpError>.Err(new HttpError.Transport());");
            _source.AppendLine("        }");
            _source.AppendLine("    }");
            _source.AppendLine();
            _source.AppendLine("    private static bool TryCreateHttpTarget(global::System.Uri origin, string target, out global::System.Uri targetUri)");
            _source.AppendLine("    {");
            _source.AppendLine("        targetUri = origin;");
            _source.AppendLine("        if (target.Length == 0 || target[0] != '/' || target.StartsWith(\"//\", global::System.StringComparison.Ordinal) ||");
            _source.AppendLine("            target.IndexOf('\\\\') >= 0 || target.IndexOf('#') >= 0)");
            _source.AppendLine("            return false;");
            _source.AppendLine("        foreach (var character in target)");
            _source.AppendLine("            if (global::System.Char.IsControl(character)) return false;");
            _source.AppendLine("        if (!global::System.Uri.TryCreate(origin, target, out var candidate) || candidate is null || candidate.UserInfo.Length != 0)");
            _source.AppendLine("            return false;");
            _source.AppendLine("        if (!global::System.String.Equals(candidate.Scheme, origin.Scheme, global::System.StringComparison.OrdinalIgnoreCase) ||");
            _source.AppendLine("            !global::System.String.Equals(candidate.IdnHost, origin.IdnHost, global::System.StringComparison.OrdinalIgnoreCase) ||");
            _source.AppendLine("            candidate.Port != origin.Port)");
            _source.AppendLine("            return false;");
            _source.AppendLine("        targetUri = candidate;");
            _source.AppendLine("        return true;");
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
            if (NeedsConfigSnapshot)
            {
                _source.AppendLine("        if (!TryCreateConfigSnapshot(out var configSnapshot, out var missingConfiguration))");
                _source.AppendLine("        {");
                _source.AppendLine("            Console.Error.WriteLine(\"Missing required configuration field: \" + missingConfiguration);");
                _source.AppendLine("            Environment.ExitCode = 78;");
                _source.AppendLine("            return;");
                _source.AppendLine("        }");
            }
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
                    .Append(route.Id.ToString(CultureInfo.InvariantCulture)).Append("(context, app.Logger");
                if (NeedsConfigSnapshot) _source.Append(", configSnapshot");
                _source.AppendLine("));");
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
            EmitRouteBindingRuntime();
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

        private void EmitRouteBindingRuntime()
        {
            if (!program.Routes.SelectMany(route => route.Bindings)
                    .Any(binding => Emitter.RouteBindingValueType(binding).Kind == LangTypeKind.I32))
                return;

            _source.AppendLine("    private static int DecodeRouteI32(string value)");
            _source.AppendLine("    {");
            _source.AppendLine("        var digitStart = value.Length > 0 && value[0] == '-' ? 1 : 0;");
            _source.AppendLine("        if (digitStart == value.Length) throw new BadHttpRequestException(\"Invalid route value.\");");
            _source.AppendLine("        for (var index = digitStart; index < value.Length; index++)");
            _source.AppendLine("            if (value[index] < '0' || value[index] > '9') throw new BadHttpRequestException(\"Invalid route value.\");");
            _source.AppendLine("        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var result)) throw new BadHttpRequestException(\"Invalid route value.\");");
            _source.AppendLine("        return result;");
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
                .Append("(HttpContext context, ILogger logger");
            if (NeedsConfigSnapshot) _source.Append(", ConfigSnapshot configSnapshot");
            _source.AppendLine(")");
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
                arguments.AddRange(EmitRouteBindings(route));
                arguments.AddRange(EmitRouteCapabilities(route));
                if (route.HandlerIsAsync) arguments.Add("context.RequestAborted");
                _source.Append("            var reply = ");
                if (route.HandlerIsAsync) _source.Append("await ");
                _source.Append("Function_").Append(handler.Id.ToString(CultureInfo.InvariantCulture))
                    .Append('(').Append(string.Join(", ", arguments)).AppendLine(");");
            }
            else
            {
                var arguments = EmitRouteBindings(route);
                arguments.AddRange(EmitRouteCapabilities(route));
                if (route.HandlerIsAsync) arguments.Add("context.RequestAborted");
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

        private List<string> EmitRouteBindings(CheckedRoute route)
        {
            var arguments = new List<string>();
            foreach (var binding in route.Bindings.OrderBy(binding => binding.HandlerParameterIndex))
            {
                var index = binding.HandlerParameterIndex.ToString(CultureInfo.InvariantCulture);
                var argumentName = "routeArg_" + index;
                var valueType = Emitter.RouteBindingValueType(binding);

                if (binding.Kind == CheckedRouteBindingKind.Path)
                {
                    if (binding.IsOptional)
                        throw new InvalidOperationException("Path route bindings cannot be optional");
                    var rawName = "routeRaw_" + index;
                    var textName = "routeText_" + index;
                    _source.Append("            if (!context.Request.RouteValues.TryGetValue(")
                        .Append(JsonSerializer.Serialize(binding.WireName)).Append(", out var ").Append(rawName)
                        .Append(") || ").Append(rawName).Append(" is not string ").Append(textName)
                        .AppendLine(") throw new BadHttpRequestException(\"Missing route value.\");");
                    _source.Append("            var ").Append(argumentName).Append(" = ")
                        .Append(DecodeRouteBindingValue(valueType, textName)).AppendLine(";");
                }
                else if (binding.Kind == CheckedRouteBindingKind.Query)
                {
                    var valuesName = "queryValues_" + index;
                    var textName = "queryText_" + index;
                    if (binding.IsOptional)
                    {
                        var innerType = EmitType(valueType);
                        _source.Append("            ").Append(EmitType(binding.Type)).Append(' ').Append(argumentName).AppendLine(";");
                        _source.Append("            if (!context.Request.Query.TryGetValue(")
                            .Append(JsonSerializer.Serialize(binding.WireName)).Append(", out var ").Append(valuesName)
                            .Append(") || ").Append(valuesName).AppendLine(".Count == 0)");
                        _source.AppendLine("            {");
                        _source.Append("                ").Append(argumentName).Append(" = new Option<").Append(innerType).AppendLine(">.None();");
                        _source.Append("            } else if (").Append(valuesName).AppendLine(".Count != 1)");
                        _source.AppendLine("            {");
                        _source.AppendLine("                throw new BadHttpRequestException(\"Invalid query value.\");");
                        _source.AppendLine("            }");
                        _source.AppendLine("            else");
                        _source.AppendLine("            {");
                        _source.Append("                var ").Append(textName).Append(" = ").Append(valuesName)
                            .AppendLine("[0] ?? throw new BadHttpRequestException(\"Invalid query value.\");");
                        _source.Append("                ").Append(argumentName).Append(" = new Option<").Append(innerType).Append(">.Some(")
                            .Append(DecodeRouteBindingValue(valueType, textName)).AppendLine(");");
                        _source.AppendLine("            }");
                    }
                    else
                    {
                        _source.Append("            if (!context.Request.Query.TryGetValue(")
                            .Append(JsonSerializer.Serialize(binding.WireName)).Append(", out var ").Append(valuesName)
                            .Append(") || ").Append(valuesName).AppendLine(".Count != 1)");
                        _source.AppendLine("                throw new BadHttpRequestException(\"Invalid query value.\");");
                        _source.Append("            var ").Append(textName).Append(" = ").Append(valuesName)
                            .AppendLine("[0] ?? throw new BadHttpRequestException(\"Invalid query value.\");");
                        _source.Append("            var ").Append(argumentName).Append(" = ")
                            .Append(DecodeRouteBindingValue(valueType, textName)).AppendLine(";");
                    }
                }
                else
                {
                    throw new InvalidOperationException("Unknown checked route binding kind");
                }

                arguments.Add(argumentName);
            }

            return arguments;
        }

        private static string DecodeRouteBindingValue(LangType type, string value) => type.Kind switch
        {
            LangTypeKind.I32 => "DecodeRouteI32(" + value + ")",
            LangTypeKind.Text => value,
            _ => throw new InvalidOperationException("Unsupported checked route parameter type")
        };

        private IEnumerable<string> EmitRouteCapabilities(CheckedRoute route)
        {
            foreach (var capability in route.Capabilities.OrderBy(capability => capability.HandlerParameterIndex))
            {
                if (capability.Kind is CheckedCapabilityKind.DbRead or CheckedCapabilityKind.DbWrite &&
                    _webDatabaseOptions is null)
                    throw new InvalidOperationException("Checked route database capability reached emission without web database options");

                yield return capability.Kind switch
                {
                    CheckedCapabilityKind.FsWrite => route.HandlerIsAsync
                        ? "new FsWrite(context.RequestAborted)"
                        : "new FsWrite()",
                    CheckedCapabilityKind.DbRead => "new DbRead(DatabaseReadConnectionString, context.RequestAborted)",
                    CheckedCapabilityKind.DbWrite => "new DbWrite(DatabaseWriteConnectionString, context.RequestAborted)",
                    CheckedCapabilityKind.HttpClient => "new HttpClientCapability(" + HttpOriginLiteral + ", context.RequestAborted)",
                    CheckedCapabilityKind.Config => "new Config(configSnapshot)",
                    CheckedCapabilityKind.Secrets => "new Secrets()",
                    CheckedCapabilityKind.Logger => "new Logger(context.TraceIdentifier)",
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
                if (NeedsConfigSnapshot)
                {
                    _source.AppendLine("                if (!TryCreateConfigSnapshot(out _, out var missingConfiguration))");
                    _source.AppendLine("                {");
                    _source.AppendLine("                    Console.Error.WriteLine(\"Missing required configuration field: \" + missingConfiguration);");
                    _source.AppendLine("                    return 78;");
                    _source.AppendLine("                }");
                }

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
            if (NeedsConfigSnapshot)
            {
                _source.AppendLine("            if (!TryCreateConfigSnapshot(out _, out var missingConfiguration))");
                _source.AppendLine("            {");
                _source.AppendLine("                Console.Error.WriteLine(\"Missing required configuration field: \" + missingConfiguration);");
                _source.AppendLine("                return 78;");
                _source.AppendLine("            }");
            }

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
            if (NeedsConfigSnapshot)
            {
                _source.AppendLine("            if (!TryCreateConfigSnapshot(out var configSnapshot, out var missingConfiguration))");
                _source.AppendLine("            {");
                _source.AppendLine("                Console.Error.WriteLine(\"Missing required configuration field: \" + missingConfiguration);");
                _source.AppendLine("                return 78;");
                _source.AppendLine("            }");
            }
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
                _source.Append("RunCommand_").Append(command.Id.ToString(CultureInfo.InvariantCulture)).Append("(args[1..]");
                if (command.HandlerIsAsync) _source.Append(", cancellation.Token");
                if (NeedsConfigSnapshot) _source.Append(", configSnapshot");
                _source.AppendLine(");");
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
                .Append("(string[] args");
            if (command.HandlerIsAsync) _source.Append(", CancellationToken cancellationToken");
            if (NeedsConfigSnapshot) _source.Append(", ConfigSnapshot configSnapshot");
            _source.AppendLine(")");
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
                    CheckedCapabilityKind.FsWrite => command.HandlerIsAsync
                        ? ", new FsWrite(cancellationToken)"
                        : ", new FsWrite()",
                    CheckedCapabilityKind.HttpClient => command.HandlerIsAsync
                        ? ", new HttpClientCapability(" + HttpOriginLiteral + ", cancellationToken)"
                        : ", new HttpClientCapability(" + HttpOriginLiteral + ", System.Threading.CancellationToken.None)",
                    CheckedCapabilityKind.Config => ", new Config(configSnapshot)",
                    CheckedCapabilityKind.Secrets => ", new Secrets()",
                    CheckedCapabilityKind.Logger => ", new Logger(null)",
                    CheckedCapabilityKind.ProcessRunner => ", " + ProcessRunnerConstructor,
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
            LangTypeKind.I64 => "long",
            LangTypeKind.U32 => "uint",
            LangTypeKind.U64 => "ulong",
            LangTypeKind.F64 => "double",
            LangTypeKind.Unit => "global::System.ValueTuple",
            LangTypeKind.Bool => "bool",
            LangTypeKind.Text => "string",
            LangTypeKind.Bytes => "Bytes",
            LangTypeKind.BytesError => "BytesError",
            LangTypeKind.List => "global::System.Collections.Immutable.ImmutableArray<" + EmitType(type.Arguments[0]) + ">",
            LangTypeKind.Map => EmitMapType(type),
            LangTypeKind.FilePath => "FilePath",
            LangTypeKind.Html => "Html",
            LangTypeKind.Union => EmitUnionType(type),
            LangTypeKind.Struct => EmitStructType(type),
            LangTypeKind.Newtype => "Newtype_" + type.NewtypeId.ToString(CultureInfo.InvariantCulture),
            LangTypeKind.Option => "Option<" + EmitType(type.Arguments[0]) + ">",
            LangTypeKind.Result => "Result<" + EmitType(type.Arguments[0]) + ", " + EmitType(type.Arguments[1]) + ">",
            LangTypeKind.TypeParameter => EmitTypeParameter(type),
            LangTypeKind.FsRead => "FsRead",
            LangTypeKind.FsWrite => "FsWrite",
            LangTypeKind.Config => "Config",
            LangTypeKind.Secrets => "Secrets",
            LangTypeKind.Logger => "Logger",
            LangTypeKind.SecretText => "SecretText",
            LangTypeKind.FsError => "FsError",
            LangTypeKind.DbRead => "DbRead",
            LangTypeKind.DbWrite => "DbWrite",
            LangTypeKind.Transaction => "DbTransaction",
            LangTypeKind.DbError => "DbError",
            LangTypeKind.HttpClient => "HttpClientCapability",
            LangTypeKind.HttpResponse => "HttpResponse",
            LangTypeKind.HttpError => "HttpError",
            LangTypeKind.ProcessRunner => "ProcessRunner",
            LangTypeKind.ProcessOutput => "ProcessOutput",
            LangTypeKind.ProcessError => "ProcessError",
            _ => throw new InvalidOperationException("Error type reached emitter")
        };

        private string EmitMapType(LangType type)
        {
            if (type.Arguments.Count != 2 || type.Arguments[0].Kind != LangTypeKind.Text)
                throw new InvalidOperationException("Checked Map types must have Text keys and one value type");
            return "global::System.Collections.Immutable.ImmutableSortedDictionary<string, " +
                EmitType(type.Arguments[1]) + ">";
        }

        private string EmitStructType(LangType type)
        {
            var name = "Struct_" + type.StructId.ToString(CultureInfo.InvariantCulture);
            return type.Arguments.Count == 0
                ? name
                : name + "<" + string.Join(", ", type.Arguments.Select(EmitType)) + ">";
        }

        private string EmitUnionType(LangType type)
        {
            var name = "Union_" + type.UnionId.ToString(CultureInfo.InvariantCulture);
            return type.Arguments.Count == 0
                ? name
                : name + "<" + string.Join(", ", type.Arguments.Select(EmitType)) + ">";
        }

        private string EmitTypeParameter(LangType type)
        {
            var ordinal = type.TypeParameterOrdinal;
            if (type.TypeParameterOwnerKind == TypeParameterOwnerKind.Trait)
            {
                if (_emittingTrait is null || _emittingTrait.Id != type.TypeParameterOwnerId || ordinal != 0)
                    throw new InvalidOperationException("Trait Self reached emitter outside its defining trait or implementation");
                return _emittingTraitSelfTarget is null ? "TSelf" : EmitType(_emittingTraitSelfTarget);
            }
            if (type.TypeParameterOwnerKind == TypeParameterOwnerKind.Function)
            {
                if (_emittingFunction is null ||
                    type.TypeParameterOwnerId != _emittingFunction.Id ||
                    ordinal < 0 || ordinal >= _emittingFunction.TypeParameters.Count)
                    throw new InvalidOperationException("Type parameter reached emitter outside its defining function");
            }
            else if (type.TypeParameterOwnerKind == TypeParameterOwnerKind.Struct)
            {
                if (_emittingStruct is null || _emittingStruct.Id != type.TypeParameterOwnerId ||
                    ordinal < 0 || ordinal >= _emittingStruct.TypeParameters.Count)
                    throw new InvalidOperationException("Type parameter reached emitter outside its defining struct");
            }
            else if (_emittingUnion is null || _emittingUnion.Id != type.TypeParameterOwnerId ||
                     ordinal < 0 || ordinal >= _emittingUnion.TypeParameters.Count)
            {
                throw new InvalidOperationException("Type parameter reached emitter outside its defining union");
            }
            return TypeParameterName(ordinal);
        }

        private static string TypeParameterName(int ordinal) =>
            "T" + ordinal.ToString(CultureInfo.InvariantCulture);

        private string HttpOriginLiteral => JsonSerializer.Serialize(_httpOrigin ??
            throw new InvalidOperationException("HTTP client capability reached emission without a manifest origin"));

        private string ProcessRunnerConstructor
        {
            get
            {
                var options = _processRunnerOptions ??
                    throw new InvalidOperationException("ProcessRunner capability reached emission without a selected executable pin");
                return "new ProcessRunner(" + JsonSerializer.Serialize(options.ArtifactFileName) + ", " +
                    JsonSerializer.Serialize(options.Sha256) + ")";
            }
        }

        private bool NeedsFilePathType => UsesTypeKind(LangTypeKind.FilePath);

        private bool NeedsBytesType => UsesTypeKind(LangTypeKind.Bytes);

        private bool NeedsBytesErrorType => UsesTypeKind(LangTypeKind.BytesError) || NeedsBytesType;

        private bool NeedsHtmlType => UsesTypeKind(LangTypeKind.Html);

        private bool NeedsConfigSnapshot => program.ConfigFields.Count != 0 || UsesTypeKind(LangTypeKind.Config) ||
            HasCapabilityKind(CheckedCapabilityKind.Config);

        private bool NeedsConfigType => program.ConfigFields.Count != 0 || UsesTypeKind(LangTypeKind.Config) ||
            HasCapabilityKind(CheckedCapabilityKind.Config);

        private bool NeedsSecretsType => UsesTypeKind(LangTypeKind.Secrets) ||
            HasCapabilityKind(CheckedCapabilityKind.Secrets);

        private bool NeedsLoggerType => UsesTypeKind(LangTypeKind.Logger) ||
            HasCapabilityKind(CheckedCapabilityKind.Logger);

        private bool NeedsSecretTextType => program.ConfigFields.Any(configField => configField.Kind == ConfigFieldKind.SecretText) ||
            UsesTypeKind(LangTypeKind.SecretText) || NeedsSecretsType;

        private bool HasCapabilityKind(CheckedCapabilityKind kind) =>
            program.Commands.Any(command => command.Capabilities.Any(capability => capability.Kind == kind)) ||
            program.Routes.Any(route => route.Capabilities.Any(capability => capability.Kind == kind));

        private bool NeedsFsReadType => UsesTypeKind(LangTypeKind.FsRead) || UsesFsReadText ||
            program.Commands.Any(command => command.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsRead));

        private bool NeedsFsWriteType => UsesTypeKind(LangTypeKind.FsWrite) || UsesFsWriteText ||
            program.Commands.Any(command => command.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsWrite)) ||
            program.Routes.Any(route => route.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.FsWrite));

        private bool NeedsFsErrorType => UsesTypeKind(LangTypeKind.FsError) || UsesFsReadText || UsesFsReadTextAsync ||
            UsesFsWriteText || UsesFsWriteTextAsync;

        private bool NeedsDbReadType => UsesTypeKind(LangTypeKind.DbRead) || UsesDatabase;

        private bool NeedsDbWriteType => UsesTypeKind(LangTypeKind.DbWrite) || UsesDatabase;

        private bool NeedsDbTransactionType => UsesTypeKind(LangTypeKind.Transaction) ||
            TransactionScopes.Count != 0 || TransactionCommits.Count != 0;

        private bool NeedsDbErrorType => UsesTypeKind(LangTypeKind.DbError) || UsesDatabase;

        private bool NeedsHttpClientType => UsesTypeKind(LangTypeKind.HttpClient) || UsesHttpGetTextAsync ||
            program.Commands.Any(command => command.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.HttpClient)) ||
            program.Routes.Any(route => route.Capabilities.Any(capability => capability.Kind == CheckedCapabilityKind.HttpClient));

        private bool NeedsHttpResponseType => UsesTypeKind(LangTypeKind.HttpResponse) || UsesHttpGetTextAsync;

        private bool NeedsHttpErrorType => UsesTypeKind(LangTypeKind.HttpError) || UsesHttpGetTextAsync;

        private bool NeedsProcessRunnerRuntime =>
            UsesTypeKind(LangTypeKind.ProcessRunner) ||
            UsesTypeKind(LangTypeKind.ProcessOutput) ||
            UsesTypeKind(LangTypeKind.ProcessError) ||
            HasCapabilityKind(CheckedCapabilityKind.ProcessRunner) ||
            UsesProcessRunTextAsync;

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

        private bool UsesResultPropagation => EmittedFunctions.Any(FunctionUsesResultPropagation);

        private bool FunctionUsesResultPropagation(CheckedFunction function) =>
            EnumerateStatements(function.Body)
                .SelectMany(StatementExpressions)
                .SelectMany(EnumerateExpressions)
                .Any(expression => expression is TypedResultPropagateExpr);

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

        private bool UsesFsWriteTextAsync => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedIntrinsicCallExpr>()
            .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.FsWriteTextAsync);

        private bool UsesHttpGetTextAsync => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedIntrinsicCallExpr>()
            .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.HttpGetTextAsync);

        private bool UsesProcessRunTextAsync => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedIntrinsicCallExpr>()
            .Any(intrinsic => intrinsic.Intrinsic == BuiltinIntrinsic.ProcessRunTextAsync);

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

        private bool UsesMapGet => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .Any(expression => expression is TypedMapGetExpr);

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

        private bool UsesStructuralEquality => EmittedFunctions
            .SelectMany(function => EnumerateStatements(function.Body))
            .SelectMany(StatementExpressions)
            .SelectMany(EnumerateExpressions)
            .OfType<TypedCompareExpr>()
            .Any(comparison => comparison.Op is "==" or "!=");

        private IEnumerable<LangType> EnumerateDeclaredTypes()
        {
            foreach (var union in program.Unions)
            foreach (var variant in union.Variants)
            foreach (var field in variant.Fields)
                yield return field.Type;

            foreach (var structure in program.Structs)
            foreach (var field in structure.Fields)
                yield return field.Type;

            foreach (var newtype in program.Newtypes)
                yield return newtype.Representation;

            foreach (var trait in program.Traits)
                foreach (var method in trait.Methods)
                {
                    foreach (var parameter in method.Parameters)
                        yield return parameter.Type;
                    yield return method.ReturnType;
                }
            foreach (var implementation in program.TraitImpls)
                yield return implementation.Target;

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
                        if (expression is TypedTraitCallExpr traitCall)
                            yield return traitCall.Type;
                        if (expression is TypedLambdaInvokeExpr lambda)
                            yield return lambda.ParameterType;
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
                case TypedLambdaInvokeExpr lambda:
                    foreach (var nested in EnumerateExpressions(lambda.Argument)) yield return nested;
                    foreach (var nested in EnumerateExpressions(lambda.Body)) yield return nested;
                    break;
                case TypedListExpr list:
                    foreach (var item in list.Items)
                    foreach (var nested in EnumerateExpressions(item)) yield return nested;
                    break;
                case TypedMapSetExpr map:
                    foreach (var nested in EnumerateExpressions(map.Target)) yield return nested;
                    foreach (var nested in EnumerateExpressions(map.Key)) yield return nested;
                    foreach (var nested in EnumerateExpressions(map.Value)) yield return nested;
                    break;
                case TypedMapGetExpr map:
                    foreach (var nested in EnumerateExpressions(map.Target)) yield return nested;
                    foreach (var nested in EnumerateExpressions(map.Key)) yield return nested;
                    break;
                case TypedMapKeysExpr map:
                    foreach (var nested in EnumerateExpressions(map.Target)) yield return nested;
                    break;
                case TypedMapLengthExpr map:
                    foreach (var nested in EnumerateExpressions(map.Target)) yield return nested;
                    break;
                case TypedBinaryExpr binary:
                    foreach (var nested in EnumerateExpressions(binary.Left)) yield return nested;
                    foreach (var nested in EnumerateExpressions(binary.Right)) yield return nested;
                    break;
                case TypedUnaryExpr unary:
                    foreach (var nested in EnumerateExpressions(unary.Operand)) yield return nested;
                    break;
                case TypedCompareExpr comparison:
                    foreach (var nested in EnumerateExpressions(comparison.Left)) yield return nested;
                    foreach (var nested in EnumerateExpressions(comparison.Right)) yield return nested;
                    break;
                case TypedCallExpr call:
                    foreach (var argument in call.Arguments)
                    foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedTraitCallExpr call:
                    foreach (var argument in call.Arguments)
                        foreach (var nested in EnumerateExpressions(argument)) yield return nested;
                    break;
                case TypedAwaitExpr awaited:
                    foreach (var nested in EnumerateExpressions(awaited.Value)) yield return nested;
                    break;
                case TypedResultPropagateExpr propagated:
                    foreach (var nested in EnumerateExpressions(propagated.Operand)) yield return nested;
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
                case TypedBytesLengthExpr length:
                    foreach (var nested in EnumerateExpressions(length.Target)) yield return nested;
                    break;
                case TypedBytesGetExpr get:
                    foreach (var nested in EnumerateExpressions(get.Target)) yield return nested;
                    foreach (var nested in EnumerateExpressions(get.Index)) yield return nested;
                    break;
                case TypedBytesAppendExpr append:
                    foreach (var nested in EnumerateExpressions(append.Target)) yield return nested;
                    foreach (var nested in EnumerateExpressions(append.Octet)) yield return nested;
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
                case TypedNewtypeConstructExpr newtype:
                    foreach (var nested in EnumerateExpressions(newtype.Value)) yield return nested;
                    break;
                case TypedNewtypeProjectExpr newtype:
                    foreach (var nested in EnumerateExpressions(newtype.Target)) yield return nested;
                    break;
                case TypedFieldAccessExpr field:
                    foreach (var nested in EnumerateExpressions(field.Target)) yield return nested;
                    break;
                case TypedMatchExpr match:
                    foreach (var nested in EnumerateExpressions(match.Value)) yield return nested;
                    foreach (var arm in match.Arms)
                    foreach (var nested in EnumerateExpressions(arm.Body)) yield return nested;
                    break;
                case TypedUnitExpr:
                    break;
            }
        }

        private static bool ContainsTypeKind(LangType type, LangTypeKind kind) =>
            type.Kind == kind || type.Arguments.Any(argument => ContainsTypeKind(argument, kind));

        private void Indent(int level) => _source.Append(' ', level * 4);
    }
}
