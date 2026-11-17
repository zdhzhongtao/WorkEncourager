using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SheetSplitter.Core
{
    /// <summary>%APPDATA%\SheetSplitter\settings.json 的内容。</summary>
    public sealed class AppSettings
    {
        /// <summary>配置格式版本（不是程序版本），迁移时用。</summary>
        public int Version { get; set; } = 1;

        public string Theme { get; set; } = "light";

        public string SourcePath { get; set; } = string.Empty;
        public string SourceSheet { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;

        public int SplitColumn { get; set; } = -1;
        public bool IncludeHeader { get; set; } = true;
        public bool SplitToSheets { get; set; }

        public int RowsPerFile { get; set; } = 1000;

        public bool AlignByName { get; set; } = true;
        public bool AddSourceColumns { get; set; } = true;

        /// <summary>合并时的 sheet 策略：First / SameName / All。</summary>
        public string MergeStrategy { get; set; } = "First";

        public List<string> RecentInputs { get; set; } = new List<string>();
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
