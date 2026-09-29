#!/usr/bin/env node
// glue-detector.mjs - notices the shell steps sessions keep repeating by hand, and writes them down.
//
//   stop           Stop: count this session's shell commands by shape; on the Nth repeat of one
//                  (`glue.repeats`) append a row to GLUE.md. Silent: a Stop hook that speaks costs
//                  the model another turn
//   prompt         UserPromptSubmit: the same count, then one line per new step, once a session
//   session-start  SessionStart (startup only): the steps at least `glue.briefMinSessions` sessions
//                  did by hand, one line each. Silent otherwise
//
// Glue is a manual step a session runs to keep a project working: a store queried through
// `docker exec`, a service checked with curl, the same throwaway script typed again. It costs
// tokens every time and only happens while a session runs. Each row names the cheapest rung that
// retires it: 1 a script or CLI verb, 2 an MCP tool or verb that answers in one line, 3 a local
// model measured on the job.
//
// Every line this hook adds to the context is paid for, so it says little: a step's shape, its
// count and its rung. The brief lists only steps two sessions repeated, since one session's
// repeat is already told in that session, and GLUE.md itself is never imported: it grows by a row
// per session per step.
//
// Commands are read from the transcript forwards from where the last run stopped, so a run costs
// only what the transcript gained since. The first run in a long resumed session reads at most
// the last 8 MB.
import fs from 'node:fs';
import path from 'node:path';
import { cacheDir, linesSince, loadConfig, projectDir, readInput, run, sessionId } from './lib/common.mjs';
import { emit } from './lib/emit.mjs';

const SHELLS = new Set(['Bash', 'PowerShell']);
const FIRST_READ_MAX = 8 << 20;
const MAX_KEY = 80;
const mode = process.argv[2];

// A step's shape: the command with what varies between runs taken out. Only the first line
// counts (a heredoc's body follows it). Leading variable assignments, `cd dir &&` and `timeout N`
// go; an inline script (`node -e '…'`, `python -c "…"`) is one step whatever it says; quoted
// strings, paths, ids, container suffixes and numbers become placeholders.
function shape(command) {
    let s = command.split('\n')[0];
    for (let before = ''; before !== s;) {
        before = s;
        s = s.replace(/^\s*(?:export\s+)?[A-Za-z_]\w*=(?:"[^"]*"|'[^']*'|\S*)(?:\s*(?:;|&&)\s*|\s+)/, '')
            .replace(/^\s*\$env:\w+\s*=\s*(?:"[^"]*"|'[^']*'|\S+)\s*;\s*/i, '')
            .replace(/^\s*(?:cd|pushd|Set-Location)\s+(?:"[^"]*"|'[^']*'|\S+)\s*(?:&&|;)\s*/i, '')
            .replace(/^\s*timeout\s+\d+\s+/, '');
    }
    return s
        .replace(/(\s(?:-e|-c|--eval|-Command)\s+)(?:'[^']*'?|"(?:[^"\\]|\\.)*"?)/, '$1<script>')
        .replace(/"(?:[^"\\]|\\.)*"|'[^']*'/g, '<str>')
        .replace(/(?<=^|[\s=])(?:[A-Za-z]:[\\/]|~[\\/]|\.{0,2}\/)[^\s"'|;&)]*/g, '<path>')
        .replace(/\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/gi, '<id>')
        .replace(/\b[0-9a-f]{7,40}\b/g, (m) => (/\d/.test(m) && /[a-f]/.test(m) ? '<sha>' : m))
        .replace(/(?<=-)[A-Za-z0-9]*\d[A-Za-z0-9]*\b/g, '<id>')
        .replace(/\b\d{2,}\b/g, '<n>')
        .replace(/\s+/g, ' ')
        .trim()
        .slice(0, MAX_KEY);
}

function rung(step) {
    if (/\/v1\/(chat\/)?completions|\blms\s|\bollama\s(run|generate)|\bllm\s/i.test(step)) { return '3, local model'; }
    if (/\bdocker\s+exec\b|\bpsql\b|\bredis-cli\b|\bmongosh\b|\bsqlite3\b|\btemporal\s+workflow\b|\b(curl|Invoke-RestMethod|Invoke-WebRequest)\b[^|]*localhost/i.test(step)) {
        return '2, MCP tool or CLI verb';
    }
    return '1, script or CLI verb';
}

const stateFileOf = (project, session) => path.join(cacheDir(project), `glue-${session}.json`);

function loadState(file) {
    try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return { offset: -1, counts: {}, recorded: [], told: [] }; }
}

function countCommands(transcript, state, ignore) {
    const from = state.offset < 0 ? Math.max(0, fs.statSync(transcript).size - FIRST_READ_MAX) : state.offset;
    const { lines, offset } = linesSince(transcript, from);
    for (const line of lines) {
        if (!line.includes('"tool_use"') || !(line.includes('"Bash"') || line.includes('"PowerShell"'))) { continue; }
        let o;
        try { o = JSON.parse(line); } catch { continue; }
        if (o.type !== 'assistant' || !Array.isArray(o.message?.content)) { continue; }
        for (const b of o.message.content) {
            if (b?.type !== 'tool_use' || !SHELLS.has(b.name)) { continue; }
            const command = String(b.input?.command ?? '');
            if (/glue-detector|lease\.mjs/.test(command)) { continue; }
            const key = shape(command);
            if (key && !ignore.some((re) => re.test(key))) { state.counts[key] = (state.counts[key] ?? 0) + 1; }
        }
    }
    state.offset = offset;
}

const cell = (s) => s.replace(/\|/g, '\\|').replace(/`/g, "'");

function appendRows(glueFile, rows, session) {
    const header = '# Glue\n\nSteps sessions repeated by hand (`.claude/hooks/harness/glue-detector.mjs`). Retire one on its rung, then delete its rows.\n\n| Date | Step | Times | Rung | Session |\n|---|---|---|---|---|\n';
    const today = new Date().toISOString().slice(0, 10);
    const body = rows.map(({ key, count }) => `| ${today} | \`${cell(key)}\` | ${count} | ${rung(key)} | ${session.slice(0, 8)} |`).join('\n') + '\n';
    if (fs.existsSync(glueFile)) { fs.appendFileSync(glueFile, body); } else { fs.writeFileSync(glueFile, header + body); }
}

// GLUE.md's rows grouped by step, the steps most sessions repeated first. A `|` inside a cell is
// written `\|`, so cells split on the others.
function inventory(glueFile) {
    if (!fs.existsSync(glueFile)) { return []; }
    const steps = new Map();
    for (const line of fs.readFileSync(glueFile, 'utf8').split(/\r?\n/)) {
        if (!/^\|\s*\d{4}-\d{2}-\d{2}\s*\|/.test(line)) { continue; }
        const [, step, times, retire, session] = line.slice(1, -1).split(/(?<!\\)\|/).map((c) => c.trim());
        const key = step.replace(/^`|`$/g, '').replace(/\\\|/g, '|');
        const s = steps.get(key) ?? { key, sessions: new Set(), times: 0, retire };
        s.sessions.add(session);
        s.times += Number(times) || 0;
        steps.set(key, s);
    }
    return [...steps.values()].sort((a, b) => b.sessions.size - a.sessions.size || b.times - a.times);
}

run(() => {
    const input = readInput();
    const project = projectDir(input);
    const session = sessionId(input);
    const { glue } = loadConfig();
    const glueFile = path.resolve(project, glue.file);
    const stateFile = stateFileOf(project, session);
    const state = loadState(stateFile);

    if (mode === 'session-start') {
        const shared = inventory(glueFile).filter((s) => s.sessions.size >= glue.briefMinSessions).slice(0, glue.briefLines);
        if (!shared.length) { return; }
        const lines = ['Glue other sessions also did by hand (GLUE.md). Retire a step before repeating it:',
            ...shared.map((s) => `- \`${s.key}\` ×${s.times}, ${s.sessions.size} sessions → rung ${s.retire}`)];
        emit({ event: 'SessionStart', name: 'glue-brief', text: lines.join('\n'), project, session });
        state.told.push(...shared.map((s) => s.key));
        fs.writeFileSync(stateFile, JSON.stringify(state));
        return;
    }

    const transcript = input.transcript_path;
    if (!transcript || !fs.existsSync(transcript)) { return; }
    countCommands(transcript, state, glue.ignore.map((p) => new RegExp(p, 'i')));

    const due = Object.entries(state.counts).filter(([key, n]) => n >= glue.repeats && !state.recorded.includes(key));
    if (due.length) {
        appendRows(glueFile, due.map(([key, count]) => ({ key, count })), session);
        state.recorded.push(...due.map(([key]) => key));
    }

    if (mode === 'prompt') {
        const tell = state.recorded.filter((key) => !state.told.includes(key)).slice(0, 3);
        if (tell.length) {
            const text = tell.map((key) => `Glue ×${state.counts[key]} this session: \`${key}\` → rung ${rung(key)} (GLUE.md).`).join('\n');
            emit({ event: 'UserPromptSubmit', name: 'glue-detector', text, project, session });
            state.told.push(...tell);
        }
    }

    fs.writeFileSync(stateFile, JSON.stringify(state));
});
