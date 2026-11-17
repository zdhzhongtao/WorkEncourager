# 表格比对器 TableDiff

两张 Excel / CSV 按业务主键找出「多了哪些行、少了哪些行、哪些字段被改过」，差异用黄色高亮，一键导出差异清单（CSV）和差异报告（xlsx）。

- 版本：`0.1.1`
- 形态：WPF 桌面程序，单文件 exe，无需安装
- 数据目录：`%APPDATA%\TableDiff\`
- 联网：**不联网**，不发任何请求

## 功能

| 功能 | 说明 |
| --- | --- |
| 导入 | `.xlsx` `.xlsm` `.csv` `.tsv`；两个文件槽都支持拖放；多 sheet 可选 |
| 比对方式 | 按主键（1~3 列复合键）/ 按行位置 / 按整行内容 |
| 列映射 | 默认按表头名自动匹配，可手动指定「A 哪列对 B 哪列」、可勾掉不参与比对的列 |
| 选项 | 忽略大小写、忽略首尾空格、空单元格与空串视为相同、忽略全空行、数值容差（默认 0） |
| 结果 | 汇总条 + 表格；差异单元格显示「旧值 → 新值」，黄色底；仅A红字、仅B绿字 |
| 导出 | `差异清单_yyyyMMdd_HHmm.csv`（UTF-8 带 BOM）、`差异报告.xlsx`（汇总 + 差异明细，保留高亮） |
| 其它 | 浅色/深色切换、只看差异、按任意列搜索、后台比对可取消 |

## 用法

1. 双击 `TableDiff.exe`
2. 把两张表分别拖进上方的「表 A」「表 B」卡片刻（或点「浏览…」）
3. 选好比对方式和主键列（默认取第 1 列，记得改成真正的业务主键，比如「单据号」）
4. 点「开始比对」，结果出来后点「导出差异清单…」或「导出差异报告…」

详细说明见 [docs/使用说明.md](docs/使用说明.md)，设计与踩坑见 [docs/开发记录.md](docs/开发记录.md)。

## 验收用样例数据

```bash
TableDiff.exe --maketestdata C:\Temp\td
```

会在 `C:\Temp\td` 下生成 `表A.xlsx`（200 行）和 `表B.xlsx`（201 行），预期结果：
**字段变更 3 行（3 个单元格）/ 仅A有 1 行 / 仅B有 2 行**。

## 开发

```bash
dotnet build src/TableDiff/TableDiff.csproj -c Release
dotnet test  src/TableDiff/TableDiff.Tests/TableDiff.Tests.csproj
dotnet publish src/TableDiff/TableDiff.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

三个项目：

| 项目 | 说明 |
| --- | --- |
| `TableDiff.csproj` | WPF 界面（`MainWindow.xaml` + `MainWindow.Diff.cs` 结果渲染） |
| `TableDiff.Core/` | 纯逻辑：读表、比对、导出，**不引用 WPF**，可单测 |
| `TableDiff.Tests/` | xUnit，85 个用例 |

## 数据与隐私

- 配置：`%APPDATA%\TableDiff\settings.json`（带 `Version` 字段，原子写 + `.bak`）
- 日志：`%APPDATA%\TableDiff\app.log`（512 KB 轮转）
- 不采集任何使用数据，不写注册表，不产生临时文件到程序目录
