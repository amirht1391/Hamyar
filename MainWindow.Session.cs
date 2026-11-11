using System.Windows;

namespace Hamyar;

// Study sessions: the setup dialog, the focus screen, the history
public partial class MainWindow
{
    void StartSession_Click(object sender, RoutedEventArgs e) => RunSession(null);

    void TopicSession_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Topic t) return;
        RunSession(t);
    }

    void RunSession(Topic preselect)
    {
        var topics = Db.GetTopics(0);
        if (preselect != null) preselect = topics.FirstOrDefault(x => x.Id == preselect.Id) ?? preselect;
        var setup = new SessionSetupDialog(this, topics, preselect);
        if (setup.ShowDialog() != true || setup.Result == null) return;

        new FocusWindow(this, setup.Result).ShowDialog();

        // flashcards may have been made, the topic may have been reviewed
        LoadCourses();
        RefreshTopics();
        RefreshCards();
    }

    void SessionHistory_Click(object sender, RoutedEventArgs e) => new SessionHistoryWindow(this).ShowDialog();
}
