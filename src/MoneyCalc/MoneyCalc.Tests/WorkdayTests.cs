using System;
using MoneyCalc.Core;

namespace MoneyCalc.Tests
{
    public class WorkdayTests
    {
        /// <summary>只用周一到周五的日历，避免依赖内置节假日。</summary>
        private static HolidayCalendar Plain() => HolidayCalendar.WeekdaysOnly();

        [Fact]
        public void 加十个工作日_跨周末()
        {
            // 2026-11-13 是周五，+10 个工作日（不含起始日）= 11-27 周五
            var start = new DateTime(2026, 11, 13);
            var end = Plain().AddWorkdays(start, 10, false);

            Assert.Equal(new DateTime(2026, 11, 27), end);
        }

        [Fact]
        public void 起点是周六时从下周一开始数()
        {
            var start = new DateTime(2026, 11, 14);   // 周六
            var end = Plain().AddWorkdays(start, 1, false);

            Assert.Equal(new DateTime(2026, 11, 16), end);
        }

        [Fact]
        public void 起点是周日时从下周一开始数()
        {
            var start = new DateTime(2026, 11, 15);   // 周日
            var end = Plain().AddWorkdays(start, 2, false);

            Assert.Equal(new DateTime(2026, 11, 17), end);
        }

        [Fact]
        public void 含起始日时起始日算第一个工作日()
        {
            var start = new DateTime(2026, 11, 13);
            var end = Plain().AddWorkdays(start, 1, true);

            Assert.Equal(new DateTime(2026, 11, 13), end);
        }

        [Fact]
        public void 起始日是周末且含起始日时先跳到工作日()
        {
            var start = new DateTime(2026, 11, 14);   // 周六
            var end = Plain().AddWorkdays(start, 1, true);

            Assert.Equal(new DateTime(2026, 11, 16), end);
        }

        [Fact]
        public void N等于0直接返回起始日()
        {
            var start = new DateTime(2026, 11, 14);
            Assert.Equal(start, Plain().AddWorkdays(start, 0, false));
            Assert.Equal(start, Plain().AddWorkdays(start, 0, true));
        }

        [Fact]
        public void 负数往前推()
        {
            var start = new DateTime(2026, 11, 16);   // 周一
            var end = Plain().AddWorkdays(start, -1, false);

            Assert.Equal(new DateTime(2026, 11, 13), end);   // 上周五
        }

        [Fact]
        public void 法定节假日不算工作日()
        {
            var calendar = HolidayCalendar.WeekdaysOnly();
            calendar.Holidays.Add(new DateTime(2026, 11, 16));   // 周一放假

            var end = calendar.AddWorkdays(new DateTime(2026, 11, 13), 1, false);
            Assert.Equal(new DateTime(2026, 11, 17), end);
        }

        [Fact]
        public void 调休补班算工作日()
        {
            var calendar = HolidayCalendar.WeekdaysOnly();
            calendar.MakeupWorkdays.Add(new DateTime(2026, 11, 14));   // 周六补班

            Assert.True(calendar.IsWorkday(new DateTime(2026, 11, 14)));
            var end = calendar.AddWorkdays(new DateTime(2026, 11, 13), 1, false);
            Assert.Equal(new DateTime(2026, 11, 14), end);
        }

        [Fact]
        public void 起止同日()
        {
            var day = new DateTime(2026, 11, 13);
            Assert.Equal(1, Plain().CountWorkdays(day, day, true));
            Assert.Equal(0, Plain().CountWorkdays(day, day, false));
        }

        [Fact]
        public void 起止同日且是周末()
        {
            var day = new DateTime(2026, 11, 14);
            Assert.Equal(0, Plain().CountWorkdays(day, day, true));
        }

        [Fact]
        public void 工作日计数含与不含端点差一天()
        {
            var a = new DateTime(2026, 11, 2);    // 周一
            var b = new DateTime(2026, 11, 6);    // 周五

            Assert.Equal(5, Plain().CountWorkdays(a, b, true));
            Assert.Equal(3, Plain().CountWorkdays(a, b, false));
        }

        [Fact]
        public void 跨月计数()
        {
            var a = new DateTime(2026, 10, 29);   // 周四
            var b = new DateTime(2026, 11, 3);    // 周二

            // 10/29 四、10/30 五、11/2 一、11/3 二
            Assert.Equal(4, Plain().CountWorkdays(a, b, true));
        }

        [Fact]
        public void 跨年计数()
        {
            var a = new DateTime(2026, 12, 31);   // 周四
            var b = new DateTime(2027, 1, 4);     // 周一

            Assert.Equal(3, Plain().CountWorkdays(a, b, true));   // 12/31、1/1、1/4
        }

        [Fact]
        public void 反向计数返回负数()
        {
            var a = new DateTime(2026, 11, 6);
            var b = new DateTime(2026, 11, 2);

            Assert.Equal(-5, Plain().CountWorkdays(a, b, true));
        }

        [Fact]
        public void 星期几显示中文()
        {
            Assert.Equal("星期六", HolidayCalendar.WeekdayName(new DateTime(2026, 11, 14)));
            Assert.Equal("星期日", HolidayCalendar.WeekdayName(new DateTime(2026, 11, 15)));
            Assert.Equal("星期一", HolidayCalendar.WeekdayName(new DateTime(2026, 11, 16)));
        }

        [Fact]
        public void 内置日历有数据且标注了截止时间()
        {
            var calendar = HolidayCalendar.BuiltIn2025And2026();

            Assert.True(calendar.Holidays.Count > 30);
            Assert.True(calendar.MakeupWorkdays.Count >= 6);
            Assert.Contains("2026-11", calendar.DataNote);
            // 2026-10-01 国庆放假
            Assert.False(calendar.IsWorkday(new DateTime(2026, 10, 1)));
            // 2025-01-26 周日补班
            Assert.True(calendar.IsWorkday(new DateTime(2025, 1, 26)));
        }

        [Fact]
        public void 清空节假日表后只剩周一到周五()
        {
            var calendar = Plain();

            Assert.True(calendar.IsWorkday(new DateTime(2026, 10, 1)));
            Assert.False(calendar.IsWorkday(new DateTime(2026, 10, 3)));
        }
    }
}
