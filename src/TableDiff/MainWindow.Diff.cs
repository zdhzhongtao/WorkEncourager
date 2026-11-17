using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;
using TableDiff.Core;

namespace TableDiff
{
    /// <summary>表格里一行要显示的东西。Text/Bg/Tip 按列下标对齐，DataGrid 直接按索引绑定。</summary>
    public sealed class RowView
    {
        public string Status { get; set; }
        public Brush StatusBrush { get; set; }
        public string Key { get; set; }
        public string[] Text { get; set; }
        public string[] Tip { get; set; }
        public Brush[] Bg { get; set; }
        public RowStatus Kind { get; set; }
    }

    public partial class MainWindow
    {
        private const string XamlNs = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"";
        private Brush[] _noMark;
        private List<ColumnMap> _lastMaps;
        private List<string> _headersA = new List<string>();
        private List<string> _headersB = new List<string>();

        // ---------------- 主键下拉 ----------------

        private List<string> SelectedKeyNames()
        {
            var list = new List<string>();
            foreach (var combo in new[] { Key1Combo, Key2Combo, Key3Combo })
            {
                string name = combo.SelectedItem as string;
                if (!string.IsNullOrEmpty(name) && !name.StartsWith("（", StringComparison.Ordinal)) list.Add(name);
            }
            return list;
        }

        private void RebuildKeyCombos(IList<string> headers)
        {
            var previous = SelectedKeyNames();
            var items = new List<string> { "（不用）" };
            if (headers != null) items.AddRange(headers);

            _loading = true;
            foreach (var combo in new[] { Key1Combo, Key2Combo, Key3Combo })
            {
                var old = combo.SelectedItem as string;
                combo.ItemsSource = items;
                combo.SelectedIndex = 1 < items.Count ? 1 : 0;
                if (!string.IsNullOrEmpty(old) && items.Contains(old)) combo.SelectedItem = old;
            }
            // 恢复用户上次选的列名
            if (_settings.KeyColumns != null && _settings.KeyColumns.Count > 0)
            {
                var combos = new[] { Key1Combo, Key2Combo, Key3Combo };
                for (int i = 0; i < combos.Length; i++)
                {
                    if (i < _settings.KeyColumns.Count && items.Contains(_settings.KeyColumns[i]))
                        combos[i].SelectedItem = _settings.KeyColumns[i];
                    else if (i > 0)
                        combos[i].SelectedIndex = 0;
                }
            }
            _loading = false;
        }

        // ---------------- 开始比对 ----------------

        private async void Compare_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            string pathA = PathABox.Text, pathB = PathBBox.Text;
            if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
            {
                Status("请先选好表 A 和表 B 两个文件。");
                return;
            }
            if (!File.Exists(pathA) || !File.Exists(pathB))
            {
                Status("文件不见了，请重新选择。");
                return;
            }

            string sheetA = SheetACombo.SelectedItem as string;
            string sheetB = SheetBCombo.SelectedItem as string;
            var keyNames = SelectedKeyNames();
            var manual = _manualMaps?.Select(m => m.Clone()).ToList();

            var mode = ModePositionRadio.IsChecked == true ? DiffMode.ByPosition
                     : ModeRowRadio.IsChecked == true ? DiffMode.ByWholeRow : DiffMode.ByKey;

            var options = new DiffOptions
            {
                Mode = mode,
                IgnoreCase = IgnoreCaseCheck.IsChecked == true,
                IgnoreTrim = IgnoreTrimCheck.IsChecked == true,
                EmptyEqualsMissing = EmptySameCheck.IsChecked == true,
                IgnoreBlankRows = IgnoreBlankCheck.IsChecked == true,
                NumberTolerance = ReadTolerance()
            };

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            SetBusy(true);
            Status("正在读取并比对…");

            IProgress<int> progress = new Progress<int>(p => { Progress.Value = Math.Max(0, Math.Min(100, p)); });

            try
            {
                var outcome = await Task.Run(() =>
                {
                    var fileA = TableReader.Load(pathA, (done, total) => progress.Report(total <= 0 ? 0 : done * 50 / total), token, Log.Error);
                    var fileB = TableReader.Load(pathB, (done, total) => progress.Report(total <= 0 ? 50 : 50 + done * 50 / total), token, Log.Error);

                    var sheetDataA = fileA.Find(sheetA);
                    var sheetDataB = fileB.Find(sheetB);
                    if (sheetDataA == null || sheetDataB == null)
                        throw new FriendlyException("选中的工作表没有数据。");

                    if (manual != null && manual.Count > 0)
                        foreach (var m in manual) options.Columns.Add(m);
                    else
                        foreach (var m in ColumnMapper.ByName(sheetDataA, sheetDataB)) options.Columns.Add(m);

                    if (options.KeyColumns.Count == 0)
                    {
                        for (int i = 0; i < keyNames.Count; i++)
                        {
                            int index = options.Columns.FindIndex(c => c.Name == keyNames[i]);
                            if (index >= 0) options.KeyColumns.Add(index);
                        }
                        if (options.KeyColumns.Count == 0 && options.Columns.Count > 0) options.KeyColumns.Add(0);
                    }

                    var result = DiffEngine.Compare(sheetDataA, sheetDataB, options, progress, token);
                    return new Outcome { Result = result, A = sheetDataA, B = sheetDataB };
                }, token);

                _result = outcome.Result;
                _lastMaps = options.Columns.Select(c => c.Clone()).ToList();
                _headersA = outcome.A.Columns.ToList();
                _headersB = outcome.B.Columns.ToList();
                _settings.NumberTolerance = options.NumberTolerance;
                _settings.Mode = mode == DiffMode.ByPosition ? "ByPosition" : mode == DiffMode.ByWholeRow ? "ByWholeRow" : "ByKey";

                Render(result: _result, headers: _result.Columns);
                SaveSettingsFromUi();

                Status(string.Format(CultureInfo.InvariantCulture,
                    "比对完成：A {0} 行 / B {1} 行，相同 {2}，仅A {3}，仅B {4}，差异行 {5}，差异单元格 {6}。",
                    _result.RowCountA, _result.RowCountB, _result.SameCount, _result.OnlyACount,
                    _result.OnlyBCount, _result.ChangedRowCount, _result.ChangedCellCount));

                Log.Info("比对完成 " + pathA + " vs " + pathB);
            }
            catch (OperationCanceledException)
            {
                Status("已取消。");
            }
            catch (FriendlyException ex)
            {
                Status(ex.Message);
                MessageBox.Show(ex.Message, "没法比对", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("比对失败", ex);
                Status("比对失败：" + ex.Message);
                MessageBox.Show("比对失败：" + ex.Message + "\n详细信息已写入日志：\n" + Log.FilePath,
                    "出错了", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        private sealed class Outcome
        {
            public DiffResult Result;
            public SheetData A;
            public SheetData B;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try { _cts?.Cancel(); } catch { }
            Status("正在取消…");
        }

        // ---------------- 结果渲染 ----------------

        private void Render() => Render(_result, _result == null ? null : _result.Columns);

        private void Render(DiffResult result, IList<string> headers)
        {
            Grid.Columns.Clear();
            _views = new List<RowView>();
            _noMark = null;

            if (result == null)
            {
                Grid.ItemsSource = null;
                UpdateStats();
                UpdateButtons();
                return;
            }

            var diffBg = ResolveBrush("ThemeDiffCellBg");
            var onlyAFg = ResolveBrush("ThemeOnlyAFg");
            var onlyBFg = ResolveBrush("ThemeOnlyBFg");
            var mutedFg = ResolveBrush("ThemeTextMuted");

            _noMark = new Brush[result.Columns.Count];

            Grid.Columns.Add(MakeStatusColumn());
            Grid.Columns.Add(MakeKeyColumn());

            for (int i = 0; i < result.Columns.Count; i++)
                Grid.Columns.Add(MakeDataColumn(i, result.Columns[i]));

            foreach (var row in result.Rows)
            {
                var view = new RowView
                {
                    Kind = row.Status,
                    Key = row.Key,
                    Text = new string[result.Columns.Count],
                    Tip = null
                };

                switch (row.Status)
                {
                    case RowStatus.Same:
                        view.Status = "相同";
                        view.StatusBrush = mutedFg;
                        break;
                    case RowStatus.Changed:
                        view.Status = "有差异";
                        view.StatusBrush = onlyAFg;
                        break;
                    case RowStatus.OnlyA:
                        view.Status = "仅A有";
                        view.StatusBrush = onlyAFg;
                        break;
                    default:
                        view.Status = "仅B有";
                        view.StatusBrush = onlyBFg;
                        break;
                }

                bool hasChange = row.Status == RowStatus.Changed;
                view.Bg = hasChange ? new Brush[result.Columns.Count] : _noMark;
                var tips = new string[result.Columns.Count];

                for (int c = 0; c < result.Columns.Count; c++)
                {
                    var cell = row.Cells[c];
                    string a = cell.ValueA ?? string.Empty;
                    string b = cell.ValueB ?? string.Empty;

                    if (cell.Changed)
                    {
                        view.Text[c] = CellText.WithThousands(a) + " → " + CellText.WithThousands(b);
                        view.Bg[c] = diffBg;
                        tips[c] = "A：" + a + "\r\nB：" + b + "\r\nA 第 " + row.SourceRowA + " 行 / B 第 " + row.SourceRowB + " 行";
                    }
                    else
                    {
                        view.Text[c] = row.Status == RowStatus.OnlyB ? b : a;
                        tips[c] = view.Text[c];
                    }
                }

                view.Tip = tips;
                _views.Add(view);
            }

            ApplyFilter();
            UpdateStats();
            UpdateButtons();
        }

        private void UpdateStats()
        {
            if (_result == null)
            {
                StatA.Text = StatB.Text = StatSame.Text = StatOnlyA.Text = StatOnlyB.Text =
                    StatChangedRows.Text = StatChangedCells.Text = "—";
                DupButton.Visibility = Visibility.Collapsed;
                return;
            }

            StatA.Text = _result.RowCountA.ToString(CultureInfo.InvariantCulture);
            StatB.Text = _result.RowCountB.ToString(CultureInfo.InvariantCulture);
            StatSame.Text = _result.SameCount.ToString(CultureInfo.InvariantCulture);
            StatOnlyA.Text = _result.OnlyACount.ToString(CultureInfo.InvariantCulture);
            StatOnlyB.Text = _result.OnlyBCount.ToString(CultureInfo.InvariantCulture);
            StatChangedRows.Text = _result.ChangedRowCount.ToString(CultureInfo.InvariantCulture);
            StatChangedCells.Text = _result.ChangedCellCount.ToString(CultureInfo.InvariantCulture);

            if (_result.DuplicateKeys.Count > 0)
            {
                DupButton.Content = "主键重复 " + _result.DuplicateKeys.Count + " 处";
                DupButton.Visibility = Visibility.Visible;
            }
            else
            {
                DupButton.Visibility = Visibility.Collapsed;
            }
        }

        private void Dup_Click(object sender, RoutedEventArgs e)
        {
            if (_result == null || _result.DuplicateKeys.Count == 0) return;

            var text = string.Join(Environment.NewLine, _result.DuplicateKeys.Take(50).Select(k => k.ToString()));
            if (_result.DuplicateKeys.Count > 50) text += Environment.NewLine + "…… 共 " + _result.DuplicateKeys.Count + " 处";
            MessageBox.Show("以下主键在表里出现了多次，比对时按第一条匹配，请人工核对：" + Environment.NewLine + Environment.NewLine + text,
                "主键重复", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private DataGridColumn MakeStatusColumn()
        {
            string xaml = "<DataTemplate " + XamlNs + ">"
                + "<TextBlock Text=\"{Binding Status}\" Foreground=\"{Binding StatusBrush}\" FontWeight=\"SemiBold\""
                + " Margin=\"8,0\" VerticalAlignment=\"Center\" />"
                + "</DataTemplate>";

            return new DataGridTemplateColumn
            {
                Header = "状态",
                Width = new DataGridLength(76),
                CellTemplate = (DataTemplate)XamlReader.Parse(xaml)
            };
        }

        private DataGridColumn MakeKeyColumn()
        {
            string xaml = "<DataTemplate " + XamlNs + ">"
                + "<TextBlock Text=\"{Binding Key}\" Margin=\"8,0\" VerticalAlignment=\"Center\""
                + " TextTrimming=\"CharacterEllipsis\" ToolTip=\"{Binding Key}\" />"
                + "</DataTemplate>";

            return new DataGridTemplateColumn
            {
                Header = "主键",
                Width = new DataGridLength(150),
                CellTemplate = (DataTemplate)XamlReader.Parse(xaml)
            };
        }

        private DataGridColumn MakeDataColumn(int index, string header)
        {
            string xaml = "<DataTemplate " + XamlNs + ">"
                + "<Border Margin=\"1,2\" CornerRadius=\"4\" Padding=\"6,1\" Background=\"{Binding Bg[" + index + "]}\">"
                + "<TextBlock Text=\"{Binding Text[" + index + "]}\" ToolTip=\"{Binding Tip[" + index + "]}\""
                + " VerticalAlignment=\"Center\" TextTrimming=\"CharacterEllipsis\" />"
                + "</Border></DataTemplate>";

            return new DataGridTemplateColumn
            {
                Header = header,
                Width = new DataGridLength(150),
                MinWidth = 80,
                CellTemplate = (DataTemplate)XamlReader.Parse(xaml)
            };
        }

        private static Brush ResolveBrush(string key)
        {
            var brush = Application.Current.TryFindResource(key) as Brush;
            return brush ?? Brushes.Gray;
        }

        // ---------------- 过滤与搜索 ----------------

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            ApplyFilter();
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_result == null) return;

            IEnumerable<RowView> query = _views;

            if (FilterCombo.SelectedIndex == 1)
                query = query.Where(v => v.Kind != RowStatus.Same);

            string keyword = (SearchBox.Text ?? string.Empty).Trim();
            if (keyword.Length > 0)
            {
                query = query.Where(v =>
                {
                    if (v.Key != null && v.Key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    for (int i = 0; i < v.Text.Length; i++)
                    {
                        string t = v.Text[i];
                        if (t != null && t.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                    }
                    return false;
                });
            }

            Grid.ItemsSource = query.ToList();
        }

        // ---------------- 导出 ----------------

        private async void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_result == null) return;

            var dialog = new SaveFileDialog
            {
                Title = "导出差异清单",
                Filter = "CSV 文件|*.csv",
                FileName = CsvDiffExporter.DefaultFileName(DateTime.Now)
            };
            if (dialog.ShowDialog(this) != true) return;

            await ExportAsync(() => CsvDiffExporter.Export(_result, dialog.FileName),
                "已导出差异清单：" + dialog.FileName);
        }

        private async void ExportXlsx_Click(object sender, RoutedEventArgs e)
        {
            if (_result == null) return;

            var dialog = new SaveFileDialog
            {
                Title = "导出差异报告",
                Filter = "Excel 工作簿|*.xlsx",
                FileName = "差异报告_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture) + ".xlsx"
            };
            if (dialog.ShowDialog(this) != true) return;

            await ExportAsync(() => XlsxDiffExporter.Export(_result, dialog.FileName, PathABox.Text, PathBBox.Text),
                "已导出差异报告：" + dialog.FileName);
        }

        private async Task ExportAsync(Action work, string successMessage)
        {
            SetBusy(true);
            Status("正在导出…");
            try
            {
                await Task.Run(work);
                Status(successMessage);
                Log.Info(successMessage);

                if (MessageBox.Show(successMessage + "\n\n要打开文件所在目录吗？", "导出完成",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    try
                    {
                        string path = successMessage.Substring(successMessage.IndexOf('：') + 1);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = "/select,\"" + path + "\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex) { Log.Error("打开目录失败", ex); }
                }
            }
            catch (IOException ex)
            {
                Log.Error("导出失败", ex);
                MessageBox.Show("写不进去：「" + ex.Message + "」。\n如果文件正在 Excel 里打开，请先关掉再试。",
                    "导出失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Log.Error("导出失败", ex);
                MessageBox.Show("导出失败：" + ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ---------------- 列映射 ----------------

        private async void MapButton_Click(object sender, RoutedEventArgs e)
        {
            if (_busy) return;

            string pathA = PathABox.Text, pathB = PathBBox.Text;
            if (!File.Exists(pathA) || !File.Exists(pathB))
            {
                Status("请先把两个文件都选好。");
                return;
            }

            List<ColumnMap> maps = null;
            List<string> headersA = _headersA;
            List<string> headersB = _headersB;

            if (_lastMaps != null && _lastMaps.Count > 0 && _headersB.Count > 0)
            {
                maps = _lastMaps.Select(m => m.Clone()).ToList();
            }
            else
            {
                string sheetA = SheetACombo.SelectedItem as string;
                string sheetB = SheetBCombo.SelectedItem as string;
                try
                {
                    Status("正在读取两张表的列名…");
                    var pair = await Task.Run(() => new[]
                    {
                        TableReader.ReadHeaders(pathA, sheetA),
                        TableReader.ReadHeaders(pathB, sheetB)
                    });
                    headersA = pair[0];
                    headersB = pair[1];
                    maps = ColumnMapper.ByName(headersA, headersB);
                }
                catch (Exception ex)
                {
                    Log.Error("读取列名失败", ex);
                    Status("读不到列名：" + ex.Message);
                    return;
                }
            }

            var dialog = new MappingWindow(maps, headersA, headersB) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            _manualMaps = dialog.Result;
            Status("列映射已更新，重新点「开始比对」生效。");
        }
    }
}
