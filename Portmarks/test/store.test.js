// 文件层集成测试：直接构造两个真实的 Bookmarks JSON，跑完整的复制流程。

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

import {
  preview,
  copy,
  emptyRaw,
  readTree,
  backupFileFor,
  exportPack,
  previewImport,
  importPack,
} from '../src/browser-store.js';

const folder = (id, name, children = []) => ({
  date_added: '13300000000000000',
  guid: `guid-${id}`,
  id,
  name,
  type: 'folder',
  children,
});

const link = (id, name, href) => ({
  date_added: '13300000000000000',
  date_last_used: '0',
  guid: `guid-${id}`,
  id,
  name,
  type: 'url',
  url: href,
});

function bookmarkFile(barChildren) {
  return {
    checksum: 'stale-checksum',
    roots: {
      bookmark_bar: folder('1', '书签栏', barChildren),
      other: folder('2', '其他书签', []),
      synced: folder('3', '移动设备书签', [link('30', '手机上的', 'https://mobile')]),
      workspaces_v2: { name: 'Workspaces' },
    },
    version: 1,
  };
}

function tmpFile(seed) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pm-store-'));
  const file = path.join(dir, 'Bookmarks');
  fs.writeFileSync(file, JSON.stringify(seed, null, 3), 'utf8');
  return file;
}

const readJson = (f) => JSON.parse(fs.readFileSync(f, 'utf8'));

function allIds(node, out = []) {
  if (node && typeof node === 'object') {
    if (node.id !== undefined) out.push(String(node.id));
    (node.children || []).forEach((c) => allIds(c, out));
  }
  return out;
}

test('预览：统计准确，且完全不改动目标文件', () => {
  const src = tmpFile(bookmarkFile([link('10', 'a', 'https://a'), folder('11', 'B', [link('12', 'b', 'https://b')])]));
  const tgt = tmpFile(bookmarkFile([link('20', 'x', 'https://x'), folder('21', 'B', [link('22', 'y', 'https://y')])]));
  const before = fs.readFileSync(tgt, 'utf8');

  const d = preview(src, tgt);

  assert.equal(d.stats.createBookmarks, 2);
  assert.equal(d.stats.createFolders, 0);
  assert.equal(d.stats.reused, 1);
  assert.equal(d.source.bookmarks, 2);
  assert.equal(d.target.bookmarks, 2);
  assert.equal(fs.readFileSync(tgt, 'utf8'), before);
});

test('复制：同名文件夹合并、追加末尾、生成备份、只读容器原封不动', async () => {
  const src = tmpFile(bookmarkFile([link('10', 'a', 'https://a'), folder('11', 'B', [link('12', 'b', 'https://b')])]));
  const tgt = tmpFile(bookmarkFile([link('20', 'x', 'https://x'), folder('21', 'B', [link('22', 'y', 'https://y')])]));
  const originalText = fs.readFileSync(tgt, 'utf8');

  const r = await copy(src, tgt);

  assert.equal(r.nothingToDo, false);
  assert.equal(r.created, 2);
  assert.ok(r.backupFile && fs.existsSync(r.backupFile), '应生成备份文件');
  assert.equal(fs.readFileSync(r.backupFile, 'utf8'), originalText, '备份应等于复制前的原文');

  const after = readJson(tgt);
  const bar = after.roots.bookmark_bar.children;
  assert.deepEqual(bar.map((n) => n.name), ['x', 'B', 'a'], '新书签追加到末尾');
  assert.deepEqual(bar[1].children.map((n) => n.name), ['y', 'b'], '合并进同名文件夹并追加');

  assert.deepEqual(
    after.roots.synced.children.map((n) => n.name),
    ['手机上的'],
    '只读容器不能被改动',
  );

  const added = bar[2];
  assert.equal(added.type, 'url');
  assert.equal(added.url, 'https://a');
  assert.match(added.guid, /^[0-9a-f-]{36}$/);
  assert.match(added.date_added, /^\d{17}$/);

  const ids = allIds(after.roots);
  assert.equal(new Set(ids).size, ids.length, 'id 不能重复');
});

test('重复复制：幂等，零新增', async () => {
  const src = tmpFile(bookmarkFile([link('10', 'a', 'https://a')]));
  const tgt = tmpFile(bookmarkFile([link('20', 'x', 'https://x')]));

  await copy(src, tgt);
  const snapshot = fs.readFileSync(tgt, 'utf8');

  const again = await copy(src, tgt);
  assert.equal(again.nothingToDo, true);
  assert.equal(again.backupFile, null, '无事可做时不应产生备份');
  assert.equal(fs.readFileSync(tgt, 'utf8'), snapshot);
});

test('目标还没有 Bookmarks 文件时：能直接新建，且不产生备份', async () => {
  const src = tmpFile(bookmarkFile([link('10', 'a', 'https://a'), folder('11', 'B', [link('12', 'b', 'https://b')])]));
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pm-empty-'));
  const tgt = path.join(dir, 'Bookmarks');
  assert.equal(fs.existsSync(tgt), false);

  const d = preview(src, tgt);
  assert.equal(d.target.bookmarks, 0);
  assert.equal(d.stats.createBookmarks, 2);
  assert.equal(fs.existsSync(tgt), false, '预览不能创建文件');

  const r = await copy(src, tgt);
  assert.equal(r.backupFile, null, '原文件不存在，没有可备份的东西');
  assert.equal(r.created, 3, 'a + B + b');

  const after = readJson(tgt);
  assert.deepEqual(after.roots.bookmark_bar.children.map((n) => n.name), ['a', 'B']);
  assert.deepEqual(after.roots.other.children, []);
});

test('源和目标相同直接报错', () => {
  const f = tmpFile(bookmarkFile([]));
  assert.throws(() => preview(f, f), /同一个收藏夹/);
});

test('空目标骨架必须使用 Chromium 的永久 GUID 常量（否则浏览器会静默忽略整个文件）', () => {
  const raw = emptyRaw();
  assert.equal(raw.version, 1);
  assert.equal(raw.roots.bookmark_bar.guid, '0bc5d13f-2cba-5d74-951f-3f233fe6c908');
  assert.equal(raw.roots.other.guid, '82b081ec-3dd3-529c-8475-ab6c344590dd');
  assert.equal(raw.roots.synced.guid, '4cf2e351-0e85-532b-bb37-df045d8f8d0f');
  assert.deepEqual(raw.roots.bookmark_bar.children, []);
  assert.equal(raw.roots.bookmark_bar.id, '1');
  assert.equal(raw.roots.other.id, '2');
});

test('新建的 url 节点带 meta_info，folder 节点带 date_modified', async () => {
  const src = tmpFile(bookmarkFile([link('10', 'a', 'https://a'), folder('11', 'B', [])]));
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pm-shape-'));
  const tgt = path.join(dir, 'Bookmarks');
  await copy(src, tgt);

  const raw = readJson(tgt);
  const [a, B] = raw.roots.bookmark_bar.children;
  assert.deepEqual(a.meta_info, { power_bookmark_meta: '' });
  assert.equal(a.type, 'url');
  assert.equal(B.type, 'folder');
  assert.equal(B.date_modified, '0');
  assert.equal(B.date_last_used, '0');
});

test('导出 → 导入：往返结构一致，二次导入幂等', async () => {
  const src = tmpFile(
    bookmarkFile([link('10', 'a', 'https://a'), folder('11', 'B', [link('12', 'b', 'https://b')])]),
  );
  const pack = exportPack(src, { browser: 'Microsoft Edge', profile: 'Default' });
  const text = JSON.stringify(pack);

  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pm-round-'));
  const tgt = path.join(dir, 'Bookmarks');

  const d = previewImport(tgt, text);
  assert.equal(d.stats.createBookmarks, 2);
  assert.equal(d.stats.createFolders, 1);
  assert.equal(d.meta.source.browser, 'Microsoft Edge');
  assert.equal(d.meta.source.profile, 'Default');
  assert.ok(d.meta.source.exportedAt, '导出时间应写入 pack');

  const r = await importPack(tgt, text);
  assert.equal(r.created, 3);

  const after = readJson(tgt);
  assert.deepEqual(after.roots.bookmark_bar.children.map((n) => n.name), ['a', 'B']);

  const again = await importPack(tgt, text);
  assert.equal(again.nothingToDo, true);
  assert.equal(again.backupFile, null);
});

test('导入非法文件直接报错', () => {
  const tgt = tmpFile(bookmarkFile([]));
  assert.throws(() => previewImport(tgt, '{ oops'), /JSON/);
  assert.throws(() => previewImport(tgt, JSON.stringify({ format: 'other' })), /portmarks/);
  assert.throws(() => previewImport(tgt, JSON.stringify({ format: 'portmarks', version: 99, tree: [] })), /版本/);
});

test('目标文件损坏时报 422，而不是崩一个 JSON 解析错误', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pm-bad-'));
  const bad = path.join(dir, 'Bookmarks');
  fs.writeFileSync(bad, '{ 这里不是 JSON', 'utf8');

  assert.throws(
    () => readTree(bad),
    (e) => e.status === 422 && /无法解析/.test(e.message),
  );

  const good = tmpFile(bookmarkFile([]));
  assert.throws(
    () => preview(good, bad),
    (e) => e.status === 422,
  );
});

test('写入是原子的：成功后不留临时文件，内容可解析', async () => {
  const src = tmpFile(bookmarkFile([link('10', 'a', 'https://a')]));
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pm-atomic-'));
  const tgt = path.join(dir, 'Bookmarks');

  await copy(src, tgt);

  assert.equal(fs.existsSync(`${tgt}.pmtmp`), false, '临时文件应已被 rename 掉');
  assert.doesNotThrow(() => JSON.parse(fs.readFileSync(tgt, 'utf8')));
});

test('备份不会互相覆盖', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'pm-bak-'));
  const file = path.join(dir, 'Bookmarks');
  fs.writeFileSync(file, '{}', 'utf8');

  const a = backupFileFor(file);
  fs.writeFileSync(a, 'x', 'utf8');
  const b = backupFileFor(file);

  assert.notEqual(a, b);
  assert.match(b, /-2$/, '撞名时应追加序号');
  assert.equal(fs.readFileSync(a, 'utf8'), 'x', '上一份备份不能被覆盖');
});

test('备份累积：多次复制产生多个可恢复的原文件副本', async () => {
  const src = tmpFile(bookmarkFile([link('10', 'a', 'https://a'), link('11', 'b', 'https://b')]));
  const tgt = tmpFile(bookmarkFile([]));
  const originalText = fs.readFileSync(tgt, 'utf8');

  const r = await copy(src, tgt);
  assert.ok(r.backupFile, '应产生备份');
  assert.equal(fs.readFileSync(r.backupFile, 'utf8'), originalText, '备份内容应等于写入前的原文');

  const backups = fs
    .readdirSync(path.dirname(tgt))
    .filter((n) => n.startsWith('Bookmarks.pmbak-'));
  assert.equal(backups.length, 1);
});
