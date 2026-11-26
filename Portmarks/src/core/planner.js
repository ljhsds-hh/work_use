// 差异规划：把「源树 + 目标树」编译成一份可预览、可执行的导入计划。
// 纯函数，不写任何数据。
//
// 关键点：同一个「目标父文件夹」可能被源里多个同名文件夹合并进来，
// 所以去重键必须按父文件夹共享，不能每次递归都重建，否则会重复建书签。

import { isFolder, bookmarkKey, pairRoots, uniqueTitle } from './model.js';

export function buildPlan(srcRoots, tgtRoots) {
  const ctx = { additions: new Map(), seq: 0 };
  return pairRoots(srcRoots, tgtRoots).map(({ src, tgt }) => ({
    action: 'reuse',
    title: src.title,
    targetId: tgt.id,
    children: planChildren(src.children || [], tgt.children || [], ctx, `t:${tgt.id}`),
  }));
}

/** 每个目标父文件夹一份去重键集合，本次计划内共享 */
function bucket(ctx, parentKey) {
  let set = ctx.additions.get(parentKey);
  if (!set) {
    set = new Set();
    ctx.additions.set(parentKey, set);
  }
  return set;
}

function planChildren(srcList, tgtList, ctx, parentKey) {
  const keys = bucket(ctx, parentKey);
  const tgtFolderByTitle = new Map();
  const takenTitles = new Set();

  for (const t of tgtList) {
    if (isFolder(t)) {
      if (!tgtFolderByTitle.has(t.title)) tgtFolderByTitle.set(t.title, t);
    } else {
      keys.add(bookmarkKey(t));
    }
    takenTitles.add(t.title);
  }

  const createdFolderByTitle = new Map();
  const out = [];

  for (const node of srcList) {
    if (isFolder(node)) {
      // 1) 目标是同名文件夹 → 合并进去（去重键沿用该目标文件夹的集合）
      const hit = tgtFolderByTitle.get(node.title);
      if (hit) {
        out.push({
          action: 'reuse',
          title: node.title,
          targetId: hit.id,
          children: planChildren(node.children || [], hit.children || [], ctx, `t:${hit.id}`),
        });
        continue;
      }
      // 2) 本轮已为同名源文件夹建过新文件夹 → 并进同一个，不再新建
      const planned = createdFolderByTitle.get(node.title);
      if (planned) {
        planned.children.push(...planChildren(node.children || [], [], ctx, planned.parentKey));
        continue;
      }
      // 3) 目标里同名的是书签（类型冲突）→ 新建 F (2)
      const title = takenTitles.has(node.title) ? uniqueTitle(node.title, takenTitles) : node.title;
      takenTitles.add(title);
      const parentKeyOfNew = `n:${(ctx.seq += 1)}`;
      const created = {
        action: 'create',
        title,
        parentKey: parentKeyOfNew,
        children: planChildren(node.children || [], [], ctx, parentKeyOfNew),
      };
      createdFolderByTitle.set(node.title, created);
      out.push(created);
      continue;
    }

    // 书签：网址 + 标题都相同才算已存在
    const key = bookmarkKey(node);
    if (keys.has(key)) {
      out.push({ action: 'skip', title: node.title, url: node.url });
    } else {
      out.push({ action: 'create', title: node.title, url: node.url });
      keys.add(key);
    }
  }

  return out;
}

export function planStats(roots) {
  const stats = { createFolders: 0, createBookmarks: 0, skipped: 0, reused: 0 };
  const walk = (nodes) => {
    for (const n of nodes) {
      if (n.action === 'skip') stats.skipped += 1;
      else if (n.action === 'reuse') {
        stats.reused += 1;
        walk(n.children);
      } else if (n.url !== undefined) stats.createBookmarks += 1;
      else {
        stats.createFolders += 1;
        walk(n.children);
      }
    }
  };
  roots.forEach((r) => walk(r.children));
  return stats;
}

/** 预览用：每个顶层容器的增量摘要 */
export function planSummary(roots) {
  return roots.map((r) => {
    const s = planStats([{ children: r.children }]);
    return { title: r.title, ...s };
  });
}
