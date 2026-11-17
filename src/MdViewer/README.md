# Markdown 阅读器 MdViewer

双击 `.md` 或 `.epub` 直接打开，排版按阅读优化（正文宽度可调、行高 1.7、代码块带语法高亮和复制按钮），
左边目录侧栏，`Ctrl+F` 页内搜索，能导出 PDF / HTML，**10 套主题随时切换**（含护眼米黄、报纸衬线）。

- **版本**：0.4.0
- **形态**：WPF 桌面程序，单文件 exe；带安装脚本（装到用户目录 + 开始菜单 + 文件关联）
- **数据目录**：`%APPDATA%\MdViewer\`（配置、日志、WebView2 缓存、EPUB 解压、`--demo` 样例）
- **依赖**：.NET 10；渲染用 WebView2，需要本机有 WebView2 运行时（Win11 自带，装了 Edge 的 Win10 一般也有）
- **联网**：默认不联网。全程序只有三个地方会发请求 —— 你点「去下载 WebView2」、你在设置里点「检查更新」、
  你打开了「启动时检查更新」。md 里的远程图片会被 CSP 挡掉，不会偷偷加载

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
| 设置窗口 | 外观（深浅模式、浅色/深色各用哪套主题、字号、正文宽度）、阅读（自动刷新、记忆位置、目录）、文件关联、更新、重置、关于 |
| 恢复默认设置 | 设置 → 重置：外观与阅读开关回到出厂值，最近打开、阅读位置、窗口大小保留；重置前自动备份 `settings.json`（留最近 5 份） |
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
| `F5` | 重新读取当前文件 |
| `Ctrl+,` | 设置 |
| `Ctrl+T` | 显示/隐藏目录 |
| `Ctrl+Shift+W` | 宽屏开关（正文铺满窗口 + 收起目录，看多列表格用） |
| `Ctrl+Shift+D` | 浅色/深色快切（在同一套主题的明暗版本之间） |
| `Ctrl+加/减`、`Ctrl+滚轮` | 正文字号 |
| `Ctrl+0` | 字号回 16 |
| `Alt+←` / `Alt+→` | 上一份 / 下一份文档 |

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
- **主题只覆盖正文排版与界面主色**：不给每套主题重做完整的界面配色（侧栏、按钮底色等仍按浅/深两套骨架走），
  这样加一套主题的成本才是一块 CSS 变量而不是 36 个字段。
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
Core 里的东西都能脱离界面测试：Markdown 渲染、HTML 清洗、EPUB 解析、最近列表、设置迁移、版本比较。

细节踩坑见 [docs/开发记录.md](docs/开发记录.md)，面向使用的说明见 [docs/使用说明.md](docs/使用说明.md)。
