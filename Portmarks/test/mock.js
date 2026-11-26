// 内存版书签树，模拟 chrome.bookmarks 的三个方法，用于跑单测。

function clone(n) {
  if (n.url !== undefined) return { id: n.id, parentId: n.parentId, title: n.title, url: n.url };
  return {
    id: n.id,
    parentId: n.parentId,
    title: n.title,
    children: (n.children || []).map(clone),
  };
}

export function createMock(seed, options = {}) {
  let seq = 1;
  const all = new Map();

  const build = (spec, parentId) => {
    const id = spec.id ?? `n${seq++}`;
    const node = { id, parentId, title: spec.title ?? '' };
    if (spec.url !== undefined) node.url = spec.url;
    else node.children = (spec.children || []).map((c) => build(c, id));
    all.set(id, node);
    return node;
  };

  const root = { id: '0', parentId: null, title: '', children: [] };
  all.set('0', root);
  seed.forEach((s) => root.children.push(build(s, '0')));

  const find = (id) => all.get(String(id));

  const dropTree = (node) => {
    (node.children || []).forEach(dropTree);
    all.delete(node.id);
  };

  return {
    getTree() {
      return [clone(root)];
    },

    async create({ parentId, title, url }) {
      if (options.failWhen && options.failWhen({ parentId, title, url })) {
        throw new Error(`模拟创建失败: ${title}`);
      }
      const parent = find(parentId);
      if (!parent || !parent.children) throw new Error(`父节点不存在或不是文件夹: ${parentId}`);
      const id = `c${seq++}`;
      const node = { id, parentId: parent.id, title };
      if (url !== undefined) node.url = url;
      else node.children = [];
      parent.children.push(node);
      all.set(id, node);
      return clone(node);
    },

    async remove(id, isFolder) {
      const node = find(id);
      if (!node) throw new Error(`节点不存在: ${id}`);
      if (isFolder) {
        if (!node.children) throw new Error(`不是文件夹: ${id}`);
        dropTree(node);
      } else {
        if (node.children) throw new Error(`是文件夹，不能按书签删除: ${id}`);
        all.delete(node.id);
      }
      const parent = find(node.parentId);
      parent.children = parent.children.filter((c) => c.id !== node.id);
    },
  };
}

export function snapshot(node) {
  if (node.url !== undefined) return { title: node.title, url: node.url };
  return { title: node.title, children: (node.children || []).map(snapshot) };
}

export function snapRoots(mock) {
  return mock.getTree()[0].children.map(snapshot);
}
