#!/usr/bin/env node
// lease.mjs - take, renew, release and list leases on shared resources, from any shell.
//
//   node lease.mjs take <resource> [--for 30m] [--purpose "..."]
//   node lease.mjs release <resource>
//   node lease.mjs list
//
// Run by a Claude Code session through its Bash or PowerShell tool, the owner is that session
// (CLAUDE_CODE_SESSION_ID), the same owner lease-gate.mjs sees in the hook input. A resource named
// in harness.json takes that entry's scope; any other name is machine-wide.
//
// This is a command, not a hook, so it exits 1 when a take is refused: a script can branch on it.
import { loadConfig, projectDir } from './lib/common.mjs';
import { currentOwner, describe, leaseDir, leaseKey, list, release, shortOwner, take } from './lib/leases.mjs';

const [verb, resource, ...rest] = process.argv.slice(2);
const flag = (name) => { const i = rest.indexOf(name); return i >= 0 ? rest[i + 1] : undefined; };
const config = loadConfig();
const dir = leaseDir(config);
const owner = currentOwner();
const project = projectDir();

function minutes(spec) {
    if (!spec) { return Number(config.leases.minutes); }
    const m = /^(\d+(?:\.\d+)?)(m|h)?$/.exec(spec);
    if (!m) { throw new Error(`--for takes minutes or hours, such as 30m or 2h, not "${spec}"`); }
    return Number(m[1]) * (m[2] === 'h' ? 60 : 1);
}

function keyOf(name) {
    const known = (config.leases?.resources ?? []).find((r) => r.name === name);
    return leaseKey(name, known?.scope ?? 'machine', project);
}

try {
    if (verb === 'take' && resource) {
        const result = take(dir, { key: keyOf(resource), resource, owner, purpose: flag('--purpose'), minutes: minutes(flag('--for')), project });
        if (result.taken) {
            console.log(`Taken: ${resource} by ${shortOwner(owner)} until ${result.lease.expiresAt}. Release it with: node lease.mjs release ${resource}`);
        } else {
            console.log(`Refused: ${describe(result.lease)}.`);
            process.exitCode = 1;
        }
    } else if (verb === 'release' && resource) {
        console.log(release(dir, keyOf(resource), owner) ? `Released: ${resource}.` : `Not released: ${shortOwner(owner)} doesn't hold ${resource}.`);
    } else if (verb === 'list') {
        const leases = list(dir);
        console.log(leases.length ? leases.map((l) => describe(l) + (l.owner === owner ? ' (yours)' : '')).join('\n') : 'No leases held.');
    } else {
        console.log('Usage: node lease.mjs take <resource> [--for 30m] [--purpose "..."] | release <resource> | list');
        process.exitCode = 2;
    }
} catch (e) {
    console.error(`lease.mjs: ${e.message}`);
    process.exitCode = 2;
}
