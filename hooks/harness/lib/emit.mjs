// emit.mjs - the only way a harness hook puts text in front of the model.
//
// Claude Code caps a hook's context at 10,000 characters. Past that it saves the text to a file
// and injects a 2,000-character preview, and nothing asks the model to read the file. A catch-up
// hook that grows past the cap fails in silence: the session starts, the preview looks like a
// catch-up, and an instruction elsewhere that trusts the hook ("don't re-read the journal")
// hides the loss. So every string goes through here:
//
//   - it is held to its hook's budget (harness.json `budgets`), never above HARD_BUDGET;
//   - it is cut at a line boundary, and the cut ends with a pointer to the full text on disk;
//   - every emission is logged with its size, so context-budget.mjs can say at the next session
//     start which hook was cut.
import fs from 'node:fs';
import path from 'node:path';
import { HARD_BUDGET, cacheDir, loadConfig, log, rel } from './common.mjs';

export function budgetFor(name, config = loadConfig()) {
    const b = Number(config.budgets[name] ?? config.budgets.default);
    return Math.min(Number.isFinite(b) && b > 0 ? b : config.budgets.default, HARD_BUDGET);
}

// Fits text to `budget` characters. Returns { text, cut }. When it cuts, the full text goes to
// `fullPath` and the last line of what is returned says where.
export function fit(text, budget, fullPath, project) {
    if (text.length <= budget) { return { text, cut: false }; }
    let pointer = '';
    try {
        fs.writeFileSync(fullPath, text);
        pointer = `[Cut to fit ${budget} characters. The full text is in ${rel(project, fullPath)}: read it when the work needs the rest.]`;
    } catch {
        pointer = `[Cut to fit ${budget} characters.]`;
    }
    const room = budget - pointer.length - 1;
    const kept = [];
    let used = 0;
    for (const line of text.split('\n')) {
        if (used + line.length + 1 > room) { break; }
        kept.push(line);
        used += line.length + 1;
    }
    // Cut at a line where that keeps most of the budget. When the next line is a long paragraph
    // (a README's Purpose is often one line), take its start instead, at a word, so the budget
    // isn't spent on a header alone. Array.from never splits a surrogate pair.
    if (room - used > budget / 4) {
        const next = Array.from(text.split('\n')[kept.length] ?? '').slice(0, room - used - 2).join('');
        const word = next.lastIndexOf(' ');
        kept.push((word > next.length / 2 ? next.slice(0, word) : next) + ' …');
    }
    return { text: kept.join('\n') + '\n' + pointer, cut: true };
}

// Emits `text` for `event` on behalf of hook `name`. `kind` is 'context' (additionalContext) or
// 'deny' (a PreToolUse denial, whose reason Claude sees). Prints nothing for empty text.
export function emit({ event, name, text, project, session = 'unknown', kind = 'context', budget }) {
    if (!text || !text.trim()) { return ''; }
    const limit = Math.min(budget ?? budgetFor(name), HARD_BUDGET);
    const full = path.join(cacheDir(project), `${name}-${session}.md`);
    const out = fit(text.trim(), limit, full, project);
    log(project, { hook: name, event, session, chars: out.text.length, original: text.length, cut: out.cut });

    const specific = kind === 'deny'
        ? { hookEventName: event, permissionDecision: 'deny', permissionDecisionReason: out.text }
        : { hookEventName: event, additionalContext: out.text };
    process.stdout.write(JSON.stringify({ hookSpecificOutput: specific }));
    return out.text;
}
