#!/usr/bin/env node
// lease-gate.mjs - stops a shell command that needs a resource another session holds.
//
//   pre-tool       PreToolUse on Bash|PowerShell: map the command to resources (harness.json
//                  `leases.resources`), and deny it, naming the holder, when another owner holds one
//   session-start  SessionStart: list the live leases held by others; silent when there are none
//
// The gate never takes a lease for a command: a session claims what it is about to hold for a
// while (a benchmark, a stack it is running) with lease.mjs, on purpose. Without a lease on the
// resource, the command runs and the normal permission flow applies.
import { projectDir, readInput, run, sessionId, loadConfig } from './lib/common.mjs';
import { emit } from './lib/emit.mjs';
import { currentOwner, describe, get, leaseDir, leaseKey, list, resourcesFor } from './lib/leases.mjs';

const mode = process.argv[2];

run(() => {
    const input = readInput();
    const project = projectDir(input);
    const session = sessionId(input);
    const config = loadConfig();
    const dir = leaseDir(config);
    const owner = currentOwner(input);

    if (mode === 'pre-tool') {
        const command = String(input.tool_input?.command ?? '');
        if (!command || /\blease\.mjs\b/.test(command)) { return; }
        const held = resourcesFor(command, config)
            .map((r) => get(dir, leaseKey(r.name, r.scope, project)))
            .filter((l) => l && l.owner !== owner);
        if (!held.length) { return; }
        const text = `Not run: ${held.map((l) => describe(l)).join('; ')}. Wait, ask the holder, or do other work.`;
        emit({ event: 'PreToolUse', name: 'lease-gate', text, project, session, kind: 'deny' });
        return;
    }

    if (mode === 'session-start') {
        const others = list(dir).filter((l) => l.owner !== owner);
        if (!others.length) { return; }
        const text = ['Held by other sessions; commands that need these are refused:', ...others.map((l) => `- ${describe(l)}`)].join('\n');
        emit({ event: 'SessionStart', name: 'lease-gate', text, project, session });
    }
});
