抖音画质助手 · 纯 JS 脚本
版本 2.0.3
项目：https://github.com/yss161/douyin-quality-helper-plus

这些脚本可以脱离 EXE 单独使用。

【怎么用】
  1. 先用 EXE 或别的办法让抖音页面能执行自定义 JS
     （或者用 DevTools 直接粘贴）
  2. 打开对应的页面：
       直播间  -> live.douyin.com/<房间号>
       视频页  -> www.douyin.com
  3. 按 F12 打开 DevTools，切到 Console
  4. 把对应 .js 文件的全部内容粘贴进去，回车
  5. 刷新页面后需要重新执行

【脚本清单】

  1-gift-hide.js          直播间：精简礼物栏
                          隐藏礼物图标，只留「更多」入口与钻石
                          临时恢复：window.__DY_LIVE_GIFT_HIDE__ = false;

  2-share-link-clean.js   直播间：优化复制链接
                          点「分享-复制链接」拿到的是干净链接
                          临时关闭：window.__DY_LIVE_SHARE_CLEAN__ = false;

  3-quality-force.js      视频：强制最高画质
                          写入 sessionStorage 的 MANUAL_SWITCH
                          这是 EXE 版「一键启用」在页面侧做的那部分

  4-gm-host.js            GM 兼容层（进阶）
                          提供 GM_* API + 左侧栏设置入口，
                          用来跑油猴脚本（如「抖音优化」）

【说明】
  - 这些脚本都不依赖项目里的其它文件，可单独使用
  - 全部用 try/catch 包裹，出错不会破坏页面
  - 4-gm-host.js 只是兼容层，还需要脚本本体才能用

【完整功能请用 EXE】
  双击「抖音画质助手.exe」，一键完成注入、还原、进程管理，
  并支持自动更新检测。