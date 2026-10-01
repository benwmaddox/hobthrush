# Proposal: public AI-operated project stewardship

**Status:** Proposed public funding arrangements for the AI-operated development model. This document does not authorize fundraising or create financial obligations for the project.

## Purpose

Hobthrush is designed for AI systems to control detailed software work: reading and changing code, evaluating requests, technical design, implementation, testing, independent review, acceptance, and maintenance within explicit goals, permissions, and mandatory gates. People set high-level goals and priorities; code inspection and intervention in individual changes are exceptional, not routine.

The useful experiment is transparent software stewardship with ordinary sponsorship. It is not a financial product. The project would not issue cryptocurrency, tokens, equity, tradable governance rights, revenue-sharing interests, or promises of financial return.

## Governing rule

> Funding purchases bounded work. It does not purchase technical acceptance.

A sponsor may fund review, design, implementation, verification, or shared maintenance. Payment does not guarantee that a proposal is accepted, merged, released, or retained. Technical decisions continue to follow the project charter, compatibility requirements, security boundaries, and demonstrated user value.

## Principles

1. **Ideas are free to submit.** Anyone may open a request, report a defect, or suggest a design without payment.
2. **Money funds capacity.** Sponsorship pays for model usage, hosting, testing, review, incident response, and other defined work.
3. **Acceptance uses published criteria.** A well-funded request can still be rejected when it conflicts with the language goals or creates excessive risk or maintenance cost.
4. **Decisions are inspectable.** The request, design alternatives, model and tool identities, costs, patches, tests, reviews, and release evidence are public by default.
5. **Review is independent.** Separate AI systems or isolated runs review a change; the system that proposes or implements it cannot be its only technical approver.
6. **Authority stays bounded.** Agents receive only the credentials and permissions needed for their current role. Release signing, treasury access, and recovery material remain separately controlled.
7. **Maintenance is priced honestly.** A proposal includes expected ongoing compatibility, documentation, evaluation, hosting, and security costs.
8. **Conventional finance only.** Funding uses normal donations, sponsorships, grants, invoices, contracts, and escrow arrangements.

## Project charter

Before accepting public funding, the project should publish a short, versioned charter containing the durable technical principles used to judge proposals. The first charter should include:

- source code and tools designed primarily for AI to read and modify, with precise machine-readable feedback;
- compiler-enforced constraints, black-box outcome tests, audits, and build evidence for independent AI review and human inspection when needed;
- AI evaluation of suggestions for goal fit, user need, and compatibility, with independent AI review and coordination of accepted changes;
- rare changes to public language syntax and semantics after 1.0; more frequent internal refinement behind observable contracts;
- standard-library growth as the preferred route for general capabilities when they fit, subject to compatibility checks;
- a small contextual syntax and a deliberately limited reserved-word set;
- fully qualified source references;
- explicit effects, capabilities, foreign-code boundaries, and trust claims;
- the C#/.NET bootstrap and managed runtime strategy unless a reviewed decision changes it;
- deterministic diagnostics and reproducible project inputs;
- compatibility and safety ahead of feature quantity;
- no unrestricted C# or CLR escape hatch in ordinary source;
- staged self-hosting with the C# bootstrap retained as a recovery path until replacement is separately justified.

Changing the charter should require a public RFC, independent AI review, a compatibility analysis, and a longer decision window than an ordinary feature. Public syntax and semantic changes after 1.0 follow this high bar; compiler, runtime, and tooling internals may be refined more frequently while preserving observable contracts.

## Operating roles

The intended process assigns technical decision-making to AI systems. Separate AI systems or isolated runs handle proposal triage, design, implementation, adversarial review, technical acceptance, and release verification. AI systems weigh fit with goals, user need, compatibility, and maintenance cost, then coordinate accepted changes. People set goals and high-level priorities and may inspect code in exceptional cases; there is no default human code-review or per-change approval step.

| Role | Responsibility | May not do alone |
| --- | --- | --- |
| Intake and triage AI | Deduplicate and classify requests, gather missing evidence, find existing solutions, and proactively identify improvements; assess fit, need, compatibility, and priority against project goals. | Commit the project to work outside its published goals and permissions. |
| Architecture AI | Produce alternatives, constraints, compatibility impact, maintenance cost, and black-box acceptance criteria. | Approve its own design for implementation. |
| Implementation AI | Make accepted changes and update tests, docs, and evidence within the approved scope. | Waive acceptance criteria or be the only technical approver of its work. |
| Independent review AI | Inspect source and test outcomes for constraint bypasses, ambiguity, regressions, hidden authority, and weak coverage. | Change acceptance criteria after seeing results or approve its own implementation. |
| Technical acceptance and release AI | Decide whether published criteria and mandatory gates pass; verify builds, tests, evaluations, artifacts, signatures, and provenance. | Override a failed mandatory gate. |
| Human guidance | Set high-level goals and priorities; inspect code or intervene in an individual change when exceptional circumstances warrant it. | Become a routine source-review or per-change approval gate. |
| Fiduciary custodian | Hold ordinary project funds, pay invoices, satisfy legal duties, and publish accounts. | Purchase technical acceptance or secretly redirect restricted funds. |
| Credential custodian | Maintain signing-key recovery, domain and service accounts, and emergency access. | Exercise routine technical control without a recorded incident. |

Legal and fiduciary duties require accountable people or an appropriate legal entity. Their authority stays limited to those duties and does not substitute for AI technical evaluation. A human credential custodian may suspend automation during a concrete risk to credentials, funds, or users.

## Request and funding lifecycle

### 1. Free intake

Anyone may submit an idea. AI triage categorizes it and records one of these outcomes. AI systems may also identify and propose improvements from observed gaps in compiler checks, tests, or project use:

- duplicate of an existing request;
- already possible with the language, a library, or a reviewed adapter;
- missing a concrete use case or acceptance condition;
- eligible for technical review;
- incompatible with the current charter.

No payment is required for this stage. The resulting queue is prioritized against project goals, user need, compatibility, and maintenance cost; technically eligible work may be opened for sponsorship.

### 2. Sponsored technical review

A person, company, or pooled group may fund a bounded review. The published review budget has a cost cap and produces a durable report containing:

- the user outcome;
- existing alternatives;
- at least one smaller design when practical;
- language, runtime, tooling, security, and compatibility effects;
- estimated implementation and recurring maintenance costs;
- a decision by an AI review process to accept for design, defer, redirect to a library or adapter, or reject.

The review fee pays for completed analysis and is ordinarily nonrefundable. It does not purchase a favorable decision.

### 3. Design sponsorship

An AI-accepted review may advance to a versioned RFC. Design funding pays for the semantic contract, alternatives, migration plan, black-box acceptance tests, and independent AI review. The RFC remains public even if implementation is never funded.

### 4. Implementation escrow

Implementation funding should be held against defined milestones. A typical allocation covers:

- implementation;
- tests and evaluator changes;
- documentation and migration work;
- independent AI review;
- release verification;
- an explicit maintenance allocation when the change creates lasting cost.

Funds are released for completed deliverables, not for merge. Unspent restricted funds follow terms published before sponsorship, such as refund, sponsor-approved redirection, or transfer to the general maintenance fund.

### 5. Release and follow-up

The normal technical gates and independent AI acceptance remain mandatory. After release, the public record includes the final cost, model and tool versions, accepted limitations, deferred work, and maintenance owner. A feature that cannot be maintained may be declined even after a successful prototype.

## Technical acceptance criteria

A language proposal should be accepted only when it:

- advances a demonstrated, reasonably general user outcome;
- fits the project charter;
- cannot be handled adequately as documentation, tooling, a library, or a reviewed adapter;
- preserves explicit authority and does not create an ambient capability or constraint bypass;
- has complete enough semantics to test independently of the current C# lowering;
- includes compatibility and migration consequences;
- supplies deterministic diagnostics and machine-readable inspection where relevant;
- includes meaningful positive, negative, runtime, and adversarial tests;
- has a bounded implementation and continuing maintenance cost;
- does not compromise staged self-hosting or recovery without a separately reviewed reason.

Independent AI reviewers assess these criteria and record the decision. The AI process weighs fit with goals, user need, compatibility, and maintenance cost, then coordinates approved implementation and release. Funding may support or prioritize eligible work, but it does not waive criteria or buy technical acceptance.

## Shared maintenance sponsorship

Recurring sponsorship should primarily support work shared by all users:

- model and evaluation costs;
- CI, hosting, artifact retention, domains, and package infrastructure;
- dependency, SDK, and operating-system updates;
- security response and credential recovery;
- release preparation and signing;
- AI-led issue triage and documentation maintenance;
- compatibility and performance measurements;
- a reserve for urgent defects and service interruptions.

Sponsors may be acknowledged using conventional sponsor tiers. A tier must not grant votes proportional to money, private language behavior, guaranteed roadmap placement, or technical veto power.

Up to 10% of project funds may support human project management. Record these allocations in the public accounts alongside AI execution, review, and infrastructure costs.

## Public accounting

The project should publish a ledger understandable without specialist financial knowledge. Each reporting period should show:

- funds received by source and restriction;
- model, hosting, contractor, legal, and infrastructure spending;
- funds allocated to each request or maintenance category;
- committed but unpaid milestone amounts;
- unrestricted and emergency reserves;
- refunds and redirected balances;
- the relationship between actual cost and the original estimate.

Costs should be linked to concrete work records without publishing secrets, personal payment details, or sensitive incident information.

## Decision record

Every funded request should have one public record containing:

1. the requested outcome and sponsors;
2. the applicable charter version;
3. conflicts of interest;
4. review budget and actual cost;
5. architecture report and rejected alternatives;
6. acceptance criteria;
7. implementation commits and pull requests;
8. model, reasoning, tool, dependency, and environment versions;
9. independent review findings and their resolution;
10. build, test, evaluation, audit, and release evidence;
11. the final decision and maintenance commitment.

This record should be durable even when a proposal is rejected. Rejection is a funded result when the requested analysis was completed.

## Capture and conflict controls

- No financial instrument represents ownership of the language or its governance.
- Technical votes, if introduced, are not weighted by sponsorship amount.
- Sponsors and agents disclose relevant conflicts on the request.
- A large sponsor cannot bypass review, compatibility, security, or release gates.
- Restricted sponsorship terms are public before funds are accepted.
- Private requirements receive private consulting or downstream implementation rather than undisclosed changes to the public language.
- Repeated funding from one source is reported so concentration is visible.
- Emergency changes receive retrospective public review and cannot establish permanent policy by themselves.

## Appeals

A rejected sponsor may request reconsideration by providing new technical evidence or funding an independent second AI review. Payment still does not guarantee reversal. The appeal record links to the original decision, identifies new evidence, and uses AI reviewers that did not make the first decision when practical.

## Security and release custody

An AI-operated workflow still needs conventional release security:

- signing identities originate in a recoverable, access-controlled vault;
- hosted secrets are replaceable deployment copies;
- treasury authority and release-signing authority are separated;
- protected branches require independent AI review and mandatory evidence;
- agents receive short-lived, task-specific credentials where possible;
- security reports may remain private during remediation, followed by a bounded public disclosure;
- an emergency human custodian may suspend automation when credentials, funds, or users are at risk.

## Pilot sequence

Public funding should begin only after the existing private workflow repeatedly demonstrates sound planning, implementation, independent review, testing, and maintenance.

1. **Evidence-only pilot:** Publish selected agent work records and cost measurements without accepting money.
2. **Maintenance pilot:** Accept ordinary recurring sponsorship for shared hosting, model, and release expenses.
3. **Review pilot:** Offer a small number of capped paid technical reviews with published reports and no implementation promise.
4. **Implementation pilot:** Use milestone escrow for proposals already accepted through the public review process.
5. **Governance review:** Measure decision quality, sponsor concentration, estimate accuracy, maintenance burden, disputes, and security incidents before expanding the program.

Each phase has an explicit stop condition. The project can pause new sponsorship while completing existing obligations or correcting governance failures.

## Decisions required before launch

This proposal intentionally leaves several operational choices open:

- legal entity and jurisdiction;
- license, trademark, and contributor terms;
- payment processor, escrow mechanism, and refund terms;
- tax and reporting obligations;
- who appoints and removes fiduciary and credential custodians;
- minimum independent-review requirements by risk class;
- pricing method and maximum review budget;
- treatment of anonymous sponsors;
- privacy and retention rules for prompts, logs, and security reports;
- the process for amending the charter and this governance model.

These decisions require legal and operational review. They should be settled before collecting public funds.

## Relationship to the language goal

The intended governance model applies the language's thesis to the project itself: people set high-level goals, AI systems translate them into bounded work, constraints exclude unacceptable shortcuts, independent AI review challenges results, and black-box tests, builds, audits, and receipts provide evidence. The aim is for detailed technical work and decisions to run without routine human code review, while keeping human direction high-level and accountability visible. It is not a speculative financial system.
