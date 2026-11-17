using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoneyCalc.Core
{
    /// <summary>%APPDATA%\MoneyCalc\settings.json 的内容。</summary>
    public sealed class AppSettings
    {
        /// <summary>配置格式版本（不是程序版本）。</summary>
        public int Version { get; set; } = 1;

        public string Theme { get; set; } = "light";

        /// <summary>上次停在哪个计算器：words / workday / tax / date / rate。</summary>
        public string LastCalc { get; set; } = "words";

        public decimal TaxRate { get; set; } = 13m;

        /// <summary>true = 用内置的 2025/2026 节假日；用户编辑或清空后变 false，只认下面的两张表。</summary>
        public bool UseBuiltInHolidays { get; set; } = true;

        /// <summary>节假日表用「yyyy-MM-dd」存，方便手改。</summary>
        public List<string> Holidays { get; set; } = new List<string>();
        public List<string> MakeupWorkdays { get; set; } = new List<string>();

        public string RateFrom { get; set; } = "USD";
        public string RateTo { get; set; } = "CNY";

        public List<RateEntry> SavedRates { get; set; } = new List<RateEntry>();
    }

    public static class SettingsStore
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static string PathIn(string dataDir) => Path.Combine(dataDir, "settings.json");

        public static AppSettings Load(string dataDir)
        {
            try
            {
                return AtomicFile.ReadJson<AppSettings>(PathIn(dataDir), JsonOptions) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(string dataDir, AppSettings settings)
        {
            if (settings == null) return;
            Directory.CreateDirectory(dataDir);
            AtomicFile.WriteJson(PathIn(dataDir), settings, JsonOptions);
        }
    }
}
