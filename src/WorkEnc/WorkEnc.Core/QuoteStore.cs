using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WorkEnc.Core
{
    /// <summary>
    /// 语录库的"可编辑层"。
    ///
    /// 存储用 CSV（逗号分隔、UTF-8 带 BOM），因为它能被 Excel / WPS 直接双击打开编辑，
    /// 三列结构一目了然：语气,情境,语录。
    /// 程序内部仍然用嵌套池，CSV 只在读写时转换一次。
    ///
    /// 合并规则：某组（语气 × 情境）在 CSV 里写了内容，就用 CSV 的；
    /// 某组没写或写空了，继续用内置的。所以你可以只改一组，不用把整套抄一遍。
    /// </summary>
    public static class QuoteStore
    {
        /// <summary>CSV 表头。</summary>
        public const string Header = "语气,情境,语录";

        /// <summary>自定义语录池；为 null 表示全部走内置。</summary>
        private static Dictionary<Tone, Dictionary<QuoteScene, List<string>>> _custom;

        /// <summary>最近一次成功解析出的条目数（0 表示完全用内置）。</summary>
        public static int CustomCount { get; private set; }

        /// <summary>
        /// 最近一次加载时的解析问题（null 表示完全没问题）。
        /// 以前解析错误被静默吞掉：用户把 quotes.csv 写坏了，界面一点提示都没有，
        /// 只是悄悄回退到内置语录。现在把它留给 UI 提示用。
        /// </summary>
        public static string LastError { get; private set; }

        /// <summary>
        /// 把 CSV 文本装进内存。传 null / 空串表示清空自定义、回到全内置。
        /// 返回实际加载条数与第一条解析错误（没有错误时 error 为 null）。
        /// </summary>
        public static (int count, string error) SetCustom(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText))
            {
                _custom = null;
                CustomCount = 0;
                LastError = null;
                return (0, null);
            }
            var (count, errors) = ParseCsv(csvText, out var pools);
            _custom = pools;
            CustomCount = count;
            LastError = errors.Count > 0 ? errors[0] : null;
            return (count, LastError);
        }

        /// <summary>取某一组的生效语录：自定义优先，否则内置。</summary>
        public static string[] PoolFor(Tone tone, QuoteScene scene)
        {
            if (_custom != null &&
                _custom.TryGetValue(tone, out var byScene) &&
                byScene.TryGetValue(scene, out var list) &&
                list.Count > 0)
            {
                return list.ToArray();
            }
            return QuoteProvider.BuiltInPool(tone, scene);
        }

        /// <summary>
        /// 把"当前生效的全部语录"导出成 CSV（含表头），用于给用户编辑。
        /// 导出的是合并后的结果，所以用户看到的就是程序正在用的全部内容。
        /// </summary>
        public static string BuildCsv()
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");

            foreach (var tone in new[] { Tone.Chill, Tone.Savage, Tone.Hype })
            {
                foreach (var scene in new[] { QuoteScene.FirstItem, QuoteScene.RepeatItem, QuoteScene.NotEnough })
                {
                    foreach (var line in PoolFor(tone, scene))
                        sb.Append(ToneText(tone)).Append(',')
                          .Append(SceneText(scene)).Append(',')
                          .Append(Escape(line)).Append("\r\n");
                }
            }
            return sb.ToString();
        }

        /// <summary>解析 CSV，返回有效条数与错误行清单（最多 20 条）。</summary>
        public static (int count, List<string> errors) ParseCsv(string text) => ParseCsv(text, out _);

        private static (int count, List<string> errors) ParseCsv(string text,
            out Dictionary<Tone, Dictionary<QuoteScene, List<string>>> pools)
        {
            pools = null;
            var errors = new List<string>();
            int count = 0;
            if (string.IsNullOrWhiteSpace(text)) return (0, errors);

            // 先保留物理行号（跳掉表头后仍然指向用户看到的行），再过滤空行
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                            .Split('\n')
                            .Select((line, i) => (line, no: i + 1))
                            .Where(x => !string.IsNullOrWhiteSpace(x.line))
                            .ToList();

            for (int k = 0; k < lines.Count; k++)
            {
                var (raw, no) = lines[k];
                if (k == 0 && IsHeaderLine(raw)) continue;   // 表头不算错误

                var cells = SplitLine(raw);
                if (cells.Count < 3)
                {
                    AddError(errors, no, "不是「语气,情境,语录」三列");
                    continue;
                }

                string toneText = cells[0].Trim();
                string sceneText = cells[1].Trim();
                string quote = cells[2].Trim();

                if (!TryParseTone(toneText, out var tone))
                {
                    AddError(errors, no, "语气「" + toneText + "」不认识，只能写 佛系 / 毒舌 / 热血");
                    continue;
                }
                if (!TryParseScene(sceneText, out var scene))
                {
                    AddError(errors, no, "情境「" + sceneText + "」不认识，只能写 首件 / 多件 / 买不够");
                    continue;
                }
                if (quote.Length == 0)
                {
                    AddError(errors, no, "语录内容是空的");
                    continue;
                }

                pools ??= new Dictionary<Tone, Dictionary<QuoteScene, List<string>>>();
                if (!pools.TryGetValue(tone, out var byScene))
                {
                    byScene = new Dictionary<QuoteScene, List<string>>();
                    pools[tone] = byScene;
                }
                if (!byScene.TryGetValue(scene, out var list))
                {
                    list = new List<string>();
                    byScene[scene] = list;
                }
                if (!list.Contains(quote)) { list.Add(quote); count++; }
            }

            if (count == 0 && errors.Count == 0)
            {
                errors.Add("没有解析出任何语录。确认文件是「语气,情境,语录」三列，且语气写 佛系/毒舌/热血。");
            }
            return (count, errors);
        }

        /// <summary>是不是表头行（"语气,情境,语录"）。</summary>
        private static bool IsHeaderLine(string raw)
        {
            var cells = SplitLine(raw);
            if (cells.Count == 0) return false;
            string first = cells[0].Trim();
            return first == "语气" || string.Equals(first, "tone", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddError(List<string> errors, int line, string why)
        {
            if (errors.Count >= 20) return;
            errors.Add("第 " + line + " 行：" + why);
        }

        /// <summary>语气 → 中文（导出用）。</summary>
        public static string ToneText(Tone tone) => tone switch
        {
            Tone.Savage => "毒舌",
            Tone.Hype => "热血",
            _ => "佛系"
        };

        /// <summary>情境 → 中文（导出用）。</summary>
        public static string SceneText(QuoteScene scene) => scene switch
        {
            QuoteScene.RepeatItem => "多件",
            QuoteScene.NotEnough => "买不够",
            _ => "首件"
        };

        private static bool TryParseTone(string s, out Tone tone)
        {
            tone = Tone.Chill;
            if (string.IsNullOrWhiteSpace(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "chill": case "佛系": case "平静": tone = Tone.Chill; return true;
                case "savage": case "毒舌": case "扎心": tone = Tone.Savage; return true;
                case "hype": case "热血": case "鸡血": tone = Tone.Hype; return true;
                default: return false;
            }
        }

        private static bool TryParseScene(string s, out QuoteScene scene)
        {
            scene = QuoteScene.FirstItem;
            if (string.IsNullOrWhiteSpace(s)) return false;
            switch (s.Trim().ToLowerInvariant())
            {
                case "firstitem": case "first": case "首件": case "第一件": case "还没买": scene = QuoteScene.FirstItem; return true;
                case "repeatitem": case "repeat": case "多件": case "已买": case "再来一件": scene = QuoteScene.RepeatItem; return true;
                case "notenough": case "not": case "买不够": case "买不起": case "不够": scene = QuoteScene.NotEnough; return true;
                default: return false;
            }
        }

        /// <summary>切分一行，支持双引号包裹（语录里有逗号也不会切错）。</summary>
        private static List<string> SplitLine(string line)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            foreach (char ch in line)
            {
                if (ch == '"') { quoted = !quoted; continue; }
                if ((ch == ',' || ch == '，') && !quoted) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        /// <summary>含逗号或引号的内容加引号包裹。</summary>
        private static string Escape(string s)
        {
            if (s == null) return "";
            bool needQuote = s.Contains(',') || s.Contains('"') || s.Contains('，');
            if (!needQuote) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
