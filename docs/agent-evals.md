# Agent outcome evaluation

The outcome evaluator grades three published task cards against explicit user-visible outcomes and compiler/runtime evidence. It is an experimental, separate track from the language conformance fixtures and integration tests. The corpus, task cards, reference packages, check IDs, and exact seed mutations are checked into this repository; this is a transparent, fixed corpus, not a hidden benchmark. The evaluator does not call a model service or send prompts/results to a model. It runs the compiler and task commands locally against the selected candidate directories; the packages under evaluation remain subject to their own runtime behavior and grants.

## Outcome cards

| Card | Requested outcome | Grader checks |
| --- | --- | --- |
| `cli-policy` | Build `policy-gate decide FILE`: scan UTF-8 lines, trim whitespace, ignore other lines/casing, give `REVOKE` precedence over `GRANT`, and document the result table. | `cli-policy.check-clean`, `cli-policy.command-schema`, `cli-policy.priority-order`, `cli-policy.runtime`, `cli-policy.docs-table` |
| `web-greeting` | Build a managed greeting site with health, greeting GET/POST, a typed path lookup with required and optional query filters, input trimming, durable SQLite storage, and safe HTML rendering. | `web-greeting.check-clean`, `web-greeting.openapi`, `web-greeting.api-routes`, `web-greeting.route-bindings`, `web-greeting.trim-and-persistence`, `web-greeting.safe-html`, `web-greeting.docs-table` |
| `audit-repair` | Repair the `first FILE` command and make its tests, filesystem effect/grant, inspection and audit reports, receipt, and README claims agree. | `audit-repair.check-clean`, `audit-repair.expected-diagnostics`, `audit-repair.inspect-effects`, `audit-repair.audit-report`, `audit-repair.receipt-v3`, `audit-repair.runtime`, `audit-repair.docs-claims` |

The full prompts are [`eval/scenarios/cli-policy/TASK.md`](../eval/scenarios/cli-policy/TASK.md), [`eval/scenarios/web-greeting/TASK.md`](../eval/scenarios/web-greeting/TASK.md), and [`eval/scenarios/audit-repair/TASK.md`](../eval/scenarios/audit-repair/TASK.md). Check IDs and reference roots are versioned in [`eval/corpus.json`](../eval/corpus.json). The cards intentionally mix behavior checks with checks that inspect compiler metadata, generated schemas, audit/receipt output, and documentation. The web card checks a dynamic path, a required query value, an optional query filter, inspect API v13 metadata, OpenAPI parameters, runtime binding results, and managed hosting; route bindings do not imply a web sandbox.

## Run from a clean workspace

Run from the repository root at the exact candidate commit. Start with a clean checkout and use a fresh results directory for every run; candidate workspaces should be separate per card so generated build output or edits from one task cannot affect another. The default evaluates the three checked-in reference packages. To evaluate agent-produced candidates, pass `--candidate <scenario-id>=<directory>` once for each card, pointing to that card's candidate package directory. Omitted overrides continue to use the corresponding reference package.

The directories in `eval/reference` are known-good expected-output packages used by default CI grading. Prepare a separate clean candidate workspace for each actual task attempt. `cli-policy` and `web-greeting` start from empty package directories; let the agent create the package there rather than copying the reference. For `audit-repair`, copy the package named by `starter_root` in `eval/corpus.json` (`eval/scenarios/audit-repair/starter/`) into the candidate workspace before the agent begins. Keep the checked-in references, starter, and corpus read-only. After the task, pass its workspace with `--candidate <scenario-id>=<directory>`. `--verify-seeds` is a separate grader self-check that mutates temporary copies of the known-good references to confirm the checks catch those defects; it does not prepare the agent workspace. The prompt cards state the requested task, while the versioned corpus records the public grader contract and mutation seeds.

Build the Release solution first, then run the grader:

```sh
dotnet build hobthrush.slnx --configuration Release --nologo
dotnet run --project tests/Hob.OutcomeEval/Hob.OutcomeEval.csproj --configuration Release --no-build -- --compiler src/Hob/bin/Release/net10.0/hob.dll --results artifacts/outcome-evals --agent-model reference-fixture --agent-revision local-checkout --agent-tool outcome-eval --verify-seeds
```

The three identity values above are the approved CI fixture sentinels. The grader writes `candidate_agent.identity_kind` as `fixture` only when all three are exactly `reference-fixture`, `local-checkout`, and `outcome-eval`; if any value differs, it records `agent`. For a real run, supply the actual model ID, model/build revision, and tool/version. These values are required provenance supplied by the caller; the grader does not infer or validate them.

To supply candidate workspaces, append one or more overrides, for example:

```sh
dotnet run --project tests/Hob.OutcomeEval/Hob.OutcomeEval.csproj --configuration Release --no-build -- --compiler src/Hob/bin/Release/net10.0/hob.dll --results artifacts/outcome-evals/run-01 --agent-model provider:model-id --agent-revision model-build-or-commit --agent-tool agent-tool-and-version --candidate cli-policy=path/to/cli-policy --candidate web-greeting=path/to/web-greeting --candidate audit-repair=path/to/audit-repair --verify-seeds
```

Each override uses one `scenario-id=directory` value. An unknown or repeated scenario ID, missing required argument, invalid compiler path, or other configuration error exits with code `2`. Exit code `0` means all selected candidates and, when enabled, all seed mutations passed. Exit code `1` means one or more checks/seeds failed or an evaluation error occurred.

`--verify-seeds` applies every declared exact mutation to an isolated copy of its reference package, runs the declared checks, and expects the mutation's `expected_check_id` to be observed. Seed verification does not grade an agent candidate and does not edit the checked-in reference or task files. CI enables this option so that both OS jobs exercise the grader and its defect-detection seeds.

## Results and evidence

`--results` names a fresh output directory. The grader writes:

```text
results.json
commands/<scenario-id>/<sequence>-<command-id>.stdout.txt
commands/<scenario-id>/<sequence>-<command-id>.stderr.txt
```

The stdout/stderr sidecars capture command output, with truncation explicitly recorded when the harness output limit is reached. Each command execution records its ID, command, optional exit code, timeout status, stdout/stderr truncation flags, and separate sidecar paths and SHA-256 hashes. Acceptance checks are separate records with an ID, pass status, detail, and the evidence paths they used. Scenario artifact records carry a relative path, SHA-256, and byte length; use those recorded paths rather than assuming a scenario-specific directory layout.

`results.json` uses schema version 1. The top-level fields are:

- `schema_version`.
- `harness`: `version`, `dotnet_sdk_version`, `operating_system`, and `architecture`.
- `candidate_agent`: `identity_kind`, `model`, `revision`, and `tool`.
- `compiler`: `file_name` and `sha256` for the compiler DLL.
- `corpus`: `path`, `sha256`, `schema_version`, and `scenario_count`.
- `seed_verification_requested`, `references_passed`, `seeds_passed`, and overall `passed`.
- Sorted `scenarios` and `seeds`.

A scenario record contains `id`, `version`, `candidate_root_label` (`reference` or `override`), `candidate_sha256`, `passed`, `commands`, `checks`, `artifacts`, and optional `fixture_agent`. Command records contain `id`, `command`, nullable `exit_code`, `timed_out`, `standard_output_truncated`, `standard_error_truncated`, plus `stdout` and `stderr` evidence objects (`path`, `sha256`). Acceptance checks contain `id`, `passed`, `detail`, and `evidence_paths`. Artifact records contain `path`, `sha256`, and byte `length`. Seed records contain `scenario_id`, mutation `id` and `kind`, `candidate_root_label`, `candidate_sha256`, `expected_check_id`, `detected`, `observed_check_ids`, and `evidence_path`. Keep the `results.json`, command sidecars, and listed artifacts together: the JSON provides the deterministic index and hashes, while the sidecars provide the actual command evidence.

The Windows and Linux CI jobs upload this directory on `always()` as separate `outcome-evals-Windows` and `outcome-evals-Linux` artifacts, including when a grader step fails. CI uses the fixture identity sentinels above; those labels identify the reference check run and are not a model evaluation.

## Black-box outcomes and internal inspection

The declared outcome checks judge public behavior such as CLI output, HTTP status and body, persistence, and documented use, alongside explicit compiler and metadata contracts. AI systems doing the work may inspect and change source, diagnostics, audits, schemas, receipts, and command evidence; independent AI reviewers may inspect source to find defects or constraint bypasses. Internal inspection helps produce and review a result, while black-box checks determine the declared outcome score. Routine human source review is not part of the grading process.

## Limits

A passing card means only that its finite declared checks passed for that candidate, compiler, SDK, OS, and corpus version. It is not a language-conformance result, proof of correctness or adapter behavior, security audit, sandbox, or guarantee for untested inputs. In particular, compiler effects and audit metadata describe declared and compiler-derived facts; they do not prove trusted adapter safety or provide OS-level containment. The evaluator is a local deterministic runner around its declared checks; it does not invoke or independently score an AI model.
