using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TorWall.UI;

/// One thin yellow click-through edge of the on-screen frame.
internal sealed class EdgeForm : Form
{
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x8;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW
                          | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
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
            SetClickThrough(f.Handle);
        }
    }

    /// Re-apply the extended-style bits after the handle exists. Some
    /// composition modes drop WS_EX_TRANSPARENT during creation, so we
    /// set it again explicitly via SetWindowLong.
    private static void SetClickThrough(IntPtr hwnd)
    {
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_LAYERED = 0x80000;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_LAYERED);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        Hide();
    }
}
