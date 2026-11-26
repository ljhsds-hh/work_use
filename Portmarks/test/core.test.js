import test from 'node:test';
import assert from 'node:assert/strict';

import { createMock, snapRoots } from './mock.js';
import { normalizeRoots, mainRoots } from '../src/core/model.js';
import { toPack, stringifyPack, parsePack } from '../src/core/packer.js';
import { buildPlan, planStats } from '../src/core/planner.js';
import { applyPlan, undoImport } from '../src/core/applier.js';

const BAR = '书签栏';
const OTHER = '其他书签';

function packFrom(barChildren, otherChildren = []) {
  return toPack(
    [
      { title: BAR, children: barChildren },
      { title: OTHER, children: otherChildren },
    ],
    { browser: 'edge' },
  );
}

async function runImport(mock, pack) {
  const { roots } = parsePack(stringifyPack(pack));
  const plan = buildPlan(roots, normalizeRoots(mock.getTree()));
  const result = await applyPlan(plan, mock);
  return { ...result, plan };
}

function targetWithBar(children) {
  return createMock([
    { id: '1', title: BAR, children },
    { id: '2', title: OTHER, children: [] },
  ]);
}

test('首次导入：同名文件夹合并，新书签追加到末尾', async () => {
  const mock = targetWithBar([
    { title: 'x', url: 'https://x' },
    { title: 'B', children: [{ title: 'y', url: 'https://y' }] },
  ]);

  const { stats } = await runImport(
    mock,
    packFrom([
      { title: 'a', url: 'https://a' },
      { title: 'B', children: [{ title: 'b', url: 'https://b' }] },
    ]),
  );

  assert.equal(stats.createBookmarks, 2);
  assert.equal(stats.createFolders, 0);
  assert.equal(stats.reused, 1);

  const [bar] = snapRoots(mock);
  assert.deepEqual(bar, {
    title: BAR,
    children: [
      { title: 'x', url: 'https://x' },
      { title: 'B', children: [{ title: 'y', url: 'https://y' }, { title: 'b', url: 'https://b' }] },
      { title: 'a', url: 'https://a' },
    ],
  });
});

test('二次导入：完全幂等，零新增', async () => {
  const mock = targetWithBar([{ title: 'x', url: 'https://x' }]);
  const pack = packFrom([
    { title: 'a', url: 'https://a' },
    { title: 'B', children: [{ title: 'b', url: 'https://b' }] },
  ]);

  await runImport(mock, pack);
  const before = snapRoots(mock);

  const { stats } = await runImport(mock, pack);
  assert.deepEqual(stats, { createFolders: 0, createBookmarks: 0, skipped: 2, reused: 1, failed: 0 });
  assert.deepEqual(snapRoots(mock), before);
});

test('只新增差异：源里多 5 条，目标只多这 5 条', async () => {
  const base = [{ title: 'a', url: 'https://a' }];
  const mock = targetWithBar([{ title: 'x', url: 'https://x' }]);

  await runImport(mock, packFrom(base));
  const afterFirst = snapRoots(mock);

  const extra = ['n1', 'n2', 'n3', 'n4', 'n5'].map((t) => ({ title: t, url: `https://${t}` }));
  const { stats } = await runImport(mock, packFrom([...base, ...extra]));
  assert.equal(stats.createBookmarks, 5);
  assert.equal(stats.skipped, 1);

  const bar = snapRoots(mock)[0];
  assert.equal(bar.children.length, afterFirst[0].children.length + 5);
});

test('同网址不同标题：算两条书签，照样新增', async () => {
  const mock = targetWithBar([{ title: '旧标题', url: 'https://same' }]);
  const { stats } = await runImport(mock, packFrom([{ title: '新标题', url: 'https://same' }]));

  assert.equal(stats.createBookmarks, 1);
  assert.equal(stats.skipped, 0);
  assert.deepEqual(snapRoots(mock)[0].children, [
    { title: '旧标题', url: 'https://same' },
    { title: '新标题', url: 'https://same' },
  ]);
});

test('类型冲突：源是文件夹、目标是同名书签 → 新建成 F (2)', async () => {
  const mock = targetWithBar([{ title: 'F', url: 'https://f' }]);
  await runImport(mock, packFrom([{ title: 'F', children: [{ title: 'inner', url: 'https://in' }] }]));

  const titles = snapRoots(mock)[0].children.map((c) => c.title);
  assert.deepEqual(titles, ['F', 'F (2)']);
  const created = snapRoots(mock)[0].children.find((c) => c.title === 'F (2)');
  assert.deepEqual(created.children, [{ title: 'inner', url: 'https://in' }]);
});

test('两个顶层容器都搬', async () => {
  const mock = targetWithBar([]);
  await runImport(
    mock,
    packFrom([{ title: 'a', url: 'https://a' }], [{ title: 'C', children: [{ title: 'c', url: 'https://c' }] }]),
  );

  const roots = snapRoots(mock);
  assert.deepEqual(roots[0].children, [{ title: 'a', url: 'https://a' }]);
  assert.deepEqual(roots[1].children, [{ title: 'C', children: [{ title: 'c', url: 'https://c' }] }]);
});

test('空文件夹照建', async () => {
  const mock = targetWithBar([]);
  const { stats } = await runImport(mock, packFrom([{ title: '空壳', children: [] }]));
  assert.equal(stats.createFolders, 1);
  assert.deepEqual(snapRoots(mock)[0].children, [{ title: '空壳', children: [] }]);
});

test('撤销：只删本次新增，原有数据一分不动', async () => {
  const mock = targetWithBar([
    { title: 'x', url: 'https://x' },
    { title: 'B', children: [{ title: 'y', url: 'https://y' }] },
  ]);
  const before = snapRoots(mock);

  const { created, stats } = await runImport(
    mock,
    packFrom([
      { title: 'a', url: 'https://a' },
      { title: 'B', children: [{ title: 'b', url: 'https://b' }] },
      { title: '新文件夹', children: [{ title: 'z', url: 'https://z' }] },
    ]),
  );
  assert.equal(stats.createBookmarks, 3);
  assert.equal(stats.createFolders, 1);

  const removed = await undoImport(created, mock);
  assert.equal(removed, 4);
  assert.deepEqual(snapRoots(mock), before);
});

test('pack 往返：序列化再解析结构不变', () => {
  const src = [
    { title: 'a', url: 'https://a' },
    { title: 'B', children: [{ title: 'b', url: 'https://b' }, { title: '空', children: [] }] },
  ];
  const { roots } = parsePack(stringifyPack(packFrom(src)));

  const strip = (nodes) =>
    nodes.map((n) =>
      n.url !== undefined ? { title: n.title, url: n.url } : { title: n.title, children: strip(n.children || []) },
    );

  assert.deepEqual(strip(roots), [
    { title: BAR, children: src },
    { title: OTHER, children: [] },
  ]);
});

test('非法文件被拒绝', () => {
  assert.throws(() => parsePack('not json'), /JSON/);
  assert.throws(() => parsePack('{"format":"other"}'), /portmarks/);
  assert.throws(
    () => parsePack(JSON.stringify({ format: 'portmarks', version: 1, tree: [{ title: 'x', children: [{ type: 'url', title: 'y' }] }] })),
    /url/,
  );
});

test('源里两个同名文件夹（目标没有）：只建一个，书签不重复', async () => {
  const mock = targetWithBar([]);
  const { stats } = await runImport(
    mock,
    packFrom([
      { title: 'A', children: [{ title: 'x', url: 'https://x' }] },
      { title: 'A', children: [{ title: 'x', url: 'https://x' }, { title: 'y', url: 'https://y' }] },
    ]),
  );

  assert.equal(stats.createFolders, 1);
  assert.equal(stats.createBookmarks, 2);
  assert.deepEqual(snapRoots(mock)[0].children, [
    {
      title: 'A',
      children: [
        { title: 'x', url: 'https://x' },
        { title: 'y', url: 'https://y' },
      ],
    },
  ]);
});

test('源里两个同名文件夹并进已有同名文件夹：不重复', async () => {
  const mock = targetWithBar([{ title: 'A', children: [{ title: 'x', url: 'https://x' }] }]);
  const { stats } = await runImport(
    mock,
    packFrom([
      { title: 'A', children: [{ title: 'y', url: 'https://y' }] },
      { title: 'A', children: [{ title: 'y', url: 'https://y' }, { title: 'z', url: 'https://z' }] },
    ]),
  );

  assert.equal(stats.createBookmarks, 2);
  assert.equal(stats.reused, 2);
  assert.deepEqual(
    snapRoots(mock)[0].children[0].children.map((c) => c.title),
    ['x', 'y', 'z'],
  );
});

test('单个节点失败：其余继续导入，已成功部分仍可撤销', async () => {
  const mock = createMock(
    [
      { id: '1', title: BAR, children: [] },
      { id: '2', title: OTHER, children: [] },
    ],
    { failWhen: (n) => n.title === '坏' },
  );

  const { created, stats, errors } = await runImport(
    mock,
    packFrom([
      { title: '好1', url: 'https://1' },
      { title: '坏', url: 'https://bad' },
      { title: '好2', url: 'https://2' },
    ]),
  );

  assert.equal(stats.createBookmarks, 2);
  assert.equal(stats.failed, 1);
  assert.equal(errors.length, 1);
  assert.match(errors[0].message, /模拟创建失败/);

  await undoImport(created, mock);
  assert.deepEqual(snapRoots(mock)[0].children, []);
});

test('顶层容器识别：多出只读容器、顺序被打乱也能正确配对', () => {
  const bar = { id: '1', title: '书签栏', children: [] };
  const other = { id: '2', title: '其他书签', children: [] };
  const synced = { id: 'synced', title: '移动设备书签', children: [] };

  assert.deepEqual(
    mainRoots([bar, other, synced]).map((r) => r.id),
    ['1', '2'],
  );
  assert.deepEqual(
    mainRoots([synced, other, bar]).map((r) => r.id),
    ['1', '2'],
  );
});

test('pack 里超过 2 个顶层容器时直接拒绝，不静默丢弃', () => {
  assert.throws(
    () =>
      parsePack(
        JSON.stringify({ format: 'portmarks', version: 1, tree: [{ title: 'a' }, { title: 'b' }, { title: 'c' }] }),
      ),
    /顶层容器/,
  );
});

test('空标题、表情符号、尖括号等特殊字符不影响去重与还原', async () => {
  const weird = '🎉 表情 & <b>标签</b> "引号"';
  const mock = targetWithBar([{ title: '', url: 'https://empty-title' }]);

  const { stats } = await runImport(
    mock,
    packFrom([
      { title: '', url: 'https://empty-title' },
      { title: weird, url: 'https://u' },
    ]),
  );

  assert.equal(stats.skipped, 1);
  assert.equal(stats.createBookmarks, 1);
  assert.equal(snapRoots(mock)[0].children[1].title, weird);
});

test('100 层深嵌套能完整还原', async () => {
  let node = { title: 'leaf', url: 'https://leaf' };
  for (let i = 0; i < 100; i += 1) node = { title: `L${i}`, children: [node] };

  const mock = targetWithBar([]);
  const { stats } = await runImport(mock, packFrom([node]));
  assert.equal(stats.createFolders, 100);

  let cur = snapRoots(mock)[0].children[0];
  for (let i = 99; i >= 0; i -= 1) {
    assert.equal(cur.title, `L${i}`);
    cur = cur.children[0];
  }
  assert.deepEqual(cur, { title: 'leaf', url: 'https://leaf' });
});

test('计划统计与执行统计一致', async () => {
  const mock = targetWithBar([{ title: 'B', children: [] }]);
  const { roots } = parsePack(stringifyPack(packFrom([
    { title: 'a', url: 'https://a' },
    { title: 'B', children: [{ title: 'b', url: 'https://b' }] },
  ])));
  const plan = buildPlan(roots, normalizeRoots(mock.getTree()));
  const planned = planStats(plan);
  const { stats } = await applyPlan(plan, mock);

  assert.deepEqual(
    { createFolders: stats.createFolders, createBookmarks: stats.createBookmarks, skipped: stats.skipped, reused: stats.reused },
    planned,
  );
});
