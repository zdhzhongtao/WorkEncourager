# Markdown 阅读器 MdViewer

双击 `.md` 或 `.epub` 直接打开，排版按阅读优化（正文宽度可调、行高 1.7、代码块带语法高亮和复制按钮），
左边目录侧栏，`Ctrl+F` 页内搜索，能导出 PDF / HTML / 长图，**20 套主题随时切换**（含护眼米黄、报纸衬线），
数学公式、Mermaid 图表、脚注、Task list 都能正常渲染。

- **版本**：0.5.0
- **形态**：WPF 桌面程序，单文件 exe；带安装脚本（装到用户目录 + 开始菜单 + 文件关联）
- **数据目录**：`%APPDATA%\MdViewer\`（配置、标注书签、日志、WebView2 缓存、EPUB 解压、`--demo` 样例）
- **依赖**：.NET 10；渲染用 WebView2，需要本机有 WebView2 运行时（Win11 自带，装了 Edge 的 Win10 一般也有）
- **联网**：默认不联网。全程序只有三个地方会发请求 —— 你点「去下载 WebView2」、你在设置里点「检查更新」、
  你打开了「启动时检查更新」。md 里的远程图片会被 CSP 挡掉，不会偷偷加载

## 0.5.0 做了什么

三个批次一起上，目标是"能当主力阅读器用"：

| 功能 | 说明 |
| --- | --- |
| 壳色跟着主题走 | 每套主题自带底色 / 文字色 / 边线 / 卡片色，窗口、侧栏、工具栏、按钮全部由它派生（36 个资源键）。切主题不再是"只换一个按钮颜色" |
| 主题从 10 套到 20 套 | 新增 One Light、Catppuccin Latte / Mocha、Gruvbox Light / Dark、Rosé Pine Dawn、Tokyo Night、Everforest Dark、高对比、纯黑白 |
| 主题缩略图 | 设置里的主题下拉带小色板（底色 + 边线 + 强调色），不用逐个试 |
| 字体可选 | 正文 / 代码 / 标题三个字体槽，下拉列系统字体也能直接敲名字；留空就跟随主题 |
| 定时换肤 | 设置里指定时间段（比如 19:00 → 07:00），到点自动切深色，跨夜支持；只覆盖显示，不改你的明暗偏好 |
| 无干扰阅读 | `F11` 收掉工具栏 / 状态栏 / 侧栏，只留正文，顺带打开宽屏；`Esc` 退出 |
| 外部程序编辑 | `Ctrl+Shift+E` 用 VS Code / Typora / Notepad++ / Sublime 打开当前文件（可指定，找不到退记事本） |
| 多标签 | 单 WebView 多文档，标签栏可中键关闭、右键菜单；重启恢复上次开着的标签 |
| 文件树侧栏 | 指定一个文件夹当书库，递归列 `.md` / `.epub`，改文件自动刷新（防抖 900ms） |
| 标注与书签 | 选中正文弹出气泡（高亮 / 批注 / 朗读）；书签记住位置和当时章节名；存在 `notes.json`，按文档聚合 |
| 朗读 | `Ctrl+Shift+R` 从当前位置念下去，语速可调，念到的段落会高亮 |
| 数学公式 | KaTeX 0.16.11，`$...$` 行内、`$$...$$` 块级，矩阵 / 积分 / 多行对齐都行 |
| Mermaid 图表 | Mermaid 11.4.1，流程图 / 时序图 / 类图 / 状态图，配色跟着主题变量走 |
| 代码高亮 | 换成 highlight.js 11.10，语言覆盖比原来多，色板仍然走主题变量 |
| front matter 信息卡 | 文档头的 YAML 变成一张卡（标题 / 作者 / 日期 / 标签），只显示读者关心的字段 |
| 导出长图 | `Ctrl+Shift+S` 整页 PNG，走 DevTools 的 `captureBeyondViewport`；超长自动降采样而不是失败 |
| 表格 | 点表头三态排序（升 / 降 / 还原），行数多的表纵向 sticky 表头 |
| 图片灯箱 | 点图放大，滚轮缩放、拖动、双击还原、`Esc` 关闭 |

三方库（KaTeX / Mermaid / highlight.js，共 3.2MB）是**内嵌资源**：首次运行释放到缓存目录，
页面按需 `<script src>` 加载。仍然零联网，也不会让每篇文档都背 3MB。

## 0.4.0 做了什么

| 功能 | 说明 |
| --- | --- |
| 10 套主题 | GitHub 浅 / GitHub 深 / Vue 绿 / Solarized 浅 / Solarized 深 / 护眼米黄 / 报纸衬线 / Nord / Dracula / One Dark。工具栏面板一键切换，**只换 CSS 变量不重渲染**，滚动位置不跳 |
| 界面跟着主题走 | 每套主题自带主色，「打开」按钮、目录选中项、进度条跟着变；不再出现深色正文配刺眼浅色按钮 |
| 浅深各选一套 | 设置里可以分别指定「浅色时用哪套」「深色时用哪套」，两个下拉各自只列对应明暗的主题 |
| 跟随系统实时生效 | 挂 `SystemEvents.UserPreferenceChanged`，Windows 切深浅后窗口立刻跟着变，不用重启 |
| 主题包结构 | 配色写在 `style.css` 的 `html[data-theme="<id>"]` 变量块里，主题清单在 `MarkdownTheme.cs`，加一套主题 = 加一块变量 + 加一条元数据 |

## 功能一览

| 功能 | 说明 |
| --- | --- |
| 自动刷新 | 文档被别的程序（编辑器、git、同步盘）改动后自动重新渲染，并保持你读到的那一行。可在设置里关 |
| 手动刷新 | 工具栏刷新按钮 / `F5` / 「更多 → 重新读取」 |
| 记忆阅读位置 | 同一篇文档下次打开直接跳回上次读到的位置 |
| 最近打开 | 工具栏下拉面板：搜索过滤、↑↓ 选择、Enter 打开、Del 移除、缺失文件置灰；起始页也有最近列表 |
| EPUB | 支持 EPUB 2/3：解析 OPF、nav/ncx 两种目录、按 spine 顺序合并章节、图片走本地虚拟域名加载 |
| 目录过滤 | 侧栏大纲顶部输入关键字只留匹配标题，回车跳到第一条，`Esc` 清空 |
| 设置窗口 | 外观（深浅模式、浅色/深色各用哪套主题、字号、字体、正文宽度）、阅读（自动刷新、记忆位置、目录、定时换肤、外部编辑器）、文件关联、更新、重置、关于 |
| 恢复默认设置 | 设置 → 重置：外观与阅读开关回到出厂值，最近打开、阅读位置、窗口大小、打开过的标签保留；重置前自动备份 `settings.json`（留最近 5 份） |
| 自动更新 | 从 GitHub Release 取最新版本，下载后原地替换并重启；仓库地址可在设置里改 |
| 前进后退 | `Alt+←` / `Alt+→` 在打开过的文档之间切换 |
| 两种发布 | `local` 2 MB（依赖本机 .NET 10）/ `portable` 60 MB（自带运行时） |

## 安装到本机

```powershell
.\src\MdViewer\install.ps1
```

默认 `-Mode auto`：本机有 .NET 10 桌面运行时就用 2 MB 的精简版，没有就用自带运行时的便携版。

```powershell
.\src\MdViewer\install.ps1 -Mode portable   # 强制便携版
.\src\MdViewer\install.ps1 -NoDesktop       # 不放桌面快捷方式
.\src\MdViewer\install.ps1 -KeepExisting    # 不抢 .md 默认程序，只加右键菜单
.\src\MdViewer\install.ps1 -Uninstall       # 卸载（会还原被顶掉的关联）
```

装之前会把原来的默认关联记在 `%APPDATA%\MdViewer\assoc-backup.json`，卸载时照原样还原。
只写 HKCU，不需要管理员。

## 只想要 exe

```powershell
.\src\MdViewer\publish.ps1              # 两个都出，放在 dist\
.\src\MdViewer\publish.ps1 -Mode local  # 只出 2 MB 的
```

| 产物 | 大小 | 适用 |
| --- | --- | --- |
| `MdViewer-<版本>-local.exe` | ~2 MB | 自己的机器（已装 .NET 10 桌面运行时） |
| `MdViewer-<版本>-portable.exe` | ~60 MB | 发给别人，任何 Win10/11 双击就跑 |

两个都是真正的单文件：没有 dll、没有 runtimes 目录。

## 快捷键

| 按键 | 作用 |
| --- | --- |
| `Ctrl+O` | 打开文件 |
| `Ctrl+E` | 最近打开面板 |
| `Ctrl+F` | 页内搜索（Enter 下一个，Shift+Enter 上一个，Esc 关闭） |
| `Ctrl+P` | 导出 PDF |
| `Ctrl+Shift+S` | 导出长图（整页 PNG） |
| `Ctrl+Shift+E` | 用外部程序编辑当前文件 |
| `Ctrl+Shift+R` | 朗读 / 停止 |
| `Ctrl+Shift+B` | 在当前位置加书签 |
| `F5` | 重新读取当前文件 |
| `F11` / `Esc` | 无干扰模式开关 |
| `Ctrl+,` | 设置 |
| `Ctrl+T` | 显示/隐藏目录 |
| `Ctrl+Shift+W` | 宽屏开关（正文铺满窗口 + 收起目录，看多列表格用） |
| `Ctrl+Shift+D` | 浅色/深色快切（在同一套主题的明暗版本之间） |
| `Ctrl+加/减`、`Ctrl+滚轮` | 正文字号 |
| `Ctrl+0` | 字号回 16 |
| `Alt+←` / `Alt+→` | 上一份 / 下一份文档 |

标签栏：中键关闭，右键菜单有「关闭 / 关闭其他 / 打开所在文件夹」。

## 自检

```powershell
.\src\MdViewer\bin\Release\net10.0-windows\MdViewer.exe --snapshot out.png 文档.md
```

会输出两张图：`out.png`（WPF 界面部分）和 `out.page.png`（WebView2 正文区域，
用浏览器自己的 `CapturePreviewAsync` 截，WPF 的 `RenderTargetBitmap` 截不到原生子窗口）。

其他命令行参数：`--demo` 生成并打开样例、`--help`、`--make-shortcut <lnk>`。这些都不弹窗，
结果写到 `%APPDATA%\MdViewer\cli-result.txt`，方便脚本调用。

## 内存占用

WebView2 是 Chromium 内核，代价在这里。实测（Win11 / 16 进程桌面）：

| 状态 | 进程数 | 工作集 |
| --- | --- | --- |
| 启动后没打开文件 | 1 | ~94 MB |
| 打开一篇文档 | 7 | ~426 MB |

启动时**不会**创建 WebView2 —— 只有真要显示内容时才建，所以「打开程序丢在那里」的成本是 94 MB。
传进去 13 个 Chromium 参数关掉了联网、后台更新、崩溃上报等常驻组件，但渲染进程组本身省不下来，
这是浏览器架构决定的，调参只能省 10 MB 左右。

## 已知边界

- **EPUB 不支持书里的原始 CSS**：出版社的固定版式（6 寸屏的死值字体、分栏、竖排）会丢掉，
  统一用本程序的排版规则。图片、表格、代码块、脚注都保留。
- **EPUB 是整本拼一页**：上百章的大书首次打开会慢一点（解压 + 合并），之后走缓存。
- **加一套主题的成本**：一块 CSS 变量（约 20 行）+ 一条 `MarkdownTheme` 元数据（含四个壳色）。壳色只给四色，
  其余 36 个界面色由 `Theme.ForMarkdownTheme()` 混出来 —— 想精确控制某个按钮色才需要额外字段，正常不用。
- **标注按字符偏移存**：正文改动后偏移会漂，程序会用当时选中的原文在全文里重新搜一次定位；
  原文也被改了的那条会跳过（不破坏排版），但那条高亮就看不见了。
- **朗读用系统自带的语音**（SAPI / OneCore）。中文得系统里装了中文语音包，否则会念成英文腔或没声音。
- **长图有像素上限**：整页超过 16000px 会自动降采样（最小 0.25 倍），宁可小一点也不让整张失败。
- **`local` 版换机器要装运行时**：发给别人请用 `portable`。
- **自动更新需要仓库里有 Release**，并且附件里要有 `.exe`。tag 里带版本号即可（`mdviewer-v0.3.0`、`v0.3.0` 都认）。
- **导出 HTML 的图片是相对路径**（EPUB 导出则指向本机缓存），分享时要连图片一起拷。
- **导出跟着宽屏走**：宽屏模式下导出 HTML 铺满浏览器窗口、导出 PDF 用 A4 横向；普通模式是 880 阅读栏 + A4 竖版。

## 开发

```powershell
dotnet build src/MdViewer/MdViewer.csproj
dotnet test  src/MdViewer/MdViewer.Tests/MdViewer.Tests.csproj
```

工程结构：`MdViewer`（WPF 界面）/ `MdViewer.Core`（纯逻辑，不引用 WPF）/ `MdViewer.Tests`（xunit）。
Core 里的东西都能脱离界面测试：Markdown 渲染、HTML 清洗、EPUB 解析、最近列表、设置迁移、版本比较、
front matter、标注存储、第三方库释放。

主窗口按职责拆成 partial：`MainWindow.xaml.cs`（打开/渲染/滚动）、`.Commands.cs`（命令、拖放、快捷键）、
`.Reading.cs`（皮肤、字体、无干扰、外部编辑、朗读、导出长图）、`.Annotations.cs`（标注书签）、
`.Tabs.cs`（多标签）、`.Folder.cs`（文件树）、`.Shell.cs`（侧栏页签、多文件拖入、定时换肤）。

改界面前可以先跑一次自检截图（见上），`src/MdViewer/samples/` 下有两篇覆盖全部新功能的样例文档。

细节踩坑见 [docs/开发记录.md](docs/开发记录.md)，面向使用的说明见 [docs/使用说明.md](docs/使用说明.md)。
