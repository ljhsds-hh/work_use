// 视觉回归工具：对一个正在运行的实例截几张图，方便改样式后自查。
// 用法：node tools/shoot-ui.mjs http://127.0.0.1:7801 ./shots
import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const EXE = 'C://Program Files\\Google\\Chrome\\Application\\chrome.exe';
const url = process.argv[2];
const outDir = process.argv[3];
fs.mkdirSync(outDir, { recursive: true });
const port = 9378;
const profile = path.join(process.env.TEMP, 'pm-shoot-profile');

const child = spawn(EXE, ['--headless=new', `--user-data-dir=${profile}`, '--no-first-run', '--disable-gpu',
  '--hide-scrollbars', `--remote-debugging-port=${port}`, 'about:blank'], { stdio: 'ignore' });

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let ver;
for (let i = 0; i < 50; i += 1) {
  try { const r = await fetch(`http://127.0.0.1:${port}/json/version`); if (r.ok) { ver = await r.json(); break; } } catch {}
  await sleep(250);
}
const ws = new WebSocket(ver.webSocketDebuggerUrl);
await new Promise((res, rej) => { ws.addEventListener('open', res); ws.addEventListener('error', rej); });

let seq = 0;
const pending = new Map();
const errors = [];
ws.addEventListener('message', (ev) => {
  const m = JSON.parse(ev.data);
  if (m.id && pending.has(m.id)) { pending.get(m.id)(m); pending.delete(m.id); return; }
  if (m.method === 'Runtime.exceptionThrown') errors.push('EXCEPTION ' + (m.params.exceptionDetails?.exception?.description || ''));
  if (m.method === 'Log.entryAdded' && m.params.entry.level === 'error') errors.push('LOG ' + m.params.entry.text);
});
const send = (method, params = {}, sessionId) => new Promise((r) => {
  seq += 1; pending.set(seq, r);
  ws.send(JSON.stringify({ id: seq, method, params, ...(sessionId ? { sessionId } : {}) }));
});

const { result: { targetId } } = await send('Target.createTarget', { url });
const { result: { sessionId } } = await send('Target.attachToTarget', { targetId, flatten: true });
await send('Runtime.enable', {}, sessionId);
await send('Log.enable', {}, sessionId);
await send('Page.enable', {}, sessionId);
await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 900, deviceScaleFactor: 1, mobile: false }, sessionId);
await sleep(2000);

const evaluate = async (expr) => {
  const r = await send('Runtime.evaluate', { expression: expr, awaitPromise: true, returnByValue: true }, sessionId);
  if (r.result?.exceptionDetails) errors.push('EVAL ' + JSON.stringify(r.result.exceptionDetails.text));
  return r.result?.result?.value;
};
const normalizeState = async () => evaluate(`(() => {
  document.querySelectorAll('.warn').forEach(el => el.remove());
  const btn = document.getElementById('apply');
  if (btn.disabled) btn.disabled = false;
  const nums = [...document.querySelectorAll('.metric__num')].slice(0, 2).map(n => Number(n.textContent) || 0);
  document.getElementById('hint').textContent = '将写入 ' + nums.reduce((a, b) => a + b, 0) + ' 个新条目';
  return 'normalized';
})()`);
const shot = async (name) => {
  const { result } = await send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true }, sessionId);
  fs.writeFileSync(path.join(outDir, name), Buffer.from(result.data, 'base64'));
};

console.log('初始:', await evaluate(`JSON.stringify({
  app: !!document.querySelector('.app'),
  theme: document.documentElement.dataset.theme,
  accent: document.documentElement.dataset.accent,
  src: document.querySelectorAll('#source option').length,
  tgt: [...document.querySelectorAll('#target option')].map(o => o.textContent),
  metrics: document.querySelectorAll('.metrics .metric').length,
  hint: document.getElementById('hint').textContent
})`));
await normalizeState();
await shot('01-light.png');

console.log('面板截图:', await evaluate(`(async () => {
  document.getElementById('lookBtn').click();
  await new Promise(r => setTimeout(r, 300));
  return 'ok';
})()`));
await shot('02-sheet.png');
await evaluate(`document.querySelector('#sheet [data-close]').click()`);
await sleep(250);

console.log('上传背景:', await evaluate(`(async () => {
  const c = document.createElement('canvas');
  c.width = 900; c.height = 620;
  const g = c.getContext('2d');
  const grad = g.createLinearGradient(0, 0, 900, 620);
  grad.addColorStop(0, '#9fc0e8'); grad.addColorStop(0.45, '#c9b6e4'); grad.addColorStop(1, '#f2c9a4');
  g.fillStyle = grad; g.fillRect(0, 0, 900, 620);
  for (let i = 0; i < 22; i++) {
    g.fillStyle = 'rgba(255,255,255,' + (0.1 + Math.random() * 0.22) + ')';
    g.beginPath(); g.arc(Math.random() * 900, Math.random() * 620, 30 + Math.random() * 90, 0, 6.28); g.fill();
  }
  const blob = await new Promise(r => c.toBlob(r, 'image/png'));
  const dt = new DataTransfer();
  dt.items.add(new File([blob], 'bg.png', { type: 'image/png' }));
  const input = document.getElementById('bgFile');
  input.files = dt.files;
  input.dispatchEvent(new Event('change', { bubbles: true }));
  await new Promise(r => setTimeout(r, 800));
  return JSON.stringify({ hasBg: document.body.classList.contains('has-bg'), stored: (localStorage.getItem('portmarks.look.v1')||'').length });
})()`));
await normalizeState();
await shot('03-light-bg.png');

console.log('切深色 + 换色:', await evaluate(`(async () => {
  document.getElementById('lookBtn').click();
  await new Promise(r => setTimeout(r, 250));
  document.querySelector('#modeSeg button[data-mode="dark"]').click();
  document.querySelector('.swatch[data-accent="lavender"]').click();
  await new Promise(r => setTimeout(r, 350));
  document.querySelector('#sheet [data-close]').click();
  await new Promise(r => setTimeout(r, 250));
  return JSON.stringify({ theme: document.documentElement.dataset.theme, accent: document.documentElement.dataset.accent });
})()`));
await normalizeState();
await shot('04-dark-bg.png');

await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 860, deviceScaleFactor: 2, mobile: true }, sessionId);
await sleep(500);
await shot('05-mobile.png');

console.log('控制台错误:', errors.length ? errors : '无');
ws.close(); child.kill(); process.exit(0);
