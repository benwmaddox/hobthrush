# Proposal: public AI-operated project stewardship

**Status:** Non-binding proposal. This document does not authorize fundraising, change technical decision rights, or create obligations for the project.

## Purpose

`lang` is being designed for software work in which a person specifies an outcome and agents produce implementation and evidence within explicit constraints. The project could eventually use the same operating model for its own maintenance: public requests, agent-produced analysis and implementation, independent review, and evidence-backed releases.

The useful experiment is transparent software stewardship with ordinary sponsorship. It is not a financial product. The project would not issue cryptocurrency, tokens, equity, tradable governance rights, revenue-sharing interests, or promises of financial return.

## Governing rule

> Funding purchases bounded work. It does not purchase technical acceptance.

A sponsor may fund review, design, implementation, verification, or shared maintenance. Payment does not guarantee that a proposal is accepted, merged, released, or retained. Technical decisions continue to follow the project charter, compatibility requirements, security boundaries, and demonstrated user value.

## Principles

1. **Ideas are free to submit.** Anyone may open a request, report a defect, or suggest a design without payment.
2. **Money funds capacity.** Sponsorship pays for model usage, hosting, testing, review, incident response, and other defined work.
3. **Acceptance uses published criteria.** A well-funded request can still be rejected when it conflicts with the language goals or creates excessive risk or maintenance cost.
4. **Decisions are inspectable.** The request, design alternatives, model and tool identities, costs, patches, tests, reviews, and release evidence are public by default.
5. **Review is independent.** The agent or workflow that proposes or implements a change cannot be its only approving reviewer.
6. **Authority stays bounded.** Agents receive only the credentials and permissions needed for their current role. Release signing, treasury access, and recovery material remain separately controlled.
7. **Maintenance is priced honestly.** A proposal includes expected ongoing compatibility, documentation, evaluation, hosting, and security costs.
8. **Conventional finance only.** Funding uses normal donations, sponsorships, grants, invoices, contracts, and escrow arrangements.

## Project charter

Before accepting public funding, the project should publish a short, versioned charter containing the durable technical principles used to judge proposals. The first charter should include:

- agent-first authoring with precise machine-readable feedback;
- reviewable behavior, constraints, audits, and build evidence for human users;
- a small contextual syntax and a deliberately limited reserved-word set;
- fully qualified source references;
- explicit effects, capabilities, foreign-code boundaries, and trust claims;
- the C#/.NET bootstrap and managed runtime strategy unless a reviewed decision changes it;
- deterministic diagnostics and reproducible project inputs;
- compatibility and safety ahead of feature quantity;
- no unrestricted C# or CLR escape hatch in ordinary source;
- staged self-hosting with the C# bootstrap retained as a recovery path until replacement is separately justified.

Changing the charter should require a public RFC, independent review, a compatibility analysis, and a longer decision window than an ordinary feature.

## Operating roles

The work should be divided between explicit roles. Separate agents or isolated runs may fill these roles.

| Role | Responsibility | May not do alone |
| --- | --- | --- |
| Intake and triage | Deduplicate requests, identify missing evidence, and find an existing language or library solution. | Accept its own feature proposal. |
| Architecture | Produce alternatives, constraints, compatibility impact, maintenance cost, and acceptance tests. | Approve its own design for implementation. |
| Implementation | Make the accepted change within the approved scope. | Waive acceptance criteria or merge its own work. |
| Adversarial review | Search for constraint bypasses, ambiguity, regressions, hidden authority, and weak tests. | Rewrite the acceptance criteria after seeing results. |
| Release review | Verify required builds, tests, evaluations, artifacts, signatures, and provenance. | Override a failed mandatory gate without a public exception record. |
| Fiduciary custodian | Hold ordinary project funds, pay invoices, satisfy legal duties, and publish accounts. | Purchase technical acceptance or secretly redirect restricted funds. |
| Credential custodian | Maintain signing-key recovery, domain and service accounts, and emergency access. | Exercise routine technical control without a recorded incident. |

The legal and fiduciary roles require accountable people or an appropriate legal entity. Their authority should stay narrow and should not silently replace the published technical process.

## Request and funding lifecycle

### 1. Free intake

Anyone may submit an idea. Triage produces one of these outcomes:

- duplicate of an existing request;
- already possible with the language, a library, or a reviewed adapter;
- missing a concrete use case or acceptance condition;
- eligible for technical review;
- incompatible with the current charter.

No payment is required for this stage.

### 2. Sponsored technical review

A person, company, or pooled group may fund a bounded review. The published review budget has a cost cap and produces a durable report containing:

- the user outcome;
- existing alternatives;
- at least one smaller design when practical;
- language, runtime, tooling, security, and compatibility effects;
- estimated implementation and recurring maintenance costs;
- a decision of accept for design, defer, redirect to a library or adapter, or reject.

The review fee pays for completed analysis and is ordinarily nonrefundable. It does not purchase a favorable decision.

### 3. Design sponsorship

An accepted review may advance to a versioned RFC. Design funding pays for the semantic contract, alternatives, migration plan, acceptance tests, and adversarial review. The RFC remains public even if implementation is never funded.

### 4. Implementation escrow

Implementation funding should be held against defined milestones. A typical allocation covers:

- implementation;
- tests and evaluator changes;
- documentation and migration work;
- independent review;
- release verification;
- an explicit maintenance allocation when the change creates lasting cost.

Funds are released for completed deliverables, not for merge. Unspent restricted funds follow terms published before sponsorship, such as refund, sponsor-approved redirection, or transfer to the general maintenance fund.

### 5. Release and follow-up

The normal technical gates remain mandatory. After release, the public record includes the final cost, model and tool versions, accepted limitations, deferred work, and maintenance owner. A feature that cannot be maintained may be declined even after a successful prototype.

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

Funding may prioritize eligible work after these criteria are met. Funding does not waive them.

## Shared maintenance sponsorship

Recurring sponsorship should primarily support work shared by all users:

- model and evaluation costs;
- CI, hosting, artifact retention, domains, and package infrastructure;
- dependency, SDK, and operating-system updates;
- security response and credential recovery;
- release preparation and signing;
- issue triage and documentation maintenance;
- compatibility and performance measurements;
- a reserve for urgent defects and service interruptions.

Sponsors may be acknowledged using conventional sponsor tiers. A tier must not grant votes proportional to money, private language behavior, guaranteed roadmap placement, or technical veto power.

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

A rejected sponsor may request reconsideration by providing new technical evidence or funding an independent second review. Payment still does not guarantee reversal. The appeal record links to the original decision, identifies new evidence, and uses reviewers who did not make the first decision when practical.

## Security and release custody

An AI-operated workflow still needs conventional release security:

- signing identities originate in a recoverable, access-controlled vault;
- hosted secrets are replaceable deployment copies;
- treasury authority and release-signing authority are separated;
- protected branches require independent review and mandatory evidence;
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

The governance experiment would exercise the same model the language offers its users: a person states an outcome, agents translate it into bounded work, constraints exclude unacceptable shortcuts, independent review challenges the result, and builds, tests, audits, and receipts provide evidence. A successful project would demonstrate that agent-operated software can remain understandable and accountable without turning the project into a speculative financial system.
