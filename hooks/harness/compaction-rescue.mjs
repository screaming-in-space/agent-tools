#!/usr/bin/env node
// compaction-rescue.mjs - what a compaction summarises away, handed back after it.
//
//   pre-compact    PreCompact: save the person's messages since the last compaction (or the
//                  session's start), and the files edited meanwhile, to .claude/.cache/harness/
//   post-compact   PostCompact: save the compaction's own summary beside them
//   session-start  SessionStart with source "compact": inject the saved messages through emit.mjs,
//                  so they arrive as context and the file holds whatever didn't fit
//
// A summary keeps the gist and drops the wording, and the wording is often the instruction: "use
// the Nano, not Lightning", "don't touch the benchmark's stack". This path is Continuum's
// journal-guard.mjs without its journal: it assumes no journal format, and never blocks a
// compaction, since an automatic one fires when the context is already full.
import fs from 'node:fs';
import path from 'node:path';
import { cacheDir, linesBackward, projectDir, readInput, rel, run, sessionId, textOf } from './lib/common.mjs';
import { emit } from './lib/emit.mjs';

const WRITE_TOOLS = new Set(['Write', 'Edit', 'MultiEdit', 'NotebookEdit']);
const MAX_MESSAGES = 400;
const mode = process.argv[2];

function sinceLastCompaction(transcript) {
    const messages = [];
    const files = new Set();
    for (const line of linesBackward(transcript)) {
        if (line.includes('"compact_boundary"') || line.includes('"isCompactSummary":true')) { break; }
        const human = line.includes('"kind":"human"');
        const write = line.includes('"tool_use"') && line.includes('file_path');
        if (!human && !write) { continue; }
        let o;
        try { o = JSON.parse(line); } catch { continue; }
        if (human && o.type === 'user' && o.origin?.kind === 'human' && !o.isMeta) {
            const text = textOf(o.message?.content).trim();
            if (text) { messages.push({ at: o.timestamp ?? '', text }); }
        } else if (human && o.type === 'attachment' && o.attachment?.type === 'queued_command' && o.attachment?.origin?.kind === 'human') {
            const text = textOf(o.attachment.prompt).trim();
            if (text) { messages.push({ at: o.timestamp ?? '', text }); }
        }
        if (write && o.type === 'assistant' && Array.isArray(o.message?.content)) {
            for (const b of o.message.content) {
                if (b?.type === 'tool_use' && WRITE_TOOLS.has(b.name) && b.input?.file_path) { files.add(b.input.file_path); }
            }
        }
        if (messages.length >= MAX_MESSAGES) { break; }
    }
    return { messages: messages.reverse(), files: [...files].reverse() };
}

run(() => {
    const input = readInput();
    const project = projectDir(input);
    const session = sessionId(input);
    const dir = cacheDir(project);
    const saved = path.join(dir, `rescue-${session}.md`);

    if (mode === 'pre-compact') {
        if (!input.transcript_path || !fs.existsSync(input.transcript_path)) { return; }
        const { messages, files } = sinceLastCompaction(input.transcript_path);
        if (!messages.length && !files.length) { return; }
        const body = [
            `# Before the compaction of ${new Date().toISOString()} (${input.trigger ?? 'unknown'})`, '',
            `The person's ${messages.length} message(s) since the last compaction, oldest first, word for word.`, '',
            ...messages.map((m, i) => `## ${i + 1} · ${m.at}\n\n${m.text.length > 4000 ? m.text.slice(0, 4000) + ' …[truncated]' : m.text}\n`),
            files.length ? `## Files edited in that stretch\n\n${files.map((f) => `- ${rel(project, f)}`).join('\n')}\n` : '',
        ].join('\n');
        fs.writeFileSync(saved, body);
        return;
    }

    if (mode === 'post-compact') {
        if (input.compact_summary) { fs.writeFileSync(path.join(dir, `compact-summary-${session}-${Date.now()}.md`), input.compact_summary); }
        return;
    }

    if (mode === 'session-start') {
        if (input.source !== 'compact' || !fs.existsSync(saved)) { return; }
        const body = fs.readFileSync(saved, 'utf8');
        const text = `The context was just compacted. The person's messages from before it are below, word for word, from ${rel(project, saved)}. Where the summary and these disagree, these are what was said.\n\n${body}`;
        emit({ event: 'SessionStart', name: 'compaction-rescue', text, project, session });
        fs.renameSync(saved, path.join(dir, `rescue-${session}-${Date.now()}.delivered.md`));
    }
});
