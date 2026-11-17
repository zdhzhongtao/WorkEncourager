using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TableDiff.Core
{
    /// <summary>存 %APPDATA%\TableDiff\settings.json 的东西。带 Version 便于以后迁移。</summary>
    public sealed class AppSettings
    {
        /// <summary>配置格式版本，不是程序版本；迁移时用它判断。</summary>
        public int Version { get; set; } = 1;

        public string Theme { get; set; } = "light";

        public string PathA { get; set; } = string.Empty;
        public string PathB { get; set; } = string.Empty;
        public string SheetA { get; set; } = string.Empty;
        public string SheetB { get; set; } = string.Empty;

        /// <summary>ByKey / ByPosition / ByWholeRow</summary>
        public string Mode { get; set; } = "ByKey";

        public bool IgnoreCase { get; set; }
        public bool IgnoreTrim { get; set; } = true;
        public bool EmptyEqualsMissing { get; set; } = true;
        public bool IgnoreBlankRows { get; set; } = true;
        public double NumberTolerance { get; set; }

        /// <summary>主键列名（存名字不存序号，换文件后还能对上）。</summary>
        public List<string> KeyColumns { get; set; } = new List<string>();

        /// <summary>手动列映射（只存被改过的那几列，空表示按表头名自动匹配）。</summary>
        public List<ColumnMap> ManualColumns { get; set; } = new List<ColumnMap>();
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
                var s = AtomicFile.ReadJson<AppSettings>(PathIn(dataDir), JsonOptions);
                return s ?? new AppSettings();
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
