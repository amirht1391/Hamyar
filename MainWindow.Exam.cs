using System.Windows;
using System.Windows.Controls;

namespace Hamyar;

// Exam tab: define exams (title, date/time, difficulty); Study topics and Flashcard courses link to one.
public partial class MainWindow
{
    List<Exam> _exams = new();
    string _examMsg = "";      // shown once at the top of the Exam tab: how many reviews were just re-planned

    public Exam ExamById(long? id) => id.HasValue ? _exams.FirstOrDefault(x => x.Id == id.Value) : null;

    void LoadExams()
    {
        _exams = Db.GetExams();
    }

    void RefreshExams()
    {
        LoadExams();
        if (ExamHost == null) return;
        BuildExamList();
    }

    void BuildExamList()
    {
        var h = ExamHost;
        h.Children.Clear();

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 14) };
        addRow.Children.Add(UI.Btn("exam_add", (s, e) => AddExam_Click(s, e)));
        h.Children.Add(addRow);
        if (_examMsg.Length > 0)
        {
            var note = new TextBlock { Text = _examMsg, Margin = new Thickness(4, -6, 0, 12), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
            note.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            h.Children.Add(note);
            _examMsg = "";
        }

        if (_exams.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = Loc.Get("exam_empty"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 10, 0, 0)
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            h.Children.Add(empty);
            return;
        }

        foreach (var ex in _exams)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10),
                BorderThickness = new Thickness(ex.IsPast ? 1 : 2)
            };
            card.SetResourceReference(Border.BackgroundProperty, "CardBrush");
            card.SetResourceReference(Border.BorderBrushProperty, ex.IsPast ? "LineBrush" : "AccentBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = ex.Title, FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });

            var sub = new TextBlock { FontSize = 13, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            sub.Text = ex.DateText + "  ·  " + ex.DifficultyText + "  ·  " + ex.WhenText;
            body.Children.Add(sub);

            var counts = new TextBlock { FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0) };
            counts.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
            counts.Text = ex.CountsText;
            body.Children.Add(counts);

            if (!string.IsNullOrWhiteSpace(ex.Notes))
            {
                var notes = new TextBlock { Text = ex.Notes, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontStyle = FontStyles.Italic };
                notes.SetResourceReference(TextBlock.ForegroundProperty, "SubFgBrush");
                body.Children.Add(notes);
            }

            Grid.SetColumn(body, 0);
            grid.Children.Add(body);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var editBtn = UI.IconBtn("\uE70F", (s, e) => EditExam_Click(ex), "edit");
            var delBtn = UI.IconBtn("\uE74D", (s, e) => DeleteExam_Click(ex), "delete");
            actions.Children.Add(editBtn);
            actions.Children.Add(delBtn);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            card.Child = grid;
            h.Children.Add(card);
        }
    }

    void AddExam_Click(object sender, RoutedEventArgs e)
    {
        var ex = new Exam();
        if (new ExamDialog(this, ex).ShowDialog() == true)
        {
            Db.SaveExam(ex);
            RefreshExams();
        }
    }

    void EditExam_Click(Exam ex)
    {
        // the dialog edits the object directly: remember the old values so a cancelled edit leaves nothing half-changed
        var oldWhen = ex.When; var oldDiff = ex.Difficulty; var oldTitle = ex.Title; var oldNotes = ex.Notes;
        if (new ExamDialog(this, ex).ShowDialog() == true)
        {
            Db.SaveExam(ex);
            // date / time / difficulty changed: every linked topic and flashcard gets a new next review right away
            int n = (ex.When != oldWhen || ex.Difficulty != oldDiff) ? Recalc.ForExam(ex.Id, ex) : 0;
            if (n > 0) _examMsg = string.Format(Loc.Get("exam_resched"), n);
            RefreshExams();
            if (n > 0) { RefreshTopics(); RefreshCards(); }
        }
        else { ex.When = oldWhen; ex.Difficulty = oldDiff; ex.Title = oldTitle; ex.Notes = oldNotes; }
    }

    void DeleteExam_Click(Exam ex)
    {
        if (!Dialogs.Confirm(this, string.Format(Loc.Get("confirm_del_exam"), ex.Title))) return;
        int n = Recalc.ForExam(ex.Id, null);      // before the delete: the linked items are still found; they go back to the normal pace
        Db.DeleteExam(ex.Id);
        if (n > 0) _examMsg = string.Format(Loc.Get("exam_resched"), n);
        RefreshExams();
        RefreshTopics();   // topics/courses that pointed to this exam just lost the link
        LoadCourses();
    }
}
