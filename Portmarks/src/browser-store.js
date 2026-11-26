// 直接操作浏览器 profile 里的 Bookmarks JSON 文件。
// 只读源文件；写目标前必须有备份，且调用方要保证目标浏览器已关闭。

import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

import { countNodes } from './core/model.js';
import { toPack, parsePack } from './core/packer.js';
import { buildPlan, planStats, planSummary } from './core/planner.js';
import { applyPlan } from './core/applier.js';

const LOCAL = process.env.LOCALAPPDATA || '';

export const BROWSERS = [
  { key: 'edge', name: 'Microsoft Edge', root: path.join(LOCAL, 'Microsoft', 'Edge', 'User Data') },
  { key: 'chrome', name: 'Google Chrome', root: path.join(LOCAL, 'Google', 'Chrome', 'User Data') },
];

/* ---------------- 进程检测 ---------------- */

export function isRunning(browserKey) {
  const image = browserKey === 'edge' ? 'msedge.exe' : 'chrome.exe';
  try {
    const out = execFileSync('tasklist', ['/FI', `IMAGENAME eq ${image}`, '/FO', 'CSV', '/NH'], {
      stdio: ['ignore', 'pipe', 'ignore'],
    });
    return Buffer.from(out).includes(image);
  } catch {
    return false;
  }
}

/* ---------------- profile 发现 ---------------- */

function readProfileNames(root) {
  const map = new Map();
  try {
    const state = JSON.parse(fs.readFileSync(path.join(root, 'Local State'), 'utf8'));
    const cache = state?.profile?.info_cache || {};
    for (const [dir, info] of Object.entries(cache)) {
      if (info?.name) map.set(dir, info.name);
    }
  } catch {
    // 没有 Local State 就用目录名
  }
  return map;
}

export function discoverProfiles() {
  const out = [];
  for (const b of BROWSERS) {
    if (!b.root || !fs.existsSync(b.root)) continue;
    const names = readProfileNames(b.root);
    const running = isRunning(b.key);
    let dirs = [];
    try {
      dirs = fs.readdirSync(b.root);
    } catch {
      continue;
    }
    for (const dir of dirs) {
      if (!/^(Default|Profile \d+)$/.test(dir)) continue;
      const dirPath = path.join(b.root, dir);
      const file = path.join(dirPath, 'Bookmarks');
      const hasFile = fs.existsSync(file);
      // 没用过的空目录不算配置；Chrome 要等你加第一个书签才生成 Bookmarks 文件，
      // 所以「有 Preferences 但还没 Bookmarks」也要算进来，否则新建的浏览器根本选不到。
      if (!hasFile && !fs.existsSync(path.join(dirPath, 'Preferences'))) continue;
      let counts = null;
      try {
        const { roots } = readTree(file);
        counts = countNodes(roots.flatMap((r) => r.children || []));
      } catch {
        counts = null;
      }
      out.push({
        id: `${b.key}:${dir}`,
        browser: b.key,
        browserName: b.name,
        profileDir: dir,
        label: names.get(dir) || dir,
        path: file,
        hasFile,
        running,
        counts,
      });
    }
  }
  return out;
}

/* ---------------- 读写 ---------------- */

function toModel(n) {
  if (n.type === 'folder') {
    return { id: String(n.id), title: n.name ?? '', children: (n.children || []).map(toModel) };
  }
  return { id: String(n.id), title: n.name ?? '', url: n.url };
}

/**
 * Chromium 里三个永久节点的 GUID 是写死的常量（见 bookmark_codec.h）。
 * 手写 Bookmarks 文件时必须用这些值，否则浏览器会判定文件无效、静默忽略。
 */
const PERMANENT_GUIDS = {
  bookmark_bar: '0bc5d13f-2cba-5d74-951f-3f233fe6c908',
  other: '82b081ec-3dd3-529c-8475-ab6c344590dd',
  synced: '4cf2e351-0e85-532b-bb37-df045d8f8d0f',
};

/** 目标浏览器还没有 Bookmarks 文件时，用这个骨架作为写入起点 */
export function emptyRaw() {
  const t = nowChromeTime();
  const root = (id, name, guid) => ({
    children: [],
    date_added: t,
    date_last_used: '0',
    date_modified: '0',
    guid,
    id,
    name,
    type: 'folder',
  });
  return {
    checksum: '',
    roots: {
      bookmark_bar: root('1', '书签栏', PERMANENT_GUIDS.bookmark_bar),
      other: root('2', '其他书签', PERMANENT_GUIDS.other),
      synced: root('3', '移动设备书签', PERMANENT_GUIDS.synced),
    },
    version: 1,
  };
}

export function readTree(file) {
  if (!fs.existsSync(file)) {
    const raw = emptyRaw();
    return { raw, roots: rootsOf(raw), exists: false };
  }

  let raw;
  try {
    raw = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (e) {
    const err = new Error(`收藏夹文件无法解析（${path.basename(file)}）：${e.message}`);
    err.status = 422;
    throw err;
  }
  if (!raw || typeof raw !== 'object' || typeof raw.roots !== 'object' || !raw.roots) {
    const err = new Error(`收藏夹文件结构异常（${path.basename(file)}），缺少 roots`);
    err.status = 422;
    throw err;
  }
  return { raw, roots: rootsOf(raw), exists: true };
}

function rootsOf(raw) {
  const r = raw.roots || {};
  const roots = [];
  if (r.bookmark_bar) roots.push(toModel(r.bookmark_bar));
  if (r.other) roots.push(toModel(r.other));
  return roots;
}

/**
 * 原子写入：先写临时文件再改名。
 * 直接覆盖原文件时若进程被杀，会留下半截 JSON —— 浏览器的收藏夹就废了。
 */
function writeRaw(file, raw) {
  const tmp = `${file}.pmtmp`;
  try {
    fs.writeFileSync(tmp, JSON.stringify(raw, null, 3), 'utf8');
    fs.renameSync(tmp, file);
  } catch (e) {
    try {
      fs.unlinkSync(tmp);
    } catch {
      /* 临时文件可能没生成 */
    }
    throw e;
  }
}

const EPOCH_DELTA = 11644473600000000n;

function nowChromeTime() {
  return String(BigInt(Date.now()) * 1000n + EPOCH_DELTA);
}

function eachNode(raw, fn, parent = null) {
  const roots = raw.roots || {};
  const visit = (node, par) => {
    fn(node, par);
    (node.children || []).forEach((c) => visit(c, node));
  };
  Object.values(roots).forEach((r) => {
    if (r && typeof r === 'object') visit(r, parent);
  });
}

function findById(raw, id) {
  let hit = null;
  eachNode(raw, (n) => {
    if (!hit && String(n.id) === String(id)) hit = n;
  });
  return hit;
}

function maxNodeId(raw) {
  let max = 0;
  eachNode(raw, (n) => {
    const v = Number(n.id);
    if (Number.isFinite(v) && v > max) max = v;
  });
  return max;
}

/** 把「新建节点」适配成 core 里 applyPlan 需要的三个方法，直接作用在 JSON 上 */
export function makeJsonApi(raw) {
  let seq = maxNodeId(raw);
  return {
    async create({ parentId, title, url }) {
      const parent = findById(raw, parentId);
      if (!parent) throw new Error(`找不到父节点: ${parentId}`);
      if (parent.type !== 'folder') throw new Error(`父节点不是文件夹: ${parentId}`);
      seq += 1;
      const base = { date_added: nowChromeTime(), guid: randomUUID(), id: String(seq), name: title };
      const node =
        url === undefined
          ? { ...base, children: [], date_last_used: '0', date_modified: '0', type: 'folder' }
          : {
              ...base,
              date_last_used: '0',
              meta_info: { power_bookmark_meta: '' },
              type: 'url',
              url,
            };
      parent.children = parent.children || [];
      parent.children.push(node);
      return { id: node.id };
    },
  };
}

/* ---------------- 备份 ---------------- */

/** 每次写入前留一份原文件的副本，命名形如 Bookmarks.pmbak-20261126-201530 */
export function backupFileFor(file) {
  const stamp = new Date()
    .toISOString()
    .replace(/[-:]/g, '')
    .replace(/\..+$/, '')
    .replace('T', '-');

  // 同一秒内连续操作会撞名，撞了就加序号，绝不能覆盖上一份备份
  let candidate = `${file}.pmbak-${stamp}`;
  let n = 2;
  while (fs.existsSync(candidate)) {
    candidate = `${file}.pmbak-${stamp}-${n}`;
    n += 1;
  }
  return candidate;
}

/* ---------------- 预览与执行 ---------------- */

export function preview(sourceFile, targetFile) {
  if (path.resolve(sourceFile) === path.resolve(targetFile)) {
    throw new Error('源和目标不能是同一个收藏夹');
  }
  const src = readTree(sourceFile);
  const tgt = readTree(targetFile);
  const plan = buildPlan(src.roots, tgt.roots);
  return {
    stats: planStats(plan),
    summary: planSummary(plan),
    source: countNodes(src.roots.flatMap((r) => r.children || [])),
    target: countNodes(tgt.roots.flatMap((r) => r.children || [])),
  };
}

export async function copy(sourceFile, targetFile) {
  if (path.resolve(sourceFile) === path.resolve(targetFile)) {
    throw new Error('源和目标不能是同一个收藏夹');
  }
  const src = readTree(sourceFile);
  const tgt = readTree(targetFile);
  const plan = buildPlan(src.roots, tgt.roots);
  const planned = planStats(plan);

  if (planned.createBookmarks === 0 && planned.createFolders === 0) {
    return { nothingToDo: true, stats: planned, backupFile: null, errors: [] };
  }

  // 目标浏览器还没生成 Bookmarks 文件时无从备份，直接新建
  const backupFile = fs.existsSync(targetFile) ? backupFileFor(targetFile) : null;
  if (backupFile) fs.copyFileSync(targetFile, backupFile);

  const { created, stats, errors } = await applyPlan(plan, makeJsonApi(tgt.raw));
  writeRaw(targetFile, tgt.raw);

  return { nothingToDo: false, created: created.length, stats, errors, backupFile };
}

/* ---------------- 导出 / 导入（.portmarks） ---------------- */

export function exportPack(sourceFile, info = {}) {
  const src = readTree(sourceFile);
  return toPack(src.roots, {
    ...info,
    exportedAt: new Date().toISOString(),
    counts: countNodes(src.roots.flatMap((r) => r.children || [])),
  });
}

export function previewImport(targetFile, packText) {
  const { roots, meta } = parsePack(packText);
  const tgt = readTree(targetFile);
  const plan = buildPlan(roots, tgt.roots);
  return {
    stats: planStats(plan),
    summary: planSummary(plan),
    target: countNodes(tgt.roots.flatMap((r) => r.children || [])),
    meta,
  };
}

export async function importPack(targetFile, packText) {
  const { roots } = parsePack(packText);
  const tgt = readTree(targetFile);
  const plan = buildPlan(roots, tgt.roots);
  const planned = planStats(plan);

  if (planned.createBookmarks === 0 && planned.createFolders === 0) {
    return { nothingToDo: true, created: 0, stats: planned, errors: [], backupFile: null };
  }

  const backupFile = fs.existsSync(targetFile) ? backupFileFor(targetFile) : null;
  if (backupFile) fs.copyFileSync(targetFile, backupFile);

  const { created, stats, errors } = await applyPlan(plan, makeJsonApi(tgt.raw));
  writeRaw(targetFile, tgt.raw);

  return { nothingToDo: false, created: created.length, stats, errors, backupFile };
}
