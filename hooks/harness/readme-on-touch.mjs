#!/usr/bin/env node
// readme-on-touch.mjs - PreToolUse on Read|Edit|Write|NotebookEdit: the owning project's README,
// once per project per session.
//
// A rule can say "read a project's README before changing it"; only a hook can make sure it
// happened. When a tool touches a file, this walks up to the nearest folder holding a project
// marker (harness.json `readme.markers`: a .csproj, package.json, go.mod…) and injects that
// folder's README.md beside the tool result. The project's own root is skipped by default
// (`readme.skipRoot`), since its README is usually the one already in context.
//
// Ported from Continuum's project-readme.ps1, with one change: the README goes through emit.mjs,
// so one longer than its budget arrives cut, with a pointer to the rest, instead of as a
// 2,000-character preview.
import fs from 'node:fs';
import path from 'node:path';
import { cacheDir, loadConfig, projectDir, readInput, rel, run, sessionId } from './lib/common.mjs';
import { emit } from './lib/emit.mjs';

const toRegex = (glob) => new RegExp(`^${glob.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*')}$`, 'i');

run(() => {
    const input = readInput();
    let target = input.tool_input?.file_path ?? input.tool_input?.notebook_path;
    if (!target) { return; }
    const project = path.resolve(projectDir(input));
    const config = loadConfig();
    target = path.resolve(project, target);
    if (!target.toLowerCase().startsWith(project.toLowerCase() + path.sep)) { return; }

    const markers = (config.readme?.markers ?? []).map(toRegex);
    let dir = fs.existsSync(target) && fs.statSync(target).isDirectory() ? target : path.dirname(target);
    let owner = null;
    while (dir.toLowerCase().startsWith(project.toLowerCase())) {
        if (dir.toLowerCase() === project.toLowerCase() && config.readme?.skipRoot !== false) { break; }
        let names = [];
        try { names = fs.readdirSync(dir); } catch { }
        if (names.some((n) => markers.some((re) => re.test(n)))) { owner = dir; break; }
        const up = path.dirname(dir);
        if (up === dir) { break; }
        dir = up;
    }
    if (!owner) { return; }
    const readme = path.join(owner, 'README.md');
    if (!fs.existsSync(readme) || readme.toLowerCase() === target.toLowerCase()) { return; }

    const session = sessionId(input);
    const seenFile = path.join(cacheDir(project), `readme-${session}.txt`);
    const seen = fs.existsSync(seenFile) ? fs.readFileSync(seenFile, 'utf8').split('\n') : [];
    const name = rel(project, owner);
    if (seen.includes(name)) { return; }
    fs.appendFileSync(seenFile, name + '\n');

    const body = fs.readFileSync(readme, 'utf8').trim();
    const text = `${rel(project, readme)}, the README of the project this touches. Update it in the same change if the contract moves.\n\n${body}`;
    emit({ event: 'PreToolUse', name: 'readme-on-touch', text, project, session });
});
