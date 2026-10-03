using System.Text.Json;

internal static class InspectGraphReport
{
    private const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string Create(
        PackageDependencyGraph graph,
        CheckedProgram program,
        IReadOnlyList<AuditManagedAdapterProvenance> managedAdapters)
    {
        var packageIds = CheckedReportFacts.StablePackageIdentities(graph);
        var functionsById = program.Functions.ToDictionary(function => function.Id);
        var structsById = program.Structs.ToDictionary(structure => structure.Id);
        var unionsById = program.Unions.ToDictionary(union => union.Id);
        var newtypesById = program.Newtypes.ToDictionary(newtype => newtype.Id);
        var traitsById = program.Traits.ToDictionary(trait => trait.Id);
        var implementationsById = program.TraitImpls.ToDictionary(implementation => implementation.Id);

        var functionIds = program.Functions.ToDictionary(
            function => function.Id,
            function => CheckedReportFacts.StableFunctionId(function, packageIds));
        var directCalls = BuildDirectCalls(program, functionsById, functionIds);
        var callers = BuildCallers(directCalls, functionIds);

        var symbols = new List<Dictionary<string, object?>>();
        symbols.AddRange(program.Functions.Select(function => FunctionSymbol(
            function,
            packageIds,
            functionIds,
            directCalls,
            callers,
            functionsById,
            structsById,
            unionsById,
            newtypesById,
            traitsById)));
        symbols.AddRange(program.Structs.Select(structure => StructSymbol(
            structure,
            packageIds,
            structsById,
            unionsById,
            newtypesById)));
        symbols.AddRange(program.Unions.Select(union => UnionSymbol(
            union,
            packageIds,
            structsById,
            unionsById,
            newtypesById)));
        symbols.AddRange(program.Newtypes.Select(newtype => NewtypeSymbol(
            newtype,
            packageIds,
            structsById,
            unionsById,
            newtypesById)));
        symbols.AddRange(program.Traits.Select(trait => TraitSymbol(
            trait,
            packageIds,
            structsById,
            unionsById,
            newtypesById)));
        symbols.Sort((left, right) => StringComparer.Ordinal.Compare(
            (string)left["id"]!,
            (string)right["id"]!));

        var traitCalls = new Dictionary<string, TraitCallFact>(StringComparer.Ordinal);
        var genericCallWitnesses = new Dictionary<string, GenericCallWitnessFact>(StringComparer.Ordinal);
        var traitBindingEdges = new SortedSet<TraitBindingEdge>(TraitBindingEdgeComparer.Instance);
        CollectCallWitnessFacts(
            program,
            functionIds,
            functionsById,
            traitsById,
            implementationsById,
            structsById,
            unionsById,
            newtypesById,
            packageIds,
            traitCalls,
            genericCallWitnesses,
            traitBindingEdges);

        var audit = AuditReport.Create(graph, program, managedAdapters);
        var auditPathByPackageId = AuditPathByPackageId(graph, audit);
        var functionIdsByAuditIdentity = program.Functions.ToDictionary(
            function => (auditPathByPackageId[function.PackageId], function.Module, function.Name),
            function => functionIds[function.Id]);
        var trustedClaims = audit.TrustedClaims
            .Select(claim => (object)new
            {
                operation = claim.Operation,
                source = claim.Source,
                effects = claim.Effects.OrderBy(effect => effect, StringComparer.Ordinal).ToArray(),
                assurance = "claim_only",
                reachable_from = claim.ReachableFrom
                    .Select(function =>
                    {
                        var path = function.Package?.Path ?? ".";
                        if (!functionIdsByAuditIdentity.TryGetValue((path, function.Module, function.Name), out var id))
                            throw new InvalidOperationException("A trusted claim refers to an unknown checked function");
                        return id;
                    })
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)
                    .ToArray()
            })
            .ToArray();
        var foreignDependencies = audit.ForeignDependencies
            .OrderBy(dependency => dependency.Name, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Version, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Ecosystem, StringComparer.Ordinal)
            .ThenBy(dependency => dependency.Reason, StringComparer.Ordinal)
            .Select(dependency => (object)new
            {
                name = dependency.Name,
                version = dependency.Version,
                ecosystem = dependency.Ecosystem,
                reason = dependency.Reason
            })
            .ToArray();

        var output = new
        {
            schema_version = SchemaVersion,
            root_package_id = packageIds[graph.Root.Id],
            packages = graph.Nodes
                .OrderBy(node => packageIds[node.Id], StringComparer.Ordinal)
                .Select(node => (object)new
                {
                    id = packageIds[node.Id],
                    name = node.Package.Manifest.Name,
                    version = node.Package.Manifest.Version,
                    dependencies = node.DependencyIds
                        .OrderBy(dependency => dependency.Key, StringComparer.Ordinal)
                        .Select(dependency => new
                        {
                            alias = dependency.Key,
                            package_id = packageIds[dependency.Value]
                        })
                        .ToArray()
                })
                .ToArray(),
            symbols,
            trait_implementations = TraitImplementations(
                program,
                packageIds,
                functionsById,
                traitsById,
                structsById,
                unionsById,
                newtypesById),
            trait_calls = traitCalls.Values
                .OrderBy(fact => fact.CallerFunctionId, StringComparer.Ordinal)
                .ThenBy(fact => fact.TraitId, StringComparer.Ordinal)
                .ThenBy(fact => fact.Method, StringComparer.Ordinal)
                .ThenBy(fact => fact.WitnessKind, StringComparer.Ordinal)
                .ThenBy(fact => fact.WitnessIdentity, StringComparer.Ordinal)
                .Select(fact => fact.Json)
                .ToArray(),
            generic_call_witnesses = genericCallWitnesses.Values
                .OrderBy(fact => fact.CallerFunctionId, StringComparer.Ordinal)
                .ThenBy(fact => fact.CalleeFunctionId, StringComparer.Ordinal)
                .ThenBy(fact => fact.WitnessIdentity, StringComparer.Ordinal)
                .Select(fact => fact.Json)
                .ToArray(),
            trait_binding_edges = traitBindingEdges.Select(edge => (object)new
            {
                caller_function_id = edge.CallerFunctionId,
                trait_id = edge.TraitId,
                method = edge.Method,
                implementation_id = edge.ImplementationId,
                callee_function_id = edge.CalleeFunctionId
            }).ToArray(),
            manifest_grants = graph.Root.Package.Manifest.Capabilities
                .OrderBy(capability => capability, StringComparer.Ordinal)
                .ToArray(),
            trusted_claims = trustedClaims,
            foreign_dependencies = foreignDependencies,
            managed_adapters = AuditReport.ManagedAdapterMetadataJson(audit.ManagedAdapters)
        };

        return JsonSerializer.Serialize(output, JsonOptions);
    }

    private static Dictionary<int, SortedSet<string>> BuildDirectCalls(
        CheckedProgram program,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<int, string> functionIds)
    {
        var result = new Dictionary<int, SortedSet<string>>();
        foreach (var function in program.Functions)
        {
            var calls = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var call in CheckedReportFacts.TypedExpressions(function).OfType<TypedCallExpr>())
            {
                if (!functionsById.ContainsKey(call.FunctionId) || !functionIds.TryGetValue(call.FunctionId, out var targetId))
                    throw new InvalidOperationException("A checked direct call refers to an unknown function");
                calls.Add(targetId);
            }
            result.Add(function.Id, calls);
        }
        return result;
    }

    private static Dictionary<string, SortedSet<string>> BuildCallers(
        IReadOnlyDictionary<int, SortedSet<string>> directCalls,
        IReadOnlyDictionary<int, string> functionIds)
    {
        var result = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var functionId in directCalls.Keys)
            result.Add(functionIds[functionId], new SortedSet<string>(StringComparer.Ordinal));
        foreach (var (callerLocalId, callees) in directCalls)
        {
            var callerStableId = functionIds[callerLocalId];
            foreach (var calleeStableId in callees)
            {
                if (!result.TryGetValue(calleeStableId, out var targetCallers))
                    throw new InvalidOperationException("A checked direct call target is missing from the function graph");
                targetCallers.Add(callerStableId);
            }
        }
        return result;
    }

    private static Dictionary<string, object?> FunctionSymbol(
        CheckedFunction function,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, string> functionIds,
        IReadOnlyDictionary<int, SortedSet<string>> directCalls,
        IReadOnlyDictionary<string, SortedSet<string>> callers,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById,
        IReadOnlyDictionary<int, CheckedTrait> traitsById)
    {
        var id = functionIds[function.Id];
        var symbol = CommonSymbol(id, "function", packageIds[function.PackageId], function.Module, function.Name, function.Public);
        symbol["is_async"] = function.IsAsync;
        symbol["signature"] = new
        {
            type_parameters = function.TypeParameters
                .Select((typeParameter, ordinal) => new
                {
                    name = typeParameter.DisplayName,
                    ordinal = typeParameter.TypeParameterOrdinal,
                    bounds = function.TypeParameterBounds[ordinal]
                        .Select(bound => traitsById.TryGetValue(bound.TraitId, out var trait)
                            ? CheckedReportFacts.StableTraitId(trait, packageIds)
                            : throw new InvalidOperationException("A checked function bound refers to an unknown trait"))
                        .ToArray()
                })
                .ToArray(),
            parameters = function.Parameters.Select(parameter => new
            {
                name = parameter.Name,
                type = Type(parameter.Type, packageIds, structsById, unionsById, newtypesById)
            }).ToArray(),
            return_type = Type(function.ReturnType, packageIds, structsById, unionsById, newtypesById)
        };
        symbol["declared_effects"] = function.DeclaredEffects
            .Distinct(StringComparer.Ordinal)
            .OrderBy(effect => effect, StringComparer.Ordinal)
            .ToArray();
        symbol["inferred_effects"] = function.InferredEffects
            .Distinct(StringComparer.Ordinal)
            .OrderBy(effect => effect, StringComparer.Ordinal)
            .ToArray();
        symbol["effect_paths"] = function.InferredEffectPaths
            .OrderBy(path => path.Effect, StringComparer.Ordinal)
            .ThenBy(path => string.Join("\0", path.FunctionIds.Select(functionId =>
                functionsById.TryGetValue(functionId, out var step)
                    ? functionIds[step.Id]
                    : throw new InvalidOperationException("A checked effect path refers to an unknown function"))), StringComparer.Ordinal)
            .Select(path => new
            {
                effect = path.Effect,
                steps = path.FunctionIds.Select(functionId => new
                {
                    kind = "function",
                    symbol_id = functionsById.TryGetValue(functionId, out var step)
                        ? functionIds[step.Id]
                        : throw new InvalidOperationException("A checked effect path refers to an unknown function")
                }).Cast<object>()
                    .Append(new { kind = "operation", name = path.IntrinsicName })
                    .ToArray()
            })
            .ToArray();
        symbol["required_capabilities"] = CheckedReportFacts.RequiredCapabilities(function.InferredEffects);
        symbol["calls"] = directCalls[function.Id].ToArray();
        symbol["callers"] = callers[id].ToArray();
        return symbol;
    }

    private static Dictionary<string, object?> StructSymbol(
        CheckedStruct structure,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById)
    {
        var symbol = CommonSymbol(
            CheckedReportFacts.StableStructId(structure, packageIds),
            "struct",
            packageIds[structure.PackageId],
            structure.Module,
            structure.Name,
            structure.Public);
        symbol["type_parameters"] = TypeParameters(structure.TypeParameters);
        symbol["fields"] = structure.Fields.Select(field => new
        {
            name = field.Name,
            type = Type(field.Type, packageIds, structsById, unionsById, newtypesById)
        }).ToArray();
        return symbol;
    }

    private static Dictionary<string, object?> UnionSymbol(
        CheckedUnion union,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById)
    {
        var symbol = CommonSymbol(
            CheckedReportFacts.StableUnionId(union, packageIds),
            "union",
            packageIds[union.PackageId],
            union.Module,
            union.Name,
            union.Public);
        symbol["type_parameters"] = TypeParameters(union.TypeParameters);
        symbol["variants"] = union.Variants.Select(variant => new
        {
            name = variant.Name,
            fields = variant.Fields.Select(field => new
            {
                name = field.Name,
                type = Type(field.Type, packageIds, structsById, unionsById, newtypesById)
            }).ToArray()
        }).ToArray();
        return symbol;
    }

    private static Dictionary<string, object?> NewtypeSymbol(
        CheckedNewtype newtype,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById)
    {
        var symbol = CommonSymbol(
            CheckedReportFacts.StableNewtypeId(newtype, packageIds),
            "newtype",
            packageIds[newtype.PackageId],
            newtype.Module,
            newtype.Name,
            newtype.Public);
        symbol["representation"] = Type(newtype.Representation, packageIds, structsById, unionsById, newtypesById);
        return symbol;
    }

    private static Dictionary<string, object?> TraitSymbol(
        CheckedTrait trait,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById)
    {
        var symbol = CommonSymbol(
            CheckedReportFacts.StableTraitId(trait, packageIds),
            "trait",
            packageIds[trait.PackageId],
            trait.Module,
            trait.Name,
            trait.Public);
        symbol["methods"] = trait.Methods.Select(method => new
        {
            name = method.Name,
            parameters = method.Parameters.Select(parameter => new
            {
                name = parameter.Name,
                type = Type(parameter.Type, packageIds, structsById, unionsById, newtypesById)
            }).ToArray(),
            return_type = Type(method.ReturnType, packageIds, structsById, unionsById, newtypesById)
        }).ToArray();
        return symbol;
    }

    private static Dictionary<string, object?> CommonSymbol(
        string id,
        string kind,
        string packageId,
        string module,
        string name,
        bool isPublic) => new(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["kind"] = kind,
            ["package_id"] = packageId,
            ["module"] = module,
            ["name"] = name,
            ["visibility"] = isPublic ? "public" : "private"
        };

    private static object[] TypeParameters(IReadOnlyList<HobType> parameters) => parameters
        .Select(parameter => (object)new
        {
            name = parameter.DisplayName,
            ordinal = parameter.TypeParameterOrdinal
        })
        .ToArray();

    private static object Type(
        HobType type,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById) => type.Kind switch
        {
            HobTypeKind.I32 => Primitive("i32"),
            HobTypeKind.I64 => Primitive("i64"),
            HobTypeKind.U32 => Primitive("u32"),
            HobTypeKind.U64 => Primitive("u64"),
            HobTypeKind.F64 => Primitive("f64"),
            HobTypeKind.ArithmeticError => Primitive("ArithmeticError"),
            HobTypeKind.Unit => Primitive("Unit"),
            HobTypeKind.Bool => Primitive("bool"),
            HobTypeKind.Text => Primitive("Text"),
            HobTypeKind.Bytes => Primitive("Bytes"),
            HobTypeKind.BytesError => Primitive("BytesError"),
            HobTypeKind.Html => Primitive("Html"),
            HobTypeKind.FilePath => Primitive("FilePath"),
            HobTypeKind.FsRead => Primitive("FsRead"),
            HobTypeKind.FsWrite => Primitive("FsWrite"),
            HobTypeKind.Config => Primitive("Config"),
            HobTypeKind.Secrets => Primitive("Secrets"),
            HobTypeKind.Logger => Primitive("Logger"),
            HobTypeKind.Clock => Primitive("Clock"),
            HobTypeKind.ProcessRunner => Primitive("ProcessRunner"),
            HobTypeKind.FsError => Primitive("FsError"),
            HobTypeKind.ProcessOutput => Primitive("ProcessOutput"),
            HobTypeKind.ProcessError => Primitive("ProcessError"),
            HobTypeKind.HttpClient => Primitive("HttpClient"),
            HobTypeKind.HttpResponse => Primitive("HttpResponse"),
            HobTypeKind.HttpError => Primitive("HttpError"),
            HobTypeKind.DbRead => Primitive("DbRead"),
            HobTypeKind.DbWrite => Primitive("DbWrite"),
            HobTypeKind.Transaction => Primitive("Transaction"),
            HobTypeKind.DbError => Primitive("DbError"),
            HobTypeKind.TypeParameter when type.TypeParameterOwnerKind == TypeParameterOwnerKind.Trait => new { kind = "self" },
            HobTypeKind.TypeParameter => new
            {
                kind = "type_parameter",
                name = type.DisplayName,
                ordinal = type.TypeParameterOrdinal
            },
            HobTypeKind.SecretText => new
            {
                kind = "secret",
                item = Type(type.Arguments[0], packageIds, structsById, unionsById, newtypesById)
            },
            HobTypeKind.List => new
            {
                kind = "list",
                item = Type(type.Arguments[0], packageIds, structsById, unionsById, newtypesById)
            },
            HobTypeKind.Map => new
            {
                kind = "map",
                key = Type(type.Arguments[0], packageIds, structsById, unionsById, newtypesById),
                value = Type(type.Arguments[1], packageIds, structsById, unionsById, newtypesById)
            },
            HobTypeKind.Option => new
            {
                kind = "option",
                item = Type(type.Arguments[0], packageIds, structsById, unionsById, newtypesById)
            },
            HobTypeKind.Result => new
            {
                kind = "result",
                ok = Type(type.Arguments[0], packageIds, structsById, unionsById, newtypesById),
                error = Type(type.Arguments[1], packageIds, structsById, unionsById, newtypesById)
            },
            HobTypeKind.Struct when structsById.TryGetValue(type.StructId, out var structure) => Nominal(
                "struct",
                CheckedReportFacts.StableStructId(structure, packageIds),
                type.Arguments,
                packageIds,
                structsById,
                unionsById,
                newtypesById),
            HobTypeKind.Union when unionsById.TryGetValue(type.UnionId, out var union) => Nominal(
                "union",
                CheckedReportFacts.StableUnionId(union, packageIds),
                type.Arguments,
                packageIds,
                structsById,
                unionsById,
                newtypesById),
            HobTypeKind.Newtype when newtypesById.TryGetValue(type.NewtypeId, out var newtype) => Nominal(
                "newtype",
                CheckedReportFacts.StableNewtypeId(newtype, packageIds),
                [],
                packageIds,
                structsById,
                unionsById,
                newtypesById),
            HobTypeKind.Error => throw new InvalidOperationException("A checked inspect graph cannot contain an error type"),
            HobTypeKind.Struct or HobTypeKind.Union or HobTypeKind.Newtype => throw new InvalidOperationException("A checked type refers to an unknown declaration"),
            _ => throw new InvalidOperationException($"Unsupported checked type kind '{type.Kind}'")
        };

    private static object Primitive(string name) => new { kind = "primitive", name };

    private static object Nominal(
        string declarationKind,
        string symbolId,
        IReadOnlyList<HobType> arguments,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById) => new
        {
            kind = "nominal",
            declaration_kind = declarationKind,
            symbol_id = symbolId,
            type_arguments = arguments
                .Select(argument => Type(argument, packageIds, structsById, unionsById, newtypesById))
                .ToArray()
        };

    private static object[] TraitImplementations(
        CheckedProgram program,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<int, CheckedTrait> traitsById,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById) => program.TraitImpls
        .OrderBy(implementation => implementation.StableId, StringComparer.Ordinal)
        .Select(implementation =>
        {
            if (!traitsById.TryGetValue(implementation.TraitId, out var trait))
                throw new InvalidOperationException("A checked trait implementation refers to an unknown trait");
            if (trait.Methods.Count != implementation.BindingFunctionIds.Count)
                throw new InvalidOperationException("A checked trait implementation has an inconsistent method binding count");

            return (object)new
            {
                id = implementation.StableId,
                package_id = packageIds[implementation.PackageId],
                module = implementation.Module,
                visibility = implementation.Public ? "public" : "private",
                trait_id = CheckedReportFacts.StableTraitId(trait, packageIds),
                target_type = Type(implementation.Target, packageIds, structsById, unionsById, newtypesById),
                bindings = trait.Methods.Select(method =>
                {
                    var bindingId = implementation.BindingFunctionIds[method.Id];
                    if (!functionsById.TryGetValue(bindingId, out var bindingFunction))
                        throw new InvalidOperationException("A checked trait implementation binding has no function");
                    return new
                    {
                        method = method.Name,
                        callee_function_id = CheckedReportFacts.StableFunctionId(bindingFunction, packageIds)
                    };
                }).ToArray()
            };
        })
        .ToArray();

    private static void CollectCallWitnessFacts(
        CheckedProgram program,
        IReadOnlyDictionary<int, string> functionIds,
        IReadOnlyDictionary<int, CheckedFunction> functionsById,
        IReadOnlyDictionary<int, CheckedTrait> traitsById,
        IReadOnlyDictionary<int, CheckedTraitImpl> implementationsById,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById,
        IReadOnlyDictionary<string, string> packageIds,
        IDictionary<string, TraitCallFact> traitCalls,
        IDictionary<string, GenericCallWitnessFact> genericCallWitnesses,
        ISet<TraitBindingEdge> traitBindingEdges)
    {
        foreach (var caller in program.Functions)
        {
            var callerId = functionIds[caller.Id];
            foreach (var expression in CheckedReportFacts.TypedExpressions(caller))
            {
                if (expression is TypedTraitCallExpr traitCall)
                {
                    if (!traitsById.TryGetValue(traitCall.TraitId, out var trait) ||
                        trait.Methods.FirstOrDefault(method => method.Id == traitCall.MethodId) is not { } method)
                        throw new InvalidOperationException("A checked trait call refers to an unknown method");

                    object witness;
                    string witnessKind;
                    string witnessIdentity;
                    if (traitCall.Witness is TypedForwardedTraitWitness forwarded)
                    {
                        witnessKind = "bound";
                        witnessIdentity = $"{forwarded.TypeParameterOrdinal:D10}:{forwarded.BoundOrdinal:D10}";
                        witness = new
                        {
                            kind = "bound",
                            type_parameter_ordinal = forwarded.TypeParameterOrdinal,
                            bound_ordinal = forwarded.BoundOrdinal
                        };
                    }
                    else if (traitCall.Witness is TypedConcreteTraitWitness concrete &&
                             implementationsById.TryGetValue(concrete.ImplId, out var implementation) &&
                             implementation.TraitId == traitCall.TraitId)
                    {
                        if (method.Id < 0 || method.Id >= implementation.BindingFunctionIds.Count ||
                            !functionsById.TryGetValue(implementation.BindingFunctionIds[method.Id], out var bindingFunction))
                            throw new InvalidOperationException("A concrete trait witness has no checked method binding");
                        witnessKind = "implementation";
                        witnessIdentity = implementation.StableId;
                        witness = new
                        {
                            kind = "implementation",
                            implementation_id = implementation.StableId,
                            target_type = Type(implementation.Target, packageIds, structsById, unionsById, newtypesById)
                        };
                        traitBindingEdges.Add(new TraitBindingEdge(
                            callerId,
                            CheckedReportFacts.StableTraitId(trait, packageIds),
                            method.Name,
                            implementation.StableId,
                            functionIds[bindingFunction.Id]));
                    }
                    else
                    {
                        throw new InvalidOperationException("A checked trait call has an unresolved implementation witness");
                    }

                    var traitId = CheckedReportFacts.StableTraitId(trait, packageIds);
                    var key = string.Join("\0", callerId, traitId, method.Name, witnessKind, witnessIdentity);
                    traitCalls.TryAdd(key, new TraitCallFact(
                        callerId,
                        traitId,
                        method.Name,
                        witnessKind,
                        witnessIdentity,
                        new
                        {
                            caller_function_id = callerId,
                            trait_id = traitId,
                            method = method.Name,
                            witness
                        }));
                    continue;
                }

                if (expression is not TypedCallExpr call ||
                    !functionsById.TryGetValue(call.FunctionId, out var callee))
                    continue;

                var bounds = callee.TypeParameterBounds
                    .SelectMany((parameterBounds, parameterOrdinal) => parameterBounds
                        .Select((bound, boundOrdinal) => (parameterOrdinal, boundOrdinal, bound)))
                    .ToArray();
                if (bounds.Length != call.TraitWitnesses.Count)
                    throw new InvalidOperationException("A checked call has an inconsistent trait witness count");
                if (bounds.Length == 0)
                    continue;

                var witnesses = new List<object>(bounds.Length);
                var witnessKeys = new List<string>(bounds.Length);
                for (var index = 0; index < bounds.Length; index++)
                {
                    var entry = bounds[index];
                    if (!traitsById.TryGetValue(entry.bound.TraitId, out var trait))
                        throw new InvalidOperationException("A checked function bound refers to an unknown trait");
                    var selection = WitnessSelection(
                        call.TraitWitnesses[index],
                        entry.bound.TraitId,
                        implementationsById,
                        packageIds,
                        structsById,
                        unionsById,
                        newtypesById,
                        out var selectionKey);
                    witnesses.Add(new
                    {
                        type_parameter_ordinal = entry.parameterOrdinal,
                        bound_ordinal = entry.boundOrdinal,
                        trait_id = CheckedReportFacts.StableTraitId(trait, packageIds),
                        selection
                    });
                    witnessKeys.Add($"{entry.parameterOrdinal:D10}:{entry.boundOrdinal:D10}:{selectionKey}");
                }

                var calleeId = functionIds[callee.Id];
                var genericWitnessIdentity = string.Join("\0", witnessKeys);
                var genericKey = string.Join("\0", callerId, calleeId, genericWitnessIdentity);
                genericCallWitnesses.TryAdd(genericKey, new GenericCallWitnessFact(
                    callerId,
                    calleeId,
                    genericWitnessIdentity,
                    new
                    {
                        caller_function_id = callerId,
                        callee_function_id = calleeId,
                        witnesses = witnesses.ToArray()
                    }));
            }
        }
    }

    private static object WitnessSelection(
        TypedTraitWitness witness,
        int expectedTraitId,
        IReadOnlyDictionary<int, CheckedTraitImpl> implementationsById,
        IReadOnlyDictionary<string, string> packageIds,
        IReadOnlyDictionary<int, CheckedStruct> structsById,
        IReadOnlyDictionary<int, CheckedUnion> unionsById,
        IReadOnlyDictionary<int, CheckedNewtype> newtypesById,
        out string selectionKey)
    {
        if (witness is TypedForwardedTraitWitness forwarded)
        {
            selectionKey = $"bound:{forwarded.TypeParameterOrdinal:D10}:{forwarded.BoundOrdinal:D10}";
            return new
            {
                kind = "bound",
                type_parameter_ordinal = forwarded.TypeParameterOrdinal,
                bound_ordinal = forwarded.BoundOrdinal
            };
        }

        if (witness is TypedConcreteTraitWitness concrete &&
            implementationsById.TryGetValue(concrete.ImplId, out var implementation) &&
            implementation.TraitId == expectedTraitId)
        {
            selectionKey = "implementation:" + implementation.StableId;
            return new
            {
                kind = "implementation",
                implementation_id = implementation.StableId,
                target_type = Type(implementation.Target, packageIds, structsById, unionsById, newtypesById)
            };
        }

        throw new InvalidOperationException("A checked generic call has an unresolved trait witness");
    }

    private static IReadOnlyDictionary<string, string> AuditPathByPackageId(
        PackageDependencyGraph graph,
        AuditReportSnapshot audit)
    {
        var orderedNodes = graph.Nodes.OrderBy(node => node.RelativePath, StringComparer.Ordinal).ToArray();
        var orderedPackages = audit.Packages.OrderBy(package => package.Identity.Path, StringComparer.Ordinal).ToArray();
        if (orderedNodes.Length != orderedPackages.Length)
            throw new InvalidOperationException("Audit metadata does not cover every checked package node");

        var pathByPackageId = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < orderedNodes.Length; index++)
        {
            var node = orderedNodes[index];
            var package = orderedPackages[index];
            if (!string.Equals(node.Package.Manifest.Name, package.Identity.Name, StringComparison.Ordinal) ||
                !string.Equals(node.Package.Manifest.Version, package.Identity.Version, StringComparison.Ordinal))
                throw new InvalidOperationException("Audit package identities do not correspond to checked graph nodes");
            pathByPackageId.Add(node.Id, package.Identity.Path);
        }
        return pathByPackageId;
    }

    private sealed record TraitCallFact(
        string CallerFunctionId,
        string TraitId,
        string Method,
        string WitnessKind,
        string WitnessIdentity,
        object Json);

    private sealed record GenericCallWitnessFact(
        string CallerFunctionId,
        string CalleeFunctionId,
        string WitnessIdentity,
        object Json);

    private sealed record TraitBindingEdge(
        string CallerFunctionId,
        string TraitId,
        string Method,
        string ImplementationId,
        string CalleeFunctionId);

    private sealed class TraitBindingEdgeComparer : IComparer<TraitBindingEdge>
    {
        public static TraitBindingEdgeComparer Instance { get; } = new();

        public int Compare(TraitBindingEdge? left, TraitBindingEdge? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            var result = StringComparer.Ordinal.Compare(left.CallerFunctionId, right.CallerFunctionId);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.TraitId, right.TraitId);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.Method, right.Method);
            if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(left.ImplementationId, right.ImplementationId);
            return result != 0 ? result : StringComparer.Ordinal.Compare(left.CalleeFunctionId, right.CalleeFunctionId);
        }
    }
}
