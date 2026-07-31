using System;

namespace BedrockServerManager.Services;

public static class ScheduledRebootService
{
    public static DateTime? GetNextRebootDate(string freq, string dateVal, string timeStr)
    {
        var now = DateTime.Now;
        var parts = timeStr.Split(':');
        int hour = int.Parse(parts[0]);
        int minute = int.Parse(parts[1]);

        var targetToday = new DateTime(now.Year, now.Month, now.Day, hour, minute, 0);

        return freq switch
        {
            "Daily" => targetToday >= now ? targetToday : targetToday.AddDays(1),
            "Weekly" or "Biweekly" => ComputeWeekly(dateVal, now, hour, minute),
            "Monthly" => ComputeMonthly(dateVal, now, hour, minute),
            _ => null
        };
    }

    private static DateTime ComputeWeekly(string dayOfWeekStr, DateTime now, int hour, int minute)
    {
        var targetDay = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), dayOfWeekStr);
        int daysAhead = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;
        var target = now.Date.AddDays(daysAhead).AddHours(hour).AddMinutes(minute);
        if (target < now) target = target.AddDays(7);
        return target;
    }

    private static DateTime? ComputeMonthly(string dateVal, DateTime now, int hour, int minute)
    {
        if (dateVal == "Last Day")
        {
            var nextMonth = now.AddMonths(1);
            var lastDay = new DateTime(nextMonth.Year, nextMonth.Month, 1).AddDays(-1);
            var target = new DateTime(lastDay.Year, lastDay.Month, lastDay.Day, hour, minute, 0);
            if (target < now)
            {
                var monthAfter = now.AddMonths(2);
                var lastDay2 = new DateTime(monthAfter.Year, monthAfter.Month, 1).AddDays(-1);
                target = new DateTime(lastDay2.Year, lastDay2.Month, lastDay2.Day, hour, minute, 0);
            }
            return target;
        }

        int targetDay = int.Parse(dateVal);
        for (int i = 0; i < 12; i++)
        {
            int m = ((now.Month - 1 + i) % 12) + 1;
            int y = now.Year + ((now.Month - 1 + i) / 12);
            if (DateTime.DaysInMonth(y, m) < targetDay) continue;
            var target = new DateTime(y, m, targetDay, hour, minute, 0);
            if (target >= now) return target;
        }
        return null;
    }
}