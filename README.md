<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/brand/banner-dark.png">
    <img alt="FanPilot — your motherboard's temperatures and fans, on your phone" src="assets/brand/banner-light.png" width="560">
  </picture>
</p>

<p align="center"><b>English</b> · <a href="README.pt-BR.md">Português</a></p>

# FanPilot

Temperature monitoring and fan control from your phone.

A web panel served by the PC itself: CPU, motherboard and GPU temperatures, power, clocks, charts of the last
10 minutes to 7 days, the motherboard's fan curves (a drag-the-points editor, with one-step fine tuning and full
keyboard and screen reader support) and the GPU fan. On the local network, a phone gets in only after pairing with a
QR code shown on the PC. The panel speaks English and Portuguese: it follows the browser's language, and the footer
switches it.

How it works under the hood: see [docs/REVERSE_ENGINEERING.md](docs/REVERSE_ENGINEERING.md) (in Portuguese).

> **Disclaimer:** an independent project, **not affiliated with GIGABYTE, ASUS, NVIDIA** or any other maker, and not
> endorsed by them. "GIGABYTE", "Control Center", "EasyTune", "ASUS" and "NVIDIA" are trademarks of their respective
> owners. FanPilot ships no Gigabyte files: it uses the GCC already installed on your PC. It changes how your
> hardware's fans behave;
> **use it at your own risk** (see the [protections](#hardware-protections)). No warranty, as the
> [license](LICENSE) says.

## Requirements

- Windows 10/11 64-bit (the .NET Framework 4.x it needs comes with Windows).
- Or Linux x64, being tested: see [Linux](#linux-being-tested).
- Run as **Administrator** (the `.exe` asks for elevation by itself).
- One of two ways to reach the motherboard (FanPilot picks it):
  - **A Gigabyte board with Gigabyte Control Center** installed and the **EasyTune Engine Service** running.
    The curves are written to the board's chip. FanPilot uses the DLLs of the GCC already installed and doesn't
    redistribute them. Optional: with the [PawnIO](https://pawnio.eu) driver installed, the CPU card shows the cores'
    temperature (read by LibreHardwareMonitor from the CPU only, without fighting the GCC over the chip); without it,
    the card shows the socket's sensor.
  - **Any other board (ASUS, MSI, ASRock… or a Gigabyte without GCC)** through
    [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), which needs the
    **[PawnIO](https://pawnio.eu)** driver. If it's missing, FanPilot offers to install it right away with `winget`
    (just answer `Y` in the window). On these boards FanPilot runs the curves itself (see
    [Boards without GCC](#boards-without-gcc-fanpilot-runs-the-curves)). **Being tested.**
- GPU: NVIDIA through the driver's `nvml.dll`; AMD, Intel Arc (and NVIDIA without NVML) through
  LibreHardwareMonitor. Without a dedicated GPU, everything else works.

## Build and run

```powershell
.\build.ps1                     # .NET SDK 10; NuGet packages pinned (with hashes) by src\packages.lock.json
dotnet test                     # automated tests (no hardware, no Administrator)
node --test "tests/js/*.test.mjs"  # panel tests (Node 22+)
dotnet build src\FanPilot.csproj -c Debug; cd tests\e2e; npm ci; npx playwright test   # end to end, in Edge
.\bin\FanPilot.exe              # asks for UAC, opens http://localhost:8787/pair; the window shows temperatures and fans
.\bin\FanPilot.exe --tui        # (no UAC) the same screen, attached to a FanPilot already running (the service)
.\bin\FanPilot.exe --log        # the window shows the scrolling log instead
.\scripts\firewall.ps1          # (admin, once) opens port 8787 on the Private network profile
.\scripts\autostart.ps1         # (admin, optional) starts it with the PC as a Windows service, no window, with the tray icon; a click on it (or Start menu > FanPilot) opens its screen
.\scripts\package.ps1           # builds dist\FanPilot-<version>-win-x64.zip to hand out (without data\)
.\bin\FanPilot.exe --version    # version and commit, e.g. FanPilot 0.1.0+1a2b3c4
.\bin\FanPilot.exe --diagnose   # read-only report of the board (for boards nobody has tested yet)
.\bin\FanPilot.exe --hardware-test [--fan N]   # (FanPilot closed) check-up on the real board, ~2 min
.\bin\FanPilot.exe --data <folder>             # data in another folder (default: data\ next to the exe)
.\bin\FanPilot.exe --service --data <folder>   # Windows service mode; the installer (in the works) registers it
.\bin\FanPilot.exe --import-data <old folder> --data <folder>   # (FanPilot closed) brings curves, pairings and names
```

Building needs the [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0): `global.json` pins the version, so
a PC and the CI build alike (another SDK would change the lock file and the C# version). To **run** it, Windows is
enough: FanPilot uses the .NET Framework 4.7.2 that comes with it.

The build doesn't depend on Gigabyte Control Center: FanPilot loads the installed GCC's DLLs only at run time, so one
build (or zip) serves Gigabyte boards and every other one.

`--hardware-test` goes through a fixed speed, a curve watched by the watchdog, Turbo, the GPU fan and a "close and
open again", and writes `data\hwtest-<date>.md`. It touches only the chosen fan (and all of them for Turbo, at 100%),
never below 40%, refuses the CPU fan and pumps (by name, or marked in the panel), stops if the CPU or the GPU passes
80 °C, and puts everything back as it was, Ctrl+C included (closing the window midway is the one way out it can't
catch). Exit code: 0 passed, 6 failed, 7 stopped, 8 not run.

Debug builds accept `--fake-board`: the whole of FanPilot runs on a fake board and GPU, keeping its data in
`data-fake\` (never in the real board's `data\`). `--fake-board=<scenario>` picks the board: `demo` (the default, the
B760M), `worst` (8 fans, long names, a disconnected sensor), `unsupported` (no fan control, no GPU), `down` (the
board's service is down), `lhm` (curves run by FanPilot) and `slow` (takes 4 s to open, as the GCC sometimes does). The
fake board needs no Administrator: without it, the panel only opens on `localhost`. To work on the looks only, use
`tools/ui-mock`.

The end-to-end tests (`tests/e2e`, Playwright) open the panel in Edge against that FanPilot, one scenario at a time:
data, the curve editor (dragging within the rules, keyboard, fine tuning, focus and Esc, 44 px touch targets,
presets, apply, restore), Turbo, the GPU, the panel reconnecting when FanPilot restarts, accessibility (axe-core), both
languages, and screenshots at 320, 390 and 1024 px in both themes. Without Administrator, the tests that play the
phone on the network are skipped. The CI runs everything as Administrator, plus the release zip's smoke test (the zip's
exe starts, answers, `--version` and `--diagnose`), and keeps the report and the screenshots as artifacts of each run.

On the phone (same Wi-Fi network): scan the QR code on the `/pair` page. The code lasts about 3 minutes and works
once; the device stays paired until it goes 30 days unused. The `/pair` page (reachable on the PC itself only) lists
paired devices and unpairs them.

**Panel mode** (`/panel`, the "Panel mode" link in the panel's footer): a read-only, full-screen view for an old phone
or tablet next to the monitor, or a small screen inside the case — the CPU and GPU temperatures in large type, power,
the GPU's load, every fan's RPM and a minigraph, in a layout for each screen shape (800×480 and 1280×400 case screens
included). Nothing on it changes a fan. Its buttons show for a few seconds after a touch: full screen, and "Keep the
screen on" (the Wake Lock API where the browser offers it, else a muted video playing in a loop, which is what works
over the LAN's plain HTTP). For screens set up once: `/panel?scale=1.3&theme=light` (`theme=auto` follows the device;
dark is the default). On OLED screens everything shifts a few pixels every 3 minutes.

## Linux (being tested)

FanPilot also runs on Linux x64 (Ubuntu 24.04 and Fedora are the reference; any distribution with systemd and kernel
6.1 or newer should work), and is **being tested** on real boards
([#35](https://github.com/mateusconrad/FanPilot/issues/35)). It reaches the motherboard through the kernel's hwmon
drivers (nct6775, it87, asus-ec-sensors…: `sensors`, from lm-sensors, shows whether the kernel knows your chip; if it
doesn't, `sudo sensors-detect` says which module to load) and the GPU through NVML (NVIDIA) or amdgpu (AMD). FanPilot
runs the curves itself, as on boards without Gigabyte Control Center, which, like the tray and the installer, stays
on Windows. The package carries its own .NET; the system's ICU (`libicu`) is all it needs.

```bash
tar -xzf FanPilot-0.1.0-linux-x64.tar.gz && cd FanPilot-0.1.0-linux-x64
sudo ./FanPilot --diagnose          # read-only report of the board, to try it first
sudo ./install.sh                   # installs it in /opt/fanpilot as a systemd service, started now and at boot
/opt/fanpilot/FanPilot --tui        # the terminal screen, attached to the service (no sudo)
sudo ./install.sh --remove          # removes it; the data stays in /var/lib/fanpilot
```

`install.sh --import-data <folder>` brings curves, pairings and names from a folder you ran it from before; at the end
it shows how to open the port on `ufw` or `firewalld` for the home network only. The service hands the fans back to
the BIOS when it stops, even after a `kill -9` (`FanPilot --restore-fans`). To build the package yourself, on Linux:
`sh scripts/package.sh` (the .NET SDK 10 is enough).

## Versions and releases

- **Download:** every version comes out on the [Releases](https://github.com/mateusconrad/FanPilot/releases) page as
  `FanPilot-<version>-win-x64.zip`, with a `SHA256SUMS.txt` to check the download
  (`Get-FileHash .\FanPilot-0.1.0-win-x64.zip` must give the same hash). `0.x` versions come out as pre-releases.
- **Which version is running:** in the panel's footer, on the log's first line, in `--diagnose` and in
  `FanPilot.exe --version` (in a terminal running as Administrator: the exe asks for elevation and, outside one, the
  window closes before you can read it). The number comes from `Directory.Build.props`, and the suffix is the build's
  commit.
- **What changed:** [CHANGELOG.md](CHANGELOG.md) (in Portuguese).
- **CI:** every push to `main` builds, runs the tests and keeps the zip as an artifact of the run
  ([Actions](https://github.com/mateusconrad/FanPilot/actions)).
- **Releasing a version:** set `VersionPrefix` in `Directory.Build.props`, replace `## [Não lançado]` in
  `CHANGELOG.md` with `## [<version>] - <date>`, commit and create the tag: `git tag v0.1.0; git push origin v0.1.0`.
  The `Release` workflow checks that the tag matches the version, builds, tests and publishes the release. To rehearse
  without publishing anything: `gh workflow run release.yml` (the files stay as an artifact of the run).

## Why Windows warns when you open it

`FanPilot.exe` isn't signed yet: free signing for open source projects has been requested from the
[SignPath Foundation](https://signpath.org). A new program, with no verifiable publisher, that asks for Administrator
and talks to the hardware is exactly what SmartScreen ("Windows protected your PC") and some antivirus programs block.

To check that the zip is exactly the one published here (built by the CI from this code, never on a personal PC):

1. Download the zip and the `SHA256SUMS.txt` from the same [release](https://github.com/mateusconrad/FanPilot/releases).
2. In PowerShell, in the download folder:

   ```powershell
   (Get-FileHash .\FanPilot-0.1.0-win-x64.zip).Hash -eq (-split (Get-Content .\SHA256SUMS.txt))[0]
   ```

   `True` means the file is the release's. `False`: don't use it, download it again.
3. With the hash checked, on the SmartScreen warning: "More info" › "Run anyway".

## Security

- QR pairing: a random 144-bit code, single use, expiring; wrong attempts are rate-limited per IP.
- Session: an `HttpOnly; SameSite=Strict` cookie with a 256-bit token; only its SHA-256 is stored
  (`bin\data\sessions.json`).
- `/pair` and `/api/pair/*` answer `localhost` only; requests from the PC itself addressed to `localhost` or
  `127.0.0.1` are trusted.
- Only requests addressed to this PC are served: the `Host` header must be `localhost`, an IP of the network
  interfaces, the PC's name or `publicHost`, on the configured port (anything else gets `421`). This keeps a web page
  from reaching the local API through DNS rebinding. To use another name (such as the router's DNS), set `publicHost`.
- POSTs need the `X-FanPilot` header and an `Origin` equal to the host (blocks CSRF). The CSP limits scripts to
  `'self'`.
- Integration tokens (Prometheus, Home Assistant): 256 bits, made on the PC only, kept as their SHA-256; they only
  read `/metrics` and `/api/v1/sensors`.
- **Limitation:** it's plain HTTP on the LAN. Someone on the same network capturing the traffic can copy the cookie.
  Use it only on a trusted home network (the firewall script opens the port on the Private profile only).

## Compatibility

| Feature | Gigabyte with GCC | Other boards (LibreHardwareMonitor + PawnIO) |
|---|---|---|
| Monitoring (temperatures, RPM, voltages, clocks, power) | Boards the GCC supports | Boards whose sensor chip LibreHardwareMonitor recognizes (Nuvoton, ITE, Fintek…) |
| Fan control | Boards where the GCC uses its "3rd generation" path | Headers LibreHardwareMonitor can control |
| Where the curve runs | In the chip (keeps working without FanPilot) | In FanPilot (when it closes, the fans go back to the BIOS) |
| GPU fan and sensors | NVIDIA (NVML); AMD and Intel Arc through LibreHardwareMonitor | Same |

Tested on: **B760M GAMING X AX** (GCC 26.03.26.01). ASUS and other boards: **being tested**, none confirmed yet. The
boards reported so far are in [docs/COMPATIBILITY.md](docs/COMPATIBILITY.md). If you test one, follow
[docs/TESTING-OTHER-BOARDS.md](docs/TESTING-OTHER-BOARDS.md) and report through the
[compatibility form](https://github.com/mateusconrad/FanPilot/issues/new?template=compatibilidade.yml): the
`--diagnose` file brings the link already filled in, and leaves out serial numbers and UUIDs.

To force a path, set `"backend"` in `config.json`: `"auto"` (the default: GCC when installed, else
LibreHardwareMonitor), `"gigabyte"` or `"lhm"`. If a GCC update changes the internal API FanPilot uses, it says in the
panel what changed and goes on with LibreHardwareMonitor (with `"gigabyte"`, it stops with the message). `--diagnose`
shows the GCC's version and whether it is compatible.

## Hardware protections

- **Gigabyte with GCC:** the curves run **in the ITE chip** — they keep working if FanPilot closes.
  On other boards, see [Boards without GCC](#boards-without-gcc-fanpilot-runs-the-curves).
- Every curve: as many points as the board uses (8 on the B760M), rising temperatures, a PWM that never drops as the
  temperature rises, at least 20%, and 100% required at the last point (≤ 90 °C). Point 0 stays at 0 °C (the chip
  never turns the fan off).
- "Fixed" mode: a constant speed, but the chip still goes to 100% above 85 °C.
- "Restore original" goes back to the curve saved on first use (`bin\data\backup-curves.json`).
- A watchdog (15 s) applies your curve again if something overwrites it (resume from sleep, the GCC, the BIOS).
  To go back to using the GCC for fans, click "Restore original" first.
- **Turbo** (button on the fans card): puts every motherboard fan at 100% without touching the GPU fan. While it's
  on, single-fan editing is blocked; when it goes off, each fan goes back to exactly what it had (FanPilot's
  curve or fixed speed, or the original curve). The state survives a restart (`bin\data\global-mode.json`).
- **Profiles** (selector on the fans card): **Quiet**, **Standard** and **Performance** put the preset's curve on
  every fan at once (pumps excepted), on the reference sensor each one already uses; **Custom** brings back exactly
  your own settings from before. Turbo works on top of any mode and goes back to it when it turns off. Editing a fan
  while a profile is on turns the profile off: the other fans keep its curve as their own setting (the editor warns
  first). The mode survives a restart (`bin\data\global-mode.json`, which replaced `turbo.json`).
- A GPU fan in fixed mode goes back to automatic by itself if the GPU reaches 83 °C, or when FanPilot closes.
- **Liquid cooler pump:** a header with "pump", "bomba" or "AIO" in its name already shows as a pump, and any header
  can be marked as one in the editor ("Name and role"). With the **Always 100%** protection (the default), anything
  applied to it becomes 100%; with **Minimum speed**, nothing goes below the chosen minimum (50 to 100%). This holds
  for the editor, Turbo and the saved curves applied again. "Restore original" is still allowed.
- **Names:** each fan can have a name of your own (up to 32 characters), shown on the card and the charts with the
  original next to it. Names and roles are kept in `bin\data\fan-meta.json`, by the fan's identity, and survive a
  reordering.
- **Alerts:** the panel warns, in a strip at the top and in the "Alerts" card, when a fan that was spinning stops
  (reads under 100 RPM for 5 s with at least 30% commanded; a stopped pump is critical), when the CPU passes 95 °C or
  the GPU 87 °C for 10 s (adjustable limits; the alert ends only 5 °C below, after 30 s), when the board doesn't answer
  for a minute, and when a fan was handed back to the BIOS for safety. Fans without a tachometer never alert.
  - **Outside the panel**, optional: an [ntfy](https://ntfy.sh) topic (the "New topic" button suggests a random one,
    hard to guess; subscribe to the same topic in the ntfy app) or a Telegram bot (token and chat id). Each alert goes
    out at most once every 10 minutes, and again when it ends. Nothing leaves the PC without a destination set, and
    the messages carry only the alert and the PC's name, in the language of the panel the settings were saved from.
    The topic's address and the token are encrypted with DPAPI in `bin\data\alerts.json` (another PC can't read them).
- **History:** the charts show the last 10 minutes (live, a reading a second) or, from the files FanPilot keeps, the
  last hour, 24 hours or 7 days, on both charts at once (the choice is kept on the device). Every 10 s, the average and
  the highest of each reading (CPU and GPU temperature and power, each fan's RPM) go to `bin\data\history\<date>.csv`,
  about 1 MB a day; days beyond the retention (`historyDays` in `config.json`: 7 by default, up to 90; 0 turns it off)
  are deleted. A point of the longer charts covers many rows: the tooltip gives their average and their highest.

### Boards without GCC: FanPilot runs the curves

LibreHardwareMonitor can only set a percentage on a header or hand it back to the BIOS; the chip doesn't keep the
curve. So FanPilot reads the temperature and sets each fan **once a second**.

So the fan doesn't go up and down at every CPU load spike, each curve has a **response** (in the editor): **Fast**,
**Balanced** (the default, also for curves saved before it existed) or **Smooth**. It sets how much the temperature is
smoothed before it becomes a speed, how many degrees the PC has to cool down before the fan slows (1, 2 or 4 °C), and
the fastest the speed may rise and fall. Fixed mode isn't smoothed. The protections hold with any response:

- **The curve's last point:** with the temperature (unsmoothed) at the last point or above it, the fan goes to 100% at
  once.
- **Fans you didn't change stay with the BIOS** ("BIOS control" in the panel). "Hand back to BIOS" undoes a curve of
  yours.
- **When it closes** (the window, Ctrl+C, logoff, shutdown) or **if FanPilot hits a fatal error**, every fan goes back
  to BIOS control.
- **A reference sensor with no reading** (or an impossible value, such as the 127 °C of a disconnected input): that
  fan goes back to the BIOS until the reading returns.
- **Repeated failures** of LibreHardwareMonitor (5 in a row): every fan goes back to the BIOS.
- **The GPU as the reference:** besides the board's sensors, a curve can follow the GPU, or the higher of the CPU and
  the GPU — case fans then speed up when a game heats the GPU while the CPU idles. Without a GPU reading, "the
  higher of" follows the sensor alone, and "GPU" hands the fan to the BIOS until the reading returns.
- The speed is set again every second, undoing whatever the BIOS or a maker's app changed.
- **Limitation:** if the process is **killed** (Task Manager > End task, `taskkill /f`) or the PC freezes, there's no
  way to react: the fan stays at its last speed until the BIOS or FanPilot takes over again. That's why the 20% floor
  and the 100% required up to 90 °C hold here too, and why the service `autostart.ps1` installs is restarted by Windows
  1 s after it dies.
- Don't use it together with the maker's fan control (**ASUS Armoury Crate / AI Suite "Fan Xpert"**, MSI Center and
  so on): the two would fight over the fans. Turn the fan control off there.

Adjustable limits in `bin\data\config.json` (`minDutyPercent`, `fixedModeSafetyTemp`, `maxFullSpeedTemp`,
`gpuSafetyTemp`, `port`, `publicHost`, `pairCodeTtlSeconds`, `sessionDays`, `backend`, `historyDays`).

## Operation

- **Stop:** close FanPilot's window or press Ctrl+C in it. On the way out, the GPU fan goes back to automatic. On a
  Gigabyte with GCC the curves stay in the chip; on other boards the fans go back to BIOS control.
- **One at a time:** opening FanPilot while it's running just opens the panel. If another program holds the port,
  FanPilot says so and closes before touching any fan.
- **Language:** the panel follows the browser's (Portuguese for any `pt-*`, English for everything else) and the footer
  switches it; the choice is kept on the device. FanPilot's window, `--diagnose` and `--hardware-test` follow Windows'
  language. Alerts sent to ntfy and Telegram go out in the language of the panel they were set up from.
- **Log:** `bin\data\fanpilot.log` (pairings, fan changes, watchdog re-applies, errors), always in English, so a
  message can be searched for in any issue. Past 5 MB it becomes `fanpilot.1.log` (up to 3 old files). A message
  repeated in a row shows once, with a summary every minute, and failures become state-change records (failed,
  recovered).
- **Data:** `bin\data\` keeps `config.json`, `sessions.json`, `backup-curves.json` (the original curves, written
  only on the first run), `desired-curves.json` (the curves FanPilot maintains) and `machine.json` (identifies the PC
  and the motherboard). Each file is written in one go (never half written) and keeps the previous version in `.bak`.
  An unreadable file is set aside as `<name>.corrompido-<date>`, and FanPilot goes on with the previous version or
  without it, saying so in the panel. An invalid `config.json` is never changed: FanPilot uses the defaults until it
  is fixed. Curves are kept by each fan's identity, not its position: if a BIOS or LibreHardwareMonitor update changes
  the fans' order, each curve stays with its fan; one for a fan that's gone isn't applied to any other, and the panel
  offers to discard it. FanPilot versions from before that change can't read the new format; the old files are kept
  as `.v1`.
- **Another PC / a motherboard swap:** if `bin\data\` came from another PC or another board, FanPilot doesn't apply
  the old curves, backup or pairings: they are moved to `bin\data\outra-maquina-<date>\` and everything starts over
  (`config.json` is kept). To take the program to another PC, use the zip made by `scripts\package.ps1` (or copy the
  `bin\` folder without `data\`).
- **GCC and FanPilot together:** don't use both to control fans at the same time — one overwrites the other, and
  FanPilot's watchdog will apply its curve again every 15 s. To go back to the GCC, use "Restore original" on each fan
  you changed (that also takes the fan out of the watchdog). Just monitoring with both open is fine.

## Integrations (Prometheus, Grafana, Home Assistant)

Read only: nothing an integration does changes a fan.

- **Token:** on the PC, open `http://localhost:8787/pair`, "Integrations": give it a name and "Create token". The
  token (`fp_…`) is shown once; FanPilot keeps only its SHA-256 (`bin\data\integrations.json`), with when it was
  created and last used. "Revoke" ends it at once (the integration then gets `401`). The integration sends it as
  `Authorization: Bearer fp_…` and reaches `/metrics` and `/api/v1/sensors` only (anything else gets `403`). From the
  network it needs the same as a phone: the port open in the firewall and the PC's address (or `publicHost`).
- **Prometheus** (`/metrics`, the text format): `fanpilot_up`, `fanpilot_info`, `fanpilot_cpu_temperature_celsius`,
  `fanpilot_cpu_power_watts`, `fanpilot_temperature_celsius{sensor,name}`, `fanpilot_power_watts{sensor,name}`,
  `fanpilot_fan_rpm{fan,name}`, `fanpilot_fan_duty_percent{fan,name}` (what the fan's curve asks for at the current
  temperature; absent while the BIOS runs it), `fanpilot_gpu_temperature_celsius`, `fanpilot_gpu_power_watts`,
  `fanpilot_gpu_utilization_percent`, `fanpilot_gpu_fan_percent` and `fanpilot_alerts_active{severity}`.

  ```yaml
  scrape_configs:
    - job_name: fanpilot
      authorization:
        credentials: fp_…   # or credentials_file: /etc/prometheus/fanpilot-token
      static_configs:
        - targets: ["192.168.0.10:8787"]   # the PC's address and FanPilot's port
  ```

- **Home Assistant over MQTT** (the easiest way): in the panel, "Home Assistant (MQTT)": the broker's address (the
  Mosquitto add-on's, for instance), port (1883, or 8883 with TLS), user and password. The entities show up on their
  own, under a device named after the PC: CPU and GPU temperature and power, GPU utilization and fan, each fan's RPM
  and requested speed, active alerts and a motherboard problem sensor, named in the panel's language. The state goes
  to `fanpilot/<pc>/state` every 5 s (the same JSON as `/api/v1/sensors`), and `fanpilot/<pc>/availability` says
  `online` or `offline`, which the broker also says when FanPilot's connection drops. Read only: Home Assistant
  controls nothing. The password is kept encrypted with DPAPI in `bin\data\mqtt.json`; with TLS, Windows has to trust
  the broker's certificate.
- **Home Assistant** (REST sensor, `/api/v1/sensors`): a JSON that only ever gains fields (`"version": 1`): `cpu`
  (`temperature`, `power`), `gpu` (`name`, `temperature`, `power`, `utilization`, `fan`; `null` without a GPU),
  `fans` (`id`, `name`, `label`, `pump`, `rpm`, `duty`), `temperatures` and `power` (`id`, `name`, `value`), `alerts`
  (`id`, `severity`, `title`), `board` (`name`, `backend`, `ok`), `time`, `fanpilot` (the version) and `pc`.

  ```yaml
  # configuration.yaml; in secrets.yaml: fanpilot_auth: "Bearer fp_…"
  rest:
    - resource: http://192.168.0.10:8787/api/v1/sensors
      headers:
        Authorization: !secret fanpilot_auth
      scan_interval: 30
      sensor:
        - name: PC CPU
          value_template: "{{ value_json.cpu.temperature }}"
          unit_of_measurement: "°C"
          device_class: temperature
          state_class: measurement
        - name: PC System 1 fan
          value_template: "{{ (value_json.fans | selectattr('name', 'eq', 'System 1') | first).rpm }}"
          unit_of_measurement: RPM
          state_class: measurement
  ```

## Common problems

| Symptom | Likely cause / fix |
|---|---|
| The phone can't open the page | Firewall: run `scripts\firewall.ps1`. The rule only holds on a **Private** network — if the Wi-Fi is set to Public, change it in Settings › Network › Properties. |
| The QR code points to the wrong IP | Pick another address in the `/pair` page's box (e.g. the Wi-Fi one, not `vEthernet`), or set `publicHost` in `config.json`. |
| "Invalid or expired code" | The code works once and lasts about 3 minutes; scan the current QR code again. Too many wrong attempts block the IP for a minute. |
| The phone goes back to the lock screen | Session revoked on `/pair`, cookies cleared, or 30 days unused: pair again. |
| Status "board sensors unavailable" | Gigabyte: `EasyTuneEngineService` stopped (`services.msc`), or the GCC was updated or removed. The GPU keeps working. |
| "The PawnIO driver is missing" | A board without GCC: close FanPilot, open it from its window and answer `Y` to install it (or install it from the [site](https://pawnio.eu)). Run as a service (`autostart.ps1`) it doesn't ask. |
| A board without GCC shows no fans | LibreHardwareMonitor doesn't recognize or can't control that board's chip. Run `FanPilot.exe --diagnose` and report through the link in the file it writes. |
| FanPilot closes right after opening | It isn't running as Administrator, or the GCC isn't installed. See the log. |
| "This motherboard has no fan control" | The board doesn't use the control path FanPilot implements; only monitoring works. |
| The GPU fan won't take fixed mode | The driver refused manual control (a message with an NVML code); the fan stays on automatic. |
| A curve "goes back by itself" | Something (the GCC, the BIOS, resume from sleep) overwrote it; the watchdog applies it again within 15 s and logs it. |

## Example: notes from the test board (B760M GAMING X AX)

- The GCC was in the global **FullSpeed** mode (`Control Center\Fan\setupmode.xml`): every original curve is 100% at
  any temperature. That's what "Restore original" gives back.
- The **CPU** header reads 0 RPM, and so does **System 3**: nothing with a tachometer is plugged into them. There's no
  USB liquid cooler controller (the only board USB device is the motherboard's own ITE RGB controller), so the AIO
  reports nothing in software. Guesses: the cooler's pump is on a System header (pumps usually spin at a steady
  ~2000–2800 RPM, like **System 1** at ~2350) or is powered straight from the PSU (SATA/Molex), without an RPM wire;
  the radiator fans are probably on **System 2** (~1680 RPM, using the CPU sensor as its reference). Check it
  physically. **If a header is the pump, mark it as a pump** in the editor ("Name and role"): a slow pump cuts cooling
  a lot, and with the "Always 100%" protection nothing FanPilot applies makes it slower. The RPM chart leaves out
  headers that read 0.
- Temperature sensors available as curve references: System 1, PCH, CPU, PCIEX16, VRM MOS, System 2. The GCC's "CPU"
  temperature is the board's sensor (the socket), not the cores': the panel calls it "CPU · socket" and, with PawnIO
  installed, shows the cores' temperature ("CPU · package") on the card. The curves stay on the board's sensor,
  because that's what the chip reads.

## Reverse engineering tools (`tools/`)

Test programs used to check the analysis; they need Administrator and reference the GCC's DLLs. They aren't part of
the project: build each one with the .NET Framework's `csc.exe`, passing the GCC's DLLs with `/r:`.

| File | What it does |
|---|---|
| `Probe.cs` | First read through remoting: the HardwareMonitor's sensors and a try at the SmartGuardian modules. |
| `Probe2.cs` | The experiment that showed the first IPC call failing (solved with a retry) and the "unsupported" modules. |
| `Probe3.cs` | Reads (only) the curves through the 3rd-generation path (`SmartGuardianFanControlCoordinateSystemModule`). |
| `Probe4.cs` | Write test: sets System 1 to 40% for 8 s, measures the RPM and **restores the original curve** by itself. |
| `ui-mock/` | A fake-data server to work on the panel **without a Gigabyte board or Administrator**: `python tools/ui-mock/mock.py` and open `http://127.0.0.1:8799/__mock`. Switches between Demo, Worst case (an 8-fan board, long names, disconnected sensors), No control, Service down and Turbo, at 320/390/1024 px in light and dark themes. |

## API

| Method | Route | |
|---|---|---|
| GET | `/api/state` | current snapshot (sensors, RPM, GPU) |
| GET | `/api/stream` | Server-Sent Events, one snapshot a second |
| GET | `/api/history?range=…` | `10m` (the default): the last 10 minutes, a point a second (temperatures, power, RPM). `1h`, `24h`, `7d`: from the files on disk, at most 600 points, each with the average (`series`), the highest (`max`) and the lowest (`min`) of its 10 s rows; `null` where FanPilot was closed |
| GET | `/api/fans` | current curves, reference sensors, limits |
| GET | `/api/info` | FanPilot's version, backend and motherboard, and whether the history on disk is on (`history`) |
| GET | `/metrics` | the readings in Prometheus' text format; also with an integration token (see Integrations) |
| GET | `/api/v1/sensors` | the stable JSON for integrations; also with an integration token (see Integrations) |
| GET/POST | `/api/mqtt` | Home Assistant over MQTT: `enabled`, `host`, `port`, `tls`, `username`, `password` (only when sent; `""` removes it), `discoveryPrefix`, and the connection's `status` |
| GET | `/api/alerts` | active and recent alerts (the active ones also come in every snapshot, in `alerts`) |
| POST | `/api/alerts/ack` | `{"id":"…"}` takes the alert off the strip (it stays in the list while it lasts) |
| GET/POST | `/api/alerts/settings` | limits, the PC's name, the message language and the destinations; secrets come back masked only; `""` removes a destination |
| POST | `/api/alerts/test` | sends a test message to the configured destinations |
| POST | `/api/fans/{i}` | `{"mode":"fixed","percent":40}` or `{"mode":"curve","style":"SmartFanMode","points":[[0,30],…N points in %, N = the fan's pointCount],"tempInput":2}`; where FanPilot runs the curves, also `"response"` (`fast`, `balanced`, `smooth`) and `"source"` (`sensor`, `gpu` or `max`, the higher of `tempInput` and the GPU) |
| POST | `/api/fans/{i}/restore` | back to the original curve |
| POST | `/api/fans/{i}/meta` | `{"label":"Front","role":"pump","pumpPolicy":"min","pumpMinPercent":70}` name (up to 32 characters; empty goes back to the original) and role; `role` `fan` or `pump`, `pumpPolicy` `full` (always 100%) or `min` (a floor from 50 to 100%) |
| POST | `/api/fans/turbo` | `{"on":true}` every motherboard fan at 100%; `{"on":false}` gives each fan back its previous state |
| POST | `/api/fans/mode` | `{"mode":"profile:quiet"}` (or `profile:standard`, `profile:performance`) puts the profile on every fan except pumps; `{"mode":"custom"}` brings back your own settings |
| POST | `/api/gpu/fan` | `{"mode":"auto"}` or `{"mode":"fixed","percent":50}` |

Errors come as `{"error": "<English text>", "code": "err.curve.points", "params": {"n": 8}}`; notices, alerts and the
board's messages as `{"code", "params", "text"}`. The panel translates them by code (`wwwroot/i18n/`).

## Layout

```
src/        C# (.NET Framework 4.7.2, FanPilot.csproj): IBoard + the Gigabyte (GCC) and LibreHardwareMonitor bridges,
            NVML, HTTP/SSE server, auth, fan policy, --diagnose, I18n
tests/      automated tests: xUnit (dotnet test), the panel (tests/js, node --test) and end to end (tests/e2e, Playwright)
wwwroot/    the panel (index.html/app.js), pairing (pair.html/pair.js), the English and Portuguese dictionaries
            (i18n/), qrcode-generator (MIT)
tools/      probes used in the reverse engineering
assets/     visual identity: source SVGs, the .exe icon, banners (to regenerate: python assets/brand/build.py)
scripts/    firewall, autostart, release packaging, release notes and the encoding check
.github/    CI, release by tag and Dependabot
docs/       the reverse engineering report, the guide to testing other boards (in both languages), tested boards,
            roadmap and specs (in Portuguese)
```

## License

[MIT](LICENSE). Third-party components: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
