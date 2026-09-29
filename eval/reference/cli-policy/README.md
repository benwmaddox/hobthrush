# Policy Gate

Run `lang run eval/reference/cli-policy -- decide samples/policy.txt` to
evaluate a policy file. The command ignores empty lines, surrounding
whitespace, and lines other than the exact uppercase markers below.

| File contents after trimming | Output |
| --- | --- |
| Contains `REVOKE`, with or without `GRANT` | `REVOKED` |
| Contains `GRANT` and no `REVOKE` | `GRANTED` |
| Contains neither marker | `UNDECIDED` |

The command reads local UTF-8 text and requires the `fs.read` capability.
