using System.Runtime.InteropServices;

namespace EolInspector.Interop
{
    /// <summary>
    /// Wraps SHOpenWithDialog, the shell API behind Windows' "How do you want
    /// to open this file?" dialog. Far more reliable from .NET than the
    /// "openas" ShellExecute verb, which often fails.
    /// </summary>
    internal static class ShellOpenWith
    {
        private const int S_OK = 0;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        /// <summary>
        /// Shows the Open With dialog for <paramref name="filePath"/>, owned by
        /// <paramref name="owner"/>. Returns true if the user picked an app or
        /// simply closed the dialog; false (with the HRESULT) on a real failure.
        /// </summary>
        public static bool TryShow(IntPtr owner, string filePath, out int hresult)
        {
            var info = new OpenAsInfo
            {
                FilePath = filePath,
                FileClass = null,
                // AllowRegistration keeps the "Always"/"Just once" choice;
                // Exec actually launches the app the user picks.
                Flags = OpenAsInfoFlags.AllowRegistration | OpenAsInfoFlags.Exec
            };

            hresult = SHOpenWithDialog(owner, ref info);
            return hresult == S_OK || hresult == ERROR_CANCELLED;
        }

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
    }
}
