// .portmarks 格式的序列化与校验。格式规范见 docs/spec.md

import { isFolder } from './model.js';

export const FORMAT = 'portmarks';
export const VERSION = 1;

/** 顶层容器顺序固定为：收藏夹栏、其他收藏夹 */
export const SLOTS = ['bar', 'other'];

export class PackError extends Error {
  constructor(message) {
    super(message);
    this.name = 'PackError';
    this.status = 400; // 用户给了个坏文件，不是服务器出错
  }
}

function toNode(n) {
  if (isFolder(n)) {
    return { type: 'folder', title: n.title, children: (n.children || []).map(toNode) };
  }
  return { type: 'url', title: n.title, url: n.url };
}

function fromNode(n) {
  if (n.type === 'folder') {
    return { title: n.title, children: (n.children || []).map(fromNode) };
  }
  return { title: n.title, url: n.url };
}

export function toPack(roots, source = {}) {
  return {
    format: FORMAT,
    version: VERSION,
    createdAt: new Date().toISOString(),
    source,
    tree: roots.map((r, i) => ({
      slot: SLOTS[i] || `slot${i}`,
      title: r.title,
      children: (r.children || []).map(toNode),
    })),
  };
}

export function stringifyPack(pack) {
  return JSON.stringify(pack, null, 2);
}

function validateNode(node, path) {
  if (!node || typeof node !== 'object') throw new PackError(`${path} 不是对象`);
  if (typeof node.title !== 'string') throw new PackError(`${path} 缺少 title`);
  if (node.type === 'folder') {
    if (node.children != null && !Array.isArray(node.children)) {
      throw new PackError(`${path}.children 不是数组`);
    }
    (node.children || []).forEach((c, i) => validateNode(c, `${path}.children[${i}]`));
    return;
  }
  if (node.type === 'url') {
    if (typeof node.url !== 'string' || !node.url.trim()) {
      throw new PackError(`${path} 缺少合法 url`);
    }
    return;
  }
  throw new PackError(`${path} 的 type 非法：${node.type}`);
}

export function parsePack(text) {
  let raw;
  try {
    raw = JSON.parse(text);
  } catch {
    throw new PackError('内容不是合法的 JSON');
  }
  if (!raw || typeof raw !== 'object') throw new PackError('内容不是对象');
  if (raw.format !== FORMAT) throw new PackError(`不是 ${FORMAT} 格式`);
  if (raw.version !== VERSION) throw new PackError(`版本不支持：${raw.version}`);
  if (!Array.isArray(raw.tree)) throw new PackError('缺少 tree 数组');
  if (raw.tree.length > SLOTS.length) {
    throw new PackError(`tree 顶层容器数量异常（最多 ${SLOTS.length} 个，实际 ${raw.tree.length} 个）`);
  }

  raw.tree.forEach((r, i) => validateNode({ ...r, type: 'folder' }, `tree[${i}]`));

  const roots = raw.tree.map((r, i) => ({
    id: SLOTS[i] || `slot${i}`,
    title: r.title,
    children: (r.children || []).map(fromNode),
  }));

  return { roots, meta: { createdAt: raw.createdAt, source: raw.source } };
}
