using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TorWall.UI;

/// One thin yellow click-through edge of the on-screen frame.
internal sealed class EdgeForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x8;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // WS_EX_LAYERED / WS_EX_TRANSPARENT are deliberately NOT set here.
            // A layered window that has never had SetLayeredWindowAttributes
            // called on it is never painted, so we add those bits later from
            // BorderOverlayManager.SetClickThrough after the handle exists.
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public EdgeForm(Rectangle bounds)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Yellow;
        Bounds = bounds;
    }
}

/// Draws a yellow click-through frame around every monitor while blocking
/// is active. Listens for display-setting changes and rebuilds itself.
public sealed class BorderOverlayManager : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const uint LWA_ALPHA = 0x2;

    private readonly int _thickness;
    private readonly List<EdgeForm> _edges = new();
    private bool _visible;

    public BorderOverlayManager(int thicknessPx = 5)
    {
        _thickness = thicknessPx;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    public void Show()
    {
        _visible = true;
        Rebuild();
    }

    public void Hide()
    {
        _visible = false;
        foreach (var f in _edges) f.Close();
        _edges.Clear();
    }

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (_visible) Rebuild();
    }

    private void Rebuild()
    {
        foreach (var f in _edges) f.Close();
        _edges.Clear();

        // Cover the union of all monitors (SystemInformation.VirtualScreen)
        // by drawing four edges per monitor — each monitor gets its own
        // frame so users with multiple displays see a ring on every screen.
        foreach (var screen in Screen.AllScreens)
        {
            var b = screen.Bounds;
            _edges.Add(new EdgeForm(new Rectangle(b.X, b.Y, b.Width, _thickness)));                       // top
            _edges.Add(new EdgeForm(new Rectangle(b.X, b.Bottom - _thickness, b.Width, _thickness)));     // bottom
            _edges.Add(new EdgeForm(new Rectangle(b.X, b.Y, _thickness, b.Height)));                      // left
            _edges.Add(new EdgeForm(new Rectangle(b.Right - _thickness, b.Y, _thickness, b.Height)));     // right
        }

        foreach (var f in _edges)
        {
            f.Show();
            ApplyLayeredClickThrough(f.Handle);
        }
    }

    /// Promote the window to a layered window with full opacity and pass
    /// every hit-test through to whatever is underneath. Order matters:
    /// SetLayeredWindowAttributes must come AFTER WS_EX_LAYERED is set,
    /// and BEFORE the window paints, or the frame stays invisible.
    private static void ApplyLayeredClickThrough(IntPtr hwnd)
    {
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        Hide();
    }
}
