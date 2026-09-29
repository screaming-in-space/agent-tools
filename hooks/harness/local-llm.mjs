#!/usr/bin/env node
// local-llm.mjs - rung 3 of the ladder: hand a piece of judgement to a local model, and trust the
// answer only as far as it has been measured.
//
//   some-command | node local-llm.mjs --system "Name the failing project and its error code."
//   node local-llm.mjs --list                  the runtime's own model ids
//   node local-llm.mjs --battery [file.json]   measure the configured model before trusting it
//
// It talks to any OpenAI-compatible endpoint (harness.json `localLlm.endpoint`, LM Studio's by
// default), and follows three rules learned the hard way in Continuum (its LOCAL_LLM.md, U7, U8
// and the LM Studio notes):
//
//   - every sampler goes out on every request (temperature, top_p, top_k, min_p, the repeat
//     penalty, max_tokens, seed). An omitted one is the runtime's chat default, and LM Studio's
//     repeat penalty of 1.1 punishes JSON, which repeats by construction;
//   - the request body is saved (.claude/.cache/harness/local-llm-request.json, and on stderr
//     with --wire), because client libraries drop fields and the configuration looks right anyway;
//   - a reply from another model is refused. LM Studio answers a request for a model that isn't
//     loaded with one that is, and the only sign is the response's `model` field.
//
// Load models with the runtime's own tooling, and a big model alone on the card. This script
// never loads one. It is a command, not a hook: it exits 3 on a refused reply and 1 on a failed
// battery, so a script can branch on it.
import fs from 'node:fs';
import path from 'node:path';
import { KIT_DIR, cacheDir, loadConfig, projectDir } from './lib/common.mjs';

const args = process.argv.slice(2);
const flag = (name) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : undefined; };
const has = (name) => args.includes(name);
const config = loadConfig().localLlm;
const endpoint = (flag('--endpoint') ?? config.endpoint).replace(/\/$/, '');
const model = flag('--model') ?? config.model;
const cache = cacheDir(projectDir());

// vLLM spells the repeat penalty `repetition_penalty`; LM Studio and llama.cpp, `repeat_penalty`.
function body(messages) {
    const s = { ...config.samplers };
    if (flag('--max-tokens')) { s.max_tokens = Number(flag('--max-tokens')); }
    if (flag('--temperature')) { s.temperature = Number(flag('--temperature')); }
    const repeatKey = config.runtime === 'vllm' ? 'repetition_penalty' : 'repeat_penalty';
    const { repeat_penalty: repeat, ...rest } = s;
    const out = { model, messages, stream: false, ...rest, [repeatKey]: repeat };
    if (config.reasoning_effort) { out.reasoning_effort = config.reasoning_effort; }
    return out;
}

async function ask(system, user) {
    const messages = [...(system ? [{ role: 'system', content: system }] : []), { role: 'user', content: user }];
    const request = body(messages);
    fs.writeFileSync(path.join(cache, 'local-llm-request.json'), JSON.stringify(request, null, 2));
    if (has('--wire')) { process.stderr.write(JSON.stringify(request) + '\n'); }
    const started = Date.now();
    const res = await fetch(`${endpoint}/chat/completions`, {
        method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(request), signal: AbortSignal.timeout(180000),
    });
    if (!res.ok) { throw new Error(`${endpoint} answered ${res.status}: ${(await res.text()).slice(0, 300)}`); }
    const json = await res.json();
    if (String(json.model ?? '').toLowerCase() !== String(model).toLowerCase()) {
        const e = new Error(`Refused: asked for ${model}, answered by ${json.model ?? 'an unnamed model'}. Load ${model} with the runtime's own tooling, then ask again.`);
        e.code = 3;
        throw e;
    }
    return { text: String(json.choices?.[0]?.message?.content ?? '').trim(), ms: Date.now() - started, usage: json.usage };
}

function passes(expect, text) {
    const t = text.trim();
    if (expect.equals !== undefined && t.toLowerCase() !== String(expect.equals).toLowerCase()) { return `expected "${expect.equals}"`; }
    for (const c of expect.contains ?? []) { if (!t.toLowerCase().includes(c.toLowerCase())) { return `missing "${c}"`; } }
    for (const c of expect.excludes ?? []) { if (t.toLowerCase().includes(c.toLowerCase())) { return `contains "${c}"`; } }
    if (expect.regex && !new RegExp(expect.regex).test(t)) { return `doesn't match /${expect.regex}/`; }
    if (expect.maxChars && t.length > expect.maxChars) { return `${t.length} characters, over ${expect.maxChars}`; }
    if (expect.json) {
        let o;
        try { o = JSON.parse(t.replace(/^```(?:json)?\s*|\s*```$/g, '')); } catch { return 'not JSON'; }
        for (const [k, v] of Object.entries(expect.json)) { if (o?.[k] !== v) { return `${k} is ${JSON.stringify(o?.[k])}, not ${JSON.stringify(v)}`; } }
    }
    return null;
}

async function battery(file) {
    const items = JSON.parse(fs.readFileSync(file, 'utf8')).items;
    const results = [];
    for (const item of items) {
        try {
            const { text, ms } = await ask(item.system, item.input);
            const why = passes(item.expect, text);
            results.push({ id: item.id, pass: !why, why, ms, answer: text.slice(0, 200) });
        } catch (e) {
            if (e.code === 3) { throw e; }
            results.push({ id: item.id, pass: false, why: e.message, ms: 0, answer: '' });
        }
        const r = results.at(-1);
        console.log(`${r.pass ? 'PASS' : 'FAIL'}  ${r.id.padEnd(22)} ${String(r.ms).padStart(6)} ms  ${r.why ?? ''}`);
    }
    const passed = results.filter((r) => r.pass).length;
    const out = path.join(cache, `battery-${model.replace(/[^A-Za-z0-9._-]/g, '_')}-${Date.now()}.json`);
    fs.writeFileSync(out, JSON.stringify({ model, endpoint, samplers: body([]), results }, null, 2));
    console.log(`${passed}/${results.length} passed for ${model}. Results: ${out}`);
    if (passed < results.length) { process.exitCode = 1; }
}

try {
    if (has('--list')) {
        const res = await fetch(`${endpoint}/models`, { signal: AbortSignal.timeout(10000) });
        const ids = ((await res.json()).data ?? []).map((m) => m.id);
        console.log(ids.length ? ids.join('\n') : `No models at ${endpoint}. Load one with the runtime's own tooling.`);
    } else if (!model) {
        console.error('local-llm.mjs: no model. Set localLlm.model in harness.json or pass --model, with an id from --list.');
        process.exitCode = 2;
    } else if (has('--battery')) {
        const file = flag('--battery') && !flag('--battery').startsWith('--') ? flag('--battery') : path.join(KIT_DIR, 'battery', 'battery.json');
        await battery(file);
    } else {
        const input = fs.readFileSync(0, 'utf8');
        const { text } = await ask(flag('--system'), input);
        console.log(text);
    }
} catch (e) {
    console.error(`local-llm.mjs: ${e.message}`);
    process.exitCode = e.code === 3 ? 3 : 2;
}
