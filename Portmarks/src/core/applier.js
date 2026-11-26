// 计划执行：只做「新建」，绝不修改或删除目标已有的任何书签。
// api 由外部注入，便于测试。
//
// 单个节点失败不中断整体：失败项记入 errors，已成功的项照样记入 created，
// 保证「导入了一半」时仍然可以完整撤销。

export async function applyPlan(roots, api, onProgress) {
  const created = [];
  const errors = [];
  const stats = { createFolders: 0, createBookmarks: 0, skipped: 0, reused: 0, failed: 0 };

  async function walk(nodes, parentId) {
    for (const n of nodes) {
      if (n.action === 'skip') {
        stats.skipped += 1;
        continue;
      }
      if (n.action === 'reuse') {
        stats.reused += 1;
        await walk(n.children, n.targetId);
        continue;
      }
      try {
        if (n.url !== undefined) {
          const made = await api.create({ parentId, title: n.title, url: n.url });
          created.push({ id: made.id, kind: 'bookmark' });
          stats.createBookmarks += 1;
        } else {
          const made = await api.create({ parentId, title: n.title });
          created.push({ id: made.id, kind: 'folder' });
          stats.createFolders += 1;
          await walk(n.children, made.id);
        }
      } catch (e) {
        stats.failed += 1;
        errors.push({ title: n.title, url: n.url, message: e?.message || String(e) });
      }
      if (onProgress) onProgress(stats);
    }
  }

  for (const r of roots) await walk(r.children, r.targetId);
  return { created, stats, errors };
}

/** 撤销：只删本次新建的节点，倒序执行，先子后父 */
export async function undoImport(created, api) {
  let removed = 0;
  for (let i = created.length - 1; i >= 0; i -= 1) {
    const c = created[i];
    try {
      await api.remove(c.id, c.kind === 'folder');
      removed += 1;
    } catch {
      // 已被用户手动删除或移动，忽略
    }
  }
  return removed;
}
