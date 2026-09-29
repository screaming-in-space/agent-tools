#!/usr/bin/env node
// context-budget.mjs - SessionStart: says when the always-on context has outgrown its budget.
//
// The always-on set is what every session pays for on every turn, before it has read a file:
// every CLAUDE.md from the project up to the root, the user's, every .claude/rules file without a
// `paths:` key (the project's and the user's), and everything those import with `@`, to four hops
// as Claude Code resolves them (code spans, code fences and HTML comments skipped). This hook
// resolves the same set from disk, totals it, and estimates tokens at harness.json's
// `contextBudget.bytesPerToken`.
//
// Silent under `contextBudget.maxTokens`. Over it, one line naming the three largest files, and
// the question that decides each: does every session need this, or only the ones that touch its
// area? It also says when a harness hook's own output was cut last session, since a cut is the
// failure emit.mjs exists to make visible.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { HARD_BUDGET, cacheDir, loadConfig, projectDir, readInput, rel, run, sessionId } from './lib/common.mjs';
import { emit } from './lib/emit.mjs';

const MAX_HOPS = 4;

function imports(file) {
    let raw;
    try { raw = fs.readFileSync(file, 'utf8'); } catch { return []; }
    raw = raw.replace(/<!--[\s\S]*?-->/g, '').replace(/`[^`\n]*`/g, '');
    const found = [];
    let fence = false;
    for (const line of raw.split(/\r?\n/)) {
        if (/^\s*(```|~~~)/.test(line)) { fence = !fence; continue; }
        if (fence) { continue; }
        for (const m of line.matchAll(/(?:^|\s)@([^\s`]+)/g)) {
            let target = m[1].replace(/[.,;:)]+$/, '');
            if (target.startsWith('~/')) { target = path.join(os.homedir(), target.slice(2)); }
            found.push(path.resolve(path.dirname(file), target));
        }
    }
    return found;
}

function hasPaths(file) {
    try {
        const head = fs.readFileSync(file, 'utf8').slice(0, 4000);
        const fm = /^---\r?\n([\s\S]*?)\r?\n---/.exec(head);
        return !!fm && /^\s*paths\s*:/m.test(fm[1]);
    } catch { return false; }
}

function rulesIn(dir, out = []) {
    if (!fs.existsSync(dir)) { return out; }
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, e.name);
        if (e.isDirectory()) { rulesIn(p, out); } else if (e.name.endsWith('.md') && !hasPaths(p)) { out.push(p); }
    }
    return out;
}

function alwaysOn(project) {
    const roots = [];
    const home = os.homedir();
    roots.push(path.join(home, '.claude', 'CLAUDE.md'), ...rulesIn(path.join(home, '.claude', 'rules')));
    for (let dir = path.resolve(project); ; dir = path.dirname(dir)) {
        roots.push(path.join(dir, 'CLAUDE.md'), path.join(dir, 'CLAUDE.local.md'));
        if (path.dirname(dir) === dir) { break; }
    }
    roots.push(path.join(project, '.claude', 'CLAUDE.md'), ...rulesIn(path.join(project, '.claude', 'rules')));

    const seen = new Map();
    const visit = (file, hops) => {
        const key = file.toLowerCase();
        if (seen.has(key) || hops > MAX_HOPS || !fs.existsSync(file) || !fs.statSync(file).isFile()) { return; }
        seen.set(key, { file, bytes: fs.statSync(file).size });
        for (const child of imports(file)) { visit(child, hops + 1); }
    };
    for (const r of roots) { visit(r, 0); }
    return [...seen.values()];
}

// Emissions a harness hook had to cut in the most recent other session, from harness.log.
function cutsLastSession(project, session) {
    const file = path.join(cacheDir(project), 'harness.log');
    if (!fs.existsSync(file)) { return []; }
    const lines = fs.readFileSync(file, 'utf8').trim().split('\n');
    if (lines.length > 5000) { fs.writeFileSync(file, lines.slice(-2000).join('\n') + '\n'); }
    let last = null;
    const cuts = new Map();
    for (let i = lines.length - 1; i >= 0; i--) {
        let r;
        try { r = JSON.parse(lines[i]); } catch { continue; }
        if (r.session === session) { continue; }
        last ??= r.session;
        if (r.session !== last) { break; }
        if (r.cut) { cuts.set(r.hook, (cuts.get(r.hook) ?? 0) + 1); }
    }
    return [...cuts.entries()];
}

run(() => {
    const input = readInput();
    const project = projectDir(input);
    const session = sessionId(input);
    const config = loadConfig();
    const max = Number(config.contextBudget.maxTokens);
    const perToken = Number(config.contextBudget.bytesPerToken);

    const files = alwaysOn(project);
    const bytes = files.reduce((n, f) => n + f.bytes, 0);
    const tokens = Math.round(bytes / perToken);
    const lines = [];

    if (tokens > max) {
        const largest = files.sort((a, b) => b.bytes - a.bytes).slice(0, 3)
            .map((f) => `${rel(project, f.file)} ${Math.round(f.bytes / 1024)} KB`).join(', ');
        lines.push(`Context budget: always-on ${Math.round(bytes / 1024)} KB, ~${Math.round(tokens / 1000)}k tokens a turn, over ${Math.round(max / 1000)}k. Largest: ${largest}. Scope what not every session needs.`);
    }
    for (const [hook, count] of cutsLastSession(project, session)) {
        lines.push(`Context budget: ${hook} was cut ${count}× last session (full text: .claude/.cache/harness/${hook}-*.md).`);
    }
    for (const [hook, value] of Object.entries(config.budgets)) {
        if (Number(value) > HARD_BUDGET) { lines.push(`Context budget: ${hook}'s budget of ${value} is held at ${HARD_BUDGET}.`); }
    }
    emit({ event: 'SessionStart', name: 'context-budget', text: lines.join('\n'), project, session });
});
