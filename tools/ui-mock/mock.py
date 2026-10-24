# Dev-only fake backend for the panel: serves wwwroot/ with fixture data, so the UI can be worked on without
# Gigabyte hardware, admin rights or the real FanPilot.exe. Never shipped (build.ps1 copies only wwwroot/).
#
#   python tools/ui-mock/mock.py            -> http://127.0.0.1:8799/__mock  (fixture switcher + panel in an iframe)
#
# The switcher stores the fixture in a cookie that the API endpoints read, so the panel code is untouched.
import json, math, pathlib, sys, time, http.server
from http.cookies import SimpleCookie

HERE = pathlib.Path(__file__).resolve().parent
WWW = HERE.parent.parent / "wwwroot"
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 8799


def curve(*pct_points):
    return [[t, round(p * 2.55)] for t, p in pct_points]


DEFAULT_CURVE = curve((0, 25), (35, 25), (45, 35), (55, 55), (62, 65), (68, 75), (74, 85), (82, 100))
FULL = curve((0, 100), (20, 100), (30, 100), (40, 100), (50, 100), (60, 100), (70, 100), (75, 100))


def fan(i, title, temp_input, points=DEFAULT_CURVE, mode="SmartFanMode", request=None, managed=None):
    return {"index": i, "title": title, "tempInput": temp_input, "mode": mode, "deltaT": 1, "points": points,
            "pointCount": len(points), "managed": bool(request) if managed is None else managed,
            "request": request, "hasBackup": True}


def sensors(temps, fans_rpm, volts, power, clocks):
    out = [{"id": "temperature-%d" % (512 + i), "type": "Temperature", "title": t, "value": v, "unit": "°C"} for i, (t, v) in enumerate(temps)]
    out += [{"id": "fan-%d" % (768 + i), "type": "Fan", "title": t, "value": v, "unit": "RPM"} for i, (t, v) in enumerate(fans_rpm)]
    out += [{"id": "voltage-%d" % i, "type": "Voltage", "title": t, "value": v, "unit": "V"} for i, (t, v) in enumerate(volts)]
    out += [{"id": "frequency-%d" % (256 + i), "type": "Frequency", "title": t, "value": v, "unit": "MHz"} for i, (t, v) in enumerate(clocks)]
    out += [{"id": "power-%d" % (1280 + i), "type": "Power", "title": t, "value": v, "unit": "W"} for i, (t, v) in enumerate(power)]
    return out


# ---- fixtures -------------------------------------------------------------------------------------------------
# demo: the B760M GAMING X AX this was developed on.
DEMO = {
    "board": "Gigabyte Technology Co., Ltd. B760M GAMING X AX",
    "temps": [("System 1", 38.0), ("PCH", 45.0), ("CPU", 52.0), ("PCIEX16", 40.0), ("VRM MOS", 48.0), ("System 2", 36.0)],
    "fans": [fan(0, "CPU", 2), fan(1, "System 1", 0), fan(2, "System 2", 2, request={"mode": "curve"}), fan(3, "System 3", 0)],
    "rpm": [0, 2350, 1680, 0],
    "volts": [("VCore", 1.104), ("+3.3V", 3.336), ("+12V", 12.168), ("+5V", 5.04)],
    "power": [("CPU Package", 41.2), ("CPU Cores", 30.1)],
    "clocks": [("CPU", 4600), ("BCLK", 100.0)],
    "gpu": {"ok": True, "name": "NVIDIA GeForce RTX 4070", "temp": 58, "util": 34, "power": 96, "powerLimit": 200,
            "clock": 2475, "memClock": 10501, "memUsed": 4200, "memTotal": 12282, "fans": [42], "fanMode": "auto",
            "safetyTemp": 83, "fanMin": 30, "fanMax": 100},
}

# worst: a big AORUS board under load. Real header/sensor names, 8 headers, a pump, a disconnected thermistor
# (127 / -128 are what an open ITE input reads), a 3-fan GPU on a long name, values at the top of their ranges.
WORST = {
    "board": "Gigabyte Technology Co., Ltd. X870E AORUS MASTER X3D ICE",
    "temps": [("System 1", 41.0), ("PCH", 71.5), ("CPU", 98.0), ("PCIEX16", 62.0), ("VRM MOS", 104.0),
              ("System 2", 127.0), ("PCIEX8", -128.0), ("EC_TEMP1", 55.0), ("EC_TEMP2", 0.0)],
    "fans": [
        fan(0, "CPU_FAN", 2, request={"mode": "fixed", "percent": 35}),
        fan(1, "CPU_OPT", 2, points=FULL, mode="StairStepMode"),
        fan(2, "SYS_FAN1", 4, request={"mode": "curve"}),
        fan(3, "SYS_FAN2", 0),
        fan(4, "SYS_FAN3", 3),
        fan(5, "SYS_FAN4", 7),
        fan(6, "SYS_FAN5_PUMP", 2, points=FULL),
        fan(7, "SYS_FAN6_PUMP", 2, points=FULL),
    ],
    "rpm": [1, 2876, 1950, 0, 7230, 845, 4815, 0],
    "volts": [("VCore", 1.452), ("CPU VDDIO / MC", 1.4), ("+3.3V", 3.312), ("+12V", 12.384), ("+5V", 5.1),
              ("CPU VDD Misc", 1.1), ("DDR VDD", 1.4), ("DDR VDDQ", 1.4), ("VDDCR_SOC", 1.25), ("PM_CLDO12", 1.2)],
    "power": [("CPU Package", 253.6), ("CPU Cores", 231.04), ("CPU SoC", 22.5)],
    "clocks": [("CPU", 5875), ("BCLK", 100.02), ("CPU Ratio", 58.75), ("DDR", 8000)],
    "gpu": {"ok": True, "name": "NVIDIA GeForce RTX 5090 D v2 Laptop GPU", "temp": 87, "util": 100, "power": 574.9,
            "powerLimit": 600, "clock": 2850, "memClock": 14001, "memUsed": 31920, "memTotal": 32607,
            "fans": [100, 98, 100], "fanMode": "fixed", "fanTarget": 100, "safetyTemp": 83, "fanMin": 30, "fanMax": 100,
            "safetyNote": "A GPU chegou a 87 °C: fan devolvido ao automático pelo FanPilot (limite de segurança 83 °C)."},
}

# unsupported: board without the fan-control path; monitoring only, no NVIDIA GPU.
UNSUPPORTED = dict(DEMO, board="Gigabyte Technology Co., Ltd. B450M DS3H V2", fans=[], rpm=[], supported=False,
                   gpu={"ok": False, "error": "NVML não encontrado (nvml.dll): instale o driver da NVIDIA."})

# down: EasyTuneEngineService stopped; GPU still works.
DOWN = dict(DEMO, service_down=True)

# turbo: demo board with turbo on.
TURBO = dict(DEMO, turbo=True)

# asus: LibreHardwareMonitor path (Nuvoton NCT6798D, LHM's board names). FanPilot runs the curves; unmanaged
# headers are on the BIOS (mode BiosDefault, no points).
def bios_fan(i, title):
    return {"index": i, "title": title, "tempInput": 0, "mode": "BiosDefault", "deltaT": 1, "points": [],
            "pointCount": 8, "managed": False, "request": None, "hasBackup": True}

ASUS = dict(DEMO,
    board="ASUSTeK COMPUTER INC. ROG STRIX B550-F GAMING (WI-FI)",
    backend="LibreHardwareMonitor", hardwareCurves=False,
    temps=[("CPU", 61.4), ("CPU (2)", 58.0), ("Motherboard", 34.0), ("Chipset", 52.0), ("CPU Socket", 44.0)],
    fans=[fan(0, "CPU Fan", 0, request={"mode": "curve"}), bios_fan(1, "CPU Optional Fan"), bios_fan(2, "Chassis Fan #1"),
          bios_fan(3, "Chassis Fan #2"), fan(4, "AIO Pump", 0, points=FULL, request={"mode": "fixed", "percent": 100})],
    rpm=[1180, 0, 860, 905, 2710],
    volts=[("Vcore", 1.362), ("+12V", 12.096), ("+5V", 5.04), ("3VCC", 3.392)],
    power=[("CPU Package", 88.4)], clocks=[("CPU", 4650)],
    gpu=dict(DEMO["gpu"], name="NVIDIA GeForce RTX 3060 Ti"))

# nopawnio: LibreHardwareMonitor without its driver: no board sensors, no fan control, GPU still fine.
NOPAWNIO = dict(ASUS, fans=[], rpm=[], supported=False, temps=[("CPU", 61.4)],
    notice="Falta o driver PawnIO para ler os sensores e controlar os fans da placa-mãe. Feche e abra o FanPilot para instalá-lo (ou baixe em https://pawnio.eu).")

LAST_PROBE = {"result": None}  # filled by switcher.html?probe=1

FIXTURES = {"demo": DEMO, "worst": WORST, "unsupported": UNSUPPORTED, "down": DOWN, "turbo": TURBO, "asus": ASUS, "nopawnio": NOPAWNIO}


def snapshot(fx, t_ms=None, wobble=0.0):
    t_ms = t_ms or int(time.time() * 1000)
    snap = {"t": t_ms, "gpu": dict(fx["gpu"])}
    if fx.get("service_down"):
        snap["service"] = {"ok": False, "error": "EasyTuneEngineService indisponível: Falha ao se conectar a uma porta de IPC"}
        return snap
    temps = [(n, round(v + (wobble if -100 < v < 120 else 0), 1)) for n, v in fx["temps"]]
    snap["sensors"] = sensors(temps, [(f["title"], r) for f, r in zip(fx["fans"], fx["rpm"])], fx["volts"], fx["power"], fx["clocks"])
    snap["fanRpm"] = list(fx["rpm"])
    snap["tempInputs"] = [v for _, v in temps]
    snap["service"] = {"ok": True, "backend": fx.get("backend", "Gigabyte Control Center")}
    if fx.get("notice"):
        snap["service"]["notice"] = fx["notice"]
    if "temp" in snap["gpu"]:
        snap["gpu"]["temp"] = round(snap["gpu"]["temp"] + wobble / 2)
    return snap


def fans_payload(fx):
    return {"supported": fx.get("supported", True), "board": fx["board"], "turbo": fx.get("turbo", False),
            "backend": fx.get("backend", "Gigabyte Control Center"), "hardwareCurves": fx.get("hardwareCurves", True),
            "notice": fx.get("notice"),
            "tempInputs": [n for n, _ in fx["temps"]],
            "limits": {"minDutyPercent": 20, "fixedModeSafetyTemp": 85, "maxFullSpeedTemp": 90}, "fans": fx["fans"]}


def history(fx):
    # 10 minutes at 1 Hz with some movement and a 40 s gap (service hiccup), shaped like CompactHistory().
    n, now = 600, int(time.time() * 1000)
    t = [now - (n - i) * 1000 for i in range(n)]
    cpu = next((v for k, v in fx["temps"] if k == "CPU"), 50)
    series = {"temperature-514": [], "gpu-temp": []}
    for k in range(len(fx["rpm"])):
        series["fan-%d" % k] = []
    for i in range(n):
        gap = 300 <= i < 340
        w = math.sin(i / 25) * 6
        series["temperature-514"].append(None if gap else round(cpu - 8 + w + i / 60, 1))
        series["gpu-temp"].append(round(fx["gpu"].get("temp", 50) - 10 + w * 1.5, 1) if fx["gpu"].get("ok") else None)
        for k, r in enumerate(fx["rpm"]):
            series["fan-%d" % k].append(None if gap else max(0, round(r + (math.sin(i / 17 + k) * r * 0.08 if r > 1 else 0))))
    return {"t": t, "series": series}


class Handler(http.server.SimpleHTTPRequestHandler):
    # Module scripts load only with a JavaScript MIME type, and the Windows registry may map .js to text/plain.
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map,
                      ".js": "text/javascript", ".mjs": "text/javascript"}

    def __init__(self, *a, **k):
        super().__init__(*a, directory=str(WWW), **k)

    def log_message(self, *a):
        pass

    def fixture(self):
        c = SimpleCookie(self.headers.get("Cookie", ""))
        return FIXTURES.get(c["mock"].value if "mock" in c else "demo", DEMO)

    def send_json(self, obj, status=200):
        body = json.dumps(obj).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        path = self.path.split("?")[0]
        fx = self.fixture()
        if path == "/__mock":
            body = (HERE / "switcher.html").read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            return self.wfile.write(body)
        if path == "/__probe":
            return self.send_json(LAST_PROBE["result"])
        if path == "/__delay":
            ms = int(self.path.split("ms=")[-1]) if "ms=" in self.path else 0
            time.sleep(min(ms, 15000) / 1000)
            gif = bytes.fromhex("47494638396101000100800000000000ffffff21f90401000000002c00000000010001000002024401003b")
            self.send_response(200)
            self.send_header("Content-Type", "image/gif")
            self.send_header("Content-Length", str(len(gif)))
            self.end_headers()
            return self.wfile.write(gif)
        if path == "/api/state":
            return self.send_json(snapshot(fx))
        if path == "/api/history":
            return self.send_json(history(fx))
        if path == "/api/fans":
            if fx.get("service_down"):
                return self.send_json({"error": "EasyTuneEngineService indisponível: Falha ao se conectar a uma porta de IPC"}, 503)
            return self.send_json(fans_payload(fx))
        if path == "/api/stream":
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            try:
                for i in range(3600):
                    self.wfile.write(("data: " + json.dumps(snapshot(fx, wobble=math.sin(i / 5))) + "\n\n").encode())
                    self.wfile.flush()
                    time.sleep(1)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                pass
            return
        if path == "/":
            self.path = "/index.html"
        return super().do_GET()

    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length") or 0))
        if self.path == "/__probe":
            LAST_PROBE["result"] = json.loads(body)
            return self.send_json({"ok": True})
        fx = self.fixture()
        if self.path == "/api/gpu/fan":
            return self.send_json(fx["gpu"])
        if self.path == "/api/fans/turbo":
            return self.send_json({"turbo": not fx.get("turbo", False)})
        if self.path.startswith("/api/fans/"):
            return self.send_json(fx["fans"][0] if fx["fans"] else {})
        return self.send_json({"ok": True})


if __name__ == "__main__":
    print(f"Painel de teste: http://127.0.0.1:{PORT}/__mock")
    http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
