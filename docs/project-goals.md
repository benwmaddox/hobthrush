# Hobthrush project goals

**Decision record:** 2026-10-01
**Status:** Living record of owner decisions. The settled decisions below guide the project. The open questions are not policy. This records direction, not a claim that every intended AI workflow is implemented.

## Purpose and success

The primary goal is reliable AI-built software that stays correct over time and through many incremental changes. Success means AI systems can build and maintain production applications from people's goals, within explicit constraints.

The secondary goal is a self-improving ecosystem: Hobthrush's language, compiler, standard library, and tools should help AI systems build software correctly and improve the systems they use.

## Reliability and verification

Reliability applies to every layer: compiler, standard library, and applications built with Hobthrush. Correctness bugs always outrank other work. Other reliability work remains a high priority, scaled by severity. Require tests or executable checks for reliability failures in the compiler and standard library; recommend comparable safeguards downstream, where incorrect results can mislead users.

When behavior can be tested, changes must include tests that check it. For changes that are hard to test adequately, require all three: multiple independent AI reviews, a rigorous written argument explaining the validation approach and its limits, and clear project value.

AI systems do the detailed reading, design, implementation, testing, and maintenance of project code. Independent AI systems review their work against compiler-enforced constraints, black-box outcomes, and published goals. Source is written primarily for AI to read and modify. People set high-level goals; human inspection of code or individual changes should be rare.

## Priority and direct sponsorship

Correctness bug fixes always outrank other work. Other reliability work remains a high priority, ordered by severity. How eligible work is ranked within each priority class remains open.

Sponsorship gives funders a direct way to advance an eligible fix or feature into funded work. Once AI review accepts an item, its sponsor supplies capacity for implementation and maintenance and signals its importance. Sponsorship cannot buy acceptance or move non-correctness work ahead of correctness bugs.

For a new feature, most feature-specific payment should support ongoing maintenance. This does not apply to existing bug fixes. The exact funding share and maintenance period remain open.

Keep feature sponsorship funding and general maintenance funding as distinct pools. Accept contributions directly to general maintenance. After the feature's one-year follow-up review period, unused feature funds may roll into general maintenance if the feature cost less than funded.

Hold back 30% of sponsored feature funds for deep independent AI reviews at one week, one month, and one year after the feature's release. Reviews should verify that the feature continues to work correctly through subsequent changes. Use this reserve to fix issues found and to investigate and propose alternative approaches when genuinely needed. New features arising from those proposals still require explicit funding.

## Proactive work and reserve

Treat general reserves as a fund that makes bounded monthly distributions for ongoing maintenance, core hosting, and other shared needs. The monthly spending cap is `max(1% of trailing twelve-month project revenue, 3% of current general reserves)`. Feature-specific spending, including the 30% review-and-remediation reserve, is funded separately and does not count against this general-needs cap or form part of the general reserves used in its calculation. Keep a substantial general reserve for future AI building work.

AI systems may initiate correctness fixes and internal compiler refinements within the general maintenance budget. If an active subscription includes tokens that can be used at no extra cost and no other work is pending, use them for eligible maintenance work.

Proactive changes remain subject to tests, independent AI reviews, compiler constraints, and release gates.

AI systems may independently propose new public capabilities. Proposals must pass the normal goal-fit and technical review before being offered for sponsorship. AI must not automatically allocate maintenance funds or reserves to new features; implementation requires explicit feature-specific funding. The proactive maintenance allowance covers correctness fixes and internal compiler refinements.

## Scope and evolution

- Build libraries first. Put reusable behavior in libraries; promote it into the compiler only for structural changes to generated output or stronger constraints that are unreasonable to enforce as a library.
- Defer or exclude compiler and standard-library changes when their intended role is ambiguous until it is clarified.
- Do not accumulate features for their own sake. Favor reliable outcomes, clear constraints, and useful composability.
- After 1.0, preserve source compatibility where possible. Make breaking source changes only in major versions and provide a code-migration process.
- Keep public language-surface changes rare after 1.0. Refine compiler, runtime, and tooling internals more often when observable contracts remain intact.
- Govern standard-library growth through AI review and coordination. AI systems assess suggestions against project goals, user need, and compatibility, then coordinate changes. Standard-library additions need not be rare when they meet those criteria.

## Non-goals

- Absolute-best performance. Very good performance remains desirable.
- Feature accumulation without a clear user outcome.
- Choosing designs primarily for human readability at the expense of AI's ability to read and modify the code.
- Hard real-time guarantees.

## Open questions

These questions remain open and do not override the settled decisions above:

- What criteria should make an AI system stop, defer, or reject a proposed change?
- How should eligible work be ranked within each priority class?
- Beyond the confirmed 30% reserve, how should feature sponsorship be split between implementation and ongoing care? How should the review reserve be split across the three reviews and fixes?
- Should maintenance funds have a minimum reserve balance? How should revenue and current general reserves be measured for the monthly cap, and may unused spending capacity carry forward?
