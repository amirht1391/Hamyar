using System.Diagnostics;
using System.Windows;

namespace Hamyar;

/// <summary>Runs the tasks attached to an item.</summary>
public static class TaskRunner
{
    public static void RunAll(IEnumerable<TaskAction> tasks)
    {
        foreach (var t in tasks)
        {
            try { Run(t); }
            catch (Exception ex) { Log.Write($"Task failed ({t.Type} {t.Target}): {ex.Message}"); }
        }
    }

    /// <summary>Lets the user type "notepad.exe C:\\a.txt" in one box: splits program and arguments.</summary>
    static (string file, string args) SplitCommand(string target, string args)
    {
        target = (target ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(args)) return (target, args);
        if (target.StartsWith("\""))
        {
            int end = target.IndexOf('"', 1);
            if (end > 0) return (target.Substring(1, end - 1), target.Substring(end + 1).Trim());
        }
        if (File.Exists(target) || Directory.Exists(target) || target.Contains("://")) return (target, "");
        int sp = target.IndexOf(' ');
        return sp > 0 ? (target.Substring(0, sp), target.Substring(sp + 1).Trim()) : (target, "");
    }

    static void Run(TaskAction t)
    {
        var full = (t.Target + " " + t.Args).Trim();
        switch (t.Type)
        {
            case TaskType.OpenTarget:
                var (file, args) = SplitCommand(t.Target, t.Args);
                Process.Start(new ProcessStartInfo(file) { Arguments = args, UseShellExecute = true });
                break;
            case TaskType.Command:
                Process.Start(new ProcessStartInfo("cmd.exe") { Arguments = "/c " + full, UseShellExecute = true });
                break;
            case TaskType.PowerShell:
                var cmd = full.Replace("\"", "\\\"");
                Process.Start(new ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{cmd}\"",
                    UseShellExecute = true
                });
                break;
            case TaskType.Message:
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    Dialogs.Info(null, t.Target)));
                break;
        }
    }
}
