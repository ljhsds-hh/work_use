// 用真实浏览器验证某个 profile 到底加载了哪些书签（通过 CDP 问 chrome.bookmarks）。
// 用法：node tools/verify-in-browser.mjs "<user-data-dir>"
// 会临时起一个 headless 实例，不碰你正在用的浏览器。

import { spawn } from 'node:child_process';
import path from 'node:path';

const EXE =
  process.env.CHROME_EXE || path.join(process.env.PROGRAMFILES || 'C:\\Program Files', 'Google', 'Chrome', 'Application', 'chrome.exe');

const dir = process.argv[2];
if (!dir) {
  console.error('用法: node tools/verify-in-browser.mjs "<user-data-dir>"');
  process.exit(1);
}
const port = Number(process.env.PORT || 9333);

const child = spawn(
  EXE,
  [
    '--headless=new',
    `--user-data-dir=${dir}`,
    '--no-first-run',
    '--no-default-browser-check',
    '--disable-gpu',
    `--remote-debugging-port=${port}`,
    'about:blank',
  ],
  { stdio: 'ignore' },
);

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function waitVersion() {
  for (let i = 0; i < 40; i += 1) {
    try {
      const r = await fetch(`http://127.0.0.1:${port}/json/version`);
      if (r.ok) return await r.json();
    } catch {
      /* 还没起来 */
    }
    await sleep(250);
  }
  throw new Error('CDP 端口没起来');
}

const version = await waitVersion();
const ws = new WebSocket(version.webSocketDebuggerUrl);
await new Promise((res, rej) => {
  ws.addEventListener('open', res);
  ws.addEventListener('error', rej);
});

let seq = 0;
const pending = new Map();
ws.addEventListener('message', (ev) => {
  const msg = JSON.parse(ev.data);
  if (msg.id && pending.has(msg.id)) {
    pending.get(msg.id)(msg);
    pending.delete(msg.id);
  }
});
const send = (method, params = {}, sessionId) =>
  new Promise((resolve) => {
    seq += 1;
    pending.set(seq, resolve);
    ws.send(JSON.stringify({ id: seq, method, params, ...(sessionId ? { sessionId } : {}) }));
  });

const created = await send('Target.createTarget', { url: 'chrome://bookmarks/' });
const { targetId } = created.result;
const attached = await send('Target.attachToTarget', { targetId, flatten: true });
const { sessionId } = attached.result;
await sleep(2200);

const probe = `(async () => {
  const tree = await chrome.bookmarks.getTree();
  const out = [];
  const walk = (n, depth) => {
    if (n.url) out.push('  '.repeat(depth) + (n.title ?? n.name) + ' | ' + n.url);
    else if (depth > 0) out.push('  '.repeat(depth) + '[' + (n.title ?? n.name) + ']');
    (n.children || []).forEach((c) => walk(c, depth + 1));
  };
  tree.forEach((r) => walk(r, 0));
  return JSON.stringify(out);
})()`;

const r = await send('Runtime.evaluate', { expression: probe, awaitPromise: true, returnByValue: true }, sessionId);
const list = JSON.parse(r.result?.result?.value || '[]');
console.log(`浏览器实际加载 ${list.length} 项：`);
list.forEach((line) => console.log(line));

ws.close();
child.kill();
process.exit(0);
