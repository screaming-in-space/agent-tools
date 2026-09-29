#!/usr/bin/env node
// session-brief.mjs - SessionStart: the last session's handoff, and only that.
//
// The handoff is the newest file in harness.json's `brief.dir` (docs/journal by default; dated
// names sort newest last, else the newest by mtime). Of it, only the sections whose heading names
// what is still open (`brief.headings`: open questions, next steps, handoff) are injected, with the
// file's path, so the rest is read on demand.
//
// Why only that: injecting whole entries is how a catch-up hook grows past Claude Code's
// 10,000-character cap without anyone noticing. Continuum's did, at 14 to 135 KB a session, and
// arrived as a 2,000-character preview. emit.mjs holds this one to its budget and says where the
// rest is if it is ever cut.
import fs from 'node:fs';
import path from 'node:path';
import { loadConfig, projectDir, readInput, rel, run, sessionId } from './lib/common.mjs';
import { emit } from './lib/emit.mjs';

const DATED = /^\d{4}-\d{2}-\d{2}/;

function markdownFiles(dir, depth = 0, out = []) {
    if (depth > 4) { return out; }
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
        const p = path.join(dir, e.name);
        if (e.isDirectory()) { markdownFiles(p, depth + 1, out); } else if (e.name.endsWith('.md')) { out.push(p); }
    }
    return out;
}

function newest(files) {
    const dated = files.filter((f) => DATED.test(path.basename(f)));
    if (dated.length) { return dated.sort((a, b) => path.basename(a).localeCompare(path.basename(b)) || a.localeCompare(b)).at(-1); }
    return files.sort((a, b) => fs.statSync(a).mtimeMs - fs.statSync(b).mtimeMs).at(-1);
}

// The sections whose heading matches, each from its heading to the next heading of its level or higher.
function openSections(text, wanted) {
    const lines = text.split(/\r?\n/);
    const out = [];
    let fence = false;
    let current = null;
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        if (/^\s*(```|~~~)/.test(line)) { fence = !fence; }
        const h = !fence && /^(#{1,6})\s+(.+?)\s*$/.exec(line);
        if (h) {
            const level = h[1].length;
            if (current && level <= current.level) { out.push(current); current = null; }
            if (!current && wanted.some((w) => h[2].toLowerCase().includes(w))) {
                current = { level, title: h[2], line: i + 1, body: [] };
                continue;
            }
        }
        if (current) { current.body.push(line); }
    }
    if (current) { out.push(current); }
    return out;
}

run(() => {
    const input = readInput();
    const project = projectDir(input);
    const config = loadConfig();
    const dir = path.resolve(project, config.brief.dir);
    if (!fs.existsSync(dir)) { return; }
    const file = newest(markdownFiles(dir));
    if (!file) { return; }

    const where = rel(project, file);
    const sections = openSections(fs.readFileSync(file, 'utf8'), (config.brief?.headings ?? []).map((h) => h.toLowerCase()));
    const text = sections.length
        ? [`# Handoff: ${where} (only what's open; the rest is on disk)`,
            ...sections.flatMap((s) => [`## ${s.title} (line ${s.line})`, ...s.body.join('\n').trim().split('\n')])].join('\n')
        : `# Handoff: ${where}. It has no open questions or next steps; read it for what's next.`;
    emit({ event: 'SessionStart', name: 'session-brief', text, project, session: sessionId(input) });
});
