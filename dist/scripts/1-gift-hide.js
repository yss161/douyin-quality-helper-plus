/* ============================================================================
 * 抖音画质助手 · 直播间：精简礼物栏
 * 版本 2.0.1 ｜ https://github.com/yss161/douyin-quality-helper-plus
 *
 * 隐藏礼物图标，只留「更多」与钻石
 *
 * 用法：抖音客户端里打开对应页面（直播页 / 视频页），在 DevTools 控制台
 *       粘贴本文件全部内容后回车即可。刷新页面后需要重新执行。
 * ==========================================================================*/
/* ============================================================================
 * 抖音直播 · 礼物栏精简（独立注入版）
 *
 * 与 injector/live-gift-hide.js 同一份逻辑，方便单独注入：
 * 底部礼物栏里的每个礼物图标全部隐去，只留「更多」二级菜单入口与钻石。
 *
 * 保留：礼物面板（点「更多」能正常打开）、底部控制条、弹幕、聊天区。
 * 隐藏：单个礼物图标、送礼全屏特效、飘屏。
 *
 * 判定用文本特征而非 class 名（抖音 class 是构建时哈希的，会随版本变）：
 *   带价格的礼物项 -> 文本含「钻」；「更多」按钮 -> 文本是「更多」
 *
 * 还会把「更多」左侧的直角补成和右侧一样的圆角（它原本是整排的最后一个，
 * 左圆角在第一个礼物上，那项被隐藏后就露出直角）。
 *
 * 临时恢复：控制台执行 window.__DY_LIVE_GIFT_HIDE__ = false; 后刷新。
 * ==========================================================================*/

(function () {
  'use strict';

  if (window.__DY_LIVE_GIFT_HIDE_INSTALLED__) return;
  window.__DY_LIVE_GIFT_HIDE_INSTALLED__ = true;

  var TAG = '[dy-live-gift]';
  var STYLE_ID = 'dy-live-gift-hide-style';
  var HIDDEN_ATTR = 'data-dy-gift-hidden';

  function log(msg) {
    try {
      if (window.__dyForceLog) window.__dyForceLog(TAG + ' ' + msg);
      else console.log(TAG + ' ' + msg);
    } catch (e) { }
  }

  // CSS 只负责"整层"的东西；单个礼物项由 JS 按文本判定后打标记
  var CSS = [
    '[' + HIDDEN_ATTR + '="1"] { display: none !important; }',

    /* 送礼全屏特效 / 飘屏。注意保留 #GiftMenuLayout，否则「更多」点不开 */
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

  // 用 textContent 而不是 innerText：
  //   - innerText 依赖布局，未渲染 / 隐藏的节点会返回空串，判不出来
  //   - innerText 会强制重排，在 MutationObserver 里反复调用很伤性能
  function textOf(el) {
    try { return (el.textContent || '').replace(/\s+/g, ''); } catch (e) { return ''; }
  }

  // 在礼物栏里找"礼物项列表"：子女里带「钻」最多的那个容器。
  // 礼物项可能直接挂在 .gitBarOptimizeEnabled 下，也可能在更深一层的列表里，
  // 所以候选集合要把 bar 自己也算进去。
  function findItemList(bar) {
    var best = null, bestScore = 0;
    var nodes;
    try { nodes = bar.querySelectorAll('*'); } catch (e) { return null; }

    function consider(n) {
      if (!n || n.children.length < 2) return;
      var score = 0;
      for (var j = 0; j < n.children.length; j++) {
        if (textOf(n.children[j]).indexOf('钻') >= 0) score++;
      }
      if (score > bestScore) { bestScore = score; best = n; }
    }

    consider(bar);                                  // 先看 bar 自己
    for (var i = 0; i < nodes.length; i++) consider(nodes[i]);

    return bestScore >= 2 ? best : null;   // 至少两个带价格的礼物，才当成列表
  }

  // 兜底：拿不到价格文本时（含「钻」字样可能变化），
  // 就按"除「更多」和钻石之外全部隐藏"处理 —— 但不能少于 3 个子项，避免误伤
  function findItemListByStructure(bar) {
    var nodes = [];
    try { nodes = bar.querySelectorAll('*'); } catch (e) { return null; }
    var best = null, bestCount = 0;
    function consider(n) {
      if (!n || n.children.length < 3) return;
      var hasMore = false, hasDiamond = false;
      for (var j = 0; j < n.children.length; j++) {
        if (looksLikeMore(n.children[j])) hasMore = true;
        if (isDiamond(n.children[j])) hasDiamond = true;
      }
      if (hasMore && n.children.length > bestCount) { bestCount = n.children.length; best = n; }
    }
    consider(bar);
    for (var i = 0; i < nodes.length; i++) consider(nodes[i]);
    return best;
  }

  function looksLikeMore(el) { return textOf(el).indexOf('更多') >= 0; }

  // 礼物项原本是"一整条"：第一个带左圆角，最后一个带右圆角，中间都是直角。
  // 把前面的礼物藏掉之后，最先露出来的那块就会显示成"左直角 + 右圆角"，
  // 看起来像被切掉一角。这里把它左侧补成和右侧一样的圆角。
  function fixFirstCorner(bar) {
    // 「更多」不在礼物列表里，而是列表的兄弟节点（列表 + 更多 同属一条 row），
    // 所以不能只在 list.children 里找，要在整条礼物栏里找"最左边那块可见的圆角块"。
    // 判据：右边有圆角、左边是直角 —— 正是"本来是整条中最后一个"的特征。
    var all;
    try { all = bar.querySelectorAll('*'); } catch (e) { return; }

    var target = null, targetX = Infinity;
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      var r;
      try { r = el.getBoundingClientRect(); } catch (e) { continue; }
      if (r.width < 20 || r.height < 20) continue;      // 太小的（文字、图标）忽略
      if (el.getAttribute(HIDDEN_ATTR) === '1') continue;

      var cs;
      try { cs = getComputedStyle(el); } catch (e) { continue; }
      if (cs.display === 'none' || cs.visibility === 'hidden') continue;

      var tl = parseFloat(cs.borderTopLeftRadius) || 0;
      var tr = parseFloat(cs.borderTopRightRadius) || 0;
      var bl = parseFloat(cs.borderBottomLeftRadius) || 0;
      var br = parseFloat(cs.borderBottomRightRadius) || 0;

      // 只处理"右侧圆角、左侧直角"的块
      if (tr <= 0 || tl > 0) continue;

      if (r.left < targetX) { targetX = r.left; target = { el: el, tr: tr, br: br, bl: bl }; }
    }

    if (!target) return;
    try {
      target.el.style.borderTopLeftRadius = target.tr + 'px';
      if (target.br > 0 && target.bl === 0) target.el.style.borderBottomLeftRadius = target.br + 'px';
    } catch (e) { }
  }

  function isDiamond(el) {
    try {
      if (el.classList && el.classList.contains('giftRechargeContaner')) return true;
      if (el.querySelector && el.querySelector('.giftRechargeContaner')) return true;
    } catch (e) { }
    return false;
  }

  function trimBar() {
    if (window.__DY_LIVE_GIFT_HIDE__ === false) { restore(); return 0; }

    var bar = document.querySelector('.gitBarOptimizeEnabled');
    if (!bar) return 0;

    var list = findItemList(bar) || findItemListByStructure(bar);
    if (!list) return 0;   // 还没加载出礼物项（未登录 / 空房间）就不动

    var hidden = 0;
    for (var i = 0; i < list.children.length; i++) {
      var child = list.children[i];
      if (looksLikeMore(child) || isDiamond(child)) continue;   // 这两个保留
      if (child.getAttribute(HIDDEN_ATTR) === '1') continue;
      child.setAttribute(HIDDEN_ATTR, '1');
      hidden++;
    }
    fixFirstCorner(bar);
    if (hidden > 0) log('已隐藏 ' + hidden + ' 个礼物图标，保留「更多」与钻石');
    return hidden;
  }

  function restore() {
    var marked = document.querySelectorAll('[' + HIDDEN_ATTR + ']');
    for (var i = 0; i < marked.length; i++) marked[i].removeAttribute(HIDDEN_ATTR);
    removeStyle();
    log('已恢复礼物显示');
  }

  function ensureStyle() {
    if (window.__DY_LIVE_GIFT_HIDE__ === false) return;
    var head = document.head || document.documentElement;
    if (!head) return;
    if (document.getElementById(STYLE_ID)) return;
    var style = document.createElement('style');
    style.id = STYLE_ID;
    style.type = 'text/css';
    style.textContent = CSS;
    head.appendChild(style);
  }

  function removeStyle() {
    var el = document.getElementById(STYLE_ID);
    if (el) el.remove();
  }

  // SPA 切房间 / 礼物栏重新渲染时补一刀
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
        if (!isLivePage()) return;
        if (window.__DY_LIVE_GIFT_HIDE__ === false) return;
        ensureStyle();
        schedule();
      });
      mo.observe(document.documentElement, { childList: true, subtree: true });
    } catch (e) { }
  }

  function boot() {
    if (!isLivePage()) { log('非直播页，跳过（url=' + location.href + '）'); return; }
    ensureStyle();
    trimBar();
    // 礼物栏是异步渲染的，多补几次
    [800, 2000, 4000, 8000].forEach(function (ms) {
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