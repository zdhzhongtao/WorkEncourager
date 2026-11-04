using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorkEnc.Core
{
    /// <summary>配置读写与版本迁移。</summary>
    public class ConfigStore
    {
        /// <summary>法定月计薪天数。</summary>
        public const double PayDaysPerMonth = 21.75;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };

        /// <summary>读取配置；不存在或损坏时返回默认配置（损坏文件会被隔离，并尝试从 .bak 恢复）。</summary>
        public static AppConfig Load(string path)
        {
            var cfg = AtomicFile.ReadJson<AppConfig>(path, Options) ?? new AppConfig();
            Migrate(cfg);
            return cfg;
        }

        /// <summary>保存配置（临时文件 + 原子替换，并保留 .bak 备份）。</summary>
        public static void Save(string path, AppConfig cfg)
        {
            try
            {
                AtomicFile.WriteJson(path, cfg, Options);
            }
            catch (Exception ex)
            {
                AtomicFile.ErrorHandler?.Invoke("配置保存失败: " + path, ex);
            }
        }

        /// <summary>旧版配置迁移 + 合法性兜底。</summary>
        public static void Migrate(AppConfig cfg)
        {
            // v1：日薪 + 工时 → 月薪 + 上下班时间
            if (!cfg.MonthlySalary.HasValue)
            {
                if (cfg.salary.HasValue && cfg.salary.Value > 0)
                {
                    cfg.MonthlySalary = Math.Round(cfg.salary.Value * PayDaysPerMonth, 2);
                    if (cfg.hours.HasValue && cfg.hours.Value > 0)
                        cfg.End = AddHours(cfg.Start, cfg.hours.Value);
                }
                else cfg.MonthlySalary = 6000;
            }
            cfg.salary = null;
            cfg.hours = null;

            if (cfg.MonthlySalary.Value < 0) cfg.MonthlySalary = 0;
            if (cfg.Shirt <= 0) cfg.Shirt = 69;
            if (cfg.Weekdays == null || cfg.Weekdays.Length == 0) cfg.Weekdays = new[] { 1, 2, 3, 4, 5 };
            if (cfg.Breaks == null) cfg.Breaks = Array.Empty<string>();
            if (cfg.ExtraHolidays == null) cfg.ExtraHolidays = Array.Empty<string>();
            if (cfg.ExtraWorkdays == null) cfg.ExtraWorkdays = Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(cfg.GoalItem)) cfg.GoalItem = "T恤";
            if (cfg.PayDay < 1 || cfg.PayDay > 31) cfg.PayDay = 10;
            if (!WorkSchedule.TryParseMinutes(cfg.Start, out _)) cfg.Start = "09:00";
            if (!WorkSchedule.TryParseMinutes(cfg.End, out _)) cfg.End = "18:00";
            if (string.IsNullOrWhiteSpace(cfg.Tone)) cfg.Tone = "chill";
            if (string.IsNullOrWhiteSpace(cfg.Theme)) cfg.Theme = "light";

            // 旧版本配置升级：窗口尺寸是针对旧的大布局存的，直接清掉让它按新布局自适应
            if (string.IsNullOrWhiteSpace(cfg.ViewMode))
            {
                cfg.ViewMode = "full";
                cfg.WindowWidth = null;
                cfg.WindowHeight = null;
                cfg.MiniWidth = null;
                cfg.MiniHeight = null;
                cfg.ActiveTab = "home";
                cfg.FullResized = false;
                cfg.MiniResized = false;
            }

            // 存下来的尺寸如果比该模式的最小尺寸还小，说明是旧版本残留，直接丢掉
            if (cfg.WindowWidth.HasValue && cfg.WindowWidth.Value < ViewState.FullMinWidth)
                cfg.WindowWidth = null;
            if (cfg.WindowHeight.HasValue && cfg.WindowHeight.Value < ViewState.FullMinHeight)
                cfg.WindowHeight = null;
            if (cfg.MiniWidth.HasValue && cfg.MiniWidth.Value < ViewState.MiniMinWidth)
                cfg.MiniWidth = null;
            if (cfg.MiniHeight.HasValue && cfg.MiniHeight.Value < ViewState.MiniMinHeight)
                cfg.MiniHeight = null;

            cfg.ViewMode = ViewState.ToText(ViewState.ParseViewMode(cfg.ViewMode));
            cfg.ActiveTab = ViewState.ToText(ViewState.ParseTab(cfg.ActiveTab));
        }

        /// <summary>构造用于计算日历的 PayCalendar（带用户自定义日期）。</summary>
        public static PayCalendar BuildCalendar(AppConfig cfg)
        {
            var cal = new PayCalendar();
            if (cfg.ExtraHolidays != null)
                foreach (var d in cfg.ExtraHolidays) cal.ExtraHolidays.Add(d);
            if (cfg.ExtraWorkdays != null)
                foreach (var d in cfg.ExtraWorkdays) cal.ExtraWorkdays.Add(d);
            return cal;
        }

        /// <summary>按配置构造作息表。</summary>
        public static WorkSchedule BuildSchedule(AppConfig cfg)
        {
            return new WorkSchedule
            {
                Start = cfg.Start,
                End = cfg.End,
                Breaks = new List<string>(cfg.Breaks ?? Array.Empty<string>())
            };
        }

        /// <summary>导出配置为 JSON 文本。</summary>
        public static string ExportJson(AppConfig cfg)
        {
            var clone = Clone(cfg);
            clone.salary = null;
            clone.hours = null;
            return JsonSerializer.Serialize(clone, Options);
        }

        /// <summary>从 JSON 文本导入配置；失败返回 null。</summary>
        public static AppConfig ImportJson(string json)
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg == null) return null;
                Migrate(cfg);
                return cfg;
            }
            catch { return null; }
        }

        /// <summary>深拷贝配置。</summary>
        public static AppConfig Clone(AppConfig cfg)
        {
            var json = JsonSerializer.Serialize(cfg, Options);
            return JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
        }

        private static string AddHours(string time, double hours)
        {
            if (!WorkSchedule.TryParseMinutes(time, out int m)) return "18:00";
            int total = m + (int)Math.Round(hours * 60);
            return WorkSchedule.FormatMinutes(total);
        }
    }
}
