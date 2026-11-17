using System;
using System.Collections.Generic;
using System.Windows;
using TableDiff.Core;

namespace TableDiff
{
    /// <summary>映射表里的一行（给 ListBox 绑）。</summary>
    public sealed class MapRow
    {
        public string Name { get; set; }
        public List<string> Choices { get; set; }
        /// <summary>选中项下标：0 = 不映射，1..n 对应表 B 的第 1..n 列。</summary>
        public int ChoiceIndex { get; set; }
        public bool Compare { get; set; }
        public int IndexA { get; set; }
    }

    public partial class MappingWindow : Window
    {
        private const string NoMapping = "（不映射）";

        private readonly List<string> _headersA;
        private readonly List<string> _headersB;

        /// <summary>用户点「确定」后的结果。</summary>
        public List<ColumnMap> Result { get; private set; }

        public MappingWindow(List<ColumnMap> maps, List<string> headersA, List<string> headersB)
        {
            InitializeComponent();
            _headersA = headersA ?? new List<string>();
            _headersB = headersB ?? new List<string>();

            Load(maps);
        }

        private void Load(List<ColumnMap> maps)
        {
            var rows = new List<MapRow>();
            if (maps != null)
            {
                foreach (var m in maps)
                {
                    var choices = new List<string> { NoMapping };
                    choices.AddRange(_headersB);
                    rows.Add(new MapRow
                    {
                        Name = m.Name,
                        Choices = choices,
                        ChoiceIndex = m.IndexB >= 0 && m.IndexB + 1 < choices.Count ? m.IndexB + 1 : 0,
                        Compare = m.Compare,
                        IndexA = m.IndexA
                    });
                }
            }

            MapList.ItemsSource = null;
            MapList.ItemsSource = rows;

            HintText.Text = rows.Count == 0
                ? "还没读到列名，请先回到主界面选好两个文件。"
                : "共 " + rows.Count + " 列；取消勾选「比对」的列只显示、不比差异。";
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            if (_headersA.Count == 0 || _headersB.Count == 0)
            {
                HintText.Text = "没有读到列名，没法自动匹配。";
                return;
            }
            Load(ColumnMapper.ByName(_headersA, _headersB));
            HintText.Text = "已按表头名重新匹配。";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var list = new List<ColumnMap>();
            if (MapList.ItemsSource is List<MapRow> rows)
            {
                foreach (var row in rows)
                {
                    list.Add(new ColumnMap
                    {
                        Name = row.Name,
                        IndexA = row.IndexA,
                        IndexB = row.ChoiceIndex <= 0 ? -1 : row.ChoiceIndex - 1,
                        Compare = row.Compare
                    });
                }
            }
            Result = list;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
