namespace EolInspector.Models
{
    /// <summary>One row in the results grid.</summary>
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
