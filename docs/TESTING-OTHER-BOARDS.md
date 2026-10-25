# Testing FanPilot on a board that isn't a Gigabyte (ASUS, MSI, ASRock…)

**English** · [Português](TESTANDO-OUTRAS-PLACAS.md)

On these boards FanPilot uses [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
and runs the curves itself (see "Boards without GCC" in the [README](../README.md#boards-without-gcc-fanpilot-runs-the-curves)).
No board is confirmed yet, so the test goes in steps, from the safest to the least safe. **Stop at the first step that
does something odd** and send what you have.

## 0. Before you start

- Use the release zip (`FanPilot-….zip`) and extract it to a folder of yours, for example `C:\FanPilot`.
- The **PawnIO** driver: the first time you open `FanPilot.exe`, if it's missing, the window asks whether it may
  install it with `winget`; answer `Y`. Without winget, install it from the official site: https://pawnio.eu. Restart
  the PC if asked.
- **Turn off the maker's fan control** while you test: on ASUS, in Armoury Crate / AI Suite, leave Fan Xpert with no
  active profile (or close it); on MSI, MSI Center. If both control the fans at the same time, the test doesn't count.
- Write down the **exact motherboard model** (it's in `--diagnose`, but check it on the box or in the BIOS).

## 1. Diagnostics (read only, touches no fan)

Open a terminal in the folder and run:

```powershell
.\FanPilot.exe --diagnose
```

The diagnostics install nothing: if PawnIO is missing, open `FanPilot.exe` once first (step 0). It asks for
Administrator, saves `data\diagnostico-<date>.txt` and opens the file in Notepad. The file leaves out serial numbers and
UUIDs (they belong to your PC, not to the board model) and starts with the link to the report form, already filled in.
On an English Windows the file is in English. Check in it:

- `PawnIO: installed` — if it says "NOT installed", go back to step 0.
- Under **Fans that can be controlled**, the list of headers (e.g. `CPU Fan`, `Chassis Fan #1`, `AIO Pump`).
- Under **Reference sensors**, plausible temperatures (a CPU between 30 and 70 °C at idle).

If the list of fans is empty, stop here: LibreHardwareMonitor can't control this board, and the file already helps to
understand why.

## 2. Monitoring

Run `.\FanPilot.exe` (it asks for Administrator and opens `http://localhost:8787/pair` in the browser). Open the panel
on the PC itself ("Open the panel here") and check that temperatures, RPM and the GPU match what Armoury Crate or
HWiNFO show. Don't change anything yet.

## 3. One case fan, at a fixed speed

Pick a **case fan** (`Chassis Fan`). **Don't start with the processor's fan or the pump (`AIO Pump`, `W_PUMP`)**.

1. In the panel, tap it › **Fixed** › 40% › Apply. Within a few seconds the RPM should drop.
2. Tap it again › **Hand back to BIOS**. The RPM should go back to what it was.
3. Set 40% again and **close FanPilot's window** (with the X). The RPM should go back to what it was: that's the
   protection that hands the fans back to the BIOS on closing.

## 4. Curve

On the same case fan, pick a curve (e.g. "Standard"), apply it and leave it for a few minutes with the PC busy (a game
or a CPU test). The RPM should follow the chosen sensor's temperature. Then, "Hand back to BIOS".

## 5. What to send

Open the link at the top of the diagnostics file (or the
[compatibility form](https://github.com/mateusconrad/FanPilot/issues/new?template=compatibilidade.yml) directly). It
comes with the board model and FanPilot's version filled in and asks for:

- the BIOS version and the path FanPilot used;
- what worked at each step (did the RPM change? did it go back on closing?) and the result;
- the `diagnostico-….txt` file and `data\fanpilot.log`, dragged into the form.

The boards reported so far are in [COMPATIBILITY.md](COMPATIBILITY.md).

## If something goes wrong

- A fan doesn't go back to normal: open and close FanPilot again, or restart the PC — the BIOS takes the fans over at
  boot.
- To remove everything: delete the folder and, if you like, uninstall PawnIO from "Add or remove programs".
