/* ============================================================================
 * 抖音直播 · 隐藏礼物栏（独立注入版）
 *
 * 用途：不想装整包脚本时，只想隐藏直播间底部那一排礼物图标。
 *       直接把本文件注入抖音客户端的页面即可（和 injectors 里用的是同一份逻辑）。
 *
 * 隐藏范围（截图红框那一排）：
 *   - 底部礼物栏：小心心 / 人气票 / 蝶恋秋鹤 / Thuglife / 美味烤鸡 / 鲜花 / 玫瑰 ...
 *   - 点击礼物后弹出的礼物面板
 *   - 送礼时的全屏特效、飘屏
 *   - 礼物充值入口
 *
 * 保留：底部控制条（清晰度、全屏、音量等）、弹幕、聊天区、游戏按钮等。
 *
 * 临时恢复显示：在控制台执行 window.__DY_LIVE_GIFT_HIDE__ = false; 后刷新即可。
 * ==========================================================================*/
(function () {
  'use strict';

  if (window.__DY_LIVE_GIFT_HIDE_INSTALLED__) return;
  window.__DY_LIVE_GIFT_HIDE_INSTALLED__ = true;

  var TAG = '[dy-live-gift]';
  var STYLE_ID = 'dy-live-gift-hide-style';

  function log(msg) {
    try {
      if (window.__dyForceLog) window.__dyForceLog(TAG + ' ' + msg);
      else console.log(TAG + ' ' + msg);
    } catch (e) { }
  }

  // 只隐藏礼物相关，其余按钮保持原样。
  // 礼物栏被抽掉后底部会空出一块，用这两条把控制条压回原位。
  var CSS = [
    /* ---- 底部礼物栏（截图红框那一排） ---- */
    '.gitBarOptimizeEnabled { display: none !important; }',
    'div[data-e2e="living-container"] #BottomLayout > div:has(> .gitBarOptimizeEnabled) { display: none !important; }',

    /* ---- 礼物面板 / 特效 / 飘屏 ---- */
    '#GiftEffectLayout, #GiftTrayLayout, #GiftMenuLayout { display: none !important; }',
    'div[id^="gift_effect_bg_"] { display: none !important; }',
    '.GiftEffectPlugin, .GiftTrayPlugin { display: none !important; }',

    /* ---- 礼物充值入口 ---- */
    '.giftRechargeContaner { display: none !important; }',

    /* ---- 礼物栏消失后把底部控制条压回原位 ---- */
    'div[data-e2e="living-container"] xg-controls xg-inner-controls:has(+ div .gitBarOptimizeEnabled) { bottom: 0 !important; }',
    'div[data-e2e="living-container"] #BottomLayout:empty { display: none !important; }',
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

  function ensureStyle() {
    if (window.__DY_LIVE_GIFT_HIDE__ === false) { removeStyle(); return; }
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

  // SPA 切房间会重建 DOM，样式表可能被 head 重建冲掉，这里盯着补
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
    if (!isLivePage()) { log('非直播页，跳过（url=' + location.href + '）'); return; }
    ensureStyle();
    startObserver();
  }

  if (document.readyState === 'loading') {
    // 尽量早：<head> 一出现就挂上，避免礼物栏先闪一下
    var timer = setInterval(function () {
      if (document.head || document.documentElement) { clearInterval(timer); boot(); }
    }, 4);
    document.addEventListener('DOMContentLoaded', boot, { once: true });
  } else {
    boot();
  }

  window.__DY_LIVE_GIFT_HIDE__ = true;
})();
