# 财务小算盘 MoneyCalc

五个日常财务小计算：人民币大写、工作日、含税/不含税、日期推算、汇率换算。输入即出结果，没有「计算」按钮，不联网。

- 版本：`0.1.1`
- 形态：WPF 桌面程序，单文件 exe，无需安装
- 数据目录：`%APPDATA%\MoneyCalc\`
- 联网：**完全不联网**（汇率要你自己填）

## 五个计算器

| 计算器 | 能干什么 |
| --- | --- |
| 人民币大写 | `1234567.89` → 壹佰贰拾叁万肆仟伍佰陆拾柒元捌角玖分；支持负数、千分位、一键复制 |
| 工作日计算 | 某天起过 N 个工作日是哪天；两日期间几个工作日（含/不含端点）；自然日/整月/整年同时给；节假日表可编辑 |
| 含税 / 不含税 | 13/9/6/5/3/1% 快捷键 + 任意小数税率；双向换算；三行结果 2 位小数，另给 4 位高精度 |
| 日期计算 | 两日期差（天/工作日/整月/整年）；加 N 天/月/年（月末对齐：1-31 + 1 月 = 2-28） |
| 汇率换算 | 手输汇率双向换算，4 位小数；常用汇率存最近 20 条，点一条即填 |

## 用法

1. 双击 `MoneyCalc.exe`
2. 左边选一个计算器，右边直接填数字
3. 结果区可以「复制」，焦点不在输入框时 `Ctrl+C` 也能复制
4. `Enter` 跳到下一个输入框

详细说明见 [docs/使用说明.md](docs/使用说明.md)，设计与踩坑见 [docs/开发记录.md](docs/开发记录.md)。

## 自检

```bash
MoneyCalc.exe --demo
```

弹出四条手算核对过的结果（大写两条、含税换算一条、月末对齐一条）。

## 开发

```bash
dotnet build src/MoneyCalc/MoneyCalc.csproj -c Release
dotnet test  src/MoneyCalc/MoneyCalc.Tests/MoneyCalc.Tests.csproj
dotnet publish src/MoneyCalc/MoneyCalc.csproj -c Release -r win-x64 ^
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

| 项目 | 说明 |
| --- | --- |
| `MoneyCalc.csproj` | WPF 界面（左导航 + 五个面板） |
| `MoneyCalc.Core/` | 纯计算，**零依赖**（连 ClosedXML 都没有）；93 个用例 |
| `MoneyCalc.Tests/` | xUnit |

## 准确性的边界（请读）

- **节假日表**：内置 2025–2026 中国法定节假日与调休，**数据截止 2026-11**，界面上有提示，请自行核对；可一键清空只按周一到周五算。
- **汇率**：不抓行情，全靠手输；倒数只保留 4 位小数，来回换算可能差几分钱。
- **含税换算**：先算基准值，另一个用加减法得出，保证「不含税 + 税额 = 含税」，不会差一分。
