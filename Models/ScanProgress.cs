namespace EolInspector.Models
{
    /// <summary>
    /// Immutable progress snapshot handed from the scan thread to the UI.
    /// Total == 0 signals the indeterminate "enumerating" phase.
    /// </summary>
    public sealed record ScanProgress(int Done, int Total, int Shown, int Skipped);
}
