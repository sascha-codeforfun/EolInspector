namespace EolInspector.Models
{
    /// <summary>
    /// Everything a scan produces, returned once when it finishes (or is
    /// cancelled; partial results are kept).
    /// </summary>
    public sealed class ScanOutcome
    {
        public List<FileEolResult> Results { get; } = new();
        public int Scanned { get; set; }
        public int Skipped { get; set; }
        public bool Cancelled { get; set; }
    }
}
