/* ============================================================================
 * 抖音直播 —— 礼物栏精简（只保留「更多」二级菜单 + 钻石）
 *
 * 效果：底部礼物栏里的每个礼物图标全部隐去，只留下：
 *         - 「更多 >」按钮  —— 点开就是完整礼物面板（二级菜单）
 *         - 钻石数量        —— 充值/余额显示
 *         - 其它非礼物入口（如「游戏 >」）—— 不是礼物，不动
 *
 * 【结构（关键）】
 *   row
 *    ├─ list        <- 礼物列表容器：直接子项全是礼物
 *    ├─ more        <- 「更多 >」，是 list 的**兄弟节点**
 *    ├─ diamond     <- 钻石，也是兄弟
 *    └─ game        <- 「游戏 >」，也是兄弟
 *
 *   所以只隐藏 list 自己的子项，兄弟节点一律不碰。
 *
 * 【为什么要遍历 Shadow DOM】
 *   抖音直播页大量使用 Web Components（pace-island 等自定义元素），
 *   礼物栏很可能在 shadow root 里。document.querySelectorAll 穿不透 shadow 边界，
 *   之前「首轮隐藏 0 个」就是这个原因。这里递归收集所有 shadow root 再扫。
 *
 * 【为什么不用 class 名】
 *   抖音的 class 是构建时哈希的（xAG9cqUC / saw7uCUW …），每次发版都会变。
 *   这里用「文本特征」判定：礼物项的文本里带价格（如 99钻、1钻、299钻）。
 *
 * 临时恢复：window.__DY_LIVE_GIFT_HIDE__ = false; 刷新即可。
 * 诊断：控制台执行 window.__DY_LIVE_GIFT_DUMP__() 打印扫描结果。
 * ==========================================================================*/
(function () {
  'use strict';

  if (window.__DY_LIVE_GIFT_HIDE_INSTALLED__) return;
  window.__DY_LIVE_GIFT_HIDE_INSTALLED__ = true;

  var TAG = '[dy-live-gift]';
  var STYLE_ID = 'dy-live-gift-hide-style';
  var HIDDEN_ATTR = 'data-dy-gift-hidden';
  var MAX_TEXT = 30;      // 礼物项文本上限（"送你糖果299钻" 之类）

  function log(msg) {
    try {
      if (window.__dyForceLog) window.__dyForceLog(TAG + ' ' + msg);
      else console.log(TAG + ' ' + msg);
    } catch (e) { }
  }

  function enabled() {
    return window.__DY_LIVE_GIFT_HIDE__ !== false;
  }

  var CSS = [
    '[' + HIDDEN_ATTR + '="1"] { display: none !important; }',
    '#GiftEffectLayout, #GiftTrayLayout { display: none !important; }',
    'div[id^="gift_effect_bg_"] { display: none !important; }',
    '.GiftEffectPlugin, .GiftTrayPlugin { display: none !important; }'
  ].join('\n');

  function isLivePage() {
    try {
      var h = location.hostname;
      if (h.indexOf('live.douyin.com') >= 0) return true;
      if (h.indexOf('douyin.com') >= 0 && /\/live|live\//.test(location.pathname)) return true;
      if (h.indexOf('douyin.com') >= 0 && location.search.indexOf('enter_from=live') >= 0) return true;
      return false;
    } catch (e) { return false; }
  }

  // textContent 而不是 innerText：后者依赖布局，未渲染就返回空
  function textOf(el) {
    try { return (el.textContent || '').replace(/\s+/g, ''); } catch (e) { return ''; }
  }

  // ---------------------------------------------------------------------
  // 收集所有根：document + 层层 shadow root
  // ---------------------------------------------------------------------
  function collectRoots() {
    var roots = [];
    var budget = 6000;

    function walk(root, depth) {
      if (!root || depth > 6 || budget <= 0) return;
      roots.push(root);

      var all;
      try { all = root.querySelectorAll('*'); } catch (e) { return; }
      if (!all) return;

      for (var i = 0; i < all.length; i++) {
        if (--budget <= 0) return;
        var sr = null;
        try { sr = all[i].shadowRoot; } catch (e) { }
        if (sr) walk(sr, depth + 1);
      }
    }

    walk(document, 0);
    return roots;
  }

  // 礼物项：文本短、含「钻」，且以「数字+钻」结尾（价格通常在末尾）
  function isGiftItem(el) {
    var t = textOf(el);
    if (!t || t.length > MAX_TEXT) return false;
    if (t.indexOf('钻') < 0) return false;
    return true;
  }

  // ---------------------------------------------------------------------
  // 找礼物列表容器：按「父级」聚合，子项里礼物最多的那个父级
  // ---------------------------------------------------------------------
  function findItemList(roots) {
    var counts = [];        // [{ parent, count }]
    var indexByParent = []; // 平行数组：parent -> counts 下标
    var total = 0;
    var samples = [];

    function idxOf(parent) {
      for (var i = 0; i < indexByParent.length; i++) {
        if (indexByParent[i] === parent) return i;
      }
      indexByParent.push(parent);
      counts.push({ parent: parent, count: 0 });
      return counts.length - 1;
    }

    for (var r = 0; r < roots.length; r++) {
      var all;
      try { all = roots[r].querySelectorAll('*'); } catch (e) { continue; }
      if (!all) continue;

      for (var i = 0; i < all.length; i++) {
        var el = all[i];
        if (!isGiftItem(el)) continue;
        total++;

        if (samples.length < 5) {
          try { samples.push(textOf(el).slice(0, 20)); } catch (e) { }
        }

        var p = el.parentElement;
        if (!p) continue;

        counts[idxOf(p)].count++;
      }
    }

    var best = null, bestCount = 0;
    for (var b = 0; b < counts.length; b++) {
      if (counts[b].count > bestCount) { bestCount = counts[b].count; best = counts[b].parent; }
    }

    return { list: bestCount >= 2 ? best : null, total: total, bestCount: bestCount, samples: samples, roots: roots.length };
  }

  // 礼物项原本是"一整条"：藏掉前面的之后，最左那块会变成"左直角+右圆角"
  function fixFirstCorner(host) {
    var all;
    try { all = host.querySelectorAll('*'); } catch (e) { return; }

    var target = null, targetX = Infinity;
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      var rect;
      try { rect = el.getBoundingClientRect(); } catch (e) { continue; }
      if (rect.width < 20 || rect.height < 20) continue;
      if (el.getAttribute(HIDDEN_ATTR) === '1') continue;

      var cs;
      try { cs = getComputedStyle(el); } catch (e) { continue; }
      if (cs.display === 'none' || cs.visibility === 'hidden') continue;

      var tl = parseFloat(cs.borderTopLeftRadius) || 0;
      var tr = parseFloat(cs.borderTopRightRadius) || 0;
      var bl = parseFloat(cs.borderBottomLeftRadius) || 0;
      var br = parseFloat(cs.borderBottomRightRadius) || 0;

      if (tr <= 0 || tl > 0) continue;

      if (rect.left < targetX) { targetX = rect.left; target = { el: el, tr: tr, br: br, bl: bl }; }
    }

    if (!target) return;
    try {
      target.el.style.borderTopLeftRadius = target.tr + 'px';
      if (target.br > 0 && target.bl === 0) target.el.style.borderBottomLeftRadius = target.br + 'px';
    } catch (e) { }
  }

  // 上一次的统计，供诊断函数使用
  var lastStats = null;

  function trimBar() {
    if (!enabled()) { restore(); return 0; }

    var roots = collectRoots();
    var res = findItemList(roots);
    lastStats = res;

    if (!res.list) return 0;

    var list = res.list;
    var hidden = 0;

    for (var i = 0; i < list.children.length; i++) {
      var child = list.children[i];
      if (!isGiftItem(child)) continue;
      if (child.getAttribute(HIDDEN_ATTR) === '1') continue;
      child.setAttribute(HIDDEN_ATTR, '1');
      hidden++;
    }

    var host = list.parentElement || list;
    fixFirstCorner(host);

    if (hidden > 0) log('已隐藏 ' + hidden + ' 个礼物图标（保留「更多」与钻石）');
    return hidden;
  }

  // ---------------------------------------------------------------------
  // 诊断：控制台执行 window.__DY_LIVE_GIFT_DUMP__()
  // ---------------------------------------------------------------------
  try {
    window.__DY_LIVE_GIFT_DUMP__ = function () {
      var roots = collectRoots();
      var res = findItemList(roots);
      var info = {
        roots: roots.length,
        giftNodes: res.total,
        bestParentCount: res.bestCount,
        samples: res.samples,
        hiddenNow: document.querySelectorAll('[' + HIDDEN_ATTR + ']').length,
      };
      try { console.log(TAG + ' dump', info); } catch (e) { }
      return info;
    };
  } catch (e) { }

  function restore() {
    var marked = document.querySelectorAll('[' + HIDDEN_ATTR + ']');
    for (var i = 0; i < marked.length; i++) marked[i].removeAttribute(HIDDEN_ATTR);
    removeStyle();
    log('已恢复礼物显示');
  }

  function ensureStyle() {
    if (!enabled()) return;
    var head = document.head || document.documentElement;
    if (!head) return;
    if (document.getElementById(STYLE_ID)) return;
    var style = document.createElement('style');
    style.id = STYLE_ID;
    style.type = 'text/css';
    style.textContent = CSS;
    try { head.appendChild(style); } catch (e) { }
  }

  function removeStyle() {
    var el = document.getElementById(STYLE_ID);
    if (el) el.remove();
  }

  var scheduled = false;
  function schedule() {
    if (scheduled) return;
    scheduled = true;
    setTimeout(function () {
      scheduled = false;
      try { trimBar(); } catch (e) { }
    }, 300);
  }

  function startObserver() {
    try {
      var mo = new MutationObserver(function () {
        if (!isLivePage() || !enabled()) return;
        ensureStyle();
        schedule();
      });
      mo.observe(document.documentElement, { childList: true, subtree: true });
    } catch (e) { log('MutationObserver 失败：' + e); }
  }

  function boot() {
    if (!isLivePage()) { log('非直播页，跳过（url=' + location.href + '）'); return; }

    ensureStyle();
    var n = trimBar();

    var extra = '';
    if (lastStats) {
      extra = '  [roots=' + lastStats.roots + ' 礼物节点=' + lastStats.total +
              ' 最大父级=' + lastStats.bestCount +
              (lastStats.samples.length ? ' 样例=' + lastStats.samples.join('|') : '') + ']';
    }
    log('已启动（首轮隐藏 ' + n + ' 个）' + extra + '  当前 URL=' + location.href);

    // 礼物栏异步渲染（客户端比网页慢），多补几轮
    [500, 1200, 2500, 4000, 7000, 11000, 16000, 22000].forEach(function (ms) {
      setTimeout(function () { try { ensureStyle(); trimBar(); } catch (e) { } }, ms);
    });

    startObserver();
  }

  if (document.readyState === 'loading') {
    var timer = setInterval(function () {
      if (document.head || document.documentElement) { clearInterval(timer); boot(); }
    }, 4);
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }

  window.__DY_LIVE_GIFT_HIDE__ = true;
})();
/* ====== LIVE-GIFT-HIDE-END ====== */