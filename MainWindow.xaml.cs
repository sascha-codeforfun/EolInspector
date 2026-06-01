using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace EolInspector
{
    public partial class MainWindow : Window
    {
        // Defaults used to populate the textboxes and to power the Reset buttons.
        private const string DefaultExtensions =
            ".cs,.xaml,.txt,.json,.js,.ts,.html,.css,.xml,.config,.md,.sh";
        private const string DefaultExcludedFolders =
            "bin,obj,.git,.vs,node_modules,packages";

        private readonly ObservableCollection<FileEolResult> _results = new();
        private string? _selectedFolder;

        public MainWindow()
        {
            InitializeComponent();
            ResultsGrid.ItemsSource = _results;

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

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFolder) || !Directory.Exists(_selectedFolder))
            {
                MessageBox.Show("Please pick a valid folder first.");
                return;
            }

            _results.Clear();
            StatusText.Text = "Scanning…";

            var extensions = ParseExtensions(ExtBox.Text);
            bool recurse = RecurseCheck.IsChecked == true;
            var searchOption = recurse
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            // Folder exclusions only apply to recursive scans; an empty set
            // means "exclude nothing".
            var excludedFolders = recurse
                ? ParseExcludedFolders(ExcludeBox.Text)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int scanned = 0, skipped = 0;

            try
            {
                foreach (var path in EnumerateFilesSafe(_selectedFolder, searchOption, excludedFolders))
                {
                    if (extensions.Count > 0)
                    {
                        var ext = Path.GetExtension(path).ToLowerInvariant();
                        if (!extensions.Contains(ext))
                            continue;
                    }

                    try
                    {
                        var result = AnalyzeFile(path, _selectedFolder);
                        if (result != null)
                            _results.Add(result);
                        else
                            skipped++;
                        scanned++;
                    }
                    catch
                    {
                        skipped++;
                    }
                }

                StatusText.Text = $"Done. {_results.Count} file(s) shown, {scanned} scanned, {skipped} skipped (binary/unreadable).";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error: " + ex.Message;
            }
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
                Eol = eol,
                CrlfCount = crlf,
                LfCount = lf,
                CrCount = cr,
                SizeBytes = bytes.Length
            };
        }
    }

    public class FileEolResult
    {
        public string RelativePath { get; set; } = "";
        public string Eol { get; set; } = "";
        public int CrlfCount { get; set; }
        public int LfCount { get; set; }
        public int CrCount { get; set; }
        public long SizeBytes { get; set; }

        public string SizeDisplay =>
            SizeBytes < 1024 ? $"{SizeBytes} B"
            : SizeBytes < 1024 * 1024 ? $"{SizeBytes / 1024.0:0.#} KB"
            : $"{SizeBytes / (1024.0 * 1024.0):0.#} MB";
    }
}
