using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

// Courses (shared by Study and Flashcards) and the Study page
public partial class MainWindow
{
    long _courseId;                 // selected course on the Study page, 0 = all
    long _cardCourseId;             // selected course on the Flashcards page, 0 = all
    long _cardSubjectId;            // selected subject inside that course, 0 = "All <subjects>" (the default chip)
    List<Subject> _cardSubjects = new();
    List<Course> _courses = new();       // Study courses     (scope 0)
    List<Course> _cardCourses = new();   // Flashcard courses (scope 1) - a different list on purpose
    bool _dueOnly = true;

    // ------------------------------------------------------------------ courses
    void LoadCourses()
    {
        _courses = Db.GetCourses(0);
        _cardCourses = Db.GetCourses(1);
        if (_courseId != 0 && !_courses.Any(c => c.Id == _courseId)) _courseId = 0;
        if (_cardCourseId != 0 && !_cardCourses.Any(c => c.Id == _cardCourseId)) _cardCourseId = 0;

        FillCourseChips(CoursesPanel, "courses", _courses, _courseId, Course_Checked, NewCourse_Click);
        FillCourseChips(CardCoursesPanel, "cardcourses", _cardCourses, _cardCourseId, CardCourse_Checked, NewCardCourse_Click);

        LoadCardSubjects();
        RefreshTopics();
        RefreshCards();
    }

    // ------------------------------------------------------------------ flashcard subjects
    /// <summary>Rebuilds the subject chips of the selected flashcard course: the fixed-first "All ..." chip, the
    /// course's subjects, and "+". With no single course selected there are no subjects, so the row is hidden.</summary>
    void LoadCardSubjects()
    {
        var course = _cardCourses.FirstOrDefault(c => c.Id == _cardCourseId);
        _cardSubjects = course != null ? Db.GetSubjects(course.Id) : new List<Subject>();
        if (_cardSubjectId != 0 && !_cardSubjects.Any(x => x.Id == _cardSubjectId)) _cardSubjectId = 0;

        CardSubjectsPanel.Children.Clear();
        CardSubjectsPanel.Visibility = Vis(course != null);
        if (course == null) return;

        var all = new RadioButton { Content = course.AllSubjectsLabel, Tag = 0L, GroupName = "cardsubjects", IsChecked = _cardSubjectId == 0, Style = UI.S("Chip") };
        all.Checked += CardSubject_Checked;
        ChipBar.Attach(CardSubjectsPanel, all, null, null, () => { _cardSubjectId = 0; RenameCardSubject_Click(null, null); });
        CardSubjectsPanel.Children.Add(all);
        foreach (var sj in _cardSubjects)
        {
            var rb = new RadioButton { Content = sj.Name, Tag = sj.Id, GroupName = "cardsubjects", IsChecked = sj.Id == _cardSubjectId, Style = UI.S("Chip") };
            rb.Checked += CardSubject_Checked;
            ChipBar.Attach(CardSubjectsPanel, rb,
                (id, name) => { Db.RenameSubject(id, name); _cardSubjects = Db.GetSubjects(_cardCourseId); RefreshCards(); },
                ids => { Db.SetSubjectOrder(ids); _cardSubjects = Db.GetSubjects(_cardCourseId); });
            CardSubjectsPanel.Children.Add(rb);
        }
        var plus = UI.IconBtn("\uE710", NewCardSubject_Click, "subj_new");
        plus.Margin = new Thickness(6, 3, 3, 3);
        CardSubjectsPanel.Children.Add(plus);
    }

    void CardSubject_Checked(object sender, RoutedEventArgs e)
    {
        _cardSubjectId = (long)((RadioButton)sender).Tag;
        RefreshCards();
    }

    void NewCardSubject_Click(object sender, RoutedEventArgs e)
    {
        if (_cardCourseId == 0) return;
        var sd = new SubjectDialog(this, "", ExamDifficulty.Medium, DateTime.Today);
        if (sd.ShowDialog() != true) return;
        _cardSubjectId = Db.AddSubject(_cardCourseId, sd.SubjectName, sd.Difficulty, sd.StartDate);
        LoadCardSubjects();
        RefreshCards();
    }

    void RenameCardSubject_Click(object sender, RoutedEventArgs e)
    {
        var course = _cardCourses.FirstOrDefault(c => c.Id == _cardCourseId);
        if (course == null) { Dialogs.Info(this, Loc.Get("card_pick_course_first")); return; }

        if (_cardSubjectId == 0)
        {
            // the default chip: the word "All" is fixed, only the word(s) after it can change
            var word = InputDialog.AskWithPrefix(this, Loc.Get("subj_rename_all_title"), Loc.Get("subj_all_prefix"),
                string.IsNullOrWhiteSpace(course.SubjectAllName) ? Loc.Get("subj_all_default") : course.SubjectAllName);
            if (word == null) return;
            Db.SetCourseSubjectAll(course.Id, word);        // empty = back to the default word
            LoadCourses();
            return;
        }
        var cur = _cardSubjects.FirstOrDefault(x => x.Id == _cardSubjectId);
        if (cur == null) return;
        var sd = new SubjectDialog(this, cur.Name, cur.Difficulty, cur.StartDate);
        if (sd.ShowDialog() != true) return;
        Db.RenameSubject(cur.Id, sd.SubjectName);
        Db.SetSubjectDifficulty(cur.Id, sd.Difficulty);
        if (sd.StartDate.Date != cur.StartDate.Date)
        {
            // new start date of the subject: every card in it is re-planned at once
            Db.SetSubjectStart(cur.Id, sd.StartDate);
            var cards = Db.GetCards(_cardCourseId, cur.Id);
            var exam = ExamById(course.ExamId);
            var now = DateTime.Now;
            foreach (var k in cards) ReviewEngine.ChangeStartDate(k, sd.StartDate.Date, exam, now);
            Db.SaveCardsState(cards);
        }
        LoadCardSubjects();
        RefreshCards();
    }

    void DeleteCardSubject_Click(object sender, RoutedEventArgs e)
    {
        if (_cardCourseId == 0) { Dialogs.Info(this, Loc.Get("card_pick_course_first")); return; }
        var cur = _cardSubjects.FirstOrDefault(x => x.Id == _cardSubjectId);
        if (cur == null) { Dialogs.Info(this, Loc.Get("subj_cannot_delete_all")); return; }   // the default chip is permanent
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("subj_confirm_delete"), cur.Name))) return;
        Db.DeleteSubject(cur.Id);
        _cardSubjectId = 0;
        LoadCardSubjects();
        RefreshCards();
    }

    void FillCourseChips(Panel host, string group, List<Course> courses, long selected, RoutedEventHandler onChecked, RoutedEventHandler onPlus)
    {
        host.Children.Clear();

        var all = new RadioButton { GroupName = group, Tag = 0L, IsChecked = selected == 0, Style = UI.S("Chip") };
        UI.Bind(all, ContentControl.ContentProperty, "study_all");
        all.Checked += onChecked;
        host.Children.Add(all);

        foreach (var c in courses)
        {
            var rb = new RadioButton
            {
                Content = c.Name,
                Tag = c.Id,
                GroupName = group,
                IsChecked = c.Id == selected,
                Style = UI.S("Chip")
            };
            rb.Checked += onChecked;
            ChipBar.Attach(host, rb,
                (id, name) => { Db.RenameCourse(id, name); _courses = Db.GetCourses(0); _cardCourses = Db.GetCourses(1); RefreshTopics(); RefreshCards(); },
                ids => { Db.SetCourseOrder(ids); _courses = Db.GetCourses(0); _cardCourses = Db.GetCourses(1); });
            host.Children.Add(rb);
        }

        // "+" is always the last chip
        var plus = UI.IconBtn("\uE710", onPlus, "study_new_course");
        plus.Margin = new Thickness(6, 3, 3, 3);
        host.Children.Add(plus);
    }

    void Course_Checked(object sender, RoutedEventArgs e)
    {
        _courseId = (long)((RadioButton)sender).Tag;
        RefreshTopics();
    }

    // ----- Study courses
    void NewCourse_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, Loc.Get("course_name"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        _courseId = Db.AddCourse(name, 0);
        LoadCourses();
    }

    void RenameCourse_Click(object sender, RoutedEventArgs e)
    {
        var cur = _courses.FirstOrDefault(c => c.Id == _courseId);
        if (cur == null) return;      // "All courses" cannot be renamed
        var name = InputDialog.Ask(this, Loc.Get("course_name"), cur.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        Db.RenameCourse(cur.Id, name);
        LoadCourses();
    }

    void DeleteCourse_Click(object sender, RoutedEventArgs e)
    {
        var cur = _courses.FirstOrDefault(c => c.Id == _courseId);
        if (cur == null) return;
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_course"), cur.Name))) return;
        Db.DeleteCourse(cur.Id);
        _courseId = 0;
        LoadCourses();
    }

    // ----- Flashcard courses
    void NewCardCourse_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, Loc.Get("course_name"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        _cardCourseId = Db.AddCourse(name, 1);
        _cardSubjectId = 0;
        LoadCourses();
    }

    void RenameCardCourse_Click(object sender, RoutedEventArgs e)
    {
        var cur = _cardCourses.FirstOrDefault(c => c.Id == _cardCourseId);
        if (cur == null) return;
        var name = InputDialog.Ask(this, Loc.Get("course_name"), cur.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        Db.RenameCourse(cur.Id, name);
        LoadCourses();
    }

    void DeleteCardCourse_Click(object sender, RoutedEventArgs e)
    {
        var cur = _cardCourses.FirstOrDefault(c => c.Id == _cardCourseId);
        if (cur == null) return;
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_course"), cur.Name))) return;
        Db.DeleteCourse(cur.Id);
        _cardCourseId = 0;
        _cardSubjectId = 0;
        LoadCourses();
    }

    // one review method + one optional exam for the WHOLE course (not per card)
    void CourseSettings_Click(object sender, RoutedEventArgs e)
    {
        var cur = _cardCourses.FirstOrDefault(c => c.Id == _cardCourseId);
        if (cur == null)
        {
            Dialogs.Info(this, Loc.Get("card_pick_course_first"));
            return;
        }
        if (new CourseSettingsDialog(this, cur, _exams).ShowDialog() == true)
        {
            LoadCourses();
            RefreshCards();
        }
    }

    void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_cardCourses.Count == 0)
        {
            NewCardCourse_Click(null, null);          // cards need a course: ask for one first
            if (_cardCourses.Count == 0) return;
        }
        var targetId = _cardCourseId;
        if (targetId == 0)
        {
            Dialogs.Info(this, Loc.Get("card_pick_course_first"));
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV (*.csv)|*.csv|" + Loc.Get("all_files") + " (*.*)|*.*" };
        if (dlg.ShowDialog(this) != true) return;

        var (added, skipped, newSubjects, error) = CsvImport.ImportCards(dlg.FileName, targetId, _cardSubjectId);
        if (error != null) { Dialogs.Info(this, error); return; }

        LoadCardSubjects();
        RefreshCards();
        var msg = string.Format(Loc.Get("card_import_done"), added, skipped);
        if (newSubjects > 0) msg += "\n" + string.Format(Loc.Get("card_import_new_subjects"), newSubjects);
        Dialogs.Info(this, msg);
    }

    /// <summary>Saves ALL cards of the selected course (every subject) as a CSV file.</summary>
    void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_cardCourseId == 0)
        {
            Dialogs.Info(this, Loc.Get("card_pick_course_first"));
            return;
        }
        var course = _cardCourses.FirstOrDefault(c => c.Id == _cardCourseId);
        var safeName = string.Concat((course?.Name ?? "cards").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (safeName.Length == 0) safeName = "cards";

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = safeName + ".csv",
            DefaultExt = ".csv",
            AddExtension = true,
            Filter = Loc.Get("card_export_filter_basic") + "|*.csv|" + Loc.Get("card_export_filter_full") + "|*.csv"
        };
        if (dlg.ShowDialog(this) != true) return;

        var (count, error) = CsvExport.Export(dlg.FileName, _cardCourseId, dlg.FilterIndex == 2);
        Dialogs.Info(this, error ?? string.Format(Loc.Get("card_export_done"), count));
    }

    // ------------------------------------------------------------------ badges (how many things are due)
    int _dueTopicCount, _dueCardCount;

    void UpdateBadges()
    {
        int s = _dueTopicCount = Db.CountDueTopics();
        int k = _dueCardCount = Db.CountDueCards();
        StudyBadge.Visibility = Vis(s > 0);
        StudyBadgeText.Text = s.ToString();
        CardsBadge.Visibility = Vis(k > 0);
        CardsBadgeText.Text = k.ToString();
    }

    // ------------------------------------------------------------------ study page
    void StudyFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _dueOnly = (string)((RadioButton)sender).Tag == "due";
        RefreshTopics();
    }

    void RefreshTopics()
    {
        if (TopicsList == null) return;
        var topics = Db.GetTopics(_courseId);
        if (_dueOnly) topics = topics.Where(t => t.IsDue).ToList();
        var sv = FindScroll(TopicsList);
        double offset = sv?.VerticalOffset ?? 0;
        TopicsList.ItemsSource = topics;
        if (sv != null && offset > 0)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => sv.ScrollToVerticalOffset(offset)));

        UpdateBadges();      // reads the due-counts once; the summary reuses them
        StudySummary.Text = string.Format(Loc.Get("study_summary"), _dueTopicCount);
        var windowReason = StudyWindow.Check(DateTime.Now);
        if (windowReason != StudyWindow.Reason.Open)
            StudySummary.Text += "   ·   " + StudyWindow.ReasonText(windowReason);
        int focusedToday = Db.StudySecondsSince(DateTime.Today);
        if (focusedToday > 0) StudySummary.Text += "   ·   ⏱ " + Loc.Get("fs_today") + " " + FocusUtil.Dur(focusedToday);

        string hint = null;
        if (_courses.Count == 0) hint = "study_empty_courses";
        else if (topics.Count == 0) hint = _dueOnly ? "study_empty_due" : "study_empty_all";
        StudyEmptyHint.Text = hint == null ? "" : Loc.Get(hint);
        StudyEmptyHint.Visibility = Vis(hint != null);
    }

    void AddTopic_Click(object sender, RoutedEventArgs e)
    {
        if (_courses.Count == 0)
        {
            NewCourse_Click(null, null);          // a topic needs a course: ask for one first
            if (_courses.Count == 0) return;
        }
        var t = new Topic();
        if (new TopicDialog(this, t, _courses, _courseId, _exams).ShowDialog() == true) RefreshTopics();
    }

    void EditTopic_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Topic t) return;
        if (new TopicDialog(this, t, _courses, _courseId, _exams).ShowDialog() == true) RefreshTopics();
    }

    void DeleteTopic_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Topic t) return;
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_topic"), t.Title))) return;
        Db.DeleteTopic(t.Id);
        RefreshTopics();
    }

    void TopicReview_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Topic t) return;
        // DateTime.Now (not .Today): the Fixed table can include minute/hour steps, and Smart can too under exam pressure - not just by day.
        ReviewEngine.Answer(t, true, DateTime.Now, ExamById(t.ExamId));
        Db.SaveTopic(t);
        RefreshTopics();
    }
}
