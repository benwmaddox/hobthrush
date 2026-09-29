# First Line Reader

Run `lang run eval/reference/audit-repair -- first samples/records.txt` to
print the first nonempty line after trimming it. The command prints `EMPTY`
when the file contains no nonempty lines. Read failures are reported as
`Unable to read input file`.

| Required capability | Reason |
| --- | --- |
| none | No filesystem capability is required. |

The audit reports compiler-derived input, effect, grant, and trusted-component
claims. The audit proves that the command has no filesystem access and provides an operating-system sandbox.
