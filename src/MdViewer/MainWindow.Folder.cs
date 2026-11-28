using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MdViewer.Core;

namespace MdViewer
{
    /// <summary>文件树的一个节点。子项懒加载：展开才去读目录，大文件夹不会一打开就卡住。</summary>
    public sealed class FileNode : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsFolder { get; set; }

        /// <summary>Segoe MDL2 图标码位：文件夹 / 文档。</summary>
        public string Glyph
        {
            get { return IsFolder ? "\uE8B7" : "\uE8A5"; }
        }

        public ObservableCollection<FileNode> Children { get; private set; }

        private bool _isExpanded;

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                Raise("IsExpanded");
            }
        }

        /// <summary>子项是否已经读过了（空文件夹也算读过）。</summary>
        public bool Loaded { get; set; }

        public FileNode()
        {
            Children = new ObservableCollection<FileNode>();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 左侧「文件」页签：把一个文件夹当书库浏览。
    ///
    /// 只列 .md / .epub / .txt 和子目录 —— 把整个目录的 png、bin、obj 都列出来没有意义，
    /// 只会让人找不到要读的那篇。目录变化用 FileSystemWatcher 跟着刷（带防抖）。
    /// </summary>
    public partial class MainWindow
    {
        private static readonly string[] BrowsableExtensions =
        {
            ".md", ".markdown", ".mdown", ".txt", ".epub"
        };

        private readonly ObservableCollection<FileNode> _folderRoots = new ObservableCollection<FileNode>();
        private FileSystemWatcher _folderWatcher;
        private DispatcherTimer _folderRefreshTimer;

        /// <summary>当前树里展开着的目录，刷新后按这个恢复（不然每次改文件树都塌回去）。</summary>
        private readonly HashSet<string> _expandedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void InitFolderPanel()
        {
            _folderRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            _folderRefreshTimer.Tick += (s, e) =>
            {
                _folderRefreshTimer.Stop();
                ReloadFolderTree();
            };

            if (FileTree != null)
            {
                FileTree.ItemsSource = _folderRoots;
                // TreeView 自己没有 Expanded 事件，得挂在 TreeViewItem 的路由事件上
                FileTree.AddHandler(TreeViewItem.ExpandedEvent,
                    new RoutedEventHandler(FileTree_Expanded));
            }
            UpdateFolderHint();
        }

        private void UpdateFolderHint()
        {
            if (FolderEmptyHint == null) return;

            bool hasRoot = !string.IsNullOrWhiteSpace(App.Settings.FolderRoot) && Directory.Exists(App.Settings.FolderRoot);
            FolderEmptyHint.Visibility = hasRoot ? Visibility.Collapsed : Visibility.Visible;

            if (PickFolderButton != null)
                PickFolderButton.ToolTip = hasRoot
                    ? App.Settings.FolderRoot
                    : "把一个文件夹当书库，双击里面的文档就能读";

            // 文件页签的标题显示书库名，比一直写"文件"有用
            if (TabFiles != null && TabFiles.IsChecked == true && TocTitle != null)
                TocTitle.Text = FolderTitleText();
        }

        /// <summary>文件页签标题：有书库就是文件夹名，否则"文件"。</summary>
        private static string FolderTitleText()
        {
            string root = App.Settings.FolderRoot;
            if (string.IsNullOrWhiteSpace(root)) return "文件";

            string name = Path.GetFileName(root.TrimEnd('\\', '/'));
            return string.IsNullOrWhiteSpace(name) ? root.TrimEnd('\\', '/') : name;
        }

        /// <summary>选一个文件夹当书库。.NET 10 的 WPF 自带 OpenFolderDialog，不用 WinForms 兜底。</summary>
        private void PickFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择一个文件夹",
                Multiselect = false
            };

            string current = App.Settings.FolderRoot;
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
                dialog.InitialDirectory = current;

            if (dialog.ShowDialog(this) != true) return;

            App.Settings.FolderRoot = dialog.FolderName;
            App.SaveSettings();
            ReloadFolderTree();
            SwitchSidebarTab("files");
        }

        /// <summary>重建整棵树，尽量恢复展开状态。</summary>
        private void ReloadFolderTree()
        {
            if (FileTree == null) return;

            CaptureExpanded(_folderRoots);
            _folderRoots.Clear();

            string root = App.Settings.FolderRoot;
            UpdateFolderHint();

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                StopFolderWatcher();
                return;
            }

            try
            {
                var node = new FileNode
                {
                    Name = Path.GetFileName(root.TrimEnd('\\')),
                    Path = root,
                    IsFolder = true,
                    IsExpanded = true
                };
                LoadChildren(node);
                _folderRoots.Add(node);

                RestoreExpanded(_folderRoots);
                ExpandToCurrentDocument(root);
                StartFolderWatcher(root);
            }
            catch (Exception ex)
            {
                Log.Error("读取文件夹失败：" + root, ex);
                StatusRight.Text = "读文件夹失败：" + ex.Message;
            }
        }

        /// <summary>只用于根节点：把根的直接子项读出来。</summary>
        private void LoadChildren(FileNode parent)
        {
            if (parent == null || !parent.IsFolder || parent.Loaded) return;

            parent.Loaded = true;
            parent.Children.Clear();

            try
            {
                var dir = new DirectoryInfo(parent.Path);
                var folders = new List<FileNode>();
                var files = new List<FileNode>();

                foreach (DirectoryInfo sub in dir.GetDirectories())
                {
                    // 跳过隐藏 / 系统目录（.git、node_modules 也不是给人读的）
                    if ((sub.Attributes & FileAttributes.Hidden) != 0) continue;
                    if (string.Equals(sub.Name, "node_modules", StringComparison.OrdinalIgnoreCase)) continue;

                    folders.Add(new FileNode { Name = sub.Name, Path = sub.FullName, IsFolder = true });
                }

                foreach (FileInfo file in dir.GetFiles())
                {
                    string ext = file.Extension.ToLowerInvariant();
                    if (Array.IndexOf(BrowsableExtensions, ext) < 0) continue;
                    files.Add(new FileNode { Name = file.Name, Path = file.FullName, IsFolder = false });
                }

                folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

                foreach (var item in folders) parent.Children.Add(item);
                foreach (var item in files) parent.Children.Add(item);

                // 给文件夹塞一个占位子项：TreeView 才会画展开箭头
                foreach (var item in folders)
                {
                    if (HasBrowsableContent(item.Path)) item.Children.Add(new FileNode());
                }
            }
            catch (Exception ex)
            {
                Log.Error("读取目录失败：" + parent.Path, ex);
            }
        }

        private static bool HasBrowsableContent(string dir)
        {
            try
            {
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    var info = new DirectoryInfo(sub);
                    if ((info.Attributes & FileAttributes.Hidden) != 0) continue;
                    return true;
                }
                foreach (string file in Directory.GetFiles(dir))
                {
                    if (Array.IndexOf(BrowsableExtensions, System.IO.Path.GetExtension(file).ToLowerInvariant()) >= 0)
                        return true;
                }
            }
            catch (Exception)
            {
                // 没权限就当空的
            }
            return false;
        }

        private void FileTree_Expanded(object sender, RoutedEventArgs e)
        {
            var item = e.OriginalSource as TreeViewItem;
            if (item == null) return;
            var node = item.DataContext as FileNode;
            if (node == null || !node.IsFolder) return;

            _expandedFolders.Add(node.Path);

            if (node.Loaded) return;

            // 先清掉占位子项，再真读
            node.Loaded = true;
            node.Children.Clear();
            LoadChildrenInto(node);
        }

        /// <summary>展开时加载子项（LoadChildren 会自己判 Loaded，这里单独提出来是为了语义清楚）。</summary>
        private void LoadChildrenInto(FileNode node)
        {
            node.Loaded = false;
            LoadChildren(node);

            foreach (var child in node.Children)
            {
                if (child.IsFolder && HasBrowsableContent(child.Path))
                {
                    // LoadChildren 只给「当前层」塞占位，孙层要再补一次
                    if (child.Children.Count == 0) child.Children.Add(new FileNode());
                }
            }
        }

        private void CaptureExpanded(IEnumerable<FileNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder) continue;
                if (node.IsExpanded) _expandedFolders.Add(node.Path);
                else _expandedFolders.Remove(node.Path);
                if (node.Loaded) CaptureExpanded(node.Children);
            }
        }

        private void RestoreExpanded(IEnumerable<FileNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (!node.IsFolder) continue;
                if (!_expandedFolders.Contains(node.Path)) continue;

                if (!node.Loaded) LoadChildrenInto(node);
                node.IsExpanded = true;
                RestoreExpanded(node.Children);
            }
        }

        /// <summary>打开文档后把树展开到它所在的位置，选中那一行。</summary>
        private void ExpandToCurrentDocument(string root)
        {
            if (string.IsNullOrEmpty(_currentPath) || _folderRoots.Count == 0) return;
            if (!_currentPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;

            var chain = new List<string>();
            string dir = Path.GetDirectoryName(_currentPath);
            while (!string.IsNullOrEmpty(dir) && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                   && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
            {
                chain.Insert(0, dir);
                dir = Path.GetDirectoryName(dir);
            }

            FileNode current = _folderRoots[0];
            foreach (string folder in chain)
            {
                FileNode next = null;
                foreach (var child in current.Children)
                {
                    if (child.IsFolder && string.Equals(child.Path, folder, StringComparison.OrdinalIgnoreCase))
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null) break;
                if (!next.Loaded) LoadChildrenInto(next);
                next.IsExpanded = true;
                current = next;
            }

            foreach (var child in current.Children)
            {
                if (!child.IsFolder && NoteStore.SamePath(child.Path, _currentPath))
                {
                    child.IsExpanded = true;
                    break;
                }
            }
        }

        private void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = e.OriginalSource as DependencyObject;
            while (item != null && !(item is TreeViewItem)) item = System.Windows.Media.VisualTreeHelper.GetParent(item);
            var treeItem = item as TreeViewItem;
            if (treeItem == null) return;

            var node = treeItem.DataContext as FileNode;
            if (node == null || node.IsFolder) return;

            e.Handled = true;
            OpenFile(node.Path, true);
        }

        private void FileTree_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var node = FileTree.SelectedItem as FileNode;
            if (node == null || node.IsFolder) return;
            e.Handled = true;
            OpenFile(node.Path, true);
        }

        // ───────────────────────── 目录变化监听 ─────────────────────────

        private void StartFolderWatcher(string root)
        {
            StopFolderWatcher();
            try
            {
                _folderWatcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 32 * 1024
                };
                _folderWatcher.Created += FolderChanged;
                _folderWatcher.Deleted += FolderChanged;
                _folderWatcher.Renamed += FolderChanged;
                _folderWatcher.Error += FolderWatcherError;
                _folderWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.Error("监听文件夹失败：" + root, ex);
            }
        }

        private void StopFolderWatcher()
        {
            if (_folderWatcher == null) return;
            try
            {
                _folderWatcher.EnableRaisingEvents = false;
                _folderWatcher.Dispose();
            }
            catch (Exception) { }
            _folderWatcher = null;
        }

        /// <summary>监听出错（缓冲区溢出 / 目录被删）时重启，别让文件树悄悄停止刷新。</summary>
        private void FolderWatcherError(object sender, ErrorEventArgs e)
        {
            Log.Error("文件树监听出错", e.GetException());
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string root = App.Settings.FolderRoot;
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root)) StartFolderWatcher(root);
            }));
        }

        private void FolderChanged(object sender, FileSystemEventArgs e)
        {
            // 只关心「多了 / 少了」文档或目录，改内容不影响树形
            string ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            bool relevant = e is RenamedEventArgs || Directory.Exists(e.FullPath)
                            || Array.IndexOf(BrowsableExtensions, ext) >= 0;
            if (!relevant) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 一次批量操作会连发几十个事件，攒一下再刷
                _folderRefreshTimer.Stop();
                _folderRefreshTimer.Start();
            }));
        }
    }
}
