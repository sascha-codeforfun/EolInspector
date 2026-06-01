EOL Inspector — a tiny WPF app to see LF vs CRLF across a folder
================================================================

WHAT IT DOES
------------
Pick a folder, click Scan, and it lists every (text) file with its line-ending
style: CRLF, LF, CR, Mixed, or None. It also shows the raw count of each
terminator so a "Mixed" file tells you how mixed it is. Binary files (anything
containing a NUL byte) are skipped automatically.

HOW TO OPEN IT IN VISUAL STUDIO 2022
------------------------------------
Option A — open the folder directly:
  1. File > Open > Project/Solution, then select EolInspector.csproj.
  2. Press F5 to build and run.

Option B — if you prefer a fresh project, create a new
  "WPF Application" (C#) targeting .NET 8, then replace the generated
  App.xaml, App.xaml.cs, MainWindow.xaml, MainWindow.xaml.cs, and the .csproj
  with the files here.

REQUIREMENTS
------------
- .NET 8 SDK (comes with current VS 2022). The project targets
  net8.0-windows.
- Microsoft.Win32.OpenFolderDialog is used for folder picking. This class
  exists in .NET 8+ WPF. 

IF YOU TARGET AN OLDER FRAMEWORK (.NET 6/7 or .NET Framework 4.x)
-----------------------------------------------------------------
OpenFolderDialog may not exist there. Two easy fixes:

  1. Add a reference to System.Windows.Forms and use its
     FolderBrowserDialog instead. Replace the body of PickButton_Click with:

         using var dialog = new System.Windows.Forms.FolderBrowserDialog();
         if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
         {
             _selectedFolder = dialog.SelectedPath;
             FolderBox.Text = _selectedFolder;
             ScanButton.IsEnabled = true;
             StatusText.Text = "Folder selected. Click Scan.";
         }

     (For .NET, also add <UseWindowsForms>true</UseWindowsForms> to the .csproj.)

  2. Or grab the Ookii.Dialogs.Wpf NuGet package, which has a nice
     VistaFolderBrowserDialog.

HOW DETECTION WORKS
-------------------
The file is read as raw bytes (not as text), so nothing re-normalizes the line
endings before we count them. We walk the bytes:
  - \r followed by \n  -> one CRLF
  - lone \n            -> one LF  (Unix)
  - lone \r            -> one CR  (classic Mac)
A file using exactly one of these is labeled accordingly; more than one ->
"Mixed"; none -> "None". This is exactly the distinction Visual Studio's
editor won't show you inline.
