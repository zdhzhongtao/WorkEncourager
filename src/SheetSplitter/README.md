# 表格拆合器 SheetSplitter

一张总表按列值拆成多个文件、多个文件合成一张、一个文件的多个 sheet 合并、大表按行数切开。财务月结那点事。

- 版本：`0.1.1`
- 形态：WPF 桌面程序，单文件 exe，无需安装
- 数据目录：`%APPDATA%\SheetSplitter\`
- 联网：**不联网**

## 四个模式

| 模式 | 干什么 | 例子 |
| --- | --- | --- |
| 按列值拆分 | 按某列的值拆成多个文件 | 500 行总表按「部门」拆成 13 个文件（12 部门 + 空白） |
| 多文件合并 | 多个文件合成一张 | 12 个月的表合成一张年度表 |
| 多 sheet 合并 | 一个文件里的多张 sheet 合成一张 | 分月 sheet 合成一张 |
| 按行数拆分 | 每份最多 N 行 | 500 行按 100 行切，分批发邮件 |

## 安全设计

- **源文件只读不写**：任何模式都不会覆盖或移动源文件（有测试守着源文件的长度与修改时间）
- 输出重名**不覆盖**，自动加 `(1)(2)`
- 先写 `.tmp` 再改名，中途失败不留半个坏文件；单个文件失败不影响其余的，失败清单一起给你
- 输出目录必须用户自己选；与源文件同目录时弹警告

## 用法

1. 双击 `SheetSplitter.exe`
2. 选一个模式页签，选源文件（也可以直接把文件拖进窗口）
3. 选输出目录，点「预览计划」确认要生成哪些文件、各多少行
4. 点「开始执行」

详细说明见 [docs/使用说明.md](docs/使用说明.md)，设计与踩坑见 [docs/开发记录.md](docs/开发记录.md)。

## 验收用样例数据

```bash
SheetSplitter.exe --maketestdata C:\Temp\ss
```

生成 `C:\Temp\ss\总表.xlsx`（500 行，12 个部门，第 37 行部门为空，其中「研发/测试部」带斜杠）。
预期：按「部门」拆分 → 13 个文件，其中 `总表_空白.xlsx` 与 `总表_研发_测试部.xlsx`。

## 开发

```bash
dotnet build src/SheetSplitter/SheetSplitter.csproj -c Release
dotnet test  src/SheetSplitter/SheetSplitter.Tests/SheetSplitter.Tests.csproj
dotnet publish src/SheetSplitter/SheetSplitter.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

| 项目 | 说明 |
| --- | --- |
| `SheetSplitter.csproj` | WPF 界面（四个模式共用一个底部操作条） |
| `SheetSplitter.Core/` | 读表 / 计划 / 执行，纯逻辑不引用 WPF；读表代码是从 TableDiff 复制过来改命名空间的 |
| `SheetSplitter.Tests/` | xUnit，47 个用例（含一条跑完四个模式的验收链路） |

## 数据

- `%APPDATA%\SheetSplitter\settings.json`（带 `Version`，原子写 + `.bak`）
- `%APPDATA%\SheetSplitter\app.log`（512 KB 轮转）
