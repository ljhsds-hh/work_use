// 书签树的规范化、比对与统计。全部为纯函数，不依赖 chrome API，便于单测。

const SEP = '\u0000';

/** 有 url 即为书签，否则为文件夹 */
export function isFolder(node) {
  return typeof node.url !== 'string';
}

/** 把 chrome 的 BookmarkTreeNode 收敛成最小结构，丢掉 dateAdded 等无关字段 */
export function normalize(node) {
  const out = { id: String(node.id), title: node.title ?? '' };
  if (isFolder(node)) {
    out.children = (node.children || []).map(normalize);
  } else {
    out.url = node.url;
  }
  return out;
}

/** chrome.bookmarks.getTree() 的返回是 [根]，根下两个容器：共 2 个顶层 */
export function normalizeRoots(tree) {
  const root = Array.isArray(tree) ? tree[0] : tree;
  return (root?.children || []).map(normalize);
}

/** 去重键 = 网址 + 标题，两者都要相同才算同一条书签 */
export function bookmarkKey(node) {
  return `${node.url}${SEP}${node.title}`;
}

export function countNodes(nodes) {
  let folders = 0;
  let bookmarks = 0;
  const walk = (list) => {
    for (const n of list) {
      if (isFolder(n)) {
        folders += 1;
        walk(n.children || []);
      } else {
        bookmarks += 1;
      }
    }
  };
  walk(nodes);
  return { folders, bookmarks, total: folders + bookmarks };
}

/**
 * 只取两个可写顶层容器。Chromium 里除「收藏夹栏」「其他收藏夹」外，
 * 还可能存在「移动设备书签」「Workspaces」等只读容器，写进去会失败。
 * 优先按固定 id（1=栏, 2=其他）识别，识别不到再按顺序兜底。
 */
export function mainRoots(roots) {
  const byId = new Map(roots.map((r) => [r.id, r]));
  const bar = byId.get('1');
  const other = byId.get('2');
  if (bar && other) return [bar, other];
  return roots.slice(0, 2);
}

/**
 * 顶层容器配对：优先按 id（Chromium 里固定是 1=收藏夹栏, 2=其他收藏夹），
 * 配不上再按顺序兜底，避免两个浏览器容器顺序不一致时错位。
 */
export function pairRoots(srcRoots, tgtRoots) {
  const byId = new Map(tgtRoots.map((r) => [r.id, r]));
  const pairs = [];
  srcRoots.forEach((src, i) => {
    const tgt = byId.get(src.id) || tgtRoots[i] || null;
    if (tgt) pairs.push({ src, tgt });
  });
  return pairs;
}

/** 同名冲突时生成 "F (2)" / "F (3)" */
export function uniqueTitle(base, taken) {
  let n = 2;
  let candidate = `${base} (${n})`;
  while (taken.has(candidate)) {
    n += 1;
    candidate = `${base} (${n})`;
  }
  return candidate;
}
