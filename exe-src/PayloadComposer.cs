using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DouyinQualityHelper
{
    /// <summary>
    /// 决定 preload 里注入什么。
    ///
    /// 三种组合（"注入内容"那三个单选）：
    ///   Both        画质强制器 + 脚本装载器
    ///   QualityOnly 只有画质强制器        —— 老行为，脚本装载器不出现
    ///   ScriptOnly  只有脚本装载器        —— 不碰 sessionStorage，不强制画质
    ///
    /// 装载器自身是 payload.js 前面那一段；这里只在需要时把它拼上去，
    /// 而不是装完再删，避免用户升级后 loaders 残留。
    /// </summary>
    public static class PayloadComposer
    {
        public const string MARK_LOADER = "[dy-userscript]";

        /// <summary>
        /// 装载器末尾的显式标记（injector/userscript-loader.js 的最后一行）。
        /// 用显式标记而不是猜代码结尾：装载器改实现时不用同步改这里。
        /// </summary>
        public const string LOADER_END = "/* ====== LOADER-END ====== */";

        /// <summary>去掉装载器段，只留画质强制器。</summary>
        public static string QualityPart(string combined)
        {
            if (string.IsNullOrEmpty(combined)) return combined;
            int cut = combined.IndexOf(LOADER_END, StringComparison.Ordinal);
            if (cut < 0) return combined;   // 没有装载器（老版本 payload）：原样返回
            cut += LOADER_END.Length;
            return combined.Substring(cut).TrimStart('\r', '\n');
        }

        /// <summary>取出装载器段。</summary>
        public static string LoaderPart(string combined)
        {
            if (string.IsNullOrEmpty(combined)) return "";
            int cut = combined.IndexOf(LOADER_END, StringComparison.Ordinal);
            if (cut < 0) return "";
            return combined.Substring(0, cut + LOADER_END.Length);
        }

        /// <summary>按注入方式组合出最终的 payload 文本。</summary>
        public static string Compose(string combined, InjectMode mode)
        {
            switch (mode)
            {
                case InjectMode.QualityOnly: return QualityPart(combined);
                case InjectMode.ScriptOnly: return LoaderPart(combined);
                default: return combined;
            }
        }

        /// <summary>
        /// 校验组合结果：ScriptOnly 必须真的有装载器、QualityOnly 不能再含装载器。
        /// </summary>
        public static void Validate(string payload, InjectMode mode)
        {
            if (string.IsNullOrWhiteSpace(payload))
                throw new InvalidOperationException("注入内容为空，无法生成补丁（内置资源可能已损坏）。");

            bool hasLoader = payload.IndexOf(MARK_LOADER, StringComparison.Ordinal) >= 0;
            bool hasQuality = payload.IndexOf("PICK_4K", StringComparison.Ordinal) >= 0;

            switch (mode)
            {
                case InjectMode.QualityOnly:
                    if (hasLoader) throw new InvalidOperationException("「只装最高画质」却仍带脚本装载器，已中止。");
                    if (!hasQuality) throw new InvalidOperationException("内置资源里找不到画质强制器，已中止。");
                    break;
                case InjectMode.ScriptOnly:
                    if (!hasLoader) throw new InvalidOperationException("内置资源里找不到脚本装载器，已中止。");
                    if (hasQuality) throw new InvalidOperationException("「只装抖音优化脚本」却仍带画质强制器，已中止。");
                    break;
                default:
                    if (!hasLoader || !hasQuality) throw new InvalidOperationException("内置资源不完整（缺少脚本装载器或画质强制器），已中止。");
                    break;
            }
        }
    }
}
