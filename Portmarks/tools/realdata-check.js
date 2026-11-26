// 用浏览器真实的 Bookmarks 文件做端到端校验。
// 用法：node tools/realdata-check.js

import fs from 'node:fs';
import path from 'node:path';

import { toPack, stringifyPack, parsePack } from '../src/core/packer.js';
import { buildPlan, planStats } from '../src/core/planner.js';
import { applyPlan, undoImport } from '../src/core/applier.js';
import { countNodes, mainRoots, normalizeRoots } from '../src/core/model.js';
import { createMock, snapRoots } from '../test/mock.js';

const FILES = {
  edge: path.join(process.env.LOCALAPPDATA, 'Microsoft/Edge/User Data/Default/Bookmarks'),
  chrome: path.join(process.env.LOCALAPPDATA, 'Google/Chrome/User Data/Default/Bookmarks'),
};

const conv = (n) =>
  n.type === 'folder'
    ? { title: n.name ?? '', children: (n.children || []).map(conv) }
    : { title: n.name ?? '', url: n.url };

function load(browser) {
  const file = FILES[browser];
  if (!file || !fs.existsSync(file)) return null;
  const raw = JSON.parse(fs.readFileSync(file, 'utf8'));
  const r = raw.roots || {};
  const roots = [];
  if (r.bookmark_bar) roots.push(conv(r.bookmark_bar));
  if (r.other) roots.push(conv(r.other));
  return { roots, rawRootKeys: Object.keys(r) };
}

function asTarget(roots) {
  return createMock(
    roots.map((c, i) => ({ id: String(i + 1), title: c.title, children: c.children || [] })),
  );
}

function depth(nodes, d = 1) {
  let max = d;
  for (const n of nodes) {
    if (n.children) max = Math.max(max, depth(n.children, d + 1));
  }
  return max;
}

const line = (s) => console.log(s);

line('=== 真实数据 ===');
const data = {};
for (const b of ['edge', 'chrome']) {
  const d = load(b);
  if (!d) {
    line(`${b}: 没有本地 Bookmarks 文件`);
    continue;
  }
  data[b] = d;
  const filtered = mainRoots(d.roots);
  const c = countNodes(filtered.flatMap((r) => r.children || []));
  line(
    `${b}: 文件根节点 [${d.rawRootKeys.join(', ')}] → 纳入 ${filtered.length} 个容器；` +
      `${c.bookmarks} 书签 / ${c.folders} 文件夹 / 最大深度 ${depth(filtered)}`,
  );
  if (d.roots.length > 2) {
    line(`   ⚠ 有 ${d.roots.length} 个顶层容器，已只取前 2 个（其余为只读的特殊容器）`);
  }
}

let failures = 0;
const check = (name, ok, detail = '') => {
  line(`${ok ? '  ✓' : '  ✗'} ${name}${detail ? ` — ${detail}` : ''}`);
  if (!ok) failures += 1;
};

const src = data.edge ?? data.chrome;
if (src) {
  const roots = mainRoots(src.roots);
  const c = countNodes(roots.flatMap((r) => r.children || []));

  line('\n=== 场景 1：源导入自身（必须 100% 跳过） ===');
  {
    const mock = asTarget(roots);
    const before = snapRoots(mock);
    const pack = toPack(roots, { browser: 'edge' });
    const { roots: parsed } = parsePack(stringifyPack(pack));
    const { created, stats } = await applyPlan(buildPlan(parsed, roots), mock);

    check('零新增', stats.createBookmarks === 0 && stats.createFolders === 0, JSON.stringify(stats));
    check('全部识别为已存在', stats.skipped === c.bookmarks, `跳过 ${stats.skipped} / 共 ${c.bookmarks}`);
    check('数据未被改动', JSON.stringify(snapRoots(mock)) === JSON.stringify(before));
    check('撤销清单为空（无可撤销项）', created.length === 0);
  }

  line('\n=== 场景 2：导入到空浏览器（必须全量且可撤销） ===');
  {
    const empty = createMock([
      { id: '1', title: '书签栏', children: [] },
      { id: '2', title: '其他书签', children: [] },
    ]);
    const pack = toPack(roots, { browser: 'edge' });
    const { roots: parsed } = parsePack(stringifyPack(pack));
    const t0 = performance.now();
    const { created, stats, errors } = await applyPlan(
      buildPlan(parsed, normalizeRoots(empty.getTree())),
      empty,
      null,
    );
    const ms = (performance.now() - t0).toFixed(1);

    check('无失败项', errors.length === 0, errors.length ? JSON.stringify(errors[0]) : '');
    check(
      '书签数一致',
      stats.createBookmarks === c.bookmarks,
      `创建 ${stats.createBookmarks} / 源 ${c.bookmarks}`,
    );
    check('文件夹数一致', stats.createFolders === c.folders, `创建 ${stats.createFolders} / 源 ${c.folders}`);
    line(`    耗时 ${ms} ms，共创建 ${created.length} 个节点`);

    const removed = await undoImport(created, empty);
    check('撤销删除数 = 创建数', removed === created.length, `${removed} / ${created.length}`);
    check(
      '撤销后目标为空',
      JSON.stringify(snapRoots(empty)) ===
        JSON.stringify([
          { title: '书签栏', children: [] },
          { title: '其他书签', children: [] },
        ]),
    );
  }

  line('\n=== 场景 3：往返两次（幂等） ===');
  {
    const mock = asTarget(roots);
    const packText = stringifyPack(toPack(roots, { browser: 'edge' }));
    const parsed = parsePack(packText).roots;
    await applyPlan(buildPlan(parsed, roots), mock);
    const afterFirst = snapRoots(mock);
    const { stats } = await applyPlan(buildPlan(parsePack(packText).roots, roots), mock);
    check('第二次零新增', stats.createBookmarks === 0 && stats.createFolders === 0, JSON.stringify(stats));
    check('第二次不改数据', JSON.stringify(snapRoots(mock)) === JSON.stringify(afterFirst));
  }
}

line('\n=== 场景 4：大树（3000 书签 / 300 文件夹） ===');
{
  const mkBig = (folders, per) =>
    Array.from({ length: folders }, (_, i) => ({
      title: `F${i}`,
      children: Array.from({ length: per }, (_, j) => ({ title: `b${i}-${j}`, url: `https://e.com/${i}/${j}` })),
    }));
  const big = [{ title: '书签栏', children: mkBig(300, 10) }, { title: '其他书签', children: [] }];
  const empty = createMock([
    { id: '1', title: '书签栏', children: [] },
    { id: '2', title: '其他书签', children: [] },
  ]);
  const tgt = normalizeRoots(empty.getTree());

  const tp = performance.now();
  const plan = buildPlan(big, tgt);
  const planMs = (performance.now() - tp).toFixed(0);
  const planned = planStats(plan);

  const ta = performance.now();
  const { created } = await applyPlan(plan, empty, null);
  const applyMs = (performance.now() - ta).toFixed(0);

  const c = countNodes(big.flatMap((r) => r.children || []));
  line(
    `  源 ${c.bookmarks} 书签 / ${c.folders} 文件夹 → 生成计划 ${planMs} ms，执行 ${applyMs} ms，创建 ${created.length} 个节点`,
  );
  check('计划统计正确', planned.createBookmarks === c.bookmarks && planned.createFolders === c.folders);
  check('全部创建成功', created.length === c.total, `${created.length} / ${c.total}`);
}

line('\n=== 场景 5：目标返回 3 个顶层容器（含只读） ===');
{
  const roots3 = [
    { id: 'synced', title: '移动设备书签', children: [{ title: 'm', url: 'https://m' }] },
    { id: '1', title: '书签栏', children: [] },
    { id: '2', title: '其他书签', children: [] },
  ];
  const picked = mainRoots(roots3);
  check('只保留两个可写容器', picked.length === 2, picked.map((r) => r.id).join(','));
  check('且顺序为 栏 → 其他', picked.map((r) => r.id).join(',') === '1,2');
}

line(`\n结论：${failures === 0 ? '全部通过' : `${failures} 项失败`}`);
process.exit(failures === 0 ? 0 : 1);
