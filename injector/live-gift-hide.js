/* ============================================================================
 * 抖音直播 —— 隐藏礼物相关元素
 *
 * 为什么单独写一个模块，而不是改上游脚本：
 *   上游 shieldGiftColumn() 里的选择器在当前版本已失效（实测命中 0）：
 *     xg-controls > div:has(div[data-e2e="gifts-container"])   -> 0
 *     >div> div:has(>pace-island>.gitBarOptimizeEnabled)        -> 0
 *   而真实结构是 living-container 里的 .gitBarOptimizeEnabled。
 *
 * 实测确认存在的节点（live.douyin.com/<房间号>）：
 *   .gitBarOptimizeEnabled   底部礼物栏（礼物图标那一排）
 *   #GiftEffectLayout        礼物特效层
 *   #GiftTrayLayout          礼物飘屏层
 *   #GiftMenuLayout          礼物面板层
 *   .giftRechargeContaner    充值入口
 *
 * 全部用 CSS 隐藏，不改 DOM 结构，所以不会破坏 React 的事件绑定。
 * 想临时关掉：window.__DY_LIVE_GIFT_HIDE__ = false; 然后刷新。
 * ==========================================================================*/
(function () {
  'use strict';

  if (window.__DY_LIVE_GIFT_HIDE_INSTALLED__) return;
  window.__DY_LIVE_GIFT_HIDE_INSTALLED__ = true;

  var TAG = '[dy-live-gift]';

  function log(msg) {
    try {
      if (window.__dyForceLog) window.__dyForceLog(TAG + ' ' + msg);
      else console.log(TAG + ' ' + msg);
    } catch (e) { }
  }

  var STYLE_ID = 'dy-live-gift-hide-style';

  // 只隐藏礼物相关，其余按钮（游戏、更多、钻石等）保持原样。
  // 礼物栏被抽掉后底部会空出一块，把它压回原位。
  var CSS = [
    /* ---- 底部礼物栏 ---- */
    '.gitBarOptimizeEnabled { display: none !important; }',
    'div[data-e2e="living-container"] #BottomLayout > div:has(> .gitBarOptimizeEnabled) { display: none !important; }',

    /* ---- 礼物特效 / 飘屏 / 面板 ---- */
    '#GiftEffectLayout, #GiftTrayLayout, #GiftMenuLayout { display: none !important; }',
    'div[id^="gift_effect_bg_"] { display: none !important; }',
    '.GiftEffectPlugin, .GiftTrayPlugin { display: none !important; }',

    /* ---- 充值入口 ---- */
    '.giftRechargeContaner { display: none !important; }',

    /* ---- 礼物栏被隐藏后，把底部控制条压回原位（否则留一条空白） ---- */
    'div[data-e2e="living-container"] xg-controls xg-inner-controls:has(+ div .gitBarOptimizeEnabled) { bottom: 0 !important; }',
    'div[data-e2e="living-container"] #BottomLayout:empty { display: none !important; }',
  ].join('\n');

  function isLivePage() {
    try {
      var h = location.hostname;
      if (h.indexOf('live.douyin.com') >= 0) return true;
      // 首页里的直播 tab
      if (h.indexOf('douyin.com') >= 0 && /\/live|live\//.test(location.pathname)) return true;
      if (h.indexOf('douyin.com') >= 0 && location.search.indexOf('enter_from=live') >= 0) return true;
      return false;
    } catch (e) { return false; }
  }

  function ensureStyle() {
    if (window.__DY_LIVE_GIFT_HIDE__ === false) {
      removeStyle();
      return;
    }
    var head = document.head || document.documentElement;
    if (!head) return;
    if (document.getElementById(STYLE_ID)) return;
    var style = document.createElement('style');
    style.id = STYLE_ID;
    style.type = 'text/css';
    style.textContent = CSS;
    head.appendChild(style);
    log('礼物相关元素已隐藏');
  }

  function removeStyle() {
    var el = document.getElementById(STYLE_ID);
    if (el) { el.remove(); log('已恢复礼物显示'); }
  }

  // SPA 切房间 / 切页面时会重建 DOM，样式表本身不丢，但可能被 head 重建清掉
  function startObserver() {
    try {
      var mo = new MutationObserver(function () {
        if (!isLivePage()) return;
        if (window.__DY_LIVE_GIFT_HIDE__ === false) return;
        if (!document.getElementById(STYLE_ID)) ensureStyle();
      });
      mo.observe(document.documentElement, { childList: true, subtree: true });
    } catch (e) { }
  }

  function boot() {
    if (!isLivePage()) {
      log('非直播页，跳过（url=' + location.href + '）');
      return;
    }
    ensureStyle();
    startObserver();
  }

  if (document.readyState === 'loading') {
    // 尽量早：<head> 一出现就能挂样式，避免礼物栏闪现
    var timer = setInterval(function () {
      if (document.head || document.documentElement) {
        clearInterval(timer);
        boot();
      }
    }, 4);
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }

  window.__DY_LIVE_GIFT_HIDE__ = true;
})();
/* ====== LIVE-GIFT-HIDE-END ====== */
