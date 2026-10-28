/* ============================================================================
 * 抖音直播 —— 礼物栏精简（只保留「更多」二级菜单 + 钻石）
 *
 * 效果：底部礼物栏里的每个礼物图标全部隐去，只留下「更多」和钻石。
 *
 * 【三层定位策略，逐级降级】
 *
 *   A. 文本层：找带价格文本的礼物项（"99钻" / "1 钻" 这类），
 *      再按父级聚合计票，子项最多的父级就是礼物列表。
 *
 *   B. 几何层（主用）：礼物栏固定在视口底部，且是一排等宽小方块。
 *      沿底部横向采样 elementsFromPoint，找到"子项数量 >= 6 且尺寸相近"的容器，
 *      直接拿它当礼物栏。这一层不依赖任何文本或 class，最稳。
 *
 *   C. iframe 层：客户端可能把直播内容放在 iframe 里，
 *      对每个同源 iframe 递归重复 A/B。
 *
 *   隐藏时只动礼物列表自己的子项，兄弟节点（更多 / 钻石 / 游戏）不碰。
 *
 * 【为什么不用 class 名】
 *   抖音的 class 是构建时哈希的（xAG9cqUC / saw7uCUW …），每次发版都会变。
 *
 * 临时恢复：window.__DY_LIVE_GIFT_HIDE__ = false; 刷新即可。
 * 诊断：控制台执行 window.__DY_LIVE_GIFT_DUMP__()
 * ==========================================================================*/
(function () {
  'use strict';

  if (window.__DY_LIVE_GIFT_HIDE_INSTALLED__) return;
  window.__DY_LIVE_GIFT_HIDE_INSTALLED__ = true;

  var TAG = '[dy-live-gift]';
  var STYLE_ID = 'dy-live-gift-hide-style';
  var HIDDEN_ATTR = 'data-dy-gift-hidden';
  var MAX_TEXT = 30;

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

  function textOf(el) {
    try { return (el.textContent || '').replace(/\s+/g, ''); } catch (e) { return ''; }
  }

  // 价格：数字后面跟「钻」。注意不能只判"含钻"——
  // 顶栏有「充钻石」三个字，会把整个页面外壳误判成礼物。
  var PRICE_RE = /\d+\s*钻/;

  function hasPrice(el) {
    var t = textOf(el);
    if (!t || t.length > MAX_TEXT) return false;
    if (!PRICE_RE.test(t)) return false;
    var m = t.match(/\d+\s*钻/g);
    if (m && m.length > 1) return false;   // 多个价格 = 大容器，不是单个礼物
    return true;
  }

  // ---------------------------------------------------------------------
  // 捕获 shadow root（含 closed）
  //
  // 为什么必须这么做：
  //   pace-island 这类 Web Components 可能用 mode:'closed' 创建 shadow root，
  //   此时 el.shadowRoot 返回 null，document.querySelectorAll 也穿不进去。
  //   结果是：礼物栏明明在页面上，脚本却一个节点都扫不到。
  //
  //   唯一可靠的办法是在 attachShadow 被调用时抢下引用 —— 无论 open 还是 closed
  //   都能拿到。脚本在 preload 阶段执行，早于页面渲染，来得及。
  // ---------------------------------------------------------------------
  var shadowRoots = [];

  (function hookAttachShadow() {
    try {
      var proto = Element.prototype;
      if (!proto || proto.__dyShadowHooked) return;

      var orig = proto.attachShadow;
      if (typeof orig !== 'function') return;

      proto.attachShadow = function (init) {
        var sr = orig.apply(this, arguments);
        try {
          if (sr) {
            var dup = false;
            for (var i = 0; i < shadowRoots.length; i++) if (shadowRoots[i] === sr) { dup = true; break; }
            if (!dup) shadowRoots.push(sr);
          }
        } catch (e) { }
        return sr;
      };

      proto.__dyShadowHooked = true;
    } catch (e) { }
  })();

  // 更早的 shadow root（在我 hook 之前就建好的）只能靠 open 的 .shadowRoot 拿
  function seedOpenShadowRoots() {
    try {
      var all = document.querySelectorAll('*');
      for (var i = 0; i < all.length; i++) {
        var sr = null;
        try { sr = all[i].shadowRoot; } catch (e) { }
        if (!sr) continue;
        var dup = false;
        for (var j = 0; j < shadowRoots.length; j++) if (shadowRoots[j] === sr) { dup = true; break; }
        if (!dup) shadowRoots.push(sr);
      }
    } catch (e) { }
  }

  // ---------------------------------------------------------------------
  // 收集所有根：document + 同源 iframe + shadow root
  // ---------------------------------------------------------------------
  function collectRoots() {
    var roots = [];
    var budget = 8000;

    function seen(root) {
      for (var i = 0; i < roots.length; i++) if (roots[i] === root) return true;
      return false;
    }

    function walk(root, win, depth) {
      if (!root || depth > 5 || budget <= 0 || seen(root)) return;
      roots.push(root);

      // 先收 shadow root
      var all;
      try { all = root.querySelectorAll('*'); } catch (e) { all = null; }
      if (all) {
        for (var i = 0; i < all.length; i++) {
          if (--budget <= 0) return;
          var sr = null;
          try { sr = all[i].shadowRoot; } catch (e) { }
          if (sr) walk(sr, null, depth + 1);
        }
      }

      // 再收 iframe（同源才可访问）
      var frames;
      try { frames = root.querySelectorAll('iframe, frame'); } catch (e) { frames = null; }
      if (!frames) return;

      for (var j = 0; j < frames.length; j++) {
        var idoc = null, iwin = null;
        try {
          idoc = frames[j].contentDocument;
          iwin = frames[j].contentWindow;
        } catch (e) { continue; }
        if (idoc) walk(idoc, iwin, depth + 1);
      }
    }

    walk(document, window, 0);

    // 把 hook 捕获到的 shadow root 全部纳入
    seedOpenShadowRoots();
    for (var s = 0; s < shadowRoots.length; s++) {
      if (!seen(shadowRoots[s])) {
        try { walk(shadowRoots[s], null, 1); } catch (e) { }
      }
    }

    return roots;
  }

  // root 对应的 window（拿 innerWidth/innerHeight 用）
  function winOf(root) {
    try {
      if (root === document) return window;
      if (root.defaultView) return root.defaultView;
      var ow = root.ownerDocument;
      if (ow && ow.defaultView) return ow.defaultView;
    } catch (e) { }
    return window;
  }

  // ---------------------------------------------------------------------
  // A. 文本层
  // ---------------------------------------------------------------------
  function findByText(root) {
    var all;
    try { all = root.querySelectorAll('*'); } catch (e) { return null; }
    if (!all) return null;

    var counts = [];
    var parents = [];

    function idxOf(p) {
      for (var i = 0; i < parents.length; i++) if (parents[i] === p) return i;
      parents.push(p);
      counts.push({ parent: p, count: 0 });
      return counts.length - 1;
    }

    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      if (!hasPrice(el)) continue;
      var p = el.parentElement;
      if (!p) continue;
      counts[idxOf(p)].count++;
    }

    var best = null, bestCount = 0;
    for (var b = 0; b < counts.length; b++) {
      if (counts[b].count > bestCount) { bestCount = counts[b].count; best = counts[b].parent; }
    }

    return bestCount >= 2 ? best : null;
  }

  // ---------------------------------------------------------------------
  // B. 几何层：沿视口底部采样，找"一排等宽小方块"的容器
  // ---------------------------------------------------------------------
  function isSimilarRow(container) {
    var kids = container.children;
    if (!kids || kids.length < 6) return false;

    var rects = [];
    for (var i = 0; i < kids.length; i++) {
      var r;
      try { r = kids[i].getBoundingClientRect(); } catch (e) { continue; }
      if (r.width < 20 || r.height < 20) continue;
      rects.push(r);
    }
    if (rects.length < 6) return false;

    // 同一行：top 相近
    var top = rects[0].top;
    for (var j = 1; j < rects.length; j++) {
      if (Math.abs(rects[j].top - top) > 12) return false;
    }

    // 宽度相近（礼物方块等宽）
    var minW = Infinity, maxW = 0;
    for (var k = 0; k < rects.length; k++) {
      if (rects[k].width < minW) minW = rects[k].width;
      if (rects[k].width > maxW) maxW = rects[k].width;
    }
    if (maxW / minW > 1.8) return false;

    return true;
  }

  function findByGeometry(root) {
    var win = winOf(root);
    var w = 0, h = 0;
    try { w = win.innerWidth || 0; h = win.innerHeight || 0; } catch (e) { return null; }
    if (!w || !h) return null;

    // 礼物栏贴在底部：从 h-25 到 h-90 之间采样
    var hits = [];
    var seenNodes = [];

    for (var y = h - 25; y > h - 100; y -= 12) {
      for (var x = 40; x < w - 280; x += 30) {
        var els;
        try { els = root.elementsFromPoint ? root.elementsFromPoint(x, y) : null; } catch (e) { els = null; }
        if (!els) continue;
        for (var i = 0; i < els.length && i < 8; i++) {
          var el = els[i];
          var dup = false;
          for (var s = 0; s < seenNodes.length; s++) if (seenNodes[s] === el) { dup = true; break; }
          if (!dup) { seenNodes.push(el); hits.push(el); }
        }
      }
    }

    // 在这些命中元素里，找子项数量最多的"一排等宽方块"
    var best = null, bestCount = 0;
    for (var k = 0; k < hits.length; k++) {
      var cand = hits[k];
      if (!isSimilarRow(cand)) continue;
      var n = cand.children.length;
      if (n > bestCount) { bestCount = n; best = cand; }
    }

    return best;
  }

  // ---------------------------------------------------------------------
  // C. 上游选择器层
  // 上游「抖音优化」脚本一直在跟进改版，它的 :has() 选择器最贴近真实结构：
  //   div[data-e2e="living-container"] >div> div:has(>pace-island>.gitBarOptimizeEnabled)
  // 这里先找出 .gitBarOptimizeEnabled 附近的容器，再定位到礼物列表。
  // ---------------------------------------------------------------------
  function findByUpstream(root) {
    // 找上游标记：.gitBarOptimizeEnabled 是"礼物栏整体"的标记，
    // 它内部可能再套一层真正的礼物列表，所以不能直接拿它当 list。
    var bar = null;
    try { bar = root.querySelector('.gitBarOptimizeEnabled'); } catch (e) { }
    if (!bar) {
      try { bar = document.querySelector('.gitBarOptimizeEnabled'); } catch (e) { }
    }

    // 在 bar 里找"子项里带价格的个数最多"的那一层，就是真正的礼物列表
    function deepestList(scope) {
      var all;
      try { all = scope.querySelectorAll('*'); } catch (e) { return null; }

      var best = null, bestCount = 0;
      var candidates = [scope];
      if (all) for (var i = 0; i < all.length && i < 300; i++) candidates.push(all[i]);

      for (var c = 0; c < candidates.length; c++) {
        var node = candidates[c];
        var kids = node.children;
        if (!kids || kids.length < 2) continue;

        var cnt = 0;
        for (var k = 0; k < kids.length; k++) {
          if (hasPrice(kids[k])) cnt++;
        }
        if (cnt > bestCount) { bestCount = cnt; best = node; }
      }
      return bestCount >= 2 ? best : null;
    }

    if (bar) {
      var inner = deepestList(bar);
      if (inner) return inner;
      // bar 里没有带价格的子项（价格文案可能变了），
      // 就退回"一排等宽方块"的判定
      if (bar.children && bar.children.length >= 6 && isSimilarRow(bar)) return bar;
      return null;
    }

    // 没有上游标记时，看 [data-e2e="gifts-container"]
    try {
      var gc = root.querySelector('[data-e2e="gifts-container"]');
      if (gc) {
        var g = deepestList(gc);
        if (g) return g;
        if (gc.children && gc.children.length >= 6 && isSimilarRow(gc)) return gc;
      }
    } catch (e) { }

    // pace-island 的 open shadow root
    try {
      var pis = root.querySelectorAll('pace-island');
      for (var i = 0; i < pis.length; i++) {
        var sr = pis[i].shadowRoot;
        if (!sr) continue;
        var b2 = sr.querySelector('.gitBarOptimizeEnabled');
        if (b2) {
          var l2 = deepestList(b2);
          if (l2) return l2;
          if (b2.children && b2.children.length >= 6 && isSimilarRow(b2)) return b2;
        }
      }
    } catch (e) { }

    return null;
  }

  // ---------------------------------------------------------------------
  // 隐藏：只动 list 自己的子项，兄弟节点不碰
  // ---------------------------------------------------------------------
  function hideIn(list, trusted) {
    if (!list) return 0;

    var hidden = 0;
    var kids = list.children;

    for (var i = 0; i < kids.length; i++) {
      var child = kids[i];
      if (child.getAttribute(HIDDEN_ATTR) === '1') continue;

      var t = textOf(child);

      // 这几个入口一定要保留
      if (t.length <= 6 && t.indexOf('更多') >= 0) continue;
      if (t.length <= 6 && (t.indexOf('游戏') >= 0 || t.indexOf('商城') >= 0)) continue;
      try {
        if (child.classList && child.classList.contains('giftRechargeContaner')) continue;
        if (child.querySelector && child.querySelector('.giftRechargeContaner')) continue;
      } catch (e) { }

      if (trusted) {
        // 几何层确认过：这是一排等宽礼物方块，剩下的都该藏
        try { child.setAttribute(HIDDEN_ATTR, '1'); hidden++; } catch (e) { }
        continue;
      }

      // 文本层：必须能看出是礼物（带价格）
      if (!PRICE_RE.test(t)) continue;
      try { child.setAttribute(HIDDEN_ATTR, '1'); hidden++; } catch (e) { }
    }

    return hidden;
  }

  // 礼物原本是一整条：藏掉前面后最左边那块会变成"左直角+右圆角"，补圆角
  function fixFirstCorner(host) {
    var all;
    try { all = host.querySelectorAll('*'); } catch (e) { return; }

    var target = null, targetX = Infinity;
    for (var i = 0; i < all.length; i++) {
      var el = all[i];
      var rect;
      try { rect = el.getBoundingClientRect(); } catch (e) { continue; }
      if (rect.width < 20 || rect.height < 20) continue;
      if (el.getAttribute(HIDDEN_ATTR) === '1') continue;

      var cs;
      try { cs = getComputedStyle(el); } catch (e) { continue; }
      if (cs.display === 'none' || cs.visibility === 'hidden') continue;

      var tl = parseFloat(cs.borderTopLeftRadius) || 0;
      var tr = parseFloat(cs.borderTopRightRadius) || 0;
      var bl = parseFloat(cs.borderBottomLeftRadius) || 0;
      var br = parseFloat(cs.borderBottomRightRadius) || 0;

      if (tr <= 0 || tl > 0) continue;
      if (rect.left < targetX) { targetX = rect.left; target = { el: el, tr: tr, br: br, bl: bl }; }
    }

    if (!target) return;
    try {
      target.el.style.borderTopLeftRadius = target.tr + 'px';
      if (target.br > 0 && target.bl === 0) target.el.style.borderBottomLeftRadius = target.br + 'px';
    } catch (e) { }
  }

  var lastStats = { roots: 0, byText: 0, byGeom: 0, hidden: 0 };

  function trimBar() {
    if (!enabled()) { restore(); return 0; }

    var roots = collectRoots();
    var totalHidden = 0;
    var st = { roots: roots.length, byUp: 0, byText: 0, byGeom: 0, hidden: 0 };
    var done = [];   // 已处理的 list，避免重复

    function markDone(list) {
      for (var i = 0; i < done.length; i++) if (done[i] === list) return true;
      done.push(list);
      return false;
    }

    for (var r = 0; r < roots.length; r++) {
      var root = roots[r];
      var list = null;

      // C. 上游选择器层（最贴近真实结构，优先）
      list = findByUpstream(root);
      if (list && !markDone(list)) {
        var byUp = hideIn(list, true);
        if (byUp > 0) { totalHidden += byUp; st.byUp = (st.byUp || 0) + byUp; }
      }

      // A. 文本层
      list = findByText(root);
      if (list && !markDone(list)) {
        var byText = hideIn(list, false);
        if (byText > 0) { totalHidden += byText; st.byText += byText; }
      }

      // B. 几何层（文本层没结果时用，或补充几何层找到的）
      list = findByGeometry(root);
      if (list && !markDone(list)) {
        var byGeom = hideIn(list, true);
        if (byGeom > 0) { totalHidden += byGeom; st.byGeom += byGeom; }
      }
    }

    // 圆角修正
    if (totalHidden > 0) {
      for (var d = 0; d < done.length; d++) {
        try { fixFirstCorner(done[d].parentElement || done[d]); } catch (e) { }
      }
    }

    st.hidden = totalHidden;
    lastStats = st;
    if (totalHidden > 0) log('已隐藏 ' + totalHidden + ' 个礼物图标（保留「更多」与钻石）');
    return totalHidden;
  }

  // ---------------------------------------------------------------------
  // 诊断
  // ---------------------------------------------------------------------
  function dump(limit) {
    var out = [];
    out.push('roots=' + collectRoots().length);

    // pace-island 检查（上游脚本用的容器）
    try {
      var pis = document.querySelectorAll('pace-island');
      out.push('pace-island=' + pis.length);
      for (var i = 0; i < pis.length && i < 4; i++) {
        var e = pis[i];
        out.push('  [' + i + '] shadowRoot=' + (e.shadowRoot ? 'OPEN' : 'null') +
                 ' textLen=' + textOf(e).length +
                 ' tag=' + e.tagName);
      }
    } catch (x) { }

    // .gitBarOptimizeEnabled 是否存在（要遍历所有 roots，shadow 里 document 查不到）
    try {
      var barCount = 0;
      var allRoots = collectRoots();
      for (var br = 0; br < allRoots.length; br++) {
        try { barCount += allRoots[br].querySelectorAll('.gitBarOptimizeEnabled').length; } catch (x) { }
      }
      out.push('gitBarOptimizeEnabled=' + barCount);
    } catch (x) { }

    // 所有含「钻」的短文本
    var roots = collectRoots();
    var hits = [];
    for (var r = 0; r < roots.length; r++) {
      var all;
      try { all = roots[r].querySelectorAll('*'); } catch (e) { continue; }
      if (!all) continue;
      for (var j = 0; j < all.length && hits.length < 200; j++) {
        var t = textOf(all[j]);
        if (t.indexOf('钻') < 0) continue;
        if (t.length > 40) continue;
        hits.push(t.length + ':"' + t.slice(0, 20) + '"');
      }
    }
    hits.sort(function (a, b) { return parseInt(a, 10) - parseInt(b, 10); });
    out.push('含钻=' + hits.length + ' 最短:' + hits.slice(0, limit || 10).join(' | '));

    var msg = out.join('\n');
    log('诊断\n' + msg);
    return msg;
  }

  try {
    window.__DY_LIVE_GIFT_DUMP__ = function () { return dump(20); };
  } catch (e) { }

  function restore() {
    var roots = collectRoots();
    for (var r = 0; r < roots.length; r++) {
      var marked;
      try { marked = roots[r].querySelectorAll('[' + HIDDEN_ATTR + ']'); } catch (e) { continue; }
      if (!marked) continue;
      for (var i = 0; i < marked.length; i++) marked[i].removeAttribute(HIDDEN_ATTR);
    }
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

    log('已启动（首轮隐藏 ' + n + ' 个）  [roots=' + lastStats.roots +
        ' 上游层=' + (lastStats.byUp || 0) + ' 文本层=' + lastStats.byText +
        ' 几何层=' + lastStats.byGeom + ']  当前 URL=' + location.href);

    // 首轮没隐藏就打出真实文本，便于定位
    if (n === 0) { try { dump(12); } catch (e) { } }

    [500, 1200, 2500, 4000, 7000, 11000, 16000, 22000, 30000].forEach(function (ms) {
      setTimeout(function () { try { ensureStyle(); trimBar(); } catch (e) { } }, ms);
    });

    startObserver();

    // 视口变化（全屏 / 缩放）后重新定位
    try {
      window.addEventListener('resize', function () { try { schedule(); } catch (e) { } });
    } catch (e) { }
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