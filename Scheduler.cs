using System.Windows.Threading;

namespace Hamyar;

/// <summary>Checks the database every second and fires due alarms.</summary>
public class Scheduler
{
    readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(1) };
    public event Action<Item> Fired;

    public void Start()
    {
        _t.Tick += (s, e) => Check();
        _t.Start();
    }

    void Check()
    {
        try
        {
            var now = DateTime.Now;
            foreach (var it in Db.GetItems(null, true, now))
            {
                var late = now - it.AlarmAt.Value;
                var next = Next(it, now);
                // advance first, so the same alarm can never fire twice
                Db.UpdateAlarm(it.Id, next ?? it.AlarmAt, next.HasValue);
                if (late <= TimeSpan.FromMinutes(10)) Fired?.Invoke(it);   // older than 10 min = missed, skip
            }
        }
        catch (Exception ex) { Log.Write("Scheduler: " + ex.Message); }
    }

    public static HashSet<int> ParseDays(string csv)
    {
        var set = new HashSet<int>();
        foreach (var p in (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(p.Trim(), out var d) && d >= 0 && d <= 6) set.Add(d);
        return set;
    }

    /// <summary>The first day on/after `t` that fits the repeat rule (Holidays / Custom), so the first ring is on a matching day.</summary>
    public static DateTime FirstMatch(Item it, DateTime t)
    {
        HashSet<int> set = it.Repeat == RepeatMode.Holidays
            ? Enumerable.Range(0, 7).Where(d => !Cfg.WorkDaySet().Contains(d)).ToHashSet()
            : it.Repeat == RepeatMode.Custom ? ParseDays(it.CustomDays) : null;
        if (set == null || set.Count == 0) return t;
        for (int i = 0; i < 8 && !set.Contains((int)t.DayOfWeek); i++) t = t.AddDays(1);
        return t;
    }

    public static DateTime? Next(Item it, DateTime now)
    {
        var t = it.AlarmAt.Value;
        switch (it.Repeat)
        {
            case RepeatMode.Daily:
                while (t <= now) t = t.AddDays(1);
                return t;
            case RepeatMode.Weekly:
                while (t <= now) t = t.AddDays(7);
                return t;
            case RepeatMode.Workdays:
            {
                var wd = Cfg.WorkDaySet();                 // set in Settings (default Saturday - Thursday)
                do { t = t.AddDays(1); }
                while (t <= now || !wd.Contains((int)t.DayOfWeek));
                return t;
            }
            case RepeatMode.Holidays:
            {
                var wd = Cfg.WorkDaySet();                 // holidays = every day that is NOT a working day
                if (wd.Count >= 7) return null;
                do { t = t.AddDays(1); }
                while (t <= now || wd.Contains((int)t.DayOfWeek));
                return t;
            }
            case RepeatMode.Custom:
            {
                var set = ParseDays(it.CustomDays);
                if (set.Count == 0) return null;
                do { t = t.AddDays(1); }
                while (t <= now || !set.Contains((int)t.DayOfWeek));
                return t;
            }
            default:
                return null;
        }
    }
}
