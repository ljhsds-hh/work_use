// 本地服务：只监听 127.0.0.1，无外部网络访问。
// 界面在 public/，所有文件操作都在 browser-store.js 里。

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { exec } from 'node:child_process';

import { discoverProfiles, preview, copy, isRunning, exportPack, previewImport, importPack } from './browser-store.js';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = path.resolve(HERE, '..');
const PUBLIC = path.join(ROOT, 'public');

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
};

function send(res, code, payload, type = 'application/json; charset=utf-8') {
  const body = typeof payload === 'string' || Buffer.isBuffer(payload) ? payload : JSON.stringify(payload);
  res.writeHead(code, { 'content-type': type, 'cache-control': 'no-store' });
  res.end(body);
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on('data', (c) => {
      size += c.length;
      if (size > 8 << 20) {
        reject(new Error('请求体过大'));
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on('end', () => {
      const text = Buffer.concat(chunks).toString('utf8');
      if (!text) return resolve({});
      try {
        resolve(JSON.parse(text));
      } catch {
        reject(new Error('请求体不是合法 JSON'));
      }
    });
    req.on('error', reject);
  });
}

function pickProfile(id) {
  const p = discoverProfiles().find((x) => x.id === id);
  if (!p) throw Object.assign(new Error(`找不到配置: ${id}`), { status: 404 });
  return p;
}

/* 本地服务的安全边界：
   1. Host 必须是回环地址 —— 防止 DNS rebinding 把 127.0.0.1 解析到外部域名
   2. 带 Origin 时必须与 Host 一致 —— 挡住跨站请求
   3. 写操作必须带自定义头 —— 浏览器跨站无法在不触发预检的情况下携带，等于二次保险 */
const LOOPBACK = /^(127\.0\.0\.1|localhost)(:\d+)?$/i;

function rejectReason(req) {
  const host = req.headers.host || '';
  if (!LOOPBACK.test(host)) return '只接受本机访问';

  const origin = req.headers.origin;
  if (origin) {
    let originHost = '';
    try {
      originHost = new URL(origin).host;
    } catch {
      return '非法来源';
    }
    if (originHost !== host) return '拒绝跨站请求';
  }

  if (req.method === 'POST' && req.headers['x-pm-client'] !== 'ui') return '缺少客户端标识';
  return null;
}

/* 写操作串行：两个标签页同时点「开始复制」会互相覆盖读-改-写的结果 */
let writing = false;

async function exclusive(fn) {
  if (writing) throw Object.assign(new Error('上一次写入还没结束，请稍候再试'), { status: 409 });
  writing = true;
  try {
    return await fn();
  } finally {
    writing = false;
  }
}

function checkPair(src, tgt) {
  if (src.id === tgt.id) return '源和目标不能是同一个收藏夹';
  if (src.browser === tgt.browser) return '源和目标不能是同一个浏览器，请换一个';
  return null;
}

async function api(req, res, url) {
  if (url.pathname === '/api/profiles' && req.method === 'GET') {
    return send(res, 200, { profiles: discoverProfiles() });
  }

  if (url.pathname === '/api/preview' && req.method === 'POST') {
    const { sourceId, targetId } = await readBody(req);
    const src = pickProfile(sourceId);
    const tgt = pickProfile(targetId);
    const bad = checkPair(src, tgt);
    if (bad) return send(res, 400, { error: bad });

    const data = preview(src.path, tgt.path);
    return send(res, 200, {
      ...data,
      targetRunning: isRunning(tgt.browser),
      sourceRunning: isRunning(src.browser),
    });
  }

  if (url.pathname === '/api/apply' && req.method === 'POST') {
    const { sourceId, targetId } = await readBody(req);
    const src = pickProfile(sourceId);
    const tgt = pickProfile(targetId);
    const bad = checkPair(src, tgt);
    if (bad) return send(res, 400, { error: bad });

    if (isRunning(tgt.browser)) {
      return send(res, 409, {
        error: `${tgt.browserName} 正在运行，请先完全退出（包括托盘图标），再点一次`,
      });
    }

    const result = await exclusive(() => copy(src.path, tgt.path));
    return send(res, 200, {
      source: `${src.browserName} · ${src.label}`,
      target: `${tgt.browserName} · ${tgt.label}`,
      backupFile: result.backupFile,
      stats: result.stats,
      created: result.created || 0,
      nothingToDo: result.nothingToDo,
      errors: result.errors,
    });
  }

  if (url.pathname === '/api/export' && req.method === 'POST') {
    const { sourceId } = await readBody(req);
    const src = pickProfile(sourceId);
    const pack = exportPack(src.path, { browser: src.browserName, profile: src.label });
    return send(res, 200, { pack, profile: `${src.browserName} · ${src.label}` });
  }

  if (url.pathname === '/api/import/preview' && req.method === 'POST') {
    const { targetId, pack } = await readBody(req);
    const tgt = pickProfile(targetId);
    if (typeof pack !== 'string' || !pack.trim()) return send(res, 400, { error: '没有收到文件内容' });
    const data = previewImport(tgt.path, pack);
    return send(res, 200, { ...data, targetRunning: isRunning(tgt.browser) });
  }

  if (url.pathname === '/api/import/apply' && req.method === 'POST') {
    const { targetId, pack } = await readBody(req);
    const tgt = pickProfile(targetId);
    if (typeof pack !== 'string' || !pack.trim()) return send(res, 400, { error: '没有收到文件内容' });
    if (isRunning(tgt.browser)) {
      return send(res, 409, {
        error: `${tgt.browserName} 正在运行，请先完全退出（包括托盘图标），再点一次`,
      });
    }
    const result = await exclusive(() => importPack(tgt.path, pack));
    return send(res, 200, {
      target: `${tgt.browserName} · ${tgt.label}`,
      backupFile: result.backupFile,
      stats: result.stats,
      created: result.created || 0,
      nothingToDo: result.nothingToDo,
      errors: result.errors,
    });
  }

  return send(res, 404, { error: 'not found' });
}

function serveStatic(req, res, url) {
  const rel = url.pathname === '/' ? '/index.html' : url.pathname;
  const file = path.join(PUBLIC, path.normalize(rel).replace(/^([/\\])+/, ''));
  const inside = path.relative(PUBLIC, file);
  if (inside.startsWith('..') || path.isAbsolute(inside)) return send(res, 403, { error: 'forbidden' });
  if (!fs.existsSync(file) || !fs.statSync(file).isFile()) return send(res, 404, 'not found', 'text/plain');
  return send(res, 200, fs.readFileSync(file), MIME[path.extname(file)] || 'application/octet-stream');
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, 'http://127.0.0.1');
  try {
    const denied = rejectReason(req);
    if (denied) return send(res, 403, { error: denied });
    if (url.pathname.startsWith('/api/')) return await api(req, res, url);
    return serveStatic(req, res, url);
  } catch (e) {
    return send(res, e.status || 500, { error: e.message || String(e) });
  }
});

function openBrowser(url) {
  exec(`cmd /c start "" "${url}"`, { windowsHide: true });
}

const BASE_PORT = Number(process.env.PORT || 7801);
let attempt = 0;

server.on('error', (e) => {
  if (e.code === 'EADDRINUSE' && attempt < 9) {
    attempt += 1;
    server.listen(BASE_PORT + attempt, '127.0.0.1');
    return;
  }
  console.error('启动失败：', e.message);
  process.exit(1);
});

server.on('listening', () => {
  const { port } = server.address();
  const url = `http://127.0.0.1:${port}`;
  console.log('');
  console.log('  Portmarks 已启动');
  console.log(`  地址: ${url}`);
  console.log('  关闭这个窗口即退出程序');
  console.log('');
  if (!process.argv.includes('--no-open')) openBrowser(url);
});

server.listen(BASE_PORT, '127.0.0.1');
