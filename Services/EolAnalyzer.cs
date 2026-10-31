using System.IO;
using EolInspector.Models;

namespace EolInspector.Services
{
    /// <summary>
    /// Per-file detection: byte-order mark and line-ending counts, read from
    /// raw bytes so nothing normalizes the endings first.
    /// </summary>
    public static class EolAnalyzer
    {
        /// <summary>
        /// Reads the file as raw bytes and counts CRLF / lone-LF / lone-CR.
        /// Returns null if the file looks binary.
        /// </summary>
        public static FileEolResult? AnalyzeFile(string path, string root)
        {
            var bytes = File.ReadAllBytes(path);

            // Detect a leading byte-order mark before the binary check. In
            // practice only files with a UTF-8 BOM survive to be reported,
            // because UTF-16/UTF-32 files contain NUL bytes and get skipped
            // below, but all of them are recognised so the value is correct if
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
        public static string DetectBom(byte[] b)
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
}
