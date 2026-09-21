using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AIFileOrganizer.Desktop.Views;

public partial class MainWindow : Window
{
    private List<MovePlan> _movePlans = new();
    private readonly List<CheckBox> _reviewItems = new();
    private FileSystemWatcher? _downloadsWatcher;
    private string? _selectedDestinationFolder;

    public MainWindow()
    {
        InitializeComponent();
        _ = CheckOllamaAsync();
    }

    private async Task CheckOllamaAsync()
    {
        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(3) };
            using HttpResponseMessage response = await client.GetAsync("http://localhost:11434/api/tags");
            OllamaStatusText.Text = response.IsSuccessStatusCode ? "Ollama is ready" : "Ollama needs attention";
        }
        catch
        {
            OllamaStatusText.Text = "Open Ollama to classify files";
        }
    }

    private async void StartScan_Click(object? sender, RoutedEventArgs e)
    {
        int selectedFolders = new[]
        {
            DownloadsCheckBox.IsChecked == true,
            DesktopCheckBox.IsChecked == true,
            DocumentsCheckBox.IsChecked == true
        }.Count(selected => selected);

        if (selectedFolders == 0)
        {
            ScanSummaryText.Text = "Choose at least one folder first.";
            return;
        }

        if (!int.TryParse(ScanLimitTextBox.Text, out int scanLimit) || scanLimit < 1 || scanLimit > 1000)
        {
            ScanSummaryText.Text = "Enter a whole number from 1 to 1000.";
            return;
        }

        string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> folders = new();

        if (DownloadsCheckBox.IsChecked == true)
            folders.Add(Path.Combine(homePath, "Downloads"));
        if (DesktopCheckBox.IsChecked == true)
            folders.Add(Path.Combine(homePath, "Desktop"));
        if (DocumentsCheckBox.IsChecked == true)
            folders.Add(Path.Combine(homePath, "Documents"));

        ScanButton.IsEnabled = false;
        ResultsList.ItemsSource = null;
        ScanSummaryText.Text = $"Scanning up to {scanLimit} files. This may take a moment for documents that need local AI classification.";
        ActivityText.Text = "Scanning safely. No files are being moved.";

        try
        {
            List<MovePlan> plans = await Task.Run(
                () => Program.ScanFromDesktopAsync(folders.ToArray(), scanLimit));

            _movePlans = plans;
            _reviewItems.Clear();
            _reviewItems.AddRange(plans.Select(CreateReviewItem));
            ResultsList.ItemsSource = _reviewItems;

            ScanSummaryText.Text = plans.Count == 0
                ? "No supported files were found in the selected folders."
                : $"Found {plans.Count} files. Review the suggestions below; no file has been moved.";
            MoveConfirmationCheckBox.IsEnabled = plans.Count > 0;
            OrganizeButton.IsEnabled = plans.Count > 0;
            CustomFolderButton.IsEnabled = plans.Count > 0;
            SelectedFolderButton.IsEnabled = plans.Count > 0;
            DuplicateButton.IsEnabled = plans.Count > 1;
            DeleteButton.IsEnabled = plans.Count > 0;
            DeleteConfirmationCheckBox.IsEnabled = plans.Count > 0;
            ActivityText.Text = "Scan complete. Untick any file you do not want to organize, then confirm the remaining selection.";
        }
        catch (Exception ex)
        {
            ScanSummaryText.Text = "The scan could not finish.";
            ActivityText.Text = ex.Message;
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private CheckBox CreateReviewItem(MovePlan plan)
    {
        StackPanel details = new() { Spacing = 2 };
        details.Children.Add(new TextBlock
        {
            Text = $"{Path.GetFileName(plan.Source)}  →  {plan.Category} / {plan.Subcategory}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        details.Children.Add(new TextBlock
        {
            Text = $"Destination: {plan.Destination}",
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        return new CheckBox
        {
            Content = details,
            IsChecked = true,
            Tag = plan,
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };
    }

    private void ToggleWatcher_Click(object? sender, RoutedEventArgs e)
    {
        if (_downloadsWatcher is not null)
        {
            _downloadsWatcher.Dispose();
            _downloadsWatcher = null;
            WatcherButton.Content = "Start Downloads watcher";
            ActivityText.Text = "Downloads watcher stopped. No files have been moved.";
            return;
        }

        string downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        _downloadsWatcher = new FileSystemWatcher(downloadsPath)
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };
        _downloadsWatcher.Created += DownloadDetected;
        _downloadsWatcher.Renamed += DownloadRenamed;
        WatcherButton.Content = "Stop Downloads watcher";
        ActivityText.Text = "Downloads watcher is active. New supported files will be added for review; nothing moves automatically.";
    }

    private void DownloadRenamed(object sender, RenamedEventArgs e) => _ = ReviewNewDownloadAsync(e.FullPath);

    private void DownloadDetected(object sender, FileSystemEventArgs e) => _ = ReviewNewDownloadAsync(e.FullPath);

    private async Task ReviewNewDownloadAsync(string filePath)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));

        if (!File.Exists(filePath))
            return;

        try
        {
            List<MovePlan> plans = await Task.Run(
                () => Program.ClassifyDownloadedFileAsync(filePath));

            if (plans.Count == 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (MovePlan plan in plans)
                {
                    if (_movePlans.Any(item => string.Equals(item.Source, plan.Source, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _movePlans.Add(plan);
                    _reviewItems.Add(CreateReviewItem(plan));
                }

                ResultsList.ItemsSource = null;
                ResultsList.ItemsSource = _reviewItems;
                MoveConfirmationCheckBox.IsEnabled = _movePlans.Count > 0;
                OrganizeButton.IsEnabled = _movePlans.Count > 0;
                CustomFolderButton.IsEnabled = _movePlans.Count > 0;
                SelectedFolderButton.IsEnabled = _movePlans.Count > 0;
                DuplicateButton.IsEnabled = _movePlans.Count > 1;
                DeleteButton.IsEnabled = _movePlans.Count > 0;
                DeleteConfirmationCheckBox.IsEnabled = _movePlans.Count > 0;
                ScanSummaryText.Text = $"New download ready for review: {Path.GetFileName(filePath)}.";
                ActivityText.Text = "The Downloads watcher added a suggestion. No file has been moved.";
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                ActivityText.Text = $"Could not classify new download: {Path.GetFileName(filePath)}.");
        }
    }

    private async void Organize_Click(object? sender, RoutedEventArgs e)
    {
        if (MoveConfirmationCheckBox.IsChecked != true)
        {
            ActivityText.Text = "Tick the confirmation box after reviewing the selected files.";
            return;
        }

        List<MovePlan> approvedPlans = GetApprovedPlans();

        if (approvedPlans.Count == 0)
        {
            ActivityText.Text = "Select at least one file to organize.";
            return;
        }

        OrganizeButton.IsEnabled = false;
        CustomFolderButton.IsEnabled = false;
        ScanButton.IsEnabled = false;
        ActivityText.Text = $"Organizing {approvedPlans.Count} reviewed file(s).";

        OrganizationResult result = await Task.Run(
            () => Program.OrganizeFromDesktop(approvedPlans));

        ScanSummaryText.Text = $"Organization complete: {result.Moved} moved, {result.Renamed} renamed, {result.Duplicates} duplicate(s) sent to review, {result.Errors} issue(s).";
        ActivityText.Text = "Every completed move is stored in local history and can be undone from the console app.";
        MoveConfirmationCheckBox.IsEnabled = false;
        ResultsList.ItemsSource = null;
        _reviewItems.Clear();
        _movePlans.Clear();
        _reviewItems.Clear();
        ScanButton.IsEnabled = true;
    }

    private async void MoveToCustomFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (MoveConfirmationCheckBox.IsChecked != true)
        {
            ActivityText.Text = "Tick the confirmation box after reviewing the selected files.";
            return;
        }

        string folderName = CustomFolderNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(folderName))
        {
            ActivityText.Text = "Enter the new folder name first.";
            return;
        }

        List<MovePlan> approvedPlans = GetApprovedPlans();
        if (approvedPlans.Count == 0)
        {
            ActivityText.Text = "Select at least one file to move.";
            return;
        }

        OrganizeButton.IsEnabled = false;
        CustomFolderButton.IsEnabled = false;
        ScanButton.IsEnabled = false;
        ActivityText.Text = $"Creating Documents/{folderName} and moving {approvedPlans.Count} reviewed file(s).";

        try
        {
            OrganizationResult result = await Task.Run(
                () => Program.MoveToNewDocumentsFolder(approvedPlans, folderName));
            ScanSummaryText.Text = $"Custom-folder move complete: {result.Moved} moved, {result.Renamed} renamed, {result.Duplicates} duplicate(s), {result.Errors} issue(s).";
            ActivityText.Text = $"Files are in Documents/{folderName}. Every completed move can be undone.";
            ClearReviewAfterMove();
        }
        catch (ArgumentException ex)
        {
            ActivityText.Text = ex.Message;
            OrganizeButton.IsEnabled = true;
            CustomFolderButton.IsEnabled = true;
            ScanButton.IsEnabled = true;
        }
    }

    private async void ChooseFolder_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = "Choose where selected files should go" });
        string? path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        _selectedDestinationFolder = path;
        ChooseFolderButton.Content = $"Chosen: {path}";
        ActivityText.Text = "Destination selected. Scan and review files before moving them.";
    }

    private async void MoveToSelectedFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (MoveConfirmationCheckBox.IsChecked != true)
        {
            ActivityText.Text = "Tick the confirmation box after reviewing the selected files.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_selectedDestinationFolder))
        {
            ActivityText.Text = "Choose an existing destination folder first.";
            return;
        }

        List<MovePlan> approvedPlans = GetApprovedPlans();
        if (approvedPlans.Count == 0)
        {
            ActivityText.Text = "Select at least one file to move.";
            return;
        }

        OrganizeButton.IsEnabled = false;
        CustomFolderButton.IsEnabled = false;
        SelectedFolderButton.IsEnabled = false;
        ScanButton.IsEnabled = false;
        ActivityText.Text = $"Moving {approvedPlans.Count} reviewed file(s) to {_selectedDestinationFolder}.";

        OrganizationResult result = await Task.Run(
            () => Program.MoveToExistingFolder(approvedPlans, _selectedDestinationFolder));
        ScanSummaryText.Text = $"Move complete: {result.Moved} moved, {result.Renamed} renamed, {result.Duplicates} duplicate(s), {result.Errors} issue(s).";
        ActivityText.Text = "The move is saved in local history and can be undone.";
        ClearReviewAfterMove();
    }

    private List<MovePlan> GetApprovedPlans() => ResultsList.ItemsSource
        ?.OfType<CheckBox>()
        .Where(item => item.IsChecked == true)
        .Select(item => (MovePlan)item.Tag!)
        .ToList()
        ?? new List<MovePlan>();

    private async void FindDuplicates_Click(object? sender, RoutedEventArgs e)
    {
        DuplicateButton.IsEnabled = false;
        ActivityText.Text = "Checking selected files for exact duplicates.";
        List<MovePlan> selectedPlans = GetApprovedPlans();
        List<List<MovePlan>> duplicates = await Task.Run(
            () => Program.FindExactDuplicates(selectedPlans));
        ScanSummaryText.Text = duplicates.Count == 0
            ? "No exact duplicate files were found in the selected scan results."
            : $"Found {duplicates.Count} duplicate group(s): " + string.Join("; ", duplicates.Select(group => string.Join(", ", group.Select(plan => Path.GetFileName(plan.Source)))));
        ActivityText.Text = "Duplicate check complete. No files were changed.";
        DuplicateButton.IsEnabled = _movePlans.Count > 1;
    }

    private async void DeleteSelected_Click(object? sender, RoutedEventArgs e)
    {
        if (DeleteConfirmationCheckBox.IsChecked != true)
        {
            ActivityText.Text = "Tick the permanent deletion confirmation before deleting files.";
            return;
        }
        List<MovePlan> selectedPlans = GetApprovedPlans();
        if (selectedPlans.Count == 0)
        {
            ActivityText.Text = "Select at least one file to delete.";
            return;
        }
        DeleteButton.IsEnabled = false;
        ActivityText.Text = $"Permanently deleting {selectedPlans.Count} selected file(s).";
        int deleted = await Task.Run(() => Program.DeleteSelectedFiles(selectedPlans));
        _reviewItems.RemoveAll(item => item.IsChecked == true);
        _movePlans.RemoveAll(plan => selectedPlans.Contains(plan));
        ResultsList.ItemsSource = null;
        ResultsList.ItemsSource = _reviewItems;
        ScanSummaryText.Text = $"Deleted {deleted} file(s).";
        ActivityText.Text = "Deletion complete.";
        DeleteConfirmationCheckBox.IsChecked = false;
        DeleteButton.IsEnabled = _movePlans.Count > 0;
    }

    private void ClearReviewAfterMove()
    {
        MoveConfirmationCheckBox.IsEnabled = false;
        ResultsList.ItemsSource = null;
        _movePlans.Clear();
        _reviewItems.Clear();
        ScanButton.IsEnabled = true;
    }

    private async void Undo_Click(object? sender, RoutedEventArgs e)
    {
        UndoButton.IsEnabled = false;
        ScanButton.IsEnabled = false;
        ActivityText.Text = "Restoring the most recent organization batch.";

        UndoResult result = await Task.Run(Program.UndoLastOrganizationFromDesktop);

        if (!result.FoundOrganization)
        {
            ActivityText.Text = "There is no organization batch to undo.";
        }
        else
        {
            ScanSummaryText.Text = $"Undo complete: {result.Restored} file(s) restored and {result.Skipped} skipped.";
            ActivityText.Text = "Files restored to their original locations. No files were deleted.";
        }

        UndoButton.IsEnabled = true;
        ScanButton.IsEnabled = true;
    }
}
