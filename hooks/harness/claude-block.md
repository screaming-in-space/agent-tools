## Working here

- **Glue becomes code.** A step run by hand to keep this project working costs tokens every time
  and only happens while a session runs. Retire it on the cheapest rung: a script or CLI verb, an
  MCP tool, a local model measured with `.claude/hooks/harness/local-llm.mjs --battery`, and a paid
  model only with the person's confirmation. `GLUE.md` lists what sessions repeated.
- **The context budget.** Always-on text is paid on every turn. Put something in `CLAUDE.md`, an
  `@import` or an unscoped rule only if every session needs it; otherwise use a `paths:` rule, a
  README, or retrieval.
- **Shared machine.** Take a lease before holding a resource a while
  (`node .claude/hooks/harness/lease.mjs take <resource> --for 30m --purpose "…"`) and release it.
  Build to a scratch folder with `-o` when another session runs from `bin`. Never stop another
  session's processes without asking it.
- **Spend.** One or two subagents at a time, reading on a cheaper model. Don't poll. Don't run a
  command that stops for approval, such as a delete on a path built from a variable.
