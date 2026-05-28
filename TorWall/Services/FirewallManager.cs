using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TorWall.Services;

/// Drives the Windows Firewall via netsh: switches the outbound default to
/// Block and adds Allow rules for the supplied Tor relay IPs. State about
/// the previous outbound policy is persisted so we can restore it on stop.
public sealed class FirewallManager
{
    private const string RulePrefix = "TorWall_";
    private const int IpsPerRule = 200;

    /// The nine Tor directory authorities, baked in as a safety net so the
    /// Tor client can always bootstrap even if the relay list is empty or
    /// the consensus is briefly out of sync with Onionoo.
    private static readonly string[] DirectoryAuthorities =
    {
        "128.31.0.39",      // moria1
        "217.196.147.77",   // tor26
        "45.66.35.11",      // dizum
        "131.188.40.189",   // gabelmoo
        "193.23.244.244",   // dannenberg
        "171.25.193.9",     // maatuska
        "199.58.81.140",    // longclaw
        "204.13.164.118",   // bastet
        "216.218.219.41",   // faravahar
    };

    private static readonly string StateDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TorWall");
    private static readonly string StatePath = Path.Combine(StateDir, "state.json");

    public bool IsBlocking { get; private set; }

    public int Apply(IEnumerable<string> relayIps)
    {
        var ips = new HashSet<string>(relayIps);
        foreach (var da in DirectoryAuthorities) ips.Add(da);

        var savedPolicy = ReadState()?.PreviousOutboundPolicy ?? GetOutboundPolicy();

        RemoveAllRules();

        var chunks = ips.Chunk(IpsPerRule).ToList();
        for (int i = 0; i < chunks.Count; i++)
        {
            var name = $"{RulePrefix}AllowRelays_{i:D4}";
            var list = string.Join(",", chunks[i]);
            RunNetsh($"advfirewall firewall add rule name=\"{name}\" dir=out action=allow " +
                     $"remoteip={list} profile=any enable=yes");
        }

        // Keep loopback working so local Tor SOCKS proxy / control port is reachable.
        RunNetsh($"advfirewall firewall add rule name=\"{RulePrefix}AllowLoopback\" " +
                 "dir=out action=allow remoteip=127.0.0.0/8 profile=any enable=yes");

        // DHCP so the adapter can keep its lease.
        RunNetsh($"advfirewall firewall add rule name=\"{RulePrefix}AllowDHCP\" " +
                 "dir=out action=allow protocol=UDP localport=68 remoteport=67 profile=any enable=yes");

        SaveState(new State { PreviousOutboundPolicy = savedPolicy });
        SetOutboundPolicy("blockoutbound");
        IsBlocking = true;
        return ips.Count;
    }

    public void Disable()
    {
        var state = ReadState();
        var restoreTo = state?.PreviousOutboundPolicy ?? "allowoutbound";

        RemoveAllRules();
        SetOutboundPolicy(restoreTo);

        if (File.Exists(StatePath)) File.Delete(StatePath);
        IsBlocking = false;
    }

    /// True if a previous TorWall session left the firewall locked down (for
    /// instance because the process was killed). Lets the UI offer recovery.
    public bool HasStaleState() => File.Exists(StatePath);

    private static void RemoveAllRules()
    {
        // Delete the fixed-name rules. netsh exits non-zero when no rule
        // with that name exists; that's fine for an idempotent cleanup.
        RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}AllowLoopback\"", allowFailure: true);
        RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}AllowDHCP\"", allowFailure: true);

        // Iterate the numbered relay-allow rules until one is missing. A
        // delete that matches >=1 rule returns 0; no match returns 1.
        for (int i = 0; i < 10_000; i++)
        {
            var name = $"{RulePrefix}AllowRelays_{i:D4}";
            var exit = RunNetshExit($"advfirewall firewall delete rule name=\"{name}\"");
            if (exit != 0) break;
        }
    }

    private static int RunNetshExit(string args)
    {
        var psi = new ProcessStartInfo("netsh", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start netsh.");
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private static void SetOutboundPolicy(string outbound)
    {
        RunNetsh($"advfirewall set allprofiles firewallpolicy blockinbound,{outbound}");
    }

    private static string GetOutboundPolicy()
    {
        var output = RunNetsh("advfirewall show allprofiles", capture: true) ?? string.Empty;
        // We look for the first "Firewall Policy" line; format is
        //   "Firewall Policy                       BlockInbound,AllowOutbound"
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("Firewall Policy", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split(',');
            if (parts.Length < 2) continue;
            var ob = parts[^1].Trim().ToLowerInvariant();
            if (ob.StartsWith("allow")) return "allowoutbound";
            if (ob.StartsWith("block")) return "blockoutbound";
        }
        return "allowoutbound";
    }

    private static string? RunNetsh(string args, bool capture = false, bool allowFailure = false)
    {
        var psi = new ProcessStartInfo("netsh", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = capture,
            RedirectStandardError = capture,
            StandardOutputEncoding = capture ? Encoding.UTF8 : null,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start netsh.");
        string? stdout = null;
        if (capture) stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException($"netsh failed (exit {p.ExitCode}) for: {args}");
        return stdout;
    }

    private static State? ReadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath));
        }
        catch { return null; }
    }

    private static void SaveState(State s)
    {
        Directory.CreateDirectory(StateDir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(s));
    }

    private sealed class State
    {
        public string PreviousOutboundPolicy { get; set; } = "allowoutbound";
    }
}
