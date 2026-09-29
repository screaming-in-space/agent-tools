#!/usr/bin/env node
// proof-sessions.mjs - the harness inside real Claude Code sessions, not just its hooks.
//
//   node test/proof-sessions.mjs <project> [--tokens <repo>]
//   node test/proof-sessions.mjs --no-sessions --tokens <repo>     the token count alone
//
// Run it from a terminal where `claude` is signed in, while session A (a live session in
// <project> with the harness installed) holds the `build` lease. It starts two headless sessions
// in <project> and writes what they saw to <tmp>/harness-proof/results.json:
//
//   B1  tries the project's build: the lease gate must refuse it, naming session A. It also
//       reports the session-start context it received: the leases, the handoff, the budget line.
//   B2  starts with the context budget lowered below the always-on set, and reports the line
//       context-budget.mjs said.
//
// --tokens <repo> also measures what <repo>'s always-on set costs in tokens: one headless run in a
// copy holding only that set, one in an empty copy, on the same model, and the difference.
//
// Both B sessions run on Haiku with no MCP servers, to keep the proof cheap. --no-sessions skips
// them: any real second session on the machine proves the same thing whenever one opens.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const args = process.argv.slice(2);
const tokensRepo = args.includes('--tokens') ? path.resolve(args[args.indexOf('--tokens') + 1]) : null;
const project = path.resolve(args.find((a, i) => !a.startsWith('--') && args[i - 1] !== '--tokens') ?? process.cwd());
const noSessions = args.includes('--no-sessions');
const out = path.join(os.tmpdir(), 'harness-proof');
fs.mkdirSync(out, { recursive: true });
const results = { project, startedAt: new Date().toISOString() };

// A terminal inside another Claude Code session passes that session's variables down; B must be a
// session of its own.
const clean = Object.fromEntries(Object.entries(process.env).filter(([k]) => k !== 'CLAUDECODE' && !k.startsWith('CLAUDE_CODE_')));

function claude(cwd, prompt, extra = [], env = {}) {
    const r = spawnSync('claude', ['-p', prompt, '--output-format', 'json', '--strict-mcp-config', ...extra], {
        cwd, encoding: 'utf8', env: { ...clean, ...env }, timeout: 300000,
    });
    let json = null;
    try { json = JSON.parse(r.stdout); } catch { }
    return { status: r.status, result: json?.result ?? r.stdout?.slice(0, 2000), session: json?.session_id, usage: json?.usage, cost: json?.total_cost_usd, stderr: r.stderr?.slice(0, 1000) };
}

const report = `Answer in exactly four lines, each quoting word for word:
DENIED: the reason your Bash call was refused, or "no"
LEASES: the first line of any session-start context about leases held by other sessions, or "none"
HANDOFF: the first line of any session-start context that starts with "# Handoff", or "none"
BUDGET: any session-start context line that starts with "Context budget", or "none"`;

if (!noSessions) {
console.log('B1: the build, under session A\'s lease…');
results.b1 = claude(project, `This is a test of this project's hooks. Run this Bash command once: dotnet build ThreadUnsafe.slnx -v q
If the call is refused or denied, don't retry it and don't run anything else.
${report}`, ['--model', 'haiku', '--allowedTools', 'Bash(dotnet build:*)']);
console.log(results.b1.result);

console.log('\nB2: the context budget lowered below the always-on set…');
results.b2 = claude(project, `This is a test of this project's hooks. Run no tools. ${report}`, ['--model', 'haiku', '--tools', ''], { HARNESS_CONTEXT_BUDGET: '20000' });
console.log(results.b2.result);
}

if (tokensRepo) {
    // The always-on set, copied with its layout so every relative @import resolves as it does in the repo.
    const files = [];
    const walk = (file, hops) => {
        if (hops > 4 || !fs.existsSync(file) || files.includes(file)) { return; }
        files.push(file);
        const raw = fs.readFileSync(file, 'utf8').replace(/<!--[\s\S]*?-->/g, '').replace(/`[^`\n]*`/g, '');
        let fence = false;
        for (const line of raw.split(/\r?\n/)) {
            if (/^\s*(```|~~~)/.test(line)) { fence = !fence; continue; }
            if (fence) { continue; }
            for (const m of line.matchAll(/(?:^|\s)@([^\s`]+)/g)) { walk(path.resolve(path.dirname(file), m[1]), hops + 1); }
        }
    };
    walk(path.join(tokensRepo, '.claude', 'CLAUDE.md'), 0);
    const rules = path.join(tokensRepo, '.claude', 'rules');
    for (const f of fs.existsSync(rules) ? fs.readdirSync(rules) : []) {
        const head = fs.readFileSync(path.join(rules, f), 'utf8').slice(0, 2000);
        if (f.endsWith('.md') && !/^---[\s\S]*?\bpaths\s*:/m.test(head.split(/\r?\n---/)[0])) { walk(path.join(rules, f), 0); }
    }
    const withSet = path.join(out, 'with');
    const without = path.join(out, 'without');
    fs.rmSync(withSet, { recursive: true, force: true });
    fs.rmSync(without, { recursive: true, force: true });
    for (const f of files) {
        const to = path.join(withSet, path.relative(tokensRepo, f));
        fs.mkdirSync(path.dirname(to), { recursive: true });
        fs.copyFileSync(f, to);
    }
    fs.mkdirSync(path.join(without, '.claude'), { recursive: true });
    fs.writeFileSync(path.join(without, '.claude', 'CLAUDE.md'), '# Empty\n');
    const bytes = files.reduce((n, f) => n + fs.statSync(f).size, 0);
    console.log(`\nTokens: ${files.length} always-on files, ${bytes} bytes, on claude-opus-5-5…`);
    const run = (dir) => claude(dir, 'Reply with the single word OK.', ['--model', 'claude-opus-5-5', '--tools', '', '--no-session-persistence']);
    const a = run(without);
    const b = run(withSet);
    const total = (u) => (u?.input_tokens ?? 0) + (u?.cache_creation_input_tokens ?? 0) + (u?.cache_read_input_tokens ?? 0);
    results.tokens = { files: files.map((f) => ({ file: path.relative(tokensRepo, f), bytes: fs.statSync(f).size })), bytes, without: a.usage, with: b.usage, difference: total(b.usage) - total(a.usage) };
    console.log(`without: ${total(a.usage)} input tokens; with: ${total(b.usage)}; the set: ${results.tokens.difference} tokens, ${(bytes / results.tokens.difference).toFixed(2)} bytes a token`);
}

fs.writeFileSync(path.join(out, 'results.json'), JSON.stringify(results, null, 2));
console.log(`\nWritten: ${path.join(out, 'results.json')}`);
