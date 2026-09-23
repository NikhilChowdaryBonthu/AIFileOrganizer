using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using AIFileOrganizer.Desktop;
using Microsoft.Data.Sqlite;

internal static class TestRunner
{
private static int failed;

private static int Main(string[] args)
{
    if (args.Length == 3 && args[0] == "--capture-demo")
    {
        App.DemoCapture = (args[1], args[2]);
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(Array.Empty<string>());
        return Environment.ExitCode;
    }

    if (args.Contains("--ai-smoke", StringComparer.OrdinalIgnoreCase))
    {
        Run("local Ollama text-file scan", TestAiSmoke);
        return failed == 0 ? 0 : 1;
    }

    Run("move and Undo", TestMoveAndUndo);
    Run("versioned filename conflict", TestVersionedConflict);
    Run("duplicate detection and review", TestDuplicates);
    Run("Undo preserves occupied original path", TestUndoConflict);
    Run("deletion requires confirmation", TestDeletionConfirmation);
    Run("text scan survives Ollama outage", TestAiFallback);
    return failed == 0 ? 0 : 1;
}

private static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {error.Message}");
    }
}

private static void TestMoveAndUndo()
{
    using Fixture fixture = new();
    string source = fixture.File("incoming/report.txt", "sample report");
    string destination = fixture.PathFor("organized/report.txt");
    OrganizationResult result = Program.OrganizeFromDesktop(
        new[] { Plan(source, destination) }, fixture.Database, fixture.Duplicates);
    Check(result.Moved == 1 && result.Errors == 0, "The move should succeed.");
    Check(!File.Exists(source) && File.ReadAllText(destination) == "sample report", "The source should move intact.");

    UndoResult undo = Program.UndoLastOrganizationFromDesktop(fixture.Database);
    Check(undo.FoundOrganization && undo.Restored == 1 && undo.Skipped == 0, "Undo should restore the batch.");
    Check(File.ReadAllText(source) == "sample report" && !File.Exists(destination), "Undo should restore the original file.");
    Check(!Program.UndoLastOrganizationFromDesktop(fixture.Database).FoundOrganization, "An undone batch must not be replayed.");
}

private static void TestVersionedConflict()
{
    using Fixture fixture = new();
    string source = fixture.File("incoming/report.txt", "new content");
    string destination = fixture.File("organized/report.txt", "original content");
    OrganizationResult result = Program.OrganizeFromDesktop(
        new[] { Plan(source, destination) }, fixture.Database, fixture.Duplicates);
    Check(result.Renamed == 1 && result.Errors == 0, "A different-content name conflict must be versioned.");
    Check(File.ReadAllText(destination) == "original content", "The existing file must not be overwritten.");
    Check(Directory.GetFiles(fixture.PathFor("organized")).Length == 2, "Both versions should remain.");
}

private static void TestDuplicates()
{
    using Fixture fixture = new();
    string first = fixture.File("incoming/a.txt", "same bytes");
    string second = fixture.File("incoming/b.txt", "same bytes");
    string other = fixture.File("incoming/c.txt", "different");
    MovePlan[] plans = { Plan(first, fixture.PathFor("organized/a.txt")), Plan(second, fixture.PathFor("organized/b.txt")), Plan(other, fixture.PathFor("organized/c.txt")) };
    var groups = Program.FindExactDuplicates(plans);
    Check(groups.Count == 1 && groups[0].Count == 2, "Only exact-content matches should be grouped.");

    string source = fixture.File("incoming/report.txt", "same bytes");
    string destination = fixture.File("organized/report.txt", "same bytes");
    OrganizationResult result = Program.OrganizeFromDesktop(
        new[] { Plan(source, destination) }, fixture.Database, fixture.Duplicates);
    Check(result.Duplicates == 1 && result.Errors == 0, "An exact destination conflict should go to duplicate review.");
    Check(File.Exists(destination) && !File.Exists(source), "The existing destination should be preserved.");
    Check(Directory.GetFiles(fixture.Duplicates).Length == 1, "The new copy should be available for review.");
}

private static void TestUndoConflict()
{
    using Fixture fixture = new();
    string source = fixture.File("incoming/report.txt", "moved content");
    string destination = fixture.PathFor("organized/report.txt");
    Program.OrganizeFromDesktop(new[] { Plan(source, destination) }, fixture.Database, fixture.Duplicates);
    fixture.File("incoming/report.txt", "replacement content");
    UndoResult result = Program.UndoLastOrganizationFromDesktop(fixture.Database);
    Check(result.Restored == 0 && result.Skipped == 1, "Undo must skip an occupied original path.");
    Check(File.ReadAllText(source) == "replacement content", "Undo must not overwrite the replacement.");
    Check(File.ReadAllText(destination) == "moved content", "The moved file must remain recoverable.");
}

private static void TestDeletionConfirmation()
{
    using Fixture fixture = new();
    string selected = fixture.File("incoming/delete-me.txt", "sample only");
    string unselected = fixture.File("incoming/keep-me.txt", "keep");
    MovePlan plan = Plan(selected, fixture.PathFor("organized/delete-me.txt"));
    bool rejected = false;
    try { Program.DeleteSelectedFiles(new[] { plan }, false); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected && File.Exists(selected), "Deletion without confirmation must leave the file untouched.");
    Check(Program.DeleteSelectedFiles(new[] { plan }, true) == 1, "Confirmed deletion should report one file.");
    Check(!File.Exists(selected) && File.Exists(unselected), "Only the selected disposable file may be deleted.");
}

private static void TestAiFallback()
{
    using Fixture fixture = new();
    string source = fixture.File("incoming/unlabeled_document.txt", "Synthetic meeting notes for testing.");
    var plans = Program.ScanFromDesktopAsync(
        new[] { fixture.PathFor("incoming") },
        1,
        fixture.Database,
        new Uri("http://127.0.0.1:1")).GetAwaiter().GetResult();
    Check(plans.Count == 1 && plans[0].Source == source,
        "A local-AI connection failure must not hide the file from review.");
}

private static void TestAiSmoke()
{
    using Fixture fixture = new();
    fixture.File("incoming/unlabeled_document_a.txt", "Synthetic project meeting notes about a new feature.");
    fixture.File("incoming/unlabeled_document_b.txt", "Synthetic personal letter about a family visit.");
    Stopwatch stopwatch = Stopwatch.StartNew();
    var plans = Program.ScanFromDesktopAsync(
        new[] { fixture.PathFor("incoming") },
        2,
        fixture.Database,
        new Uri("http://localhost:11434")).GetAwaiter().GetResult();
    Check(plans.Count == 2, "Both text files should remain visible after classification.");
    Check(stopwatch.Elapsed < TimeSpan.FromSeconds(45), "The two-file scan took too long.");
    using SqliteConnection connection = new($"Data Source={fixture.Database}");
    connection.Open();
    using SqliteCommand command = new("SELECT COUNT(*) FROM ClassificationCache;", connection);
    Check(Convert.ToInt32(command.ExecuteScalar()) == 2,
        "Both files should receive structured local-AI classifications.");
}

private static MovePlan Plan(string source, string destination) => new(source, destination, "Tests", "Sample");
private static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
}

sealed class Fixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AIFileOrganizer-tests-" + Guid.NewGuid().ToString("N"));
    public Fixture() => Directory.CreateDirectory(_root);
    public string Database => PathFor("history/test.db");
    public string Duplicates => PathFor("duplicates");
    public string PathFor(string relative) => Path.Combine(_root, relative);
    public string File(string relative, string content)
    {
        string path = PathFor(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);
        return path;
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
