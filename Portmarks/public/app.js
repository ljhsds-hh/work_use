/* Portmarks 前端
   一、外观（主题色 / 明暗 / 背景图）
   二、数据（配置、预览、复制）
   三、视图渲染
*/

const $ = (id) => document.getElementById(id);

const clamp = (v, min, max) => Math.min(max, Math.max(min, Number(v) || 0));

/* ============================================================
   一、外观
   ============================================================ */

const ACCENTS = [
  { key: 'mist', name: '雾蓝' },
  { key: 'sage', name: '鼠尾草' },
  { key: 'rose', name: '藕粉' },
  { key: 'apricot', name: '杏桃' },
  { key: 'lavender', name: '薰衣草' },
  { key: 'celadon', name: '青瓷' },
  { key: 'sand', name: '沙金' },
];

const LOOK_KEY = 'portmarks.look.v1';
const DEFAULT_LOOK = {
  accent: 'mist',
  mode: 'auto',
  bg: { data: '', opacity: 85, blur: 8, scrim: 45 },
};

function loadLook() {
  try {
    const raw = JSON.parse(localStorage.getItem(LOOK_KEY) || 'null');
    if (!raw) return structuredClone(DEFAULT_LOOK);
    return {
      accent: ACCENTS.some((a) => a.key === raw.accent) ? raw.accent : 'mist',
      mode: ['auto', 'light', 'dark'].includes(raw.mode) ? raw.mode : 'auto',
      bg: {
        data: typeof raw.bg?.data === 'string' ? raw.bg.data : '',
        opacity: clamp(raw.bg?.opacity ?? 85, 10, 100),
        blur: clamp(raw.bg?.blur ?? 8, 0, 24),
        scrim: clamp(raw.bg?.scrim ?? 45, 0, 95),
      },
    };
  } catch {
    return structuredClone(DEFAULT_LOOK);
  }
}

let look = loadLook();

function saveLook() {
  try {
    localStorage.setItem(LOOK_KEY, JSON.stringify(look));
  } catch {
    setStatus('背景图太大，浏览器存不下，换一张小一点的吧', 'err');
  }
}

const systemDark = () => window.matchMedia('(prefers-color-scheme: dark)').matches;

function resolvedTheme() {
  return look.mode === 'auto' ? (systemDark() ? 'dark' : 'light') : look.mode;
}

function applyLook() {
  const root = document.documentElement;
  root.dataset.accent = look.accent;
  root.dataset.mode = look.mode;
  root.dataset.theme = resolvedTheme();

  root.style.setProperty('--bg-opacity', String(look.bg.opacity / 100));
  root.style.setProperty('--bg-blur', `${look.bg.blur}px`);
  root.style.setProperty('--bg-scrim', String(look.bg.scrim / 100));

  if (look.bg.data) {
    $('bgImage').style.backgroundImage = `url("${look.bg.data}")`;
    document.body.classList.add('has-bg');
  } else {
    $('bgImage').style.backgroundImage = 'none';
    document.body.classList.remove('has-bg');
  }

  $('bgOpacity').value = String(look.bg.opacity);
  $('bgOpacityOut').textContent = `${look.bg.opacity}%`;
  $('bgBlur').value = String(look.bg.blur);
  $('bgBlurOut').textContent = `${look.bg.blur}px`;
  $('bgScrim').value = String(look.bg.scrim);
  $('bgScrimOut').textContent = `${look.bg.scrim}%`;
  $('bgSliders').setAttribute('aria-disabled', look.bg.data ? 'false' : 'true');
  $('bgClear').disabled = !look.bg.data;

  document.querySelectorAll('.swatch').forEach((b) => {
    b.setAttribute('aria-pressed', String(b.dataset.accent === look.accent));
  });
  document.querySelectorAll('#modeSeg button').forEach((b) => {
    b.setAttribute('aria-pressed', String(b.dataset.mode === look.mode));
  });
}

function buildSwatches() {
  const host = $('swatches');
  host.textContent = '';
  ACCENTS.forEach((a) => {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'swatch';
    b.dataset.accent = a.key;
    b.setAttribute('aria-label', a.name);
    b.title = a.name;
    b.addEventListener('click', () => {
      look.accent = a.key;
      applyLook();
      saveLook();
    });
    host.appendChild(b);
  });
}

async function fileToDataUrl(file) {
  const bitmap = await createImageBitmap(file);
  const max = 1600;
  const scale = Math.min(1, max / Math.max(bitmap.width, bitmap.height));
  const w = Math.max(1, Math.round(bitmap.width * scale));
  const h = Math.max(1, Math.round(bitmap.height * scale));
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  canvas.getContext('2d').drawImage(bitmap, 0, 0, w, h);
  bitmap.close?.();
  return canvas.toDataURL('image/jpeg', 0.82);
}

const sheetFocus = new Map();

function openSheet(id, trigger) {
  const el = $(id);
  sheetFocus.set(id, trigger || document.activeElement);
  el.hidden = false;
  const first = el.querySelector('[data-close]');
  if (first) first.focus();
}

function closeSheet(id) {
  const el = $(id);
  if (el.hidden) return;
  el.hidden = true;
  const prev = sheetFocus.get(id);
  if (prev instanceof HTMLElement) prev.focus();
}

function bindSheets() {
  document.querySelectorAll('.sheet').forEach((el) => {
    el.querySelectorAll('[data-close]').forEach((btn) => {
      btn.addEventListener('click', () => closeSheet(el.id));
    });
  });
  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    document.querySelectorAll('.sheet:not([hidden])').forEach((el) => closeSheet(el.id));
  });
}

function initLook() {
  buildSwatches();

  document.querySelectorAll('#modeSeg button').forEach((b) => {
    b.addEventListener('click', () => {
      look.mode = b.dataset.mode;
      applyLook();
      saveLook();
    });
  });

  $('modeBtn').addEventListener('click', () => {
    look.mode = resolvedTheme() === 'dark' ? 'light' : 'dark';
    applyLook();
    saveLook();
  });

  $('lookBtn').addEventListener('click', () => openSheet('sheet', $('lookBtn')));

  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (look.mode === 'auto') applyLook();
  });

  $('bgFile').addEventListener('change', async (ev) => {
    const file = ev.target.files?.[0];
    ev.target.value = '';
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      setStatus('请选择图片文件', 'err');
      return;
    }
    try {
      look.bg.data = await fileToDataUrl(file);
      applyLook();
      saveLook();
      setStatus('背景图已应用');
    } catch {
      setStatus('这张图片读不出来，换一张试试', 'err');
    }
  });

  $('bgClear').addEventListener('click', () => {
    look.bg.data = '';
    applyLook();
    saveLook();
  });

  const bindSlider = (id, out, key, unit) => {
    $(id).addEventListener('input', (e) => {
      look.bg[key] = clamp(e.target.value, Number(e.target.min), Number(e.target.max));
      $(out).textContent = `${look.bg[key]}${unit}`;
      applyLook();
    });
    $(id).addEventListener('change', saveLook);
  };

  bindSlider('bgOpacity', 'bgOpacityOut', 'opacity', '%');
  bindSlider('bgBlur', 'bgBlurOut', 'blur', 'px');
  bindSlider('bgScrim', 'bgScrimOut', 'scrim', '%');

  $('resetLook').addEventListener('click', () => {
    look = structuredClone(DEFAULT_LOOK);
    applyLook();
    saveLook();
  });

  applyLook();
}

/* ============================================================
   二、数据
   ============================================================ */

let profiles = [];

async function api(path, body) {
  const res = await fetch(path, {
    method: body ? 'POST' : 'GET',
    headers: body ? { 'content-type': 'application/json', 'x-pm-client': 'ui' } : { 'x-pm-client': 'ui' },
    body: body ? JSON.stringify(body) : undefined,
  });
  const data = await res.json().catch(() => ({}));
  if (!res.ok) throw new Error(data.error || `请求失败（${res.status}）`);
  return data;
}

const sourceProfile = () => profiles.find((p) => p.id === $('source').value);
const targetProfile = () => profiles.find((p) => p.id === $('target').value);

function targetsForSource() {
  const src = sourceProfile();
  return src ? profiles.filter((p) => p.browser !== src.browser) : profiles.slice();
}

async function load() {
  try {
    profiles = (await api('/api/profiles')).profiles;
  } catch (e) {
    setDiffNotice(`读取配置失败：${e.message}`, true);
    return;
  }

  if (!profiles.length) {
    setDiffNotice('没有找到 Edge 或 Chrome 的收藏夹配置，请先打开过一次浏览器。', true);
    return;
  }

  const prev = $('source').value;
  fillSelect($('source'), profiles);
  if (prev && profiles.some((p) => p.id === prev)) $('source').value = prev;
  else {
    const best = [...profiles].sort((a, b) => (b.counts?.bookmarks || 0) - (a.counts?.bookmarks || 0))[0];
    $('source').value = best.id;
  }

  refreshTargets();
  syncPickers();
  await refreshPreview();
}

/* ============================================================
   三、视图
   ============================================================ */

function setStatus(text, kind = '') {
  const el = $('status');
  el.textContent = text;
  el.className = kind ? `status status--${kind}` : 'status';
}

function setHint(text) {
  $('hint').textContent = text;
}

function browserBadge(browser) {
  return browser === 'edge' ? 'E' : browser === 'chrome' ? 'C' : '?';
}

function optionLabel(p) {
  return `${p.browserName} · ${p.label}`;
}

function fillSelect(el, list) {
  el.textContent = '';
  for (const p of list) {
    const o = document.createElement('option');
    o.value = p.id;
    o.textContent = optionLabel(p);
    el.appendChild(o);
  }
}

function refreshTargets() {
  const list = targetsForSource();
  const keep = $('target').value;
  fillSelect($('target'), list);
  if (keep && list.some((p) => p.id === keep)) $('target').value = keep;
  if (!$('target').value && list.length) $('target').value = list[0].id;
  return list;
}

function syncPicker(side) {
  const p = side === 'source' ? sourceProfile() : targetProfile();
  const avatar = $(`${side}Avatar`);
  const stat = $(`${side}Stat`);
  const tag = $(`${side}Tag`);

  if (!p) {
    avatar.textContent = '?';
    stat.textContent = '—';
    tag.hidden = true;
    return;
  }

  avatar.textContent = browserBadge(p.browser);
  stat.textContent = p.hasFile
    ? `${p.counts?.bookmarks ?? 0} 条书签 · ${p.counts?.folders ?? 0} 个文件夹`
    : '还没有收藏夹文件，可以直接作为目标新建';

  if (p.running) {
    tag.hidden = false;
    tag.textContent = '运行中';
    tag.className = 'tag tag--live';
  } else if (!p.hasFile) {
    tag.hidden = false;
    tag.textContent = '空';
    tag.className = 'tag tag--empty';
  } else {
    tag.hidden = true;
  }
}

function syncPickers() {
  syncPicker('source');
  syncPicker('target');
}

function metric(num, label, lead = false) {
  const el = document.createElement('div');
  el.className = lead ? 'metric metric--lead' : 'metric';
  const n = document.createElement('span');
  n.className = 'metric__num';
  n.textContent = String(num);
  const l = document.createElement('span');
  l.className = 'metric__label';
  l.textContent = label;
  el.append(n, l);
  return el;
}

function setDiffNotice(text, isWarn = false) {
  const box = $('diff');
  box.textContent = '';
  const p = document.createElement('p');
  p.className = isWarn ? 'warn' : 'muted';
  p.textContent = text;
  box.appendChild(p);
  $('apply').disabled = true;
  setHint('');
}

function renderDiff(d, src, tgt) {
  const box = $('diff');
  box.textContent = '';

  const head = document.createElement('div');
  head.className = 'diff__head';
  const title = document.createElement('span');
  title.className = 'diff__title';
  title.textContent = '差异预览';
  const flow = document.createElement('span');
  flow.className = 'diff__flow';
  flow.textContent = `${src.browserName} → ${tgt.browserName}`;
  head.append(title, flow);
  box.appendChild(head);

  const s = d.stats;
  const metrics = document.createElement('div');
  metrics.className = 'metrics';
  metrics.append(
    metric(s.createBookmarks, '新增书签', true),
    metric(s.createFolders, '新增文件夹'),
    metric(s.skipped, '跳过重复'),
    metric(s.reused, '复用文件夹'),
  );
  box.appendChild(metrics);

  const rows = document.createElement('ul');
  rows.className = 'rows';
  d.summary.forEach((x) => {
    const li = document.createElement('li');
    const name = document.createElement('span');
    name.textContent = x.title;
    const val = document.createElement('span');
    val.textContent = `新增 ${x.createBookmarks} · 文件夹 ${x.createFolders} · 跳过 ${x.skipped}`;
    li.append(name, val);
    rows.appendChild(li);
  });
  box.appendChild(rows);

  if (d.targetRunning) {
    const w = document.createElement('p');
    w.className = 'warn';
    w.textContent = `${tgt.browserName} 正在运行。请先完全退出它（含托盘图标），否则改动会被覆盖。`;
    box.appendChild(w);
  }

  const nothing = s.createBookmarks === 0 && s.createFolders === 0;
  $('apply').disabled = nothing || d.targetRunning;

  if (d.targetRunning) setHint('目标浏览器未退出，暂不能写入');
  else if (nothing) setHint('目标已和源一致，无需复制');
  else setHint(`将写入 ${s.createBookmarks + s.createFolders} 个新条目`);

  if (nothing) setStatus('目标已经和源一致，没有需要复制的内容');
  else setStatus('');
}

async function refreshPreview() {
  const src = sourceProfile();
  const tgt = targetProfile();

  if (!src) return;
  if (targetsForSource().length === 0) {
    setDiffNotice('本机只找到一个浏览器，没有可复制到的目标。', true);
    return;
  }
  if (!tgt) {
    setDiffNotice('请选择一个目标配置。');
    return;
  }

  try {
    const d = await api('/api/preview', { sourceId: src.id, targetId: tgt.id });
    renderDiff(d, src, tgt);
  } catch (e) {
    setDiffNotice(e.message, true);
  }
}

/* ---------- 事件 ---------- */

$('source').addEventListener('change', async () => {
  refreshTargets();
  syncPickers();
  await refreshPreview();
});

$('target').addEventListener('change', async () => {
  syncPickers();
  await refreshPreview();
});

$('apply').addEventListener('click', async () => {
  const src = sourceProfile();
  const tgt = targetProfile();
  if (!src || !tgt) return;

  const btn = $('apply');
  btn.disabled = true;
  btn.textContent = '正在写入…';
  setStatus('正在写入目标收藏夹…');
  setHint('');

  try {
    const r = await api('/api/apply', { sourceId: src.id, targetId: tgt.id });

    // 必须先刷新再写提示：load() 内部会重算差异，而差异渲染会清空状态栏
    await load();

    if (r.nothingToDo) {
      setStatus('目标已经是最新，没有需要复制的内容', 'ok');
    } else {
      const name = r.backupFile ? r.backupFile.split(/[\\/]/).pop() : '';
      setStatus(
        `完成：新增 ${r.stats.createBookmarks} 条书签、${r.stats.createFolders} 个文件夹` +
          (name ? `　原文件已备份为 ${name}` : ''),
        'ok',
      );
    }
  } catch (e) {
    setStatus(e.message, 'err');
    btn.disabled = false;
  } finally {
    btn.textContent = '开始复制';
  }
});

/* ============================================================
   四、导出 / 导入（.portmarks）
   ============================================================ */

let importText = '';

function resetImportPreview(hint = '选好文件会先算一次差异，确认后再写入。') {
  importText = '';
  $('importFileName').textContent = '还没有选择文件';
  const box = $('importPreview');
  box.textContent = '';
  const p = document.createElement('p');
  p.className = 'muted';
  p.textContent = hint;
  box.appendChild(p);
  $('importApply').disabled = true;
}

function openImport() {
  const tgt = targetProfile();
  if (!tgt) {
    setStatus('先在上方选一个目标配置，再导入', 'err');
    return;
  }
  $('importTarget').textContent =
    `${tgt.browserName} · ${tgt.label}` + (tgt.running ? '（正在运行，写入前需要先退出）' : '');
  resetImportPreview();
  openSheet('importSheet', $('importBtn'));
}

function renderImportPreview(d) {
  const box = $('importPreview');
  box.textContent = '';

  const s = d.stats;
  const metrics = document.createElement('div');
  metrics.className = 'metrics';
  metrics.append(
    metric(s.createBookmarks, '新增书签', true),
    metric(s.createFolders, '新增文件夹'),
    metric(s.skipped, '跳过重复'),
    metric(s.reused, '复用文件夹'),
  );
  box.appendChild(metrics);

  const src = d.meta?.source || {};
  const when = src.exportedAt ? `，导出于 ${new Date(src.exportedAt).toLocaleString()}` : '';
  const info = document.createElement('p');
  info.className = 'field__hint';
  info.textContent = `文件来自 ${src.browser || '未知'}${src.profile ? ` · ${src.profile}` : ''}${when}；目标现有 ${d.target.bookmarks} 条书签。`;
  box.appendChild(info);

  if (d.targetRunning) {
    const w = document.createElement('p');
    w.className = 'warn';
    w.textContent = '目标浏览器正在运行，写入前请先完全退出它（含托盘图标）。';
    box.appendChild(w);
  }

  const nothing = s.createBookmarks === 0 && s.createFolders === 0;
  if (nothing) {
    const p = document.createElement('p');
    p.className = 'muted';
    p.textContent = '目标已经包含这些书签，无需再导入。';
    box.appendChild(p);
  }
  $('importApply').disabled = nothing || d.targetRunning;
}

async function onImportFile(ev) {
  const file = ev.target.files?.[0];
  ev.target.value = '';
  if (!file) return;

  const tgt = targetProfile();
  if (!tgt) {
    setStatus('先在上方选一个目标配置', 'err');
    return;
  }

  try {
    const text = await file.text();
    const d = await api('/api/import/preview', { targetId: tgt.id, pack: text });
    importText = text;
    $('importFileName').textContent = `${file.name} · ${(file.size / 1024).toFixed(1)} KB`;
    renderImportPreview(d);
  } catch (e) {
    resetImportPreview();
    const box = $('importPreview');
    const p = document.createElement('p');
    p.className = 'warn';
    p.textContent = e.message;
    box.appendChild(p);
  }
}

$('exportBtn').addEventListener('click', async () => {
  const src = sourceProfile();
  if (!src) return;
  if (!src.counts?.bookmarks) {
    setStatus('这个源里没有可导出的书签', 'err');
    return;
  }

  const btn = $('exportBtn');
  btn.disabled = true;
  try {
    const { pack } = await api('/api/export', { sourceId: src.id });
    const json = JSON.stringify(pack, null, 2);
    const url = URL.createObjectURL(new Blob([json], { type: 'application/json' }));
    const a = document.createElement('a');
    const stamp = new Date().toISOString().slice(0, 16).replace(/[-:T]/g, '');
    a.href = url;
    a.download = `bookmarks-${src.browser}-${stamp}.portmarks`;
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 2000);
    setStatus(`已从 ${src.browserName} 导出 ${src.counts.bookmarks} 条书签（${(json.length / 1024).toFixed(1)} KB）`, 'ok');
  } catch (e) {
    setStatus(e.message, 'err');
  } finally {
    btn.disabled = false;
  }
});

$('importBtn').addEventListener('click', openImport);
$('importFile').addEventListener('change', onImportFile);

$('importApply').addEventListener('click', async () => {
  const tgt = targetProfile();
  if (!tgt || !importText) return;

  const btn = $('importApply');
  btn.disabled = true;
  btn.textContent = '正在写入…';
  try {
    const r = await api('/api/import/apply', { targetId: tgt.id, pack: importText });
    closeSheet('importSheet');

    // 同上：先刷新，再写提示，否则会被差异渲染清掉
    await load();

    if (r.nothingToDo) {
      setStatus('目标已经包含这些书签，无需导入', 'ok');
    } else {
      const name = r.backupFile ? r.backupFile.split(/[\\/]/).pop() : '';
      setStatus(
        `导入完成：新增 ${r.stats.createBookmarks} 条书签、${r.stats.createFolders} 个文件夹` +
          (name ? `　原文件已备份为 ${name}` : ''),
        'ok',
      );
    }
  } catch (e) {
    setStatus(e.message, 'err');
    btn.disabled = false;
  } finally {
    btn.textContent = '写入目标';
  }
});

bindSheets();
initLook();
load();
