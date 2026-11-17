using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SheetSplitter.Core;

namespace SheetSplitter
{
    /// <summary>模式 3 里的一张 sheet 要不要参与合并。</summary>
    public sealed class SheetPick : INotifyPropertyChanged
    {
        private bool _checked = true;

        public string Name { get; set; }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly ObservableCollection<SheetPick> _sheetPicks = new ObservableCollection<SheetPick>();
        private readonly List<string> _mergeFiles = new List<string>();
        private readonly Dictionary<string, TableFile> _cache = new Dictionary<string, TableFile>(StringComparer.OrdinalIgnoreCase);

        private List<MergeInput> _inputs = new List<MergeInput>();
        private SplitPlan _plan;
        private bool _loading = true;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
            _settings = App.Settings;

            SheetList.ItemsSource = _sheetPicks;
            ApplySettings();
            UpdateThemeButton();

            _loading = false;
            UpdateStatus("先选源文件，再选输出目录");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            SaveSettingsFromUi();
            base.OnClosing(e);
        }

        // ---------------- 设置 ----------------

        private void ApplySettings()
        {
            _loading = true;
            OutDirBox.Text = _settings.OutputDir ?? string.Empty;
            HeaderCheck1.IsChecked = _settings.IncludeHeader;
            HeaderCheck4.IsChecked = _settings.IncludeHeader;
            SplitSheetRadio.IsChecked = _settings.SplitToSheets;
            SplitFileRadio.IsChecked = !_settings.SplitToSheets;
            RowsPerFileBox.Text = _settings.RowsPerFile.ToString(CultureInfo.InvariantCulture);
            SourceColsCheck2.IsChecked = _settings.AddSourceColumns;
            SourceColsCheck3.IsChecked = _settings.AddSourceColumns;
            AlignCombo2.SelectedIndex = _settings.AlignByName ? 0 : 1;
            AlignCombo3.SelectedIndex = _settings.AlignByName ? 0 : 1;
            StrategyCombo.SelectedIndex = _settings.MergeStrategy == "SameName" ? 1 : _settings.MergeStrategy == "All" ? 2 : 0;
            SameNameBox.Text = string.IsNullOrEmpty(_settings.SourceSheet) ? "Sheet1" : _settings.SourceSheet;
            _loading = false;

            UpdateRunState();
        }

        private void SaveSettingsFromUi()
        {
            _settings.OutputDir = OutDirBox.Text ?? string.Empty;
            _settings.IncludeHeader = HeaderCheck1.IsChecked == true;
            _settings.SplitToSheets = SplitSheetRadio.IsChecked == true;
            _settings.RowsPerFile = ReadRowsPerFile();
            _settings.AddSourceColumns = SourceColsCheck2.IsChecked == true;
            _settings.AlignByName = AlignCombo2.SelectedIndex == 0;
            _settings.MergeStrategy = StrategyCombo.SelectedIndex == 1 ? "SameName" : StrategyCombo.SelectedIndex == 2 ? "All" : "First";
            _settings.SourceSheet = Sheet1Combo.SelectedItem as string ?? _settings.SourceSheet;
            _settings.SourcePath = SourceBox1.Text ?? string.Empty;

            App.SaveSettings();
        }

        private int ReadRowsPerFile()
        {
            string text = (RowsPerFileBox.Text ?? string.Empty).Trim();
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : 1000;
        }

        private void UpdateThemeButton()
        {
            bool dark = string.Equals(_settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);
            ThemeButton.Content = dark ? "浅色模式" : "深色模式";
        }

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            bool dark = string.Equals(_settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);
            _settings.Theme = dark ? "light" : "dark";
            App.ApplyTheme(Theme.Get(_settings.Theme));
            UpdateThemeButton();
            App.SaveSettings();
        }

        // ---------------- 选文件 ----------------

        private void Browse1_Click(object sender, RoutedEventArgs e) => PickSource(1);
        private void Browse3_Click(object sender, RoutedEventArgs e) => PickSource(3);
        private void Browse4_Click(object sender, RoutedEventArgs e) => PickSource(4);

        private void PickSource(int tab)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要处理的表格",
                Filter = "表格文件|*.xlsx;*.xlsm;*.csv;*.tsv|所有文件|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;

            _ = LoadSourceAsync(tab, dialog.FileName);
        }

        private async Task LoadSourceAsync(int tab, string path)
        {
            try
            {
                UpdateStatus("正在打开 " + Path.GetFileName(path) + " …");
                var names = await Task.Run(() => TableReader.ListSheetNames(path));

                _loading = true;
                if (tab == 1 || tab == 4)
                {
                    var combo = tab == 1 ? Sheet1Combo : Sheet4Combo;
                    combo.ItemsSource = names;
                    combo.SelectedIndex = names.Count > 0 ? 0 : -1;
                }

                if (tab == 1) SourceBox1.Text = path;
                else if (tab == 3)
                {
                    SourceBox3.Text = path;
                    _sheetPicks.Clear();
                    foreach (var name in names) _sheetPicks.Add(new SheetPick { Name = name, Checked = true });
                }
                else SourceBox4.Text = path;
                _loading = false;

                if (tab == 1)
                {
                    await LoadColumnsAsync(path, Sheet1Combo.SelectedItem as string);
                    UpdateColumnPreview();
                }
                else if (tab == 4)
                {
                    UpdateRowSplitPreview(path, Sheet4Combo.SelectedItem as string);
                }
                else if (tab == 3)
                {
                    // 预读一次，后面点预览/执行就不用再打开
                    await Task.Run(() => Cache(path));
                    UpdateSheetMergePreview();
                }

                // 输出目录还空着就默认放到源文件旁边，省一次「选择…」
                if (string.IsNullOrWhiteSpace(OutDirBox.Text))
                {
                    string parent = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(parent)) OutDirBox.Text = parent;
                }

                UpdateStatus("已选择：" + Path.GetFileName(path));
                UpdateRunState();
            }
            catch (FriendlyException ex)
            {
                _loading = false;
                UpdateStatus(ex.Message);
                MessageBox.Show(ex.Message, "打不开这个文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                _loading = false;
                Log.Error("打开文件失败: " + path, ex);
                UpdateStatus("打开失败：" + ex.Message);
            }
        }

        private async Task LoadColumnsAsync(string path, string sheetName)
        {
            try
            {
                var headers = await Task.Run(() => TableReader.ReadHeaders(path, sheetName));
                _loading = true;
                SplitColumnCombo.ItemsSource = headers;
                if (headers.Count > 0)
                    SplitColumnCombo.SelectedIndex = _settings.SplitColumn >= 0 && _settings.SplitColumn < headers.Count ? _settings.SplitColumn : 0;
                _loading = false;
            }
            catch (Exception ex)
            {
                _loading = false;
                Log.Error("读取列名失败", ex);
            }
        }

        private void Sheet1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loading) return;
            string path = SourceBox1.Text;
            if (File.Exists(path)) _ = LoadColumnsAsync(path, Sheet1Combo.SelectedItem as string);
        }

        // ---------------- 模式 2：合并输入 ----------------

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要合并的文件（可多选）",
                Filter = "表格文件|*.xlsx;*.xlsm;*.csv;*.tsv|所有文件|*.*",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;

            foreach (var file in dialog.FileNames)
                if (!_mergeFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) _mergeFiles.Add(file);

            _ = RebuildMergeInputsAsync();
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "先随便选一个文件所在文件夹（选择文件夹对话框见下方提示）",
                Filter = "表格文件|*.xlsx;*.xlsm;*.csv;*.tsv",
                CheckFileExists = false,
                ValidateNames = false,
                FileName = "选择此文件夹里的所有表格"
            };
            if (dialog.ShowDialog(this) != true) return;

            string folder = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                UpdateStatus("这个文件夹不存在。");
                return;
            }

            var option = SubDirCheck.IsChecked == true ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(folder, "*.*", option)
                .Where(f => TableReader.IsSupported(f))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
                if (!_mergeFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) _mergeFiles.Add(file);

            UpdateStatus("从 " + folder + " 里找到 " + files.Count + " 个表格。");
            _ = RebuildMergeInputsAsync();
        }

        private void RemoveFiles_Click(object sender, RoutedEventArgs e)
        {
            var selected = MergeList.SelectedItems.Cast<string>().ToList();
            if (selected.Count == 0) return;
            foreach (var item in selected) _mergeFiles.Remove(item);
            _ = RebuildMergeInputsAsync();
        }

        private void Strategy_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loading) return;
            SameNameBox.IsEnabled = StrategyCombo.SelectedIndex == 1;
            if (_mergeFiles.Count > 0) _ = RebuildMergeInputsAsync();
        }

        private async Task RebuildMergeInputsAsync()
        {
            MergeList.ItemsSource = _mergeFiles.ToList();
            MergeCountText.Text = _mergeFiles.Count == 0 ? "还没添加文件" : "共 " + _mergeFiles.Count + " 个文件";

            if (_mergeFiles.Count == 0)
            {
                _inputs = new List<MergeInput>();
                Preview2Text.Text = string.Empty;
                UpdateRunState();
                return;
            }

            try
            {
                UpdateStatus("正在读取 " + _mergeFiles.Count + " 个文件…");
                var files = _mergeFiles.ToList();
                string sameName = SameNameBox.Text;
                int strategy = StrategyCombo.SelectedIndex;

                var inputs = await Task.Run(() =>
                {
                    var list = new List<MergeInput>();
                    foreach (var path in files)
                    {
                        TableFile file;
                        try { file = Cache(path); }
                        catch (Exception ex)
                        {
                            Log.Error("读取失败: " + path, ex);
                            continue;
                        }

                        foreach (var sheet in PickSheets(file, strategy, sameName))
                            list.Add(new MergeInput { Path = path, Data = sheet });
                    }
                    return list;
                });

                _inputs = inputs;
                Preview2Text.Text = "共 " + inputs.Count + " 张表参与合并：" + Environment.NewLine
                    + string.Join(Environment.NewLine, inputs.Take(12).Select(i => "· " + i.Display))
                    + (inputs.Count > 12 ? Environment.NewLine + "…… 还有 " + (inputs.Count - 12) + " 张" : "");
                UpdateStatus("准备就绪：" + inputs.Count + " 张表");
            }
            catch (Exception ex)
            {
                Log.Error("准备合并输入失败", ex);
                UpdateStatus("读取失败：" + ex.Message);
            }
            UpdateRunState();
        }

        private static IEnumerable<SheetData> PickSheets(TableFile file, int strategy, string sameName)
        {
            if (strategy == 2) return file.Sheets;
            if (strategy == 1)
            {
                var hit = file.Sheets.FirstOrDefault(s => string.Equals(s.Name, sameName, StringComparison.OrdinalIgnoreCase));
                return hit != null ? new[] { hit } : new[] { file.First };
            }
            return new[] { file.First };
        }

        private TableFile Cache(string path)
        {
            if (_cache.TryGetValue(path, out var hit)) return hit;
            var file = TableReader.Load(path);
            _cache[path] = file;
            return file;
        }

        // ---------------- 预览文字 ----------------

        private void Tabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _plan = null;
            UpdateRunState();
        }

        private void UpdateColumnPreview()
        {
            string path = SourceBox1.Text;
            if (!File.Exists(path)) { Preview1Text.Text = string.Empty; return; }

            try
            {
                var file = Cache(path);
                var sheet = file.Find(Sheet1Combo.SelectedItem as string);
                if (sheet == null) return;
                Preview1Text.Text = "源表 " + sheet.Rows.Count + " 行，" + sheet.ColumnCount + " 列。"
                    + "点「预览」看要生成哪些文件。";
            }
            catch (Exception ex)
            {
                Preview1Text.Text = "读表失败：" + ex.Message;
            }
        }

        private void UpdateRowSplitPreview(string path, string sheetName)
        {
            if (!File.Exists(path)) { Preview4Text.Text = string.Empty; return; }
            try
            {
                var sheet = Cache(path).Find(sheetName);
                if (sheet == null) return;
                int per = ReadRowsPerFile();
                int files = per <= 0 ? 0 : (int)Math.Ceiling(sheet.Rows.Count / (double)per);
                Preview4Text.Text = "源表 " + sheet.Rows.Count + " 行，每份 " + per + " 行 → 约 " + files + " 个文件。";
            }
            catch (Exception ex)
            {
                Preview4Text.Text = "读表失败：" + ex.Message;
            }
        }

        private void UpdateSheetMergePreview()
        {
            Preview3Text.Text = _sheetPicks.Count == 0
                ? "这个文件里没有可用的 sheet。"
                : "共 " + _sheetPicks.Count + " 张 sheet，勾选 " + _sheetPicks.Count(p => p.Checked) + " 张参与合并。";
        }

        // ---------------- 预览 / 执行 ----------------

        private async void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;
            try
            {
                SetBusy(true);
                _plan = await BuildPlanAsync();
                ShowPlan(_plan);
                UpdateStatus(_plan == null ? "没有可执行的计划。" : _plan.Summary);
            }
            catch (FriendlyException ex)
            {
                UpdateStatus(ex.Message);
                MessageBox.Show(ex.Message, "还差一步", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error("预览失败", ex);
                UpdateStatus("预览失败：" + ex.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            string outDir = OutDirBox.Text;
            if (string.IsNullOrWhiteSpace(outDir))
            {
                UpdateStatus("请先选输出目录。");
                MessageBox.Show("要先选一个输出目录，我不会往源文件所在的目录乱写。", "还差一步");
                return;
            }

            var plan = _plan;
            if (plan == null)
            {
                try
                {
                    SetBusy(true);
                    plan = await BuildPlanAsync();
                    SetBusy(false);
                }
                catch (FriendlyException ex)
                {
                    SetBusy(false);
                    UpdateStatus(ex.Message);
                    MessageBox.Show(ex.Message, "还差一步", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                catch (Exception ex)
                {
                    SetBusy(false);
                    Log.Error("准备执行失败", ex);
                    UpdateStatus("执行失败：" + ex.Message);
                    return;
                }
            }

            if (plan == null || plan.Files.Count == 0)
            {
                UpdateStatus("没有要生成的文件。");
                return;
            }

            if (IsSameDirectory(outDir))
            {
                var answer = MessageBox.Show(
                    "输出目录和源文件在同一个目录里。\n程序不会覆盖源文件（重名会自动加序号），但还是建议换个目录。\n\n继续吗？",
                    "输出目录和源文件同目录", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            SetBusy(true);
            UpdateStatus("正在生成 " + plan.Files.Count + " 个文件…");
            var progress = new Progress<int>(done =>
            {
                Progress.Value = plan.Files.Count == 0 ? 0 : done * 100 / plan.Files.Count;
            });

            try
            {
                var report = await Task.Run(() => SplitExecutor.Execute(plan, outDir, (d, t) => ((IProgress<int>)progress).Report(d)));
                string message = report.Summary;
                if (report.Failed.Count > 0)
                {
                    message += Environment.NewLine + "失败清单：" + Environment.NewLine
                        + string.Join(Environment.NewLine, report.Failed.Take(10).Select(f => "· " + f));
                }
                UpdateStatus(message.Replace(Environment.NewLine, "  "));
                OpenOutButton.IsEnabled = report.Written.Count > 0;
                Log.Info("拆分完成 → " + outDir + " : " + report.Summary);

                MessageBox.Show(message, report.Failed.Count > 0 ? "完成（有失败）" : "完成",
                    MessageBoxButton.OK, report.Failed.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                _plan = null;
            }
            catch (Exception ex)
            {
                Log.Error("执行失败", ex);
                UpdateStatus("执行失败：" + ex.Message);
                MessageBox.Show("执行失败：" + ex.Message, "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task<SplitPlan> BuildPlanAsync()
        {
            int tab = Tabs.SelectedIndex;
            string outDir = OutDirBox.Text;

            if (tab == 0)
            {
                string path = SourceBox1.Text;
                if (!File.Exists(path)) throw new FriendlyException("请先选择源文件。");
                if (SplitColumnCombo.SelectedIndex < 0) throw new FriendlyException("请先选择按哪一列拆分。");

                string sheetName = Sheet1Combo.SelectedItem as string;
                int column = SplitColumnCombo.SelectedIndex;
                bool header = HeaderCheck1.IsChecked == true;
                bool toSheets = SplitSheetRadio.IsChecked == true;
                string baseName = Path.GetFileNameWithoutExtension(path);

                return await Task.Run(() =>
                {
                    var sheet = Cache(path).Find(sheetName);
                    return SplitPlanner.PlanByColumn(sheet, column, baseName, header, toSheets);
                });
            }

            if (tab == 1)
            {
                if (_inputs.Count == 0) throw new FriendlyException("请先添加要合并的文件。");
                var inputs = _inputs;
                var alignment = AlignCombo2.SelectedIndex == 0 ? HeaderAlignment.ByName : HeaderAlignment.ByPosition;
                bool sourceCols = SourceColsCheck2.IsChecked == true;
                string fileName = FileNameSanitizer.Clean(Path.GetFileNameWithoutExtension(_mergeFiles.FirstOrDefault() ?? "合并"), "合并") + "_合并.xlsx";

                return await Task.Run(() => SplitPlanner.PlanMerge(inputs, alignment, sourceCols, fileName, "合并结果"));
            }

            if (tab == 2)
            {
                string path = SourceBox3.Text;
                if (!File.Exists(path)) throw new FriendlyException("请先选择源文件。");
                var chosen = _sheetPicks.Where(p => p.Checked).Select(p => p.Name).ToList();
                if (chosen.Count == 0) throw new FriendlyException("至少勾选一张 sheet。");

                var alignment = AlignCombo3.SelectedIndex == 0 ? HeaderAlignment.ByName : HeaderAlignment.ByPosition;
                bool sourceCols = SourceColsCheck3.IsChecked == true;
                string fileName = FileNameSanitizer.Clean(Path.GetFileNameWithoutExtension(path), "合并") + "_sheet合并.xlsx";

                return await Task.Run(() =>
                {
                    var file = Cache(path);
                    var inputs = chosen
                        .Select(name => file.Sheets.FirstOrDefault(s => s.Name == name))
                        .Where(s => s != null)
                        .Select(s => new MergeInput { Path = path, Data = s })
                        .ToList();
                    return SplitPlanner.PlanMerge(inputs, alignment, sourceCols, fileName, "合并结果");
                });
            }

            string path4 = SourceBox4.Text;
            if (!File.Exists(path4)) throw new FriendlyException("请先选择源文件。");
            int per = ReadRowsPerFile();
            if (per <= 0) throw new FriendlyException("每份行数要填一个大于 0 的整数。");
            string sheet4 = Sheet4Combo.SelectedItem as string;
            bool header4 = HeaderCheck4.IsChecked == true;
            string base4 = Path.GetFileNameWithoutExtension(path4);

            return await Task.Run(() =>
            {
                var sheet = Cache(path4).Find(sheet4);
                return SplitPlanner.PlanByRows(sheet, per, base4, header4);
            });
        }

        private void ShowPlan(SplitPlan plan)
        {
            var text = new List<string> { plan.Summary };
            if (plan.Notes.Count > 0) text.AddRange(plan.Notes.Select(n => "· " + n));

            if (plan.Files.Count > 0)
            {
                text.Add("要生成的（前 15 个）：");
                text.AddRange(plan.Files.Take(15).Select(f => "· " + f.FileName + "（" + f.RowCount + " 行）"));
                if (plan.Files.Count > 15) text.Add("…… 还有 " + (plan.Files.Count - 15) + " 个");
            }

            string block = string.Join(Environment.NewLine, text);
            switch (Tabs.SelectedIndex)
            {
                case 0: Preview1Text.Text = block; break;
                case 1: Preview2Text.Text = block; break;
                case 2: Preview3Text.Text = block; break;
                default: Preview4Text.Text = block; break;
            }
        }

        private bool IsSameDirectory(string outDir)
        {
            try
            {
                string source = Tabs.SelectedIndex == 1 ? _mergeFiles.FirstOrDefault() : CurrentSourcePath();
                if (string.IsNullOrEmpty(source)) return false;
                string a = Path.GetFullPath(Path.GetDirectoryName(source) ?? string.Empty).TrimEnd('\\');
                string b = Path.GetFullPath(outDir).TrimEnd('\\');
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private string CurrentSourcePath()
        {
            switch (Tabs.SelectedIndex)
            {
                case 0: return SourceBox1.Text;
                case 2: return SourceBox3.Text;
                case 3: return SourceBox4.Text;
                default: return null;
            }
        }

        // ---------------- 其他按钮 ----------------

        private void BrowseOut_Click(object sender, RoutedEventArgs e)
        {
            // .NET 8 起 WPF 自带系统的「选择文件夹」对话框，不用再拿 OpenFileDialog 凑
            var dialog = new OpenFolderDialog
            {
                Title = "选择输出目录",
                Multiselect = false
            };
            string current = OutDirBox.Text;
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog(this) != true) return;

            string dir = dialog.FolderName;
            if (string.IsNullOrEmpty(dir)) return;
            OutDirBox.Text = dir;
            UpdateRunState();
        }

        private void OutDir_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            UpdateRunState();
        }

        private void OpenOut_Click(object sender, RoutedEventArgs e)
        {
            string dir = OutDirBox.Text;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log.Error("打开目录失败", ex); }
        }

        private void SelectAllSheets_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pick in _sheetPicks) pick.Checked = true;
            UpdateSheetMergePreview();
        }

        private void SelectNoSheets_Click(object sender, RoutedEventArgs e)
        {
            foreach (var pick in _sheetPicks) pick.Checked = false;
            UpdateSheetMergePreview();
        }

        private void PreviewSource_Click(object sender, RoutedEventArgs e)
        {
            string path = CurrentSourcePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                UpdateStatus("先选源文件才能预览。");
                return;
            }

            try
            {
                var file = Cache(path);
                string sheetName = Tabs.SelectedIndex == 0 ? Sheet1Combo.SelectedItem as string
                                 : Tabs.SelectedIndex == 3 ? Sheet4Combo.SelectedItem as string : null;
                var sheet = file.Find(sheetName);
                new PreviewWindow(sheet) { Owner = this }.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("预览失败", ex);
                UpdateStatus("预览失败：" + ex.Message);
            }
        }

        // ---------------- 拖放 ----------------

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;

            if (Tabs.SelectedIndex == 1)
            {
                foreach (var file in files)
                    if (!_mergeFiles.Contains(file, StringComparer.OrdinalIgnoreCase)) _mergeFiles.Add(file);
                _ = RebuildMergeInputsAsync();
                return;
            }

            int tab = Tabs.SelectedIndex == 0 ? 1 : Tabs.SelectedIndex == 2 ? 3 : 4;
            _ = LoadSourceAsync(tab, files[0]);
        }

        // ---------------- 状态 ----------------

        private void UpdateStatus(string message) => StatusText.Text = message ?? string.Empty;

        private void UpdateRunState()
        {
            bool hasOut = !string.IsNullOrWhiteSpace(OutDirBox.Text);
            RunButton.IsEnabled = !_busy && hasOut;
            PreviewButton.IsEnabled = !_busy;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (!busy) Progress.Value = 0;
            RunButton.Content = busy ? "正在执行…" : "开始执行";
            UpdateRunState();
        }
    }
}
