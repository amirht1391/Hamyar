namespace Hamyar;

public static class Log
{
    public static string FilePath => System.IO.Path.Combine(Db.Dir, "log.txt");

    public static void Write(string msg)
    {
        try
        {
            Directory.CreateDirectory(Db.Dir);
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length > 1_000_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
