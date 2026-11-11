namespace Hamyar;

/// <summary>
/// Smart recalculation of the upcoming reviews when something they depend on changes: an exam is added / moved /
/// given another difficulty / removed, or a Study topic / flashcard course is linked to another exam.
/// Only the NEXT review date moves; ease, interval and history are kept. All writes go in one transaction.
/// </summary>
public static class Recalc
{
    /// <summary>`exam` = the exam as it is NOW (null when it was just deleted: call this BEFORE deleting it, so the
    /// affected items can still be found). Returns how many reviews got a new date.</summary>
    public static int ForExam(long examId, Exam exam)
    {
        var now = DateTime.Now;
        var topics = Db.GetTopics().Where(t => t.ExamId == examId).ToList();
        var cards = Db.GetCards().Where(c => c.ExamId == examId).ToList();
        return Apply(topics, cards, exam, now);
    }

    /// <summary>A flashcard course was linked to another exam (or to none).</summary>
    public static int ForCourse(long courseId, Exam exam)
    {
        var cards = Db.GetCards(courseId);
        return Apply(new List<Topic>(), cards, exam, DateTime.Now);
    }

    static int Apply(List<Topic> topics, List<Card> cards, Exam exam, DateTime now)
    {
        var changedTopics = topics.Where(t => ReviewEngine.Reschedule(t, exam, now)).ToList();
        var changedCards = cards.Where(c => ReviewEngine.Reschedule(c, exam, now)).ToList();
        if (changedTopics.Count + changedCards.Count > 0) Db.UpdateNextReviews(changedTopics, changedCards);
        return changedTopics.Count + changedCards.Count;
    }
}
