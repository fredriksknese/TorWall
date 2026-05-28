using TorWall.Services;
using TorWall.UI;

namespace TorWall;

public sealed class MainForm : Form
{
    private readonly TorRelayFetcher _fetcher = new();
    private readonly FirewallManager _firewall = new();
    private readonly BorderOverlayManager _overlay = new();

    private readonly Button _btnToggle = new();
    private readonly Button _btnUpdate = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblRelays = new();
    private readonly TextBox _log = new();
    private readonly NotifyIcon _tray;

    private List<string> _relayIps = new();
    private DateTime? _lastFetch;

    public MainForm()
    {
        Text = "TorWall";
        Size = new Size(520, 380);
        MinimumSize = new Size(420, 320);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;

        _lblStatus.Text = "Status: idle";
        _lblStatus.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _lblStatus.AutoSize = true;
        _lblStatus.Location = new Point(12, 12);

        _lblRelays.Text = "Relay list: not loaded";
        _lblRelays.AutoSize = true;
        _lblRelays.Location = new Point(12, 38);

        _btnToggle.Text = "Start blocking";
        _btnToggle.Size = new Size(160, 36);
        _btnToggle.Location = new Point(12, 70);
        _btnToggle.Click += OnToggleClicked;

        _btnUpdate.Text = "Update relay list";
        _btnUpdate.Size = new Size(160, 36);
        _btnUpdate.Location = new Point(180, 70);
        _btnUpdate.Click += OnUpdateClicked;

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Location = new Point(12, 118);
        _log.Size = new Size(ClientSize.Width - 24, ClientSize.Height - 130);
        _log.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _log.Font = new Font("Consolas", 9F);

        Controls.AddRange(new Control[] { _lblStatus, _lblRelays, _btnToggle, _btnUpdate, _log });

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "TorWall",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu(),
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        FormClosing += OnFormClosing;
        Shown += OnShown;
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => Close());
        return menu;
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        if (_firewall.HasStaleState())
        {
            var r = MessageBox.Show(this,
                "TorWall state from a previous session was found. The firewall may still be locked down. Restore now?",
                "TorWall", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes)
            {
                Log("Restoring firewall from previous session...");
                try { _firewall.Disable(); Log("Restored."); }
                catch (Exception ex) { Log($"Restore failed: {ex.Message}"); }
            }
        }
        await EnsureRelaysAsync();
    }

    private async Task<bool> EnsureRelaysAsync()
    {
        if (_relayIps.Count > 0) return true;
        return await FetchRelaysAsync();
    }

    private async Task<bool> FetchRelaysAsync()
    {
        SetBusy(true, "Fetching Tor relays from onionoo.torproject.org...");
        try
        {
            var ips = await _fetcher.FetchRelayIPv4Async();
            _relayIps = ips.ToList();
            _lastFetch = DateTime.Now;
            _lblRelays.Text = $"Relay list: {_relayIps.Count} IPs (fetched {_lastFetch:HH:mm:ss})";
            Log($"Fetched {_relayIps.Count} relay IPv4 addresses.");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Fetch failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Fetch failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally { SetBusy(false); }
    }

    private async void OnUpdateClicked(object? sender, EventArgs e)
    {
        var ok = await FetchRelaysAsync();
        if (!ok) return;
        if (_firewall.IsBlocking)
        {
            Log("Re-applying firewall rules with refreshed list...");
            ApplyBlock();
        }
    }

    private async void OnToggleClicked(object? sender, EventArgs e)
    {
        if (_firewall.IsBlocking)
        {
            StopBlock();
        }
        else
        {
            if (!await EnsureRelaysAsync()) return;
            ApplyBlock();
        }
    }

    private void ApplyBlock()
    {
        SetBusy(true, "Applying firewall rules...");
        try
        {
            var count = _firewall.Apply(_relayIps);
            _overlay.Show();
            _btnToggle.Text = "Stop blocking";
            _lblStatus.Text = "Status: BLOCKING (Tor-only)";
            _lblStatus.ForeColor = Color.DarkGoldenrod;
            Log($"Blocking enabled — {count} allowed IPs.");
        }
        catch (Exception ex)
        {
            Log($"Apply failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Apply failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void StopBlock()
    {
        SetBusy(true, "Restoring firewall...");
        try
        {
            _firewall.Disable();
            _overlay.Hide();
            _btnToggle.Text = "Start blocking";
            _lblStatus.Text = "Status: idle";
            _lblStatus.ForeColor = SystemColors.ControlText;
            Log("Blocking disabled, firewall restored.");
        }
        catch (Exception ex)
        {
            Log($"Stop failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Stop failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, string? statusOverride = null)
    {
        _btnToggle.Enabled = !busy;
        _btnUpdate.Enabled = !busy;
        if (busy && statusOverride is not null) _lblStatus.Text = statusOverride;
        Application.DoEvents();
    }

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        _log.AppendText(line);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_firewall.IsBlocking)
        {
            var r = MessageBox.Show(this,
                "Blocking is currently ACTIVE. Disable blocking and restore the firewall before exiting?\n\n" +
                "Yes — restore and exit\nNo — exit and leave the firewall locked\nCancel — keep TorWall running",
                "TorWall", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (r == DialogResult.Cancel) { e.Cancel = true; return; }
            if (r == DialogResult.Yes)
            {
                try { _firewall.Disable(); }
                catch (Exception ex) { Log($"Restore on exit failed: {ex.Message}"); }
            }
        }
        _overlay.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
