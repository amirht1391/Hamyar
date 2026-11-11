using System.Globalization;

namespace Hamyar;

/// <summary>Solar Hijri (Shamsi) dates for everything the user sees or types. Storage stays Gregorian.</summary>
public static class Jal
{
    static readonly PersianCalendar Pc = new();

    public static string Date(DateTime d)
    {
        try { return string.Format(CultureInfo.InvariantCulture, "{0:0000}/{1:00}/{2:00}", Pc.GetYear(d), Pc.GetMonth(d), Pc.GetDayOfMonth(d)); }
        catch { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }

    /// <summary>"16 آبان" - day and Shamsi month name.</summary>
    public static string DayMonth(DateTime d)
    {
        try { return Pc.GetDayOfMonth(d) + " " + Loc.Get("mo_" + Pc.GetMonth(d)); }
        catch { return d.ToString("MM-dd", CultureInfo.InvariantCulture); }
    }

    public static string Stamp(DateTime d) => Date(d) + " " + d.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Accepts a Shamsi date (year below 1700, e.g. 1405/07/26) or a Gregorian one; separators - / . are all fine.</summary>
    public static bool TryParse(string text, out System.DateTime result)
    {
        result = default;
        var parts = Util.Digits((text ?? "").Trim()).Split(new[] { '-', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var y) || !int.TryParse(parts[1], out var m) || !int.TryParse(parts[2], out var d)) return false;
        try
        {
            result = y < 1700 ? Pc.ToDateTime(y, m, d, 0, 0, 0, 0) : new System.DateTime(y, m, d);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Converts a stored yyyy-MM-dd string for display; unchanged if it cannot be parsed.</summary>
    public static string FromIso(string iso) =>
        System.DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? Date(d) : iso;
}
