using System.Windows;
using HueThemeKit;

namespace EolInspector
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Hands the colours back to Windows while a contrast theme is on.
            HueContrast.Follow(this);
        }
    }
}
