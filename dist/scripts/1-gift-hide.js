/* ============================================================================
 * 抖音画质助手 · 直播间：精简礼物栏
 * 版本 2.0.2 ｜ https://github.com/yss161/douyin-quality-helper-plus
 *
 * 隐藏礼物图标，只留「更多」与钻石
 *
 * 用法：抖音客户端里打开对应页面（直播页 / 视频页），在 DevTools 控制台
 *       粘贴本文件全部内容后回车即可。刷新页面后需要重新执行。
 * ==========================================================================*/
/* ============================================================================
 * 抖音直播 —— 礼物栏精简（只保留「更多」二级菜单 + 钻石）
 *
 * 效果：底部礼物栏里的每个礼物图标全部隐去，只留下：
 *         - 「更多 >」按钮  —— 点开就是完整礼物面板（二级菜单）
 *         - 钻石数量        —— 充值/余额显示
 *
 * 【为什么不再依赖 .gitBarOptimizeEnabled】
 * 旧版靠这个 class 定位礼物栏。客户端新版本里它已经不存在（日志里从未打印
 * 「已隐藏 N 个」），所以整块逻辑静默失效。
 *
 * 现在改成完全的「文本特征 + 结构」判定，与 class 名解耦：
 *   1. 先找页面里所有含「钻」的叶子块（礼物项的定价文本，如 9999钻、1钻）
 *   2. 它们的共同祖先，且子节点里有「更多」的，就是礼物栏
 *   3. 栏内除「更多」和钻石之外的同级项，全部打标记隐藏
 *
 * 因为抖音的 class 是构建时哈希的（xAG9cqUC / saw7uCUW …）每次发版都会变，
 * 只有文本是稳定的。
 *
 * 另外仍然隐藏：送礼全屏特效、飘屏。
 *
 * 临时恢复：window.__DY_LIVE_GIFT_HIDE__ = false; 刷新即可。
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

  // 礼物项的定价文本：整段里只有「数字+钻」或含「钻」
  function hasPrice(el) {
    var t = textOf(el);
    if (t.indexOf('钻') < 0) return false;
    if (t.length > 14) return false;          // 太长的多半是整块容器
    return true;
  }

  function looksLikeMore(el) {
    var t = textOf(el);
    if (t.length > 6) return false;
    return t.indexOf('更多') >= 0;
  }

  function isDiamond(el) {
    try {
      if (el.classList && el.classList.contains('giftRechargeContaner')) return true;
      if (el.querySelector && el.querySelector('.giftRechargeContaner')) return true;
    } catch (e) { }
    return false;
  }

  // ---------------------------------------------------------------------
  // 找礼物栏：含「钻」的叶子块的共同祖先里，子节点带「更多」的那个
  // ---------------------------------------------------------------------
  function findGiftBar() {
    var all;
    try { all = document.querySelectorAll('div, li, span'); } catch (e) { return null; }

    // 收集所有「带价格的礼物项」
    var items = [];
    for (var i = 0; i < all.length && items.length < 60; i++) {
      if (hasPrice(all[i])) items.push(all[i]);
    }
    if (items.length < 2) return null;

    // 从某个礼物项往上走，第一个「子节点里既有礼物项又有「更多」」的容器就是礼物栏
    var start = items[0];
    var node = start;
    for (var depth = 0; node && depth < 8; depth++) {
      var kids = node.children;
      var priceKids = 0, hasMore = false, hasDiamond = false;

      for (var k = 0; k < kids.length; k++) {
        if (hasPrice(kids[k])) priceKids++;
        if (looksLikeMore(kids[k])) hasMore = true;
        if (isDiamond(kids[k])) hasDiamond = true;
      }

      // 礼物栏的特征：至少 2 个带价礼物 + 有「更多」入口
      if (priceKids >= 2 && hasMore) return node;

      node = node.parentElement;
    }

    // 退一步：没有「更多」但有多个带价礼物 + 钻石，也算
    node = start;
    for (var d2 = 0; node && d2 < 8; d2++) {
      var ps = 0, dm = false;
      for (var j = 0; j < node.children.length; j++) {
        if (hasPrice(node.children[j])) ps++;
        if (isDiamond(node.children[j])) dm = true;
      }
      if (ps >= 3 && dm) return node;
      node = node.parentElement;
    }

    return null;
  }

  // 礼物项原本是"一整条"：第一个带左圆角，最后一个带右圆角。
  // 藏掉前面的之后，最先露出来的那块会变成"左直角 + 右圆角"，看起来像被切了角。
  function fixFirstCorner(bar) {
    var all;
    try { all = bar.querySelectorAll('*'); } catch (e) { return; }

    var target = null, targetX = Infinity;
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      var r;
      try { r = el.getBoundingClientRect(); } catch (e) { continue; }
      if (r.width < 20 || r.height < 20) continue;
      if (el.getAttribute(HIDDEN_ATTR) === '1') continue;

      var cs;
      try { cs = getComputedStyle(el); } catch (e) { continue; }
      if (cs.display === 'none' || cs.visibility === 'hidden') continue;

      var tl = parseFloat(cs.borderTopLeftRadius) || 0;
      var tr = parseFloat(cs.borderTopRightRadius) || 0;
      var bl = parseFloat(cs.borderBottomLeftRadius) || 0;
      var br = parseFloat(cs.borderBottomRightRadius) || 0;

      if (tr <= 0 || tl > 0) continue;   // 只要"右圆左直"的块

      if (r.left < targetX) { targetX = r.left; target = { el: el, tr: tr, br: br, bl: bl }; }
    }

    if (!target) return;
    try {
      target.el.style.borderTopLeftRadius = target.tr + 'px';
      if (target.br > 0 && target.bl === 0) target.el.style.borderBottomLeftRadius = target.br + 'px';
    } catch (e) { }
  }

  function trimBar() {
    if (!enabled()) { restore(); return 0; }

    var bar = findGiftBar();
    if (!bar) return 0;   // 礼物栏还没渲染出来，等下一轮

    var hidden = 0;
    for (var i = 0; i < bar.children.length; i++) {
      var child = bar.children[i];
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
    log('已启动（首轮隐藏 ' + n + ' 个）  当前 URL=' + location.href);

    // 礼物栏异步渲染，多补几轮（客户端里比网页慢）
    [500, 1200, 2500, 4000, 7000, 11000].forEach(function (ms) {
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
