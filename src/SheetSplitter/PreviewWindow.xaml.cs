using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SheetSplitter.Core;

namespace SheetSplitter
{
    /// <summary>执行前看一眼源表长什么样（最多 100 行）。</summary>
    public partial class PreviewWindow : Window
    {
        public PreviewWindow(SheetData sheet, int maxRows = 100)
        {
            InitializeComponent();

            if (sheet == null)
            {
                HintText.Text = "没有可预览的内容。";
                return;
            }

            int shown = Math.Min(maxRows, sheet.Rows.Count);
            HintText.Text = sheet.Name + " · 共 " + sheet.Rows.Count + " 行 " + sheet.ColumnCount + " 列，这里显示前 " + shown + " 行。";

            for (int i = 0; i < sheet.ColumnCount; i++)
            {
                Grid.Columns.Add(new DataGridTextColumn
                {
                    Header = sheet.Columns[i],
                    Binding = new Binding("Cells[" + i + "]"),
                    Width = new DataGridLength(140),
                    MinWidth = 80
                });
            }

            var rows = new List<TableRow>();
            for (int i = 0; i < shown; i++) rows.Add(sheet.Rows[i]);
            Grid.ItemsSource = rows;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
