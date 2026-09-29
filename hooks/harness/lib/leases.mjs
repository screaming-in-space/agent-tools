// leases.mjs - claims on shared resources, as files in one folder every session on the machine can see.
//
// A lease records an owner, a purpose and an expiry. The owner is the Claude Code session
// (CLAUDE_CODE_SESSION_ID, which Claude Code sets for hooks and for Bash and PowerShell commands
// alike), so a hook and a command run by the same session agree on who holds what. A holder that
// dies lets go by itself when its lease expires; nothing has to notice it died.
//
// Taking is atomic: the file is created with O_EXCL ('wx'), so of two sessions taking at once
// exactly one succeeds. Taking over an expired lease happens under a lock directory (mkdir is
// atomic too), so two sessions can't both replace the same expired file and one lose the other's
// fresh lease.
//
// Continuum's own leases are Redis keys whose TTL is the expiry, taken and released by a Lua script
// so the owner check and the write are one step. A folder does the same job with nothing running.
import crypto from 'node:crypto';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

export function leaseDir(config) {
    const dir = config.leases?.dir || process.env.HARNESS_LEASE_DIR || path.join(os.homedir(), '.claude', 'harness', 'leases');
    fs.mkdirSync(dir, { recursive: true });
    return dir;
}

// HARNESS_LEASE_OWNER, else the Claude Code session, else this machine and process.
export function currentOwner(input = {}) {
    if (process.env.HARNESS_LEASE_OWNER) { return process.env.HARNESS_LEASE_OWNER; }
    const id = input.session_id || process.env.CLAUDE_CODE_SESSION_ID;
    return id ? `claude-code:${id}` : `${os.hostname()}:${process.pid}`;
}

export function shortOwner(owner) {
    return owner.startsWith('claude-code:') ? `session ${owner.slice(12, 20)}` : owner;
}

// A project-scoped resource (a build, the working tree) is one lease per checkout; a machine-scoped
// one (the GPU, the stack's ports) is one lease for everyone.
export function leaseKey(resource, scope, project) {
    if (scope !== 'project') { return resource; }
    const slug = path.basename(project).toLowerCase().replace(/[^a-z0-9]+/g, '-').slice(0, 24);
    const hash = crypto.createHash('sha1').update(path.resolve(project).toLowerCase()).digest('hex').slice(0, 8);
    return `${resource}@${slug}-${hash}`;
}

const fileOf = (dir, key) => path.join(dir, `${key.replace(/[^A-Za-z0-9@._-]/g, '_')}.json`);

function read(file) {
    try { return JSON.parse(fs.readFileSync(file, 'utf8')); } catch { return null; }
}

export const isLive = (lease, now = Date.now()) => !!lease && Date.parse(lease.expiresAt) > now;

export function get(dir, key) {
    const lease = read(fileOf(dir, key));
    return isLive(lease) ? lease : null;
}

export function list(dir) {
    const out = [];
    for (const f of fs.readdirSync(dir)) {
        if (!f.endsWith('.json')) { continue; }
        const lease = read(path.join(dir, f));
        if (isLive(lease)) { out.push(lease); }
    }
    return out.sort((a, b) => a.key.localeCompare(b.key));
}

function withLock(file, fn) {
    const lock = `${file}.lock`;
    for (let attempt = 0; attempt < 40; attempt++) {
        try {
            fs.mkdirSync(lock);
        } catch {
            // A lock older than ten seconds belongs to a process that died mid-takeover.
            try { if (Date.now() - fs.statSync(lock).mtimeMs > 10000) { fs.rmdirSync(lock); } } catch { }
            Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 25);
            continue;
        }
        try { return fn(); } finally { try { fs.rmdirSync(lock); } catch { } }
    }
    throw new Error('lease lock busy');
}

// Takes the lease, or renews it when `owner` already holds it. Returns { taken, lease }: the lease
// is the caller's own when taken, else the live holder's.
export function take(dir, { key, resource, owner, purpose, minutes, project }) {
    const file = fileOf(dir, key);
    const now = Date.now();
    const lease = {
        key, resource, owner, purpose: purpose || '', project: project || '',
        takenAt: new Date(now).toISOString(), expiresAt: new Date(now + minutes * 60000).toISOString(),
    };
    const body = JSON.stringify(lease, null, 2);
    try {
        fs.writeFileSync(file, body, { flag: 'wx' });
        return { taken: true, lease };
    } catch (e) {
        if (e.code !== 'EEXIST') { throw e; }
    }
    return withLock(file, () => {
        const held = read(file);
        if (isLive(held) && held.owner !== owner) { return { taken: false, lease: held }; }
        if (isLive(held)) { lease.takenAt = held.takenAt; }
        const tmp = `${file}.${process.pid}.tmp`;
        fs.writeFileSync(tmp, JSON.stringify(lease, null, 2));
        fs.renameSync(tmp, file);
        return { taken: true, lease };
    });
}

export function release(dir, key, owner) {
    const file = fileOf(dir, key);
    return withLock(file, () => {
        const held = read(file);
        if (!held || held.owner !== owner) { return false; }
        fs.unlinkSync(file);
        return true;
    });
}

export function describe(lease, now = Date.now()) {
    const left = Math.max(0, Math.round((Date.parse(lease.expiresAt) - now) / 60000));
    const where = lease.project ? ` in ${path.basename(lease.project)}` : '';
    const what = lease.purpose ? ` for "${lease.purpose}"` : '';
    return `${lease.resource} is held by ${shortOwner(lease.owner)}${what}${where}, ${left} min left`;
}

// The resources a command touches, from harness.json's `leases.resources`: a command matching one
// of a resource's `patterns` needs it, unless it also matches one of its `except` patterns. A
// build into a scratch folder (`-o`, `--output`) is the usual exception: it leaves the shared
// `bin` alone, so it needs no lease. The first build the gate stopped, on 2026-09-28, was one.
export function resourcesFor(command, config) {
    const hits = [];
    const any = (list) => (list ?? []).some((p) => new RegExp(p, 'i').test(command));
    for (const r of config.leases?.resources ?? []) {
        if (any(r.patterns) && !any(r.except)) { hits.push(r); }
    }
    return hits;
}
