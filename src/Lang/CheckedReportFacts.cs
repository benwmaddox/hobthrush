internal sealed record TrustedOperation(string Operation, string Trust, IReadOnlyList<string> Effects);

internal static class CheckedReportFacts
{
    public static string[] RequiredCapabilities(IEnumerable<string> effects) => effects
        .Where(effect => effect is "fs.read" or "fs.write" or "db.read" or "db.write" or "net.client" or
            "env.read" or "secret.reveal" or "log.write" or "process.spawn")
        .Distinct(StringComparer.Ordinal)
        .OrderBy(effect => effect, StringComparer.Ordinal)
        .ToArray();

    public static object[] ConfigMetadata(IEnumerable<CheckedConfigField> fields) => fields
        .OrderBy(field => field.Name, StringComparer.Ordinal)
        .Select(field => (object)new
        {
            name = field.Name,
            source_type = field.Kind == ConfigFieldKind.Text ? "Text" : "Secret<Text>",
            required = field.Required,
            has_default = field.HasDefault
        })
        .ToArray();

    private static IReadOnlyList<string> TrustedAdapterEffects(string operation) => operation switch
    {
        "sha256.text.hash_utf8" => Array.Empty<string>(),
        "FsRead.read_text" or "FsRead.read_text_async" => ["fs.read"],
        "FsWrite.write_text" or "FsWrite.write_text_async" => ["fs.write"],
        "DbRead.query_one" => ["db.read"],
        "DbWrite.execute" or "DbWrite.begin" or "Transaction.execute" or "Transaction.commit" => ["db.write"],
        "HttpClient.get_text_async" => ["net.client"],
        "ProcessRunner.run_text_async" => ["process.spawn"],
        "Config.get_text" or "Config.get_secret_text" => ["env.read"],
        "Secrets.reveal_text" => ["secret.reveal"],
        "Logger.info" => ["log.write"],
        _ => throw new InvalidOperationException($"Unknown trusted adapter operation '{operation}'")
    };

    public static IReadOnlyList<TrustedOperation> FindTrustedAdapterOperations(
        CheckedProgram program,
        CheckedFunction root)
    {
        var functionById = program.Functions.ToDictionary(function => function.Id);
        var visitedFunctions = new HashSet<int>();
        var operationNames = new SortedSet<string>(StringComparer.Ordinal);

        VisitFunction(root);
        return operationNames.Select(operation => new TrustedOperation(
            operation,
            "trusted_adapter",
            TrustedAdapterEffects(operation))).ToArray();

        void VisitFunction(CheckedFunction function)
        {
            if (!visitedFunctions.Add(function.Id))
                return;

            if (function.AdapterBinding is { } adapterBinding)
                operationNames.Add(adapterBinding.OperationId);

            foreach (var statement in function.Body)
                VisitStatement(statement);
        }

        void VisitStatement(TypedStmt statement)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    VisitExpression(let.Value);
                    break;
                case TypedAssignStmt assigned:
                    VisitExpression(assigned.Value);
                    break;
                case TypedReturnStmt returned:
                    VisitExpression(returned.Value);
                    break;
                case TypedIfStmt conditional:
                    VisitExpression(conditional.Condition);
                    foreach (var nested in conditional.ThenBody)
                        VisitStatement(nested);
                    if (conditional.ElseBody is not null)
                    {
                        foreach (var nested in conditional.ElseBody)
                            VisitStatement(nested);
                    }
                    break;
                case TypedForStmt loop:
                    VisitExpression(loop.Collection);
                    foreach (var nested in loop.Body)
                        VisitStatement(nested);
                    break;
                case TypedWithTransactionStmt transaction:
                    operationNames.Add("DbWrite.begin");
                    VisitExpression(transaction.Database);
                    foreach (var nested in transaction.Body)
                        VisitStatement(nested);
                    break;
            }
        }

        void VisitExpression(TypedExpr expression)
        {
            switch (expression)
            {
                case TypedLambdaInvokeExpr lambda:
                    VisitExpression(lambda.Argument);
                    VisitExpression(lambda.Body);
                    break;
                case TypedAwaitExpr awaited:
                    VisitExpression(awaited.Value);
                    break;
                case TypedListExpr list:
                    foreach (var item in list.Items)
                        VisitExpression(item);
                    break;
                case TypedBinaryExpr binary:
                    VisitExpression(binary.Left);
                    VisitExpression(binary.Right);
                    break;
                case TypedCompareExpr comparison:
                    VisitExpression(comparison.Left);
                    VisitExpression(comparison.Right);
                    break;
                case TypedTextLengthExpr length:
                    VisitExpression(length.Target);
                    break;
                case TypedTextTrimExpr trim:
                    VisitExpression(trim.Target);
                    break;
                case TypedListLengthExpr length:
                    VisitExpression(length.Target);
                    break;
                case TypedListGetExpr get:
                    VisitExpression(get.Target);
                    VisitExpression(get.Index);
                    break;
                case TypedListAppendExpr append:
                    VisitExpression(append.Target);
                    VisitExpression(append.Value);
                    break;
                case TypedMapEmptyExpr:
                    break;
                case TypedMapSetExpr set:
                    VisitExpression(set.Target);
                    VisitExpression(set.Key);
                    VisitExpression(set.Value);
                    break;
                case TypedMapGetExpr get:
                    VisitExpression(get.Target);
                    VisitExpression(get.Key);
                    break;
                case TypedMapKeysExpr keys:
                    VisitExpression(keys.Target);
                    break;
                case TypedMapLengthExpr length:
                    VisitExpression(length.Target);
                    break;
                case TypedCallExpr call:
                    foreach (var argument in call.Arguments)
                        VisitExpression(argument);
                    if (functionById.TryGetValue(call.FunctionId, out var calledFunction))
                        VisitFunction(calledFunction);
                    break;
                case TypedIntrinsicCallExpr intrinsic:
                    if (intrinsic.Intrinsic == BuiltinIntrinsic.FsReadText)
                        operationNames.Add("FsRead.read_text");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.FsReadTextAsync)
                        operationNames.Add("FsRead.read_text_async");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.FsWriteText)
                        operationNames.Add("FsWrite.write_text");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.FsWriteTextAsync)
                        operationNames.Add("FsWrite.write_text_async");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.HttpGetTextAsync)
                        operationNames.Add("HttpClient.get_text_async");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.ProcessRunTextAsync)
                        operationNames.Add("ProcessRunner.run_text_async");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.ConfigGetText)
                        operationNames.Add("Config.get_text");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.ConfigGetSecretText)
                        operationNames.Add("Config.get_secret_text");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.SecretsRevealText)
                        operationNames.Add("Secrets.reveal_text");
                    else if (intrinsic.Intrinsic == BuiltinIntrinsic.LoggerInfo)
                        operationNames.Add("Logger.info");
                    foreach (var argument in intrinsic.Arguments)
                        VisitExpression(argument);
                    break;
                case TypedDatabaseCallExpr databaseCall:
                    operationNames.Add(databaseCall.Operation.Kind switch
                    {
                        CheckedDatabaseOperationKind.QueryOne => "DbRead.query_one",
                        CheckedDatabaseOperationKind.Execute => "DbWrite.execute",
                        CheckedDatabaseOperationKind.TransactionExecute => "Transaction.execute",
                        _ => throw new InvalidOperationException("Unknown checked database operation")
                    });
                    VisitExpression(databaseCall.Receiver);
                    VisitExpression(databaseCall.Parameters);
                    break;
                case TypedTransactionCommitExpr:
                    operationNames.Add("Transaction.commit");
                    break;
                case TypedBuiltinConstructExpr builtin:
                    foreach (var argument in builtin.Arguments)
                        VisitExpression(argument);
                    break;
                case TypedUnionConstructExpr union:
                    foreach (var argument in union.Arguments)
                        VisitExpression(argument);
                    break;
                case TypedStructConstructExpr structure:
                    foreach (var field in structure.Fields)
                        VisitExpression(field.Value);
                    break;
                case TypedFieldAccessExpr field:
                    VisitExpression(field.Target);
                    break;
                case TypedMatchExpr match:
                    VisitExpression(match.Value);
                    foreach (var arm in match.Arms)
                        VisitExpression(arm.Body);
                    break;
            }
        }
    }
}
