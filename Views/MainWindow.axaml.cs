using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AIFileOrganizer.Desktop.Views;

public partial class MainWindow : Window
{
    private List<MovePlan> _movePlans = new();

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

        int scanLimit = ScanLimitBox.SelectedIndex switch
        {
            0 => 20,
            2 => 100,
            _ => 50
        };

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

            ResultsList.ItemsSource = plans.Select(plan => new CheckBox
            {
                Content = $"{Path.GetFileName(plan.Source)}  →  {plan.Category} / {plan.Subcategory}",
                IsChecked = true,
                Tag = plan
            }).ToList();

            ScanSummaryText.Text = plans.Count == 0
                ? "No supported files were found in the selected folders."
                : $"Found {plans.Count} files. Review the suggestions below; no file has been moved.";
            MoveConfirmationCheckBox.IsEnabled = plans.Count > 0;
            OrganizeButton.IsEnabled = plans.Count > 0;
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

    private async void Organize_Click(object? sender, RoutedEventArgs e)
    {
        if (MoveConfirmationCheckBox.IsChecked != true)
        {
            ActivityText.Text = "Tick the confirmation box after reviewing the selected files.";
            return;
        }

        List<MovePlan> approvedPlans = ResultsList.ItemsSource
            ?.OfType<CheckBox>()
            .Where(item => item.IsChecked == true)
            .Select(item => (MovePlan)item.Tag!)
            .ToList()
            ?? new List<MovePlan>();

        if (approvedPlans.Count == 0)
        {
            ActivityText.Text = "Select at least one file to organize.";
            return;
        }

        OrganizeButton.IsEnabled = false;
        ScanButton.IsEnabled = false;
        ActivityText.Text = $"Organizing {approvedPlans.Count} reviewed file(s).";

        OrganizationResult result = await Task.Run(
            () => Program.OrganizeFromDesktop(approvedPlans));

        ScanSummaryText.Text = $"Organization complete: {result.Moved} moved, {result.Renamed} renamed, {result.Duplicates} duplicate(s) sent to review, {result.Errors} issue(s).";
        ActivityText.Text = "Every completed move is stored in local history and can be undone from the console app.";
        MoveConfirmationCheckBox.IsEnabled = false;
        ResultsList.ItemsSource = null;
        _movePlans.Clear();
        ScanButton.IsEnabled = true;
    }
}
