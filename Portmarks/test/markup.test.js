// 静态结构检查：这类错误跑不出异常，但会让某个控件静默失效。
// 起因：遮罩层和滑块曾经共用 id="bgScrim"，getElementById 只返回第一个，
// 于是监听器挂在了 div 上，拖滑块完全没反应。

import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const root = path.resolve(import.meta.dirname, '..');
const html = fs.readFileSync(path.join(root, 'public/index.html'), 'utf8');
const js = fs.readFileSync(path.join(root, 'public/app.js'), 'utf8');

const htmlIds = [...html.matchAll(/\bid="([^"]+)"/g)].map((m) => m[1]);
const defined = new Set(htmlIds);

test('HTML 里没有重复 id', () => {
  const seen = new Set();
  const dup = htmlIds.filter((id) => (seen.has(id) ? true : (seen.add(id), false)));
  assert.deepEqual(dup, [], `重复的 id: ${dup.join(', ')}`);
});

test('app.js 里 $() 引用的 id 都存在于 HTML', () => {
  const used = [...new Set([...js.matchAll(/\$\('([^']+)'\)/g)].map((m) => m[1]))];
  const missing = used.filter((id) => !defined.has(id));
  assert.deepEqual(missing, [], `缺失的 id: ${missing.join(', ')}`);
});

test('动态拼接的 #sourceXxx / #targetXxx 也都存在', () => {
  const suffixes = [...new Set([...js.matchAll(/\$\{side\}([A-Za-z]+)/g)].map((m) => m[1]))];
  assert.ok(suffixes.length > 0, '应当存在动态 id');
  const missing = [];
  for (const side of ['source', 'target']) {
    for (const suffix of suffixes) {
      if (!defined.has(`${side}${suffix}`)) missing.push(`${side}${suffix}`);
    }
  }
  assert.deepEqual(missing, [], `缺失的动态 id: ${missing.join(', ')}`);
});
