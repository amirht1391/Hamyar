using System.Globalization;
using System.Text;

namespace Hamyar;

/// <summary>
/// Prayer times (اوقات شرعی). The table starts EMPTY; the user fills it by importing a CSV file:
///
///   date,fajr,sunrise,dhuhr,sunset,maghrib,midnight
///   2026-03-21,04:43:00,06:07:00,12:12:00,18:17:00,18:35:00,23:30:00
///
/// Column order is fixed: date, Fajr (اذان صبح), Sunrise (طلوع آفتاب), Dhuhr (اذان ظهر), Sunset (غروب آفتاب),
/// Maghrib (اذان مغرب), Islamic midnight (نیمه شب شرعی). The first row may be a header (any text instead of a date).
/// Dates are yyyy-MM-dd (yyyy/MM/dd also works), times are HH:mm or HH:mm:ss, Persian digits are accepted.
/// Importing a file overwrites the days it contains and keeps all other days.
/// </summary>
public static class PrayTimes
{
    public static readonly string[] Keys = { "pr_fajr", "pr_sunrise", "pr_dhuhr", "pr_sunset", "pr_maghrib", "pr_midnight" };

    static bool TryDate(string text, out DateTime d)
    {
        text = Util.Digits(text ?? "").Trim().Replace('/', '-');
        return DateTime.TryParseExact(text, new[] { "yyyy-M-d", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
    }

    static bool TryTime(string text, out string hhmm)
    {
        hhmm = null;
        text = Util.Digits(text ?? "").Trim();
        if (!TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var t)) return false;
        if (t < TimeSpan.Zero || t >= TimeSpan.FromDays(1)) return false;
        hhmm = t.ToString(@"hh\:mm");
        return true;
    }

    public static (int imported, int skipped, string error) Import(string filePath)
    {
        string text;
        try { text = File.ReadAllText(filePath, Encoding.UTF8); }
        catch (Exception ex) { return (0, 0, string.Format(Loc.Get("card_import_read_error"), ex.Message)); }

        var rows = CsvImport.ParseRows(text);
        if (rows.Count == 0) return (0, 0, Loc.Get("card_import_empty"));

        var good = new List<(string, string[])>();
        int skipped = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Length < 7 || !TryDate(row[0], out var date))
            {
                if (i > 0) skipped++;            // the first row may be a header
                continue;
            }
            var t = new string[6];
            bool ok = true;
            for (int k = 0; k < 6 && ok; k++) ok = TryTime(row[k + 1], out t[k]);
            if (!ok) { skipped++; continue; }
            good.Add((date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t));
        }

        if (good.Count == 0) return (0, skipped, Loc.Get("pr_import_bad"));
        try { Db.SavePrayTimes(good); }
        catch (Exception ex) { Log.Write("Prayer import: " + ex); return (0, skipped, ex.Message); }
        return (good.Count, skipped, null);
    }

    /// <summary>The moment a stored time happens. An Islamic midnight after 00:00 (earlier than Maghrib) belongs to the next calendar day.</summary>
    public static DateTime At(DateTime day, string[] t, int index)
    {
        var time = TimeSpan.Parse(t[index], CultureInfo.InvariantCulture);
        var when = day.Date + time;
        if (index == 5 && time < TimeSpan.Parse(t[4], CultureInfo.InvariantCulture)) when = when.AddDays(1);
        return when;
    }

    /// <summary>Prayer windows: dawn (Fajr until sunrise), noon (Dhuhr until sunset), evening (Maghrib until Islamic midnight).</summary>
    public static readonly (int from, int to)[] Periods = { (0, 1), (2, 3), (4, 5) };

    /// <summary>The window we are in right now (if any): which one, when it began, when it ends.</summary>
    public static (int period, DateTime start, DateTime end, string[] t)? Current(DateTime now)
    {
        for (int off = -1; off <= 1; off++)
        {
            var day = now.Date.AddDays(off);
            var t = Db.GetPrayDay(day);
            if (t == null) continue;
            for (int p = 0; p < Periods.Length; p++)
            {
                try
                {
                    var a = At(day, t, Periods[p].from); var b = At(day, t, Periods[p].to);
                    if (now >= a && now < b) return (p, a, b, t);
                }
                catch { }
            }
        }
        return null;
    }

    /// <summary>The next prayer time after <paramref name="now"/> (looks at yesterday, today and tomorrow), or null.</summary>
    public static (DateTime when, int index)? Next(DateTime now)
    {
        (DateTime when, int index)? best = null;
        for (int off = -1; off <= 1; off++)
        {
            var day = now.Date.AddDays(off);
            var t = Db.GetPrayDay(day);
            if (t == null) continue;
            for (int i = 0; i < 6; i++)
            {
                DateTime w;
                try { w = At(day, t, i); } catch { continue; }
                if (w > now && (best == null || w < best.Value.when)) best = (w, i);
            }
        }
        return best;
    }
}
