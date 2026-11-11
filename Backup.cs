using System.Globalization;

namespace Hamyar;

public class BackupInfo
{
    public string Path { get; set; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public DateTime Created { get; set; }
    public string Kind { get; set; } = "manual";       // manual | auto | beforerestore | beforereset
    public long Size { get; set; }
}

/// <summary>Backups made by the app live in %AppData%\TaskAlarm\backups (never deleted by "reset").</summary>
public static class Backups
{
    public static string Dir => System.IO.Path.Combine(Db.Dir, "backups");

    public static string Create(string kind)
    {
        Directory.CreateDirectory(Dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = System.IO.Path.Combine(Dir, stamp + "_" + kind + ".db");
        int n = 2;
        while (File.Exists(path))     // same second: never overwrite
            path = System.IO.Path.Combine(Dir, stamp + "-" + (n++) + "_" + kind + ".db");
        Db.BackupTo(path);
        return path;
    }

    /// <summary>All backups, newest first.</summary>
    public static List<BackupInfo> List()
    {
        var list = new List<BackupInfo>();
        if (!Directory.Exists(Dir)) return list;
        foreach (var f in Directory.GetFiles(Dir, "*.db"))
        {
            var fi = new FileInfo(f);
            var name = System.IO.Path.GetFileNameWithoutExtension(f);
            string kind = "manual";
            DateTime created = fi.LastWriteTime;
            int us = name.LastIndexOf('_');
            if (us > 0)
            {
                kind = name.Substring(us + 1);
                var stamp = name.Substring(0, us);
                if (stamp.Length >= 15 &&
                    DateTime.TryParseExact(stamp.Substring(0, 15), "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    created = dt;
            }
            list.Add(new BackupInfo { Path = f, Created = created, Kind = kind, Size = fi.Length });
        }
        return list.OrderByDescending(b => b.Created).ToList();
    }

    /// <summary>The newest backup made on purpose (manual or automatic) - what "restore the latest" uses.</summary>
    public static BackupInfo Latest() => List().FirstOrDefault(b => b.Kind == "manual" || b.Kind == "auto");

    /// <summary>Once a day (at start) an automatic backup is made; the last 10 of each automatic kind are kept.</summary>
    public static void AutoIfNeeded()
    {
        var last = List().FirstOrDefault(b => b.Kind == "auto");
        if (last == null || (DateTime.Now - last.Created).TotalHours >= 24) Create("auto");
        Prune("auto", 10);
        Prune("beforerestore", 10);
        Prune("beforereset", 10);
    }

    public static void Prune(string kind, int keep)
    {
        foreach (var b in List().Where(x => x.Kind == kind).Skip(keep))
        {
            try { File.Delete(b.Path); } catch { }
        }
    }
}
