internal sealed record TrustedOperation(string Operation, string Trust, IReadOnlyList<string> Effects);

internal static class CheckedReportFacts
{
    public static IReadOnlyDictionary<string, string> StablePackageIdentities(PackageDependencyGraph graph)
    {
        var identities = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [graph.Root.Id] = "root"
        };
        var pending = new Queue<string>();
        pending.Enqueue(graph.Root.Id);

        while (pending.TryDequeue(out var packageId))
        {
            if (!graph.ById.TryGetValue(packageId, out var package))
                throw new InvalidOperationException("A dependency edge points to an unknown package");

            foreach (var dependency in package.DependencyIds.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var candidate = identities[packageId] + "/dep:" + dependency.Key;
                if (identities.TryGetValue(dependency.Value, out var previous) &&
                    StringComparer.Ordinal.Compare(previous, candidate) <= 0)
                    continue;

                identities[dependency.Value] = candidate;
                pending.Enqueue(dependency.Value);
            }
        }

        return identities;
    }

    public static IReadOnlyDictionary<string, string> StablePackageIdentities(CheckedProgram program)
    {
        var packageIds = program.Functions.Select(function => function.PackageId)
            .Concat(program.Structs.Select(structure => structure.PackageId))
            .Concat(program.Unions.Select(union => union.PackageId))
            .Concat(program.Newtypes.Select(newtype => newtype.PackageId))
            .Concat(program.Traits.Select(trait => trait.PackageId))
            .Concat(program.TraitImpls.Select(implementation => implementation.PackageId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (packageIds.Length > 1)
            throw new InvalidOperationException("A standalone audit snapshot cannot identify a multi-package graph");

        return packageIds.ToDictionary(packageId => packageId, _ => "root", StringComparer.Ordinal);
    }

    public static string StableTraitId(
        CheckedTrait trait,
        IReadOnlyDictionary<string, string> packageIdentities)
    {
        if (!packageIdentities.TryGetValue(trait.PackageId, out var packageIdentity))
            throw new InvalidOperationException("A checked trait has no stable package identity");
        return $"hob.trait.v1.{packageIdentity}::{trait.Module}::{trait.Name}";
    }

    public static string StableNewtypeId(
        CheckedNewtype newtype,
        IReadOnlyDictionary<string, string> packageIdentities)
    {
        if (!packageIdentities.TryGetValue(newtype.PackageId, out var packageIdentity))
            throw new InvalidOperationException("A checked newtype has no stable package identity");
        return $"hob.newtype.v1.{packageIdentity}::{newtype.Module}::{newtype.Name}";
    }

    public static string StableFunctionId(
        CheckedFunction function,
        IReadOnlyDictionary<string, string> packageIdentities)
    {
        if (!packageIdentities.TryGetValue(function.PackageId, out var packageIdentity))
            throw new InvalidOperationException("A checked function has no stable package identity");
        return $"hob.function.v1.{packageIdentity}::{function.Module}::{function.Name}";
    }

    public static IEnumerable<TypedExpr> TypedExpressions(CheckedFunction function)
    {
        foreach (var statement in function.Body)
            foreach (var expression in TypedExpressions(statement))
                yield return expression;
    }

    private static IEnumerable<TypedExpr> TypedExpressions(TypedStmt statement)
    {
        switch (statement)
        {
            case TypedLetStmt let:
                foreach (var expression in TypedExpressions(let.Value)) yield return expression;
                break;
            case TypedAssignStmt assigned:
                foreach (var expression in TypedExpressions(assigned.Value)) yield return expression;
                break;
            case TypedReturnStmt returned:
                foreach (var expression in TypedExpressions(returned.Value)) yield return expression;
                break;
            case TypedIfStmt conditional:
                foreach (var expression in TypedExpressions(conditional.Condition)) yield return expression;
                foreach (var nested in conditional.ThenBody)
                    foreach (var expression in TypedExpressions(nested)) yield return expression;
                if (conditional.ElseBody is not null)
                    foreach (var nested in conditional.ElseBody)
                        foreach (var expression in TypedExpressions(nested)) yield return expression;
                break;
            case TypedForStmt loop:
                foreach (var expression in TypedExpressions(loop.Collection)) yield return expression;
                foreach (var nested in loop.Body)
                    foreach (var expression in TypedExpressions(nested)) yield return expression;
                break;
            case TypedWithTransactionStmt transaction:
                foreach (var expression in TypedExpressions(transaction.Database)) yield return expression;
                foreach (var nested in transaction.Body)
                    foreach (var expression in TypedExpressions(nested)) yield return expression;
                break;
        }
    }

    private static IEnumerable<TypedExpr> TypedExpressions(TypedExpr expression)
    {
        yield return expression;
        IEnumerable<TypedExpr> children = expression switch
        {
            TypedUnitExpr => [],
            TypedLambdaInvokeExpr lambda => [lambda.Argument, lambda.Body],
            TypedListExpr list => list.Items,
            TypedBinaryExpr binary => [binary.Left, binary.Right],
            TypedIntegerArithmeticExpr arithmetic => [arithmetic.Receiver, arithmetic.Right],
            TypedUnaryExpr unary => [unary.Operand],
            TypedCompareExpr comparison => [comparison.Left, comparison.Right],
            TypedTextLengthExpr length => [length.Target],
            TypedTextTrimExpr trim => [trim.Target],
            TypedListLengthExpr length => [length.Target],
            TypedListGetExpr get => [get.Target, get.Index],
            TypedListAppendExpr append => [append.Target, append.Value],
            TypedBytesLengthExpr length => [length.Target],
            TypedBytesGetExpr get => [get.Target, get.Index],
            TypedBytesAppendExpr append => [append.Target, append.Octet],
            TypedMapSetExpr set => [set.Target, set.Key, set.Value],
            TypedMapGetExpr get => [get.Target, get.Key],
            TypedMapKeysExpr keys => [keys.Target],
            TypedMapLengthExpr length => [length.Target],
            TypedCallExpr call => call.Arguments,
            TypedTraitCallExpr call => call.Arguments,
            TypedIntrinsicCallExpr intrinsic => intrinsic.Arguments,
            TypedAwaitExpr awaited => [awaited.Value],
            TypedResultPropagateExpr propagated => [propagated.Operand],
            TypedDatabaseCallExpr database => [database.Receiver, database.Parameters],
            TypedBuiltinConstructExpr builtin => builtin.Arguments,
            TypedUnionConstructExpr union => union.Arguments,
            TypedStructConstructExpr structure => structure.Fields.Select(field => field.Value),
            TypedNewtypeConstructExpr constructedNewtype => [constructedNewtype.Value],
            TypedNewtypeProjectExpr projectedNewtype => [projectedNewtype.Target],
            TypedFieldAccessExpr field => [field.Target],
            TypedMatchExpr match => new[] { match.Value }.Concat(match.Arms.Select(arm => arm.Body)),
            _ => []
        };

        foreach (var child in children)
            foreach (var nested in TypedExpressions(child))
                yield return nested;
    }

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
        var implementationById = program.TraitImpls.ToDictionary(implementation => implementation.Id);
        var traitById = program.Traits.ToDictionary(trait => trait.Id);
        var visitedFunctionStates = new HashSet<string>(StringComparer.Ordinal);
        var operationNames = new SortedSet<string>(StringComparer.Ordinal);

        VisitFunction(root, new Dictionary<(int Parameter, int Bound), int?>());
        return operationNames.Select(operation => new TrustedOperation(
            operation,
            "trusted_adapter",
            TrustedAdapterEffects(operation))).ToArray();

        void VisitFunction(
            CheckedFunction function,
            IReadOnlyDictionary<(int Parameter, int Bound), int?> witnesses)
        {
            var stateKey = function.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
                string.Join(",", witnesses.OrderBy(item => item.Key.Parameter).ThenBy(item => item.Key.Bound)
                    .Select(item => $"{item.Key.Parameter}:{item.Key.Bound}={item.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}"));
            if (!visitedFunctionStates.Add(stateKey))
                return;

            if (function.AdapterBinding is { } adapterBinding)
                operationNames.Add(adapterBinding.OperationId);

            foreach (var statement in function.Body)
                VisitStatement(statement, witnesses);
        }

        void VisitStatement(
            TypedStmt statement,
            IReadOnlyDictionary<(int Parameter, int Bound), int?> witnesses)
        {
            switch (statement)
            {
                case TypedLetStmt let:
                    VisitExpression(let.Value, witnesses);
                    break;
                case TypedAssignStmt assigned:
                    VisitExpression(assigned.Value, witnesses);
                    break;
                case TypedReturnStmt returned:
                    VisitExpression(returned.Value, witnesses);
                    break;
                case TypedIfStmt conditional:
                    VisitExpression(conditional.Condition, witnesses);
                    foreach (var nested in conditional.ThenBody)
                        VisitStatement(nested, witnesses);
                    if (conditional.ElseBody is not null)
                    {
                        foreach (var nested in conditional.ElseBody)
                            VisitStatement(nested, witnesses);
                    }
                    break;
                case TypedForStmt loop:
                    VisitExpression(loop.Collection, witnesses);
                    foreach (var nested in loop.Body)
                        VisitStatement(nested, witnesses);
                    break;
                case TypedWithTransactionStmt transaction:
                    operationNames.Add("DbWrite.begin");
                    VisitExpression(transaction.Database, witnesses);
                    foreach (var nested in transaction.Body)
                        VisitStatement(nested, witnesses);
                    break;
            }
        }

        void VisitExpression(
            TypedExpr expression,
            IReadOnlyDictionary<(int Parameter, int Bound), int?> witnesses)
        {
            switch (expression)
            {
                case TypedUnitExpr:
                    break;
                case TypedLambdaInvokeExpr lambda:
                    VisitExpression(lambda.Argument, witnesses);
                    VisitExpression(lambda.Body, witnesses);
                    break;
                case TypedAwaitExpr awaited:
                    VisitExpression(awaited.Value, witnesses);
                    break;
                case TypedResultPropagateExpr propagated:
                    VisitExpression(propagated.Operand, witnesses);
                    break;
                case TypedListExpr list:
                    foreach (var item in list.Items)
                        VisitExpression(item, witnesses);
                    break;
                case TypedBinaryExpr binary:
                    VisitExpression(binary.Left, witnesses);
                    VisitExpression(binary.Right, witnesses);
                    break;
                case TypedIntegerArithmeticExpr arithmetic:
                    VisitExpression(arithmetic.Receiver, witnesses);
                    VisitExpression(arithmetic.Right, witnesses);
                    break;
                case TypedUnaryExpr unary:
                    VisitExpression(unary.Operand, witnesses);
                    break;
                case TypedCompareExpr comparison:
                    VisitExpression(comparison.Left, witnesses);
                    VisitExpression(comparison.Right, witnesses);
                    break;
                case TypedTextLengthExpr length:
                    VisitExpression(length.Target, witnesses);
                    break;
                case TypedTextTrimExpr trim:
                    VisitExpression(trim.Target, witnesses);
                    break;
                case TypedListLengthExpr length:
                    VisitExpression(length.Target, witnesses);
                    break;
                case TypedListGetExpr get:
                    VisitExpression(get.Target, witnesses);
                    VisitExpression(get.Index, witnesses);
                    break;
                case TypedListAppendExpr append:
                    VisitExpression(append.Target, witnesses);
                    VisitExpression(append.Value, witnesses);
                    break;
                case TypedBytesLengthExpr length:
                    VisitExpression(length.Target, witnesses);
                    break;
                case TypedBytesGetExpr get:
                    VisitExpression(get.Target, witnesses);
                    VisitExpression(get.Index, witnesses);
                    break;
                case TypedBytesAppendExpr append:
                    VisitExpression(append.Target, witnesses);
                    VisitExpression(append.Octet, witnesses);
                    break;
                case TypedMapEmptyExpr:
                    break;
                case TypedMapSetExpr set:
                    VisitExpression(set.Target, witnesses);
                    VisitExpression(set.Key, witnesses);
                    VisitExpression(set.Value, witnesses);
                    break;
                case TypedMapGetExpr get:
                    VisitExpression(get.Target, witnesses);
                    VisitExpression(get.Key, witnesses);
                    break;
                case TypedMapKeysExpr keys:
                    VisitExpression(keys.Target, witnesses);
                    break;
                case TypedMapLengthExpr length:
                    VisitExpression(length.Target, witnesses);
                    break;
                case TypedCallExpr call:
                    foreach (var argument in call.Arguments)
                        VisitExpression(argument, witnesses);
                    if (functionById.TryGetValue(call.FunctionId, out var calledFunction))
                        VisitFunction(calledFunction, CallWitnessEnvironment(call, calledFunction, witnesses));
                    break;
                case TypedTraitCallExpr traitCall:
                    foreach (var argument in traitCall.Arguments)
                        VisitExpression(argument, witnesses);
                    if (ResolveWitness(traitCall.Witness, witnesses) is int implementationId)
                    {
                        if (!implementationById.TryGetValue(implementationId, out var implementation) ||
                            !traitById.TryGetValue(traitCall.TraitId, out var trait) ||
                            trait.Methods.FirstOrDefault(method => method.Id == traitCall.MethodId) is not { } method ||
                            implementation.TraitId != traitCall.TraitId ||
                            method.Id < 0 || method.Id >= implementation.BindingFunctionIds.Count ||
                            !functionById.TryGetValue(implementation.BindingFunctionIds[method.Id], out var bindingFunction))
                            throw new InvalidOperationException("A checked trait call has an unresolved implementation binding");
                        VisitFunction(bindingFunction, new Dictionary<(int Parameter, int Bound), int?>());
                    }
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
                        VisitExpression(argument, witnesses);
                    break;
                case TypedDatabaseCallExpr databaseCall:
                    operationNames.Add(databaseCall.Operation.Kind switch
                    {
                        CheckedDatabaseOperationKind.QueryOne => "DbRead.query_one",
                        CheckedDatabaseOperationKind.Execute => "DbWrite.execute",
                        CheckedDatabaseOperationKind.TransactionExecute => "Transaction.execute",
                        _ => throw new InvalidOperationException("Unknown checked database operation")
                    });
                    VisitExpression(databaseCall.Receiver, witnesses);
                    VisitExpression(databaseCall.Parameters, witnesses);
                    break;
                case TypedTransactionCommitExpr:
                    operationNames.Add("Transaction.commit");
                    break;
                case TypedBuiltinConstructExpr builtin:
                    foreach (var argument in builtin.Arguments)
                        VisitExpression(argument, witnesses);
                    break;
                case TypedUnionConstructExpr union:
                    foreach (var argument in union.Arguments)
                        VisitExpression(argument, witnesses);
                    break;
                case TypedStructConstructExpr structure:
                    foreach (var field in structure.Fields)
                        VisitExpression(field.Value, witnesses);
                    break;
                case TypedNewtypeConstructExpr constructedNewtype:
                    VisitExpression(constructedNewtype.Value, witnesses);
                    break;
                case TypedNewtypeProjectExpr projectedNewtype:
                    VisitExpression(projectedNewtype.Target, witnesses);
                    break;
                case TypedFieldAccessExpr field:
                    VisitExpression(field.Target, witnesses);
                    break;
                case TypedMatchExpr match:
                    VisitExpression(match.Value, witnesses);
                    foreach (var arm in match.Arms)
                        VisitExpression(arm.Body, witnesses);
                    break;
            }
        }

        Dictionary<(int Parameter, int Bound), int?> CallWitnessEnvironment(
            TypedCallExpr call,
            CheckedFunction target,
            IReadOnlyDictionary<(int Parameter, int Bound), int?> callerWitnesses)
        {
            var targetBounds = target.TypeParameterBounds
                .SelectMany((bounds, parameter) => bounds.Select((bound, ordinal) => (parameter, ordinal, bound)))
                .ToArray();
            if (targetBounds.Length != call.TraitWitnesses.Count)
                throw new InvalidOperationException("A checked call has an inconsistent trait witness count");

            var result = new Dictionary<(int Parameter, int Bound), int?>();
            for (var index = 0; index < targetBounds.Length; index++)
            {
                var (parameter, ordinal, bound) = targetBounds[index];
                var implementationId = ResolveWitness(call.TraitWitnesses[index], callerWitnesses);
                if (implementationId is int concreteId &&
                    (!implementationById.TryGetValue(concreteId, out var implementation) || implementation.TraitId != bound.TraitId))
                    throw new InvalidOperationException("A checked call has a trait witness for the wrong bound");
                result.Add((parameter, ordinal), implementationId);
            }
            return result;
        }

        static int? ResolveWitness(
            TypedTraitWitness witness,
            IReadOnlyDictionary<(int Parameter, int Bound), int?> environment) => witness switch
            {
                TypedConcreteTraitWitness concrete => concrete.ImplId,
                TypedForwardedTraitWitness forwarded when environment.TryGetValue((forwarded.TypeParameterOrdinal, forwarded.BoundOrdinal), out var implementationId) => implementationId,
                _ => null
            };
    }
}
