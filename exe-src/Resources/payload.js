/* ============================================================================
 * 抖音优化 · preload 装载器
 *
 * 由 patch-preload.js 追加进抖音的 loose preload（isolated=false）。
 *
 * 为什么不在 preload 里直接内联整个 bundle：
 *   - 安装目录在 C:\Program Files，写它需要管理员权限；而 bundle 有 2MB、
 *     且要跟着用户脚本一起更新。把它放在 %APPDATA% 下，以后换版本不用再提权。
 *   - 页面里没有 require，所以先在 preload 侧把代码读成字符串，再交给页面 eval。
 *
 * 开关（%APPDATA%\douyin\inject-mode.json）：
 *   {"scripts":true,"quality":true}   —— 两个都要
 *   {"scripts":false,"quality":true}  —— 只装原项目的自动最高画质
 *   {"scripts":true,"quality":false}  —— 只装抖音优化脚本
 * 文件不存在时按"两个都开"处理，老安装不受影响。
 * ==========================================================================*/
(function () {
  'use strict';

  var TAG = '[dy-userscript]';

  function emit(line) {
    try {
      if (typeof window !== 'undefined' && window.__dyForceLog) window.__dyForceLog(TAG + ' ' + line);
      else if (typeof console !== 'undefined') console.log(TAG + ' ' + line);
    } catch (e) { }
  }

  // 页面里没有 require：这里先把 fs 拿到手
  var nodeRequire = null;
  try { nodeRequire = typeof require === 'function' ? require : null; } catch (e) { nodeRequire = null; }

  function appDataDir() {
    if (!nodeRequire) return null;
    try {
      var path = nodeRequire('path');
      var base = (typeof process !== 'undefined' && process.env && process.env.APPDATA) || '';
      if (!base) return null;
      return path.join(base, 'douyin');
    } catch (e) { return null; }
  }

  // 默认两个都开：老安装没有这个文件，行为必须和以前一样
  var flags = { scripts: true, quality: true };

  function readFlags() {
    if (!nodeRequire) return flags;
    try {
      var fs = nodeRequire('fs');
      var path = nodeRequire('path');
      var dir = appDataDir();
      if (!dir) return flags;
      var file = path.join(dir, 'inject-mode.json');
      if (!fs.existsSync(file)) return flags;
      var raw = fs.readFileSync(file, 'utf8');
      var obj = JSON.parse(raw);
      if (obj && typeof obj === 'object') {
        if (typeof obj.scripts === 'boolean') flags.scripts = obj.scripts;
        if (typeof obj.quality === 'boolean') flags.quality = obj.quality;
      }
    } catch (e) {
      emit('read inject-mode.json failed, assuming both on: ' + e);
    }
    return flags;
  }

  function readBundle() {
    if (!nodeRequire) return null;
    try {
      var fs = nodeRequire('fs');
      var path = nodeRequire('path');
      var dir = appDataDir();
      if (!dir) return null;
      var file = path.join(dir, 'userscript.bundle.js');
      if (!fs.existsSync(file)) return null;
      var stat = fs.statSync(file);
      if (stat.size < 1000) return null;
      return fs.readFileSync(file, 'utf8');
    } catch (e) {
      emit('read bundle failed: ' + e);
      return null;
    }
  }

  function isTargetPage() {
    try {
      if (typeof location === 'undefined') return false;
      if (location.protocol !== 'https:' && location.protocol !== 'http:') return false;
      if (location.hostname.indexOf('douyin.com') < 0 && location.hostname.indexOf('iesdouyin.com') < 0) return false;
      // 编辑器 / 创作者后台不注入（与用户脚本的 @exclude 一致）
      if (location.hostname.indexOf('creator.douyin.com') >= 0) return false;
      return true;
    } catch (e) { return false; }
  }

  var state = readFlags();

  // 暴露给页面：画质强制器用 isQualityEnabled() 决定自己要不要干活。
  // 这样"只装脚本"模式下，画质强制器即使混在 preload 里也会安静退出。
  function installFlagsApi() {
    try {
      window.__DY_FLAGS__ = {
        scripts: !!state.scripts,
        quality: !!state.quality,
        isScriptsEnabled: function () { return !!state.scripts; },
        isQualityEnabled: function () { return !!state.quality; },
      };
    } catch (e) { }
  }

  // preload 比油猴的 document-start 更早：这时 document.documentElement 还是 null，
  // 而 domutils 的 addStyle 在 document.head 不存在时会读 documentElement.childNodes，
  // 于是脚本刚跑就抛 "Cannot read properties of null (reading 'childNodes')"。
  // 所以必须等到 <html> 出现，但又要尽量早（页面自己的脚本还没跑）。
  function domUsable() {
    try { return !!(document && document.documentElement); } catch (e) { return false; }
  }

  var MAX_WAIT_ROUNDS = 1000;   // 1000 × 4ms ≈ 4s，够 HTML 解析开头
  var WAIT_STEP_MS = 4;

  function runScripts() {
    if (!state.scripts) {
      emit('scripts disabled by inject-mode.json, skipped');
      return;
    }
    if (!isTargetPage()) return;
    if (window.__DY_US_BOOTED__ || window.__DY_US_PENDING__) return;
    window.__DY_US_PENDING__ = true;

    var src = readBundle();
    if (!src) {
      emit('no bundle at %APPDATA%\\douyin\\userscript.bundle.js, skipped');
      return;
    }

    var rounds = 0;
    var startedAt = 0;

    function attempt() {
      if (window.__DY_US_BOOTED__) return;

      if (!domUsable()) {
        rounds++;
        if (rounds > MAX_WAIT_ROUNDS) {
          emit('gave up waiting for <html>, skipped (url=' + location.href + ')');
          return;
        }
        setTimeout(attempt, WAIT_STEP_MS);
        return;
      }

      window.__DY_US_BOOTED__ = true;
      startedAt = Date.now();
      try {
        // 直接 eval：preload 与页面同上下文，脚本会挂在页面的 window 上
        (0, eval)(src);
        emit('bundle executed after ' + (rounds * WAIT_STEP_MS) + 'ms wait, ' +
          (Date.now() - startedAt) + 'ms exec, url=' + location.href);
      } catch (e) {
        emit('bundle failed: ' + (e && e.stack ? e.stack : e));
      }
    }

    attempt();
  }

  installFlagsApi();
  try { runScripts(); } catch (e) { emit('boot error: ' + e); }
})();
/* ====== LOADER-END ====== */
/* ============================================================================
 * 抖音 PC 版 —— 视频画质强制器（由 loose preload 注入 www.douyin.com 页面）
 *
 * 实测前提（已用日志验证）：
 *   - 该 preload 在 https://www.douyin.com/ 上以 isolated=false 运行，
 *     与页面同一个 JS 上下文，可直接读写页面 sessionStorage。
 *
 * 原理（来自对网页 bundle 的逆向）：
 *   播放器每次构建播放配置时读 sessionStorage 的 MANUAL_SWITCH，
 *   取其中的 gearType 作为 config.shared.definition.userSelectDefinition。
 *   键值格式（与清晰度菜单点选后写入的完全一致）：
 *     { gearType:-2, gearClarity:'20', gearName:'超清 4K', qualityType:20, done:1 }
 *   档位表（gearClarity / gearType / 显示名）：
 *     '30' / -3 / 超清4K HDR   ← 最高（仅当前 HDR 版常量表有；需视频提供 HDR 档）
 *     '20' / -2 / 超清 4K
 *     '10' / -1 / 超清 2K
 *     '5'  /  1 / 高清 1080P
 *     '4'  /  2 / 高清 720P
 *     '3'  /  3 / 标清 540P
 *     '2'  /  4 / 极速
 *     '0'  /  0 / 智能
 *
 *   页面自身会优雅降级：请求 4K 但该视频没有 4K 时，
 *   会把 gearType 20 重写成（有 1440 则 10，否则 1）。
 *   因此对不支持 4K 的视频写 4K 是安全的，不会导致播放失败。
 *
 * 策略：优先 4K（若该视频确有 HDR 4K 档则用 HDR 4K），由页面自动降到 2K / 1080P。
 * ==========================================================================*/
(function () {
  'use strict';

  var TAG = '[dy-force]';
  var VERSION = '0.4.0';
  var KEY = 'MANUAL_SWITCH';

  // 4K 档（页面对不可用档位会自动降级）
  var PICK_4K = { gearType: -2, gearClarity: '20', gearName: '超清 4K', qualityType: 20, done: 1 };
  // HDR 4K 档（仅在能确认该视频提供 HDR 档时使用，避免旧版常量表不认识 -3）
  var PICK_4K_HDR = { gearType: -3, gearClarity: '30', gearName: '超清4K HDR', qualityType: 20, done: 1 };
  // 2K 档（仅在明确知道该视频没有 4K 时使用）
  var PICK_2K = { gearType: -1, gearClarity: '10', gearName: '超清 2K', qualityType: 10, done: 1 };

  function emit(line) {
    try {
      if (window.__dyForceLog) window.__dyForceLog(TAG + ' ' + line);
      else console.log(TAG + ' ' + line);
    } catch (e) { }
  }

  function safe(v) {
    try { return typeof v === 'string' ? v.slice(0, 400) : JSON.stringify(v); } catch (e) { return String(v); }
  }

  // ---------------------------------------------------------------------------
  // 档位判定
  // ---------------------------------------------------------------------------
  function gearTypeOf(obj) {
    if (!obj) return null;
    var t = Number(obj.gearType);
    return isNaN(t) ? null : t;
  }

  // 已知的"已达最高档"集合：HDR4K(-3) 与 4K(-2) 都算
  function isTop(gt) { return gt === -2 || gt === -3; }

  // clarityReal 是该视频【实际可用】的档位 id 列表，用来预判该写几档
  function resOfGearId(id) {
    var s = String(id || '');
    if (/adapt_(lowest|lower)_hdr_4_1/.test(s)) return 2160;
    if (/adapt_(lowest|lower)_4_1/.test(s)) return 2160;
    if (/adapt_(lowest|lower)_1440_1/.test(s)) return 1440;
    var m = s.match(/_(\d{3,4})_/);
    if (m) {
      var n = parseInt(m[1], 10);
      if (n >= 240 && n <= 4320) return n;
    }
    return 0;
  }

  // 该视频是否提供 HDR 4K 档（决定能不能用 gearClarity '30'）
  function hasHdr4k(obj) {
    var arr = obj && obj.clarityReal;
    if (Object.prototype.toString.call(arr) !== '[object Array]') return false;
    for (var i = 0; i < arr.length; i++) {
      if (/adapt_(lowest|lower)_hdr_4_1/.test(String(arr[i]))) return true;
    }
    return false;
  }

  // ---------------------------------------------------------------------------
  // 与"抖音优化"脚本共存
  //
  // 用户在优化脚本里选过清晰度后，脚本会自己写 MANUAL_SWITCH；这时我们的看护
  // 不该把它再拉回 4K，否则设置面板看起来"不生效"。
  // 脚本把选择存在 localStorage 的 dy-userscript::GM_Panel -> chooseVideoDefinition，
  // -999 表示"无"（不干预），此时仍然由我们强制最高档。
  // ---------------------------------------------------------------------------
  var USER_PANEL_KEY = 'dy-userscript::GM_Panel';
  var DEFER_VALUE = -999;

  function userChosenQuality() {
    try {
      var raw = window.localStorage.getItem(USER_PANEL_KEY);
      if (!raw) return null;
      var panel = JSON.parse(raw);
      if (!panel || typeof panel !== 'object') return null;
      var v = panel.chooseVideoDefinition;
      if (v === undefined || v === null) return null;
      var n = Number(v);
      return isNaN(n) ? null : n;
    } catch (e) { return null; }
  }

  function shouldDefer() {
    var chosen = userChosenQuality();
    if (chosen === null || chosen === DEFER_VALUE) return false;
    return !isTop(chosen);
  }

  function bestAvailable(obj) {
    var top = 0;
    var arr = obj && obj.clarityReal;
    if (Object.prototype.toString.call(arr) === '[object Array]') {
      for (var i = 0; i < arr.length; i++) {
        var r = resOfGearId(arr[i]);
        if (r > top) top = r;
      }
    }
    return top;
  }

  // ---------------------------------------------------------------------------
  // 核心：确保 MANUAL_SWITCH 处于最高档
  // ---------------------------------------------------------------------------
  var lastLogged = '';
  var deferLogged = '';

  function ensureTop(where) {
    try {
      if (storageBlocked) return;
      // 用户在优化脚本里显式选了别的档位 → 尊重它，不再强制
      if (deferLogged !== where && shouldDefer()) {
        deferLogged = where;
        emit('用户已在设置里选定清晰度 ' + userChosenQuality() + '，本工具让位（@' + where + '）');
        return;
      }
      var ss = window.sessionStorage;
      if (!ss) return;

      var raw = ss.getItem(KEY);
      var obj = null;
      if (raw) { try { obj = JSON.parse(raw); } catch (e) { obj = null; } }

      // 情况一：键不存在 —— 主动创建（这是首次运行能生效的关键）
      if (!obj || typeof obj !== 'object') {
        ss.setItem(KEY, JSON.stringify(PICK_4K));
        logOnce('created', '键不存在，已创建为 4K 档 @' + where);
        return;
      }

      var gt = gearTypeOf(obj);
      if (isTop(gt)) {
        // 正常状态不刷日志，避免日志爆炸
        return;
      }

      // 情况二：该视频明显没有 4K（clarityReal 有内容且最高只到 1440/1080）
      var avail = bestAvailable(obj);
      var pick = hasHdr4k(obj) ? PICK_4K_HDR : PICK_4K;
      if (avail && avail < 2160) {
        if (avail >= 1440) pick = PICK_2K;
        else {
          logOnce('skip', '该视频最高仅 ' + avail + 'p，保持页面自身行为（gearType=' + gt + '）@' + where);
          return;
        }
      }

      // 情况三：是"智能"/较低档 —— 拉高
      obj.gearType = pick.gearType;
      obj.gearClarity = pick.gearClarity;
      obj.gearName = pick.gearName;
      obj.qualityType = pick.qualityType;
      obj.done = 1;
      ss.setItem(KEY, JSON.stringify(obj));
      logOnce('forced', '拉高到 ' + pick.gearName + ' (gearType=' + pick.gearType + ')，原 gearType=' + gt
        + ' avail=' + avail + ' @' + where);
    } catch (e) {
      handleError(e, where);
    }
  }

  // 沙箱帧 / about:blank 上访问 sessionStorage 会抛 SecurityError，
  // 这类文档永久跳过，避免每秒刷屏。
  var storageBlocked = false;
  var errLogged = '';

  function handleError(e, where) {
    var msg = String(e);
    if (msg.indexOf('SecurityError') >= 0 || msg.indexOf('Access is denied') >= 0) {
      storageBlocked = true;
      // 每个文档只报一次
      if (errLogged !== 'blocked') {
        errLogged = 'blocked';
        emit('该文档不允许访问 sessionStorage，已跳过（url=' + safe(location.href) + '）');
      }
      return;
    }
    if (errLogged !== msg) {
      errLogged = msg;
      emit('ensureTop 异常 @' + where + ': ' + msg);
    }
  }

  // 同样的状态不重复刷日志，避免日志爆炸
  function logOnce(tag, msg) {
    if (lastLogged === tag + msg) return;
    lastLogged = tag + msg;
    emit('[' + tag + '] ' + msg);
  }

  // ---------------------------------------------------------------------------
  // 启动：preload 执行时尽早写入，之后持续看护（页面可能把智能重置回去）
  // ---------------------------------------------------------------------------
  function boot() {
    emit('injector v' + VERSION + ' 启动 readyState=' + document.readyState
      + ' url=' + location.href);

    // 立刻写一次（越早越好：播放器构建配置前就位）
    ensureTop('boot');

    // 页面后续可能重置成"智能"，前 3 分钟每秒看护
    var n = 0;
    var timer = setInterval(function () {
      n++;
      try { ensureTop('tick' + n); } catch (e) { }
      if (n >= 180) clearInterval(timer);
    }, 1000);

    // 页面切换视频/从后台恢复时也复查
    try {
      document.addEventListener('visibilitychange', function () { ensureTop('visibilitychange'); });
      window.addEventListener('focus', function () { ensureTop('focus'); });
    } catch (e) { }
  }

  // "只装抖音优化脚本"模式：这里安静退出。
  //
  // 优先用装载器放在 window 上的开关；没有（老 preload）就直接读
  // %APPDATA%\douyin\inject-mode.json —— payload 这段代码就在 preload 的包装函数里，
  // require 在作用域内，所以即使装载器不参与也能读到开关。
  function readQualityFlagFromDisk() {
    try {
      if (typeof require !== 'function') return null;
      var fs2 = require('fs');
      var path2 = require('path');
      var base = (typeof process !== 'undefined' && process.env && process.env.APPDATA) || '';
      if (!base) return null;
      var file = path2.join(base, 'douyin', 'inject-mode.json');
      if (!fs2.existsSync(file)) return null;
      var obj = JSON.parse(fs2.readFileSync(file, 'utf8'));
      if (obj && typeof obj.quality === 'boolean') return obj.quality;
    } catch (e) { }
    return null;
  }

  function qualityEnabled() {
    try {
      var f = window.__DY_FLAGS__;
      if (f && typeof f.isQualityEnabled === 'function') return f.isQualityEnabled();
    } catch (e) { }
    var fromDisk = readQualityFlagFromDisk();
    if (fromDisk !== null) return fromDisk;
    return true;   // 完全没有配置（老安装/单独使用）时保持原行为
  }

  try {
    if (!qualityEnabled()) {
      emit('quality forcing disabled by inject-mode, skipped');
    } else if (document.readyState === 'loading') {
      // preload 早于 DOMContentLoaded：先立即跑一次，DOM 就绪后再跑
      ensureTop('early');
      document.addEventListener('DOMContentLoaded', boot, { once: true });
    } else {
      boot();
    }
  } catch (e) {
    emit('启动失败: ' + e);
  }

  // 供人工/诊断调用
  try {
    window.__dyForceInfo = { version: VERSION, key: KEY, pick: PICK_4K };
    window.__dyForceNow = function () { ensureTop('manual'); return window.sessionStorage.getItem(KEY); };
  } catch (e) { }
})();
