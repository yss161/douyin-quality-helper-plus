#!/usr/bin/env node
/**
 * build-userscript.js -- 把"抖音优化"用户脚本打包成可注入的 bundle
 *
 * 用户脚本原本是给油猴的：依赖 UMD 库 + GM_* API + 油猴菜单。
 * 客户端里没有油猴，所以按顺序拼成一段：
 *
 *   dy-host.js          提供 GM_* 兼容层 + 左侧栏设置入口
 *   vendor/utils        @whitesev/utils
 *   vendor/domutils     @whitesev/domutils
 *   vendor/pops         @whitesev/pops
 *   vendor/qmsg         qmsg
 *   douyin-optimize.js  原版用户脚本（一行不改）
 *
 * 关键：脚本本体被 SCRIPT-BEGIN / SCRIPT-END 两个标记包围，
 * 所以客户端里"上传脚本更新"只要替换这一段，不用重新打包依赖
 * （见 exe-src/UserscriptBundler.cs，两边标记必须一致）。
 *
 * 用法：
 *   node tools/build-userscript.js
 *   node tools/build-userscript.js --src <用户脚本.js> --out <bundle.js>
 */
'use strict';

const fs = require('fs');
const path = require('path');

const SCRIPT_BEGIN = '/* ====== SCRIPT-BEGIN ====== */';
const SCRIPT_END = '/* ====== SCRIPT-END ====== */';

function parseArgs(argv) {
  const out = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) out[a.slice(2)] = argv[++i];
  }
  return out;
}

/**
 * 读 UserScript 头部里的 @name / @version / @description / @match 等。
 * 头部是 --- ==UserScript== ... ==/UserScript== --- 之间的注释块。
 */
function parseUserScriptHeader(src) {
  const meta = { name: '', version: '', description: '', author: '', namespace: '', match: [], exclude: [], require: [] };
  // 头部里可能有超长的 @icon base64，所以窗口给到 64KB 再找结束标记
  const closeAt = src.indexOf('==/UserScript==');
  const headEnd = closeAt >= 0 ? Math.min(src.length, closeAt + 32) : Math.min(src.length, 65536);
  const head = src.slice(0, headEnd);
  const block = head.match(/==UserScript==[\s\S]*?==\/UserScript==/);
  if (!block) return meta;
  block[0].split(/\r?\n/).forEach((line) => {
    const m = line.match(/^\s*\/\/\s*@(\w+)\s+(.*)$/);
    if (!m) return;
    const key = m[1];
    const val = m[2].trim();
    if (key === 'match' || key === 'exclude' || key === 'require' || key === 'grant' || key === 'connect') {
      if (Array.isArray(meta[key])) meta[key].push(val);
    } else if (key === 'name' || key === 'version' || key === 'description' || key === 'author' || key === 'namespace') {
      meta[key] = val;
    }
  });
  return meta;
}

const args = parseArgs(process.argv.slice(2));
const root = path.resolve(__dirname, '..');
const injector = path.join(root, 'injector');

const srcScript = args.src || path.join(injector, 'vendor', 'douyin-optimize.js');
const outFile = args.out || path.join(injector, 'userscript.bundle.js');

const parts = [
  { name: 'dy-host', file: path.join(injector, 'dy-host.js') },
  { name: 'utils', file: path.join(injector, 'vendor', 'utils.umd.js') },
  { name: 'domutils', file: path.join(injector, 'vendor', 'domutils.umd.js') },
  { name: 'pops', file: path.join(injector, 'vendor', 'pops.umd.js') },
  { name: 'qmsg', file: path.join(injector, 'vendor', 'qmsg.umd.js') },
  // 注：礼物栏精简已移除（选择器反复随抖音改版失效，成本过高）
  // 直播间复制链接优化：上游只处理视频页，直播页没有对应实现
  { name: 'live-share-clean', file: path.join(injector, 'live-share-clean.js') },
];

const missing = parts.filter((p) => !fs.existsSync(p.file));
if (missing.length) {
  console.error('[x] missing part(s): ' + missing.map((p) => p.file).join(', '));
  console.error('    run tools/fetch-vendor.ps1 first');
  process.exit(1);
}
if (!fs.existsSync(srcScript)) {
  console.error('[x] script not found: ' + srcScript);
  process.exit(1);
}

const scriptSrc = fs.readFileSync(srcScript, 'utf8');
const meta = parseUserScriptHeader(scriptSrc);
if (!meta.name) {
  console.error('[x] not a userscript (no @name in the ==UserScript== header): ' + srcScript);
  process.exit(1);
}

// 构建版本号：装载器用它判断"外部文件 vs 内置脚本"哪份更新。
// 取当前时间（UTC，秒级），单调递增，不需要人工维护。
const builtAt = new Date().toISOString().replace(/[-:T.Z]/g, '').slice(0, 14);   // yyyyMMddHHmmss
const bundleVersion = Number(builtAt);

const header = [
  '/* ===========================================================================',
  ' * 抖音优化 · 客户端 bundle（自动生成，不要手改）',
  ' *',
  ' * 内容：GM 兼容层 + 左侧栏设置入口 + UMD 依赖 + 原版用户脚本',
  ' * 重新生成：node tools/build-userscript.js',
  ' *',
  ' * 版本：' + builtAt + '（装载器据此判断内外部脚本谁更新）',
  ' * =========================================================================*/',
].join('\n');

let out = header + '\n';
out += ';(function () {\n';
out += '  var __dyBundleStart = Date.now();\n';
out += '  try { window.__DY_BUNDLE_VERSION__ = ' + bundleVersion + '; } catch (e) {}\n';

for (const p of parts) {
  const code = fs.readFileSync(p.file, 'utf8');
  out += '\n/* ==== ' + p.name + ' ==== */\n';
  out += code;
  if (!code.endsWith('\n')) out += '\n';
}

// 脚本本体：两端带标记，客户端换脚本时只替换这一段。
//
// 外面包一层 __DY_DEFER_SCRIPT__：Electron preload 比油猴的 document-start 更早，
// 那时 <html> 还不存在，domutils.addStyle 会抛
// "Cannot read properties of null (reading 'childNodes')"。
// dy-host 里的 deferScript 会等 <html> 出现再执行，语义与油猴一致。
out += '\n' + SCRIPT_BEGIN + '\n';
out += 'window.__DY_SCRIPT_META__ = ' + JSON.stringify(meta) + ';\n';
out += '(function () {\n';
out += '  var __run = function () {\n';
out += '    try {\n';
out += scriptSrc.split('\n').map(function (line) { return line.length ? '      ' + line : line; }).join('\n');
out += '\n';
out += '    } catch (e) {\n';
out += '      try { console.error("[dy-userscript] script error", e); } catch (e2) {}\n';
out += '    }\n';
out += '  };\n';
out += '  if (typeof window.__DY_DEFER_SCRIPT__ === "function") window.__DY_DEFER_SCRIPT__(__run);\n';
out += '  else __run();\n';
out += '})();\n';
out += SCRIPT_END + '\n';

out += '\n  try {\n';
out += '    if (window.__dyForceLog) window.__dyForceLog("[dy-userscript] bundle loaded in " + (Date.now() - __dyBundleStart) + "ms");\n';
out += '  } catch (e) {}\n';
out += '})();\n';

fs.writeFileSync(outFile, out, 'utf8');
console.log('[ok] ' + outFile);
console.log('[i] ' + Buffer.byteLength(out) + ' bytes from ' + (parts.length + 1) + ' parts');
console.log('[i] script: ' + meta.name + ' v' + meta.version);
console.log('[i] bundle version: ' + builtAt);

// ---------------------------------------------------------------------------
// 顺带生成装载器要内嵌的压缩副本（gzip + base64，约 0.5MB）。
// 装载器以这段为"内置脚本"，外部 %APPDATA%\douyin\userscript.bundle.js
// 存在且版本更新时才覆盖它 —— 开箱即用，同时保留外部升级。
// ---------------------------------------------------------------------------
if (!args["no-embed"]) {
  const zlib = require('zlib');
  const gz = zlib.gzipSync(Buffer.from(out, 'utf8'), { level: 9 });
  const b64 = gz.toString('base64');
  const chunks = [];
  for (let i = 0; i < b64.length; i += 200) chunks.push("'" + b64.slice(i, i + 200) + "'");
  const embedFile = path.join(injector, 'userscript.bundle.embedded.js');
  const embedSrc = [
    '/* ===========================================================================',
    ' * 抖音优化 · 内置脚本副本（自动生成，不要手改）',
    ' *',
    ' * 由 tools/build-userscript.js 生成：',
    ' *   injector/userscript.bundle.js -> gzip -> base64',
    ' * 装载器（userscript-loader.js）把它注入前导，作为"内置脚本"兜底。',
    ' *',
    ' * bundle 版本：' + builtAt,
    ' * 打包体积：' + Buffer.byteLength(out) + ' 字节，压缩后 ' + gz.length + ' 字节',
    ' * =========================================================================*/',
    'window.__DY_EMBEDDED_BUNDLE__ = {',
    '  version: ' + bundleVersion + ',',
    '  builtAt: ' + JSON.stringify(builtAt) + ',',
    '  scriptName: ' + JSON.stringify(meta.name) + ',',
    '  scriptVersion: ' + JSON.stringify(meta.version) + ',',
    '  bytes: ' + Buffer.byteLength(out) + ',',
    '  gzipBytes: ' + gz.length + ',',
    '  b64:',
    '    ' + chunks.join(' +\n    ') + '',
    '};',
  ].join('\n') + '\n';

  fs.writeFileSync(embedFile, embedSrc, 'utf8');
  console.log('[i] embedded: ' + embedFile + ' (' + Buffer.byteLength(embedSrc) + ' bytes, gzip ' + gz.length + ')');
}