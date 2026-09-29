#!/usr/bin/env node
// smoke.mjs - drives every hook as Claude Code would, with real payloads, in a throwaway project.
//
//   node test/smoke.mjs
//
// Each hook runs as its own process with the hook JSON on stdin and CLAUDE_PROJECT_DIR set, so
// what is checked is exactly what a session would get on stdout. It proves the hooks' behaviour,
// not Claude Code's: the two-session proof (README.md, "Proof") runs them inside real sessions.
import { spawn, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const KIT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const root = fs.mkdtempSync(path.join(os.tmpdir(), 'harness-smoke-'));
const project = path.join(root, 'project');
const leases = path.join(root, 'leases');
fs.mkdirSync(path.join(project, '.claude'), { recursive: true });
const results = [];

function hook(script, mode, input, env = {}) {
    const started = process.hrtime.bigint();
    const r = spawnSync(process.execPath, [path.join(project, '.claude', 'hooks', 'harness', script), ...(mode ? [mode] : [])], {
        input: JSON.stringify(input), encoding: 'utf8',
        env: { ...process.env, CLAUDE_PROJECT_DIR: project, HARNESS_LEASE_DIR: leases, CLAUDE_CODE_SESSION_ID: input.session_id ?? '', ...env },
    });
    const ms = Number(process.hrtime.bigint() - started) / 1e6;
    let json = null;
    try { json = r.stdout ? JSON.parse(r.stdout) : null; } catch { }
    return { status: r.status, stdout: r.stdout, stderr: r.stderr, json, ms, context: json?.hookSpecificOutput?.additionalContext ?? '', deny: json?.hookSpecificOutput?.permissionDecisionReason ?? '' };
}

function check(name, ok, detail = '') {
    results.push({ name, ok: !!ok });
    console.log(`${ok ? 'ok  ' : 'FAIL'}  ${name}${detail ? `  (${detail})` : ''}`);
}

const A = 'aaaaaaaa-1111-4111-8111-000000000001';
const B = 'bbbbbbbb-2222-4222-8222-000000000002';
const tx = (lines) => { const f = path.join(root, `tx-${Math.random().toString(36).slice(2)}.jsonl`); fs.writeFileSync(f, lines.map((l) => JSON.stringify(l)).join('\n') + '\n'); return f; };
const bash = (command) => ({ type: 'assistant', message: { content: [{ type: 'tool_use', name: 'Bash', input: { command } }] } });
const human = (text) => ({ type: 'user', origin: { kind: 'human' }, message: { content: text }, timestamp: new Date().toISOString() });

// install.mjs into a project whose settings already hold other hooks and permissions.
const existing = { permissions: { allow: ['Bash(npm test:*)'] }, hooks: { PreToolUse: [{ matcher: 'Bash', hooks: [{ type: 'command', command: 'node other.mjs' }] }] } };
fs.writeFileSync(path.join(project, '.claude', 'settings.json'), JSON.stringify(existing, null, 2).replace(/\n/g, '\r\n'));
let r = spawnSync(process.execPath, [path.join(KIT, 'install.mjs'), project], { encoding: 'utf8' });
const merged = JSON.parse(fs.readFileSync(path.join(project, '.claude', 'settings.json'), 'utf8'));
check('install merges and leaves valid JSON', r.status === 0 && merged.permissions.allow[0] === 'Bash(npm test:*)' && merged.hooks.PreToolUse.some((g) => g.hooks.some((h) => h.command === 'node other.mjs')) && merged.hooks.SessionStart);
r = spawnSync(process.execPath, [path.join(KIT, 'install.mjs'), project], { encoding: 'utf8' });
check('install is idempotent', /already had every harness hook/.test(r.stdout));
const bad = path.join(root, 'bad');
fs.mkdirSync(path.join(bad, '.claude'), { recursive: true });
fs.writeFileSync(path.join(bad, '.claude', 'settings.json'), '{ "hooks": ');
r = spawnSync(process.execPath, [path.join(KIT, 'install.mjs'), bad], { encoding: 'utf8' });
check('install refuses a settings file that does not parse', r.status === 1 && fs.readFileSync(path.join(bad, '.claude', 'settings.json'), 'utf8') === '{ "hooks": ');

// context-budget: silent under budget, names the largest files over it.
fs.mkdirSync(path.join(project, 'docs'), { recursive: true });
fs.writeFileSync(path.join(project, '.claude', 'CLAUDE.md'), '# Project\n\n@../docs/RULES.md\n\n`@not/an/import.md`\n');
fs.writeFileSync(path.join(project, 'docs', 'RULES.md'), 'rules '.repeat(3000));
fs.mkdirSync(path.join(project, '.claude', 'rules'), { recursive: true });
fs.writeFileSync(path.join(project, '.claude', 'rules', 'always.md'), '# Always\n\n@../../docs/BIG.md\n');
fs.writeFileSync(path.join(project, 'docs', 'BIG.md'), 'big '.repeat(8000));
fs.writeFileSync(path.join(project, '.claude', 'rules', 'scoped.md'), '---\npaths: src/**\n---\n@../../docs/SCOPED.md\n');
fs.writeFileSync(path.join(project, 'docs', 'SCOPED.md'), 'scoped '.repeat(20000));
r = hook('context-budget.mjs', null, { session_id: B, source: 'startup' });
check('context-budget is silent under budget', r.stdout === '', `${r.ms.toFixed(0)} ms`);
r = hook('context-budget.mjs', null, { session_id: B, source: 'startup' }, { HARNESS_CONTEXT_BUDGET: '1000' });
check('context-budget names the largest files over budget, and skips scoped rules', /Largest: docs\/BIG\.md/.test(r.context) && !r.context.includes('SCOPED'), r.context.slice(0, 90));

// session-brief: only the open questions, and cut with a pointer when too long.
const journal = path.join(project, 'docs', 'journal');
fs.mkdirSync(journal, { recursive: true });
fs.writeFileSync(path.join(journal, '2026-09-27_01.md'), '# Older\n\n## Next steps\n\n- stale\n');
fs.writeFileSync(path.join(journal, '2026-09-28_01.md'), '# Today\n\n## Work\n\nLots done.\n\n## Open questions and next steps\n\n- Split the queue.\n- Measure the embedder.\n\n## Findings\n\nnot this\n');
r = hook('session-brief.mjs', null, { session_id: A, source: 'startup' });
check('session-brief injects the newest handoff\'s open questions only', r.context.includes('Split the queue') && !r.context.includes('stale') && !r.context.includes('not this'), `${r.ms.toFixed(0)} ms`);
fs.writeFileSync(path.join(journal, '2026-09-28_02.md'), `# Big\n\n## Next steps\n\n${Array.from({ length: 600 }, (_, i) => `- step ${i} ${'x'.repeat(40)}`).join('\n')}\n`);
r = hook('session-brief.mjs', null, { session_id: A, source: 'startup' });
const briefFull = path.join(project, '.claude', '.cache', 'harness', `session-brief-${A}.md`);
check('a long handoff is cut to its budget, ending with a pointer to the full text', r.context.length <= 4000 && /full text is in \.claude\/\.cache\/harness\/session-brief-/.test(r.context.split('\n').at(-1)) && fs.statSync(briefFull).size > 20000, `${r.context.length} chars`);

// leases: A takes the build, B's build is refused naming A, B's ls is not.
const leaseCli = (session, ...a) => spawnSync(process.execPath, [path.join(project, '.claude', 'hooks', 'harness', 'lease.mjs'), ...a], { encoding: 'utf8', env: { ...process.env, CLAUDE_PROJECT_DIR: project, HARNESS_LEASE_DIR: leases, CLAUDE_CODE_SESSION_ID: session } });
r = leaseCli(A, 'take', 'build', '--for', '10m', '--purpose', 'benchmark run');
check('session A takes the build lease', r.status === 0 && /Taken: build/.test(r.stdout));
r = leaseCli(B, 'take', 'build');
check('session B cannot take it', r.status === 1 && /session aaaaaaaa/.test(r.stdout));
r = hook('lease-gate.mjs', 'pre-tool', { session_id: B, tool_name: 'Bash', tool_input: { command: 'dotnet build ThreadUnsafe.slnx' } });
check('the gate denies B\'s build and names A', r.json?.hookSpecificOutput?.permissionDecision === 'deny' && /held by session aaaaaaaa for "benchmark run"/.test(r.deny), `${r.ms.toFixed(0)} ms`);
r = hook('lease-gate.mjs', 'pre-tool', { session_id: A, tool_name: 'Bash', tool_input: { command: 'dotnet build ThreadUnsafe.slnx' } });
check('the gate lets the holder build', r.stdout === '');
r = hook('lease-gate.mjs', 'pre-tool', { session_id: B, tool_name: 'Bash', tool_input: { command: 'timeout 200 dotnet build ThreadUnsafe.slnx -o "$TEMP/claude-tu-build" 2>&1 | tail -4' } });
check('the gate lets B build into a scratch folder with -o', r.stdout === '');
r = hook('lease-gate.mjs', 'pre-tool', { session_id: B, tool_name: 'Bash', tool_input: { command: 'ls -la' } });
check('the gate is silent for a command that needs nothing', r.stdout === '');
r = hook('lease-gate.mjs', 'session-start', { session_id: B, source: 'startup' });
check('B hears of A\'s lease at session start', /build is held by session aaaaaaaa/.test(r.context));
r = hook('lease-gate.mjs', 'session-start', { session_id: A, source: 'startup' });
check('A hears nothing of its own lease', r.stdout === '');
leaseCli(A, 'release', 'build');
r = hook('lease-gate.mjs', 'pre-tool', { session_id: B, tool_name: 'Bash', tool_input: { command: 'dotnet build ThreadUnsafe.slnx' } });
check('once released, B builds', r.stdout === '');

// glue-detector: the third run of a step writes GLUE.md, and Claude hears of it once. The same
// query through docker exec, against a container whose suffix changed with each restart.
const psql = (suffix) => bash(`docker exec -i continuum-postgres-server-${suffix} sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" psql -U postgres -d continuum -t -A' <<'SQL'`);
const glueTx = tx([human('go'), psql('ab12cd'), bash('ls'), bash('dotnet build Continuum.slnx'), bash('dotnet build Continuum.slnx'), bash('dotnet build Continuum.slnx'), psql('9f3e1a')]);
r = hook('glue-detector.mjs', 'stop', { session_id: A, transcript_path: glueTx });
check('two repeats write nothing, and three builds are not glue', !fs.existsSync(path.join(project, 'GLUE.md')));
fs.appendFileSync(glueTx, JSON.stringify(psql('77e0b2')) + '\n');
r = hook('glue-detector.mjs', 'stop', { session_id: A, transcript_path: glueTx });
const glue = fs.existsSync(path.join(project, 'GLUE.md')) ? fs.readFileSync(path.join(project, 'GLUE.md'), 'utf8') : '';
const row = glue.split('\n').find((l) => l.includes('continuum-postgres-server-<id>')) ?? '';
check('the third repeat writes GLUE.md, silently at Stop, as a short shape', r.stdout === '' && /`docker exec -i continuum-postgres-server-<id> sh -c <script> <<<str>` \| 3 \| 2, MCP tool/.test(row), `${r.ms.toFixed(0)} ms`);
r = hook('glue-detector.mjs', 'prompt', { session_id: A, transcript_path: glueTx, prompt: 'next' });
check('the next prompt tells Claude in one short line', r.context.split('\n').length === 1 && /^Glue ×3 this session: `docker exec/.test(r.context) && r.context.length < 140, `${r.context.length} chars`);
r = hook('glue-detector.mjs', 'prompt', { session_id: A, transcript_path: glueTx, prompt: 'again' });
check('and only once', r.stdout === '');
r = hook('glue-detector.mjs', 'session-start', { session_id: 'cccccccc-3333-4333-8333-000000000003', source: 'startup' });
check('one session\'s repeat is not briefed to the next', r.stdout === '');
// A second session repeats the same step: the next session starts knowing both did it by hand.
fs.appendFileSync(path.join(project, 'GLUE.md'), row.replace('| 3 |', '| 4 |').replace(A.slice(0, 8), B.slice(0, 8)) + '\n');
r = hook('glue-detector.mjs', 'session-start', { session_id: 'dddddddd-4444-4444-8444-000000000004', source: 'startup' });
check('a step two sessions repeated is briefed, one line', /^- `docker exec -i continuum-postgres-server-<id> [^`]*` ×7, 2 sessions → rung 2, MCP tool/m.test(r.context) && r.context.split('\n').length === 2, `${r.context.length} chars, ${r.ms.toFixed(0)} ms`);
r = hook('glue-detector.mjs', 'prompt', { session_id: 'dddddddd-4444-4444-8444-000000000004', transcript_path: glueTx, prompt: 'go' });
check('and that session is not told it again at its first prompt', r.stdout === '');
const noGlue = path.join(root, 'no-glue');
fs.mkdirSync(noGlue, { recursive: true });
r = hook('glue-detector.mjs', 'session-start', { session_id: A, source: 'startup' }, { CLAUDE_PROJECT_DIR: noGlue });
check('and hears nothing when there is no glue', r.stdout === '');

// readme-on-touch: once per project per session, cut when long.
fs.mkdirSync(path.join(project, 'src', 'web', 'lib'), { recursive: true });
fs.writeFileSync(path.join(project, 'src', 'web', 'package.json'), '{}');
fs.writeFileSync(path.join(project, 'src', 'web', 'README.md'), `# web\n\n${'Purpose line. '.repeat(1200)}\n`);
fs.writeFileSync(path.join(project, 'src', 'web', 'lib', 'a.js'), '');
r = hook('readme-on-touch.mjs', null, { session_id: A, tool_name: 'Read', tool_input: { file_path: path.join(project, 'src', 'web', 'lib', 'a.js') } });
check('readme-on-touch injects the owning README, cut to its budget', r.context.startsWith('src/web/README.md, the README') && r.context.length <= 6000 && /full text is in/.test(r.context), `${r.context.length} chars, ${r.ms.toFixed(0)} ms`);
r = hook('readme-on-touch.mjs', null, { session_id: A, tool_name: 'Edit', tool_input: { file_path: path.join(project, 'src', 'web', 'lib', 'a.js') } });
check('once per project per session', r.stdout === '');

// compaction-rescue: messages saved before, handed back after.
const rescueTx = tx([human('Use the Nano, not Lightning.'), bash('ls'), human("Don't touch the benchmark's stack.")]);
hook('compaction-rescue.mjs', 'pre-compact', { session_id: A, transcript_path: rescueTx, trigger: 'auto' });
r = hook('compaction-rescue.mjs', 'session-start', { session_id: A, source: 'startup' });
check('rescue says nothing on a normal start', r.stdout === '');
r = hook('compaction-rescue.mjs', 'session-start', { session_id: A, source: 'compact' });
check('after a compaction the person\'s words come back as context', r.context.includes('Use the Nano, not Lightning.') && r.context.includes("Don't touch the benchmark's stack."));

// emit's hard ceiling holds even when harness.json asks for more.
const cfgFile = path.join(project, '.claude', 'hooks', 'harness', 'harness.json');
const cfg = JSON.parse(fs.readFileSync(cfgFile, 'utf8'));
cfg.budgets = { ...cfg.budgets, 'session-brief': 50000 };
fs.writeFileSync(cfgFile, JSON.stringify(cfg));
r = hook('session-brief.mjs', null, { session_id: A, source: 'startup' });
check('no budget in harness.json can push a hook past 9,000 characters', r.context.length <= 9000, `${r.context.length} chars`);
r = hook('context-budget.mjs', null, { session_id: B, source: 'startup' });
check('context-budget reports the over-cap budget, and last session\'s cuts', /is held at 9000/.test(r.context) && /session-brief was cut/.test(r.context));

// A hook fed garbage stays silent and exits 0.
const g = spawnSync(process.execPath, [path.join(project, '.claude', 'hooks', 'harness', 'glue-detector.mjs'), 'prompt'], { input: 'not json', encoding: 'utf8', env: { ...process.env, CLAUDE_PROJECT_DIR: project } });
check('bad input: exit 0, no output', g.status === 0 && g.stdout === '');

// local-llm against a stand-in OpenAI-compatible server: every sampler on the wire, and a reply
// from another model refused. The stand-in is its own process: spawnSync would block a server in
// this one.
const wireFile = path.join(root, 'wire.json');
const standIn = spawn(process.execPath, ['-e', `
const http=require('http');let n=0;
const s=http.createServer((q,r)=>{let b='';q.on('data',d=>b+=d);q.on('end',()=>{require('fs').writeFileSync(${JSON.stringify(wireFile)},b);n++;r.setHeader('content-type','application/json');r.end(JSON.stringify({model:n===1?'test-model':'other-model',choices:[{message:{content:'warning'}}]}));});});
s.listen(0,'127.0.0.1',()=>console.log(s.address().port));`], { stdio: ['ignore', 'pipe', 'inherit'] });
const port = await new Promise((ok) => standIn.stdout.once('data', (d) => ok(String(d).trim())));
const llm = () => spawnSync(process.execPath, [path.join(project, '.claude', 'hooks', 'harness', 'local-llm.mjs'), '--endpoint', `http://127.0.0.1:${port}/v1`, '--model', 'test-model'],
    { input: 'WARN disk 91%', encoding: 'utf8', env: { ...process.env, CLAUDE_PROJECT_DIR: project }, timeout: 20000 });
const first = llm();
const wire = fs.existsSync(wireFile) ? JSON.parse(fs.readFileSync(wireFile, 'utf8')) : {};
const second = llm();
standIn.kill();
check('local-llm answers when the right model replies', first.status === 0 && first.stdout.trim() === 'warning');
check('local-llm sends every sampler', ['temperature', 'top_p', 'top_k', 'min_p', 'repeat_penalty', 'max_tokens', 'seed'].every((k) => k in wire), Object.keys(wire).join(','));
check('local-llm refuses a reply from another model', second.status === 3 && /Refused: asked for test-model, answered by other-model/.test(second.stderr));

const failed = results.filter((x) => !x.ok).length;
console.log(`\n${results.length - failed}/${results.length} passed. Scratch project: ${root}`);
process.exitCode = failed ? 1 : 0;
