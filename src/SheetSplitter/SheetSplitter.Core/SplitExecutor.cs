using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace SheetSplitter.Core
{
    /// <summary>执行结果：写了哪些、跳了哪些（含原因）。</summary>
    public sealed class ExecutionReport
    {
        public List<string> Written { get; } = new List<string>();
        public List<string> Failed { get; } = new List<string>();
        public int Rows { get; set; }

        public string Summary =>
            "生成 " + Written.Count + " 个文件，共 " + Rows + " 行"
            + (Failed.Count > 0 ? "，失败 " + Failed.Count + " 个" : "") + "。";
    }

    /// <summary>把计划落盘。单个文件失败不影响其余的，失败原因记下来一起给用户看。</summary>
    public static class SplitExecutor
    {
        public static ExecutionReport Execute(SplitPlan plan, string outputDirectory,
                                              Action<int, int> progress = null,
                                              CancellationToken token = default)
        {
            if (plan == null) throw new FriendlyException("还没有可执行的计划。");
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new FriendlyException("请先选择输出目录。");

            Directory.CreateDirectory(outputDirectory);

            var report = new ExecutionReport();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < plan.Files.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var file = plan.Files[i];
                string name;
                try
                {
                    name = FileNameSanitizer.ResolveUnique(outputDirectory, file.FileName, used);
                    string path = Path.Combine(outputDirectory, name);
                    ExcelWriter.Write(path, file.Sheets);
                    report.Written.Add(name);
                    report.Rows += file.RowCount;
                }
                catch (Exception ex)
                {
                    report.Failed.Add(file.FileName + " - " + Friendly(ex));
                }

                progress?.Invoke(i + 1, plan.Files.Count);
            }

            return report;
        }

        private static string Friendly(Exception ex)
        {
            if (ex is IOException) return "文件被占用或没有写权限（" + ex.Message + "）";
            if (ex is UnauthorizedAccessException) return "没有写权限";
            return ex.Message;
        }
    }
}
