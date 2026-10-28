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
 *    │    ├─ item(99钻)
 *    │    ├─ item(1钻)
 *    │    └─ ... 十几个
 *    ├─ more        <- 「更多 >」，是 list 的**兄弟节点**
 *    ├─ diamond     <- 钻石，也是兄弟
 *    └─ game        <- 「游戏 >」，也是兄弟
 *
 *   所以不能要求「同一容器的子项里既有礼物又有『更多』」—— 那个条件永远不成立。
 *   正确做法：找到**礼物列表容器**（直接子项含「钻」最多的那个），
 *             只隐藏它自己的子项，兄弟节点一律不碰。
 *
 * 为什么不写死 class 名：
 *   抖音的 class 是构建时哈希的（xAG9cqUC / saw7uCUW …），每次发版都会变。
 *   这里用「文本特征」判定，只有文本稳定：
 *     带价格的礼物项 -> 文本里有「钻」（如 99钻、1钻、299钻）
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

  // 礼物项：文本短、含「钻」
  function hasPrice(el) {
    var t = textOf(el);
    if (t.indexOf('钻') < 0) return false;
    if (t.length > 16) return false;
    return true;
  }

  // ---------------------------------------------------------------------
  // 找礼物列表容器：直接子项里「带价格」的个数最多，且至少 2 个
  // ---------------------------------------------------------------------
  function findItemList() {
    var all;
    try { all = document.querySelectorAll('div, ul, li'); } catch (e) { return null; }

    var best = null, bestScore = 0;

    for (var i = 0; i < all.length; i++) {
      var node = all[i];
      var kids = node.children;
      if (!kids || kids.length < 2) continue;

      var score = 0;
      for (var j = 0; j < kids.length; j++) {
        if (hasPrice(kids[j])) score++;
      }

      if (score > bestScore) { bestScore = score; best = node; }
    }

    // 至少两个带价礼物，才认为是礼物列表（避免误伤偶出现了「钻」字的区域）
    return bestScore >= 2 ? best : null;
  }

  // 礼物项原本是"一整条"：第一个带左圆角，最后一个带右圆角。
  // 藏掉前面的之后，最先露出来的那块会变成"左直角 + 右圆角"，看起来像被切了角。
  function fixFirstCorner(host) {
    var all;
    try { all = host.querySelectorAll('*'); } catch (e) { return; }

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

    var list = findItemList();
    if (!list) return 0;   // 礼物栏还没渲染出来，等下一轮

    var hidden = 0;
    for (var i = 0; i < list.children.length; i++) {
      var child = list.children[i];
      if (!hasPrice(child)) continue;               // 只隐藏礼物项
      if (child.getAttribute(HIDDEN_ATTR) === '1') continue;
      child.setAttribute(HIDDEN_ATTR, '1');
      hidden++;
    }

    // 圆角修正：隐藏后在父级范围内找"最左边那块"，通常就是「更多」
    var host = list.parentElement || list;
    fixFirstCorner(host);

    if (hidden > 0) log('已隐藏 ' + hidden + ' 个礼物图标（保留「更多」与钻石）');
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

    // 礼物栏异步渲染（客户端比网页慢），多补几轮
    [500, 1200, 2500, 4000, 7000, 11000, 16000].forEach(function (ms) {
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
