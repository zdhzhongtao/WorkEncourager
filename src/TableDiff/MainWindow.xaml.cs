using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TableDiff.Core;

namespace TableDiff
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private List<ColumnMap> _manualMaps;              // null = 按表头名自动匹配
        private DiffResult _result;
        private List<RowView> _views = new List<RowView>();
        private CancellationTokenSource _cts;
        private bool _loading = true;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
            _settings = App.Settings;

            ApplySettingsToUi();
            UpdateThemeButton();

            _loading = false;

            if (!string.IsNullOrEmpty(_settings.PathA) && File.Exists(_settings.PathA))
                _ = LoadFileAsync('A', _settings.PathA, _settings.SheetA);
            if (!string.IsNullOrEmpty(_settings.PathB) && File.Exists(_settings.PathB))
                _ = LoadFileAsync('B', _settings.PathB, _settings.SheetB);

            UpdateButtons();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SaveSettingsFromUi();
            base.OnClosing(e);
        }

        // ---------------- 设置来回 ----------------

        private void ApplySettingsToUi()
        {
            IgnoreCaseCheck.IsChecked = _settings.IgnoreCase;
            IgnoreTrimCheck.IsChecked = _settings.IgnoreTrim;
            EmptySameCheck.IsChecked = _settings.EmptyEqualsMissing;
            IgnoreBlankCheck.IsChecked = _settings.IgnoreBlankRows;
            ToleranceBox.Text = _settings.NumberTolerance.ToString("0.####", CultureInfo.InvariantCulture);

            if (string.Equals(_settings.Mode, "ByPosition", StringComparison.OrdinalIgnoreCase)) ModePositionRadio.IsChecked = true;
            else if (string.Equals(_settings.Mode, "ByWholeRow", StringComparison.OrdinalIgnoreCase)) ModeRowRadio.IsChecked = true;
            else ModeKeyRadio.IsChecked = true;

            if (_settings.ManualColumns != null && _settings.ManualColumns.Count > 0)
                _manualMaps = _settings.ManualColumns;

            PathABox.Text = _settings.PathA ?? string.Empty;
            PathBBox.Text = _settings.PathB ?? string.Empty;

            UpdateKeyComboState();
        }

        private void SaveSettingsFromUi()
        {
            _settings.IgnoreCase = IgnoreCaseCheck.IsChecked == true;
            _settings.IgnoreTrim = IgnoreTrimCheck.IsChecked == true;
            _settings.EmptyEqualsMissing = EmptySameCheck.IsChecked == true;
            _settings.IgnoreBlankRows = IgnoreBlankCheck.IsChecked == true;
            _settings.NumberTolerance = ReadTolerance();

            _settings.Mode = ModePositionRadio.IsChecked == true ? "ByPosition"
                           : ModeRowRadio.IsChecked == true ? "ByWholeRow" : "ByKey";

            _settings.PathA = PathABox.Text ?? string.Empty;
            _settings.PathB = PathBBox.Text ?? string.Empty;
            _settings.SheetA = SheetACombo.SelectedItem as string ?? string.Empty;
            _settings.SheetB = SheetBCombo.SelectedItem as string ?? string.Empty;

            _settings.KeyColumns = new List<string>();
            foreach (var name in SelectedKeyNames()) _settings.KeyColumns.Add(name);

            _settings.ManualColumns = _manualMaps ?? new List<ColumnMap>();
            App.SaveSettings();
        }

        private double ReadTolerance()
        {
            string text = (ToleranceBox.Text ?? string.Empty).Trim();
            if (text.Length == 0) return 0;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0) return v;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out v) && v >= 0) return v;
            return 0;
        }

        // ---------------- 主题 ----------------

        private void ThemeButton_Click(object sender, RoutedEventArgs e)
        {
            bool dark = string.Equals(_settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);
            _settings.Theme = dark ? "light" : "dark";
            App.ApplyTheme(Theme.Get(_settings.Theme));
            UpdateThemeButton();
            App.SaveSettings();
            Render();   // 单元格刷的是画刷，换主题要重建
        }

        private void UpdateThemeButton()
        {
            bool dark = string.Equals(_settings.Theme, "dark", StringComparison.OrdinalIgnoreCase);
            ThemeButton.Content = dark ? "浅色模式" : "深色模式";
        }

        // ---------------- 选文件 ----------------

        private void BrowseA_Click(object sender, RoutedEventArgs e) => BrowseFile('A');
        private void BrowseB_Click(object sender, RoutedEventArgs e) => BrowseFile('B');

        private void BrowseFile(char side)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要" + (side == 'A' ? "比对的" : "对照的") + "表格",
                Filter = "表格文件|*.xlsx;*.xlsm;*.csv;*.tsv|Excel 工作簿|*.xlsx;*.xlsm|CSV / TSV|*.csv;*.tsv|所有文件|*.*",
                CheckFileExists = true
            };

            string current = side == 'A' ? PathABox.Text : PathBBox.Text;
            if (!string.IsNullOrEmpty(current))
            {
                try { dialog.InitialDirectory = Path.GetDirectoryName(current); } catch { }
            }

            if (dialog.ShowDialog(this) == true)
                _ = LoadFileAsync(side, dialog.FileName, null);
        }

        private async Task LoadFileAsync(char side, string path, string preferredSheet)
        {
            MessageBox_IfUnsupported(path);
            if (!TableReader.IsSupported(path)) return;

            try
            {
                Status("正在打开 " + Path.GetFileName(path) + " …");
                var names = await Task.Run(() => TableReader.ListSheetNames(path));

                var combo = side == 'A' ? SheetACombo : SheetBCombo;
                var box = side == 'A' ? PathABox : PathBBox;
                var info = side == 'A' ? InfoA : InfoB;

                _loading = true;
                box.Text = path;
                combo.ItemsSource = names;
                int index = 0;
                if (!string.IsNullOrEmpty(preferredSheet))
                {
                    int hit = names.IndexOf(preferredSheet);
                    if (hit >= 0) index = hit;
                }
                combo.SelectedIndex = names.Count > 0 ? index : -1;
                info.Text = names.Count + " 个工作表";
                _loading = false;

                _manualMaps = null;
                UpdateKeyComboState();
                await RefreshKeyCombosAsync();
                Status("已选择：" + Path.GetFileName(path));
                App.SaveSettings();
            }
            catch (FriendlyException ex)
            {
                _loading = false;
                Status(ex.Message);
                MessageBox.Show(ex.Message, "打不开这个文件", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                _loading = false;
                Log.Error("打开文件失败: " + path, ex);
                Status("打开失败：" + ex.Message);
            }
        }

        private void MessageBox_IfUnsupported(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            if (ext == ".xls")
            {
                string message = "这个格式读不了（.xls 是 Excel 97-2003 老格式）。\n请在 Excel 里打开后「另存为」xlsx，再回来比对。";
                Status(message.Replace("\n", " "));
                MessageBox.Show(message, "格式不支持", MessageBoxButton.OK, MessageBoxImage.Information);
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

            if (files.Length >= 2)
            {
                _ = LoadFileAsync('A', files[0], null);
                _ = LoadFileAsync('B', files[1], null);
            }
            else
            {
                // 只有一个文件：哪边空就填哪边
                bool aEmpty = string.IsNullOrWhiteSpace(PathABox.Text) || !File.Exists(PathABox.Text);
                _ = LoadFileAsync(aEmpty ? 'A' : 'B', files[0], null);
            }
        }

        // ---------------- 下拉与选项变化 ----------------

        private void SheetA_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            _ = RefreshKeyCombosAsync();
            App.SaveSettings();
        }

        /// <summary>读表 A 的表头填主键下拉（只读第一行，不读整表）。</summary>
        private async System.Threading.Tasks.Task RefreshKeyCombosAsync()
        {
            string path = PathABox.Text;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                RebuildKeyCombos(null);
                return;
            }

            try
            {
                string sheet = SheetACombo.SelectedItem as string;
                var headers = await System.Threading.Tasks.Task.Run(() => TableReader.ReadHeaders(path, sheet));
                _headersA = new List<string>(headers);
                RebuildKeyCombos(headers);
            }
            catch (Exception ex)
            {
                Log.Error("读取表头失败", ex);
                RebuildKeyCombos(null);
            }
        }

        private void SheetB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            App.SaveSettings();
        }

        private void Mode_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            UpdateKeyComboState();
        }

        private void Option_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            UpdateButtons();
        }

        private void Tolerance_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
        }

        private void UpdateKeyComboState()
        {
            bool byKey = ModeKeyRadio.IsChecked == true;
            Key1Combo.IsEnabled = byKey;
            Key2Combo.IsEnabled = byKey;
            Key3Combo.IsEnabled = byKey;
            MapButton.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(PathABox.Text) && !string.IsNullOrWhiteSpace(PathBBox.Text);
        }

        private void UpdateButtons()
        {
            bool ready = !_busy
                         && !string.IsNullOrWhiteSpace(PathABox.Text)
                         && !string.IsNullOrWhiteSpace(PathBBox.Text)
                         && File.Exists(PathABox.Text) && File.Exists(PathBBox.Text);

            CompareButton.IsEnabled = ready;
            CancelButton.IsEnabled = _busy;
            ExportCsvButton.IsEnabled = !_busy && _result != null;
            ExportXlsxButton.IsEnabled = !_busy && _result != null;
            MapButton.IsEnabled = !_busy && ready;
            Grid.IsEnabled = !_busy;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Progress.Value = 0;
            UpdateButtons();
        }

        private void Status(string message)
        {
            StatusText.Text = message ?? string.Empty;
        }

        /// <summary>快捷键：Ctrl+O 选 A、Ctrl+Shift+O 选 B、F5 重新比对、Ctrl+E / Ctrl+Shift+E 导出。</summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (ctrl && e.Key == Key.O)
            {
                BrowseFile(shift ? 'B' : 'A');
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5 && CompareButton.IsEnabled)
            {
                Compare_Click(sender, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

            if (ctrl && e.Key == Key.E)
            {
                if (shift && ExportXlsxButton.IsEnabled) ExportXlsx_Click(sender, new RoutedEventArgs());
                else if (!shift && ExportCsvButton.IsEnabled) ExportCsv_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}
