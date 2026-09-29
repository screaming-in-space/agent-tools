// common.mjs - what every hook in the harness shares: its input, its paths, its config, and a
// transcript reader that stays fast on a 50 MB file.
//
// Every hook follows three rules (README.md, "Constraints"): Node with no dependencies, exit 0 in
// silence on any error, and finish in milliseconds. `run` enforces the first two.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const KIT_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

// Claude Code caps each hook string at 10,000 characters, then saves it to a file and shows the
// model a 2,000-character preview it is never asked to follow (code.claude.com/docs/en/hooks,
// "JSON output"). No budget may reach it, whatever harness.json says.
export const HARNESS_CAP = 10000;
export const HARD_BUDGET = 9000;

// Every setting and its default, in one place. A project's harness.json (beside the hooks) holds
// only what it changes: objects merge key by key, and an array replaces the default whole.
const DEFAULTS = {
    // Characters each hook may put in front of the model. emit.mjs never lets one pass HARD_BUDGET.
    budgets: {
        default: 6000,
        'session-brief': 4000,
        'readme-on-touch': 6000,
        'compaction-rescue': 6000,
        'lease-gate': 1000,
        'glue-detector': 600,
        'glue-brief': 800,
        'context-budget': 600,
    },
    contextBudget: { maxTokens: 25000, bytesPerToken: 4 },
    glue: {
        repeats: 3,
        file: 'GLUE.md',
        briefMinSessions: 2,
        briefLines: 5,
        // Reading and editing files, git, and the edit-build-test loop are the work, not glue.
        ignore: [
            '^(ls|dir|pwd|cat|type|head|tail|echo|printf|wc|clear|sed|awk|grep|rg|cut|sort|uniq|find|jq|diff|stat|du|tree)\\b',
            '^(Get-Content|Get-ChildItem|Select-String|Test-Path|Write-Output)\\b',
            '^git\\s',
            '^(dotnet|npm|npx|pnpm|yarn|cargo|go|make|pytest|jest|vitest)\\s+(build|test|run|install|restore|i|check)\\b',
            '^node\\s+(--check|-c)\\b',
        ],
    },
    leases: {
        dir: null,
        minutes: 30,
        // A command matching `patterns` needs the resource, unless it matches `except`. A build into
        // a scratch folder leaves the shared bin alone: the first build the gate stopped, on
        // 2026-09-28, was one.
        resources: [
            {
                name: 'build',
                scope: 'project',
                patterns: [
                    '\\bdotnet\\s+(build|test|run|publish|pack)\\b',
                    '\\bmsbuild\\b',
                    '\\b(npm|pnpm|yarn)\\s+(run\\s+)?(build|test)\\b',
                    '\\bcargo\\s+(build|test|run)\\b',
                    '\\bgo\\s+(build|test)\\b',
                    '(^|[;&|]\\s*)(make|gradle|mvn)\\b',
                ],
                except: ['\\s(-o|--output)(\\s|=)'],
            },
            {
                name: 'stack',
                scope: 'machine',
                patterns: ['\\bdocker\\s+compose\\s+(up|down|stop|restart|rm)\\b', '\\bdocker\\s+(stop|restart|kill|rm)\\b'],
            },
            {
                name: 'gpu',
                scope: 'machine',
                patterns: ['\\blms\\s+(load|unload)\\b', '\\bollama\\s+(run|stop|pull)\\b', '\\bvllm\\s+serve\\b'],
            },
        ],
    },
    brief: {
        dir: 'docs/journal',
        headings: ['open questions', 'next steps', 'handoff'],
    },
    readme: {
        markers: ['*.csproj', '*.fsproj', 'package.json', 'go.mod', 'pyproject.toml', 'Cargo.toml'],
        skipRoot: true,
    },
    localLlm: {
        endpoint: 'http://localhost:1234/v1',
        model: null,
        runtime: 'lmstudio',
        samplers: { temperature: 0.2, top_p: 0.95, top_k: 40, min_p: 0.05, repeat_penalty: 1.0, max_tokens: 1024, seed: 42 },
    },
};

export function readInput() {
    try {
        const raw = fs.readFileSync(0, 'utf8');
        return raw.trim() ? JSON.parse(raw) : {};
    } catch {
        return {};
    }
}

export function projectDir(input = {}) {
    return process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();
}

// The session id names cache files, so it is cut to characters any file system takes.
export function sessionId(input = {}) {
    const id = input.session_id || process.env.CLAUDE_CODE_SESSION_ID || 'unknown';
    return String(id).replace(/[^A-Za-z0-9_-]/g, '_');
}

export function cacheDir(project) {
    const dir = path.join(project, '.claude', '.cache', 'harness');
    fs.mkdirSync(dir, { recursive: true });
    return dir;
}

function merge(base, over) {
    if (!over || typeof over !== 'object' || Array.isArray(over)) { return over ?? base; }
    const out = { ...base };
    for (const [k, v] of Object.entries(over)) {
        out[k] = base && typeof base[k] === 'object' && !Array.isArray(base[k]) ? merge(base[k], v) : v;
    }
    return out;
}

// harness.json sits beside the hooks, so a project's copy of the kit carries its own settings.
export function loadConfig() {
    let file = {};
    try { file = JSON.parse(fs.readFileSync(path.join(KIT_DIR, 'harness.json'), 'utf8')); } catch { }
    const config = merge(DEFAULTS, file);
    if (process.env.HARNESS_CONTEXT_BUDGET) { config.contextBudget.maxTokens = Number(process.env.HARNESS_CONTEXT_BUDGET); }
    return config;
}

// One line per hook run, so context-budget can report at the next session start what was cut.
export function log(project, record) {
    try {
        fs.appendFileSync(path.join(cacheDir(project), 'harness.log'), JSON.stringify({ at: new Date().toISOString(), ...record }) + '\n');
    } catch { }
}

// Runs a hook and always exits 0: a broken hook degrades to silence and never breaks the session.
export function run(main) {
    Promise.resolve()
        .then(main)
        .catch(() => { })
        .finally(() => process.exit(0));
}

// Lines from the end of a file back towards `stopAt` bytes, newest first. A 50 MB transcript is
// read a megabyte at a time and a caller stops as soon as it has what it needs.
export function* linesBackward(file, { chunk = 1 << 20, maxBytes = Infinity } = {}) {
    const fd = fs.openSync(file, 'r');
    try {
        let pos = fs.fstatSync(fd).size;
        const floor = Math.max(0, pos - maxBytes);
        let carry = '';
        while (pos > floor) {
            const size = Math.min(chunk, pos - floor);
            pos -= size;
            const buf = Buffer.alloc(size);
            fs.readSync(fd, buf, 0, size, pos);
            const lines = (buf.toString('utf8') + carry).split('\n');
            carry = pos > floor ? lines.shift() : '';
            for (let i = lines.length - 1; i >= 0; i--) {
                if (lines[i]) { yield lines[i]; }
            }
        }
        if (carry) { yield carry; }
    } finally {
        fs.closeSync(fd);
    }
}

// New lines since `offset`, read forwards. A hook that keeps an offset reads only what the
// transcript gained since it last ran.
export function linesSince(file, offset) {
    const size = fs.statSync(file).size;
    if (offset > size) { offset = 0; }
    const fd = fs.openSync(file, 'r');
    try {
        const buf = Buffer.alloc(size - offset);
        fs.readSync(fd, buf, 0, buf.length, offset);
        const text = buf.toString('utf8');
        const end = text.lastIndexOf('\n') + 1;
        return { lines: text.slice(0, end).split('\n').filter(Boolean), offset: offset + Buffer.byteLength(text.slice(0, end)) };
    } finally {
        fs.closeSync(fd);
    }
}

export function textOf(content) {
    if (typeof content === 'string') { return content; }
    if (Array.isArray(content)) { return content.map((b) => (b?.type === 'text' ? b.text : '')).filter(Boolean).join(' '); }
    return '';
}

export function rel(project, file) {
    const r = path.relative(project, file);
    return (r && !r.startsWith('..') ? r : file).replace(/\\/g, '/');
}

export function homeDir() {
    return os.homedir();
}
