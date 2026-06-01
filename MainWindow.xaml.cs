using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace EolInspector
{
    public partial class MainWindow : Window
    {
        // Defaults used to populate the textboxes and to power the Reset buttons.
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

            ExtBox.Text = DefaultExtensions;
            ExcludeBox.Text = DefaultExcludedFolders;

            // The exclusion controls only apply to recursive scans, so their
            // initial enabled state should match the checkbox.
            UpdateExclusionEnabled();
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

            bool on = RecurseCheck.IsChecked == true;
            ExcludeBox.IsEnabled = on;
            ExcludeLabel.IsEnabled = on;
            ResetExcludeButton.IsEnabled = on;
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
            // OpenFolderDialog ships with .NET 8 / WPF. If you target an older
            // framework, see the note in README.txt for the WinForms fallback.
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a folder to inspect"
            };

            if (dialog.ShowDialog() == true)
            {
                _selectedFolder = dialog.FolderName;
                FolderBox.Text = _selectedFolder;
                ScanButton.IsEnabled = true;
                StatusText.Text = "Folder selected. Click Scan.";
            }
        }

        // Double-clicking a row opens the Windows "Open with" picker for that
        // file, so you choose the app every time instead of launching whatever
        // is registered as the default. SHOpenWithDialog is the shell API behind
        // the "How do you want to open this file?" dialog — far more reliable
        // from .NET than the "openas" ShellExecute verb, which often fails here.
        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is not FileEolResult row)
                return;

            if (!File.Exists(row.FullPath))
            {
                StatusText.Text = "File no longer exists: " + row.RelativePath;
                return;
            }

            var info = new OpenAsInfo
            {
                FilePath = row.FullPath,
                FileClass = null,
                // AllowRegistration keeps the "Always"/"Just once" choice;
                // Exec actually launches the app the user picks.
                Flags = OpenAsInfoFlags.AllowRegistration | OpenAsInfoFlags.Exec
            };

            // Parent the dialog to this window so it's owned and centred.
            var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;

            try
            {
                int hr = SHOpenWithDialog(owner, ref info);

                // S_OK (0) = app chosen and launched.
                // ERROR_CANCELLED = user closed the dialog. Neither is an error.
                const int ERROR_CANCELLED = unchecked((int)0x800704C7);
                if (hr != 0 && hr != ERROR_CANCELLED)
                    StatusText.Text = $"Couldn't show the Open With dialog (0x{hr:X8}).";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Could not open: " + ex.Message;
            }
        }

        // --- Win32 interop for the shell "Open With" dialog -----------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenAsInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string FilePath;
            [MarshalAs(UnmanagedType.LPWStr)] public string? FileClass;
            [MarshalAs(UnmanagedType.I4)] public OpenAsInfoFlags Flags;
        }

        [Flags]
        private enum OpenAsInfoFlags
        {
            AllowRegistration = 0x00000001, // OAIF_ALLOW_REGISTRATION
            RegisterExt       = 0x00000002, // OAIF_REGISTER_EXT
            Exec              = 0x00000004, // OAIF_EXEC
            ForceRegistration = 0x00000008, // OAIF_FORCE_REGISTRATION
            HideRegistration  = 0x00000020, // OAIF_HIDE_REGISTRATION
            UrlProtocol       = 0x00000040, // OAIF_URL_PROTOCOL
            FileIsUri         = 0x00000080, // OAIF_FILE_IS_URI
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
        private static extern int SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo oainfo);

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            // While a scan is running this same button acts as Cancel.
            if (_cts != null)
            {
                _cts.Cancel();
                StatusText.Text = "Cancelling…";
                return;
            }

            if (string.IsNullOrEmpty(_selectedFolder) || !Directory.Exists(_selectedFolder))
            {
                MessageBox.Show("Please pick a valid folder first.");
                return;
            }

            // Snapshot every input on the UI thread. The background task must
            // not read any controls.
            string root = _selectedFolder;
            var extensions = ParseExtensions(ExtBox.Text);
            bool recurse = RecurseCheck.IsChecked == true;
            var searchOption = recurse
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            var excludedFolders = recurse
                ? ParseExcludedFolders(ExcludeBox.Text)
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
            ResultsGrid.ItemsSource = null;   // detach old rows; reassigned once at the end

            ScanOutcome outcome;
            try
            {
                outcome = await Task.Run(
                    () => RunScan(root, searchOption, extensions, excludedFolders, progress, token),
                    token);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
                ScanProgressBar.Value = 0;
                SetScanningUi(false);
                _cts.Dispose();
                _cts = null;
                return;
            }

            // Assign all rows in a single shot — the biggest win over adding
            // them one at a time during the scan.
            ResultsGrid.ItemsSource = outcome.Results;

            StatusText.Text = outcome.Cancelled
                ? $"Cancelled. {outcome.Results.Count} file(s) shown, {outcome.Scanned} scanned, {outcome.Skipped} skipped."
                : $"Done. {outcome.Results.Count} file(s) shown, {outcome.Scanned} scanned, {outcome.Skipped} skipped (binary/unreadable).";

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
            bool excl = !scanning && RecurseCheck.IsChecked == true;
            ExcludeBox.IsEnabled = excl;
            ExcludeLabel.IsEnabled = excl;
            ResetExcludeButton.IsEnabled = excl;
        }

        // Runs entirely on a background thread: walks the tree, analyzes each
        // matching file, and reports throttled progress. Touches no UI.
        private static ScanOutcome RunScan(
            string root,
            SearchOption option,
            HashSet<string> extensions,
            HashSet<string> excludedFolders,
            IProgress<ScanProgress> progress,
            CancellationToken token)
        {
            var outcome = new ScanOutcome();

            // Phase 1 — enumerate matching paths up front so phase 2 has a known
            // total and the progress bar can be determinate.
            progress.Report(new ScanProgress(0, 0, 0, 0)); // Total 0 => "Enumerating…"

            var paths = new List<string>();
            foreach (var path in EnumerateFilesSafe(root, option, excludedFolders))
            {
                if (token.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    return outcome;
                }

                if (extensions.Count > 0)
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (!extensions.Contains(ext))
                        continue;
                }
                paths.Add(path);
            }

            int total = paths.Count;

            // Phase 2 — analyze. Report at most every ReportEvery files (plus the
            // final one) to keep UI marshaling cheap.
            const int ReportEvery = 64;
            for (int i = 0; i < total; i++)
            {
                if (token.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    break;
                }

                try
                {
                    var result = AnalyzeFile(paths[i], root);
                    if (result != null)
                        outcome.Results.Add(result);
                    else
                        outcome.Skipped++;
                    outcome.Scanned++;
                }
                catch
                {
                    outcome.Skipped++;
                }

                if (i % ReportEvery == 0 || i == total - 1)
                    progress.Report(new ScanProgress(i + 1, total, outcome.Results.Count, outcome.Skipped));
            }

            return outcome;
        }

        // Immutable progress snapshot handed from the scan thread to the UI.
        // Total == 0 signals the indeterminate "enumerating" phase.
        private sealed record ScanProgress(int Done, int Total, int Shown, int Skipped);

        // Everything a scan produces, returned once when it finishes (or is
        // cancelled — partial results are kept).
        private sealed class ScanOutcome
        {
            public List<FileEolResult> Results { get; } = new();
            public int Scanned { get; set; }
            public int Skipped { get; set; }
            public bool Cancelled { get; set; }
        }

        private static HashSet<string> ParseExtensions(string raw)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var ext = part.StartsWith('.') ? part : "." + part;
                set.Add(ext.ToLowerInvariant());
            }
            return set;
        }

        // Folder names to skip during recursion, matched case-insensitively
        // against each directory's own name (not its full path).
        private static HashSet<string> ParseExcludedFolders(string raw)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                set.Add(part);
            }
            return set;
        }

        // Enumerates files, swallowing access-denied on individual subfolders.
        // 'excludedFolders' is matched against each subdirectory's name during
        // recursion; the root itself is always scanned.
        private static IEnumerable<string> EnumerateFilesSafe(
            string root, SearchOption option, HashSet<string> excludedFolders)
        {
            if (option == SearchOption.TopDirectoryOnly)
            {
                string[] files;
                try { files = Directory.GetFiles(root); }
                catch { yield break; }
                foreach (var f in files) yield return f;
                yield break;
            }

            // Manual recursion so one locked folder doesn't abort the whole walk.
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();

                string[] files = Array.Empty<string>();
                try { files = Directory.GetFiles(dir); } catch { }
                foreach (var f in files) yield return f;

                string[] subdirs = Array.Empty<string>();
                try { subdirs = Directory.GetDirectories(dir); } catch { }
                foreach (var d in subdirs)
                {
                    // Skip excluded folders by their own name. This prunes the
                    // whole subtree, so e.g. excluding "node_modules" also skips
                    // everything beneath it.
                    var name = Path.GetFileName(d);
                    if (excludedFolders.Count > 0 && excludedFolders.Contains(name))
                        continue;

                    stack.Push(d);
                }
            }
        }

        /// <summary>
        /// Reads the file as raw bytes and counts CRLF / lone-LF / lone-CR.
        /// Returns null if the file looks binary.
        /// </summary>
        private static FileEolResult? AnalyzeFile(string path, string root)
        {
            var bytes = File.ReadAllBytes(path);

            // Detect a leading byte-order mark before the binary check. In
            // practice only files with a UTF-8 BOM survive to be reported,
            // because UTF-16/UTF-32 files contain NUL bytes and get skipped
            // below — but we recognise all of them so the value is correct if
            // the binary rule is ever relaxed.
            string bom = DetectBom(bytes);

            // Up-front binary check: a NUL byte anywhere is a strong signal the
            // file is binary. Doing this first means a NUL that appears *after*
            // some line endings still causes the file to be skipped, rather than
            // getting mislabeled from the endings counted before it.
            foreach (var bb in bytes)
            {
                if (bb == 0)
                    return null;
            }

            int crlf = 0, lf = 0, cr = 0;

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];

                if (b == (byte)'\r')
                {
                    if (i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n')
                    {
                        crlf++;
                        i++; // consume the \n too
                    }
                    else
                    {
                        cr++; // lone CR (old Mac style)
                    }
                }
                else if (b == (byte)'\n')
                {
                    lf++; // lone LF (Unix style)
                }
            }

            string eol;
            int styles = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);

            if (styles == 0)
                eol = "None";        // single line / no terminators
            else if (styles > 1)
                eol = "Mixed";
            else if (crlf > 0)
                eol = "CRLF";
            else if (lf > 0)
                eol = "LF";
            else
                eol = "CR";

            return new FileEolResult
            {
                RelativePath = Path.GetRelativePath(root, path),
                FullPath = path,
                Eol = eol,
                Bom = bom,
                CrlfCount = crlf,
                LfCount = lf,
                CrCount = cr,
                SizeBytes = bytes.Length
            };
        }

        /// <summary>
        /// Returns the byte-order mark at the very start of the file, or "None".
        /// Longer marks are checked first because shorter ones are prefixes of
        /// them (UTF-16 LE "FF FE" is the start of UTF-32 LE "FF FE 00 00").
        /// </summary>
        private static string DetectBom(byte[] b)
        {
            if (b.Length >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0xFE && b[3] == 0xFF)
                return "UTF-32 BE";
            if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0x00 && b[3] == 0x00)
                return "UTF-32 LE";
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
                return "UTF-8";
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
                return "UTF-16 BE";
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
                return "UTF-16 LE";
            return "None";
        }
    }

    public class FileEolResult
    {
        public string RelativePath { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string Eol { get; set; } = "";
        public string Bom { get; set; } = "None";
        public int CrlfCount { get; set; }
        public int LfCount { get; set; }
        public int CrCount { get; set; }
        public long SizeBytes { get; set; }

        // Always shown in bytes with the current culture's thousands separator
        // (e.g. "1.000.000 bytes" on a de-DE machine). The column sorts on the
        // raw SizeBytes value via SortMemberPath in the XAML, so ordering stays
        // numeric regardless of how this string is formatted.
        public string SizeDisplay =>
            SizeBytes == 1 ? "1 byte" : $"{SizeBytes:N0} bytes";
    }
}
