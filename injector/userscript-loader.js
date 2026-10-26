/* ============================================================================
 * 抖音优化 · preload 装载器
 *
 * 由 patch-preload.js 追加进抖音的 loose preload（isolated=false）。
 *
 * 脚本来源（两者取新，外部优先）：
 *   1. 外部：%APPDATA%\douyin\userscript.bundle.js —— 可随时替换，无需提权
 *   2. 内置：本文件前面的 __DY_EMBEDDED_BUNDLE__（gzip+base64，由构建脚本生成）
 *
 *   装载器比较两者的 __DY_BUNDLE_VERSION__：
 *     - 外部不存在 / 读不出 / 版本更旧  -> 用内置
 *     - 外部版本较新                    -> 用外部（这就是"外部升级"能力）
 *   所以开箱即用，同时保留以后换脚本的可能。
 *
 * 为什么外部放 %APPDATA% 而不是安装目录：
 *   安装目录在 C:\Program Files，写它需要管理员权限；放在用户目录换版本不用再提权。
 *   页面里没有 require，所以先在 preload 侧把代码读成字符串，再交给页面 eval。
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

  // ---------------------------------------------------------------------------
  // 内置脚本：userscript.bundle.embedded.js 把它挂到 window 上
  // （gzip + base64，构建时生成，约 0.5MB）
  // ---------------------------------------------------------------------------
  function embeddedRaw() {
    try {
      var e = window.__DY_EMBEDDED_BUNDLE__;
      if (e && typeof e.b64 === 'string' && e.b64.length > 1000) return e;
    } catch (e) { }
    return null;
  }

  function embeddedVersion() {
    var e = embeddedRaw();
    return e && typeof e.version === 'number' ? e.version : 0;
  }

  function gunzipBase64(b64) {
    try {
      var zlib = nodeRequire('zlib');
      var buf = Buffer.from(b64, 'base64');
      return zlib.gunzipSync(buf).toString('utf8');
    } catch (e) {
      emit('gunzip embedded bundle failed: ' + e);
      return null;
    }
  }

  // 从外部文件里读版本号（bundle 第一行附近有 __DY_BUNDLE_VERSION__）
  function versionOf(src) {
    if (typeof src !== 'string') return 0;
    var m = src.match(/__DY_BUNDLE_VERSION__\s*=\s*(\d+)/);
    return m ? parseInt(m[1], 10) : 0;
  }

  function readExternalBundle() {
    if (!nodeRequire) return null;
    try {
      var fs = nodeRequire('fs');
      var path = nodeRequire('path');
      var dir = appDataDir();
      if (!dir) return null;
      var file = path.join(dir, 'userscript.bundle.js');
      if (!fs.existsSync(file)) return null;
      if (fs.statSync(file).size < 1000) return null;
      return fs.readFileSync(file, 'utf8');
    } catch (e) {
      emit('read external bundle failed: ' + e);
      return null;
    }
  }

  // 取新者：外部版本 > 内置版本 才用外部，否则用内置。
  // 两者都没有才返回 null（此时跳过，不影响页面）。
  function pickBundle() {
    var ext = readExternalBundle();
    var extVer = ext ? versionOf(ext) : 0;
    var embVer = embeddedVersion();

    if (ext && extVer > embVer) {
      emit('using external bundle (v' + extVer + ' > embedded v' + embVer + ')');
      return { src: ext, source: 'external', version: extVer };
    }

    var emb = embeddedRaw();
    if (emb) {
      var src = gunzipBase64(emb.b64);
      if (src) {
        var note = ext
          ? ('external v' + extVer + ' not newer than embedded v' + embVer + ', using embedded')
          : ('no external bundle, using embedded v' + embVer);
        emit(note + (emb.scriptName ? ' [' + emb.scriptName + ' ' + emb.scriptVersion + ']' : ''));
        return { src: src, source: 'embedded', version: embVer };
      }
    }

    if (ext) {
      emit('embedded unavailable, falling back to external v' + extVer);
      return { src: ext, source: 'external', version: extVer };
    }

    return null;
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

    var picked = pickBundle();
    if (!picked || !picked.src) {
      emit('no bundle available (embedded + external both missing), skipped');
      return;
    }
    var src = picked.src;
    var sourceTag = picked.source + ' v' + picked.version;

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
        emit('bundle executed [' + sourceTag + '] after ' + (rounds * WAIT_STEP_MS) + 'ms wait, ' +
          (Date.now() - startedAt) + 'ms exec, url=' + location.href);
      } catch (e) {
        emit('bundle failed [' + sourceTag + ']: ' + (e && e.stack ? e.stack : e));
      }
    }

    attempt();
  }

  installFlagsApi();
  try { runScripts(); } catch (e) { emit('boot error: ' + e); }
})();
/* ====== LOADER-END ====== */
