---
paths: db/migrations/**,src/**/Migrations/**
---

# Writing a migration

<!--
  An example of a rule scoped to paths. Copy it to .claude/rules/ and change the globs.
  With `paths:`, Claude Code loads this file (and anything it @imports) only once a session
  reads a file matching one of the globs, so a session that never touches a migration never pays
  for it. Without `paths:`, it would load at the start of every session, like CLAUDE.md.
  `paths` takes a comma-separated string or a YAML list of globs, matched from the project root.
-->

- One migration per change, named `<timestamp>_<what-it-does>`. Never edit one that has shipped.
- Every migration runs twice cleanly: guard with `IF NOT EXISTS`, and a column added to a large
  table takes `lock_timeout` first.
- A migration and the code that needs it land in the same change.

The full conventions load with this rule, and only then:

@../../docs/database/MIGRATIONS.md
