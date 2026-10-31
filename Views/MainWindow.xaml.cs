using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EolInspector.Interop;
using EolInspector.Models;
using EolInspector.Services;
using HueThemeKit;

namespace EolInspector.Views
{
    public partial class MainWindow : Window
    {
        // Defaults used to populate the textboxes and to power the Reset buttons.
        // The app is stateless: every start begins from these.
        private const string DefaultExtensions =
            ".cs,.xaml,.txt,.json,.js,.ts,.html,.css,.xml,.config,.md,.sh";
        private const string DefaultExcludedFolders =
            "bin,obj,.git,.vs,node_modules,packages";

        private string? _selectedFolder;

        // Non-null only while a scan is running. Doubles as the "is scanning"
        // flag and the handle the Cancel button uses.
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();

            // Dark title bar, following the theme; hooked before the window has a handle.
            HueTitleBar.Attach(this);

            VersionText.Text = HueVersion.Text;
            SdkText.Text = HueVersion.Sdk is { } sdk ? "built with SDK " + sdk : "";
            SdkText.Visibility = HueVersion.Sdk is null ? Visibility.Collapsed : Visibility.Visible;
            RuntimeText.Text = "runtime " + HueVersion.Runtime;

            ExtBox.Text = DefaultExtensions;
            ExcludeBox.Text = DefaultExcludedFolders;

            // The exclusion controls only apply to recursive scans, so their
            // initial enabled state should match the checkbox.
            UpdateExclusionEnabled();

            SetStatus("Pick a folder to start.", "Brush.Stale");
        }

        /// <summary>One line in the status bar, with the dot in a theme colour.</summary>
        private void SetStatus(string text, string brushKey)
        {
            StatusText.Text = text;
            StatusDot.SetResourceReference(ForegroundProperty, brushKey);
        }

        private void RecurseCheck_Changed(object sender, RoutedEventArgs e)
        {
            UpdateExclusionEnabled();
        }

        // Enable the exclude-folder controls only when 'Include subfolders' is ticked.
        private void UpdateExclusionEnabled()
        {
            // Guard: this can fire during XAML init before all controls exist.
            if (ExcludeBox == null) return;

            SetExclusionEnabled(RecurseCheck.IsChecked == true);
        }

        private void SetExclusionEnabled(bool on)
        {
            ExcludeBox.IsEnabled = on;
            ResetExcludeButton.IsEnabled = on;

            // A TextBlock has no disabled look of its own, so the label dims with its box.
            ExcludeLabel.IsEnabled = on;
            ExcludeLabel.Opacity = on ? 1.0 : 0.45;
        }

        private void ResetExtButton_Click(object sender, RoutedEventArgs e)
        {
            ExtBox.Text = DefaultExtensions;
        }

        private void ResetExcludeButton_Click(object sender, RoutedEventArgs e)
        {
            ExcludeBox.Text = DefaultExcludedFolders;
        }

        private void PickButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a folder to inspect"
            };

            if (dialog.ShowDialog(this) == true)
            {
                _selectedFolder = dialog.FolderName;
                FolderBox.Text = _selectedFolder;
                ScanButton.IsEnabled = true;
                SetStatus("Folder selected. Click Scan.", "Brush.Accent");
            }
        }

        // Double-clicking a row opens the Windows "Open with" picker for that
        // file, so you choose the app every time instead of launching whatever
        // is registered as the default.
        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not FileEolResult row)
                return;

            if (!File.Exists(row.FullPath))
            {
                SetStatus("File no longer exists: " + row.RelativePath, "Brush.Warn");
                return;
            }

            var owner = new WindowInteropHelper(this).Handle;

            try
            {
                if (!ShellOpenWith.TryShow(owner, row.FullPath, out int hr))
                    SetStatus($"Couldn't show the Open With dialog (0x{hr:X8}).", "Brush.Warn");
            }
            catch (Exception ex)
            {
                SetStatus("Could not open: " + ex.Message, "Brush.Error");
            }
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            // While a scan is running this same button acts as Cancel.
            if (_cts != null)
            {
                _cts.Cancel();
                SetStatus("Cancelling…", "Brush.Warn");
                return;
            }

            if (string.IsNullOrEmpty(_selectedFolder) || !Directory.Exists(_selectedFolder))
            {
                HueMessage.Show(this, "EOL Inspector", "Please pick a valid folder first.");
                return;
            }

            // Snapshot every input on the UI thread. The background task must
            // not read any controls.
            string root = _selectedFolder;
            var extensions = FileScanner.ParseExtensions(ExtBox.Text);
            bool recurse = RecurseCheck.IsChecked == true;
            var searchOption = recurse
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            var excludedFolders = recurse
                ? FileScanner.ParseExcludedFolders(ExcludeBox.Text)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Progress<T> captures this (UI) thread's context, so the callback
            // runs on the UI thread. The scan loop throttles how often it
            // reports, so this fires tens of times, not thousands.
            var progress = new Progress<ScanProgress>(p =>
            {
                if (p.Total > 0)
                {
                    ScanProgressBar.Maximum = p.Total;
                    ScanProgressBar.Value = p.Done;
                    StatusText.Text =
                        $"Scanning… {p.Done}/{p.Total}  ({p.Shown} shown, {p.Skipped} skipped)";
                }
                else
                {
                    ScanProgressBar.Value = 0;
                    StatusText.Text = "Enumerating files…";
                }
            });

            SetScanningUi(true);
            SetStatus("Enumerating files…", "Brush.Accent");
            ResultsGrid.ItemsSource = null;   // detach old rows; reassigned once at the end

            ScanOutcome outcome;
            try
            {
                outcome = await Task.Run(
                    () => FileScanner.Run(root, searchOption, extensions, excludedFolders, progress, token),
                    token);
            }
            catch (Exception ex)
            {
                SetStatus("Error: " + ex.Message, "Brush.Error");
                ScanProgressBar.Value = 0;
                SetScanningUi(false);
                _cts.Dispose();
                _cts = null;
                return;
            }

            // Assign all rows in a single shot rather than adding them one at a
            // time during the scan.
            ResultsGrid.ItemsSource = outcome.Results;

            if (outcome.Cancelled)
                SetStatus($"Cancelled. {outcome.Results.Count} file(s) shown, {outcome.Scanned} scanned, {outcome.Skipped} skipped.",
                          "Brush.Warn");
            else
                SetStatus($"Done. {outcome.Results.Count} file(s) shown, {outcome.Scanned} scanned, {outcome.Skipped} skipped (binary/unreadable).",
                          "Brush.Ok");

            SetScanningUi(false);
            _cts.Dispose();
            _cts = null;
        }

        // Toggles the window between idle and scanning states. The Scan button
        // stays enabled throughout because it becomes the Cancel button.
        private void SetScanningUi(bool scanning)
        {
            ScanButton.Content = scanning ? "Cancel" : "Scan";
            ScanProgressBar.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;

            PickButton.IsEnabled = !scanning;
            ResetExtButton.IsEnabled = !scanning;
            ExtBox.IsEnabled = !scanning;
            RecurseCheck.IsEnabled = !scanning;

            // Exclusion controls additionally depend on the recurse checkbox.
            SetExclusionEnabled(!scanning && RecurseCheck.IsChecked == true);
        }
    }
}
