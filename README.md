# 上班鼓励器 WorkEncourager

一个常驻桌面的「上班鼓励器」悬浮窗，实时计算你上班期间已经赚到多少钱，并生成工时报表。

![WPF / Windows](https://img.shields.io/badge/.NET-10.0-blue)
![Tests](https://img.shields.io/badge/tests-128%20passed-brightgreen)

## 文档

| 文档 | 内容 |
| --- | --- |
| [使用说明.md](docs/使用说明.md) | 完整手册：界面速览、设置项、窗口操作、暂停语义、计算规则、CSV 字段、常见问题 |
| [开发记录.md](docs/开发记录.md) | 需求演进、技术决策、踩坑与解决方案、提交历史 |

## 特性

- **两种形态**：完整界面（主页/明细/统计/设置 4 个 Tab）与**迷你挂件**（252×112 的桌面小卡片，双击展开）
- **无边框半透明悬浮窗**，永远置顶（可关），按住空白处任意拖动，位置/大小自动记忆
- **边缘可拖动缩放**：鼠标移到窗口最外圈（上下左右边缘或四角）出现缩放光标即可拖动改大小
- **实时金额**：今日已赚金额按秒累加，保留 4 位小数
- **赚钱速度**：元/秒 实时显示
- **下班倒计时**：距下班还剩多久，一目了然
- **发薪日倒计时**：距发工资还有几天，本月计薪日数
- **本月工资条**：已赚金额 / 月薪 的可视化进度条
- **工时报表**：程序开着就在工作，自动累计实际在场时长，一键导出 CSV
- **导出路径可复制**：导出后提示框里的路径可选中复制，也可一键复制到剪贴板 / 打开所在文件夹
- **鼓励语**：语录库 × 语气（佛系/毒舌/热血）× 场景（首件/多件/买不够）
- **工时进度条**：今日工时完成百分比 + 已工作/总工时
- **本月累计**：已过的计薪日按满勤补算，只计已赚不含亏损
- **历史统计**：本周/本月/本年累计工时与收入、加班合计、最赚的一天
- **透明度可调**：45% ~ 100% 滑块实时调节
- **窗体可缩放**：拖边缘自由缩放，或用 小/中/大 预设；内容等比自适应（Viewbox）
- **深色模式**：深色配色切换
- **鼠标穿透**：悬浮窗不挡下面的工作时可切换为点击穿透
- **暂停即冻结**：关闭计时器后金额/进度/倒计时全部停住，恢复瞬间不跳变
- **设置可自定义**：月薪、上班/下班时间、午休时段、目标物名称与单价、发薪日、计薪日（周一~周日多选）

## 计算规则

按中国法定 **月计薪天数 21.75 天** 折算（365 − 104 个休息日 = 261 天，÷ 12 = 21.75）：

```
日薪         = 月薪 ÷ 21.75
时薪         = 日薪 ÷ 每日计薪工时（已扣午休）
每秒         = 日薪 ÷ 每日计薪工时秒数
今日已赚     = 每秒 × 今日已工作秒数
今日满勤可得 = 日薪
本月累计     = 本月已过的计薪日 × 日薪 + 今天实际已赚
```

> 注意：另有「月工作日 20.83 天」（扣除 11 天法定节假日）常被混用，但折算日薪/时薪的标准基数是 **21.75**。

### 节假日与调休

内置 **2026 年**法定假日与调休上班日（依据《国务院办公厅关于2026年部分节假日安排的通知》国办发明电〔2025〕7号）。
「本月累计」会自动扣除假日、补上调休上班日。后续年度可在 `PayCalendar` 中补充。

### 午休不计薪

支持多时段作息：`09:00-18:00` + 午休 `12:00-13:00` → 实际计薪工时 8 小时，时薪和每秒都按 8 小时算。

### "开着就在工作"

程序运行且处于上班时段内，即视为在岗。实际在场时长会：
- 跨重启累计（关机期间不追补）
- 跨天时按自然日切分归属，不会把凌晨的工时记到前一天
- 暂停期间不计入
- 超出下班时间的部分单独计为加班

设置保存在 `%APPDATA%\WorkEncourager\settings.json`，历史记录在同目录 `history.json`，均不上传任何服务器。

## 项目结构

```
WorkEnc/
├── WorkEnc.slnx                    解决方案
├── WorkEncourager.csproj          WPF 主程序
├── App.xaml / App.xaml.cs          入口与命令行参数
├── MainWindow.xaml                 界面布局（完整界面 4 Tab / 迷你挂件）
├── MainWindow.xaml.cs              界面逻辑与事件
├── Win32.cs                        鼠标穿透（WS_EX_TRANSPARENT）
├── app.manifest                    PerMonitorV2 DPI 感知
├── app.ico / make-icon.js          图标与生成脚本
├── docs/                           文档
│   ├── 使用说明.md                 完整使用手册
│   └── 开发记录.md                 需求演进、技术踩坑、提交历史
├── WorkEnc.Core/                   纯逻辑类库（无 UI 依赖，可独立测试）
│   ├── AppConfig.cs                配置模型
│   ├── ConfigStore.cs              配置读写与版本迁移
│   ├── ViewState.cs                视图模式 / Tab / 窗口尺寸计算
│   ├── WorkSchedule.cs             作息表（上下班 + 午休多时段）
│   ├── PayCalendar.cs              法定节假日与调休日历
│   ├── SalaryCalculator.cs         薪资与工时计算
│   ├── WorkdayTracker.cs           实际在场时长追踪与持久化
│   ├── HistoryAnalyzer.cs          周/月/年统计
│   ├── CsvExporter.cs              CSV 报表生成
│   ├── QuoteProvider.cs            语录库
│   └── Theme.cs                    主题配色
└── WorkEnc.Tests/                  xUnit 单元测试（128 个）
```

## 构建与测试

```bash
# 运行全部单元测试
dotnet test WorkEnc.Tests/WorkEnc.Tests.csproj

# 编译
dotnet build WorkEncourager.csproj -c Release

# 发布为单文件 exe（框架依赖，约 280 KB）
dotnet publish WorkEncourager.csproj -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true

# 发布为自包含单文件（任意机器可运行，约 80 MB）
dotnet publish WorkEncourager.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 调试参数

| 参数 | 说明 |
| --- | --- |
| `--size 宽x高` | 指定初始窗口尺寸，例如 `--size 700x900` |
| `--open-settings` | 启动即打开「设置」Tab |
| `--open-stats` | 启动即打开「统计」Tab |
| `--open-detail` | 启动即打开「明细」Tab |
| `--mini` | 启动即进入迷你挂件模式 |
| `--paused` | 以暂停状态启动（退出时不写回配置） |
| `--shot <路径>` | 用 `RenderTargetBitmap` 渲染窗口截图后退出 |
| `--gap <毫秒>` | 配合 `--shot` 连拍两张（第二张存为 `<路径>.t2.png`） |

```bash
./WorkEncourager.exe --open-stats --shot stats.png
```

> 带 `--` 开头的参数进入**调试模式**：配置与历史改写到 `%APPDATA%\WorkEncourager\debug\` 子目录，绝不碰真实数据，且退出时不落盘。

## CSV 报表格式

按天一行，UTF-8 BOM 编码，Excel 直接双击打开：

```
日期,星期,是否计薪日,备注,首次上班,最后下班,实际工时(小时),应出勤(小时),加班(小时),当日已赚(元)
2026-11-04,周三,是,,09:02,17:05,8.00,9.00,0.00,88.8889
```

月度汇总导出格式：

```
月份,计薪日数,出勤日数,实际工时(小时),加班(小时),本月已赚(元),日均工时(小时)
2026-11,21,5,37.70,0.50,354.4570,7.54
```

## 已知限制

- 仅支持 Windows（WPF）
- 框架依赖版本需要目标机器安装 .NET 10 Desktop Runtime；需要免安装请用自包含发布
- 透明度调节会让整个窗体（含文字）一起变淡，这是 WPF 窗口级透明度的固有限制
- 鼠标穿透开启后无法直接拖动窗口，需先在设置里关掉

## License

MIT
