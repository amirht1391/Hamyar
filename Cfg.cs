using Microsoft.Win32;

namespace Hamyar;

/// <summary>Typed access to the Settings table.</summary>
public static class Cfg
{
    static string S(string k, string d) => Db.GetSetting(k, d);
    static void W(string k, string v) => Db.SetSetting(k, v);

    /// <summary>Big/local data (font cache): %LOCALAPPDATA%\TaskAlarm</summary>
    public static string LocalDir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskAlarm");

    public static int Theme { get => int.Parse(S("theme", "0")); set => W("theme", value.ToString()); }       // 0 dark, 1 light
    public static int Accent { get => int.Parse(S("accent", "0")); set => W("accent", value.ToString()); }     // 0 blue, 1 red, 2 green, 3 yellow
    public static int Backdrop                                                                                  // DWM: 3 acrylic, 2 mica, 1 none
    {
        get { var v = int.Parse(S("backdrop", "3")); return v == 4 ? 2 : v; }
        set => W("backdrop", value.ToString());
    }
    /// <summary>"fa" or "en". On the very first run it follows the Windows display language (Persian Windows -> fa, anything else -> en)
    /// and is saved; after that only the user's own choice in Settings changes it.</summary>
    public static string Lang
    {
        get
        {
            var v = S("lang", "");
            if (v == "fa" || v == "en") return v;
            string detected;
            try
            {
                var c = System.Globalization.CultureInfo.CurrentUICulture;
                detected = c.TwoLetterISOLanguageName.Equals("fa", StringComparison.OrdinalIgnoreCase) ? "fa" : "en";
            }
            catch { detected = "en"; }
            W("lang", detected);
            return detected;
        }
        set => W("lang", value);
    }
    public static bool KeepRunning { get => S("keep", "1") == "1"; set => W("keep", value ? "1" : "0"); }
    public static bool ShowHelp { get => S("show_help", "1") == "1"; set => W("show_help", value ? "1" : "0"); }
    public static bool StudyCompact { get => S("study_compact", "1") == "1"; set => W("study_compact", value ? "1" : "0"); }   // default: compact
    // Planner behaviours (all on by default, can be switched off in Settings)
    // alarms at prayer times: one flag per time (fajr, sunrise, dhuhr, sunset, maghrib, midnight)
    /// <summary>Order of the main tabs in the side bar (comma separated tags).</summary>
    public static string NavOrder { get => S("nav_order", ""); set => W("nav_order", value); }
    public static bool PrayAlarm(int i) { var m = S("pray_alarm", "111111"); return i < m.Length && m[i] == '1'; }
    public static void SetPrayAlarm(int i, bool on)
    {
        var m = S("pray_alarm", "111111").PadRight(6, '0').ToCharArray();
        m[i] = on ? '1' : '0';
        W("pray_alarm", new string(m));
    }

    public static bool PlanLimit { get => S("pl_limit", "1") == "1"; set => W("pl_limit", value ? "1" : "0"); }       // last row needs start + duration before a new row
    public static bool PlanAutoFill { get => S("pl_autofill", "1") == "1"; set => W("pl_autofill", value ? "1" : "0"); } // new row start = previous start + duration
    public static bool PlanChain { get => S("pl_chain", "1") == "1"; set => W("pl_chain", value ? "1" : "0"); }       // editing a row shifts the rows below
    public static bool Animations { get => S("anim", "1") == "1"; set => W("anim", value ? "1" : "0"); }
    public static bool SafeMode { get => S("safe", "0") == "1"; set => W("safe", value ? "1" : "0"); }   // no glass, system font
    // working days (0=Sunday ... 6=Saturday). Default: Saturday to Wednesday.
    public static HashSet<int> WorkDaySet()
    {
        var set = new HashSet<int>();
        foreach (var p in S("workdays", "6,0,1,2,3").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(p.Trim(), out var d) && d >= 0 && d <= 6) set.Add(d);
        if (set.Count == 0) for (int i = 0; i < 7; i++) set.Add(i);
        return set;
    }
    public static void SetWorkDays(IEnumerable<int> days) => W("workdays", string.Join(",", days));

    // Timetable top card: before this time of day (also after midnight) = the current day,
    // from this time until midnight = the next school day. Default 15:00 (school is usually over by then).
    public static bool TryParseTimeOfDay(string text, out TimeSpan time)
    {
        time = default;
        text = Util.Digits(text ?? "").Trim();
        if (text.Length == 0) return false;
        if (!text.Contains(':'))
        {
            if (!int.TryParse(text, out var hour) || hour < 0 || hour > 23) return false;     // "15" = 15:00
            time = TimeSpan.FromHours(hour);
            return true;
        }
        return TimeSpan.TryParse(text, out time) && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    }

    public static TimeSpan NextDayTime =>
        TryParseTimeOfDay(S("nextday_time", "15:00"), out var t) ? t : new TimeSpan(15, 0, 0);

    public static string NextDayTimeText
    {
        get => NextDayTime.ToString(@"hh\:mm");
        set => W("nextday_time", value);
    }

    // Sleep / wake / school hours, used to keep study and flashcard suggestions out of sleep and school time
    // (see Scheduler.IsStudyBlocked). Defaults: sleep 23:00-07:00, school 07:30-14:00.
    public static TimeSpan WakeTime => TryParseTimeOfDay(S("wake_time", "07:00"), out var t) ? t : new TimeSpan(7, 0, 0);
    public static string WakeTimeText { get => WakeTime.ToString(@"hh\:mm"); set => W("wake_time", value); }

    public static TimeSpan SleepTime => TryParseTimeOfDay(S("sleep_time", "23:00"), out var t) ? t : new TimeSpan(23, 0, 0);
    public static string SleepTimeText { get => SleepTime.ToString(@"hh\:mm"); set => W("sleep_time", value); }

    public static TimeSpan SchoolStart => TryParseTimeOfDay(S("school_start", "07:30"), out var t) ? t : new TimeSpan(7, 30, 0);
    public static string SchoolStartText { get => SchoolStart.ToString(@"hh\:mm"); set => W("school_start", value); }

    public static TimeSpan SchoolEnd => TryParseTimeOfDay(S("school_end", "14:00"), out var t) ? t : new TimeSpan(14, 0, 0);
    public static string SchoolEndText { get => SchoolEnd.ToString(@"hh\:mm"); set => W("school_end", value); }

    // The ONE Fixed-method interval table, user-editable in Settings. Stored as "value:unit" pairs separated by
    // commas, unit is m(inutes)/h(ours)/d(ays), e.g. "10:m,1:h,1:d,3:d,7:d,14:d". Shared by Study and Flashcards -
    // there is only one Fixed method in the whole app now, not several preset tables.

    public static bool Autostart { get => S("autostart", "0") == "1"; set => W("autostart", value ? "1" : "0"); }

    public static void ApplyAutostart(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (k == null) return;
        k.DeleteValue("TaskAlarm", false);                       // entry of the old name
        if (on) k.SetValue("Hamyar", $"\"{Environment.ProcessPath}\" /minimized");
        else k.DeleteValue("Hamyar", false);
    }
}
