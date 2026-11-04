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

    /// <summary>
    /// 鼓励语录库。按语气分组，同一条文案在不同语境下动态插入数字。
    /// </summary>
    public class QuoteProvider
    {
        private static readonly Dictionary<Tone, string[]> Pools = new()
        {
            [Tone.Chill] = new[]
            {
                "再过 {TIME}，一件{ITEM}就赚回来了",
                "距离下班还有 {TIME}，{ITEM}基金还差一点点",
                "{TIME}后又是一天，{ITEM}慢慢买",
                "今天已经赚了 {EARNED}，够 {ITEM} 犒劳自己了",
                "离下班 {TIME}，把 {ITEM} 列入购物车吧",
                "时间是 {TIME}，够你认真摸会儿鱼了",
                "离下班还有 {TIME}，{ITEM}在向你招手",
                "{EARNED} 到手，先想想今天要买几件{ITEM}",
            },
            [Tone.Savage] = new[]
            {
                "再过 {TIME}，才够买一件{ITEM}，工资涨了吗",
                "离下班 {TIME}，你今天挣了 {EARNED}，够买 {ITEM} 吗",
                "{TIME} 后才能下班，而 {ITEM} 是按秒计价的梦想",
                "今日已赚 {EARNED}，按这个速度买 {ITEM} 需要再等 {TIME}",
                "别刷手机了，还有 {TIME}，你还在为 {ITEM} 努力",
                "上班 {TIME}，摸鱼 0 秒，这就是 {ITEM} 的代价",
                "距离下班 {TIME}，否则你买不起下一件{ITEM}",
                "你的 {ITEM} 基金进度：{EARNED}，距离自由还有 {TIME}",
            },
            [Tone.Hype] = new[]
            {
                "再过 {TIME}，一件{ITEM}到手，冲！",
                "距下班还有 {TIME}，胜利在望，{ITEM} 有着落了",
                "今天已赚 {EARNED}！再战 {TIME} 拿下 {ITEM}",
                "最后 {TIME}，冲刺！{ITEM} 就是今天的战利品",
                "你已经赚到了 {EARNED}，离下班还有 {TIME}，继续冲",
                "{TIME} 后收工，那件{ITEM} 已经是你的了",
                "目标 {ITEM}，进度 {EARNED}，剩余 {TIME}，干就完了",
                "时间就是金钱，每一秒都在为 {ITEM} 充值",
            },
        };

        private readonly Random _rng;

        public QuoteProvider(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

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

        /// <summary>取一条语录并填充占位符。</summary>
        /// <param name="tone">语气</param>
        /// <param name="item">目标物名称</param>
        /// <param name="timeText">时间文本，如"3小时25分"</param>
        /// <param name="earnedText">金额文本</param>
        public string Get(Tone tone, string item, string timeText, string earnedText)
        {
            var pool = Pools[tone];
            string tpl = pool[_rng.Next(pool.Length)];
            return tpl
                .Replace("{ITEM}", string.IsNullOrWhiteSpace(item) ? "目标" : item)
                .Replace("{TIME}", string.IsNullOrWhiteSpace(timeText) ? "一会儿" : timeText)
                .Replace("{EARNED}", string.IsNullOrWhiteSpace(earnedText) ? "0" : earnedText);
        }
    }
}
