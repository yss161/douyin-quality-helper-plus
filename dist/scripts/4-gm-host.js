/* ============================================================================
 * 抖音画质助手 · GM 兼容层
 * 版本 2.0.4 ｜ https://github.com/yss161/douyin-quality-helper-plus
 *
 * 让油猴脚本能在客户端里跑（进阶）
 *
 * 用法：抖音客户端里打开对应页面（直播页 / 视频页），在 DevTools 控制台
 *       粘贴本文件全部内容后回车即可。刷新页面后需要重新执行。
 * ==========================================================================*/
/* ============================================================================
 * 抖音优化 · 客户端宿主（GM 兼容层 + 左侧栏设置入口）
 *
 * 这个文件运行在抖音页面的 JS 上下文里（preload isolated=false）。
 * 它做两件事：
 *   1) 提供油猴的 GM_* API（存储 / 菜单 / 网络 / 下载），让原版用户脚本不用改一行就能跑
 *   2) 把 GM_registerMenuCommand 注册的菜单项渲染成左侧栏里的按钮
 *      —— 也就是原来点油猴图标才有的"设置入口"
 * ==========================================================================*/
(function () {
  'use strict';

  if (window.__DY_HOST_INSTALLED__) return;
  window.__DY_HOST_INSTALLED__ = true;

  var STORE_PREFIX = 'dy-userscript::';
  var listeners = {};
  var listenerSeq = 0;
  var menuCommands = [];
  var menuSeq = 0;

  // -------------------------------------------------------------------------
  // 延迟执行器
  //
  // Electron preload 在 <html> 之前就跑，这时 document.documentElement 是 null。
  // domutils 的 addStyle 在 document.head 不存在时会读 documentElement.childNodes，
  // 于是脚本刚跑就抛 "Cannot read properties of null (reading 'childNodes')"。
  // 用户脚本本身按油猴的 document-start 写的，那时 <html> 已经在了。
  //
  // 所以脚本主体统一通过这里延迟到 <html> 出现再跑。
  // -------------------------------------------------------------------------
  var deferred = [];
  var deferTimer = null;

  function domUsable() {
    try { return !!(document && document.documentElement); } catch (e) { return false; }
  }

  function flushDeferred() {
    if (!domUsable()) return;
    if (deferTimer) { clearInterval(deferTimer); deferTimer = null; }
    var queue = deferred;
    deferred = [];
    for (var i = 0; i < queue.length; i++) {
      try { queue[i](); } catch (e) { log('deferred script failed: ' + (e && e.stack ? e.stack : e)); }
    }
  }

  function deferScript(fn) {
    if (typeof fn !== 'function') return;
    if (domUsable()) {
      // 已经很晚（例如页面内重复注入）：直接跑
      setTimeout(function () {
        try { fn(); } catch (e) { log('script failed: ' + (e && e.stack ? e.stack : e)); }
      }, 0);
      return;
    }
    deferred.push(fn);
    if (deferTimer) return;
    deferTimer = setInterval(flushDeferred, 4);
    // 兜底：DOMContentLoaded 时再冲一次
    try {
      document.addEventListener('DOMContentLoaded', flushDeferred, { once: true });
    } catch (e) { }
  }

  try { window.__DY_DEFER_SCRIPT__ = deferScript; } catch (e) { }

  function log(msg) {
    try {
      if (window.__dyForceLog) window.__dyForceLog('[dy-host] ' + msg);
      else console.log('[dy-host] ' + msg);
    } catch (e) { }
  }

  // -------------------------------------------------------------------------
  // GM 存储：localStorage（跨重启保留）
  // -------------------------------------------------------------------------
  function rawGet(key) {
    try {
      var v = window.localStorage.getItem(STORE_PREFIX + key);
      return v == null ? null : JSON.parse(v);
    } catch (e) { return null; }
  }

  function rawSet(key, value) {
    try {
      if (value === undefined) window.localStorage.removeItem(STORE_PREFIX + key);
      else window.localStorage.setItem(STORE_PREFIX + key, JSON.stringify(value));
    } catch (e) { log('storage write failed: ' + e); }
  }

  function GM_getValue(key, defaultValue) {
    var v = rawGet(key);
    return v === null ? defaultValue : v;
  }

  function GM_setValue(key, value) {
    var old = GM_getValue(key);
    rawSet(key, value);
    fireChange(key, old, value);
  }

  function GM_deleteValue(key) {
    var old = GM_getValue(key);
    try { window.localStorage.removeItem(STORE_PREFIX + key); } catch (e) { }
    fireChange(key, old, undefined);
  }

  function GM_listValues() {
    var out = [];
    try {
      for (var i = 0; i < window.localStorage.length; i++) {
        var k = window.localStorage.key(i);
        if (k && k.indexOf(STORE_PREFIX) === 0) out.push(k.slice(STORE_PREFIX.length));
      }
    } catch (e) { }
    return out;
  }

  function GM_setValues(obj) {
    if (!obj || typeof obj !== 'object') return;
    Object.keys(obj).forEach(function (k) { GM_setValue(k, obj[k]); });
  }

  function fireChange(key, oldValue, newValue) {
    var list = listeners[key];
    if (!list) return;
    for (var i = list.length - 1; i >= 0; i--) {
      try { list[i].cb(key, oldValue, newValue, false); } catch (e) { log('listener error ' + e); }
    }
  }

  function GM_addValueChangeListener(key, cb) {
    var id = ++listenerSeq;
    (listeners[key] = listeners[key] || []).push({ id: id, cb: cb });
    return id;
  }

  function GM_removeValueChangeListener(id) {
    for (var key in listeners) {
      var list = listeners[key];
      for (var i = list.length - 1; i >= 0; i--) {
        if (list[i].id === id) { list.splice(i, 1); return true; }
      }
    }
    return false;
  }

  // -------------------------------------------------------------------------
  // GM 菜单：注册表由左侧栏 UI 消费
  // -------------------------------------------------------------------------
  function GM_registerMenuCommand(text, callback) {
    var id = 'menu-' + (++menuSeq);
    menuCommands.push({ id: id, text: String(text == null ? '' : text), callback: callback });
    scheduleSidebarSync();
    return id;
  }

  function GM_unregisterMenuCommand(id) {
    for (var i = menuCommands.length - 1; i >= 0; i--) {
      if (menuCommands[i].id === id) { menuCommands.splice(i, 1); break; }
    }
    scheduleSidebarSync();
  }

  // -------------------------------------------------------------------------
  // GM 网络 / 下载
  // -------------------------------------------------------------------------
  function GM_xmlhttpRequest(details) {
    var d = details || {};
    var xhr = new XMLHttpRequest();
    var aborted = false;
    try {
      xhr.open(d.method || 'GET', d.url, true);
      if (d.responseType) {
        try { xhr.responseType = d.responseType === 'json' ? 'json' : d.responseType; } catch (e) { }
      }
      if (d.timeout) xhr.timeout = d.timeout;
      if (d.headers) {
        for (var k in d.headers) {
          try { xhr.setRequestHeader(k, d.headers[k]); } catch (e) { }
        }
      }
      xhr.onload = function () {
        if (aborted) return;
        var resp = { responseText: xhr.responseText, response: xhr.response, readyState: xhr.readyState, status: xhr.status, statusText: xhr.statusText, finalUrl: xhr.responseURL };
        if (d.onload) d.onload(resp);
      };
      xhr.onerror = function () { if (!aborted && d.onerror) d.onerror({ error: 'error' }); };
      xhr.ontimeout = function () { if (!aborted && d.ontimeout) d.ontimeout({ error: 'timeout' }); };
      xhr.onabort = function () { if (d.onabort) d.onabort({ error: 'abort' }); };
      xhr.onprogress = function (e) { if (d.onprogress) d.onprogress({ loaded: e.loaded, total: e.total }); };
      xhr.send(d.data == null ? null : d.data);
    } catch (e) {
      setTimeout(function () { if (d.onerror) d.onerror({ error: String(e) }); }, 0);
    }
    return {
      abort: function () {
        aborted = true;
        try { xhr.abort(); } catch (e) { }
      },
    };
  }

  function GM_download(details, onload, onerror) {
    var d = typeof details === 'string' ? { url: details } : (details || {});
    try {
      var a = document.createElement('a');
      a.href = d.url;
      a.download = d.name || '';
      a.rel = 'noreferrer';
      document.body.appendChild(a);
      a.click();
      a.remove();
      if (typeof d.onload === 'function') setTimeout(function () { d.onload({}); }, 0);
      else if (typeof onload === 'function') setTimeout(function () { onload({}); }, 0);
    } catch (e) {
      if (typeof d.onerror === 'function') d.onerror({ error: String(e) });
      else if (typeof onerror === 'function') onerror({ error: String(e) });
    }
  }

  // -------------------------------------------------------------------------
  // 把 GM_* 挂到页面
  // -------------------------------------------------------------------------
  var GM = {
    GM_getValue: GM_getValue,
    GM_setValue: GM_setValue,
    GM_deleteValue: GM_deleteValue,
    GM_listValues: GM_listValues,
    GM_setValues: GM_setValues,
    GM_addValueChangeListener: GM_addValueChangeListener,
    GM_removeValueChangeListener: GM_removeValueChangeListener,
    GM_registerMenuCommand: GM_registerMenuCommand,
    GM_unregisterMenuCommand: GM_unregisterMenuCommand,
    GM_xmlhttpRequest: GM_xmlhttpRequest,
    GM_download: GM_download,
    GM_getResourceText: function () { return null; },
    GM_info: {
      scriptHandler: 'DouyinQualityHelper',
      version: '1.0.0',
      script: {
        name: '抖音优化',
        version: '2026.10.23',
        namespace: 'https://github.com/WhiteSevs/TamperMonkeyScript',
        supportURL: 'https://github.com/WhiteSevs/TamperMonkeyScript/issues',
        description: '视频过滤、自定义清晰度、屏蔽弹窗等',
      },
    },
    unsafeWindow: window,
  };

  Object.keys(GM).forEach(function (k) {
    try { window[k] = GM[k]; } catch (e) { }
  });

  // =========================================================================
  // 左侧栏设置入口
  // =========================================================================
  var SIDEBAR_FLAG = 'data-dy-userscript-entry';
  var sidebarScheduled = false;

  function scheduleSidebarSync() {
    if (sidebarScheduled) return;
    sidebarScheduled = true;
    setTimeout(function () {
      sidebarScheduled = false;
      try { syncSidebar(); } catch (e) { log('sidebar sync failed: ' + e); }
    }, 120);
  }

  function deepestCommonAncestor(nodes) {
    if (!nodes || nodes.length === 0) return null;
    var node = nodes[0];
    while (node && node !== document.documentElement) {
      var all = true;
      for (var i = 0; i < nodes.length; i++) {
        if (!node.contains(nodes[i])) { all = false; break; }
      }
      if (all) return node;
      node = node.parentElement;
    }
    return null;
  }

  function findNavContainer() {
    var links = document.querySelectorAll('a[href*="from_nav"]');
    if (links.length < 4) return null;
    var arr = [];
    for (var i = 0; i < links.length; i++) arr.push(links[i]);
    var common = deepestCommonAncestor(arr);
    if (!common) return null;
    var r = common.getBoundingClientRect();
    if (r.width > 320 || r.height < 200) return null;
    return common;
  }

  // 页面有浅色/深色两套主题：用 currentColor + 半透明黑/白，两边都能看清
  function isDarkTheme() {
    try {
      var nav = findNavContainer();
      var probe = nav || document.body;
      var bg = getComputedStyle(probe).backgroundColor;
      var m = bg && bg.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
      if (m) {
        var lum = (parseInt(m[1], 10) * 299 + parseInt(m[2], 10) * 587 + parseInt(m[3], 10) * 114) / 1000;
        return lum < 128;
      }
      var bodyBg = getComputedStyle(document.body).backgroundColor;
      var m2 = bodyBg && bodyBg.match(/rgba?\((\d+),\s*(\d+),\s*(\d+)/);
      if (m2) {
        var lum2 = (parseInt(m2[1], 10) * 299 + parseInt(m2[2], 10) * 587 + parseInt(m2[3], 10) * 114) / 1000;
        return lum2 < 128;
      }
    } catch (e) { }
    return true;
  }

  function buildEntry() {
    var dark = isDarkTheme();
    var textColor = dark ? 'rgba(255,255,255,.86)' : 'rgba(0,0,0,.75)';
    var mutedColor = dark ? 'rgba(255,255,255,.35)' : 'rgba(0,0,0,.35)';
    var hoverBg = dark ? 'rgba(255,255,255,.08)' : 'rgba(0,0,0,.05)';
    var borderColor = dark ? 'rgba(255,255,255,.08)' : 'rgba(0,0,0,.06)';

    var box = document.createElement('div');
    box.setAttribute(SIDEBAR_FLAG, '1');
    box.style.cssText = 'display:flex;flex-direction:column;gap:2px;margin-top:10px;padding-top:10px;border-top:1px solid ' + borderColor + ';width:100%;box-sizing:border-box;';

    var title = document.createElement('div');
    title.textContent = '抖音优化';
    title.style.cssText = 'font-size:11px;line-height:16px;letter-spacing:.5px;color:' + mutedColor + ';padding:0 12px;user-select:none;';
    box.appendChild(title);

    var shown = menuCommands.filter(function (m) { return m.text && m.text.trim() !== ''; });
    if (shown.length === 0) shown = [{ id: 'none', text: '⚙ 设置', callback: null }];

    shown.forEach(function (m) {
      var btn = document.createElement('div');
      btn.textContent = m.text;
      btn.title = m.text;
      btn.style.cssText = 'display:flex;align-items:center;height:34px;padding:0 12px;border-radius:8px;font-size:13px;line-height:20px;color:' + textColor + ';cursor:pointer;user-select:none;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;transition:background-color .15s;';
      btn.addEventListener('mouseenter', function () { btn.style.backgroundColor = hoverBg; });
      btn.addEventListener('mouseleave', function () { btn.style.backgroundColor = 'transparent'; });
      btn.addEventListener('click', function (ev) {
        ev.preventDefault();
        ev.stopPropagation();
        if (typeof m.callback !== 'function') return;
        try {
          m.callback(ev);
        } catch (e) {
          log('menu callback failed: ' + e);
        }
      });
      box.appendChild(btn);
    });
    return box;
  }

  function syncSidebar() {
    if (menuCommands.length === 0) return;
    var nav = findNavContainer();
    if (!nav) return;
    var want = String(menuCommands.length);

    // 同一个容器里可能残留多个副本（SPA 重渲染 / 宿主被重复装载），先清干净
    var existing = document.querySelectorAll('[' + SIDEBAR_FLAG + ']');
    for (var i = 0; i < existing.length; i++) {
      var el0 = existing[i];
      if (el0 !== nav.querySelector('[' + SIDEBAR_FLAG + ']') || el0.parentElement !== nav) el0.remove();
    }

    var current = nav.querySelector('[' + SIDEBAR_FLAG + ']');
    if (current && current.getAttribute('data-count') === want) return;
    if (current) current.remove();

    var el = buildEntry();
    el.setAttribute('data-count', want);
    nav.appendChild(el);
  }

  function startObserver() {
    var mo = new MutationObserver(function () {
      if (menuCommands.length === 0) return;
      var nav = findNavContainer();
      if (!nav) return;
      if (!nav.querySelector('[' + SIDEBAR_FLAG + ']')) scheduleSidebarSync();
    });
    try {
      mo.observe(document.documentElement, { childList: true, subtree: true });
    } catch (e) { }
  }

  function onReady(fn) {
    if (document.readyState === 'complete' || document.readyState === 'interactive') setTimeout(fn, 0);
    else document.addEventListener('DOMContentLoaded', fn, { once: true });
  }

  onReady(function () {
    startObserver();
    scheduleSidebarSync();
  });

  window.__DY_HOST__ = {
    GM: GM,
    menuCommands: menuCommands,
    syncSidebar: function () { scheduleSidebarSync(); },
    log: log,
    onReady: onReady,
  };
})();
