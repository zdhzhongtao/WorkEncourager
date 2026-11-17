using System;
using System.Collections.Generic;

namespace TableDiff.Core
{
    public enum DiffMode
    {
        /// <summary>按主键（1~3 列复合键）。</summary>
        ByKey,
        /// <summary>按行位置（第 N 行对第 N 行）。</summary>
        ByPosition,
        /// <summary>按整行内容。</summary>
        ByWholeRow
    }

    public enum RowStatus
    {
        Same,
        Changed,
        OnlyA,
        OnlyB
    }

    /// <summary>一列的对齐关系：A 的第几列、B 的第几列、显示名、是否参与比对。</summary>
    public sealed class ColumnMap
    {
        public string Name { get; set; } = string.Empty;
        public int IndexA { get; set; } = -1;
        public int IndexB { get; set; } = -1;
        public bool Compare { get; set; } = true;

        public ColumnMap() { }

        public ColumnMap(string name, int indexA, int indexB, bool compare = true)
        {
            Name = name;
            IndexA = indexA;
            IndexB = indexB;
            Compare = compare;
        }

        public ColumnMap Clone() => new ColumnMap(Name, IndexA, IndexB, Compare);
    }

    /// <summary>比对选项。默认值就是"零配置可用"的那一套。</summary>
    public sealed class DiffOptions
    {
        public DiffMode Mode { get; set; } = DiffMode.ByKey;

        /// <summary>主键列（索引指向 <see cref="Columns"/>，不是指向原表）。</summary>
        public List<int> KeyColumns { get; } = new List<int>();

        /// <summary>已对齐的列顺序。</summary>
        public List<ColumnMap> Columns { get; } = new List<ColumnMap>();

        public bool IgnoreCase { get; set; }
        public bool IgnoreTrim { get; set; } = true;
        public bool EmptyEqualsMissing { get; set; } = true;
        public bool IgnoreBlankRows { get; set; } = true;
        public double NumberTolerance { get; set; }

        public ColumnMap[] EffectiveColumns()
        {
            return Columns.Count > 0 ? Columns.ToArray() : new ColumnMap[0];
        }
    }

    public sealed class CellDiff
    {
        public string ValueA { get; set; } = string.Empty;
        public string ValueB { get; set; } = string.Empty;
        public bool Changed { get; set; }
    }

    public sealed class DiffRow
    {
        public RowStatus Status { get; set; }
        public string Key { get; set; } = string.Empty;
        public CellDiff[] Cells { get; set; } = new CellDiff[0];
        /// <summary>A 侧的原始行号（0 表示只有 B 有）。</summary>
        public int SourceRowA { get; set; }
        /// <summary>B 侧的原始行号（0 表示只有 A 有）。</summary>
        public int SourceRowB { get; set; }

        public int ChangedCellCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Cells.Length; i++) if (Cells[i].Changed) n++;
                return n;
            }
        }
    }

    /// <summary>主键重复告警。绝不静默取第一条。</summary>
    public sealed class KeyConflict
    {
        public string Key { get; set; } = string.Empty;
        public int CountA { get; set; }
        public int CountB { get; set; }

        public override string ToString()
        {
            return "「" + Key.Replace("\u0001", " + ") + "」 A 侧 " + CountA + " 条 / B 侧 " + CountB + " 条";
        }
    }

    public sealed class DiffResult
    {
        public List<string> Columns { get; } = new List<string>();
        public List<DiffRow> Rows { get; } = new List<DiffRow>();

        public int RowCountA { get; set; }
        public int RowCountB { get; set; }
        public int SameCount { get; set; }
        public int OnlyACount { get; set; }
        public int OnlyBCount { get; set; }
        public int ChangedRowCount { get; set; }
        public int ChangedCellCount { get; set; }

        public List<KeyConflict> DuplicateKeys { get; } = new List<KeyConflict>();

        /// <summary>比对过程中的提示（主键重复、跳过空行等），给界面显示。</summary>
        public List<string> Notes { get; } = new List<string>();

        public bool HasDifference => OnlyACount > 0 || OnlyBCount > 0 || ChangedRowCount > 0;
    }
}
