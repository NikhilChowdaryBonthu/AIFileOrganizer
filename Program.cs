using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OllamaSharp;
using OllamaSharp.Models;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

class Program
{
    private const string OllamaModel = "qwen3:4b";
    private const int MaxDocumentCharacters = 2500;
    private static readonly TimeSpan AiTimeout = TimeSpan.FromSeconds(45);

    internal static async Task<List<MovePlan>> ScanFromDesktopAsync(
        string[] foldersToScan,
        int scanLimit)
    {
        string homePath = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        string documentsPath = Path.Combine(homePath, "Documents");
        string downloadsPath = Path.Combine(homePath, "Downloads");
        string organizedPath = Path.Combine(documentsPath, "Organized");
        string projectPath = Path.Combine(documentsPath, "AIFileOrganizer");
        string duplicateReviewPath = Path.Combine(downloadsPath, "Duplicates_Review");

        HashSet<string> supportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".heic", ".webp",
            ".mp4", ".mov", ".avi", ".mkv", ".mp3", ".wav", ".m4a", ".zip", ".rar", ".7z",
            ".dmg", ".pkg", ".xlsx", ".xls", ".csv", ".ppt", ".pptx"
        };

        var ollama = new OllamaApiClient(
            new Uri("http://localhost:11434"),
            OllamaModel);

        InitializeDatabase();

        return await ScanFiles(
            foldersToScan,
            organizedPath,
            projectPath,
            duplicateReviewPath,
            supportedExtensions,
            ollama,
            scanLimit);
    }

    internal static OrganizationResult OrganizeFromDesktop(
        IEnumerable<MovePlan> movePlans)
    {
        InitializeDatabase();

        string homePath = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        string duplicateReviewPath = Path.Combine(
            homePath,
            "Downloads",
            "Duplicates_Review");
        string operationId = Guid.NewGuid().ToString();
        int moved = 0;
        int renamed = 0;
        int duplicates = 0;
        int errors = 0;

        foreach (MovePlan plan in movePlans)
        {
            try
            {
                if (!File.Exists(plan.Source))
                {
                    errors++;
                    continue;
                }

                string? destinationFolder = Path.GetDirectoryName(plan.Destination);
                if (string.IsNullOrWhiteSpace(destinationFolder))
                {
                    errors++;
                    continue;
                }

                Directory.CreateDirectory(destinationFolder);
                string finalDestination = plan.Destination;
                string action = "MOVED";

                if (File.Exists(finalDestination))
                {
                    if (FilesAreIdentical(plan.Source, finalDestination))
                    {
                        Directory.CreateDirectory(duplicateReviewPath);
                        finalDestination = GetUniqueDuplicatePath(
                            duplicateReviewPath,
                            Path.GetFileName(plan.Source));
                        action = "DUPLICATE";
                        duplicates++;
                    }
                    else
                    {
                        finalDestination = GetUniqueDestinationPath(finalDestination);
                        action = "VERSIONED";
                        renamed++;
                    }
                }
                else
                {
                    moved++;
                }

                File.Move(plan.Source, finalDestination);
                RecordHistory(
                    action,
                    operationId,
                    plan.Source,
                    finalDestination,
                    plan.Category,
                    plan.Subcategory);
            }
            catch
            {
                errors++;
            }
        }

        return new OrganizationResult(moved, renamed, duplicates, errors);
    }

    internal static UndoResult UndoLastOrganizationFromDesktop()
    {
        InitializeDatabase();
        string connectionString = $"Data Source={GetDatabasePath()}";
        using SqliteConnection connection = new(connectionString);
        connection.Open();

        using SqliteCommand operationCommand = new(
            "SELECT OperationId FROM FileOperations WHERE IsUndone = 0 ORDER BY Id DESC LIMIT 1;",
            connection);
        object? operationResult = operationCommand.ExecuteScalar();

        if (operationResult is null || operationResult == DBNull.Value)
            return new UndoResult(0, 0, false);

        string operationId = Convert.ToString(operationResult)!;
        using SqliteCommand filesCommand = new(
            "SELECT Id, SourcePath, DestinationPath FROM FileOperations WHERE OperationId = $operationId AND IsUndone = 0 ORDER BY Id DESC;",
            connection);
        filesCommand.Parameters.AddWithValue("$operationId", operationId);

        List<(long Id, string Source, string Destination)> files = new();
        using (SqliteDataReader reader = filesCommand.ExecuteReader())
        {
            while (reader.Read())
                files.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
        }

        int restored = 0;
        int skipped = 0;
        foreach ((long id, string source, string destination) in files)
        {
            try
            {
                if (!File.Exists(destination) || File.Exists(source))
                {
                    skipped++;
                    continue;
                }

                string? originalFolder = Path.GetDirectoryName(source);
                if (!string.IsNullOrWhiteSpace(originalFolder))
                    Directory.CreateDirectory(originalFolder);

                File.Move(destination, source);
                using SqliteCommand updateCommand = new(
                    "UPDATE FileOperations SET IsUndone = 1 WHERE Id = $id;",
                    connection);
                updateCommand.Parameters.AddWithValue("$id", id);
                updateCommand.ExecuteNonQuery();
                restored++;
            }
            catch
            {
                skipped++;
            }
        }

        return new UndoResult(restored, skipped, true);
    }

    static async Task Main()
    {
        string homePath =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile
            );

        string downloadsPath =
            Path.Combine(homePath, "Downloads");

        string desktopPath =
            Path.Combine(homePath, "Desktop");

        string documentsPath =
            Path.Combine(homePath, "Documents");

        string organizedPath =
            Path.Combine(
                documentsPath,
                "Organized"
            );

        string projectPath =
            Path.Combine(
                documentsPath,
                "AIFileOrganizer"
            );

        string duplicateReviewPath =
            Path.Combine(
                downloadsPath,
                "Duplicates_Review"
            );

        HashSet<string> supportedExtensions =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".pdf",
                ".docx",
                ".txt",

                ".jpg",
                ".jpeg",
                ".png",
                ".gif",
                ".heic",
                ".webp",

                ".mp4",
                ".mov",
                ".avi",
                ".mkv",

                ".mp3",
                ".wav",
                ".m4a",

                ".zip",
                ".rar",
                ".7z",

                ".dmg",
                ".pkg",

                ".xlsx",
                ".xls",
                ".csv",

                ".ppt",
                ".pptx"
            };

        var ollama = new OllamaApiClient(
            new Uri("http://localhost:11434"),
            OllamaModel
        );
        InitializeDatabase();

        List<MovePlan> movePlans = new();

        while (true)
        {
            Console.Clear();

            Console.WriteLine("========== AI FILE ORGANIZER ==========");
            Console.WriteLine();
            Console.WriteLine("1. Scan files");
            Console.WriteLine("2. Preview organization");
            Console.WriteLine("3. Organize files");
            Console.WriteLine("4. Review duplicates");
            Console.WriteLine("5. Undo last organization");
            Console.WriteLine("6. Exit");
            Console.WriteLine();
            Console.Write("Choose an option: ");

            string? choice =
                Console.ReadLine();

            Console.WriteLine();

            if (choice == "1")
            {
                string[] foldersToScan = ChooseFoldersToScan(
                    downloadsPath,
                    desktopPath,
                    documentsPath
                );

                if (foldersToScan.Length == 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("No valid folder was selected.");
                    Console.WriteLine("Press ENTER to return to the menu.");
                    Console.ReadLine();
                    continue;
                }

                int scanLimit = ChooseScanLimit();

                movePlans = await ScanFiles(
                    foldersToScan,
                    organizedPath,
                    projectPath,
                    duplicateReviewPath,
                    supportedExtensions,
                    ollama,
                    scanLimit
                );

                Console.WriteLine();
                Console.WriteLine(
                    $"Scan complete. Files ready: {movePlans.Count}"
                );

                Console.WriteLine();
                Console.WriteLine(
                    "Press ENTER to return to the menu."
                );

                Console.ReadLine();
            }
            else if (choice == "2")
            {
                if (movePlans.Count == 0)
                {
                    Console.WriteLine(
                        "No scan results are available."
                    );

                    Console.WriteLine();
                    Console.WriteLine(
                        "Please choose option 1 first."
                    );
                }
                else
                {
                    movePlans = ReviewMovePlans(movePlans);
                }

                Console.WriteLine();
                Console.WriteLine(
                    "Press ENTER to return to the menu."
                );

                Console.ReadLine();
            }
            else if (choice == "3")
            {
                if (movePlans.Count == 0)
                {
                    Console.WriteLine(
                        "No files are ready to organize."
                    );

                    Console.WriteLine();
                    Console.WriteLine(
                        "Please choose option 1 first."
                    );
                }
                else
                {
                    OrganizeFiles(
                        movePlans,
                        duplicateReviewPath
                    );

                    // Clear the old plan after the organize attempt.
                    // Run Scan again before the next organize action.
                    movePlans =
                        new List<MovePlan>();
                }

                Console.WriteLine();
                Console.WriteLine(
                    "Press ENTER to return to the menu."
                );

                Console.ReadLine();
            }
            else if (choice == "4")
            {
                ReviewDuplicates(
                    duplicateReviewPath
                );

                Console.WriteLine();
                Console.WriteLine(
                    "Press ENTER to return to the menu."
                );

                Console.ReadLine();
            }
            else if (choice == "5")
            {
                UndoLastOrganization();

                Console.WriteLine();
                Console.WriteLine(
                    "Press ENTER to return to the menu."
                );

                Console.ReadLine();
            }
            else if (choice == "6")
            {
                Console.WriteLine(
                    "Exiting AI File Organizer."
                );

                return;
            }
            else
            {
                Console.WriteLine(
                    "Invalid option. Please choose 1, 2, 3, 4, 5, or 6."
                );

                Console.WriteLine();
                Console.WriteLine(
                    "Press ENTER to try again."
                );

                Console.ReadLine();
            }
        }
    }
    static int ChooseScanLimit()
    {
        Console.WriteLine("Choose scan size:");
        Console.WriteLine("1. 20 files");
        Console.WriteLine("2. 50 files");
        Console.WriteLine("3. 100 files");
        Console.WriteLine("4. All supported files");
        Console.WriteLine();
        Console.Write("Choose an option: ");

        return Console.ReadLine() switch
        {
            "2" => 50,
            "3" => 100,
            "4" => int.MaxValue,
            _ => 20
        };
    }

    static string[] ChooseFoldersToScan(
        string downloadsPath,
        string desktopPath,
        string documentsPath)
    {
        Console.WriteLine("Choose where to scan:");
        Console.WriteLine("1. Downloads");
        Console.WriteLine("2. Desktop");
        Console.WriteLine("3. Documents");
        Console.WriteLine("4. Downloads, Desktop, and Documents");
        Console.WriteLine("5. Enter a custom folder path");
        Console.WriteLine();
        Console.Write("Choose an option: ");

        string? choice = Console.ReadLine();

        if (choice == "1")
        {
            return new[] { downloadsPath };
        }

        if (choice == "2")
        {
            return new[] { desktopPath };
        }

        if (choice == "3")
        {
            return new[] { documentsPath };
        }

        if (choice == "5")
        {
            Console.Write("Enter the full folder path: ");
            string? customPath = Console.ReadLine()?.Trim();

            if (!string.IsNullOrWhiteSpace(customPath) &&
                Directory.Exists(customPath))
            {
                return new[] { Path.GetFullPath(customPath) };
            }

            Console.WriteLine("That folder does not exist.");
            return Array.Empty<string>();
        }

        return new[] { downloadsPath, desktopPath, documentsPath };
    }

    static async Task<List<MovePlan>> ScanFiles(
    string[] foldersToScan,
    string organizedPath,
    string projectPath,
    string duplicateReviewPath,
    HashSet<string> supportedExtensions,
    OllamaApiClient ollama,
    int scanLimit)
    {
        List<string> files =
            new();

        HashSet<string> warnedReadOnlyFolders =
            new(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine();
        Console.WriteLine(
            "========== SCANNING LAPTOP =========="
        );
        Console.WriteLine();

        foreach (string folder in foldersToScan)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            Console.WriteLine(
                $"Scanning: {folder}"
            );

            foreach (string file in SafeGetFiles(folder))
            {
                string extension =
                    Path.GetExtension(file);

                if (!supportedExtensions.Contains(extension))
                {
                    continue;
                }

                if (ShouldSkipFile(
                    file,
                    organizedPath,
                    projectPath,
                    duplicateReviewPath))
                {
                    continue;
                }

                if (!CanMoveFromFolder(file, out string sourceFolder))
                {
                    if (warnedReadOnlyFolders.Add(sourceFolder))
                    {
                        Console.WriteLine(
                            $"Skipping read-only folder: {sourceFolder}"
                        );
                        Console.WriteLine(
                            "Grant your account write access before organizing files from this folder."
                        );
                    }

                    continue;
                }

                files.Add(file);
            }
        }

        files = files
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(scanLimit)
            .ToList();

        Console.WriteLine();
        Console.WriteLine(
            $"Files selected for this test: {files.Count}"
        );

        List<MovePlan> movePlans =
            new();

        for (int index = 0; index < files.Count; index++)
        {
            string file = files[index];
            string fileName =
                Path.GetFileName(file);

            string extension =
                Path.GetExtension(file)
                    .ToLowerInvariant();

            Console.WriteLine();
            Console.WriteLine(
                $"[{index + 1}/{files.Count}] Analyzing: {fileName}"
            );

            try
            {
                Classification classification;
                string method;
                Classification? filenameClassification =
                    TryClassifyByFileName(file, fileName);

                if (
                    extension == ".pdf" ||
                    extension == ".docx" ||
                    extension == ".txt"
                )
                {
                    if (filenameClassification != null)
                    {
                        classification = filenameClassification;
                        method = "Filename and folder rule";
                    }
                    else
                    {
                        Classification? cachedClassification =
                            TryGetCachedClassification(file);

                        if (cachedClassification != null &&
                            !IsUncategorized(cachedClassification))
                        {
                            classification = cachedClassification;
                            method = "Saved local result";
                        }
                        else
                        {
                            classification = await ClassifyDocumentWithAI(
                                file,
                                fileName,
                                ollama
                            );

                            SaveClassificationCache(file, classification);
                            method = "Local AI";
                        }
                    }
                }
                else
                {
                    if (filenameClassification != null)
                    {
                        classification = filenameClassification;
                        method = "Filename and folder rule";
                    }
                    else
                    {
                        classification =
                            ClassifyByFileContext(
                                file,
                                fileName,
                                extension
                            );

                        method = "Smart File Context Rule";
                    }
                }

                string destinationFolder =
                    Path.Combine(
                        organizedPath,
                        classification.Category,
                        classification.Subcategory
                    );

                string destinationFile =
                    Path.Combine(
                        destinationFolder,
                        fileName
                    );

                movePlans.Add(
                    new MovePlan(
                        file,
                        destinationFile,
                        classification.Category,
                        classification.Subcategory
                    )
                );

                Console.WriteLine(
                    $"Method: {method}"
                );

                Console.WriteLine(
                    $"Category: {classification.Category}"
                );

                Console.WriteLine(
                    $"Subcategory: {classification.Subcategory}"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Could not analyze file."
                );

                Console.WriteLine(
                    $"Reason: {ex.Message}"
                );
            }

            Console.WriteLine();
            Console.WriteLine(
                "----------------------------------------"
            );
        }

        return movePlans;
    }

    static List<MovePlan> ReviewMovePlans(
        List<MovePlan> movePlans)
    {
        List<MovePlan> automaticallyApproved = movePlans
            .Where(plan => !NeedsReview(plan))
            .ToList();

        List<MovePlan> uncertainPlans = movePlans
            .Where(NeedsReview)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("========== SMART REVIEW SUMMARY ==========");
        Console.WriteLine();

        foreach (var group in automaticallyApproved
            .GroupBy(plan => $"{plan.Category}/{plan.Subcategory}")
            .OrderByDescending(group => group.Count()))
        {
            Console.WriteLine($"{group.Key}: {group.Count()} files approved");
        }

        Console.WriteLine();
        Console.WriteLine($"Files approved automatically: {automaticallyApproved.Count}");
        Console.WriteLine($"Files needing your review: {uncertainPlans.Count}");

        if (uncertainPlans.Count == 0)
        {
            return automaticallyApproved;
        }

        Console.WriteLine();
        Console.Write("Review the uncertain files now? (Y/N): ");

        if (!string.Equals(
            Console.ReadLine(),
            "Y",
            StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Uncertain files will stay in their current folders.");
            return automaticallyApproved;
        }

        automaticallyApproved.AddRange(
            ReviewIndividualPlans(uncertainPlans));

        return automaticallyApproved;
    }

    static List<MovePlan> ReviewIndividualPlans(
        List<MovePlan> movePlans)
    {
        Console.WriteLine();
        Console.WriteLine(
            "========== REVIEW ORGANIZATION =========="
        );
        Console.WriteLine();

        List<MovePlan> approvedPlans = new();
        int skipped = 0;

        for (int index = 0; index < movePlans.Count; index++)
        {
            MovePlan plan = movePlans[index];

            Console.WriteLine(
                $"[{index + 1}/{movePlans.Count}] {Path.GetFileName(plan.Source)}"
            );

            Console.WriteLine();
            Console.WriteLine(
                $"Category: {plan.Category}"
            );

            Console.WriteLine(
                $"Subcategory: {plan.Subcategory}"
            );

            Console.WriteLine();
            Console.WriteLine(
                $"Destination: {plan.Destination}"
            );

            Console.WriteLine();
            Console.WriteLine(
                "K = keep   S = skip   C = change category   A = approve remaining   Q = stop review"
            );

            while (true)
            {
                Console.Write("Choose an action: ");
                string? action = Console.ReadLine();

                if (string.Equals(action, "K", StringComparison.OrdinalIgnoreCase))
                {
                    approvedPlans.Add(plan);
                    break;
                }

                if (string.Equals(action, "S", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    break;
                }

                if (string.Equals(action, "C", StringComparison.OrdinalIgnoreCase))
                {
                    plan = ChangeMovePlanCategory(plan);
                    approvedPlans.Add(plan);
                    break;
                }

                if (string.Equals(action, "A", StringComparison.OrdinalIgnoreCase))
                {
                    approvedPlans.AddRange(movePlans.Skip(index));
                    PrintReviewSummary(approvedPlans.Count, skipped);
                    return approvedPlans;
                }

                if (string.Equals(action, "Q", StringComparison.OrdinalIgnoreCase))
                {
                    skipped += movePlans.Count - index;
                    PrintReviewSummary(approvedPlans.Count, skipped);
                    return approvedPlans;
                }

                Console.WriteLine("Choose K, S, C, A, or Q.");
            }

            Console.WriteLine();
        }

        PrintReviewSummary(approvedPlans.Count, skipped);
        return approvedPlans;
    }

    static MovePlan ChangeMovePlanCategory(MovePlan plan)
    {
        Console.Write("New category: ");
        string? category = Console.ReadLine()?.Trim();
        Console.Write("New subcategory: ");
        string? subcategory = Console.ReadLine()?.Trim();

        if (!IsSafeFolderName(category) || !IsSafeFolderName(subcategory))
        {
            Console.WriteLine("Category was not changed. Use a simple folder name without slashes.");
            return plan;
        }

        string newCategory = category!;
        string newSubcategory = subcategory!;

        string organizedRoot = Path.GetDirectoryName(
            Path.GetDirectoryName(
                Path.GetDirectoryName(plan.Destination)!)!)!;

        string destinationFolder = Path.Combine(
            organizedRoot,
            newCategory,
            newSubcategory);

        string destination = Path.Combine(
            destinationFolder,
            Path.GetFileName(plan.Source));

        Console.WriteLine($"Updated destination: {destination}");

        return new MovePlan(
            plan.Source,
            destination,
            newCategory,
            newSubcategory);
    }

    static bool IsSafeFolderName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value != "." &&
            value != ".." &&
            !value.Contains(Path.DirectorySeparatorChar) &&
            !value.Contains(Path.AltDirectorySeparatorChar) &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    static void PrintReviewSummary(int approved, int skipped)
    {
        Console.WriteLine();
        Console.WriteLine($"Files approved to organize: {approved}");
        Console.WriteLine($"Files skipped: {skipped}");
    }

    static bool NeedsReview(MovePlan plan)
    {
        return plan.Category.Equals(
            "Other",
            StringComparison.OrdinalIgnoreCase) &&
            plan.Subcategory.Equals(
                "Uncategorized",
                StringComparison.OrdinalIgnoreCase);
    }
    static void UndoLastOrganization()
{
    string databasePath =
        GetDatabasePath();

    string connectionString =
        $"Data Source={databasePath}";

    using SqliteConnection connection =
        new(connectionString);

    connection.Open();

    Console.WriteLine();
    Console.WriteLine(
        "========== UNDO LAST ORGANIZATION =========="
    );
    Console.WriteLine();

    string lastOperationSql =
        """
        SELECT OperationId
        FROM FileOperations
        WHERE IsUndone = 0
        ORDER BY Id DESC
        LIMIT 1;
        """;

    using SqliteCommand lastOperationCommand =
        new(
            lastOperationSql,
            connection
        );

    object? operationResult =
        lastOperationCommand.ExecuteScalar();

    if (
        operationResult == null ||
        operationResult == DBNull.Value
    )
    {
        Console.WriteLine(
            "There is nothing to undo."
        );

        return;
    }

    string lastOperationId =
        Convert.ToString(
            operationResult
        )!;

    string selectSql =
        """
        SELECT
            Id,
            SourcePath,
            DestinationPath
        FROM FileOperations
        WHERE
            OperationId = $operationId
            AND IsUndone = 0
        ORDER BY Id DESC;
        """;

    using SqliteCommand selectCommand =
        new(
            selectSql,
            connection
        );

    selectCommand.Parameters.AddWithValue(
        "$operationId",
        lastOperationId
    );

    List<(long Id, string Source, string Destination)>
        filesToRestore =
            new();

    using (
        SqliteDataReader reader =
            selectCommand.ExecuteReader()
    )
    {
        while (reader.Read())
        {
            filesToRestore.Add(
                (
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2)
                )
            );
        }
    }

    if (filesToRestore.Count == 0)
    {
        Console.WriteLine(
            "There is nothing to undo."
        );

        return;
    }

    Console.WriteLine(
        $"Files in last organization: {filesToRestore.Count}"
    );

    Console.WriteLine();

    Console.Write(
        "Do you want to restore these files to their original locations? (Y/N): "
    );

    string? answer =
        Console.ReadLine();

    if (
        !string.Equals(
            answer,
            "Y",
            StringComparison.OrdinalIgnoreCase
        )
    )
    {
        Console.WriteLine();
        Console.WriteLine(
            "Undo cancelled. No files were moved."
        );

        return;
    }

    Console.WriteLine();

    int restored =
        0;

    int skipped =
        0;

    foreach (
        var item in filesToRestore
    )
    {
        try
        {
            if (!File.Exists(item.Destination))
            {
                Console.WriteLine(
                    $"SKIPPED: {Path.GetFileName(item.Destination)}"
                );

                Console.WriteLine(
                    "The organized file no longer exists."
                );

                Console.WriteLine();

                skipped++;
                continue;
            }

            if (File.Exists(item.Source))
            {
                Console.WriteLine(
                    $"SKIPPED: {Path.GetFileName(item.Source)}"
                );

                Console.WriteLine(
                    "A file already exists at the original location."
                );

                Console.WriteLine();

                skipped++;
                continue;
            }

            string? originalFolder =
                Path.GetDirectoryName(
                    item.Source
                );

            if (
                !string.IsNullOrWhiteSpace(
                    originalFolder
                )
            )
            {
                Directory.CreateDirectory(
                    originalFolder
                );
            }

            File.Move(
                item.Destination,
                item.Source
            );

            string updateSql =
                """
                UPDATE FileOperations
                SET IsUndone = 1
                WHERE Id = $id;
                """;

            using SqliteCommand updateCommand =
                new(
                    updateSql,
                    connection
                );

            updateCommand.Parameters.AddWithValue(
                "$id",
                item.Id
            );

            updateCommand.ExecuteNonQuery();

            Console.WriteLine(
                $"RESTORED: {Path.GetFileName(item.Source)}"
            );

            Console.WriteLine(
                $"→ {item.Source}"
            );

            Console.WriteLine();

            restored++;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"UNDO ERROR: {Path.GetFileName(item.Source)}"
            );

            Console.WriteLine(
                ex.Message
            );

            Console.WriteLine();

            skipped++;
        }
    }

    Console.WriteLine(
        "========== UNDO COMPLETE =========="
    );

    Console.WriteLine();

    Console.WriteLine(
        $"Files restored: {restored}"
    );

    Console.WriteLine(
        $"Files skipped/errors: {skipped}"
    );

    Console.WriteLine();

    Console.WriteLine(
        "SQLite history updated."
    );

    Console.WriteLine(
        "No files were deleted."
    );
}

    static void OrganizeFiles(
        List<MovePlan> movePlans,
        string duplicateReviewPath)
    {
       

        Console.WriteLine();
        Console.WriteLine(
            "========== MOVE SUMMARY =========="
        );
        Console.WriteLine();

        Console.WriteLine(
            $"Files ready to organize: {movePlans.Count}"
        );

        Console.WriteLine();

        Console.Write(
            "Do you want to move these files? (Y/N): "
        );

        string? answer =
            Console.ReadLine();

        if (
            !string.Equals(
                answer,
                "Y",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            Console.WriteLine();
            Console.WriteLine(
                "Cancelled. No files were moved."
            );

            return;
        }

        // One ID for this entire organization batch.
        // Undo uses this ID to restore the whole batch together.
        string operationId =
            Guid.NewGuid()
                .ToString();

        Console.WriteLine();
        Console.WriteLine(
            "========== ORGANIZING =========="
        );
        Console.WriteLine();

        int moved =
            0;

        int renamed =
            0;

        int duplicatesMoved =
            0;

        int errors =
            0;

        foreach (MovePlan plan in movePlans)
        {
            try
            {
                // A previous file from this same batch may already
                // have moved this source. Skip stale plans safely.
                if (!File.Exists(plan.Source))
                {
                    Console.WriteLine(
                        $"SKIPPED: {Path.GetFileName(plan.Source)}"
                    );

                    Console.WriteLine(
                        "The source file no longer exists."
                    );

                    Console.WriteLine();

                    errors++;
                    continue;
                }

                string? destinationFolder =
                    Path.GetDirectoryName(
                        plan.Destination
                    );

                if (destinationFolder == null)
                {
                    errors++;
                    continue;
                }

                Directory.CreateDirectory(
                    destinationFolder
                );

                string finalDestination =
                    plan.Destination;

                if (File.Exists(finalDestination))
                {
                    bool identical =
                        FilesAreIdentical(
                            plan.Source,
                            finalDestination
                        );

                    if (identical)
                    {
                        Directory.CreateDirectory(
                            duplicateReviewPath
                        );

                        string duplicateDestination =
                            GetUniqueDuplicatePath(
                                duplicateReviewPath,
                                Path.GetFileName(
                                    plan.Source
                                )
                            );

                        File.Move(
                            plan.Source,
                            duplicateDestination
                        );

                        RecordHistory(

                            "DUPLICATE",
                            operationId,
                            plan.Source,
                            duplicateDestination,
                            plan.Category,
                            plan.Subcategory
                        );

                        Console.WriteLine(
                            $"EXACT DUPLICATE: {Path.GetFileName(plan.Source)}"
                        );

                        Console.WriteLine(
                            "Moved to Duplicates_Review."
                        );

                        Console.WriteLine();

                        duplicatesMoved++;
                        continue;
                    }

                    finalDestination =
                        GetUniqueDestinationPath(
                            finalDestination
                        );

                    File.Move(
                        plan.Source,
                        finalDestination
                    );

                    RecordHistory(
            
                        "VERSIONED",
                        operationId,
                        plan.Source,
                        finalDestination,
                        plan.Category,
                        plan.Subcategory
                    );

                    Console.WriteLine(
                        $"MOVED AS NEW VERSION: {Path.GetFileName(plan.Source)}"
                    );

                    Console.WriteLine(
                        $"New name: {Path.GetFileName(finalDestination)}"
                    );

                    Console.WriteLine(
                        $"→ {plan.Category}/{plan.Subcategory}"
                    );

                    Console.WriteLine();

                    renamed++;
                    continue;
                }

                File.Move(
                    plan.Source,
                    finalDestination
                );

                RecordHistory(
                
                    "MOVED",
                    operationId,
                    plan.Source,
                    finalDestination,
                    plan.Category,
                    plan.Subcategory
                );

                Console.WriteLine(
                    $"MOVED: {Path.GetFileName(plan.Source)}"
                );

                Console.WriteLine(
                    $"→ {plan.Category}/{plan.Subcategory}"
                );

                Console.WriteLine();

                moved++;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"ERROR: {Path.GetFileName(plan.Source)}"
                );

                Console.WriteLine(
                    ex.Message
                );

                Console.WriteLine();

                errors++;
            }
        }

        Console.WriteLine(
            "========== COMPLETE =========="
        );

        Console.WriteLine();

        Console.WriteLine(
            $"Moved normally: {moved}"
        );

        Console.WriteLine(
            $"Different versions renamed: {renamed}"
        );

        Console.WriteLine(
            $"Exact duplicates moved to review: {duplicatesMoved}"
        );

        Console.WriteLine(
            $"Errors: {errors}"
        );

        Console.WriteLine();

        Console.WriteLine(
            "No files were deleted."
        );
    }

    static void ReviewDuplicates(
        string duplicateReviewPath)
    {
        Console.WriteLine();
        Console.WriteLine(
            "========== DUPLICATES REVIEW =========="
        );
        Console.WriteLine();

        if (!Directory.Exists(duplicateReviewPath))
        {
            Console.WriteLine(
                "The Duplicates_Review folder does not exist yet."
            );

            Console.WriteLine();
            Console.WriteLine(
                "No duplicate files have been moved there."
            );

            return;
        }

        string[] duplicates =
            SafeGetFiles(
                duplicateReviewPath
            )
            .ToArray();

        if (duplicates.Length == 0)
        {
            Console.WriteLine(
                "No files are currently in Duplicates_Review."
            );

            return;
        }

        Console.WriteLine(
            $"Files in Duplicates_Review: {duplicates.Length}"
        );

        Console.WriteLine();

        foreach (string file in duplicates)
        {
            Console.WriteLine(
                Path.GetFileName(file)
            );

            Console.WriteLine(
                file
            );

            Console.WriteLine(
                "----------------------------------------"
            );
        }
    }

    static async Task<Classification>
        ClassifyDocumentWithAI(
            string file,
            string fileName,
            OllamaApiClient ollama)
    {
        string content =
            ExtractText(file);

        if (
            string.IsNullOrWhiteSpace(
                content
            )
        )
        {
            Console.WriteLine(
                "No readable text found. Using filename only."
            );

            content =
                fileName;
        }

        if (content.Length > MaxDocumentCharacters)
        {
            content =
                content[..MaxDocumentCharacters];
        }

        string prompt =
            $"""
            Classify the file into one exact value from this list:
            Career|Resumes, Career|Job Descriptions, Career|Interviews,
            Career|Employment, Career|Professional, Education|Assignments,
            Education|Research, Education|Academic Articles, Education|Admissions,
            Education|Certificates, Education|Course Materials,
            Finance|Bank Statements, Finance|Bills, Finance|Receipts,
            Finance|Taxes, Finance|Other Finance, Personal|Personal Documents,
            Personal|Letters, Personal|Other Personal, Other|Uncategorized.

            Reply with only Category|Subcategory. Do not explain your choice.

            Filename: {fileName}
            Content: {content}
            """;

        StringBuilder aiResponse =
            new();

        using CancellationTokenSource timeout =
            new(AiTimeout);

        GenerateRequest request =
            new()
            {
                Model = OllamaModel,
                Prompt = prompt,
                Think = false,
                Options = new RequestOptions
                {
                    NumPredict = 16,
                    Temperature = 0
                }
            };

        await foreach (
            var response in
            ollama.GenerateAsync(request, timeout.Token)
        )
        {
            if (
                response?.Response != null
            )
            {
                aiResponse.Append(
                    response.Response
                );
            }
        }

        return ParseAIClassification(
            aiResponse.ToString()
        );
    }

    static Classification
        ClassifyByFileContext(
            string filePath,
            string fileName,
            string extension)
    {
        string lowerPath =
            filePath
                .ToLowerInvariant();

        string lowerFileName =
    fileName
        .ToLowerInvariant();

string lowerFileStem =
    Path.GetFileNameWithoutExtension(
        fileName
    )
    .ToLowerInvariant();

// Recognize personal identity documents
// based on safe filename keywords.
bool isPersonalDocument =
    lowerFileName.Contains(
        "ssn"
    ) ||
    lowerFileName.Contains(
        "social security"
    ) ||
    lowerFileName.Contains(
        "passport"
    ) ||
    lowerFileName.Contains(
        "driver license"
    ) ||
    lowerFileName.Contains(
        "drivers license"
    ) ||
    lowerFileName.Contains(
        "driving license"
    ) ||
    lowerFileStem == "dl" ||
    lowerFileStem.Contains(
        "_dl"
    ) ||
    lowerFileStem.Contains(
        "-dl"
    );

if (isPersonalDocument)
{
    return new Classification(
        "Personal",
        "Personal Documents"
    );
}

// Recognize the C# class-recording folder

        // Recognize the C# class-recording folder
        // and common C# course filenames.
        bool isCSharpCourse =
            lowerPath.Contains(
                "c# class recordings"
            ) ||
            lowerFileName.Contains(
                "c#day"
            ) ||
            lowerFileName.Contains(
                "c# day"
            ) ||
            (
                lowerFileName.Contains(
                    "day"
                ) &&
                lowerFileName.Contains(
                    "c#"
                )
            );

        if (isCSharpCourse)
        {
            if (
                extension == ".mp4" ||
                extension == ".mov" ||
                extension == ".avi" ||
                extension == ".mkv"
            )
            {
                return new Classification(
                    "Education",
                    Path.Combine(
                        "Course Materials",
                        "CSharp",
                        "Videos"
                    )
                );
            }

            if (
                extension == ".zip" ||
                extension == ".rar" ||
                extension == ".7z"
            )
            {
                return new Classification(
                    "Education",
                    Path.Combine(
                        "Course Materials",
                        "CSharp",
                        "Archives"
                    )
                );
            }
        }

        return ClassifyByExtension(
            extension
        );
    }

    static Classification? TryClassifyByFileName(
        string filePath,
        string fileName)
    {
        string value =
            NormalizeForMatching($"{filePath} {fileName}");

        if (ContainsAny(value,
            "passport", "driver license", "drivers license", "driving license",
            "social security", "ssn", "i20", "i 20", "i94", "i 94",
            "ead", "visa", "affidavit", "medical document") ||
            HasAnyToken(value, "dl"))
        {
            return new Classification("Personal", "Personal Documents");
        }

        if (ContainsAny(value,
            "job description", "job role", "position description"))
        {
            return new Classification("Career", "Job Descriptions");
        }

        if (ContainsAny(value,
            "admit letter", "admission", "transfer in", "transfer form",
            "statement of purpose", " sop ", "recommendation letter",
            "decision letter"))
        {
            return new Classification("Education", "Admissions");
        }

        if (ContainsAny(value,
            "gre", "toefl", "tofel", "scorecard", "provisional certificate",
            "certificate", "10 12"))
        {
            return new Classification("Education", "Certificates");
        }

        if (ContainsAny(value,
            "resume", "curriculum vitae", "_cv", " cv.",
            "full stack developer", ".net developer", ".net engineer",
            "software engineer", "tech lead", "backend developer",
            "microsoft dynamics"))
        {
            return new Classification("Career", "Resumes");
        }

        if (ContainsAny(value,
            "interview", "first round", "questions", "assessment",
            "screening", "recruiter"))
        {
            return new Classification("Career", "Interviews");
        }

        if (ContainsAny(value,
            "offer letter", "employment agreement", "employment contract",
            "employee information", "direct deposit", "anti-discrimination",
            "compliance document", "pre-employment", "consultant"))
        {
            return new Classification("Career", "Employment");
        }

        if (ContainsAny(value,
            "w4", "w-4", "tax", "1099"))
        {
            return new Classification("Finance", "Taxes");
        }

        if (ContainsAny(value,
            "paycheck", "pay stub", "bank statement", "statement",
            "receipt", "invoice", "billing"))
        {
            return new Classification("Finance", "Other Finance");
        }

        if (ContainsAny(value,
            "passport", "driver license", "drivers license", "driving license",
            "social security", "ssn", "i9", "i-9", "medical document"))
        {
            return new Classification("Personal", "Personal Documents");
        }

        if (ContainsAny(value,
            "reservation", "airlines", "flight", "travel", "hotel"))
        {
            return new Classification("Personal", "Other Personal");
        }

        if (ContainsAny(value,
            "course", "session", "assignment", "syllabus", "aws-v",
            "resource pack"))
        {
            return new Classification("Education", "Course Materials");
        }

        return null;
    }

    static bool ContainsAny(string value, params string[] keywords)
    {
        return keywords.Any(keyword =>
            value.Contains(
                NormalizeForMatching(keyword),
                StringComparison.Ordinal));
    }

    static bool HasAnyToken(string value, params string[] tokens)
    {
        string[] words = value.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);

        return tokens.Any(token => words.Contains(
            token,
            StringComparer.Ordinal));
    }

    static string NormalizeForMatching(string value)
    {
        StringBuilder normalized = new();
        bool previousWasSpace = false;

        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                normalized.Append(' ');
                previousWasSpace = true;
            }
        }

        return normalized.ToString().Trim();
    }

    static bool IsUncategorized(Classification classification)
    {
        return classification.Category.Equals(
            "Other",
            StringComparison.OrdinalIgnoreCase) &&
            classification.Subcategory.Equals(
                "Uncategorized",
                StringComparison.OrdinalIgnoreCase);
    }

    static Classification
        ClassifyByExtension(
            string extension)
    {
        return extension switch
        {
            ".jpg" or
            ".jpeg" or
            ".png" or
            ".gif" or
            ".heic" or
            ".webp"
                => new Classification(
                    "Media",
                    "Images"
                ),

            ".mp4" or
            ".mov" or
            ".avi" or
            ".mkv"
                => new Classification(
                    "Media",
                    "Videos"
                ),

            ".mp3" or
            ".wav" or
            ".m4a"
                => new Classification(
                    "Media",
                    "Audio"
                ),

            ".zip" or
            ".rar" or
            ".7z"
                => new Classification(
                    "Archives",
                    "Compressed Files"
                ),

            ".dmg" or
            ".pkg"
                => new Classification(
                    "Software",
                    "Installers"
                ),

            ".xlsx" or
            ".xls" or
            ".csv"
                => new Classification(
                    "Documents",
                    "Spreadsheets"
                ),

            ".ppt" or
            ".pptx"
                => new Classification(
                    "Documents",
                    "Presentations"
                ),

            _ =>
                new Classification(
                    "Other",
                    "Uncategorized"
                )
        };
    }

    static IEnumerable<string>
        SafeGetFiles(
            string folder)
    {
        EnumerationOptions options =
            new()
            {
                RecurseSubdirectories =
                    true,

                IgnoreInaccessible =
                    true,

                ReturnSpecialDirectories =
                    false
            };

        try
        {
            return Directory
                .EnumerateFiles(
                    folder,
                    "*",
                    options
                );
        }
        catch
        {
            return Enumerable
                .Empty<string>();
        }
    }

    static bool ShouldSkipFile(
        string filePath,
        string organizedPath,
        string projectPath,
        string duplicateReviewPath)
    {
        if (Path.GetFileName(filePath).Equals(
            ".DS_Store",
            StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IsInsideFolder(filePath, organizedPath) ||
            IsInsideFolder(filePath, projectPath) ||
            IsInsideFolder(filePath, duplicateReviewPath))
        {
            return true;
        }

        string[] pathParts =
            filePath.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

        if (pathParts.Any(part =>
            part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Library", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("Caches", StringComparison.OrdinalIgnoreCase) ||
            part.StartsWith(".", StringComparison.Ordinal)))
        {
            return true;
        }

        return Path.GetFileName(filePath).EndsWith(
            ".csproj.FileListAbsolute.txt",
            StringComparison.OrdinalIgnoreCase);
    }

    static bool CanMoveFromFolder(
        string filePath,
        out string sourceFolder)
    {
        sourceFolder = Path.GetDirectoryName(filePath) ?? "Unknown folder";

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            UnixFileMode mode = File.GetUnixFileMode(sourceFolder);

            return (mode &
                (UnixFileMode.UserWrite |
                 UnixFileMode.GroupWrite |
                 UnixFileMode.OtherWrite)) != 0;
        }
        catch
        {
            return true;
        }
    }

    static bool IsInsideFolder(
        string filePath,
        string folderPath)
    {
        string fullFilePath =
            Path.GetFullPath(
                filePath
            );

        string fullFolderPath =
            Path.GetFullPath(
                folderPath
            )
            .TrimEnd(
                Path.DirectorySeparatorChar
            )
            + Path.DirectorySeparatorChar;

        return fullFilePath
            .StartsWith(
                fullFolderPath,
                StringComparison.OrdinalIgnoreCase
            );
    }

    static bool FilesAreIdentical(
        string file1,
        string file2)
    {
        FileInfo info1 =
            new(file1);

        FileInfo info2 =
            new(file2);

        if (
            info1.Length !=
            info2.Length
        )
        {
            return false;
        }

        string hash1 =
            CalculateFileHash(
                file1
            );

        string hash2 =
            CalculateFileHash(
                file2
            );

        return string.Equals(
            hash1,
            hash2,
            StringComparison.OrdinalIgnoreCase
        );
    }

    static string CalculateFileHash(
        string filePath)
    {
        using SHA256 sha256 =
            SHA256.Create();

        using FileStream stream =
            File.OpenRead(
                filePath
            );

        byte[] hashBytes =
            sha256.ComputeHash(
                stream
            );

        return Convert.ToHexString(
            hashBytes
        );
    }

    static string GetUniqueDestinationPath(
        string originalDestination)
    {
        string? folder =
            Path.GetDirectoryName(
                originalDestination
            );

        if (folder == null)
        {
            return originalDestination;
        }

        string fileName =
            Path.GetFileNameWithoutExtension(
                originalDestination
            );

        string extension =
            Path.GetExtension(
                originalDestination
            );

        int number =
            1;

        while (true)
        {
            string newFileName =
                $"{fileName}_{number}{extension}";

            string newPath =
                Path.Combine(
                    folder,
                    newFileName
                );

            if (!File.Exists(newPath))
            {
                return newPath;
            }

            number++;
        }
    }

    static string GetUniqueDuplicatePath(
        string folder,
        string fileName)
    {
        string destination =
            Path.Combine(
                folder,
                fileName
            );

        if (!File.Exists(destination))
        {
            return destination;
        }

        string name =
            Path.GetFileNameWithoutExtension(
                fileName
            );

        string extension =
            Path.GetExtension(
                fileName
            );

        int number =
            1;

        while (true)
        {
            string newName =
                $"{name}_{number}{extension}";

            destination =
                Path.Combine(
                    folder,
                    newName
                );

            if (!File.Exists(destination))
            {
                return destination;
            }

            number++;
        }
    }

    static string ExtractText(
        string filePath)
    {
        string extension =
            Path.GetExtension(
                filePath
            )
            .ToLowerInvariant();

        return extension switch
        {
            ".pdf" =>
                ReadPdf(
                    filePath
                ),

            ".docx" =>
                ReadWordDocument(
                    filePath
                ),

            ".txt" =>
                File.ReadAllText(
                    filePath
                ),

            _ =>
                ""
        };
    }

    static string ReadPdf(
        string filePath)
    {
        StringBuilder text =
            new();

        using PdfDocument document =
            PdfDocument.Open(
                filePath
            );

        foreach (
            var page in
            document.GetPages()
        )
        {
            string pageText =
                ContentOrderTextExtractor
                    .GetText(
                        page
                    );

            text.AppendLine(
                pageText
            );

            if (text.Length >= 6000)
            {
                break;
            }
        }

        return text.ToString();
    }

    static string ReadWordDocument(
        string filePath)
    {
        using WordprocessingDocument document =
            WordprocessingDocument.Open(
                filePath,
                false
            );

        return document
                   .MainDocumentPart?
                   .Document?
                   .Body?
                   .InnerText
               ?? "";
    }

    static Classification
        ParseAIClassification(
            string aiResponse)
    {
        string result =
            aiResponse
                .Trim()
                .Replace(
                    "\n",
                    ""
                )
                .Replace(
                    "\r",
                    ""
                );

        string[] parts =
            result.Split('|');


        if (parts.Length < 2)
        {
            return new Classification(
                "Other",
                "Uncategorized"
            );
        }

        string category =
            parts[0]
                .Trim();

        string subcategory =
            parts[1]
                .Trim();

        Dictionary<string, string[]>
            allowed =
                new(
                    StringComparer
                        .OrdinalIgnoreCase
                )
                {
                    ["Career"] =
                        new[]
                        {
                            "Resumes",
                            "Job Descriptions",
                            "Interviews",
                            "Employment",
                            "Professional"
                        },

                    ["Education"] =
                        new[]
                        {
                            "Assignments",
                            "Research",
                            "Academic Articles",
                            "Admissions",
                            "Certificates",
                            "Course Materials"
                        },

                    ["Finance"] =
                        new[]
                        {
                            "Bank Statements",
                            "Bills",
                            "Receipts",
                            "Taxes",
                            "Other Finance"
                        },

                    ["Personal"] =
                        new[]
                        {
                            "Personal Documents",
                            "Letters",
                            "Other Personal"
                        },

                    ["Other"] =
                        new[]
                        {
                            "Uncategorized"
                        }
                };

        string? validCategory =
            allowed.Keys
                .FirstOrDefault(
                    x =>
                        x.Equals(
                            category,
                            StringComparison
                                .OrdinalIgnoreCase
                        )
                );

        if (validCategory == null)
        {
            return new Classification(
                "Other",
                "Uncategorized"
            );
        }

        string? validSubcategory =
            allowed[validCategory]
                .FirstOrDefault(
                    x =>
                        x.Equals(
                            subcategory,
                            StringComparison
                                .OrdinalIgnoreCase
                        )
                );

        if (validSubcategory == null)
        {
            return new Classification(
                "Other",
                "Uncategorized"
            );
        }

        return new Classification(
            validCategory,
            validSubcategory
        );
    }
    static string GetDatabasePath()
    {
        string homePath =
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile
            );

        string databaseFolder =
            Path.Combine(
                homePath,
                "Documents",
                "AIFileOrganizer",
                "Database"
            );

        Directory.CreateDirectory(
            databaseFolder
        );

        return Path.Combine(
            databaseFolder,
            "AIFileOrganizer.db"
        );
    }

    static void InitializeDatabase()
    {
        string databasePath =
            GetDatabasePath();

        string connectionString =
            $"Data Source={databasePath}";

        using SqliteConnection connection =
            new(connectionString);

        connection.Open();

        string createTableSql =
            """
            CREATE TABLE IF NOT EXISTS FileOperations
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OperationId TEXT NOT NULL,
                Action TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                DestinationPath TEXT NOT NULL,
                Category TEXT NOT NULL,
                Subcategory TEXT NOT NULL,
                Timestamp TEXT NOT NULL,
                IsUndone INTEGER NOT NULL DEFAULT 0
            );
            """;

        using SqliteCommand command =
            new(
                createTableSql,
                connection
            );

        command.ExecuteNonQuery();

        string createCacheSql =
            """
            CREATE TABLE IF NOT EXISTS ClassificationCache
            (
                SourcePath TEXT PRIMARY KEY,
                FileSize INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                Category TEXT NOT NULL,
                Subcategory TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;

        using SqliteCommand cacheCommand =
            new(createCacheSql, connection);

        cacheCommand.ExecuteNonQuery();
    }

    static Classification? TryGetCachedClassification(string filePath)
    {
        try
        {
            FileInfo fileInfo = new(filePath);

            if (!fileInfo.Exists)
            {
                return null;
            }

            using SqliteConnection connection =
                new($"Data Source={GetDatabasePath()}");

            connection.Open();

            using SqliteCommand command =
                new(
                    """
                    SELECT Category, Subcategory
                    FROM ClassificationCache
                    WHERE SourcePath = $sourcePath
                      AND FileSize = $fileSize
                      AND LastWriteUtcTicks = $lastWriteUtcTicks;
                    """,
                    connection
                );

            command.Parameters.AddWithValue("$sourcePath", filePath);
            command.Parameters.AddWithValue("$fileSize", fileInfo.Length);
            command.Parameters.AddWithValue(
                "$lastWriteUtcTicks",
                fileInfo.LastWriteTimeUtc.Ticks);

            using SqliteDataReader reader = command.ExecuteReader();

            return reader.Read()
                ? new Classification(reader.GetString(0), reader.GetString(1))
                : null;
        }
        catch
        {
            return null;
        }
    }

    static void SaveClassificationCache(
        string filePath,
        Classification classification)
    {
        FileInfo fileInfo = new(filePath);

        using SqliteConnection connection =
            new($"Data Source={GetDatabasePath()}");

        connection.Open();

        using SqliteCommand command =
            new(
                """
                INSERT INTO ClassificationCache
                (
                    SourcePath,
                    FileSize,
                    LastWriteUtcTicks,
                    Category,
                    Subcategory,
                    UpdatedAt
                )
                VALUES
                (
                    $sourcePath,
                    $fileSize,
                    $lastWriteUtcTicks,
                    $category,
                    $subcategory,
                    $updatedAt
                )
                ON CONFLICT(SourcePath) DO UPDATE SET
                    FileSize = excluded.FileSize,
                    LastWriteUtcTicks = excluded.LastWriteUtcTicks,
                    Category = excluded.Category,
                    Subcategory = excluded.Subcategory,
                    UpdatedAt = excluded.UpdatedAt;
                """,
                connection
            );

        command.Parameters.AddWithValue("$sourcePath", filePath);
        command.Parameters.AddWithValue("$fileSize", fileInfo.Length);
        command.Parameters.AddWithValue(
            "$lastWriteUtcTicks",
            fileInfo.LastWriteTimeUtc.Ticks);
        command.Parameters.AddWithValue("$category", classification.Category);
        command.Parameters.AddWithValue("$subcategory", classification.Subcategory);
        command.Parameters.AddWithValue(
            "$updatedAt",
            DateTime.UtcNow.ToString("O"));

        command.ExecuteNonQuery();
    }

  
   static void RecordHistory(
    string action,
    string operationId,
    string source,
    string destination,
    string category,
    string subcategory)
{
    string timestamp =
        DateTime.Now.ToString(
            "yyyy-MM-dd HH:mm:ss"
        );

    string databasePath =
        GetDatabasePath();

    string connectionString =
        $"Data Source={databasePath}";

    using SqliteConnection connection =
        new(connectionString);

    connection.Open();

    string insertSql =
        """
        INSERT INTO FileOperations
        (
            OperationId,
            Action,
            SourcePath,
            DestinationPath,
            Category,
            Subcategory,
            Timestamp,
            IsUndone
        )
        VALUES
        (
            $operationId,
            $action,
            $sourcePath,
            $destinationPath,
            $category,
            $subcategory,
            $timestamp,
            0
        );
        """;

    using SqliteCommand command =
        new(
            insertSql,
            connection
        );

    command.Parameters.AddWithValue(
        "$operationId",
        operationId
    );

    command.Parameters.AddWithValue(
        "$action",
        action
    );

    command.Parameters.AddWithValue(
        "$sourcePath",
        source
    );

    command.Parameters.AddWithValue(
        "$destinationPath",
        destination
    );

    command.Parameters.AddWithValue(
        "$category",
        category
    );

    command.Parameters.AddWithValue(
        "$subcategory",
        subcategory
    );

    command.Parameters.AddWithValue(
        "$timestamp",
        timestamp
    );

    command.ExecuteNonQuery();
}
       
}

record Classification(
    string Category,
    string Subcategory
);

record MovePlan(
    string Source,
    string Destination,
    string Category,
    string Subcategory
);

record OrganizationResult(
    int Moved,
    int Renamed,
    int Duplicates,
    int Errors
);

record UndoResult(
    int Restored,
    int Skipped,
    bool FoundOrganization
);
