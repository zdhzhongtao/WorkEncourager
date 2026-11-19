# WadeToolbox · 日常工作小工具箱

一堆**装了就能用**的 Windows 小工具，不需要会编程，不需要注册账号，不联网也不上传任何数据。
每个工具独立开发、独立发布、独立使用，放在同一个代码库里只是为了少开几个仓库。

> 目标不是做「开发者工具」，而是做**办公室里每个人都会遇到的麻烦事**：
> 算钱、对表、整理文件、批量改名、导出报表、开会记东西。
> 你在嘉士伯做的那些重复劳动，能自动化的就做成一个工具丢进来。

---

## 工具清单

| 工具 | 一句话说明 | 形态 | 状态 |
| --- | --- | --- | --- |
| [上班鼓励器 WorkEncourager](src/WorkEnc/README.md) | 桌面悬浮窗，实时算出今天已经赚了多少钱，顺带记工时报表 | WPF 桌面程序 | ✅ v2.2.2 |
| [表格比对器 TableDiff](src/TableDiff/README.md) | 比两个 xlsx / csv 的差异：按键 / 按位置 / 按整行，导出 CSV 或高亮 xlsx | WPF 桌面程序 | 🚧 v0.1.1 |
| [表格拆合器 SheetSplitter](src/SheetSplitter/README.md) | 按列值拆分、多文件合并、多 sheet 合并、按行数拆分，源文件只读 | WPF 桌面程序 | 🚧 v0.1.1 |
| [财务小算盘 MoneyCalc](src/MoneyCalc/README.md) | 人民币大写、工作日计算、含税换算、日期推算、汇率换算 | WPF 桌面程序 | 🚧 v0.1.1 |
| [Markdown 阅读器 MdViewer](src/MdViewer/README.md) | 双击 .md / .epub 直接打开，20 套主题 + 公式图表 + 标注书签 + 朗读 + 多标签 + 导出 PDF/长图，自带安装脚本 | WPF 桌面程序 | ✅ v0.5.2 |

> 新增工具请同时在 `WadeToolbox.slnx` 里加一行、在这张表里加一行。

---

## 目录结构

```
WadeToolbox/
├── README.md                      本文件：库总览与工具清单
├── WadeToolbox.slnx               全库解决方案（VS 双击打开，一次加载所有工具）
├── .gitignore                     库级忽略规则（构建产物 / 本地配置 / 数据文件）
├── .github/workflows/
│   ├── ci-workenc.yml             上班鼓励器：编译 + 179 个单元测试 + 发布 exe
│   ├── ci-tablediff.yml           表格比对器：编译 + 64 个单元测试 + 发布 exe
│   ├── ci-sheetsplitter.yml       表格拆合器：编译 + 38 个单元测试 + 发布 exe
│   ├── ci-moneycalc.yml           财务小算盘：编译 + 89 个单元测试 + 发布 exe
│   ├── ci-mdviewer.yml            Markdown 阅读器：编译 + 217 个单元测试 + 发布 exe
│   └── (未来) ci-<工具名>.yml      每个工具一条独立流水线，互不干扰
├── docs/
│   ├── 库结构说明.md               这套目录怎么用、约定是什么
│   ├── 新增工具指南.md             从零加一个工具的标准动作（含模板）
│   ├── 共享约定.md                 命名、版本号、界面风格、数据存放位置
│   └── 四个小工具开发规格.md        本轮四个工具的需求与验收清单
└── src/
    ├── _template/                 新工具起点模板，复制一份就能开工
    ├── WorkEnc/                   上班鼓励器（完整独立项目，含自己的 docs）
    │   ├── WorkEncourager.csproj  WPF 主程序
    │   ├── WorkEnc.Core/          纯逻辑类库（无 UI 依赖，可测）
    │   ├── WorkEnc.Tests/         xUnit 单元测试
    │   └── docs/                  使用说明 + 开发记录
    ├── TableDiff/                 表格比对器（同上三项目结构）
    ├── SheetSplitter/             表格拆合器
    ├── MoneyCalc/                 财务小算盘
    └── MdViewer/                  Markdown 阅读器
```

### 为什么这样分

- **`src/` 下每个工具一个文件夹，互不引用**。想删哪个工具，直接删文件夹，不影响别的。
- **每个工具自带 `.csproj` 和自己的测试项目**。构建、发版、CI 都是工具级的事，库级只负责把它们串起来。
- **`docs/` 放「跨越所有工具」的规矩**，工具自己的说明书放在工具文件夹里的 `docs/`，不要混。
- **数据不放在仓库里**。工具的运行数据统一写到 `%APPDATA%\<工具名>\`，仓库只放源码。

---

## 怎么开始

### 用现成的工具
去对应工具的 `README.md` 看安装方式。上班鼓励器 `v2.2.2`；表格那三个是 `v0.1.1`（初版 + 一轮自检修复）；Markdown 阅读器 `v0.5.2`（20 套主题 + 三槽字体 + 公式图表 + 标注书签 + 朗读 + 文件树 + 多标签 + 导出长图）。阅读器出两种包：`local` 约 2 MB（依赖本机 .NET 10 桌面运行时）、`portable` 约 62 MB（自带运行时，任何人都能双击跑）。
本仓库现在不挂 Release，需要安装包就在 `src/<工具>` 下用对应的 `publish.ps1` 自己出（产物落到 `dist/`，不入库）。
每个工具都能用 `--demo` 或 `--maketestdata` 自查一遍（见各自 README 的「自检」）。

### 加一个新工具
看 [docs/新增工具指南.md](docs/新增工具指南.md)，三步：
1. `src/_template` 复制成 `src/<新工具名>`
2. 改 csproj 里的 `AssemblyName` / 产品名
3. 往 `WadeToolbox.slnx` 和上面那张表里各加一行

### 编译与测试
```bash
# 编译整个库
dotnet build WadeToolbox.slnx -c Release

# 只跑某个工具的测试
dotnet test src/WorkEnc/WorkEnc.Tests/WorkEnc.Tests.csproj
```

---

## 设计原则（写新工具时照着做）

1. **零配置可用** — 装上就能用，默认值要合理，设置项是给想改的人准备的。
2. **不联网** — 除了「检查更新」这种用户主动点的动作，不发起任何网络请求。
3. **数据本地化** — 全部存 `%APPDATA%`，不上传、不需要账号、不需要数据库。
4. **写得糙不如写得稳** — 写文件用「临时文件 + 原子替换 + .bak」，崩溃不丢数据。
5. **单文件可分发** — 最终产物是一个 exe，发给同事不用装运行时（或明确告知要装 .NET）。
6. **随时能退** — 关窗口不等于退出、误点不丢数据；有个能真正退出的明确入口。

---

## 技术栈

统一 .NET 10 + WPF（Windows 桌面），能复用就复用：
- 界面：WPF + 手写 XAML（不用第三方控件库，减少体积和许可问题）
- 托盘：WinForms `NotifyIcon`
- 测试：xUnit
- 发布：`dotnet publish` 单文件

> 不是所有工具都必须是 WPF。批处理类、CLI 类工具可以直接用控制台项目，只要能双击运行或做成快捷方式即可。

## License

MIT
