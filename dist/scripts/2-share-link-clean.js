/* ============================================================================
 * 抖音画质助手 · 直播间：优化复制链接
 * 版本 2.0.0 ｜ https://github.com/yss161/douyin-quality-helper-plus
 *
 * 复制的链接去掉追踪参数
 *
 * 用法：抖音客户端里打开对应页面（直播页 / 视频页），在 DevTools 控制台
 *       粘贴本文件全部内容后回车即可。刷新页面后需要重新执行。
 * ==========================================================================*/
/* ============================================================================
 * 抖音直播 · 优化「复制链接」（独立注入版）
 *
 * 与 injector/live-share-clean.js 同一份逻辑，方便单独注入：
 * 直播间点「分享 → 复制链接」时，写进剪贴板的是干净的
 *   https://live.douyin.com/<房间号>
 * 而不是带一串追踪参数的原始链接。
 *
 * 链接来源（按可靠性）：
 *   1. React fiber 里的 shareInfo / roomInfo / web_rid
 *   2. 地址栏 pathname
 * 两者都拿不到就不干预，让页面按原样复制。
 *
 * 关闭：控制台执行 window.__DY_LIVE_SHARE_CLEAN__ = false; 后刷新。
 * ==========================================================================*/

(function () {
  'use strict';

  if (window.__DY_LIVE_SHARE_CLEAN_INSTALLED__) return;
  window.__DY_LIVE_SHARE_CLEAN_INSTALLED__ = true;

  var TAG = '[dy-live-share]';
  var CLICK_ATTR = 'data-dy-share-copied';

  function log(msg) {
    try {
      if (window.__dyForceLog) window.__dyForceLog(TAG + ' ' + msg);
      else console.log(TAG + ' ' + msg);
    } catch (e) { }
  }

  function isLivePage() {
    try {
      var h = location.hostname;
      if (h.indexOf('live.douyin.com') >= 0) return true;
      if (h.indexOf('douyin.com') >= 0 && /\/live|live\//.test(location.pathname)) return true;
      if (h.indexOf('douyin.com') >= 0 && location.search.indexOf('enter_from=live') >= 0) return true;
      return false;
    } catch (e) { return false; }
  }

  function textOf(el) {
    try { return (el.textContent || '').replace(/\s+/g, ''); } catch (e) { return ''; }
  }

  // 递归找 React fiber 里的房间/分享信息。
  // 直播间的结构比视频页深，字段名也不一样，所以按"看起来像"来兜：
  //   roomInfo / shareInfo / web_rid / roomId / shareUrl
  function pluckShareUrl(fiber) {
    var seen = 0;
    var found = null;

    function pick(node) {
      if (found || !node || seen > 400) return;
      seen++;
      var props = node.memoizedProps;
      if (!props || typeof props !== 'object') return;

      // 直接就是分享信息对象
      if (props.shareInfo && typeof props.shareInfo === 'object') {
        var u = props.shareInfo.shareUrl || props.shareInfo.url || props.shareInfo.share_url;
        if (typeof u === 'string' && u.indexOf('douyin.com') >= 0) { found = u; return; }
      }
      // roomInfo 里可能挂着 shareUrl / roomUrl
      if (props.roomInfo && typeof props.roomInfo === 'object') {
        var ri = props.roomInfo;
        var cand = ri.shareUrl || ri.roomUrl || ri.url || (ri.share && (ri.share.url || ri.share.shareUrl));
        if (typeof cand === 'string' && cand.indexOf('douyin.com') >= 0) { found = cand; return; }
      }
      // 有些分支直接把 shareUrl 挂在 props 上
      if (typeof props.shareUrl === 'string' && props.shareUrl.indexOf('douyin.com') >= 0) {
        found = props.shareUrl;
        return;
      }
      // 任何带 web_rid / roomId 的组合，用拼出来的地址
      if (props.web_rid || props.webRid) {
        var rid = String(props.web_rid || props.webRid).replace(/\D/g, '');
        if (rid.length >= 6) { found = 'https://live.douyin.com/' + rid; return; }
      }
    }

    function walk(node, depth) {
      if (found || !node || depth > 30) return;
      pick(node);
      if (found) return;
      walk(node.return, depth + 1);          // 往上
      if (!found) walk(node.child, depth + 1); // 往下
    }

    walk(fiber, 0);
    return found;
  }

  // 从页面地址推：live.douyin.com/<数字> 这种形式最干净
  function fromLocation() {
    try {
      var m = location.pathname.match(/^\/(\d{6,})/);
      if (m) return 'https://live.douyin.com/' + m[1];
    } catch (e) { }
    return null;
  }

  // 去掉链接上的追踪参数，只留域名 + 路径（保留必要的 path）
  function cleanUrl(raw) {
    if (typeof raw !== 'string' || !raw) return null;

    // 只接受"看起来像链接"的输入。
    // new URL('随便什么', location.href) 会把相对串拼到当前域名上，
    // 得出 https://live.douyin.com/not%20a%20url 这种垃圾结果，所以先挡一道。
    var s = raw.trim();
    if (!/^(https?:\/\/|\/)/i.test(s)) return null;

    var u;
    try { u = new URL(s, location.href); } catch (e) { return null; }
    if (!u.hostname) return null;

    // 直播链接统一收敛到 https://live.douyin.com/<房间号>
    var host = u.hostname.replace(/^www\./, '');
    if (host.indexOf('douyin.com') < 0) return raw;   // 非抖音域名不动

    var m = (u.pathname || '').match(/^\/(\d{6,})/);
    if (m && (host.indexOf('live') >= 0 || u.pathname.length < 40)) {
      return 'https://live.douyin.com/' + m[1];
    }

    // 其它形式：只砍掉 query / hash，保留路径
    u.search = '';
    u.hash = '';
    if (host.indexOf('live') >= 0) u.protocol = 'https:';
    return u.toString();
  }

  function copyText(text) {
    // 优先 Clipboard API（需要用户手势上下文，点击回调里满足）
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return fallbackCopy(text); });
      }
    } catch (e) { }
    return Promise.resolve(fallbackCopy(text));
  }

  function fallbackCopy(text) {
    try {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.cssText = 'position:fixed;left:-9999px;top:0;opacity:0;';
      document.body.appendChild(ta);
      ta.select();
      ta.setSelectionRange(0, ta.value.length);
      var ok = false;
      try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
      ta.remove();
      return ok;
    } catch (e) { return false; }
  }

  function toast(msg, isError) {
    try {
      // 复用页面自己的 qmsg（由 dy-host 暴露的 GM 环境提供）
      var q = window.Qmsg && window.Qmsg.default;
      if (q && typeof q[isError ? 'error' : 'success'] === 'function') {
        q[isError ? 'error' : 'success'](msg);
        return;
      }
    } catch (e) { }
    try { console.log(TAG + ' ' + msg); } catch (e) { }
  }

  // 判断一个元素是不是「复制链接」按钮
  function isCopyLinkButton(el) {
    if (!el || !el.tagName) return false;
    if (el.tagName.toLowerCase() !== 'button' && el.tagName.toLowerCase() !== 'div' && el.tagName.toLowerCase() !== 'span') return false;
    var t = textOf(el);
    if (t.indexOf('复制链接') < 0) return false;
    // 排除整个面板这种超大容器：取文本长度最小的命中项
    return t.length <= 12;
  }

  // 在分享面板里定位「复制链接」按钮：从点击目标往上找，再往下找
  function findCopyButton(start) {
    var node = start;
    var depth = 0;
    while (node && depth < 8) {
      if (isCopyLinkButton(node)) return node;
      var inner = node.querySelectorAll ? node.querySelectorAll('button, div, span') : [];
      for (var i = 0; i < inner.length; i++) {
        if (isCopyLinkButton(inner[i])) return inner[i];
      }
      node = node.parentElement;
      depth++;
    }
    return null;
  }

  function handleClick(event) {
    if (window.__DY_LIVE_SHARE_CLEAN__ === false) return;
    if (!isLivePage()) return;

    var btn = findCopyButton(event.target);
    if (!btn) return;

    // 已经处理过（同一个按钮上 DOM 事件可能冒泡多次）
    if (btn.getAttribute(CLICK_ATTR) === '1') return;

    // 取干净链接
    var clean = null;
    try {
      var fiber = null;
      // 页面的 React 实例挂载工具不一定有，直接找 __reactFiber$ 属性
      var n = btn;
      while (n && !fiber) {
        var keys = n ? Object.keys(n) : [];
        for (var k = 0; k < keys.length; k++) {
          if (keys[k].indexOf('__reactFiber$') === 0) { fiber = n[keys[k]]; break; }
        }
        n = n.parentElement;
      }
      if (fiber) {
        var raw = pluckShareUrl(fiber);
        if (raw) clean = cleanUrl(raw);
      }
    } catch (e) { log('fiber 读取失败: ' + e); }

    if (!clean) clean = fromLocation();
    if (!clean) return;   // 拿不到就不干预，让页面自己复制

    log('原始链接已优化为：' + clean);

    // 阻止页面原本的复制逻辑（它会写入带参数的链接）
    try {
      event.stopPropagation();
      event.preventDefault();
    } catch (e) { }

    btn.setAttribute(CLICK_ATTR, '1');
    setTimeout(function () { try { btn.removeAttribute(CLICK_ATTR); } catch (e) { } }, 300);

    copyText(clean).then(function (ok) {
      toast(ok ? '已复制链接' : '复制链接失败', !ok);
    });
  }

  function install() {
    if (!isLivePage()) { log('非直播页，跳过（url=' + location.href + '）'); return; }
    // 捕获阶段拦，抢在页面自己的 handler 之前
    document.addEventListener('click', handleClick, true);
    log('已启用直播间复制链接优化');
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', install, { once: true });
  } else {
    install();
  }

  window.__DY_LIVE_SHARE_CLEAN__ = true;
})();
/* ====== LIVE-SHARE-CLEAN-END ====== */