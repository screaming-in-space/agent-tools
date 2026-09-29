#!/usr/bin/env node
// install.mjs - puts the harness into a project, merging its hooks into the settings already there.
//
//   node install.mjs [project] [--local] [--with-claude-md] [--dry-run]
//   node install.mjs [project] --uninstall [--local]
//
// Copies the hooks into <project>/.claude/hooks/harness/, so they travel with the repository, and
// merges settings.json's hooks block into <project>/.claude/settings.json (settings.local.json with
// --local). The merge only adds: every existing key, hook and group stays as it was, a hook
// already present is not added twice, and a settings file that doesn't parse is left alone and
// reported. The original is backed up to .claude/.cache/harness/ first. A harness.json already in
// the project is kept, so a reinstall never resets its budgets or lease patterns.
//
// --with-claude-md appends claude-block.md to .claude/CLAUDE.md between markers, once.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const KIT = path.dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const has = (f) => args.includes(f);
const project = path.resolve(args.find((a) => !a.startsWith('--')) ?? process.cwd());
const claudeDir = path.join(project, '.claude');
const dest = path.join(claudeDir, 'hooks', 'harness');
const settingsFile = path.join(claudeDir, has('--local') ? 'settings.local.json' : 'settings.json');
const MARK = '/.claude/hooks/harness/';
const COPY = ['lib', 'battery', 'glue-detector.mjs', 'lease.mjs', 'lease-gate.mjs', 'session-brief.mjs', 'context-budget.mjs',
    'readme-on-touch.mjs', 'compaction-rescue.mjs', 'local-llm.mjs', 'README.md', 'claude-block.md'];

const sameHook = (a, b) => a.command === b.command && JSON.stringify(a.args ?? null) === JSON.stringify(b.args ?? null);
const isOurs = (h) => String(h.command).includes(MARK) || (h.args ?? []).some((a) => String(a).includes(MARK));

function readSettings() {
    if (!fs.existsSync(settingsFile)) { return { data: {}, eol: '\r\n', raw: null }; }
    const raw = fs.readFileSync(settingsFile, 'utf8');
    try {
        return { data: JSON.parse(raw.replace(/^﻿/, '')), eol: raw.includes('\r\n') ? '\r\n' : '\n', raw };
    } catch (e) {
        console.error(`install.mjs: ${settingsFile} isn't valid JSON (${e.message}), so it was left alone. Fix it and run again.`);
        process.exit(1);
    }
}

function merge(existing, template) {
    const out = structuredClone(existing);
    const added = [];
    out.hooks ??= {};
    for (const [event, groups] of Object.entries(template.hooks)) {
        out.hooks[event] ??= [];
        for (const group of groups) {
            let target = out.hooks[event].find((g) => (g.matcher ?? '') === (group.matcher ?? ''));
            if (!target) {
                target = { ...(group.matcher ? { matcher: group.matcher } : {}), hooks: [] };
                out.hooks[event].push(target);
            }
            target.hooks ??= [];
            for (const hook of group.hooks) {
                if (target.hooks.some((h) => sameHook(h, hook))) { continue; }
                target.hooks.push(hook);
                added.push(`${event}${group.matcher ? ` (${group.matcher})` : ''}: ${path.basename(hook.args?.[0] ?? hook.command)} ${hook.args?.[1] ?? ''}`.trim());
            }
        }
    }
    return { settings: out, added };
}

// Takes the harness's hooks out. A reinstall keeps the emptied groups and events where they were
// (`keepSlots`), so merge refills them in place and an unchanged kit rewrites nothing; prune()
// then drops whatever stayed empty.
function unmerge(existing, keepSlots = false) {
    const out = structuredClone(existing);
    let removed = 0;
    for (const groups of Object.values(out.hooks ?? {})) {
        for (const g of groups) {
            const before = g.hooks?.length ?? 0;
            g.hooks = (g.hooks ?? []).filter((h) => !isOurs(h));
            removed += before - g.hooks.length;
        }
    }
    return { settings: keepSlots ? out : prune(out), removed };
}

function prune(settings) {
    for (const [event, groups] of Object.entries(settings.hooks ?? {})) {
        settings.hooks[event] = groups.filter((g) => g.hooks?.length);
        if (!settings.hooks[event].length) { delete settings.hooks[event]; }
    }
    if (settings.hooks && !Object.keys(settings.hooks).length) { delete settings.hooks; }
    return settings;
}

function write(settings, eol, raw) {
    const text = JSON.stringify(settings, null, 2).replace(/\n/g, eol) + eol;
    if (has('--dry-run')) { console.log(text); return; }
    fs.mkdirSync(claudeDir, { recursive: true });
    if (raw !== null) {
        const backups = path.join(claudeDir, '.cache', 'harness');
        fs.mkdirSync(backups, { recursive: true });
        fs.writeFileSync(path.join(backups, `${path.basename(settingsFile)}.${Date.now()}.bak`), raw);
    }
    fs.writeFileSync(settingsFile, text);
    JSON.parse(fs.readFileSync(settingsFile, 'utf8'));
}

function copyKit() {
    fs.mkdirSync(dest, { recursive: true });
    for (const name of COPY) {
        const from = path.join(KIT, name);
        if (fs.existsSync(from)) { fs.cpSync(from, path.join(dest, name), { recursive: true }); }
    }
    const config = path.join(dest, 'harness.json');
    if (!fs.existsSync(config)) { fs.copyFileSync(path.join(KIT, 'harness.json'), config); }
}

function addClaudeBlock() {
    const file = path.join(claudeDir, 'CLAUDE.md');
    const current = fs.existsSync(file) ? fs.readFileSync(file, 'utf8') : '';
    if (current.includes('<!-- harness:start -->')) { return 'CLAUDE.md already has the harness block.'; }
    const eol = current.includes('\r\n') || !current ? '\r\n' : '\n';
    const block = fs.readFileSync(path.join(KIT, 'claude-block.md'), 'utf8').replace(/\r?\n/g, eol).trimEnd();
    fs.writeFileSync(file, `${current.trimEnd()}${current ? eol + eol : ''}<!-- harness:start -->${eol}${block}${eol}<!-- harness:end -->${eol}`);
    return `Added the harness block to ${path.relative(project, file)}.`;
}

function cacheIgnored() {
    const probe = spawnSync('git', ['check-ignore', '-q', '.claude/.cache/harness/x'], { cwd: project });
    return probe.error || probe.status !== 1;
}

const { data, eol, raw } = readSettings();
if (has('--uninstall')) {
    const { settings, removed } = unmerge(data);
    write(settings, eol, raw);
    console.log(`Removed ${removed} harness hook(s) from ${path.relative(project, settingsFile)}. The files in ${path.relative(project, dest)} are left for you to delete.`);
} else {
    // A reinstall replaces the harness's own hooks rather than adding beside them, so a hook the
    // kit moved to another group (a new matcher) isn't left behind in its old one.
    const template = JSON.parse(fs.readFileSync(path.join(KIT, 'settings.json'), 'utf8'));
    const merged = merge(unmerge(data, true).settings, template);
    const settings = prune(merged.settings);
    const { added } = merged;
    if (!has('--dry-run')) { copyKit(); }
    const unchanged = JSON.stringify(settings) === JSON.stringify(data);
    if (!unchanged) { write(settings, eol, raw); }
    console.log(unchanged ? `${path.relative(project, settingsFile)} already had every harness hook.` : `Harness hooks in ${path.relative(project, settingsFile)}:\n  ${added.join('\n  ')}`);
    if (has('--with-claude-md') && !has('--dry-run')) { console.log(addClaudeBlock()); }
    if (!cacheIgnored()) { console.log('Note: .claude/.cache/ isn\'t gitignored here. The hooks keep their state there; add it to .gitignore.'); }
}
