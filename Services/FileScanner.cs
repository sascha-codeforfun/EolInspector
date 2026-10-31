using System.IO;
using EolInspector.Models;

namespace EolInspector.Services
{
    /// <summary>
    /// Walks a folder and analyzes each matching file. Runs entirely on a
    /// background thread and touches no UI.
    /// </summary>
    public static class FileScanner
    {
        // Report at most every ReportEvery files (plus the final one) to keep
        // UI marshaling cheap.
        private const int ReportEvery = 64;

        public static ScanOutcome Run(
            string root,
            SearchOption option,
            HashSet<string> extensions,
            HashSet<string> excludedFolders,
            IProgress<ScanProgress> progress,
            CancellationToken token)
        {
            var outcome = new ScanOutcome();

            // Phase 1: enumerate matching paths up front so phase 2 has a known
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

            // Phase 2: analyze.
            for (int i = 0; i < total; i++)
            {
                if (token.IsCancellationRequested)
                {
                    outcome.Cancelled = true;
                    break;
                }

                try
                {
                    var result = EolAnalyzer.AnalyzeFile(paths[i], root);
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

        public static HashSet<string> ParseExtensions(string raw)
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
        public static HashSet<string> ParseExcludedFolders(string raw)
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
                    // whole subtree, so excluding "node_modules" also skips
                    // everything beneath it.
                    var name = Path.GetFileName(d);
                    if (excludedFolders.Count > 0 && excludedFolders.Contains(name))
                        continue;

                    stack.Push(d);
                }
            }
        }
    }
}
