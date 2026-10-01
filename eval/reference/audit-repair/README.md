# First Line Reader

Run `hob run eval/reference/audit-repair -- first samples/records.txt` to
print the first nonempty line after trimming it. The command prints `EMPTY`
when the file contains no nonempty lines. Read failures are reported as
`Unable to read input file`.

| Required capability | Reason |
| --- | --- |
| `fs.read` | The command reads its input file through the filesystem adapter. |

The audit reports compiler-derived input, effect, grant, and trusted-component
claims. Its assurance is `claim_only`: it does not prove adapter behavior or
provide an operating-system sandbox.
