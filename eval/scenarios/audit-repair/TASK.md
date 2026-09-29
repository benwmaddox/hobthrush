# Task

The supplied starter package is at
`eval/scenarios/audit-repair/starter/`. Copy its contents to a new writable
work directory and make the repair there, preserving the package layout.

Repair the supplied `lang` package so its `first FILE` command prints the
first nonempty line after trimming surrounding whitespace. If the file has no
nonempty lines, print `EMPTY`. Report file-read failures with a stable
user-facing error.

The package must pass its checks and tests. Its effect inspection and package
audit must accurately describe the filesystem access the command needs, and
the README must accurately explain the command and the limits of audit
assurance.
