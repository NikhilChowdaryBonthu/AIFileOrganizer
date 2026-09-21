using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using OllamaSharp;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

class Program
{
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

        string[] foldersToScan =
        {
            downloadsPath,
            desktopPath,
            documentsPath
        };

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
            "qwen3:4b"
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
                Console.WriteLine("Choose scan size:");
Console.WriteLine("1. 20 files");
Console.WriteLine("2. 50 files");
Console.WriteLine("3. 100 files");
Console.WriteLine("4. All supported files");
Console.WriteLine();
Console.Write("Choose an option: ");

string? scanChoice =
    Console.ReadLine();

int scanLimit =
    scanChoice switch
    {
        "2" => 50,
        "3" => 100,
        "4" => int.MaxValue,
        _ => 20
    };

Console.WriteLine();
               movePlans =
    await ScanFiles(
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
                    ShowPreview(movePlans);
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

                if (
                    Path.GetFileName(file)
                        .Equals(
                            ".DS_Store",
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                {
                    continue;
                }

                if (IsInsideFolder(file, organizedPath))
                {
                    continue;
                }

                if (IsInsideFolder(file, projectPath))
                {
                    continue;
                }

                if (IsInsideFolder(file, duplicateReviewPath))
                {
                    continue;
                }

                files.Add(file);
            }
        }

        // Safety limit while the project is still being tested.
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

        foreach (string file in files)
        {
            string fileName =
                Path.GetFileName(file);

            string extension =
                Path.GetExtension(file)
                    .ToLowerInvariant();

            Console.WriteLine();
            Console.WriteLine(
                $"Analyzing: {fileName}"
            );

            try
            {
                Classification classification;
                string method;

                if (
                    extension == ".pdf" ||
                    extension == ".docx" ||
                    extension == ".txt"
                )
                {
                    classification =
                        await ClassifyDocumentWithAI(
                            file,
                            fileName,
                            ollama
                        );

                    method =
                        "Local AI";
                }
                else
                {
                    classification =
                        ClassifyByFileContext(
                            file,
                            fileName,
                            extension
                        );

                    method =
                        "Smart File Context Rule";
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

    static void ShowPreview(
        List<MovePlan> movePlans)
    {
        Console.WriteLine();
        Console.WriteLine(
            "========== ORGANIZATION PREVIEW =========="
        );
        Console.WriteLine();

        foreach (MovePlan plan in movePlans)
        {
            Console.WriteLine(
                $"File: {Path.GetFileName(plan.Source)}"
            );

            Console.WriteLine();
            Console.WriteLine(
                "Current Location:"
            );

            Console.WriteLine(
                plan.Source
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
                "Suggested Destination:"
            );

            Console.WriteLine(
                plan.Destination
            );

            Console.WriteLine();
            Console.WriteLine(
                "----------------------------------------"
            );
            Console.WriteLine();
        }

        Console.WriteLine(
            $"Files ready to organize: {movePlans.Count}"
        );
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

        if (content.Length > 6000)
        {
            content =
                content[..6000];
        }

        string prompt =
            $"""
            You are organizing files on a personal computer.

            Analyze this document.

            Filename:
            {fileName}

            Document content:
            {content}

            Choose exactly one category
            and exactly one subcategory.

            Career:
            - Resumes
            - Job Descriptions
            - Interviews
            - Employment
            - Professional

            Education:
            - Assignments
            - Research
            - Academic Articles
            - Admissions
            - Certificates
            - Course Materials

            Finance:
            - Bank Statements
            - Bills
            - Receipts
            - Taxes
            - Other Finance

            Personal:
            - Personal Documents
            - Letters
            - Other Personal

            Other:
            - Uncategorized

            Return ONLY:

            Category|Subcategory

            Example:
            Education|Assignments

            Do not add explanations.
            """;

        StringBuilder aiResponse =
            new();

        await foreach (
            var response in
            ollama.GenerateAsync(prompt)
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

