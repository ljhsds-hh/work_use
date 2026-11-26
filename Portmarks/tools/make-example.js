// 生成示例包：node tools/make-example.js
import fs from 'node:fs';
import path from 'node:path';
import { toPack, stringifyPack } from '../src/core/packer.js';

const tree = [
  {
    title: '收藏夹栏',
    children: [
      { title: 'MDN Web Docs', url: 'https://developer.mozilla.org/zh-CN/' },
      {
        title: '技术',
        children: [
          { title: 'Node.js 文档', url: 'https://nodejs.org/docs/latest/api/' },
          { title: 'V8 Blog', url: 'https://v8.dev/blog' },
          {
            title: '前端',
            children: [{ title: 'Can I use', url: 'https://caniuse.com/' }],
          },
        ],
      },
      { title: '空文件夹示例', children: [] },
    ],
  },
  {
    title: '其他收藏夹',
    children: [
      {
        title: '待读',
        children: [{ title: 'Chrome 扩展开发', url: 'https://developer.chrome.com/docs/extensions/' }],
      },
    ],
  },
];

const pack = toPack(tree, { browser: 'edge', ua: 'example' });
const out = path.resolve(import.meta.dirname, '../packs/example.portmarks');
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, stringifyPack(pack), 'utf8');
console.log('written:', out);
