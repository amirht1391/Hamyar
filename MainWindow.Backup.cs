using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hamyar;

// Settings -> Backup, restore and reset
public partial class MainWindow
{
    StackPanel _backupList;

    void BuildBackupCard(Panel host)
    {
        var card = NewCard(host, "sec_backup", "\uE74E");
        Note(card, "bk_note");

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        buttons.Children.Add(UI.Btn("bk_now", BackupNow_Click));
        buttons.Children.Add(UI.Btn("bk_restore_latest", RestoreLatest_Click, true));
        buttons.Children.Add(UI.Btn("bk_restore_file", RestoreFile_Click, true));
        var reset = UI.Btn("bk_reset", Reset_Click);
        reset.Background = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        reset.Foreground = Brushes.White;
        buttons.Children.Add(reset);
        card.Children.Add(buttons);

        SubHeader(card, "bk_list_title");
        _backupList = new StackPanel();
        card.Children.Add(_backupList);
        card.Children.Add(UI.Link("\uE8B7", "link_backups", () => UI.OpenFolder(Backups.Dir)));
        RefreshBackupList();
    }

    void RefreshBackupList()
    {
        if (_backupList == null) return;
        _backupList.Children.Clear();

        var all = Backups.List();
        if (all.Count == 0)
        {
            var empty = new TextBlock { Text = Loc.Get("bk_empty"), Margin = new Thickness(4, 4, 0, 8) };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            _backupList.Children.Add(empty);
            return;
        }
        foreach (var b in all.Take(40)) _backupList.Children.Add(BackupRow(b));
    }

    FrameworkElement BackupRow(BackupInfo b)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var kind = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, MinWidth = 90 };
        kind.SetResourceReference(Border.BackgroundProperty, "InputBrush");
        kind.Child = new TextBlock { Text = Loc.Get("bk_kind_" + b.Kind), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = Jal.Stamp(b.Created),
            FontWeight = FontWeights.SemiBold,
            FlowDirection = System.Windows.FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left
        });
        var size = new TextBlock
        {
            Text = Math.Max(1, b.Size / 1024) + " KB   ·   " + b.Name,
            FontSize = 11.5,
            FlowDirection = System.Windows.FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        size.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
        info.Children.Add(size);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(UI.Btn("bk_restore", (s, e) => RestoreBackup(b.Path), true));
        actions.Children.Add(UI.Btn("bk_delete", (s, e) => DeleteBackup(b), true));

        Grid.SetColumn(info, 1);
        Grid.SetColumn(actions, 2);
        row.Children.Add(kind);
        row.Children.Add(info);
        row.Children.Add(actions);
        return row;
    }

    // ------------------------------------------------------------------ actions
    /// <summary>Asks where to put the user's OWN copy of a backup file. Returns false if the user cancelled.</summary>
    bool SaveOwnCopy(string source)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.Get("bk_save_copy"),
            Filter = "Hamyar backup (*.db)|*.db",
            FileName = System.IO.Path.GetFileName(source)
        };
        if (dlg.ShowDialog(this) != true) return false;
        File.Copy(source, dlg.FileName, true);
        return true;
    }

    void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Backups.Create("manual");                 // the app's own copy
            bool copied = SaveOwnCopy(path);                     // + the user's own copy (optional here)
            RefreshBackupList();
            Dialogs.Info(this, string.Format(Loc.Get(copied ? "bk_saved_copy" : "bk_saved"), System.IO.Path.GetFileName(path)));
        }
        catch (Exception ex)
        {
            Log.Write("Backup: " + ex);
            Dialogs.Info(this, Loc.Get("bk_failed") + "\n" + ex.Message);
        }
    }

    void RestoreLatest_Click(object sender, RoutedEventArgs e)
    {
        var latest = Backups.Latest();
        if (latest == null) { Dialogs.Info(this, Loc.Get("bk_none")); return; }
        RestoreBackup(latest.Path);
    }

    void RestoreFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Hamyar backup (*.db)|*.db|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        RestoreBackup(dlg.FileName);
    }

    /// <summary>Restore: the CURRENT data is always saved as a new backup first, then replaced.</summary>
    void RestoreBackup(string path)
    {
        if (!Db.IsBackupFile(path)) { Dialogs.Info(this, Loc.Get("bk_bad_file")); return; }
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("bk_confirm_restore"), System.IO.Path.GetFileName(path)), false)) return;
        try
        {
            Backups.Create("beforerestore");
            Db.RestoreFrom(path);
            ReloadEverything();
            Dialogs.Info(this, Loc.Get("bk_restored"));
        }
        catch (Exception ex)
        {
            Log.Write("Restore: " + ex);
            Dialogs.Info(this, Loc.Get("bk_failed") + "\n" + ex.Message);
        }
    }

    void DeleteBackup(BackupInfo b)
    {
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_backup"), b.Name))) return;
        try { File.Delete(b.Path); } catch (Exception ex) { Log.Write("Delete backup: " + ex.Message); }
        RefreshBackupList();
    }

    /// <summary>Reset: a backup is mandatory. The app saves one, the user must save his own copy, then a last confirmation.</summary>
    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (!Dialogs.Confirm(this, Loc.Get("bk_reset_step1"), false)) return;
        try
        {
            var auto = Backups.Create("beforereset");
            RefreshBackupList();
            if (!SaveOwnCopy(auto))
            {
                Dialogs.Info(this, Loc.Get("bk_reset_need_copy"));
                return;
            }
            if (!Dialogs.Confirm(this, Loc.Get("bk_reset_final"))) return;

            Db.ResetAll();
            ReloadEverything();
            Dialogs.Info(this, Loc.Get("bk_reset_done"));
        }
        catch (Exception ex)
        {
            Log.Write("Reset: " + ex);
            Dialogs.Info(this, Loc.Get("bk_failed") + "\n" + ex.Message);
        }
    }

    /// <summary>After a restore / reset: re-read settings and refresh every page.</summary>
    void ReloadEverything()
    {
        Loc.I.SetLang(Cfg.Lang);
        ThemeManager.Apply(Cfg.Theme, Cfg.Accent, Cfg.Backdrop);
        Glass.Apply(this, Cfg.Theme == 0, Cfg.Backdrop);

        _boardId = 0;
        _courseId = 0;
        _cardCourseId = 0;
        _cardSubjectId = 0;
        LoadBoards();
        LoadCourses();
        ApplyTabs();
        Safe(BuildSettings);
        Safe(BuildHelp);
        Safe(RefreshExams);
        Safe(BuildTimetable);
    }
}
