using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorkEnc.Core;

namespace WorkEncourager
{
    public partial class MainWindow : Window
{
        // ---------------- 导入导出 ----------------

        /// <summary>最近一次导出的文件路径，供"复制路径 / 打开文件夹"使用。</summary>
        private string _lastExportPath = "";

        /// <summary>进备份包的文件清单。</summary>
        private static readonly string[] BackupFiles = { "settings.json", "history.json", "quotes.json" };

        // ---------------- 版本更新 ----------------

        /// <summary>手动检查更新：失败给原因，有新版本直接打开下载页。</summary>
        private async void CheckUpdate()
        {
            CheckUpdateBtn.IsEnabled = false;
            SettingsTip.Text = "正在检查更新…";
            try
            {
                var (tag, url, error) = await UpdateChecker.GetLatestAsync();
                if (tag == null)
                {
                    SettingsTip.Text = "检查失败：" + (error ?? "未知原因")
                        + "\n可以手动打开：" + UpdateChecker.RepoUrl;
                    _lastExportPath = UpdateChecker.RepoUrl;
                    return;
                }

                if (UpdateChecker.IsNewer(tag, UpdateChecker.CurrentVersion))
                {
                    SettingsTip.Text = "发现新版本 " + tag + "（当前 v" + UpdateChecker.CurrentVersion + "）\n" + url;
                    _lastExportPath = url;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else
                {
                    SettingsTip.Text = "已是最新版本 v" + UpdateChecker.CurrentVersion;
                }
            }
            catch (Exception ex)
            {
                Log.Error("检查更新失败", ex);
                SettingsTip.Text = "检查失败：" + ex.Message;
            }
            finally
            {
                CheckUpdateBtn.IsEnabled = true;
            }
        }

        /// <summary>启动时静默查一次，有新版本才提示，失败完全不打扰。</summary>
        private async System.Threading.Tasks.Task CheckUpdateSilentlyAsync()
        {
            try
            {
                var (tag, _, _) = await UpdateChecker.GetLatestAsync(6);
                if (tag == null) return;

                if (UpdateChecker.IsNewer(tag, UpdateChecker.CurrentVersion))
                {
                    Log.Info("发现新版本 " + tag);
                    _tray?.ShowBalloon("上班鼓励器有新版 " + tag,
                        "当前 v" + UpdateChecker.CurrentVersion + "。右键托盘图标 → 打开设置 → 检查更新。");
                }
            }
            catch { /* 静默 */ }
        }

        // ---------------- 语录（CSV，Excel 可直接编辑） ----------------

        /// <summary>
        /// 语录文件有变化就重新加载。
        /// 用户用 Excel 改完保存，界面最多 5 秒后自动生效，不用重启。
        /// </summary>
        private void LoadQuotesIfChanged(bool force = false)
        {
            try
            {
                if (string.IsNullOrEmpty(_quotePath)) _quotePath = Path.Combine(DataDir, "quotes.csv");
                if (!File.Exists(_quotePath)) return;

                var stamp = File.GetLastWriteTimeUtc(_quotePath);
                if (!force && stamp == _quoteStamp) return;
                _quoteStamp = stamp;

                QuoteStore.SetCustom(File.ReadAllText(_quotePath, Encoding.UTF8));
                Log.Info("语录已加载：自定义 " + QuoteStore.CustomCount + " 条");
            }
            catch (Exception ex) { Log.Error("加载语录失败", ex); }
        }

        /// <summary>语录文件不存在时，把当前生效的全部语录写成一份模板。</summary>
        private void EnsureQuotesFile()
        {
            try
            {
                if (File.Exists(_quotePath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_quotePath));
                File.WriteAllText(_quotePath, QuoteStore.BuildCsv(), new UTF8Encoding(true));
                _quoteStamp = File.GetLastWriteTimeUtc(_quotePath);
            }
            catch (Exception ex) { Log.Error("生成语录模板失败", ex); }
        }

        /// <summary>
        /// 用系统默认程序打开语录 CSV（Excel / WPS 都行）。
        /// 存的是「语气,情境,语录」三列，改完保存即可热加载。
        /// </summary>
        private void EditQuotes()
        {
            try
            {
                EnsureQuotesFile();
                _lastExportPath = _quotePath;   // 让下方的"复制路径 / 打开文件夹"也能用

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _quotePath,
                    UseShellExecute = true
                });

                SettingsTip.Text = "已打开语录文件，共 " + QuoteStore.CustomCount + " 条自定义：\n" + _quotePath
                    + "\n格式：语气,情境,语录（佛系/毒舌/热血 × 首件/多件/买不够）。改完保存，5 秒内自动生效。"
                    + "\n保存时请选「CSV UTF-8」，否则中文会乱码。";
            }
            catch (Exception ex)
            {
                Log.Error("打开语录文件失败", ex);
                SettingsTip.Text = "打开失败：" + ex.Message + "\n文件路径：" + _quotePath;
            }
        }

        /// <summary>
        /// 导入外部工时文件（CSV/TXT）。
        ///
        /// 语义是"按日期增量合并"：文件里有的日期就更新，没有的日期一条不动。
        /// 同一天导入两遍结果完全一样（幂等），所以可以放心重复点。
        /// 打开对话框时会直接定位到上次导入的目录。
        /// </summary>
        private void ImportWorkLog()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "导入工时数据（CSV / TXT）",
                    Filter = "工时数据 (*.csv;*.txt)|*.csv;*.txt|所有文件 (*.*)|*.*",
                    InitialDirectory = Directory.Exists(_cfg.LastImportDir) ? _cfg.LastImportDir : DataDir
                };
                if (dlg.ShowDialog(this) != true) return;

                // 记住目录，下次直接到这儿
                _cfg.LastImportDir = Path.GetDirectoryName(dlg.FileName) ?? "";
                SaveCfg();

                var errors = new List<string>();
                var rows = WorkLogImporter.Parse(File.ReadAllText(dlg.FileName), errors);
                if (rows.Count == 0)
                {
                    SettingsTip.Text = "没解析出可导入的行。最少需要「日期 + 工时」两列。"
                        + (errors.Count > 0 ? "\n" + string.Join("\n", errors.Take(5)) : "");
                    return;
                }

                // 金额统一按当前月薪口径重算，避免和程序自己算的日期对不上
                double perSec = _calc.Compute(DateTime.Now, _cfg.MonthlySalary ?? 0, _cfg.Weekdays,
                                              _cfg.PayDay, false, 0).PerSec;

                int added = 0, updated = 0;
                foreach (var d in rows)
                {
                    bool existed = _tracker.HasDate(d.Date);
                    var rec = _tracker.GetOrCreate(d.Date);

                    if (d.WorkedHours.HasValue) rec.WorkedSec = d.WorkedHours.Value * 3600.0;
                    if (!string.IsNullOrEmpty(d.FirstClockIn)) rec.FirstClockIn = d.FirstClockIn;
                    if (!string.IsNullOrEmpty(d.LastClockOut)) rec.LastClockOut = d.LastClockOut;
                    if (d.OvertimeHours.HasValue) rec.OvertimeSec = d.OvertimeHours.Value * 3600.0;
                    if (!string.IsNullOrEmpty(d.Note)) rec.DayNote = d.Note;

                    rec.Earned = perSec * rec.WorkedSec;

                    if (DateTime.TryParse(d.Date, out var day))
                    {
                        if (rec.ScheduledSec <= 0) rec.ScheduledSec = _schedule.GetTotalWorkSeconds(day);
                        rec.IsPayDay = _calendar.IsPayDay(day, _cfg.Weekdays);
                    }

                    if (existed) updated++; else added++;
                }

                _tracker.Save(HistoryPath);
                if (_tab == MainTab.Stats) RefreshStats();
                Render();

                SettingsTip.Text = "导入完成：新增 " + added + " 天，更新 " + updated + " 天。"
                    + (errors.Count > 0 ? "\n有 " + errors.Count + " 行没读进来：" + errors[0] : "");
                Log.Info("导入工时 " + dlg.FileName + " 新增 " + added + " 更新 " + updated);
            }
            catch (Exception ex)
            {
                Log.Error("导入工时失败", ex);
                SettingsTip.Text = "导入失败：" + ex.Message;
            }
        }

        /// <summary>把配置、工时历史、语录打成一个 zip，方便换机或存档。</summary>
        private void BackupAll()
        {
            try
            {
                _tracker.Save(HistoryPath);
                ConfigStore.Save(CfgPath, _cfg);

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "备份全部数据",
                    Filter = "备份包 (*.zip)|*.zip",
                    FileName = "WorkEncourager-备份-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".zip",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog(this) != true) return;

                var files = BackupFiles
                    .Select(n => Path.Combine(DataDir, n))
                    .Where(File.Exists)
                    .ToList();
                if (files.Count == 0) { SettingsTip.Text = "没有可备份的数据"; return; }

                if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                using (var zip = System.IO.Compression.ZipFile.Open(dlg.FileName,
                           System.IO.Compression.ZipArchiveMode.Create))
                {
                    foreach (var f in files) zip.CreateEntryFromFile(f, Path.GetFileName(f));
                }

                _lastExportPath = dlg.FileName;
                SettingsTip.Text = "已备份 " + files.Count + " 个文件到：\n" + dlg.FileName;
                Log.Info("备份完成: " + dlg.FileName);
            }
            catch (Exception ex)
            {
                Log.Error("备份失败", ex);
                SettingsTip.Text = "备份失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 从 zip 恢复。当前数据会先原样留在 before-restore-时间戳 目录里，
        /// 恢复错了还能退回去。
        /// </summary>
        private void RestoreAll()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择备份包",
                    Filter = "备份包 (*.zip)|*.zip",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                };
                if (dlg.ShowDialog(this) != true) return;

                string safety = Path.Combine(DataDir, "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(safety);
                foreach (var name in BackupFiles)
                {
                    var p = Path.Combine(DataDir, name);
                    if (File.Exists(p)) File.Copy(p, Path.Combine(safety, name), true);
                }

                int restored = 0;
                using (var zip = System.IO.Compression.ZipFile.OpenRead(dlg.FileName))
                {
                    foreach (var entry in zip.Entries)
                    {
                        // 只认白名单里的三个文件，防止压缩包里塞别的东西
                        if (Array.IndexOf(BackupFiles, entry.Name) < 0) continue;
                        entry.ExtractToFile(Path.Combine(DataDir, entry.Name), true);
                        restored++;
                    }
                }
                if (restored == 0) { SettingsTip.Text = "这个包里没有可恢复的数据"; return; }

                LoadCfg();
                ApplyTheme();
                ApplyTopmost();
                ApplyOpacity();
                ApplyClickThrough();
                FillSettings(_cfg);
                UpdateFeatureButtons();
                UpdateToneButtons();
                UpdateThemeButtons();
                UpdateAutoHours();
                UpdatePreview();
                Render();

                SettingsTip.Text = "已恢复 " + restored + " 个文件。恢复前的数据留在：\n" + safety;
                Log.Info("从备份恢复: " + dlg.FileName);
            }
            catch (Exception ex)
            {
                Log.Error("恢复失败", ex);
                SettingsTip.Text = "恢复失败：" + ex.Message;
            }
        }

        private void ExportConfig()
        {
            try
            {
                var path = Path.Combine(DataDir, "workenc-config-" +
                    DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                File.WriteAllText(path, ConfigStore.ExportJson(_cfg), new System.Text.UTF8Encoding(true));
                _lastExportPath = path;
                SettingsTip.Text = "配置已导出，路径可复制：\n" + path;
            }
            catch (Exception ex) { SettingsTip.Text = "导出失败：" + ex.Message; }
        }

        private void ImportConfig()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择要导入的配置文件",
                    Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
                    InitialDirectory = DataDir
                };
                if (dlg.ShowDialog(this) != true) return;
                var cfg = ConfigStore.ImportJson(File.ReadAllText(dlg.FileName));
                if (cfg == null) { SettingsTip.Text = "配置文件格式不对，导入失败"; return; }
                _cfg = cfg;
                RebuildCalc();

                // 导入的每一项都要真正生效（v1.0.0 漏了置顶、穿透与视图模式）
                ApplyTheme();
                ApplyTopmost();
                ApplyOpacity();
                ApplyClickThrough();
                ApplyMinSize();
                SetViewMode(ViewState.ParseViewMode(_cfg.ViewMode));

                FillSettings(_cfg);
                SelectTab(ViewState.ParseTab(_cfg.ActiveTab));
                UpdateToneButtons();
                UpdateThemeButtons();
                UpdateAutoHours();
                UpdatePreview();

                SaveCfg();
                SettingsTip.Text = "配置已导入并保存";
                Render();
            }
            catch (Exception ex) { SettingsTip.Text = "导入失败：" + ex.Message; }
        }

        private void ExportCsv()
        {
            try
            {
                _tracker.Save(HistoryPath);
                var path = Path.Combine(DataDir, "工时报表-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                CsvExporter.WriteFile(path, CsvExporter.BuildWorkReport(_tracker.Records, _calendar, _cfg.Weekdays));
                _lastExportPath = path;
                ShowTip(_tab == MainTab.Stats ? StatsTip : SettingsTip,
                        "工时报表已导出，路径可复制：\n" + path);
            }
            catch (Exception ex) { ShowTip(SettingsTip, "导出失败：" + ex.Message); }
        }

        private void ExportMonthlyCsv()
        {
            try
            {
                _tracker.Save(HistoryPath);
                var path = Path.Combine(DataDir, "月度汇总-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                CsvExporter.WriteFile(path, CsvExporter.BuildMonthlyReport(_tracker.Records, _calendar, _cfg.Weekdays));
                _lastExportPath = path;
                ShowTip(StatsTip, "月度汇总已导出，路径可复制：\n" + path);
            }
            catch (Exception ex) { ShowTip(StatsTip, "导出失败：" + ex.Message); }
        }

        /// <summary>把最近一次导出的路径复制到剪贴板。</summary>
        private void CopyLastPath(TextBox tip)
        {
            if (string.IsNullOrEmpty(_lastExportPath))
            {
                ShowTip(tip, "还没有导出过文件，先点一次导出。");
                return;
            }
            try
            {
                Clipboard.SetText(_lastExportPath);
                ShowTip(tip, "路径已复制到剪贴板：\n" + _lastExportPath);
            }
            catch (Exception ex) { ShowTip(tip, "复制失败：" + ex.Message); }
        }

        /// <summary>在资源管理器中定位最近一次导出的文件。</summary>
        private void OpenLastFolder(TextBox tip)
        {
            if (string.IsNullOrEmpty(_lastExportPath))
            {
                ShowTip(tip, "还没有导出过文件，先点一次导出。");
                return;
            }
            try
            {
                string dir = Path.GetDirectoryName(_lastExportPath) ?? DataDir;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + _lastExportPath + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { ShowTip(tip, "打开失败：" + ex.Message); }
        }

        private static void ShowTip(TextBox target, string text)
        {
            if (target != null) target.Text = text;
        }

        // ---------------- 历史统计 ----------------

        public void ShowStatsForDebug() => SelectTab(MainTab.Stats);

        public void ShowDetailForDebug() => SelectTab(MainTab.Detail);

        public void ShowMiniForDebug() => SetViewMode(AppViewMode.Mini);

        /// <summary>调试用：以暂停状态启动，且退出时不把暂停状态写回配置。</summary>
        public void ShowPausedForDebug()
        {
            _paused = true;
            _pauseAt = DateTime.Now;
            _debugNoPersist = true;
            Render();
        }

        private void RefreshStats()
        {
            var analyzer = new HistoryAnalyzer(_calendar);
            var now = DateTime.Now;
            var s = analyzer.Analyze(_tracker.Records, now, _cfg.Weekdays);

            StatWeekHours.Text = (s.Week.TotalWorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            StatWeekEarned.Text = "¥" + N4(s.Week.TotalEarned);
            StatMonthHours.Text = (s.Month.TotalWorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            StatMonthEarned.Text = "¥" + N4(s.Month.TotalEarned);
            StatYearHours.Text = (s.Year.TotalWorkedSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
            StatYearEarned.Text = "¥" + N4(s.Year.TotalEarned);
            StatOvertimeHours.Text = (s.All.TotalOvertimeSec / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);

            int otDays = _tracker.Records.Count(r => r.OvertimeSec > 60);
            StatOvertimeDays.Text = otDays + " 天有加班";

            var sb = new List<string>();
            sb.Add("本月出勤 " + s.Month.DayCount + " 天 / 应出勤 " + s.MonthPayDays + " 个计薪日");
            sb.Add("日均工时 " + (s.Month.DayCount > 0 ? (s.Month.AvgWorkedSec / 3600.0).ToString("0.00") : "0.00") + " 小时");
            if (!string.IsNullOrEmpty(s.All.BestDayDate))
                sb.Add("最赚的一天 " + s.All.BestDayDate + "（¥" + N4(s.All.BestDayEarned) +
                       "，" + (s.All.BestDayWorkedSec / 3600.0).ToString("0.00") + " 小时）");
            sb.Add("累计工作 " + (s.All.TotalWorkedSec / 3600.0).ToString("0.00") + " 小时，共 " + s.All.DayCount + " 天");
            StatDetailText.Text = string.Join("\n", sb);
        }

        // ---------------- 调试截图 ----------------

        public void SaveShot(string path)
        {
            double w = Math.Max(1, ActualWidth);
            double h = Math.Max(1, ActualHeight);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Ceiling(w * 2), (int)Math.Ceiling(h * 2), 192, 192, PixelFormats.Pbgra32);
            rtb.Render(this);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
}
