"""Desktop pairing and PlatformIO upload window for a Waveshare 8/8 MB board."""
import json
import os
import queue
import shutil
import subprocess
import sys
import threading
from pathlib import Path
from types import SimpleNamespace
import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from pair import pair, STATE, WINDOWS_DICTATION_MODEL

ROOT = Path(__file__).resolve().parents[1]
APPLE = "Mac + iPhone (iCloud)"
ANDROID = "PC + Galaxy A55 (Google / Microsoft)"
STOCK = "Pala 2.0 s vlastným OpenAI API kľúčom"


class Installer:
    def __init__(self, window):
        self.window = window
        self.messages = queue.Queue()
        self.busy = False
        self.bridge = None
        window.title("Pala Note 2.0 – inštalácia")
        box = ttk.Frame(window, padding=14)
        box.pack(fill="both", expand=True)
        self.box = box
        config = json.loads((STATE / "config.json").read_text()) if (STATE / "config.json").exists() else {}
        self.shopping_list = config.get("shopping", "Nákup")
        self.fields = {}
        default = ANDROID if config.get("destination") == "google" or os.name == "nt" else APPLE
        ttk.Label(box, text="Profil zariadenia").grid(row=0, column=0, sticky="w")
        self.profile = ttk.Combobox(box, values=[APPLE, ANDROID, STOCK], state="readonly", width=43)
        self.profile.set(default)
        self.profile.grid(row=0, column=1, sticky="ew")
        self.profile.bind("<<ComboboxSelected>>", self.profile_changed)
        self.add("IPv4 počítača", config.get("host", os.environ.get("PALA_INITIAL_IP", "")))
        if os.name == "nt":
            self.add("Model Local Dictation", config.get("transcription_model", WINDOWS_DICTATION_MODEL))
        else:
            self.add("Model Whisper (.bin)", config.get("model", os.environ.get("PALA_INITIAL_MODEL", "")))
            ttk.Button(box, text="Vybrať…", command=self.choose_model).grid(row=2, column=2)
        self.add("Predajňa – zemepisná šírka", str(config.get("lat", "")))
        self.add("Predajňa – zemepisná dĺžka", str(config.get("lon", "")))
        self.add("Kalendár (názov iCloud / ID Google)", config.get("calendar", "primary" if default == ANDROID else ""))
        self.add("Additional approved IPv4 networks (CIDR)",
                 ", ".join(config.get("discovery_networks", [])))
        ttk.Label(box, text="USB port zariadenia").grid(row=7, column=0, sticky="w")
        self.port = ttk.Combobox(box, state="readonly")
        self.port.grid(row=7, column=1, sticky="ew")
        ttk.Button(box, text="Obnoviť", command=self.ports).grid(row=7, column=2)
        actions = ttk.Frame(box); actions.grid(row=8, columnspan=3, pady=8)
        for label, fn in [("Zostaviť", lambda: self.run(False)), ("Nahrať cez USB", lambda: self.run(True)),
                          ("Spustiť prepojenie", self.start_bridge)]:
            ttk.Button(actions, text=label, command=fn).pack(side="left", padx=3)
        accounts = ttk.Frame(box); accounts.grid(row=10, columnspan=3)
        for label, provider in [("Pripojiť Google", "google"), ("Pripojiť Microsoft 365", "ms")]:
            ttk.Button(accounts, text=label, command=lambda p=provider: self.account(p)).pack(side="left", padx=4)
        ttk.Button(accounts, text="Zostaviť aplikáciu pre Samsung", command=self.android).pack(side="left", padx=4)
        self.log = tk.Text(box, width=76, height=12, state="disabled")
        self.log.grid(row=9, columnspan=3, sticky="nsew")
        box.columnconfigure(1, weight=1); box.rowconfigure(9, weight=1)
        self.ports()
        self.window.after(100, self.poll)

    def add(self, name, value):
        row = len(self.fields) + 1
        ttk.Label(self.box, text=name).grid(row=row, column=0, sticky="w")
        field = ttk.Entry(self.box)
        field.insert(0, value); field.grid(row=row, column=1, sticky="ew")
        self.fields[name] = field

    def android(self):
        if self.busy: return
        source = ROOT / "samsung"
        self.busy = True
        def build():
            try:
                command = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(ROOT / "Telefon.ps1")]
                subprocess.run(command, check=True, creationflags=subprocess.CREATE_NEW_CONSOLE)
                self.messages.put("APK je pripravené. Otvoril sa priečinok so súbormi pre mobil.")
            except Exception as exc: self.messages.put("Chyba: " + str(exc))
            finally: self.messages.put(None)
        threading.Thread(target=build, daemon=True).start()

    def account(self, provider):
        if self.busy: return
        if not (STATE / "config.json").exists():
            messagebox.showinfo("Pala", "Najprv použi Zostaviť v profile pre Galaxy."); return
        if json.loads((STATE / "config.json").read_text()).get("destination") != "google":
            messagebox.showinfo("Pala", "Najprv zostav profil pre Galaxy."); return
        name = "google-client.json" if provider == "google" else "microsoft.json"
        if not (STATE / name).exists():
            file = filedialog.askopenfilename(title="Vyber " + name, filetypes=[("JSON", "*.json")])
            if not file: return
            shutil.copyfile(file, STATE / name)
        self.busy = True
        def login():
            try: self.stream([sys.executable, str(ROOT / "mac-bridge/bridge.py"), "--" + provider + "-login"], ROOT)
            except Exception as exc: self.messages.put(str(exc))
            finally: self.messages.put(None)
        threading.Thread(target=login, daemon=True).start()

    def profile_changed(self, _):
        calendar = self.fields["Kalendár (názov iCloud / ID Google)"]
        if self.profile.get() == ANDROID and not calendar.get(): calendar.insert(0, "primary")
        elif self.profile.get() == APPLE and calendar.get() == "primary": calendar.delete(0, tk.END)

    def choose_model(self):
        file = filedialog.askopenfilename(filetypes=[("Whisper GGML", "*.bin")])
        if file:
            field = list(self.fields.values())[1]
            field.delete(0, tk.END); field.insert(0, file)

    def ports(self):
        try:
            from serial.tools import list_ports
            values = [p.device for p in list_ports.comports()]
            self.port["values"] = values
            if self.port.get() not in values: self.port.set(values[0] if len(values) == 1 else "")
        except ImportError:
            self.messages.put("USB vyžaduje inštaláciu PlatformIO (pozri návod).")

    def poll(self):
        while not self.messages.empty():
            msg = self.messages.get()
            if msg is None: self.busy = False; continue
            self.log.config(state="normal"); self.log.insert("end", msg + "\n")
            self.log.see("end"); self.log.config(state="disabled")
        self.window.after(100, self.poll)

    def stream(self, args, cwd):
        with subprocess.Popen(args, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                              text=True, errors="replace") as job:
            for line in job.stdout: self.messages.put(line.rstrip())
            if job.wait(): raise RuntimeError("Príkaz zlyhal. Podrobnosti sú vyššie.")

    def run(self, upload):
        if self.busy: return
        profile, port = self.profile.get(), self.port.get()
        if upload and not port: messagebox.showerror("USB", "Vyber port zariadenia."); return
        if upload and not messagebox.askyesno("Nahrať firmvér?", f"Pala Note 2.0, doska 8/8 MB\n{port}\n{profile}\nPrepísať firmvér?"): return
        ip, model, lat, lon, calendar, networks = [f.get().strip() for f in self.fields.values()]
        if profile != STOCK:
            from serial.tools import list_ports

            serial = next((entry.serial_number for entry in list_ports.comports() if entry.device == port), None)
            if not serial:
                messagebox.showerror("USB", "Select the connected Pala with a stable USB serial number.")
                return

        def work():
            try:
                if profile == APPLE and sys.platform != "darwin":
                    raise ValueError("iCloud prepojenie vyžaduje Mac.")
                project = STATE / ("build-stock" if profile == STOCK else "build-paired")
                project.mkdir(mode=0o700, parents=True, exist_ok=True)
                # Keep the compiled dependency cache, replace our managed source tree on updates.
                for folder in ("pala_note", "certs"):
                    if (project / folder).exists():
                        shutil.rmtree(project / folder)
                    shutil.copytree(ROOT / folder, project / folder, dirs_exist_ok=True,
                                    ignore=shutil.ignore_patterns("bridge_pairing.h", "secrets.h", "*.ino.cpp"))
                (project / "pala_note/secrets.h").write_text('#pragma once\n#define WIFI_SSID ""\n#define WIFI_PASS ""\n#define OPENAI_KEY ""\n')
                for name in ("platformio.ini", "partitions_pala_8MB.csv"):
                    shutil.copy2(ROOT / name, project / name)
                if profile != STOCK:
                    if not model or not calendar or (os.name != "nt" and not Path(model).is_file()):
                        raise ValueError("Vyber model Whisper a kalendár.")
                    pair(STATE, project / "pala_note/bridge_pairing.h",
                         SimpleNamespace(ip=ip, model=model, lat=float(lat), lon=float(lon),
                                         calendar=calendar, shopping=self.shopping_list, work="Práca", voice="Laura",
                                         timezone="Europe/Bratislava", destination="apple" if profile == APPLE else "google",
                                         device_id=serial, networks=[part.strip() for part in networks.split(",") if part.strip()]))
                    if profile == APPLE and not (STATE / "PalaApple.app/Contents/MacOS/PalaApple").exists():
                        self.stream(["sh", str(ROOT / "mac-bridge/build-helper.sh"), str(STATE / "PalaApple.app")], ROOT)
                cmd = [sys.executable, "-m", "platformio", "run", "-e", "pala_note_2"]
                self.stream(cmd + (["-t", "upload", "--upload-port", port] if upload else []), project)
                self.messages.put("Firmvér nahratý. Wi-Fi nastav cez obe tlačidlá a Web Companion." if upload else "Firmvér zostavený.")
            except Exception as exc: self.messages.put("Chyba: " + str(exc))
            finally: self.messages.put(None)
        self.busy = True
        threading.Thread(target=work, daemon=True).start()

    def start_bridge(self):
        if self.busy: return
        if self.profile.get() == STOCK:
            messagebox.showinfo("Pala", "Tento profil komunikuje priamo s OpenAI."); return
        if self.bridge and self.bridge.poll() is None: return
        self.bridge = subprocess.Popen([sys.executable, "-u", str(ROOT / "mac-bridge/bridge.py")],
                                       stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
        proc = self.bridge
        def read():
            for line in proc.stdout: self.messages.put(line.rstrip())
        threading.Thread(target=read, daemon=True).start()
        self.window.protocol("WM_DELETE_WINDOW", self.close)

    def close(self):
        if self.busy: return
        if self.bridge and self.bridge.poll() is None: self.bridge.terminate()
        self.window.destroy()


if __name__ == "__main__":
    os.umask(0o077)
    window = tk.Tk()
    Installer(window)
    window.mainloop()
