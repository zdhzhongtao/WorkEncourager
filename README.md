# 上班鼓励器 WorkEncourager

一个常驻桌面的「上班鼓励器」悬浮窗，实时计算你上班期间已经赚到多少钱。

![WPF / Windows](https://img.shields.io/badge/.NET-10.0-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## 特性

- **无边框半透明悬浮窗**，永远置顶（可关），按住空白处任意拖动，位置/大小自动记忆
- **实时金额**：今日已赚金额按秒累加，保留 4 位小数
- **赚钱速度**：元/秒 实时显示
- **下班倒计时**：距下班还剩多久，一目了然
- **鼓励语**：如「再过 3 小时 25 分，一件 T 恤就赚回来了」
- **工时进度条**：今日工时完成百分比 + 已工作/总工时
- **本月累计**：已过的计薪日按满勤补算，只计已赚不含亏损
- **透明度可调**：45% ~ 100% 滑块实时调节
- **窗体可缩放**：拖边框自由缩放，或用 小/中/大 预设；内容等比自适应（Viewbox）
- **设置可自定义**：月薪、上班/下班时间、T 恤单价、本月计薪日（周一~周日多选）

## 计算规则

按中国法定 **月计薪天数 21.75 天** 折算（365 − 104 个休息日 = 261 天，÷ 12 = 21.75）：

```
日薪        = 月薪 ÷ 21.75
时薪        = 日薪 ÷ 每日打卡工时
每秒        = 日薪 ÷ 每日工时秒数
今日已赚    = 每秒 × 今天已工作秒数
今日满勤可得 = 日薪
本月累计    = 本月已过的计薪日 × 日薪 + 今天实际已赚
```

> 注意：另有「月工作日 20.83 天」（扣除 11 天法定节假日）常被混用，但折算日薪/时薪的标准基数是 **21.75**。

设置保存在 `%APPDATA%\WorkEncourager\settings.json`，不上传任何服务器。

## 构建

```bash
# 还原并编译
dotnet build -c Release

# 发布为单文件 exe（框架依赖，约 220 KB）
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 发布为自包含单文件（任意机器可运行，约 80 MB）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

产物位于 `bin/Release/net10.0-windows/win-x64/publish/WorkEncourager.exe`。

## 调试参数

程序支持几个命令行参数，方便自检和自动化截图：

| 参数 | 说明 |
| --- | --- |
| `--size 宽x高` | 指定初始窗口尺寸，例如 `--size 700x900` |
| `--open-settings` | 启动即打开设置面板 |
| `--shot <路径>` | 用 `RenderTargetBitmap` 渲染窗口截图后退出 |

```bash
./WorkEncourager.exe --open-settings --shot settings.png
```

## 项目结构

```
WorkEncourager/
├── App.xaml / App.xaml.cs        程序入口、命令行参数处理
├── MainWindow.xaml                界面布局（主面板 + 设置面板）
├── MainWindow.xaml.cs             计算逻辑、配置读写、拖动/缩放
├── app.manifest                   DPI 感知声明
├── app.ico                        应用图标（由 make-icon.js 生成）
├── make-icon.js                   Node 脚本，生成 app.ico
└── web-version-backup.html        早期网页版原型，仅作参考
```

## 已知限制

- 仅支持 Windows（WPF）
- 框架依赖版本需要目标机器安装 .NET 10 Desktop Runtime；需要免安装请用自包含发布
- 透明度调节会让整个窗体（含文字）一起变淡，这是 WPF 窗口级透明度的固有限制

## License

MIT
