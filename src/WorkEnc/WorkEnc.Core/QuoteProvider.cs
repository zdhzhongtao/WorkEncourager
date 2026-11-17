using System;
using System.Collections.Generic;

namespace WorkEnc.Core
{
    /// <summary>鼓励语语气。</summary>
    public enum Tone
    {
        /// <summary>佛系：云淡风轻。</summary>
        Chill,
        /// <summary>毒舌：扎心但好笑。</summary>
        Savage,
        /// <summary>热血：打鸡血。</summary>
        Hype
    }

    /// <summary>鼓励语的三种情境。</summary>
    public enum QuoteScene
    {
        /// <summary>还没赚到第一件目标物。</summary>
        FirstItem,
        /// <summary>已经赚到多件，等下一件。</summary>
        RepeatItem,
        /// <summary>今天剩下的时间也买不起一件了。</summary>
        NotEnough
    }

    /// <summary>
    /// 鼓励语录库。按语气 × 情境分组，模板中的数字由运行时填充。
    /// </summary>
    public class QuoteProvider
    {
        private static readonly Dictionary<Tone, Dictionary<QuoteScene, string[]>> Pools = new()
        {
            [Tone.Chill] = new Dictionary<QuoteScene, string[]>
            {
                [QuoteScene.FirstItem] = new[]
                {
                    "再过 {TIME}，一件{ITEM}就赚回来了",
                    "距离下班还有 {TIME}，{ITEM}基金还差一点点",
                    "{TIME}后又是一天，{ITEM}慢慢买",
                    "离下班 {TIME}，把 {ITEM} 列入购物车吧",
                },
                [QuoteScene.RepeatItem] = new[]
                {
                    "今天已赚回 {COUNT} 件{ITEM}，再过 {TIME} 下一件到手",
                    "{COUNT} 件{ITEM}已经在账上，距离下班还有 {TIME}",
                    "{TIME}后收工，今天的 {ITEM} 已经拿下 {COUNT} 件",
                },
                [QuoteScene.NotEnough] = new[]
                {
                    "再过 {TIME} 就下班了，今天的钱还能买 {CANBUY} 件{ITEM}",
                    "{TIME}后收工，{ITEM}今天先买 {CANBUY} 件吧",
                    "距离下班 {TIME}，{ITEM} 的预算还剩 {CANBUY} 件",
                },
            },
            [Tone.Savage] = new Dictionary<QuoteScene, string[]>
            {
                [QuoteScene.FirstItem] = new[]
                {
                    "再过 {TIME}，才够买一件{ITEM}，工资涨了吗",
                    "离下班 {TIME}，你今天挣了 {EARNED}，够买 {ITEM} 吗",
                    "{TIME} 后才能下班，而 {ITEM} 是按秒计价的梦想",
                    "别刷手机了，还有 {TIME}，你还在为 {ITEM} 努力",
                },
                [QuoteScene.RepeatItem] = new[]
                {
                    "今日已赚 {EARNED}，按这个速度买 {ITEM} 需要再等 {TIME}",
                    "{COUNT} 件{ITEM}到手，剩下 {TIME}，你确定不多赚点？",
                    "上班 {TIME}，摸鱼 0 秒，这就是 {COUNT} 件{ITEM} 的代价",
                },
                [QuoteScene.NotEnough] = new[]
                {
                    "再过 {TIME} 就下班了，今天的钱只能买 {CANBUY} 件{ITEM}",
                    "{TIME} 后收工，你的 {ITEM} 基金停在 {CANBUY} 件，明天继续",
                    "距离下班 {TIME}，连一件{ITEM} 都买不起，这就是现实",
                },
            },
            [Tone.Hype] = new Dictionary<QuoteScene, string[]>
            {
                [QuoteScene.FirstItem] = new[]
                {
                    "再过 {TIME}，一件{ITEM}到手，冲！",
                    "距下班还有 {TIME}，胜利在望，{ITEM} 有着落了",
                    "最后 {TIME}，冲刺！{ITEM} 就是今天的战利品",
                    "目标 {ITEM}，进度 {EARNED}，剩余 {TIME}，干就完了",
                },
                [QuoteScene.RepeatItem] = new[]
                {
                    "已经赚到了 {EARNED}！再战 {TIME} 拿下第 {COUNT} 件{ITEM}",
                    "{COUNT} 件{ITEM} 已到手！还有 {TIME}，继续冲！",
                    "目标 {ITEM}，进度 {EARNED}，剩余 {TIME}，干就完了",
                },
                [QuoteScene.NotEnough] = new[]
                {
                    "再过 {TIME} 就下班了，今天能买 {CANBUY} 件{ITEM}，收工！",
                    "{TIME} 后收工，今天的战利品是 {CANBUY} 件{ITEM}，明天翻倍",
                    "最后一程 {TIME}！{CANBUY} 件{ITEM} 已经到手",
                },
            },
        };

        private readonly Random _rng;

        /// <summary>
        /// 每个"语气 × 情境"组合各自记住上一次抽到的下标。
        /// 以前是一个字段全局共享：换个语气或情境后拿到的下标和别的池比较，防重复的判断等于失效，
        /// 于是偶尔会连着蹦同一句。
        /// </summary>
        private readonly Dictionary<(Tone tone, QuoteScene scene), int> _lastIndex = new();

        public QuoteProvider(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <summary>内置语录池（没有自定义语录时使用）。</summary>
        public static string[] BuiltInPool(Tone tone, QuoteScene scene) => Pools[tone][scene];

        /// <summary>把语气字符串解析为枚举，非法值回退到佛系。</summary>
        public static Tone ParseTone(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Tone.Chill;
            switch (s.Trim().ToLowerInvariant())
            {
                case "savage": return Tone.Savage;
                case "hype": return Tone.Hype;
                default: return Tone.Chill;
            }
        }

        /// <summary>
        /// 取一条语录并填充占位符。同一情境下连续调用会尽量避开重复（除非池里只有一条）。
        /// </summary>
        public string Get(Tone tone, QuoteScene scene, string item, string timeText,
                          string earnedText, int count = 0, string canBuyText = "0")
        {
            // 自定义语录优先（quotes.csv 里写了就用 CSV 的，没写就用内置）
            var pool = QuoteStore.PoolFor(tone, scene);
            if (pool == null || pool.Length == 0) return "";

            var key = (tone, scene);
            int idx = _rng.Next(pool.Length);
            if (pool.Length > 1 && _lastIndex.TryGetValue(key, out int last) && idx == last)
                idx = (idx + 1) % pool.Length;
            _lastIndex[key] = idx;

            string tpl = pool[idx];
            return tpl
                .Replace("{ITEM}", string.IsNullOrWhiteSpace(item) ? "目标" : item)
                .Replace("{TIME}", string.IsNullOrWhiteSpace(timeText) ? "一会儿" : timeText)
                .Replace("{EARNED}", string.IsNullOrWhiteSpace(earnedText) ? "0" : earnedText)
                .Replace("{COUNT}", count > 0 ? count.ToString() : "0")
                .Replace("{CANBUY}", string.IsNullOrWhiteSpace(canBuyText) ? "0" : canBuyText);
        }

        /// <summary>通用取法（不区分情境），用于设置面板预览等场景。</summary>
        public string Get(Tone tone, string item, string timeText, string earnedText)
        {
            return Get(tone, QuoteScene.FirstItem, item, timeText, earnedText);
        }
    }
}
