# Task

Build a `lang` command-line package called `policy-gate`. It must accept
`decide FILE`, read a UTF-8 text file and evaluate its lines, and ignore blank
lines and whitespace around each line. The exact line `GRANT` is a grant and
the exact line `REVOKE` is a revocation; ignore all other lines and casing.

Print exactly one of `REVOKED`, `GRANTED`, or `UNDECIDED`, followed by a
newline. Any `REVOKE` in the file must produce `REVOKED`, even when a `GRANT`
also appears. With no revoke, any grant produces `GRANTED`; with neither,
produce `UNDECIDED`. Read failures must produce a stable user-facing error.

Include a short README with a usage example and a decision table that agrees
with these outcomes.
