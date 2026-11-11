using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Hamyar;

public static class Db
{
    static readonly string FilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskAlarm", "data.db");
    const string Fmt = "yyyy-MM-dd HH:mm:ss";

    static SqliteConnection Open()
    {
        var c = new SqliteConnection("Data Source=" + FilePath);
        c.Open();
        // WAL + NORMAL: a write no longer waits for a full disk flush, so saving / rescheduling many rows is fast
        // (the data stays safe against app crashes; only a power cut in the same instant could lose the last write).
        using (var p = c.CreateCommand()) { p.CommandText = "PRAGMA synchronous=NORMAL"; p.ExecuteNonQuery(); }
        return c;
    }

    /// <summary>Runs many writes inside ONE transaction (one disk sync instead of one per row).</summary>
    static void InTransaction(Action<SqliteConnection, SqliteTransaction> work)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        work(c, tx);
        tx.Commit();
    }

    static void Run(SqliteConnection c, SqliteTransaction tx, string sql, params (string, object)[] p)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    static void Exec(string sql, params (string, object)[] p)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    static object Scalar(string sql, params (string, object)[] p)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (k, v) in p) cmd.Parameters.AddWithValue(k, v ?? DBNull.Value);
        return cmd.ExecuteScalar();
    }

    public static string Dir => System.IO.Path.GetDirectoryName(FilePath);

    public static void Init()
    {
        Directory.CreateDirectory(Dir);
        try { Scalar("PRAGMA journal_mode=WAL"); } catch (Exception ex) { Log.Write("WAL: " + ex.Message); }
        ClearSettingsCache();
        Exec(@"CREATE TABLE IF NOT EXISTS Boards(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);
               CREATE TABLE IF NOT EXISTS Items(Id INTEGER PRIMARY KEY AUTOINCREMENT, BoardId INTEGER NOT NULL,
                   Title TEXT, Notes TEXT, AlarmAt TEXT, Repeat INTEGER, Sound TEXT, Enabled INTEGER);
               CREATE TABLE IF NOT EXISTS Tasks(Id INTEGER PRIMARY KEY AUTOINCREMENT, ItemId INTEGER NOT NULL,
                   Type INTEGER, Target TEXT, Args TEXT);
               CREATE TABLE IF NOT EXISTS Settings(Key TEXT PRIMARY KEY, Value TEXT);
               CREATE TABLE IF NOT EXISTS Courses(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);
               CREATE TABLE IF NOT EXISTS Topics(Id INTEGER PRIMARY KEY AUTOINCREMENT, CourseId INTEGER NOT NULL,
                   Title TEXT, Notes TEXT, Kind INTEGER, StudiedOn TEXT, Step INTEGER, LastReview TEXT, NextReview TEXT);
               CREATE TABLE IF NOT EXISTS Cards(Id INTEGER PRIMARY KEY AUTOINCREMENT, CourseId INTEGER NOT NULL,
                   Front TEXT, Back TEXT, Box INTEGER, StartDate TEXT, NextReview TEXT, Reviews INTEGER, Lapses INTEGER);
               CREATE TABLE IF NOT EXISTS Timetable(Day INTEGER NOT NULL, Slot INTEGER NOT NULL, Subject TEXT,
                   PRIMARY KEY(Day, Slot));
               CREATE TABLE IF NOT EXISTS Exams(Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT NOT NULL,
                   WhenAt TEXT NOT NULL, Difficulty INTEGER NOT NULL DEFAULT 1, Notes TEXT);");
        Exec(@"CREATE TABLE IF NOT EXISTS PrayTimes(Date TEXT PRIMARY KEY, Fajr TEXT, Sunrise TEXT, Dhuhr TEXT,
                   Sunset TEXT, Maghrib TEXT, Midnight TEXT)");
        try { Exec("ALTER TABLE Topics ADD COLUMN Log TEXT"); } catch { }      // older databases: add the review-log column
        try { Exec("ALTER TABLE Courses ADD COLUMN Scope INTEGER NOT NULL DEFAULT 0"); } catch { }   // 0 = study, 1 = flashcards
        // review-method columns (older databases: added with safe defaults, existing data keeps behaving exactly as before)
        try { Exec("ALTER TABLE Topics ADD COLUMN Method INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN Ease REAL NOT NULL DEFAULT 2.5"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN IntervalDays REAL NOT NULL DEFAULT 1"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN Reps INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN Lapses INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN CustomIv TEXT NOT NULL DEFAULT ''"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN Mastery INTEGER NOT NULL DEFAULT 3"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN ExamId INTEGER"); } catch { }
        Exec(@"CREATE TABLE IF NOT EXISTS StudySessions(Id INTEGER PRIMARY KEY AUTOINCREMENT, TopicId INTEGER, CourseId INTEGER,
                   Title TEXT, StartedAt TEXT, Seconds INTEGER, PlannedSeconds INTEGER, Mode INTEGER, Locked INTEGER, Rating INTEGER,
                   Notes TEXT, Checklist TEXT, Cards INTEGER, Ink BLOB)");
        Exec("CREATE TABLE IF NOT EXISTS Plans(Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT, Start TEXT, Minutes INTEGER, Notes TEXT)");
        try { Exec("ALTER TABLE Items ADD COLUMN CustomDays TEXT NOT NULL DEFAULT ''"); } catch { }
        try { Exec("ALTER TABLE Topics ADD COLUMN Difficulty INTEGER NOT NULL DEFAULT 1"); } catch { }
        try { Exec("ALTER TABLE Subjects ADD COLUMN Difficulty INTEGER NOT NULL DEFAULT 1"); } catch { }
        // flashcard courses carry ONE method for every card in them (Scope=1); Study courses (Scope=0) don't use these.
        try { Exec("ALTER TABLE Courses ADD COLUMN Method INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Courses ADD COLUMN CustomIv TEXT NOT NULL DEFAULT ''"); } catch { }
        try { Exec("ALTER TABLE Courses ADD COLUMN ExamId INTEGER"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN Method INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN Ease REAL NOT NULL DEFAULT 2.5"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN IntervalDays REAL NOT NULL DEFAULT 1"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN Reps INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN Step INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN LastReview TEXT"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN Log TEXT NOT NULL DEFAULT ''"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN CustomIv TEXT NOT NULL DEFAULT ''"); } catch { }
        // flashcard subjects (lessons / chapters inside a flashcard course)
        Exec("CREATE TABLE IF NOT EXISTS Subjects(Id INTEGER PRIMARY KEY AUTOINCREMENT, CourseId INTEGER NOT NULL, Name TEXT NOT NULL)");
        try { Exec("ALTER TABLE Cards ADD COLUMN SubjectId INTEGER NOT NULL DEFAULT 0"); } catch { }          // 0 = no subject
        try { Exec("ALTER TABLE Courses ADD COLUMN SubjAll TEXT NOT NULL DEFAULT ''"); } catch { }           // 2nd word of \"All ...\"
        Exec("CREATE TABLE IF NOT EXISTS PlanSets(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT, Date TEXT, Daily INTEGER NOT NULL DEFAULT 0)");
        try { Exec("ALTER TABLE Plans ADD COLUMN SetId INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Plans ADD COLUMN DayOffset INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Courses ADD COLUMN Pos INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("UPDATE Courses SET Pos=Id WHERE Pos=0"); } catch { }
        try { Exec("ALTER TABLE Subjects ADD COLUMN Pos INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("UPDATE Subjects SET Pos=Id WHERE Pos=0"); } catch { }
        try { Exec("ALTER TABLE Subjects ADD COLUMN StartDate TEXT NOT NULL DEFAULT ''"); } catch { }
        try { Exec("ALTER TABLE Cards ADD COLUMN Pos INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("UPDATE Cards SET Pos=Id WHERE Pos=0"); } catch { }
        try { Exec("ALTER TABLE Boards ADD COLUMN Pos INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("UPDATE Boards SET Pos=Id WHERE Pos=0"); } catch { }
        try { Exec("ALTER TABLE Plans ADD COLUMN Muted INTEGER NOT NULL DEFAULT 0"); } catch { }
        // Pos = the row's place in its plan (so a row can be inserted in the middle); Free = an extra row that does not move the others
        try { Exec("ALTER TABLE Plans ADD COLUMN Pos INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE Plans ADD COLUMN Free INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("UPDATE Plans SET Pos=Id WHERE Pos=0"); } catch { }
        Exec("CREATE TABLE IF NOT EXISTS PlanGroups(Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Color TEXT NOT NULL)");
        try { Exec("ALTER TABLE Plans ADD COLUMN GroupId INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { Exec("ALTER TABLE PlanSets ADD COLUMN GroupId INTEGER NOT NULL DEFAULT 0"); } catch { }
        // plans of earlier days must never ring on later days: an old "repeat every day" plan is switched off once
        if (GetSetting("migr_plan_daily_off", "") != "1") { try { Exec("UPDATE PlanSets SET Daily=0"); } catch { } SetSetting("migr_plan_daily_off", "1"); }
        MigrateBoxToStep();
        MigrateDateOnlyToDateTime();
        MigrateSixMethodsToThree();
        MigrateCourseScopes();
        MigrateCardMethodToCourse();
        MigrateToSmartOnly();
        if (Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Boards")) == 0)
            Exec("INSERT INTO Boards(Name) VALUES('Main')");
    }

    /// <summary>One-time: old cards only had a Leitner Box (1..5). Give them an equivalent Step so the new
    /// Fixed-method engine schedules them exactly as before, with no change in behavior.</summary>
    static void MigrateBoxToStep()
    {
        try
        {
            if (GetSetting("migr_box_step", "0") == "1") return;
            Exec("UPDATE Cards SET Step = MAX(0, MIN(Box,5) - 1) WHERE Step = 0 AND Box > 1");
            SetSetting("migr_box_step", "1");
        }
        catch (Exception ex) { Log.Write("Box->Step migration: " + ex.Message); }
    }

    /// <summary>One-time: NextReview/LastReview used to be stored as a plain date ("yyyy-MM-dd"); sub-day intervals
    /// method needs minute precision, so these columns now carry a full date+time. Old rows get midnight appended
    /// (same calendar day, so nothing about when they are due changes), via a plain TEXT comparison that is safe either way.</summary>
    static void MigrateDateOnlyToDateTime()
    {
        try
        {
            if (GetSetting("migr_datetime", "0") == "1") return;
            Exec(@"UPDATE Topics SET NextReview = NextReview || ' 00:00:00' WHERE length(NextReview) = 10;
                   UPDATE Topics SET LastReview = LastReview || ' 00:00:00' WHERE LastReview IS NOT NULL AND length(LastReview) = 10;
                   UPDATE Cards SET NextReview = NextReview || ' 00:00:00' WHERE length(NextReview) = 10;
                   UPDATE Cards SET LastReview = LastReview || ' 00:00:00' WHERE LastReview IS NOT NULL AND length(LastReview) = 10;");
            SetSetting("migr_datetime", "1");
        }
        catch (Exception ex) { Log.Write("Date->DateTime migration: " + ex.Message); }
    }

    /// <summary>One-time: the app used to offer 6 review methods (StandardFixed=0, WideFixed=1, QuickFixed=2,
    /// Smart=3, SmartIntensive=4, Manual=5); it now offers 3 (Fixed=0, Manual=1, Smart=2), and those numbers
    /// mean different things. Every stored Method value - on Topics, Cards and Courses - is remapped so
    /// existing items land on the closest equivalent new method rather than silently changing meaning:
    /// the three fixed-table methods (Standard/Wide/Quick) all become Fixed, SmartIntensive becomes Smart,
    /// and old Smart/Manual keep their behavior under their new numbers. An item that mapped from
    /// StandardFixed/WideFixed/QuickFixed also has its own interval table copied into CustomIv as a Manual-style
    /// list before being switched to Fixed, in case the user had a schedule going that doesn't match whatever
    /// the new single Fixed table in Settings happens to be - re-pointing it at Manual with that exact table
    /// preserves it exactly; the user can switch it to Fixed themselves later if they want the shared table instead.</summary>
    static void MigrateSixMethodsToThree()
    {
        try
        {
            if (GetSetting("migr_six_to_three", "0") == "1") return;

            // old StandardFixed/WideFixed tables, hard-coded here (the live code that produced them is gone) so
            // this migration keeps working correctly even after future edits to ReviewEngine.
            string standardCsv = "1d,3d,7d,14d,30d,90d,180d";
            string wideCsv = "1d,7d,21d,60d,180d";
            string quickCsv = "10m,1h,1d,3d,7d,14d";

            void Remap(string table)
            {
                // All five old-method cases are resolved in ONE pass per column, each reading the OLD Method
                // value (0..5) before any row changes - doing this as separate sequential UPDATEs would have
                // the second statement's WHERE match rows the first statement just rewrote into its own target
                // number, corrupting them (e.g. old WideFixed=1 colliding with the new Manual=1 that
                // StandardFixed=0 rows were just moved to).
                Exec($@"UPDATE {table} SET
                            CustomIv = CASE Method WHEN 0 THEN $std WHEN 1 THEN $wide WHEN 2 THEN $quick ELSE CustomIv END,
                            Method   = CASE Method WHEN 0 THEN 1 WHEN 1 THEN 1 WHEN 2 THEN 1
                                                    WHEN 3 THEN 2 WHEN 4 THEN 2 WHEN 5 THEN 1 ELSE Method END
                        WHERE Method IN (0,1,2,3,4,5)",
                    ("$std", standardCsv), ("$wide", wideCsv), ("$quick", quickCsv));
            }
            Remap("Topics");
            Remap("Cards");
            Remap("Courses");

            SetSetting("migr_six_to_three", "1");
        }
        catch (Exception ex) { Log.Write("Six->three methods migration: " + ex.Message); }
    }

    /// <summary>One-time: the review method used to be picked per card; now it is picked once per flashcard course.
    /// Each course adopts the method its own cards were most often using, so nobody's existing schedule jumps
    /// to a different method on upgrade. Cards whose method didn't match the course's are left as-is in the
    /// database (their own row), but every read/save from now on overwrites them with the course's method.</summary>
    static void MigrateCardMethodToCourse()
    {
        try
        {
            if (GetSetting("migr_course_method", "0") == "1") return;
            using (var c = Open())
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT Id FROM Courses WHERE Scope=1";
                var courseIds = new List<long>();
                using (var r = cmd.ExecuteReader()) while (r.Read()) courseIds.Add(r.GetInt64(0));

                foreach (var cid in courseIds)
                {
                    // group by (Method, CustomIv) together, not just Method: two cards can share a Method but
                    // carry different tables (e.g. one card migrated from the old WideFixed, another from
                    // QuickFixed, both now Method=1 Manual but with different CustomIv) - picking the most
                    // common PAIR, and copying both columns to the course, is what actually preserves behavior.
                    using var pick = c.CreateCommand();
                    pick.CommandText = "SELECT Method, CustomIv, COUNT(*) n FROM Cards WHERE CourseId=$c GROUP BY Method, CustomIv ORDER BY n DESC LIMIT 1";
                    pick.Parameters.AddWithValue("$c", cid);
                    using var r2 = pick.ExecuteReader();
                    if (r2.Read())
                    {
                        int method = r2.GetInt32(0);
                        string customIv = r2.IsDBNull(1) ? "" : r2.GetString(1);
                        using var upd = c.CreateCommand();
                        upd.CommandText = "UPDATE Courses SET Method=$m,CustomIv=$ci WHERE Id=$c";
                        upd.Parameters.AddWithValue("$m", method);
                        upd.Parameters.AddWithValue("$ci", customIv);
                        upd.Parameters.AddWithValue("$c", cid);
                        upd.ExecuteNonQuery();
                    }
                }
            }
            SetSetting("migr_course_method", "1");
        }
        catch (Exception ex) { Log.Write("Card->Course method migration: " + ex.Message); }
    }

    /// <summary>One-time: the Fixed and Manual review methods were removed, so every course, topic and card becomes Smart.
    /// Items that were on a table keep their place: the completed-review count becomes the streak and the gap between the
    /// last and the next review becomes the interval.</summary>
    static void MigrateToSmartOnly()
    {
        try
        {
            if (GetSetting("migr_smart_only", "0") == "1") return;
            Exec(@"UPDATE Cards SET Reps = CASE WHEN Reps=0 THEN Step ELSE Reps END,
                          IntervalDays = MAX(1.0, CASE WHEN LastReview IS NOT NULL AND LastReview<>'' AND NextReview IS NOT NULL
                                                       THEN julianday(NextReview)-julianday(LastReview) ELSE 1.0 END)
                       WHERE CourseId IN (SELECT Id FROM Courses WHERE Method<>2);
                   UPDATE Topics SET Reps = CASE WHEN Reps=0 THEN Step ELSE Reps END,
                          IntervalDays = MAX(1.0, CASE WHEN LastReview IS NOT NULL AND LastReview<>'' AND NextReview IS NOT NULL
                                                       THEN julianday(NextReview)-julianday(LastReview) ELSE 1.0 END)
                       WHERE Method<>2;
                   UPDATE Courses SET Method=2, CustomIv='';
                   UPDATE Topics SET Method=2, CustomIv='';
                   UPDATE Cards SET Method=2, CustomIv='';");
            SetSetting("migr_smart_only", "1");
        }
        catch (Exception ex) { Log.Write("Smart-only migration: " + ex.Message); }
    }

    /// <summary>One-time: Study and Flashcards used to share their courses. Cards now get their own copy of the courses they use.</summary>
    static void MigrateCourseScopes()
    {
        try
        {
            if (GetSetting("migr_scope", "0") == "1") return;
            Exec(@"INSERT INTO Courses(Name,Scope)
                   SELECT DISTINCT co.Name,1 FROM Cards k JOIN Courses co ON co.Id=k.CourseId
                   WHERE co.Scope<>1 AND NOT EXISTS (SELECT 1 FROM Courses c3 WHERE c3.Scope=1 AND c3.Name=co.Name);
                   UPDATE Cards SET CourseId=(SELECT c2.Id FROM Courses c2 WHERE c2.Scope=1 AND c2.Name=(SELECT Name FROM Courses WHERE Id=Cards.CourseId) ORDER BY c2.Id LIMIT 1)
                   WHERE CourseId IN (SELECT Id FROM Courses WHERE Scope<>1);
                   DELETE FROM Courses WHERE Scope<>1 AND NOT EXISTS (SELECT 1 FROM Topics t WHERE t.CourseId=Courses.Id)
                   AND EXISTS (SELECT 1 FROM Courses c4 WHERE c4.Scope=1 AND c4.Name=Courses.Name);");
            SetSetting("migr_scope", "1");
        }
        catch (Exception ex) { Log.Write("Course split migration: " + ex.Message); }
    }

    // ---------- settings ----------
    // Settings are read all the time (sleep / school hours are checked thousands of times while a review plan is
    // computed), so they live in memory after the first read. Every write goes through SetSetting and updates the cache.
    static readonly Dictionary<string, string> _settingsCache = new();
    static readonly object _settingsLock = new();
    /// <summary>Goes up whenever any setting changes: caches built from settings compare it to know they are stale.</summary>
    public static int SettingsVersion { get; private set; }

    static void ClearSettingsCache()
    {
        lock (_settingsLock) { _settingsCache.Clear(); SettingsVersion++; }
    }

    public static string GetSetting(string key, string def)
    {
        lock (_settingsLock)
            if (_settingsCache.TryGetValue(key, out var cached)) return cached ?? def;
        var r = Scalar("SELECT Value FROM Settings WHERE Key=$k", ("$k", key));
        var val = r == null || r is DBNull ? null : (string)r;
        lock (_settingsLock) _settingsCache[key] = val;
        return val ?? def;
    }

    public static void SetSetting(string key, string value)
    {
        Exec("INSERT OR REPLACE INTO Settings(Key,Value) VALUES($k,$v)", ("$k", key), ("$v", value));
        lock (_settingsLock) { _settingsCache[key] = value; SettingsVersion++; }
    }

    // ---------- boards (tables) ----------
    public static List<Board> GetBoards()
    {
        var list = new List<Board>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name FROM Boards ORDER BY Pos, Id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new Board { Id = r.GetInt64(0), Name = r.GetString(1) });
        return list;
    }
    public static long AddBoard(string name) =>
        Convert.ToInt64(Scalar("INSERT INTO Boards(Name,Pos) VALUES($n,(SELECT COALESCE(MAX(Pos),0)+1 FROM Boards)) RETURNING Id", ("$n", name)));
    public static void SetBoardOrder(IList<long> ids) => InTransaction((c, tx) =>
    {
        for (int i = 0; i < ids.Count; i++) Run(c, tx, "UPDATE Boards SET Pos=$p WHERE Id=$i", ("$p", i + 1), ("$i", ids[i]));
    });
    /// <summary>Moves an alarm to another table.</summary>
    public static void MoveItem(long itemId, long boardId) => Exec("UPDATE Items SET BoardId=$b WHERE Id=$i", ("$b", boardId), ("$i", itemId));
    public static void RenameBoard(long id, string name) =>
        Exec("UPDATE Boards SET Name=$n WHERE Id=$i", ("$n", name), ("$i", id));
    public static void DeleteBoard(long id)
    {
        Exec(@"DELETE FROM Tasks WHERE ItemId IN (SELECT Id FROM Items WHERE BoardId=$i);
               DELETE FROM Items WHERE BoardId=$i;
               DELETE FROM Boards WHERE Id=$i;", ("$i", id));
    }

    // ---------- study (courses + topics), flashcards, timetable ----------
    const string DFmt = "yyyy-MM-dd";
    const string DTFmt = "yyyy-MM-dd HH:mm:ss";
    static string D(DateTime d) => d.ToString(DFmt, CultureInfo.InvariantCulture);
    static DateTime PD(string s) => DateTime.ParseExact(s, DFmt, CultureInfo.InvariantCulture);
    /// <summary>Writes with time-of-day: needed so any sub-day interval (minute/hour steps, including Smart under exam pressure) keeps precision.</summary>
    static string DT(DateTime d) => d.ToString(DTFmt, CultureInfo.InvariantCulture);
    /// <summary>Reads either a plain date (older rows / StudiedOn-style columns) or a full date+time.</summary>
    static DateTime PDT(string s) => s.Length > 10
        ? DateTime.ParseExact(s, DTFmt, CultureInfo.InvariantCulture)
        : DateTime.ParseExact(s, DFmt, CultureInfo.InvariantCulture);

    /// <summary>scope 0 = courses of the Study tab, 1 = courses of the Flashcards tab (they are separate lists).</summary>
    public static List<Course> GetCourses(int scope)
    {
        var list = new List<Course>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT co.Id,co.Name,co.Method,co.CustomIv,co.ExamId,ex.Title,co.SubjAll " +
            "FROM Courses co LEFT JOIN Exams ex ON ex.Id=co.ExamId " +
            "WHERE co.Scope=$s ORDER BY co.Pos, co.Id";
        cmd.Parameters.AddWithValue("$s", scope);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Course
            {
                Id = r.GetInt64(0),
                Name = r.GetString(1),
                Method = ReviewMethod.Smart,
                CustomIntervalsCsv = r.IsDBNull(3) ? "" : r.GetString(3),
                ExamId = r.IsDBNull(4) ? null : r.GetInt64(4),
                ExamTitle = r.IsDBNull(5) ? "" : r.GetString(5),
                SubjectAllName = r.IsDBNull(6) ? "" : r.GetString(6)
            });
        }
        return list;
    }

    public static long AddCourse(string name, int scope) =>
        Convert.ToInt64(Scalar("INSERT INTO Courses(Name,Scope,Pos) VALUES($n,$s,(SELECT COALESCE(MAX(Pos),0)+1 FROM Courses)) RETURNING Id", ("$n", name), ("$s", scope)));

    /// <summary>Saves the order of the course chips (ids in the new order).</summary>
    public static void SetCourseOrder(IList<long> ids) => InTransaction((c, tx) =>
    {
        for (int i = 0; i < ids.Count; i++) Run(c, tx, "UPDATE Courses SET Pos=$p WHERE Id=$i", ("$p", i + 1), ("$i", ids[i]));
    });

    public static void SetSubjectOrder(IList<long> ids) => InTransaction((c, tx) =>
    {
        for (int i = 0; i < ids.Count; i++) Run(c, tx, "UPDATE Subjects SET Pos=$p WHERE Id=$i", ("$p", i + 1), ("$i", ids[i]));
    });

    public static void RenameCourse(long id, string name) =>
        Exec("UPDATE Courses SET Name=$n WHERE Id=$i", ("$n", name), ("$i", id));

    public static void SetCourseExam(long courseId, long? examId) =>
        Exec("UPDATE Courses SET ExamId=$e WHERE Id=$i", ("$e", (object)examId ?? DBNull.Value), ("$i", courseId));

    public static void DeleteCourse(long id) =>
        Exec("DELETE FROM Topics WHERE CourseId=$i; DELETE FROM Cards WHERE CourseId=$i; DELETE FROM Subjects WHERE CourseId=$i; DELETE FROM Courses WHERE Id=$i;", ("$i", id));

    /// <summary>courseId 0 = all courses. Sorted by next review date.</summary>
    public static List<Topic> GetTopics(long courseId = 0)
    {
        var list = new List<Topic>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT t.Id,t.CourseId,c.Name,t.Title,t.Notes,t.Kind,t.StudiedOn,t.Step,t.LastReview,t.NextReview,t.Log," +
            "t.Method,t.Ease,t.IntervalDays,t.Reps,t.Lapses,t.CustomIv,t.Mastery,t.ExamId,ex.Title,ex.WhenAt,ex.Difficulty,t.Difficulty " +
            "FROM Topics t JOIN Courses c ON c.Id=t.CourseId LEFT JOIN Exams ex ON ex.Id=t.ExamId" +
            (courseId != 0 ? " WHERE t.CourseId=$c" : "") + " ORDER BY t.NextReview, t.Id";
        if (courseId != 0) cmd.Parameters.AddWithValue("$c", courseId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var examId = r.IsDBNull(18) ? (long?)null : r.GetInt64(18);
            list.Add(new Topic
            {
                Id = r.GetInt64(0),
                CourseId = r.GetInt64(1),
                CourseName = r.GetString(2),
                Title = r.IsDBNull(3) ? "" : r.GetString(3),
                Notes = r.IsDBNull(4) ? "" : r.GetString(4),
                Kind = (TopicKind)(r.IsDBNull(5) ? 0 : r.GetInt32(5)),
                StudiedOn = PD(r.GetString(6)),
                Step = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                LastReview = r.IsDBNull(8) ? null : PDT(r.GetString(8)),
                NextReview = PDT(r.GetString(9)),
                ReviewLog = r.IsDBNull(10) ? "" : r.GetString(10),
                Method = ReviewMethod.Smart,
                EaseFactor = r.IsDBNull(12) ? 2.5 : r.GetDouble(12),
                IntervalDays = r.IsDBNull(13) ? 1 : r.GetDouble(13),
                Reps = r.IsDBNull(14) ? 0 : r.GetInt32(14),
                Lapses = r.IsDBNull(15) ? 0 : r.GetInt32(15),
                CustomIntervalsCsv = r.IsDBNull(16) ? "" : r.GetString(16),
                Mastery = r.IsDBNull(17) ? 3 : r.GetInt32(17),
                Difficulty = (ExamDifficulty)(r.IsDBNull(22) ? 1 : r.GetInt32(22)),
                ExamId = examId,
                ExamTitle = r.IsDBNull(19) ? "" : r.GetString(19),
                Exam = examId.HasValue && !r.IsDBNull(20) ? new Exam
                {
                    Id = examId.Value,
                    Title = r.IsDBNull(19) ? "" : r.GetString(19),
                    When = PDT(r.GetString(20)),
                    Difficulty = (ExamDifficulty)(r.IsDBNull(21) ? 1 : r.GetInt32(21))
                } : null
            });
        }
        return list;
    }

    public static long SaveTopic(Topic t)
    {
        object last = t.LastReview.HasValue ? (object)DT(t.LastReview.Value) : DBNull.Value;
        object examId = (object)t.ExamId ?? DBNull.Value;
        if (t.Id == 0)
        {
            t.Id = Convert.ToInt64(Scalar(
                "INSERT INTO Topics(CourseId,Title,Notes,Kind,StudiedOn,Step,LastReview,NextReview,Log,Method,Ease,IntervalDays,Reps,Lapses,CustomIv,Mastery,ExamId,Difficulty) " +
                "VALUES($c,$t,$n,$k,$s,$st,$l,$x,$g,$m,$e,$iv,$rp,$lp,$ci,$ms,$ex,$df) RETURNING Id",
                ("$c", t.CourseId), ("$t", t.Title ?? ""), ("$n", t.Notes ?? ""), ("$k", (int)t.Kind),
                ("$s", D(t.StudiedOn)), ("$st", t.Step), ("$l", last), ("$x", DT(t.NextReview)), ("$g", t.ReviewLog ?? ""),
                ("$m", (int)t.Method), ("$e", t.EaseFactor), ("$iv", t.IntervalDays), ("$rp", t.Reps), ("$lp", t.Lapses),
                ("$ci", t.CustomIntervalsCsv ?? ""), ("$ms", t.Mastery), ("$ex", examId), ("$df", (int)t.Difficulty)));
        }
        else
        {
            Exec("UPDATE Topics SET CourseId=$c,Title=$t,Notes=$n,Kind=$k,StudiedOn=$s,Step=$st,LastReview=$l,NextReview=$x,Log=$g," +
                 "Method=$m,Ease=$e,IntervalDays=$iv,Reps=$rp,Lapses=$lp,CustomIv=$ci,Mastery=$ms,ExamId=$ex,Difficulty=$df WHERE Id=$i",
                ("$c", t.CourseId), ("$t", t.Title ?? ""), ("$n", t.Notes ?? ""), ("$k", (int)t.Kind),
                ("$s", D(t.StudiedOn)), ("$st", t.Step), ("$l", last), ("$x", DT(t.NextReview)), ("$g", t.ReviewLog ?? ""),
                ("$m", (int)t.Method), ("$e", t.EaseFactor), ("$iv", t.IntervalDays), ("$rp", t.Reps), ("$lp", t.Lapses),
                ("$ci", t.CustomIntervalsCsv ?? ""), ("$ms", t.Mastery), ("$ex", examId), ("$df", (int)t.Difficulty), ("$i", t.Id));
        }
        return t.Id;
    }

    public static void DeleteTopic(long id) => Exec("DELETE FROM Topics WHERE Id=$i", ("$i", id));

    public static int CountDueTopics() =>
        (int)Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Topics WHERE NextReview<=$d", ("$d", DT(DateTime.Now))));

    // ----- flashcards
    public static List<Card> GetCards(long courseId = 0, long subjectId = 0, bool manualOrder = false)
    {
        var list = new List<Card>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT k.Id,k.CourseId,co.Name,k.Front,k.Back,k.Box,k.StartDate,k.NextReview,k.Reviews,k.Lapses," +
            "co.Method,k.Ease,k.IntervalDays,k.Reps,k.Step,k.LastReview,k.Log,co.CustomIv,co.ExamId,ex.Title,ex.WhenAt,ex.Difficulty,k.SubjectId,sj.Name,sj.Difficulty,k.Pos " +
            "FROM Cards k JOIN Courses co ON co.Id=k.CourseId LEFT JOIN Exams ex ON ex.Id=co.ExamId LEFT JOIN Subjects sj ON sj.Id=k.SubjectId" +
            " WHERE 1=1" + (courseId != 0 ? " AND k.CourseId=$c" : "") + (courseId != 0 && subjectId != 0 ? " AND k.SubjectId=$sj" : "") +
            (manualOrder ? " ORDER BY k.Pos, k.Id" : " ORDER BY k.NextReview, k.Id");
        if (courseId != 0) cmd.Parameters.AddWithValue("$c", courseId);
        if (courseId != 0 && subjectId != 0) cmd.Parameters.AddWithValue("$sj", subjectId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var examId = r.IsDBNull(18) ? (long?)null : r.GetInt64(18);
            list.Add(new Card
            {
                Id = r.GetInt64(0),
                CourseId = r.GetInt64(1),
                CourseName = r.GetString(2),
                SubjectId = r.IsDBNull(22) ? 0 : r.GetInt64(22),
                Pos = r.IsDBNull(25) ? 0 : r.GetInt64(25),
                SubjectName = r.IsDBNull(23) ? "" : r.GetString(23),
                TopicDifficulty = (ExamDifficulty)(r.IsDBNull(24) ? 1 : r.GetInt32(24)),
                Front = r.IsDBNull(3) ? "" : r.GetString(3),
                Back = r.IsDBNull(4) ? "" : r.GetString(4),
                Box = r.IsDBNull(5) ? 1 : r.GetInt32(5),
                StartDate = PD(r.GetString(6)),
                NextReview = PDT(r.GetString(7)),
                Reviews = r.IsDBNull(8) ? 0 : r.GetInt32(8),
                Lapses = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                // Method and CustomIv come from the COURSE, not the card's own row: the review method is
                // chosen once per flashcard course, never per individual card.
                Method = ReviewMethod.Smart,
                EaseFactor = r.IsDBNull(11) ? 2.5 : r.GetDouble(11),
                IntervalDays = r.IsDBNull(12) ? 1 : r.GetDouble(12),
                Reps = r.IsDBNull(13) ? 0 : r.GetInt32(13),
                Step = r.IsDBNull(14) ? 0 : r.GetInt32(14),
                LastReview = r.IsDBNull(15) ? null : PDT(r.GetString(15)),
                ReviewLog = r.IsDBNull(16) ? "" : r.GetString(16),
                CustomIntervalsCsv = r.IsDBNull(17) ? "" : r.GetString(17),
                ExamId = examId,
                ExamTitle = r.IsDBNull(19) ? "" : r.GetString(19),
                Exam = examId.HasValue && !r.IsDBNull(20) ? new Exam
                {
                    Id = examId.Value,
                    Title = r.IsDBNull(19) ? "" : r.GetString(19),
                    When = PDT(r.GetString(20)),
                    Difficulty = (ExamDifficulty)(r.IsDBNull(21) ? 1 : r.GetInt32(21))
                } : null
            });
        }
        return list;
    }

    public static long SaveCard(Card k)
    {
        // Method/CustomIv are NOT written here: they live on the course and are applied to every card in it
        // by the course. Kept as real columns (old data, and a safety fallback) but never written from a
        // single card's save, so one card can never drift onto its own method.
        object last = k.LastReview.HasValue ? (object)DT(k.LastReview.Value) : DBNull.Value;
        if (k.Id == 0)
        {
            k.Pos = Convert.ToInt64(Scalar("SELECT COALESCE(MAX(Pos),0)+1 FROM Cards"));
            k.Id = Convert.ToInt64(Scalar(
                "INSERT INTO Cards(CourseId,Front,Back,Box,StartDate,NextReview,Reviews,Lapses,Method,Ease,IntervalDays,Reps,Step,LastReview,Log,CustomIv,SubjectId,Pos) " +
                "VALUES($c,$f,$b,$x,$s,$n,$r,$l,$m,$e,$iv,$rp,$st,$lr,$g,$ci,$sj,$pos) RETURNING Id",
                ("$c", k.CourseId), ("$f", k.Front ?? ""), ("$b", k.Back ?? ""), ("$x", k.Box),
                ("$s", D(k.StartDate)), ("$n", DT(k.NextReview)), ("$r", k.Reviews), ("$l", k.Lapses),
                ("$m", (int)k.Method), ("$e", k.EaseFactor), ("$iv", k.IntervalDays), ("$rp", k.Reps),
                ("$st", k.Step), ("$lr", last), ("$g", k.ReviewLog ?? ""), ("$ci", k.CustomIntervalsCsv ?? ""), ("$sj", k.SubjectId), ("$pos", k.Pos)));
        }
        else
        {
            Exec("UPDATE Cards SET CourseId=$c,Front=$f,Back=$b,Box=$x,StartDate=$s,NextReview=$n,Reviews=$r,Lapses=$l," +
                 "Ease=$e,IntervalDays=$iv,Reps=$rp,Step=$st,LastReview=$lr,Log=$g,SubjectId=$sj WHERE Id=$i",
                ("$c", k.CourseId), ("$f", k.Front ?? ""), ("$b", k.Back ?? ""), ("$x", k.Box),
                ("$s", D(k.StartDate)), ("$n", DT(k.NextReview)), ("$r", k.Reviews), ("$l", k.Lapses),
                ("$e", k.EaseFactor), ("$iv", k.IntervalDays), ("$rp", k.Reps),
                ("$st", k.Step), ("$lr", last), ("$g", k.ReviewLog ?? ""), ("$sj", k.SubjectId), ("$i", k.Id));
        }
        return k.Id;
    }

    // ----- flashcard subjects (inside a flashcard course)
    public static List<Subject> GetSubjects(long courseId)
    {
        var list = new List<Subject>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT s.Id,s.CourseId,s.Name,(SELECT COUNT(*) FROM Cards k WHERE k.SubjectId=s.Id),s.Difficulty,s.StartDate FROM Subjects s WHERE s.CourseId=$c ORDER BY s.Pos, s.Id";
        cmd.Parameters.AddWithValue("$c", courseId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Subject { Id = r.GetInt64(0), CourseId = r.GetInt64(1), Name = r.GetString(2), CardCount = r.GetInt32(3), Difficulty = (ExamDifficulty)r.GetInt32(4),
                StartDate = r.IsDBNull(5) || string.IsNullOrEmpty(r.GetString(5)) ? DateTime.Today : PD(r.GetString(5)) });
        return list;
    }

    public static long AddSubject(long courseId, string name, int difficulty = 1, DateTime? startDate = null) =>
        Convert.ToInt64(Scalar("INSERT INTO Subjects(CourseId,Name,Difficulty,StartDate,Pos) VALUES($c,$n,$d,$s,(SELECT COALESCE(MAX(Pos),0)+1 FROM Subjects)) RETURNING Id",
            ("$c", courseId), ("$n", name), ("$d", difficulty), ("$s", D((startDate ?? DateTime.Today).Date))));

    public static void SetSubjectStart(long id, DateTime date) =>
        Exec("UPDATE Subjects SET StartDate=$s WHERE Id=$i", ("$s", D(date.Date)), ("$i", id));

    /// <summary>Writes the manual order: the same set of positions is handed out again in the new row order.</summary>
    public static void SaveCardOrder(IList<Card> orderedCards)
    {
        var slots = orderedCards.Select(k => k.Pos).OrderBy(x => x).ToList();
        InTransaction((c, tx) =>
        {
            for (int i = 0; i < orderedCards.Count; i++)
            {
                orderedCards[i].Pos = slots[i];
                Run(c, tx, "UPDATE Cards SET Pos=$p WHERE Id=$i", ("$p", slots[i]), ("$i", orderedCards[i].Id));
            }
        });
    }

    public static void SetSubjectDifficulty(long id, int difficulty) =>
        Exec("UPDATE Subjects SET Difficulty=$d WHERE Id=$i", ("$d", difficulty), ("$i", id));

    public static void RenameSubject(long id, string name) =>
        Exec("UPDATE Subjects SET Name=$n WHERE Id=$i", ("$n", name), ("$i", id));

    /// <summary>Deleting a subject never deletes cards: they stay in the course (shown under \"All ...\").</summary>
    public static void DeleteSubject(long id) =>
        Exec("UPDATE Cards SET SubjectId=0 WHERE SubjectId=$i; DELETE FROM Subjects WHERE Id=$i;", ("$i", id));

    public static void SetCourseSubjectAll(long courseId, string name) =>
        Exec("UPDATE Courses SET SubjAll=$n WHERE Id=$i", ("$n", name ?? ""), ("$i", courseId));

    // ----- batch operations (one transaction each)
    /// <summary>Saves only the text (and subject) of many cards - never touches their review schedule.</summary>
    public static void UpdateCardTexts(IEnumerable<Card> cards) => InTransaction((c, tx) =>
    {
        foreach (var k in cards)
            Run(c, tx, "UPDATE Cards SET Front=$f,Back=$b,SubjectId=$sj WHERE Id=$i",
                ("$f", k.Front ?? ""), ("$b", k.Back ?? ""), ("$sj", k.SubjectId), ("$i", k.Id));
    });

    /// <summary>Saves the whole review state (dates, ease, interval ...) of many cards at once.</summary>
    public static void SaveCardsState(IEnumerable<Card> cards) => InTransaction((c, tx) =>
    {
        foreach (var k in cards)
        {
            object last = k.LastReview.HasValue ? (object)DT(k.LastReview.Value) : DBNull.Value;
            Run(c, tx, "UPDATE Cards SET StartDate=$s,NextReview=$n,Reviews=$r,Lapses=$l,Ease=$e,IntervalDays=$iv,Reps=$rp,Step=$st,LastReview=$lr,Log=$g,Box=$x WHERE Id=$i",
                ("$s", D(k.StartDate)), ("$n", DT(k.NextReview)), ("$r", k.Reviews), ("$l", k.Lapses), ("$e", k.EaseFactor),
                ("$iv", k.IntervalDays), ("$rp", k.Reps), ("$st", k.Step), ("$lr", last), ("$g", k.ReviewLog ?? ""), ("$x", k.Box), ("$i", k.Id));
        }
    });

    public static void DeleteCards(IEnumerable<long> ids) => InTransaction((c, tx) =>
    {
        foreach (var id in ids) Run(c, tx, "DELETE FROM Cards WHERE Id=$i", ("$i", id));
    });

    /// <summary>Only the NextReview column: what a reschedule changes (ease / interval / history stay as they are).</summary>
    public static void UpdateNextReviews(IEnumerable<Topic> topics, IEnumerable<Card> cards) => InTransaction((c, tx) =>
    {
        foreach (var t in topics) Run(c, tx, "UPDATE Topics SET NextReview=$n WHERE Id=$i", ("$n", DT(t.NextReview)), ("$i", t.Id));
        foreach (var k in cards) Run(c, tx, "UPDATE Cards SET NextReview=$n WHERE Id=$i", ("$n", DT(k.NextReview)), ("$i", k.Id));
    });

    public static void DeleteCard(long id) => Exec("DELETE FROM Cards WHERE Id=$i", ("$i", id));

    public static int CountDueCards() =>
        (int)Convert.ToInt64(Scalar("SELECT COUNT(*) FROM Cards WHERE NextReview<=$d", ("$d", DT(DateTime.Now))));

    // ----- study sessions (focus mode)
    static (string, object)[] SessionParams(StudySession s, bool withInk)
    {
        var l = new List<(string, object)>
        {
            ("$t", (object)s.TopicId ?? DBNull.Value), ("$c", (object)s.CourseId ?? DBNull.Value), ("$ti", s.Title ?? ""),
            ("$st", DT(s.StartedAt)), ("$sec", s.Seconds), ("$pl", s.PlannedSeconds), ("$m", s.Mode), ("$l", s.Locked ? 1 : 0),
            ("$r", s.Rating), ("$n", s.Notes ?? ""), ("$ck", s.Checklist ?? ""), ("$cd", s.Cards)
        };
        if (withInk) l.Add(("$ink", (object)s.Ink ?? DBNull.Value));
        return l.ToArray();
    }

    public static long AddSession(StudySession s)
    {
        s.Id = Convert.ToInt64(Scalar(
            "INSERT INTO StudySessions(TopicId,CourseId,Title,StartedAt,Seconds,PlannedSeconds,Mode,Locked,Rating,Notes,Checklist,Cards,Ink) " +
            "VALUES($t,$c,$ti,$st,$sec,$pl,$m,$l,$r,$n,$ck,$cd,$ink) RETURNING Id", SessionParams(s, true)));
        return s.Id;
    }

    /// <summary>`withInk` false leaves the stored drawing untouched (so the frequent autosave stays small).</summary>
    public static void SaveSession(StudySession s, bool withInk)
    {
        var p = SessionParams(s, withInk).Concat(new[] { ("$i", (object)s.Id) }).ToArray();
        Exec("UPDATE StudySessions SET TopicId=$t,CourseId=$c,Title=$ti,StartedAt=$st,Seconds=$sec,PlannedSeconds=$pl,Mode=$m,Locked=$l,Rating=$r,Notes=$n,Checklist=$ck,Cards=$cd" +
             (withInk ? ",Ink=$ink" : "") + " WHERE Id=$i", p);
    }

    public static List<StudySession> GetSessions(int limit = 500)
    {
        var list = new List<StudySession>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,TopicId,CourseId,Title,StartedAt,Seconds,PlannedSeconds,Mode,Locked,Rating,Notes,Checklist,Cards,(Ink IS NOT NULL AND length(Ink)>0) " +
                          "FROM StudySessions ORDER BY StartedAt DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new StudySession
            {
                Id = r.GetInt64(0),
                TopicId = r.IsDBNull(1) ? null : r.GetInt64(1),
                CourseId = r.IsDBNull(2) ? null : r.GetInt64(2),
                Title = r.IsDBNull(3) ? "" : r.GetString(3),
                StartedAt = PDT(r.GetString(4)),
                Seconds = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                PlannedSeconds = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                Mode = r.IsDBNull(7) ? 0 : r.GetInt32(7),
                Locked = !r.IsDBNull(8) && r.GetInt32(8) == 1,
                Rating = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                Notes = r.IsDBNull(10) ? "" : r.GetString(10),
                Checklist = r.IsDBNull(11) ? "" : r.GetString(11),
                Cards = r.IsDBNull(12) ? 0 : r.GetInt32(12),
                HasInk = !r.IsDBNull(13) && r.GetInt32(13) == 1
            });
        }
        return list;
    }

    public static byte[] GetSessionInk(long id) => Scalar("SELECT Ink FROM StudySessions WHERE Id=$i", ("$i", id)) as byte[];

    public static void DeleteSession(long id) => Exec("DELETE FROM StudySessions WHERE Id=$i", ("$i", id));

    /// <summary>Focused seconds of all sessions that started at or after `from`.</summary>
    public static int StudySecondsSince(DateTime from) =>
        (int)Convert.ToInt64(Scalar("SELECT COALESCE(SUM(Seconds),0) FROM StudySessions WHERE StartedAt>=$d", ("$d", DT(from))));

    // ----- exams
    public static List<Exam> GetExams()
    {
        var list = new List<Exam>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT e.Id,e.Title,e.WhenAt,e.Difficulty,e.Notes," +
            "(SELECT COUNT(*) FROM Topics WHERE ExamId=e.Id)," +
            "(SELECT COUNT(*) FROM Courses WHERE ExamId=e.Id) " +
            "FROM Exams e ORDER BY e.WhenAt";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Exam
            {
                Id = r.GetInt64(0),
                Title = r.IsDBNull(1) ? "" : r.GetString(1),
                When = PDT(r.GetString(2)),
                Difficulty = (ExamDifficulty)(r.IsDBNull(3) ? 1 : r.GetInt32(3)),
                Notes = r.IsDBNull(4) ? "" : r.GetString(4),
                TopicCount = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                CardCourseCount = r.IsDBNull(6) ? 0 : r.GetInt32(6)
            });
        }
        return list;
    }

    public static long SaveExam(Exam e)
    {
        if (e.Id == 0)
        {
            e.Id = Convert.ToInt64(Scalar(
                "INSERT INTO Exams(Title,WhenAt,Difficulty,Notes) VALUES($t,$w,$d,$n) RETURNING Id",
                ("$t", e.Title ?? ""), ("$w", DT(e.When)), ("$d", (int)e.Difficulty), ("$n", e.Notes ?? "")));
        }
        else
        {
            Exec("UPDATE Exams SET Title=$t,WhenAt=$w,Difficulty=$d,Notes=$n WHERE Id=$i",
                ("$t", e.Title ?? ""), ("$w", DT(e.When)), ("$d", (int)e.Difficulty), ("$n", e.Notes ?? ""), ("$i", e.Id));
        }
        return e.Id;
    }

    /// <summary>Deletes the exam and clears the link from any topic or flashcard course that pointed to it -
    /// their material is kept, just no longer tied to an exam date.</summary>
    public static void DeleteExam(long id)
    {
        Exec(@"UPDATE Topics SET ExamId=NULL WHERE ExamId=$i;
               UPDATE Courses SET ExamId=NULL WHERE ExamId=$i;
               DELETE FROM Exams WHERE Id=$i;", ("$i", id));
    }

    public static void SetTopicExam(long topicId, long? examId) =>
        Exec("UPDATE Topics SET ExamId=$e WHERE Id=$i", ("$e", (object)examId ?? DBNull.Value), ("$i", topicId));

    // ----- weekly timetable: Day = (int)DayOfWeek, Slot = 1..7
    public static Dictionary<(int, int), string> GetTimetable()
    {
        var map = new Dictionary<(int, int), string>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Day,Slot,Subject FROM Timetable";
        using var r = cmd.ExecuteReader();
        while (r.Read()) map[(r.GetInt32(0), r.GetInt32(1))] = r.IsDBNull(2) ? "" : r.GetString(2);
        return map;
    }

    public static void SetCell(int day, int slot, string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            Exec("DELETE FROM Timetable WHERE Day=$d AND Slot=$s", ("$d", day), ("$s", slot));
        else
            Exec("INSERT OR REPLACE INTO Timetable(Day,Slot,Subject) VALUES($d,$s,$t)", ("$d", day), ("$s", slot), ("$t", subject.Trim()));
    }

    // ----- prayer times: one row per date (yyyy-MM-dd), six times as HH:mm
    public static void SavePrayTimes(List<(string date, string[] t)> rows)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        foreach (var (d, t) in rows)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR REPLACE INTO PrayTimes(Date,Fajr,Sunrise,Dhuhr,Sunset,Maghrib,Midnight) VALUES($d,$a,$b,$c,$e,$f,$g)";
            cmd.Parameters.AddWithValue("$d", d);
            cmd.Parameters.AddWithValue("$a", t[0]);
            cmd.Parameters.AddWithValue("$b", t[1]);
            cmd.Parameters.AddWithValue("$c", t[2]);
            cmd.Parameters.AddWithValue("$e", t[3]);
            cmd.Parameters.AddWithValue("$f", t[4]);
            cmd.Parameters.AddWithValue("$g", t[5]);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>The six times of one day, or null when the day is not in the table.</summary>
    public static string[] GetPrayDay(DateTime day)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Fajr,Sunrise,Dhuhr,Sunset,Maghrib,Midnight FROM PrayTimes WHERE Date=$d";
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var t = new string[6];
        for (int i = 0; i < 6; i++) t[i] = r.IsDBNull(i) ? "" : r.GetString(i);
        return t;
    }

    /// <summary>First date, last date and number of days stored (count 0 = empty table).</summary>
    public static (string first, string last, int count) PrayRange()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT MIN(Date),MAX(Date),COUNT(*) FROM PrayTimes";
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.GetInt32(2) == 0) return ("", "", 0);
        return (r.GetString(0), r.GetString(1), r.GetInt32(2));
    }

    public static void ClearPrayTimes() => Exec("DELETE FROM PrayTimes");

    // ---------- backup / restore / reset ----------
    /// <summary>Consistent copy of the whole database (works while the app is running).</summary>
    public static void BackupTo(string path)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        if (File.Exists(path)) File.Delete(path);
        using var src = Open();
        using var dst = new SqliteConnection("Data Source=" + path + ";Pooling=False");
        dst.Open();
        src.BackupDatabase(dst);
    }

    public static bool IsBackupFile(string path)
    {
        try
        {
            using var c = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Pooling=False");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Boards','Items','Settings')";
            return Convert.ToInt64(cmd.ExecuteScalar()) == 3;
        }
        catch { return false; }
    }

    /// <summary>Replaces ALL current data by the content of a backup file.</summary>
    public static void RestoreFrom(string path)
    {
        using (var src = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly;Pooling=False"))
        {
            src.Open();
            using var dst = Open();
            src.BackupDatabase(dst);
        }
        Init();   // older backups: add the tables/columns that did not exist yet (also clears the settings cache)
    }

    /// <summary>Deletes all data and all settings (the caller must have made a backup first).</summary>
    public static void ResetAll()
    {
        Exec(@"DELETE FROM Tasks; DELETE FROM Items; DELETE FROM Boards; DELETE FROM Topics; DELETE FROM Cards;
               DELETE FROM Courses; DELETE FROM StudySessions; DELETE FROM Timetable; DELETE FROM PrayTimes; DELETE FROM Settings;");
        try { Exec("DELETE FROM sqlite_sequence"); } catch { }
        ClearSettingsCache();
        Init();
    }

    // ---------- items ----------
    public static List<Item> GetItems(long? boardId = null, bool dueOnly = false, DateTime? now = null)
    {
        var list = new List<Item>();
        using var c = Open();
        using (var cmd = c.CreateCommand())
        {
            var where = new List<string>();
            if (boardId.HasValue) { where.Add("BoardId=$b"); cmd.Parameters.AddWithValue("$b", boardId.Value); }
            if (dueOnly)
            {
                where.Add("Enabled=1 AND AlarmAt IS NOT NULL AND AlarmAt<=$now");
                cmd.Parameters.AddWithValue("$now", (now ?? DateTime.Now).ToString(Fmt, CultureInfo.InvariantCulture));
            }
            cmd.CommandText = "SELECT Id,BoardId,Title,Notes,AlarmAt,Repeat,Sound,Enabled,CustomDays FROM Items" +
                              (where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "") + " ORDER BY (AlarmAt IS NULL), AlarmAt, Id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Item
                {
                    Id = r.GetInt64(0),
                    BoardId = r.GetInt64(1),
                    Title = r.IsDBNull(2) ? "" : r.GetString(2),
                    Notes = r.IsDBNull(3) ? "" : r.GetString(3),
                    AlarmAt = r.IsDBNull(4) ? null : DateTime.ParseExact(r.GetString(4), Fmt, CultureInfo.InvariantCulture),
                    Repeat = (RepeatMode)(r.IsDBNull(5) ? 0 : r.GetInt32(5)),
                    Sound = r.IsDBNull(6) ? "" : r.GetString(6),
                    Enabled = !r.IsDBNull(7) && r.GetInt32(7) == 1,
                    CustomDays = r.IsDBNull(8) ? "" : r.GetString(8)
                });
            }
        }
        foreach (var it in list)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT Id,Type,Target,Args FROM Tasks WHERE ItemId=$i ORDER BY Id";
            cmd.Parameters.AddWithValue("$i", it.Id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                it.Tasks.Add(new TaskAction
                {
                    Id = r.GetInt64(0),
                    Type = (TaskType)r.GetInt32(1),
                    Target = r.IsDBNull(2) ? "" : r.GetString(2),
                    Args = r.IsDBNull(3) ? "" : r.GetString(3)
                });
        }
        return list;
    }

    static void AddItemParams(SqliteCommand cmd, Item it)
    {
        cmd.Parameters.AddWithValue("$b", it.BoardId);
        cmd.Parameters.AddWithValue("$t", it.Title ?? "");
        cmd.Parameters.AddWithValue("$n", it.Notes ?? "");
        cmd.Parameters.AddWithValue("$a", it.AlarmAt.HasValue
            ? (object)it.AlarmAt.Value.ToString(Fmt, CultureInfo.InvariantCulture) : DBNull.Value);
        cmd.Parameters.AddWithValue("$r", (int)it.Repeat);
        cmd.Parameters.AddWithValue("$s", it.Sound ?? "");
        cmd.Parameters.AddWithValue("$e", it.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$cd", it.CustomDays ?? "");
    }

    public static long SaveItem(Item it)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (it.Id == 0)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO Items(BoardId,Title,Notes,AlarmAt,Repeat,Sound,Enabled,CustomDays) VALUES($b,$t,$n,$a,$r,$s,$e,$cd) RETURNING Id";
            AddItemParams(cmd, it);
            it.Id = Convert.ToInt64(cmd.ExecuteScalar());
        }
        else
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Items SET BoardId=$b,Title=$t,Notes=$n,AlarmAt=$a,Repeat=$r,Sound=$s,Enabled=$e,CustomDays=$cd WHERE Id=$id";
            AddItemParams(cmd, it);
            cmd.Parameters.AddWithValue("$id", it.Id);
            cmd.ExecuteNonQuery();
        }
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM Tasks WHERE ItemId=$i";
            del.Parameters.AddWithValue("$i", it.Id);
            del.ExecuteNonQuery();
        }
        foreach (var t in it.Tasks)
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT INTO Tasks(ItemId,Type,Target,Args) VALUES($i,$ty,$tg,$a)";
            ins.Parameters.AddWithValue("$i", it.Id);
            ins.Parameters.AddWithValue("$ty", (int)t.Type);
            ins.Parameters.AddWithValue("$tg", t.Target ?? "");
            ins.Parameters.AddWithValue("$a", t.Args ?? "");
            ins.ExecuteNonQuery();
        }
        tx.Commit();
        return it.Id;
    }

    public static void DeleteItem(long id) =>
        Exec("DELETE FROM Tasks WHERE ItemId=$i; DELETE FROM Items WHERE Id=$i;", ("$i", id));

    public static void SetEnabled(long id, bool on) =>
        Exec("UPDATE Items SET Enabled=$e WHERE Id=$i", ("$e", on ? 1 : 0), ("$i", id));

    public static void SetAllEnabled(bool on) =>
        Exec("UPDATE Items SET Enabled=$e WHERE AlarmAt IS NOT NULL", ("$e", on ? 1 : 0));

    public static void UpdateAlarm(long id, DateTime? at, bool enabled) =>
        Exec("UPDATE Items SET AlarmAt=$a, Enabled=$e WHERE Id=$i",
            ("$a", at.HasValue ? (object)at.Value.ToString(Fmt, CultureInfo.InvariantCulture) : DBNull.Value),
            ("$e", enabled ? 1 : 0), ("$i", id));

    // ---------- planner ----------
    public static List<PlanSet> GetPlanSets()
    {
        var list = new List<PlanSet>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,Date,Daily,GroupId FROM PlanSets ORDER BY Date, Id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PlanSet
            {
                Id = r.GetInt64(0),
                Name = r.IsDBNull(1) ? "" : r.GetString(1),
                Date = r.IsDBNull(2) || string.IsNullOrEmpty(r.GetString(2)) ? DateTime.Today : PD(r.GetString(2)),
                Daily = !r.IsDBNull(3) && r.GetInt32(3) == 1,
                GroupId = r.IsDBNull(4) ? 0 : r.GetInt64(4)
            });
        return list;
    }

    public static void SavePlanSet(PlanSet p)
    {
        if (p.Id == 0)
            p.Id = Convert.ToInt64(Scalar("INSERT INTO PlanSets(Name,Date,Daily,GroupId) VALUES($n,$d,$y,$g) RETURNING Id",
                ("$n", p.Name ?? ""), ("$d", D(p.Date.Date)), ("$y", p.Daily ? 1 : 0), ("$g", p.GroupId)));
        else
            Exec("UPDATE PlanSets SET Name=$n,Date=$d,Daily=$y,GroupId=$g WHERE Id=$i", ("$n", p.Name ?? ""), ("$d", D(p.Date.Date)), ("$y", p.Daily ? 1 : 0), ("$g", p.GroupId), ("$i", p.Id));
    }

    public static void DeletePlanSet(long id)
    {
        Exec("DELETE FROM Plans WHERE SetId=$i", ("$i", id));
        Exec("DELETE FROM PlanSets WHERE Id=$i", ("$i", id));
    }

    /// <summary>Rows saved before plans had dates (SetId 0) are moved into the given plan; returns how many.</summary>
    public static int AdoptOrphanPlans(long setId)
    {
        int n = Convert.ToInt32(Scalar("SELECT COUNT(*) FROM Plans WHERE SetId=0"));
        if (n > 0) Exec("UPDATE Plans SET SetId=$s WHERE SetId=0", ("$s", setId));
        return n;
    }

    public static List<PlanRow> GetPlans(long? setId = null)
    {
        var list = new List<PlanRow>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Title,Start,Minutes,Notes,SetId,DayOffset,Muted,GroupId,Pos,Free FROM Plans" + (setId.HasValue ? " WHERE SetId=" + setId.Value : "") + " ORDER BY Pos, Id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new PlanRow
            {
                Id = r.GetInt64(0),
                Title = r.IsDBNull(1) ? "" : r.GetString(1),
                Start = r.IsDBNull(2) ? "" : r.GetString(2),
                Minutes = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                Notes = r.IsDBNull(4) ? "" : r.GetString(4),
                SetId = r.IsDBNull(5) ? 0 : r.GetInt64(5),
                DayOffset = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                Muted = !r.IsDBNull(7) && r.GetInt32(7) != 0,
                GroupId = r.IsDBNull(8) ? 0 : r.GetInt64(8),
                Pos = r.IsDBNull(9) ? 0 : r.GetInt64(9),
                Free = !r.IsDBNull(10) && r.GetInt32(10) != 0
            });
        return list;
    }

    public static List<PlanGroup> GetPlanGroups()
    {
        var list = new List<PlanGroup>();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,Color FROM PlanGroups ORDER BY Id";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new PlanGroup { Id = r.GetInt64(0), Name = r.GetString(1), Color = r.GetString(2) });
        return list;
    }

    public static void SavePlanGroup(PlanGroup g)
    {
        if (g.Id == 0)
            g.Id = Convert.ToInt64(Scalar("INSERT INTO PlanGroups(Name,Color) VALUES($n,$c) RETURNING Id", ("$n", g.Name), ("$c", g.Color)));
        else
            Exec("UPDATE PlanGroups SET Name=$n,Color=$c WHERE Id=$i", ("$n", g.Name), ("$c", g.Color), ("$i", g.Id));
    }

    public static void DeletePlanGroup(long id) =>
        Exec("UPDATE PlanSets SET GroupId=0 WHERE GroupId=$i; UPDATE Plans SET GroupId=0 WHERE GroupId=$i; DELETE FROM PlanGroups WHERE Id=$i", ("$i", id));

    public static void SavePlan(PlanRow p)
    {
        if (p.Id == 0)
            p.Id = Convert.ToInt64(Scalar("INSERT INTO Plans(Title,Start,Minutes,Notes,SetId,DayOffset,Muted,GroupId,Free,Pos) VALUES($t,$s,$m,$n,$g,$o,$mu,$gr,$fr,CASE WHEN $pos>0 THEN $pos ELSE (SELECT COALESCE(MAX(Pos),0)+1 FROM Plans) END) RETURNING Id",
                ("$t", p.Title ?? ""), ("$s", p.Start ?? ""), ("$m", p.Minutes), ("$n", p.Notes ?? ""), ("$g", p.SetId), ("$o", p.DayOffset), ("$mu", p.Muted ? 1 : 0), ("$gr", p.GroupId), ("$fr", p.Free ? 1 : 0), ("$pos", p.Pos)));
        else
            Exec("UPDATE Plans SET Title=$t,Start=$s,Minutes=$m,Notes=$n,SetId=$g,DayOffset=$o,Muted=$mu,GroupId=$gr,Free=$fr WHERE Id=$i",
                ("$t", p.Title ?? ""), ("$s", p.Start ?? ""), ("$m", p.Minutes), ("$n", p.Notes ?? ""), ("$g", p.SetId), ("$o", p.DayOffset), ("$mu", p.Muted ? 1 : 0), ("$gr", p.GroupId), ("$fr", p.Free ? 1 : 0), ("$i", p.Id));
    }

    /// <summary>Adds a new row right after the row at position <paramref name="afterPos"/> (the later rows keep their times; only their place number grows).</summary>
    public static void InsertPlanAfter(PlanRow np, long afterPos)
    {
        Exec("UPDATE Plans SET Pos=Pos+1 WHERE SetId=$s AND Pos>$p", ("$s", np.SetId), ("$p", afterPos));
        np.Pos = afterPos + 1;
        SavePlan(np);
    }

    public static void DeletePlan(long id) => Exec("DELETE FROM Plans WHERE Id=$i", ("$i", id));
}
