namespace Hamyar;

/// <summary>
/// Decides whether study/flashcard review should be suggested at a given moment, based on the user's
/// sleep, wake and school hours (Settings) and their working/school days (also Settings, reused as-is).
///
///  - Sleep blocks every day (workday or not): from SleepTime until WakeTime (a window that crosses midnight).
///  - School blocks ONLY on a workday (the days configured as working/school days): from SchoolStart until SchoolEnd.
///  - Everything else - awake, not at school - is open for study.
/// </summary>
public static class StudyWindow
{
    public enum Reason { Open, Sleeping, AtSchool }

    /// <summary>The sleep / wake / school settings, read once and reused until a setting changes. A review plan asks
    /// "is this moment free?" thousands of times; reading the database each time made every refresh slow.</summary>
    sealed class Snap
    {
        public int Version;
        public TimeSpan Sleep, Wake, SchoolStart, SchoolEnd;
        public readonly bool[] Work = new bool[7];
    }
    static Snap _snap;

    static Snap Cur()
    {
        var s = _snap;
        int v = Db.SettingsVersion;
        if (s != null && s.Version == v) return s;
        s = new Snap { Version = v, Sleep = Cfg.SleepTime, Wake = Cfg.WakeTime, SchoolStart = Cfg.SchoolStart, SchoolEnd = Cfg.SchoolEnd };
        foreach (var d in Cfg.WorkDaySet()) s.Work[d] = true;
        _snap = s;
        return s;
    }

    /// <summary>Is `at` inside the sleep window? The window crosses midnight whenever SleepTime > WakeTime
    /// (the normal case, e.g. 23:00 to 07:00), so it is checked as two half-open ranges.</summary>
    public static bool IsSleeping(DateTime at)
    {
        var sn = Cur();
        var sleep = sn.Sleep;
        var wake = sn.Wake;
        var tod = at.TimeOfDay;
        if (sleep == wake) return false;             // degenerate setting: treat as "never asleep" rather than "always"
        if (sleep < wake)
            return tod >= sleep && tod < wake;        // same-day window (unusual, but a valid setting)
        return tod >= sleep || tod < wake;             // normal overnight window
    }

    /// <summary>Is `at` inside school hours, on a day configured as a working/school day?</summary>
    public static bool IsAtSchool(DateTime at)
    {
        var sn = Cur();
        if (!sn.Work[(int)at.DayOfWeek]) return false;
        var start = sn.SchoolStart;
        var end = sn.SchoolEnd;
        if (start >= end) return false;                // degenerate setting: no school window
        var tod = at.TimeOfDay;
        return tod >= start && tod < end;
    }

    /// <summary>Why review should or shouldn't be suggested right now.</summary>
    public static Reason Check(DateTime at)
    {
        if (IsSleeping(at)) return Reason.Sleeping;
        if (IsAtSchool(at)) return Reason.AtSchool;
        return Reason.Open;
    }

    public static bool IsOpen(DateTime at) => Check(at) == Reason.Open;

    /// <summary>
    /// The next moment at or after `from` when review may be suggested: past both the sleep window and,
    /// on a school day, the school window. Used so a review that would naturally fall during sleep or
    /// school is pushed to the moment the user is free - typically right at wake-up.
    /// </summary>
    public static DateTime NextOpen(DateTime from)
    {
        var t = from;
        for (int guard = 0; guard < 14; guard++)   // at most ~2 weeks of (sleep, school) pairs; always terminates
        {
            var reason = Check(t);
            if (reason == Reason.Open) return t;

            if (reason == Reason.Sleeping)
            {
                // jump to today's (or tomorrow's) wake time, whichever is the next one after `t`
                var wake = Cur().Wake;
                var candidate = t.Date + wake;
                if (candidate <= t) candidate = candidate.AddDays(1);
                t = candidate;
            }
            else // AtSchool
            {
                t = t.Date + Cur().SchoolEnd;
            }
        }
        return t; // extremely degenerate settings only; avoids an infinite loop
    }

    /// <summary>
    /// The latest moment at or BEFORE `from` when review may be suggested (the mirror of NextOpen). Used when the next
    /// free moment would fall after an exam: the review is then placed just before bedtime / school instead.
    /// </summary>
    public static DateTime PrevOpen(DateTime from)
    {
        var t = from;
        for (int guard = 0; guard < 14; guard++)
        {
            var reason = Check(t);
            if (reason == Reason.Open) return t;

            if (reason == Reason.Sleeping)
            {
                var sleepStart = t.Date + Cur().Sleep;      // when this sleep window began
                if (sleepStart > t) sleepStart = sleepStart.AddDays(-1);
                t = sleepStart.AddMinutes(-1);
            }
            else // AtSchool
            {
                t = t.Date + Cur().SchoolStart - TimeSpan.FromMinutes(1);
            }
        }
        return t;
    }

    /// <summary>Minutes of free study time (not asleep, not at school) between `from` and `to`. Counted in 5-minute steps;
    /// very long spans are measured over 60 days and scaled.</summary>
    public static double OpenMinutes(DateTime from, DateTime to)
    {
        if (to <= from) return 0;
        var horizon = from.AddDays(60);
        var end = to < horizon ? to : horizon;
        double open = 0;
        for (var t = from; t < end; t = t.AddMinutes(5))
        {
            double step = Math.Min(5, (end - t).TotalMinutes);
            if (IsOpen(t)) open += step;
        }
        double measured = (end - from).TotalMinutes;
        return measured <= 0 ? 0 : open * ((to - from).TotalMinutes / measured);
    }

    /// <summary>The moment reached after spending `minutes` of free study time starting at `from` (never later than `limit`).</summary>
    public static DateTime AddOpenMinutes(DateTime from, double minutes, DateTime limit)
    {
        var t = from;
        double left = minutes;
        while (left > 0 && t < limit)
        {
            if (IsOpen(t)) left -= 5;
            t = t.AddMinutes(5);
            if ((t - from).TotalDays > 60) break;
        }
        return t > limit ? limit : t;
    }

    /// <summary>Human label for why "due now" isn't being offered, for a due item that falls in a blocked window.</summary>
    public static string ReasonText(Reason r) => r switch
    {
        Reason.Sleeping => Loc.Get("block_sleep"),
        Reason.AtSchool => Loc.Get("block_school"),
        _ => ""
    };
}
