using System;
using System.Text.Json.Serialization;

namespace WorkEnc.Core
{
    /// <summary>应用配置。可序列化为 settings.json。</summary>
    public class AppConfig
    {
        /// <summary>月薪（元）。</summary>
        public double? MonthlySalary { get; set; }

        /// <summary>上班时间 HH:mm。</summary>
        public string Start { get; set; } = "09:00";

        /// <summary>下班时间 HH:mm。</summary>
        public string End { get; set; } = "18:00";

        /// <summary>午休等休息时段，格式 "12:00-13:00"。</summary>
        public string[] Breaks { get; set; } = Array.Empty<string>();

        /// <summary>目标物单价（元/件）。</summary>
        public double Shirt { get; set; } = 69;

        /// <summary>目标物名称，如"T恤""奶茶""游戏皮肤"。</summary>
        public string GoalItem { get; set; } = "T恤";

        /// <summary>每月发薪日（1-31）。</summary>
        public int PayDay { get; set; } = 10;

        /// <summary>计薪星期（0=周日）。</summary>
        public int[] Weekdays { get; set; } = { 1, 2, 3, 4, 5 };

        /// <summary>额外放假日（公司调休等），yyyy-MM-dd。</summary>
        public string[] ExtraHolidays { get; set; } = Array.Empty<string>();

        /// <summary>额外调休上班日，yyyy-MM-dd。</summary>
        public string[] ExtraWorkdays { get; set; } = Array.Empty<string>();

        /// <summary>鼓励语气：chill（佛系）/ savage（毒舌）/ hype（热血）。</summary>
        public string Tone { get; set; } = "chill";

        /// <summary>主题：light / dark。</summary>
        public string Theme { get; set; } = "light";

        /// <summary>视图模式：full（完整多 Tab）/ mini（桌面小挂件）。空值表示旧版本配置，首次升级时重置布局。</summary>
        public string ViewMode { get; set; } = "";

        /// <summary>上次停留的主 Tab：home / detail / stats / settings。</summary>
        public string ActiveTab { get; set; } = "home";

        /// <summary>鼠标穿透（点击穿透窗口）。</summary>
        public bool ClickThrough { get; set; }

        /// <summary>幽灵模式：鼠标离开窗口时自动变淡，移回来恢复。默认关。</summary>
        public bool GhostMode { get; set; }

        /// <summary>幽灵模式下的淡化比例。</summary>
        public double GhostOpacity { get; set; } = 0.28;

        /// <summary>迷你挂件拖到屏幕边缘时自动吸附。默认关。</summary>
        public bool SnapToEdge { get; set; }

        /// <summary>窗口上次所在的显示器设备名（如 \\.\DISPLAY2），用于重启后回到同一块屏。</summary>
        public string WindowScreen { get; set; } = "";

        /// <summary>迷你挂件上次所在的显示器设备名。</summary>
        public string MiniScreen { get; set; } = "";

        /// <summary>上次导入工时文件所在的目录，下次打开对话框直接定位过去。</summary>
        public string LastImportDir { get; set; } = "";

        /// <summary>窗口位置与状态。</summary>
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public double? Opacity { get; set; }
        public bool Topmost { get; set; } = true;

        /// <summary>迷你模式窗口位置与大小（与完整模式分开记）。</summary>
        public double? MiniLeft { get; set; }
        public double? MiniTop { get; set; }
        public double? MiniWidth { get; set; }
        public double? MiniHeight { get; set; }

        /// <summary>用户是否手动拖过完整模式的窗体边框。只有拖过才沿用自定义高度。</summary>
        public bool FullResized { get; set; }

        /// <summary>用户是否手动拖过迷你挂件的边框。</summary>
        public bool MiniResized { get; set; }

        /// <summary>暂停状态持久化：是否处于暂停。</summary>
        public bool Paused { get; set; }
        /// <summary>累计暂停秒数（当日）。</summary>
        public double PausedAccumSec { get; set; }
        /// <summary>暂停起始时刻 ISO 字符串，用于重启后接续。</summary>
        public string PausedAt { get; set; } = "";
        /// <summary>暂停累计所属日期 yyyy-MM-dd。</summary>
        public string PausedDate { get; set; } = "";
        /// <summary>上一次运行日期，用于跨天重置。</summary>
        public string LastRunDate { get; set; } = "";

        // 旧版配置字段（只读不写，用于自动迁移）
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double? salary { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double? hours { get; set; }
    }
}
