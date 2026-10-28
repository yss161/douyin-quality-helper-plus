/* ============================================================================
 * 抖音画质助手 · 直播间：优化复制链接
 * 版本 2.0.6 ｜ https://github.com/yss161/douyin-quality-helper-plus
 *
 * 复制的链接去掉追踪参数
 *
 * 用法：抖音客户端里打开对应页面（直播页 / 视频页），在 DevTools 控制台
 *       粘贴本文件全部内容后回车即可。刷新页面后需要重新执行。
 * ==========================================================================*/
/* ============================================================================
 * 抖音 —— 优化「复制链接」
 *
 * 目标：保留有效信息，去掉无用参数。
 *
 * 【直播间：一律收敛到最简形式】
 *   https://live.douyin.com/928003489865?enter_from_merge=link_share&...
 *     ->  https://live.douyin.com/928003489865
 *   任何直播间、任何参数组合，都只留 https://live.douyin.com/<房间号>。
 *   房间号从这些地方取（按可靠性）：
 *     1. 路径 /<数字>
 *     2. 查询参数 room_id / web_rid / roomId
 *     3. 页面当前地址（SPA 内跳转时 location 里可能才有）
 *   拿不到房间号就原样返回，不乱猜。
 *
 * 【其它抖音页面：保留有效信息，去掉追踪参数】
 *   1. 有效信息（一定保留）
 *      modal_id / vid / aweme_id / item_ids / sec_uid / sec_user_id / type
 *      这些决定「打开哪条内容」，删了链接就指错地方。
 *      典型：https://www.douyin.com/discover?modal_id=73xxxx
 *      路径只有 /discover，视频 id 全在 modal_id 上，砍了就废。
 *
 *   2. 追踪参数（一定删）
 *      enter_from / enter_from_merge / enter_method / action_type / from /
 *      from_gid / from_tab_name / previous_page / share_token / author_id /
 *      is_from_webapp / iid / timestamp / u_code / region / mid …
 *
 *   3. 未知参数
 *      路径已能唯一确定内容时（/video/<id>、/user/<sec_uid>）视为噪音删掉；
 *      否则保守保留，避免误伤新出现的功能参数。
 *
 *   短链（v.douyin.com / v.iesdouyin.com）本身就是最简形式，原样返回。
 *   非抖音域名不动。
 *
 * 关闭：window.__DY_LIVE_SHARE_CLEAN__ = false; 刷新即可。
 * ==========================================================================*/
(function () {
  'use strict';

  if (window.__DY_LIVE_SHARE_CLEAN_INSTALLED__) return;
  window.__DY_LIVE_SHARE_CLEAN_INSTALLED__ = true;

  var TAG = '[dy-live-share]';

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
  // 有效信息：决定「打开哪条内容」，一定保留
  // ---------------------------------------------------------------------
  var KEEP = {
    modal_id: 1, vid: 1, aweme_id: 1, item_ids: 1,
    room_id: 1, web_rid: 1, roomid: 1,
    sec_uid: 1, sec_user_id: 1, type: 1
  };

  // ---------------------------------------------------------------------
  // 追踪 / 统计参数：不影响打开，一定删
  // ---------------------------------------------------------------------
  var TRACKING = {
    enter_from: 1, enter_from_merge: 1, enter_method: 1, enter_from_page: 1,
    action_type: 1, from: 1, from_gid: 1, from_tab_name: 1, from_source: 1,
    from_previous: 1, from_page: 1, from_search: 1,
    previous_page: 1, previous_page_position: 1, page_from: 1,
    share_token: 1, share_link_id: 1, share_sign: 1, share_iid: 1,
    share_app_id: 1, share_app_name: 1,
    author_id: 1, is_from_webapp: 1, sender_device: 1, sender_web_id: 1,
    web_id: 1, iid: 1, timestamp: 1, u_code: 1, with_sec_did: 1, sec_did: 1,
    region: 1, mid: 1, csrf_token: 1, csrf: 1,
    ug_source: 1, refer: 1, referer: 1, sp: 1, sc: 1, se: 1, so: 1,
    aid: 1, cid: 1, app: 1, gid: 1, gid_source: 1,
    tt_from: 1, tracker: 1, is_copy_url: 1, is_retry: 1,
    _r: 1, _t: 1, _d: 1
  };

  // ---------------------------------------------------------------------
  // 提取直播间房间号。拿不到返回 null。
  // ---------------------------------------------------------------------
  function liveRoomId(u) {
    // 1. 路径 /<数字>
    var m = (u.pathname || '').match(/^\/(\d{6,})(?:\/|$)/);
    if (m) return m[1];

    // 2. 查询参数里的 room_id / web_rid（客户端分享常见）
    var keys = ['room_id', 'web_rid', 'roomid', 'roomId', 'webRid'];
    for (var i = 0; i < keys.length; i++) {
      var v;
      try { v = u.searchParams.get(keys[i]); } catch (e) { v = null; }
      if (v) {
        var d = String(v).replace(/\D/g, '');
        if (d.length >= 6) return d;
      }
    }

    // 3. 当前页面地址（SPA 内跳转时 location 里可能才有）
    try {
      var here = new URL(location.href);
      var hm = (here.pathname || '').match(/^\/(\d{6,})(?:\/|$)/);
      if (hm) return hm[1];
    } catch (e) { }

    return null;
  }

  // ---------------------------------------------------------------------
  // 路径是否已能唯一确定内容？能的话查询参数一律视为噪音
  // ---------------------------------------------------------------------
  function isSelfContained(host, path) {
    if (host.indexOf('live') >= 0 && /^\/\d{6,}$/.test(path)) return true;
    if (/^\/(video|note)\/\d+/.test(path)) return true;
    if (/^\/user\/[^\/]+/.test(path)) return true;
    return false;
  }

  // ---------------------------------------------------------------------
  // 核心：清洗一条链接
  // ---------------------------------------------------------------------
  function cleanUrl(raw) {
    if (!raw || typeof raw !== 'string') return raw;

    var s = raw.trim();
    if (!/^https?:\/\//i.test(s)) return raw;

    var u;
    try { u = new URL(s); } catch (e) { return raw; }

    var host = (u.hostname || '').toLowerCase();
    var isLiveHost = host.indexOf('live.douyin.com') >= 0 || host.indexOf('live.iesdouyin.com') >= 0;
    var isDouyin = host.indexOf('douyin.com') >= 0;
    if (!isDouyin) return raw;

    // 短链（v.douyin.com）已经是最简形式，改了就失效
    if (/^v[0-9a-z]*\./.test(host)) return raw;

    // ---------- 直播间：只留 https://live.douyin.com/<房间号> ----------
    if (isLiveHost) {
      var rid = liveRoomId(u);
      if (rid) return 'https://live.douyin.com/' + rid;
      // 拿不到房间号：保守处理，只砍追踪参数
      u.search = '';
      u.hash = '';
      return u.protocol + '//' + u.host + (u.pathname || '/');
    }

    // 非直播域名但带直播意图（www.douyin.com/...?type=live&room_id=xxx）
    try {
      var t = (u.searchParams.get('type') || '').toLowerCase();
      var rid2 = u.searchParams.get('room_id') || u.searchParams.get('web_rid');
      if ((t === 'live' || rid2) && rid2) {
        var d2 = String(rid2).replace(/\D/g, '');
        if (d2.length >= 6) return 'https://live.douyin.com/' + d2;
      }
    } catch (e) { }

    // ---------- 其它页面 ----------
    var path = u.pathname || '/';
    if (path.length > 1 && path.charAt(path.length - 1) === '/') path = path.slice(0, -1);

    var selfContained = isSelfContained(host, path);
    var kept = [];

    try {
      u.searchParams.forEach(function (value, key) {
        var k = key.toLowerCase();
        if (KEEP[k]) { kept.push(key + '=' + value); return; }
        if (TRACKING[k]) return;
        if (selfContained) return;
        kept.push(key + '=' + value);
      });
    } catch (e) { }

    var out = u.protocol + '//' + u.host + path;
    if (kept.length) out += '?' + kept.join('&');
    if (u.hash && u.hash.length > 1) out += u.hash;
    return out;
  }

  // ---------------------------------------------------------------------
  // 分享文案里可能夹着标题和链接，逐个链接清洗
  // ---------------------------------------------------------------------
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
  // 页面常用隐藏 textarea + execCommand('copy')，此时 getSelection() 是空的
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
            if (cleaned !== text) { out = cleaned; log('剪贴板写入已清洗：' + cleaned); }
          }
        } catch (e) { }
        return orig.call(this, out);
      };

      proto.__dySharePatched = true;
    } catch (e) { }
  }

  function install() {
    try { document.addEventListener('copy', onCopy, true); } catch (e) { }
    try { window.addEventListener('copy', onCopy, true); } catch (e) { }

    hookClipboard();
    log('剪贴板优化已启用（url=' + location.href + '）');
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', install, { once: true });
  } else {
    install();
  }

  setInterval(function () { try { hookClipboard(); } catch (e) { } }, 4000);

  window.__DY_LIVE_SHARE_CLEAN__ = true;
})();
/* ====== LIVE-SHARE-CLEAN-END ====== */
