using System.Globalization;
using System.Windows;

namespace Hamyar;

public enum TaskType { OpenTarget = 0, Command = 1, PowerShell = 2, Message = 3 }
public enum RepeatMode { None = 0, Daily = 1, Workdays = 2, Weekly = 3, Holidays = 4, Custom = 5 }

public class Board
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
}

public class TaskAction
{
    public long Id { get; set; }
    public TaskType Type { get; set; }
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
    public string Display => $"[{Loc.Get("tt_" + Type)}]  {Target} {Args}".Trim();
}

public class Item
{
    public long Id { get; set; }
    public long BoardId { get; set; }
    public string Title { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime? AlarmAt { get; set; }
    public RepeatMode Repeat { get; set; }
    /// <summary>Custom repeat: comma separated days of week (0=Sunday .. 6=Saturday).</summary>
    public string CustomDays { get; set; } = "";
    public string Sound { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<TaskAction> Tasks { get; set; } = new();

    // Display helpers for the UI
    public string WhenText => AlarmAt.HasValue
        ? "⏰ " + Jal.Stamp(AlarmAt.Value) + "   ·   " + Loc.Get("rp_" + Repeat)
        : Loc.Get("no_alarm");
    string LeftSuffix
    {
        get
        {
            if (!Enabled || !AlarmAt.HasValue) return "";
            var d = AlarmAt.Value - DateTime.Now;
            if (d <= TimeSpan.Zero) return "";
            int total = (int)Math.Ceiling(d.TotalMinutes), h = total / 60, m = total % 60;
            return "  ·  ⏳ " + (h >= 24 ? (h / 24) + " d " + (h % 24) + " h" : h > 0 ? string.Format(Loc.Get("pr_left_hm"), h, m) : string.Format(Loc.Get("pr_left_m"), m));
        }
    }
    public bool HasAlarm => AlarmAt.HasValue;
    public Visibility AlarmVisibility => AlarmAt.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public string TimeText => AlarmAt.HasValue ? AlarmAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "--:--";
    public string DateText => AlarmAt.HasValue
        ? Jal.Date(AlarmAt.Value) + "  ·  " + Loc.Get("rp_" + Repeat) + LeftSuffix
        : Loc.Get("no_alarm");
    public string SoundText => string.IsNullOrEmpty(Sound) ? "" : "🔊 " + System.IO.Path.GetFileName(Sound);
    public string TasksText => Tasks.Count == 0 ? "" : "▶ " + string.Join("   |   ", Tasks.Select(t => t.Display));
}
