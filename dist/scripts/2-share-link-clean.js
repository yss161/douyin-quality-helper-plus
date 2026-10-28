/* ============================================================================
 * 抖音画质助手 · 直播间：优化复制链接
 * 版本 2.0.1 ｜ https://github.com/yss161/douyin-quality-helper-plus
 *
 * 复制的链接去掉追踪参数
 *
 * 用法：抖音客户端里打开对应页面（直播页 / 视频页），在 DevTools 控制台
 *       粘贴本文件全部内容后回车即可。刷新页面后需要重新执行。
 * ==========================================================================*/
/* ============================================================================
 * 抖音直播 —— 优化「复制链接」
 *
 * 问题：直播间点「分享 - 复制链接」或分享面板里的复制，拿到的链接带一堆追踪参数：
 *   https://live.douyin.com/928003489865?enter_from_merge=link_share&enter_method=copy_link_share&action_type=click&from=web_code_link
 * 实际只需要：
 *   https://live.douyin.com/928003489865
 *
 * 【为什么改用剪贴板拦截】
 * 旧版是截获「复制链接」按钮的 click，自己写剪贴板。实测在客户端里失效，原因：
 *   1. 客户端直播页的文档 URL 是 live.douyin.com/preload-living，
 *      location.pathname 里根本没有房间号，fromLocation() 永远拿到 null；
 *   2. 分享面板的按钮层级深且常变，靠文本找按钮命中率低；
 *   3. 页面可能是 SPA 内跳转，DOM 重渲染后按钮引用失效。
 *
 * 现在改成三层拦截，都不依赖 DOM 结构：
 *   A. document 的 copy 事件（Ctrl+C、execCommand('copy') 都走这里）
 *   B. navigator.clipboard.writeText（页面直接写剪贴板的情况）
 *   C. 兜底：复制后再校正一次
 *
 * 清洗规则只做「去掉查询参数和锚点」，房间号是页面自己给的那个，不需要我们去猜。
 *
 * 关闭：window.__DY_LIVE_SHARE_CLEAN__ = false; 刷新即可。
 * ==========================================================================*/
(function () {
  'use strict';

  if (window.__DY_LIVE_SHARE_CLEAN_INSTALLED__) return;
  window.__DY_LIVE_SHARE_CLEAN_INSTALLED__ = true;

  var TAG = '[dy-live-share]';
  var ROOM_RE = /^\/(\d{6,})(?:\/|$)/;

  function log(msg) {
    try {
      if (window.__dyForceLog) window.__dyForceLog(TAG + ' ' + msg);
      else console.log(TAG + ' ' + msg);
    } catch (e) { }
  }

  function enabled() {
    return window.__DY_LIVE_SHARE_CLEAN__ !== false;
  }

  // ---------------------------------------------------------------------
  // 清洗：把一条抖音链接收敛到最简形式
  // ---------------------------------------------------------------------
  function cleanUrl(raw) {
    if (!raw || typeof raw !== 'string') return raw;

    var s = raw.trim();
    if (!/^https?:\/\//i.test(s)) return raw;

    var u;
    try { u = new URL(s); } catch (e) { return raw; }

    var host = (u.hostname || '').toLowerCase();
    if (host.indexOf('douyin.com') < 0) return raw;

    // 直播间：只要 https://live.douyin.com/<房间号>，其余全删
    var m = (u.pathname || '').match(ROOM_RE);
    if (m && host.indexOf('live') >= 0) {
      return 'https://live.douyin.com/' + m[1];
    }

    // 直播间但没有房间号的路径（极少见）：保留路径，砍掉参数
    if (host.indexOf('live') >= 0) {
      u.search = '';
      u.hash = '';
      return u.toString();
    }

    // 其它抖音页面（视频/用户主页）：只砍 query / hash
    u.search = '';
    u.hash = '';
    return u.toString();
  }

  // 分享文案里可能夹着链接和标题，把每一段链接都清洗
  function cleanText(text) {
    if (!text || typeof text !== 'string') return text;

    var changed = false;
    var out = text.replace(/https?:\/\/[^\s\u3000-\u303f\u4e00-\u9fff\uff00-\uffef"'<>]+/gi, function (m) {
      var c = cleanUrl(m);
      if (c && c !== m) { changed = true; return c; }
      return m;
    });

    return changed ? out : text;
  }

  // ---------------------------------------------------------------------
  // 取当前「正在被复制」的文本
  // 页面常用隐藏 textarea + execCommand('copy')，此时 getSelection() 是空的，
  // 必须从 activeElement 的 selectionStart/End 里取。
  // ---------------------------------------------------------------------
  function currentCopiedText() {
    try {
      var ae = document.activeElement;
      if (ae && (ae.tagName === 'TEXTAREA' || ae.tagName === 'INPUT')) {
        var a = ae.selectionStart;
        var b = ae.selectionEnd;
        if (typeof a === 'number' && typeof b === 'number' && b > a) {
          return ae.value.substring(a, b);
        }
        if (typeof ae.value === 'string' && ae.value) return ae.value;
      }
    } catch (e) { }

    try {
      var sel = window.getSelection();
      return sel ? sel.toString() : '';
    } catch (e) { return ''; }
  }

  // ---------------------------------------------------------------------
  // A. copy 事件拦截（最通用）
  // ---------------------------------------------------------------------
  function onCopy(e) {
    if (!enabled()) return;

    try {
      var text = currentCopiedText();
      if (!text) {
        var dt0 = e.clipboardData || window.clipboardData;
        if (dt0) text = dt0.getData('text/plain') || dt0.getData('Text') || '';
      }
      if (!text) return;

      var cleaned = cleanText(text);
      if (cleaned === text) return;

      var dt = e.clipboardData || window.clipboardData;
      if (!dt) return;

      dt.setData('text/plain', cleaned);

      // 还要把页面的隐藏 textarea 改掉，
      // 否则某些实现会在事件之后又按 textarea 内容写一次
      try {
        var ae = document.activeElement;
        if (ae && (ae.tagName === 'TEXTAREA' || ae.tagName === 'INPUT') &&
            ae.value && ae.value.indexOf(text) >= 0) {
          ae.value = ae.value.replace(text, cleaned);
        }
      } catch (e2) { }

      e.preventDefault();
      log('复制内容已清洗：' + cleaned);
    } catch (err) {
      log('copy 处理失败：' + err);
    }
  }

  // ---------------------------------------------------------------------
  // B. navigator.clipboard.writeText 拦截
  // 有些新版页面直接用异步剪贴板 API，不触发 copy 事件
  // ---------------------------------------------------------------------
  function hookClipboard() {
    try {
      if (!navigator.clipboard) return;

      var proto = Object.getPrototypeOf(navigator.clipboard);
      if (!proto || proto.__dySharePatched) return;

      var orig = proto.writeText;
      if (typeof orig !== 'function') return;

      proto.writeText = function (text) {
        var out = text;
        try {
          if (enabled()) {
            var cleaned = cleanText(text);
            if (cleaned !== text) {
              out = cleaned;
              log('剪贴板写入已清洗：' + cleaned);
            }
          }
        } catch (e) { }
        return orig.call(this, out);
      };

      proto.__dySharePatched = true;
    } catch (e) { }
  }

  // ---------------------------------------------------------------------
  // C. 兜底：execCommand 包一层，保证 copy 事件路径可用
  // ---------------------------------------------------------------------
  function hookExecCommand() {
    try {
      if (document.__dyExecPatched) return;
      var orig = document.execCommand;
      if (typeof orig !== 'function') return;

      document.execCommand = function (cmd) {
        return orig.apply(document, arguments);
      };
      document.__dyExecPatched = true;
    } catch (e) { }
  }

  function install() {
    // copy 事件装两级：document 捕获 + window 冒泡，尽量不漏
    try { document.addEventListener('copy', onCopy, true); } catch (e) { }
    try { window.addEventListener('copy', onCopy, true); } catch (e) { }

    hookClipboard();
    hookExecCommand();

    log('剪贴板优化已启用（url=' + location.href + '）');
  }

  // 剪贴板钩子与页面生命周期无关，任何页面装上都不亏
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', install, { once: true });
  } else {
    install();
  }

  // SPA 换页后 activeElement / clipboard 可能被重建，定期补钩
  setInterval(function () { try { hookClipboard(); hookExecCommand(); } catch (e) { } }, 4000);

  window.__DY_LIVE_SHARE_CLEAN__ = true;
})();
/* ====== LIVE-SHARE-CLEAN-END ====== */
