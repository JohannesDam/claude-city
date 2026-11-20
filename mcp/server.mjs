#!/usr/bin/env node
// Claude City MCP server: exposes Cities: Skylines II (via the ClaudeCityBridge mod's
// localhost HTTP API) as MCP tools. Zero dependencies; speaks MCP over stdio
// (newline-delimited JSON-RPC 2.0).
import { createInterface } from 'node:readline';
import { TOOLS } from './tools.mjs';

const BASE = process.env.CS2_BRIDGE_URL || 'http://127.0.0.1:8787';
const PROTOCOL = '2025-06-18';

async function callBridge(tool, args) {
  const method = tool.http[0];
  let path = tool.http[1];
  const init = { method, headers: { 'content-type': 'application/json' } };
  const timeoutMs = tool.timeoutMs || 90000;
  // the bridge waits this long for the game's main thread before answering 504
  const qs = new URLSearchParams({ timeout: String(timeoutMs - 3000) });
  if (method === 'GET') {
    for (const [k, v] of Object.entries(args || {})) if (v !== undefined && v !== null) qs.set(k, String(v));
  } else {
    init.body = JSON.stringify(args || {});
  }
  path += (path.includes('?') ? '&' : '?') + qs.toString();
  const ctl = new AbortController();
  const timer = setTimeout(() => ctl.abort(), timeoutMs);
  try {
    const res = await fetch(BASE + path, { ...init, signal: ctl.signal });
    const text = await res.text();
    return { ok: res.ok, text: res.ok ? text : `HTTP ${res.status}: ${text}` };
  } catch (e) {
    return { ok: false, text: `Cannot reach the game bridge at ${BASE} (${e.message}). Is Cities: Skylines II running with the ClaudeCityBridge mod?` };
  } finally {
    clearTimeout(timer);
  }
}

function send(msg) { process.stdout.write(JSON.stringify(msg) + '\n'); }
function reply(id, result) { send({ jsonrpc: '2.0', id, result }); }
function fail(id, code, message) { send({ jsonrpc: '2.0', id, error: { code, message } }); }

const byName = new Map(TOOLS.map(t => [t.name, t]));

async function handle(msg) {
  const { id, method, params } = msg;
  if (id === undefined) return; // notification
  switch (method) {
    case 'initialize':
      return reply(id, {
        protocolVersion: params?.protocolVersion || PROTOCOL,
        capabilities: { tools: {} },
        serverInfo: { name: 'claude-city', version: '0.1.0' },
        instructions: 'Tools to play Cities: Skylines II through the ClaudeCityBridge mod. Start with cs2_status, then cs2_city / cs2_map to look around without screenshots. Coordinates are world metres (x east, z north); y is height.',
      });
    case 'ping':
      return reply(id, {});
    case 'tools/list':
      return reply(id, { tools: TOOLS.map(({ name, description, inputSchema }) => ({ name, description, inputSchema })) });
    case 'tools/call': {
      const tool = byName.get(params?.name);
      if (!tool) return fail(id, -32602, `Unknown tool: ${params?.name}`);
      const r = await callBridge(tool, params.arguments || {});
      return reply(id, { content: [{ type: 'text', text: r.text }], isError: !r.ok });
    }
    default:
      return fail(id, -32601, `Method not found: ${method}`);
  }
}

const rl = createInterface({ input: process.stdin });
rl.on('line', line => {
  if (!line.trim()) return;
  let msg;
  try { msg = JSON.parse(line); } catch { return fail(null, -32700, 'Parse error'); }
  handle(msg).catch(e => fail(msg.id ?? null, -32603, String(e?.stack || e)));
});
