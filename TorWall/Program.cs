using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace TorWall;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (!IsElevated())
        {
            if (TryRelaunchElevated(args)) return;
            MessageBox.Show(
                "TorWall must run as Administrator to modify the Windows Firewall, " +
                "and the elevation prompt was declined.",
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

    /// Re-spawn the current process with the "runas" verb so Windows raises
    /// the UAC prompt. Returns true if the elevated process was started
    /// (so the current non-elevated process should exit silently).
    private static bool TryRelaunchElevated(string[] args)
    {
        // Environment.ProcessPath points at the real .exe even when the host
        // was launched via `dotnet run`, which is what ShellExecute needs.
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return false;

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Environment.CurrentDirectory,
            Arguments = string.Join(' ', args.Select(QuoteArg)),
        };

        try
        {
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception)
        {
            // User clicked "No" on the UAC prompt (ERROR_CANCELLED 1223),
            // or no shell is available to elevate through.
            return false;
        }
    }

    private static string QuoteArg(string a) =>
        a.Contains(' ') || a.Contains('"') ? $"\"{a.Replace("\"", "\\\"")}\"" : a;
}
