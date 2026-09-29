using System.Text.Json;

namespace Lang.OutcomeEval;

internal sealed record EvaluationOptions(
    string CompilerPath,
    string ResultsDirectory,
    string AgentModel,
    string AgentRevision,
    string AgentTool,
    bool VerifySeeds,
    IReadOnlyDictionary<string, string> CandidateOverrides);

internal sealed record CorpusManifest(
    int SchemaVersion,
    IReadOnlyList<EvaluationScenario> Scenarios,
    string Sha256);

internal sealed record EvaluationScenario(
    string Id,
    string Version,
    string CandidateRoot,
    string? StarterRoot,
    string TaskFile,
    IReadOnlyList<string> CheckIds,
    IReadOnlyList<EvaluationMutation> Mutations,
    JsonElement? Smoke,
    JsonElement? FixtureAgent);

internal sealed record EvaluationMutation(
    string Id,
    string Kind,
    IReadOnlyList<string> Files,
    IReadOnlyList<ExactReplacement> ReplaceExactly,
    string ExpectedCheckId);

internal sealed record ExactReplacement(string Path, string Old, string New);

internal sealed record CandidateAgentIdentity(
    string IdentityKind,
    string Model,
    string Revision,
    string Tool);

internal sealed record HarnessIdentity(
    string Version,
    string DotnetSdkVersion,
    string OperatingSystem,
    string Architecture);

internal sealed record CompilerIdentity(string FileName, string Sha256);

internal sealed record CorpusIdentity(string Path, string Sha256, int SchemaVersion, int ScenarioCount);

internal sealed record EvidenceFile(string Path, string Sha256);

internal sealed record CommandExecution(
    string Id,
    string Command,
    int? ExitCode,
    bool TimedOut,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated,
    EvidenceFile Stdout,
    EvidenceFile Stderr);

internal sealed record AcceptanceCheck(
    string Id,
    bool Passed,
    string Detail,
    IReadOnlyList<string> EvidencePaths);

internal sealed record ArtifactIdentity(string Path, string Sha256, long Length);

internal sealed record ScenarioResult(
    string Id,
    string Version,
    string CandidateRootLabel,
    string CandidateSha256,
    bool Passed,
    IReadOnlyList<CommandExecution> Commands,
    IReadOnlyList<AcceptanceCheck> Checks,
    IReadOnlyList<ArtifactIdentity> Artifacts,
    JsonElement? FixtureAgent);

internal sealed record SeedResult(
    string ScenarioId,
    string Id,
    string Kind,
    string CandidateRootLabel,
    string CandidateSha256,
    string ExpectedCheckId,
    bool Detected,
    IReadOnlyList<string> ObservedCheckIds,
    string EvidencePath);

internal sealed record ResultsDocument(
    int SchemaVersion,
    HarnessIdentity Harness,
    CandidateAgentIdentity CandidateAgent,
    CompilerIdentity Compiler,
    CorpusIdentity Corpus,
    bool SeedVerificationRequested,
    string SeedVerificationStatus,
    bool ReferencesPassed,
    bool SeedsPassed,
    bool Passed,
    IReadOnlyList<ScenarioResult> Scenarios,
    IReadOnlyList<SeedResult> Seeds);

internal sealed record CapturedCommand(
    int? ExitCode,
    bool TimedOut,
    byte[] StandardOutput,
    byte[] StandardError,
    bool StandardOutputTruncated,
    bool StandardErrorTruncated);

internal sealed class EvaluationConfigurationException(string message, Exception? inner = null)
    : Exception(message, inner);
