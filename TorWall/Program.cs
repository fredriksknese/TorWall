using System.Security.Principal;

namespace TorWall;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!IsElevated())
        {
            MessageBox.Show(
                "TorWall must run as Administrator to modify the Windows Firewall.\n\n" +
                "Right-click the executable and choose \"Run as administrator\".",
                "TorWall", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm());
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
