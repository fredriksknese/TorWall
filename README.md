# TorWall

A Windows tool that blocks **all** outbound network traffic except connections to
running Tor relays. A bright yellow frame around your desktop reminds you that
the lockdown is active.

> ⚠️ **TorWall does not anonymise your traffic by itself.** It only restricts
> what your machine is *allowed to talk to*. You still need a Tor client
> (Tor Browser, the `tor` daemon, Tails-on-the-side, etc.) to actually route
> your traffic through the Tor network. TorWall is a belt that keeps badly-behaved
> apps from leaking around your Tor configuration.

## What it does

- On **Start blocking**, TorWall
  1. Fetches the list of currently running Tor relays from the official
     [`onionoo.torproject.org`](https://onionoo.torproject.org/) service.
  2. Switches Windows Firewall's default outbound action to **Block** for
     every profile (Domain / Private / Public).
  3. Adds Allow rules for the relay IPv4 addresses, the nine hard-coded Tor
     directory authorities, the loopback range (`127.0.0.0/8`), and DHCP
     so your adapter keeps its lease.
  4. Paints a thin yellow click-through frame around every monitor so you
     always know the lockdown is on.
- The relay list can be **refreshed on demand** with the *Update relay list*
  button. If blocking is already active the firewall is re-applied with the
  new list in place.
- On **Stop blocking** TorWall removes every rule it created and restores
  the previous default outbound policy.

## Requirements

- Windows 10 or 11
- [.NET 11 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/11.0)
- Administrator privileges (the executable carries a manifest that requests
  elevation automatically).

## Build

```powershell
git clone https://github.com/<your-user>/TorWall.git
cd TorWall
dotnet build -c Release
```

The compiled binary will be in `TorWall/bin/Release/net11.0-windows/`.
For a single self-contained executable:

```powershell
dotnet publish TorWall -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Usage

1. Launch `TorWall.exe` (UAC will prompt for elevation).
2. The relay list is fetched automatically on first launch.
3. Click **Start blocking**. The desktop frame turns yellow; only Tor relays
   are reachable.
4. Launch Tor Browser (or your own `tor` daemon) and verify connectivity.
5. Click **Stop blocking** — or close the window and choose *Yes* — to
   return to a normal firewall.

If TorWall is killed while blocking is active, the firewall will stay locked
down. On the next launch TorWall detects the leftover state file in
`%APPDATA%\TorWall\state.json` and offers to clean up.

## How it works under the hood

- **Firewall changes** are made with `netsh advfirewall` rules tagged
  `TorWall_*`, so they are easy to inspect and to remove manually if needed:

  ```powershell
  netsh advfirewall firewall show rule name=all | findstr TorWall_
  ```

- **Relay list** comes from
  `https://onionoo.torproject.org/details?type=relay&running=true&fields=or_addresses`.
  IPv6 addresses are dropped; the IPv4 set (~7000 entries) is split across
  several Allow rules to stay below netsh's command-length limit.
- **Desktop frame** is drawn by four borderless `TopMost` forms per monitor
  with `WS_EX_TRANSPARENT | WS_EX_LAYERED` so clicks pass through to whatever
  is underneath.

## Project layout

```
TorWall/
├── Program.cs                  entry point, elevation check
├── MainForm.cs                 control panel UI
├── app.manifest                requestedExecutionLevel = requireAdministrator
├── Services/
│   ├── TorRelayFetcher.cs      Onionoo HTTP client
│   └── FirewallManager.cs      netsh wrapper, state persistence
└── UI/
    └── BorderOverlay.cs        click-through yellow frame
```

## Caveats

- IPv6 traffic is not blocked or whitelisted. If you need a truly Tor-only
  setup, disable IPv6 on your adapter as well.
- Onionoo's relay set churns every few hours; refresh occasionally for best
  reliability.
- If the firewall service itself is stopped, TorWall can neither block nor
  restore. Don't disable the Windows Defender Firewall service.
- The bundled directory-authority IP list is current as of release and
  changes only when the Tor Project updates the consensus authorities.

## License

MIT
