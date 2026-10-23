// SystemReport.cs - single-file Windows system report tool.
//
// Targets .NET Framework 4.x (built into Windows 10/11) and uses only C# 5 features,
// so it compiles with the csc.exe that ships with Windows. No SDK needed. See Build.bat.
//
// Usage:
//   SystemReport.exe                 interactive menu (double-click friendly)
//   SystemReport.exe -m              minimal, token-light text (copied to clipboard)
//   SystemReport.exe -j              JSON
//   SystemReport.exe -r              redact serial, MAC, IPs, SSID, username, PC name
//   SystemReport.exe -e              relaunch elevated (UAC) for TPM/BitLocker details
//   SystemReport.exe -o file.txt     choose the output file
//   SystemReport.exe --help          all options
//
// The report starts with a Summary section that lists real problems (low disk space,
// disabled protection, problem devices, pending reboot, ...), so it is useful at a
// glance as well as for pasting into an AI chat.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

[assembly: AssemblyTitle("SystemReport")]
[assembly: AssemblyDescription("Windows system information report")]
[assembly: AssemblyProduct("SystemReport")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace SystemReport
{
    // ===================================================================
    //  Config
    // ===================================================================
    static class Cfg
    {
        // Keep this in sync with AssemblyVersion/AssemblyFileVersion below and the
        // release tag (v1.1.0). Release.ps1 updates all three; build.ps1 checks the
        // source constants.
        public const string Version = "1.1.0";
        public static bool Redact;
        public static bool IsAdmin;

        // Problems and neutral observations found while collecting. They become
        // the Summary section and the "warnings" array in JSON output.
        public static readonly List<string> Warnings = new List<string>();
        public static readonly List<string> Notes = new List<string>();

        public static void Warn(string msg)
        {
            if (!string.IsNullOrEmpty(msg) && !Warnings.Contains(msg)) Warnings.Add(msg);
        }

        public static void Note(string msg)
        {
            if (!string.IsNullOrEmpty(msg) && !Notes.Contains(msg)) Notes.Add(msg);
        }

        public static void ResetHealth()
        {
            Warnings.Clear();
            Notes.Clear();
        }
    }

    // ===================================================================
    //  Data model: Report -> Sections -> Blocks -> Entries
    //  Collectors fill the model once; renderers turn it into text/JSON.
    // ===================================================================
    class Entry
    {
        public string Key;
        public string Value;
        public double? Pct;

        // Text form of the entry. With bar=true a usage bar is drawn for percentage entries.
        public string Text(bool bar)
        {
            if (!Pct.HasValue) return Value;
            string p = U.Pct(Pct.Value);
            string head = bar ? U.Bar(Pct.Value) + " " + p : p;
            return string.IsNullOrEmpty(Value) ? head : head + "  " + Value;
        }
    }

    class Block
    {
        public string Name;
        public List<Entry> Items = new List<Entry>();

        public Block Add(string key, string value)
        {
            return Add(key, value, null, false);
        }

        // Personal data (serial, MAC, IP, user...). Replaced when --redact is on.
        public Block Secret(string key, string value)
        {
            return Add(key, value, null, true);
        }

        public Block AddPct(string key, double pct, string value)
        {
            return Add(key, value, pct, false);
        }

        Block Add(string key, string value, double? pct, bool sensitive)
        {
            if (string.IsNullOrWhiteSpace(value) && !pct.HasValue) value = "N/A";
            else if (sensitive && Cfg.Redact) value = "[redacted]";

            // keep keys unique so JSON output stays valid
            string k = key ?? "";
            string cand = k;
            int n = 1;
            while (Items.Any(x => x.Key == cand)) { n++; cand = k + " (" + n + ")"; }

            Items.Add(new Entry { Key = cand, Value = value, Pct = pct });
            return this;
        }
    }

    class Section
    {
        public string Title;
        public List<Block> Blocks = new List<Block>();
        public List<string> Brief = new List<string>();   // one-liners for --minimal

        public Section(string title) { Title = title; }

        public Block G() { return G(null); }

        public Block G(string name)
        {
            string n = name;
            if (!string.IsNullOrEmpty(n))
            {
                string cand = n;
                int c = 1;
                while (Blocks.Any(b => b.Name == cand)) { c++; cand = n + " (" + c + ")"; }
                n = cand;
            }
            Block blk = new Block { Name = n };
            Blocks.Add(blk);
            return blk;
        }
    }

    class Report
    {
        public string Computer;
        public string Generated;
        public bool IsAdmin;
        public double DurationSec;
        public List<Section> Sections = new List<Section>();
    }

    // ===================================================================
    //  Small helpers
    // ===================================================================
    static class U
    {
        public static readonly CultureInfo IC = CultureInfo.InvariantCulture;

        public static string Size(double b)
        {
            const double KB = 1024.0;
            const double MB = KB * 1024.0;
            const double GB = MB * 1024.0;
            const double TB = GB * 1024.0;
            if (b >= TB) return (b / TB).ToString("0.##", IC) + " TB";
            if (b >= GB) return (b / GB).ToString("0.##", IC) + " GB";
            if (b >= MB) return (b / MB).ToString("0.#", IC) + " MB";
            return (b / KB).ToString("0", IC) + " KB";
        }

        public static string N(double d) { return d.ToString("0", IC); }

        public static string Pct(double p) { return ((int)Math.Round(p)).ToString(IC) + "%"; }

        public static string Bar(double pct)
        {
            const int len = 20;
            int f = (int)Math.Round(pct / 100.0 * len);
            if (f < 0) f = 0;
            if (f > len) f = len;
            return "[" + new string('#', f) + new string('-', len - f) + "]";
        }

        public static string Uptime(TimeSpan t) { return t.Days + "d " + t.Hours + "h " + t.Minutes + "m"; }

        public static string UptimeShort(TimeSpan t) { return t.Days + "d" + t.Hours + "h"; }

        public static string Squash(string s)
        {
            if (s == null) return null;
            return string.Join(" ", s.Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        public static string Cat(params string[] parts)
        {
            return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToArray());
        }

        public static string OnOff(bool? b)
        {
            if (!b.HasValue) return "Unknown";
            return b.Value ? "On" : "Off";
        }

        public static string Date(DateTime? d)
        {
            return d.HasValue ? d.Value.ToString("dd MMM yyyy", IC) : null;
        }

        // Hardware vendors fill missing serials with placeholders. Treat those as unknown.
        public static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            string t = s.Trim();
            string l = t.ToLowerInvariant();
            if (l == "none" || l == "n/a" || l == "unknown" || l == "0" || l == "not applicable" ||
                l.IndexOf("to be filled", StringComparison.Ordinal) >= 0 ||
                l.IndexOf("default string", StringComparison.Ordinal) >= 0 ||
                l.IndexOf("system serial", StringComparison.Ordinal) >= 0 ||
                l.IndexOf("base board serial", StringComparison.Ordinal) >= 0 ||
                l.IndexOf("chassis serial", StringComparison.Ordinal) >= 0 ||
                l.IndexOf("not specified", StringComparison.Ordinal) >= 0 ||
                l.IndexOf("o.e.m", StringComparison.Ordinal) >= 0 ||
                l.IndexOf("invalid", StringComparison.Ordinal) >= 0)
                return null;
            return t;
        }

        public static string Shorten(string s, int max)
        {
            if (s == null) return null;
            string t = Squash(s);
            if (max < 8) max = 8;
            if (t.Length <= max) return t;
            return t.Substring(0, max - 3).TrimEnd() + "...";
        }

        // Keep commands readable without exposing the user's profile path.
        public static string MaskUser(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) s = ReplaceIgnoreCase(s, profile, "%USERPROFILE%");
            return s;
        }

        static string ReplaceIgnoreCase(string s, string find, string repl)
        {
            int i = s.IndexOf(find, StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                s = s.Substring(0, i) + repl + s.Substring(i + find.Length);
                i = s.IndexOf(find, i + repl.Length, StringComparison.OrdinalIgnoreCase);
            }
            return s;
        }

        // Value of "Key: value" in tool output. Leading '-' (tpmtool) is ignored.
        public static string LineValue(string text, string key)
        {
            if (string.IsNullOrEmpty(text)) return null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int i = line.IndexOf(':');
                if (i <= 0) continue;
                string k = line.Substring(0, i).Trim().TrimStart('\uFEFF', '-').Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(i + 1).Trim();
            }
            return null;
        }

        // ---- registry (always the 64-bit view) ----
        static RegistryKey OpenHklm()
        {
            try { return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64); }
            catch { return Registry.LocalMachine; }
        }

        public static object RegGet(string path, string name)
        {
            return RegGet(RegistryHive.LocalMachine, RegistryView.Registry64, path, name);
        }

        public static object RegGet(RegistryHive hive, RegistryView view, string path, string name)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey k = b.OpenSubKey(path))
                {
                    if (k == null) return null;
                    return k.GetValue(name);
                }
            }
            catch { return null; }
        }

        public static string RegStr(string path, string name)
        {
            object v = RegGet(path, name);
            return v == null ? null : v.ToString();
        }

        public static bool RegKeyExists(string path)
        {
            try
            {
                using (RegistryKey b = OpenHklm())
                using (RegistryKey k = b.OpenSubKey(path))
                    return k != null;
            }
            catch { return false; }
        }

        static double ToNum(object q)
        {
            if (q == null) return 0;
            byte[] bytes = q as byte[];
            if (bytes != null)
            {
                byte[] buf = new byte[8];
                Array.Copy(bytes, buf, Math.Min(8, bytes.Length));
                return (double)BitConverter.ToUInt64(buf, 0);
            }
            try { return Convert.ToDouble(q, IC); }
            catch { return 0; }
        }

        // WMI's AdapterRAM is 32-bit (caps at 4 GB). The GPU driver stores the real
        // 64-bit size in the registry, so read it from there. Returns 0 if unknown.
        public static double GpuVram(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            const string basePath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            try
            {
                using (RegistryKey b = OpenHklm())
                using (RegistryKey cls = b.OpenSubKey(basePath))
                {
                    if (cls == null) return 0;
                    foreach (string sub in cls.GetSubKeyNames())
                    {
                        if (sub.Length != 4) continue;
                        using (RegistryKey k = cls.OpenSubKey(sub))
                        {
                            if (k == null) continue;
                            object d = k.GetValue("DriverDesc");
                            if (d == null || !string.Equals(d.ToString(), name, StringComparison.OrdinalIgnoreCase)) continue;
                            double val = ToNum(k.GetValue("HardwareInformation.qwMemorySize"));
                            if (val > 0) return val;
                        }
                    }
                }
            }
            catch { }
            return 0;
        }

        // Runs a console program and returns its output (null on failure/timeout).
        public static string Run(string exe, string args)
        {
            return Run(exe, args, 8000);
        }

        public static string Run(string exe, string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                try { psi.StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { }

                using (Process p = Process.Start(psi))
                {
                    if (p == null) return null;

                    StringBuilder output = new StringBuilder();
                    p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) output.AppendLine(e.Data);
                    };
                    p.BeginOutputReadLine();

                    // Drain stderr too, otherwise a chatty child can block on a full pipe.
                    p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { };
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return null;
                    }
                    p.WaitForExit(); // flush any remaining async output
                    return output.ToString();
                }
            }
            catch { return null; }
        }
    }
    // ===================================================================
    //  WMI helpers - every call is failure-tolerant
    // ===================================================================
    static class W
    {
        public static List<ManagementObject> Q(string query, string ns = @"root\cimv2")
        {
            List<ManagementObject> list = new List<ManagementObject>();
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(ns, query))
                {
                    // A sick provider must not hang the whole report.
                    try { s.Options.Timeout = TimeSpan.FromSeconds(10); } catch { }
                    foreach (ManagementObject o in s.Get()) list.Add(o);
                }
            }
            catch { }
            return list;
        }

        public static ManagementObject First(string query, string ns = @"root\cimv2")
        {
            return Q(query, ns).FirstOrDefault();
        }

        public static string S(ManagementBaseObject o, string p)
        {
            try
            {
                if (o == null) return null;
                object v = o[p];
                if (v == null) return null;
                string s = v.ToString().Trim();
                return s.Length == 0 ? null : s;
            }
            catch { return null; }
        }

        public static double D(ManagementBaseObject o, string p)
        {
            try
            {
                if (o == null) return 0;
                object v = o[p];
                return v == null ? 0 : Convert.ToDouble(v, U.IC);
            }
            catch { return 0; }
        }

        public static bool? B(ManagementBaseObject o, string p)
        {
            try
            {
                if (o == null) return null;
                object v = o[p];
                if (v == null) return null;
                return Convert.ToBoolean(v);
            }
            catch { return null; }
        }

        public static DateTime? Dt(ManagementBaseObject o, string p)
        {
            try
            {
                string s = S(o, p);
                if (s == null) return null;
                return ManagementDateTimeConverter.ToDateTime(s);
            }
            catch { return null; }
        }

        // WmiMonitorID exposes names as arrays of Unicode code units.
        public static string U16(ManagementBaseObject o, string p)
        {
            try
            {
                if (o == null) return null;
                ushort[] a = o[p] as ushort[];
                if (a == null) return null;
                StringBuilder sb = new StringBuilder();
                foreach (ushort c in a)
                {
                    if (c == 0) break;
                    sb.Append((char)c);
                }
                string s = sb.ToString().Trim();
                return s.Length == 0 ? null : s;
            }
            catch { return null; }
        }

        public static uint[] Arr(ManagementBaseObject o, string p)
        {
            try
            {
                if (o == null) return new uint[0];
                uint[] a = o[p] as uint[];
                return a ?? new uint[0];
            }
            catch { return new uint[0]; }
        }
    }

    // ===================================================================
    //  Collectors - one per section
    // ===================================================================
    static class Collectors
    {
        // ---------- Summary ----------
        public static Section GetSummary()
        {
            Section s = new Section("Summary");
            Block g = s.G();
            int w = Cfg.Warnings.Count;
            int n = Cfg.Notes.Count;

            string status;
            if (w == 0) status = "OK - no issues detected";
            else status = w + (w == 1 ? " warning" : " warnings");
            if (n > 0) status += ", " + n + (n == 1 ? " note" : " notes");
            g.Add("Status", status);

            foreach (string msg in Cfg.Warnings) g.Add("Warning", msg);
            foreach (string msg in Cfg.Notes) g.Add("Note", msg);

            if (w == 0) s.Brief.Add("Health: OK");
            else s.Brief.Add("Health: " + w + (w == 1 ? " warning" : " warnings") + " - " + string.Join("; ", Cfg.Warnings.ToArray()));
            foreach (string msg in Cfg.Notes) s.Brief.Add("Note: " + msg);
            return s;
        }

        // ---------- Operating system ----------
        public static Section GetOs()
        {
            Section s = new Section("Operating System");
            Block g = s.G();
            ManagementObject os = W.First("SELECT * FROM Win32_OperatingSystem");
            const string cvp = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

            string ver = U.RegStr(cvp, "DisplayVersion") ?? U.RegStr(cvp, "ReleaseId");
            string ubr = U.RegStr(cvp, "UBR");
            string build = W.S(os, "BuildNumber");
            string buildFull = ubr != null ? build + "." + ubr : build;
            string name = W.S(os, "Caption");
            if (name != null) name = name.Replace("Microsoft ", "");
            string arch = W.S(os, "OSArchitecture");
            DateTime? boot = W.Dt(os, "LastBootUpTime");
            DateTime? inst = W.Dt(os, "InstallDate");
            TimeSpan up = boot.HasValue ? DateTime.Now - boot.Value : TimeSpan.Zero;
            if (up < TimeSpan.Zero) up = TimeSpan.Zero;

            g.Add("Edition", name);
            g.Add("Version", (ver ?? "?") + " (build " + buildFull + ")");
            g.Add("Architecture", arch);
            g.Add("Installed on", U.Date(inst));
            g.Add("Last boot", boot.HasValue ? boot.Value.ToString("dd MMM yyyy HH:mm", U.IC) : null);
            g.Add("Uptime", U.Uptime(up));
            g.Add("Time zone", TimeZoneInfo.Local.DisplayName);
            g.Secret("Current user", Environment.UserDomainName + "\\" + Environment.UserName);

            if (up.TotalDays >= 30)
                Cfg.Note("Uptime is " + (int)up.TotalDays + " days - a reboot may be due");

            s.Brief.Add("OS: " + name + " " + ver + " build " + buildFull + " " + arch + ", up " + U.UptimeShort(up));
            return s;
        }

        // ---------- Machine & firmware ----------
        public static Section GetMachine()
        {
            Section s = new Section("Machine & Firmware");
            Block g = s.G();
            ManagementObject cs = W.First("SELECT * FROM Win32_ComputerSystem");
            ManagementObject bios = W.First("SELECT * FROM Win32_BIOS");
            ManagementObject bb = W.First("SELECT * FROM Win32_BaseBoard");

            string mfr = W.S(cs, "Manufacturer");
            string model = W.S(cs, "Model");
            string biosVer = W.S(bios, "SMBIOSBIOSVersion");
            string serial = U.Clean(W.S(bios, "SerialNumber"));

            g.Add("Manufacturer", mfr);
            g.Add("Model", model);
            g.Add("System type", W.S(cs, "SystemType"));
            g.Secret("Serial number", serial);
            g.Add("Motherboard", U.Cat(W.S(bb, "Manufacturer"), W.S(bb, "Product")));
            g.Add("BIOS version", biosVer);
            g.Add("BIOS date", U.Date(W.Dt(bios, "ReleaseDate")));
            g.Add("Secure Boot", SecureBoot());
            g.Add("TPM", Tpm());

            s.Brief.Add("PC: " + U.Cat(mfr, model) + ", BIOS " + biosVer);
            return s;
        }

        static string SecureBoot()
        {
            object v = U.RegGet(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
            if (v == null) return "Not supported (legacy BIOS) or unreadable";
            try
            {
                bool on = Convert.ToInt32(v) == 1;
                if (!on) Cfg.Warn("Secure Boot is disabled");
                return on ? "Enabled" : "Disabled";
            }
            catch { return "Unknown"; }
        }

        static string Tpm()
        {
            // tpmtool works without elevation and is much faster than Win32_Tpm.
            string o = U.Run("tpmtool", "getdeviceinformation", 5000);
            if (o != null)
            {
                string present = U.LineValue(o, "TPM Present");
                if (string.Equals(present, "False", StringComparison.OrdinalIgnoreCase))
                    return "Not present";
                if (string.Equals(present, "True", StringComparison.OrdinalIgnoreCase))
                {
                    string ver = U.LineValue(o, "TPM Version");
                    string mfr = U.LineValue(o, "TPM Manufacturer ID");
                    string init = U.LineValue(o, "Is Initialized");
                    string txt = "Present" + (ver != null ? ", v" + ver : "") + (mfr != null ? ", " + mfr : "");
                    if (string.Equals(init, "True", StringComparison.OrdinalIgnoreCase)) txt += ", initialized";
                    return txt;
                }
            }

            ManagementObject t = W.First("SELECT * FROM Win32_Tpm", @"root\CIMV2\Security\MicrosoftTpm");
            if (t == null) return Cfg.IsAdmin ? "Not detected" : "Unknown (run as administrator)";
            string spec = W.S(t, "SpecVersion");
            if (spec != null)
            {
                int i = spec.IndexOf(',');
                if (i > 0) spec = spec.Substring(0, i);
            }
            bool? en = W.B(t, "IsEnabled_InitialValue");
            return "Present, spec " + (spec ?? "?") + (en == true ? ", enabled" : ", disabled");
        }

        // ---------- Processor ----------
        public static Section GetCpu()
        {
            Section s = new Section("Processor");
            List<ManagementObject> cpus = W.Q("SELECT * FROM Win32_Processor");
            for (int i = 0; i < cpus.Count; i++)
            {
                ManagementObject c = cpus[i];
                Block g = s.G(cpus.Count > 1 ? "Socket " + (i + 1) : null);
                string name = U.Squash(W.S(c, "Name"));
                string cores = U.N(W.D(c, "NumberOfCores"));
                string threads = U.N(W.D(c, "NumberOfLogicalProcessors"));
                double l2 = W.D(c, "L2CacheSize") / 1024.0;
                double l3 = W.D(c, "L3CacheSize") / 1024.0;
                double max = W.D(c, "MaxClockSpeed");
                double cur = W.D(c, "CurrentClockSpeed");

                g.Add("Name", name);
                g.Add("Cores / Threads", cores + " / " + threads);
                if (cur > 0 && max > 0 && Math.Abs(cur - max) > 1)
                    g.Add("Clock", (cur / 1000.0).ToString("0.00", U.IC) + " GHz (max " + (max / 1000.0).ToString("0.00", U.IC) + " GHz)");
                else
                    g.Add("Max clock", (max / 1000.0).ToString("0.00", U.IC) + " GHz");
                g.Add("L2 / L3 cache", l2.ToString("0.#", U.IC) + " MB / " + l3.ToString("0.#", U.IC) + " MB");
                g.Add("Virtualization", U.OnOff(W.B(c, "VirtualizationFirmwareEnabled")));
                g.AddPct("Current load", W.D(c, "LoadPercentage"), null);

                if (i == 0) s.Brief.Add("CPU: " + name + ", " + cores + "C/" + threads + "T");
            }
            return s;
        }

        // ---------- Memory ----------
        static string MemType(int t)
        {
            switch (t)
            {
                case 20: return "DDR";
                case 21: return "DDR2";
                case 24: return "DDR3";
                case 26: return "DDR4";
                case 27: return "LPDDR";
                case 28: return "LPDDR2";
                case 29: return "LPDDR3";
                case 30: return "LPDDR4";
                case 34: return "DDR5";
                case 35: return "LPDDR5";
                default: return "RAM";
            }
        }

        public static Section GetMemory()
        {
            Section s = new Section("Memory");
            Block g = s.G();
            ManagementObject cs = W.First("SELECT * FROM Win32_ComputerSystem");
            ManagementObject os = W.First("SELECT * FROM Win32_OperatingSystem");
            ManagementObject arr = W.First("SELECT * FROM Win32_PhysicalMemoryArray");
            List<ManagementObject> mods = W.Q("SELECT * FROM Win32_PhysicalMemory");

            double total = W.D(cs, "TotalPhysicalMemory");
            double free = W.D(os, "FreePhysicalMemory") * 1024.0;
            double used = Math.Max(0, total - free);
            double pct = total > 0 ? used / total * 100.0 : 0;

            g.Add("Total", U.Size(total));
            g.AddPct("In use", pct, U.Size(used));
            g.Add("Available", U.Size(free));
            g.Add("Slots", mods.Count + " used of " + (arr != null ? U.N(W.D(arr, "MemoryDevices")) : "?"));
            double maxCap = W.D(arr, "MaxCapacity") * 1024.0;
            if (maxCap > 0) g.Add("Max capacity", U.Size(maxCap));

            ManagementObject pf = W.First("SELECT * FROM Win32_PageFileUsage");
            if (pf != null)
            {
                double allocMb = W.D(pf, "AllocatedBaseSize");
                double curMb = W.D(pf, "CurrentUsage");
                if (allocMb > 0)
                {
                    bool auto = W.B(cs, "AutomaticManagedPagefile") == true;
                    g.Add("Page file", U.Size(curMb * 1024 * 1024) + " used of " + U.Size(allocMb * 1024 * 1024) + (auto ? " (system managed)" : ""));
                    if (curMb / allocMb >= 0.9) Cfg.Warn("Page file is nearly full (" + U.Pct(curMb / allocMb * 100) + ")");
                }
            }

            if (pct >= 90 && total > 0) Cfg.Warn("Memory usage is high (" + U.Pct(pct) + " in use)");

            string firstType = null;
            string firstSpeed = null;
            if (mods.Count > 0)
            {
                Block mg = s.G("Modules");
                for (int i = 0; i < mods.Count; i++)
                {
                    ManagementObject m = mods[i];
                    string type = MemType((int)W.D(m, "SMBIOSMemoryType"));
                    double spd = W.D(m, "ConfiguredClockSpeed");
                    if (spd <= 0) spd = W.D(m, "Speed");
                    string speed = spd > 0 ? U.N(spd) + " MHz" : "? MHz";
                    if (firstType == null) { firstType = type; firstSpeed = spd > 0 ? U.N(spd) + "MHz" : null; }

                    string slot = W.S(m, "DeviceLocator");
                    string bank = W.S(m, "BankLabel");
                    string label = !string.IsNullOrEmpty(bank) ? bank : slot;
                    if (string.IsNullOrEmpty(label)) label = "Slot " + (i + 1);
                    if (!string.IsNullOrEmpty(bank) && !string.IsNullOrEmpty(slot) &&
                        !string.Equals(bank, slot, StringComparison.OrdinalIgnoreCase))
                        label += " (" + slot + ")";

                    mg.Add(label, U.Size(W.D(m, "Capacity")) + "  " + type + "  " + speed + "  (" + (W.S(m, "Manufacturer") ?? "unknown") + ")");
                }
            }

            string detail = firstType == null ? "" : " (" + firstType + (firstSpeed != null ? " " + firstSpeed : "") + " x" + mods.Count + ")";
            s.Brief.Add("RAM: " + U.Size(total) + detail + ", " + U.Pct(pct) + " used");
            return s;
        }
        // ---------- Graphics ----------
        public static Section GetGpu()
        {
            Section s = new Section("Graphics");
            foreach (ManagementObject gpu in W.Q("SELECT * FROM Win32_VideoController"))
            {
                string name = W.S(gpu, "Name");
                string drv = W.S(gpu, "DriverVersion");
                Block g = s.G(name);

                double vram = U.GpuVram(name);
                double wmiRam = W.D(gpu, "AdapterRAM");
                string vramTxt = null;
                string vramShort = null;
                if (vram > 0) { vramTxt = U.Size(vram); vramShort = vramTxt; }
                else if (wmiRam > 0) { vramShort = U.Size(wmiRam); vramTxt = vramShort + "  (WMI value, may be capped at 4 GB)"; }

                g.Add("Driver version", drv);
                g.Add("Driver date", U.Date(W.Dt(gpu, "DriverDate")));
                g.Add("VRAM (dedicated)", vramTxt);
                double hr = W.D(gpu, "CurrentHorizontalResolution");
                if (hr > 0)
                    g.Add("Resolution", U.N(hr) + " x " + U.N(W.D(gpu, "CurrentVerticalResolution")) + " @ " + U.N(W.D(gpu, "CurrentRefreshRate")) + " Hz");

                string status = W.S(gpu, "Status");
                if (status != null && !string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    g.Add("Status", status);
                    Cfg.Warn("GPU " + (name ?? "adapter") + " reports " + status);
                }

                s.Brief.Add("GPU: " + name + (vramShort != null ? ", " + vramShort + " VRAM" : "") + ", driver " + drv);
            }

            if (s.Blocks.Count == 0) s.G().Add("Status", "No display adapter detected");
            AddMonitors(s);
            return s;
        }

        static void AddMonitors(Section s)
        {
            List<ManagementObject> ids = W.Q("SELECT * FROM WmiMonitorID", @"root\wmi");
            if (ids.Count == 0) return;
            List<ManagementObject> basic = W.Q("SELECT * FROM WmiMonitorBasicDisplayParams", @"root\wmi");

            for (int i = 0; i < ids.Count; i++)
            {
                ManagementObject id = ids[i];
                string name = W.U16(id, "UserFriendlyName");
                string mfr = W.U16(id, "ManufacturerName");
                string serial = W.U16(id, "SerialNumberID");
                double year = W.D(id, "YearOfManufacture");
                double week = W.D(id, "WeekOfManufacture");
                string instance = W.S(id, "InstanceName");

                double hcm = 0, vcm = 0;
                foreach (ManagementObject b in basic)
                {
                    if (string.Equals(W.S(b, "InstanceName"), instance, StringComparison.OrdinalIgnoreCase))
                    {
                        hcm = W.D(b, "MaxHorizontalImageSize");
                        vcm = W.D(b, "MaxVerticalImageSize");
                        break;
                    }
                }
                double inches = (hcm > 0 && vcm > 0) ? Math.Sqrt(hcm * hcm + vcm * vcm) / 2.54 : 0;

                string label = name ?? ("Monitor " + (i + 1));
                Block g = s.G("Monitor: " + label);
                g.Add("Manufacturer", mfr);
                if (inches > 0)
                    g.Add("Size", inches.ToString("0.#", U.IC) + " in (" + U.N(hcm) + " x " + U.N(vcm) + " cm)");
                if (year > 0)
                    g.Add("Manufactured", U.N(year) + (week > 0 ? " week " + U.N(week) : ""));
                g.Secret("Serial number", serial);

                s.Brief.Add("Monitor: " + label + (inches > 0 ? " " + inches.ToString("0.#", U.IC) + "in" : ""));
            }
        }

        // ---------- Storage ----------
        static string MediaType(int t)
        {
            switch (t) { case 3: return "HDD"; case 4: return "SSD"; case 5: return "SCM"; default: return "Unspecified"; }
        }

        static string BusType(int t)
        {
            switch (t)
            {
                case 1: return "SCSI"; case 3: return "ATA"; case 7: return "USB"; case 8: return "RAID";
                case 10: return "SAS"; case 11: return "SATA"; case 12: return "SD"; case 13: return "MMC";
                case 14: case 15: return "Virtual"; case 17: return "NVMe";
                default: return "Bus " + t;
            }
        }

        static string Health(int t)
        {
            switch (t) { case 0: return "Healthy"; case 1: return "Warning"; case 2: return "Unhealthy"; default: return "Unknown"; }
        }

        public static Section GetStorage()
        {
            Section s = new Section("Storage");

            Block pd = s.G("Physical disks");
            List<ManagementObject> disks = W.Q("SELECT * FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage");
            if (disks.Count > 0)
            {
                foreach (ManagementObject d in disks)
                {
                    string name = W.S(d, "FriendlyName");
                    string size = U.Size(W.D(d, "Size"));
                    string media = MediaType((int)W.D(d, "MediaType"));
                    string bus = BusType((int)W.D(d, "BusType"));
                    string health = Health((int)W.D(d, "HealthStatus"));
                    pd.Add(name ?? "Disk", size + "  |  " + media + "  |  " + bus + "  |  " + health);
                    s.Brief.Add("Disk: " + name + ", " + size + ", " + media + ", " + health);
                    if (health != "Healthy" && health != "Unknown")
                        Cfg.Warn("Disk " + (name ?? "?") + " reports " + health.ToLowerInvariant());
                }
            }
            else
            {
                foreach (ManagementObject d in W.Q("SELECT * FROM Win32_DiskDrive"))
                {
                    string name = W.S(d, "Model");
                    string size = U.Size(W.D(d, "Size"));
                    string status = W.S(d, "Status");
                    pd.Add(name ?? "Disk", size + "  |  " + (W.S(d, "InterfaceType") ?? "?") + (status != null ? "  |  " + status : ""));
                    s.Brief.Add("Disk: " + name + ", " + size);
                    if (status != null && !string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase))
                        Cfg.Warn("Disk " + (name ?? "?") + " reports " + status);
                }
            }
            if (pd.Items.Count == 0) pd.Add("Status", "No physical disks detected");

            Block vg = s.G("Volumes");
            foreach (DriveInfo di in DriveInfo.GetDrives())
            {
                try
                {
                    if (di.DriveType != DriveType.Fixed && di.DriveType != DriveType.Removable) continue;
                    if (!di.IsReady) continue;
                    double tot = di.TotalSize;
                    double fr = di.AvailableFreeSpace;
                    if (tot <= 0) continue;
                    double pct = (tot - fr) / tot * 100.0;
                    double freePct = 100.0 - pct;
                    string letter = di.Name.TrimEnd('\\');
                    string label = string.IsNullOrEmpty(di.VolumeLabel) ? "No label" : di.VolumeLabel;
                    if (Cfg.Redact) label = "Volume";
                    vg.AddPct(letter + " " + label, pct, U.Size(fr) + " free of " + U.Size(tot) + " (" + di.DriveFormat + ")");
                    s.Brief.Add("Vol: " + letter + " " + U.Size(tot) + " " + di.DriveFormat + ", " + U.Pct(pct) + " used");
                    if (freePct <= 10)
                        Cfg.Warn("Low free space on " + letter + " (" + U.Pct(freePct) + " free, " + U.Size(fr) + ")");
                }
                catch { }
            }
            if (vg.Items.Count == 0) vg.Add("Status", "No ready fixed/removable volumes");
            return s;
        }

        // ---------- Devices ----------
        static string Problem(int code)
        {
            switch (code)
            {
                case 1: return "device is not configured correctly";
                case 3: return "driver is not installed";
                case 10: return "device cannot start";
                case 12: return "not enough free resources";
                case 14: return "device needs a restart";
                case 16: return "device is not fully detected";
                case 18: return "reinstall the drivers";
                case 19: return "registry problem";
                case 21: return "Windows is removing the device";
                case 22: return "device is disabled";
                case 24: return "device is not present";
                case 28: return "drivers are not installed";
                case 31: return "device is not working properly";
                case 43: return "Windows stopped the device (code 43)";
                case 45: return "device is not connected";
                default: return "device error";
            }
        }

        public static Section GetDevices()
        {
            Section s = new Section("Devices");
            Block g = s.G("Problem devices");
            List<ManagementObject> bad = W.Q("SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            if (bad.Count == 0)
            {
                g.Add("Status", "None detected");
                s.Brief.Add("Devices: no problems");
            }
            else
            {
                foreach (ManagementObject o in bad)
                {
                    string name = W.S(o, "Name") ?? W.S(o, "DeviceID") ?? "Unknown device";
                    int code = (int)W.D(o, "ConfigManagerErrorCode");
                    g.Add(name, "Code " + code + " - " + Problem(code));
                    // 22 = disabled, 24 = not present, 45 = not connected. These are
                    // frequently intentional, so list them but do not raise a warning.
                    if (code != 22 && code != 24 && code != 45)
                        Cfg.Warn("Device problem: " + name + " (code " + code + ")");
                }
                s.Brief.Add("Devices: " + bad.Count + " with issues");
            }
            return s;
        }
        // ---------- Network ----------
        static bool IsV4(IPAddress a)
        {
            return a != null && a.AddressFamily == AddressFamily.InterNetwork;
        }

        static string Speed(long bitsPerSec)
        {
            if (bitsPerSec <= 0) return null;
            if (bitsPerSec >= 1000000000L) return (bitsPerSec / 1e9).ToString("0.##", U.IC) + " Gbps";
            return (bitsPerSec / 1e6).ToString("0.##", U.IC) + " Mbps";
        }

        static bool IsVirtual(NetworkInterfaceType t, string desc, string name)
        {
            if (t == NetworkInterfaceType.Tunnel) return true;
            string s = ((desc ?? "") + " " + (name ?? "")).ToLowerInvariant();
            string[] keys = { "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "loopback",
                              "tap-", "tap adapter", "vpn", "wireguard", "tailscale", "zerotier",
                              "bluetooth", "npcap", "pseudo", "wan miniport", "teredo", "isatap" };
            foreach (string k in keys)
                if (s.IndexOf(k, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static bool LooksLikeMac(string v)
        {
            if (v == null || (v.Length != 17 && v.Length != 12)) return false;
            int digits = 0, seps = 0;
            foreach (char c in v)
            {
                if (Uri.IsHexDigit(c)) digits++;
                else if (c == ':' || c == '-') seps++;
                else return false;
            }
            return digits == 12 && (v.Length == 12 || seps == 5);
        }

        class NetAdapter
        {
            public string Name;
            public string Description;
            public string Type;
            public string Mac;
            public string Speed;
            public string Ips;
            public string Gateway;
            public string Dns;
            public string DnsSuffix;
            public string Ipv6;
            public bool Dhcp;
            public bool Virtual;
        }

        public static Section GetNetwork()
        {
            Section s = new Section("Network");
            List<NetAdapter> list = new List<NetAdapter>();
            bool hasWifi = false;

            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    NetworkInterfaceType t = ni.NetworkInterfaceType;
                    if (t == NetworkInterfaceType.Loopback || t == NetworkInterfaceType.Tunnel) continue;

                    IPInterfaceProperties ip = ni.GetIPProperties();
                    List<string> v4 = new List<string>();
                    List<string> v6 = new List<string>();
                    foreach (UnicastIPAddressInformation u in ip.UnicastAddresses)
                    {
                        if (IsV4(u.Address)) v4.Add(u.Address.ToString());
                        else if (u.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                                 !u.Address.IsIPv6LinkLocal && !u.Address.IsIPv6SiteLocal)
                            v6.Add(u.Address.ToString());
                    }
                    if (v4.Count == 0 && v6.Count == 0) continue;

                    List<string> gw = new List<string>();
                    foreach (GatewayIPAddressInformation gi in ip.GatewayAddresses)
                        if (IsV4(gi.Address)) gw.Add(gi.Address.ToString());

                    List<string> dns = new List<string>();
                    foreach (IPAddress d in ip.DnsAddresses)
                        if (IsV4(d)) dns.Add(d.ToString());

                    string type = t == NetworkInterfaceType.Wireless80211 ? "Wi-Fi"
                                : t == NetworkInterfaceType.Ethernet ? "Ethernet"
                                : t.ToString();
                    if (t == NetworkInterfaceType.Wireless80211) hasWifi = true;

                    NetAdapter a = new NetAdapter();
                    a.Name = ni.Name;
                    a.Description = ni.Description;
                    a.Type = type;
                    a.Virtual = IsVirtual(t, ni.Description, ni.Name);
                    byte[] mb = ni.GetPhysicalAddress().GetAddressBytes();
                    a.Mac = mb.Length > 0 ? BitConverter.ToString(mb) : null;
                    try { a.Speed = Speed(ni.Speed); } catch { }
                    a.Ips = string.Join(", ", v4.ToArray());
                    a.Ipv6 = v6.Count > 0 ? v6[0] : null;
                    a.Gateway = string.Join(", ", gw.ToArray());
                    a.Dns = string.Join(", ", dns.ToArray());
                    a.DnsSuffix = ip.DnsSuffix;
                    try { a.Dhcp = ip.DhcpServerAddresses.Count > 0; } catch { }
                    list.Add(a);
                }
                catch { }
            }

            list.Sort(delegate(NetAdapter a, NetAdapter b)
            {
                int c = a.Virtual.CompareTo(b.Virtual);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            bool physical = false;
            foreach (NetAdapter a in list)
            {
                Block g = s.G(a.Name);
                g.Add("Adapter", a.Description);
                g.Add("Type", a.Type + (a.Virtual ? " (virtual)" : ""));
                g.Secret("MAC address", a.Mac);
                g.Add("Link speed", a.Speed);
                g.Add("DHCP", a.Dhcp ? "On" : "Off");
                g.Secret("IPv4", a.Ips);
                g.Secret("IPv6", a.Ipv6);
                g.Secret("Gateway", a.Gateway);
                g.Secret("DNS", a.Dns);
                g.Secret("DNS suffix", a.DnsSuffix);

                if (!a.Virtual)
                {
                    physical = true;
                    s.Brief.Add("Net: " + a.Name + " (" + a.Type + ") " +
                                (Cfg.Redact || string.IsNullOrEmpty(a.Ips) ? "" : a.Ips + " ") + a.Speed);
                }
            }

            // If only virtual adapters are up, still show one in the compact report.
            if (!physical && list.Count > 0)
            {
                NetAdapter a = list[0];
                s.Brief.Add("Net: " + a.Name + " (" + a.Type + ") " +
                            (Cfg.Redact || string.IsNullOrEmpty(a.Ips) ? "" : a.Ips + " ") + a.Speed);
            }

            if (hasWifi) WifiInfo(s);
            if (list.Count == 0) s.G().Add("Status", "No active network adapters");
            return s;
        }

        static void WifiInfo(Section s)
        {
            string o = U.Run("netsh", "wlan show interfaces");
            if (string.IsNullOrEmpty(o)) return;

            string ssid = null, signal = null, radio = null, band = null, channel = null, bssid = null;
            foreach (string raw in o.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int i = line.IndexOf(':');
                if (i <= 0) continue;
                string k = line.Substring(0, i).Trim();
                string v = line.Substring(i + 1).Trim();
                if (v.Length == 0) continue;

                // SSID/BSSID keys stay English on most localized Windows builds.
                if (ssid == null && k.IndexOf("SSID", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    k.IndexOf("BSSID", StringComparison.OrdinalIgnoreCase) < 0) { ssid = v; continue; }
                if (bssid == null && k.IndexOf("BSSID", StringComparison.OrdinalIgnoreCase) >= 0) { bssid = v; continue; }
                if (channel == null && string.Equals(k, "Channel", StringComparison.OrdinalIgnoreCase)) { channel = v; continue; }
                if (radio == null && string.Equals(k, "Radio type", StringComparison.OrdinalIgnoreCase)) { radio = v; continue; }
                if (band == null && string.Equals(k, "Band", StringComparison.OrdinalIgnoreCase)) { band = v; continue; }

                // Locale-independent fallbacks.
                if (signal == null && v.EndsWith("%", StringComparison.Ordinal)) signal = v;
                if (band == null && (v.IndexOf("GHz", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     v.IndexOf("MHz", StringComparison.OrdinalIgnoreCase) >= 0)) band = v;
                if (radio == null && v.StartsWith("802.11", StringComparison.OrdinalIgnoreCase)) radio = v;
                if (bssid == null && LooksLikeMac(v)) bssid = v;
            }

            if (ssid == null) return;
            Block g = s.G("Wi-Fi");
            g.Secret("Connected to", ssid);
            g.Add("Signal", signal);
            g.Add("Radio", radio);
            g.Add("Band", band);
            g.Add("Channel", channel);
            g.Secret("BSSID", bssid);

            s.Brief.Add("Wi-Fi: " + (Cfg.Redact ? "" : ssid + " ") + (signal != null ? signal : ""));
        }

        // ---------- Battery (laptops only) ----------
        static string BatteryStatus(int t)
        {
            switch (t)
            {
                case 1: return "Discharging";
                case 2: return "On AC power";
                case 3: return "Fully charged";
                case 4: return "Low";
                case 5: return "Critical";
                case 6: case 7: case 8: case 9: return "Charging";
                case 11: return "Partially charged";
                default: return "Unknown";
            }
        }

        public static Section GetBattery()
        {
            List<ManagementObject> bats = W.Q("SELECT * FROM Win32_Battery");
            if (bats.Count == 0) return null;

            Section s = new Section("Battery");
            for (int i = 0; i < bats.Count; i++)
            {
                ManagementObject b = bats[i];
                Block g = s.G(bats.Count > 1 ? "Battery " + (i + 1) : null);
                double charge = W.D(b, "EstimatedChargeRemaining");
                string status = BatteryStatus((int)W.D(b, "BatteryStatus"));
                g.AddPct("Charge", charge, null);
                g.Add("Status", status);

                double full = W.D(W.First("SELECT * FROM BatteryFullChargedCapacity", @"root\WMI"), "FullChargedCapacity");
                double design = W.D(W.First("SELECT * FROM BatteryStaticData", @"root\WMI"), "DesignedCapacity");
                if (full > 0 && design > 0)
                {
                    double h = Math.Min(100.0, full / design * 100.0);
                    g.AddPct("Health (vs. design)", h, U.N(full) + " / " + U.N(design) + " mWh");
                    if (h < 80) Cfg.Warn("Battery health is low (" + U.Pct(h) + " of design capacity)");
                    s.Brief.Add("Battery: " + U.Pct(charge) + ", " + status + ", health " + U.Pct(h));
                }
                else
                {
                    s.Brief.Add("Battery: " + U.Pct(charge) + ", " + status);
                }
            }
            return s;
        }
        // ---------- Security ----------
        static DateTime ParseDate(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return DateTime.MinValue;
            DateTime dt;
            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt)) return dt;
            if (DateTime.TryParse(raw, U.IC, DateTimeStyles.None, out dt)) return dt;

            string hex = raw.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    long ft = long.Parse(hex.Substring(2), NumberStyles.HexNumber, U.IC);
                    return DateTime.FromFileTime(ft);
                }
                catch { }
            }
            return DateTime.MinValue;
        }

        static bool PendingReboot()
        {
            if (U.RegKeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
                return true;
            if (U.RegKeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
                return true;

            object pfr = U.RegGet(@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations");
            string[] arr = pfr as string[];
            if (arr != null)
            {
                foreach (string x in arr)
                    if (!string.IsNullOrWhiteSpace(x)) return true;
                return false;
            }
            return pfr != null;
        }

        public static Section GetSecurity()
        {
            Section s = new Section("Security");
            Block g = s.G();

            // Third-party AV first: Defender may disable itself on purpose when another
            // antivirus is installed, so we must not report that as a problem.
            bool thirdPartyOn = false;
            Block ag = null;
            foreach (ManagementObject av in W.Q("SELECT * FROM AntiVirusProduct", @"root\SecurityCenter2"))
            {
                string n = W.S(av, "displayName");
                if (n == null || n.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                int state = (int)W.D(av, "productState");
                bool enabled = (state & 0x1000) != 0;
                bool updated = (state & 0x10) != 0;
                if (enabled) thirdPartyOn = true;
                if (ag == null) ag = s.G("Other antivirus");
                ag.Add(n, enabled ? "enabled" + (updated ? ", up to date" : ", out of date") : "disabled");
            }

            ManagementObject mp = W.First("SELECT * FROM MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender");
            bool defenderOn = false;
            bool realtimeOn = false;
            if (mp != null)
            {
                bool? av = W.B(mp, "AntivirusEnabled");
                bool? rt = W.B(mp, "RealTimeProtectionEnabled");
                defenderOn = av == true;
                realtimeOn = rt == true;
                g.Add("Antivirus (Defender)", U.OnOff(av));
                g.Add("Real-time protection", U.OnOff(rt));
                DateTime? sig = W.Dt(mp, "AntivirusSignatureLastUpdated");
                g.Add("Definitions updated", sig.HasValue ? sig.Value.ToString("dd MMM yyyy HH:mm", U.IC) : null);
                if (sig.HasValue)
                {
                    double days = (DateTime.Now - sig.Value).TotalDays;
                    if (days > 7) Cfg.Note("Defender definitions are " + (int)days + " days old");
                }
            }
            else
            {
                g.Add("Antivirus (Defender)", "Not available (third-party antivirus or not running)");
            }

            if (!defenderOn && !thirdPartyOn) Cfg.Warn("No active antivirus detected");
            if (defenderOn && !realtimeOn && !thirdPartyOn) Cfg.Warn("Defender real-time protection is off");

            string[] profiles = { "DomainProfile", "StandardProfile", "PublicProfile" };
            string[] labels = { "Domain", "Private", "Public" };
            for (int i = 0; i < profiles.Length; i++)
            {
                object v = U.RegGet(@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\" + profiles[i], "EnableFirewall");
                bool? on = null;
                try { if (v != null) on = Convert.ToInt32(v) == 1; } catch { }
                g.Add("Firewall - " + labels[i], U.OnOff(on));
                if (on == false) Cfg.Warn("Firewall is off for the " + labels[i].ToLowerInvariant() + " profile");
            }

            object uac = U.RegGet(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
            bool? uacOn = null;
            try { if (uac != null) uacOn = Convert.ToInt32(uac) == 1; } catch { }
            g.Add("User Account Control", U.OnOff(uacOn));

            string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            ManagementObject bl = W.First("SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter='" + sysDrive + "'", @"root\CIMV2\Security\MicrosoftVolumeEncryption");
            string blText;
            if (bl == null) blText = Cfg.IsAdmin ? "Not available" : "Unknown (run as administrator)";
            else blText = W.D(bl, "ProtectionStatus") == 1 ? "On" : "Off";
            g.Add("BitLocker (" + sysDrive + ")", blText);

            ManagementObject dg = W.First("SELECT * FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard");
            if (dg != null)
            {
                int vbs = (int)W.D(dg, "VirtualizationBasedSecurityStatus");
                g.Add("VBS", vbs == 2 ? "Running" : vbs == 1 ? "Enabled, not running" : "Off");
                uint[] running = W.Arr(dg, "SecurityServicesRunning");
                bool hvci = Array.IndexOf(running, (uint)2) >= 0;
                bool cg = Array.IndexOf(running, (uint)1) >= 0;
                g.Add("Memory integrity (HVCI)", hvci ? "On" : "Off");
                g.Add("Credential Guard", cg ? "On" : "Off");
            }

            bool reboot = PendingReboot();
            g.Add("Pending reboot", reboot ? "Yes" : "No");
            if (reboot) Cfg.Warn("A reboot is pending to finish an installation or update");

            var fixes = W.Q("SELECT * FROM Win32_QuickFixEngineering")
                .Select(h => new { Id = W.S(h, "HotFixID"), Raw = W.S(h, "InstalledOn"), Date = ParseDate(W.S(h, "InstalledOn")) })
                .OrderByDescending(x => x.Date)
                .Take(5)
                .ToList();
            if (fixes.Count > 0)
            {
                Block ug = s.G("Latest installed updates");
                foreach (var x in fixes)
                    ug.Add(x.Id ?? "Update", x.Date != DateTime.MinValue ? x.Date.ToString("dd MMM yyyy", U.IC) : x.Raw);

                DateTime newest = fixes[0].Date;
                if (newest != DateTime.MinValue)
                {
                    double days = (DateTime.Now - newest).TotalDays;
                    if (days > 60) Cfg.Note("Latest installed update is " + (int)days + " days old");
                }
            }
            return s;
        }
        // ---------- Startup ----------
        static void AddRunKey(Block g, HashSet<string> seen, RegistryHive hive, RegistryView view, string path)
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(hive, view))
                using (RegistryKey k = b.OpenSubKey(path))
                {
                    if (k == null) return;
                    foreach (string name in k.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        object v = k.GetValue(name);
                        string cmd = v == null ? null : v.ToString();
                        string key = name + "|" + (cmd ?? "");
                        if (!seen.Add(key)) continue;
                        g.Add(name, U.Shorten(U.MaskUser(cmd), 140));
                    }
                }
            }
            catch { }
        }

        public static Section GetStartup()
        {
            Section s = new Section("Startup");
            Block g = s.G("Auto-start programs");
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const string run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

            AddRunKey(g, seen, RegistryHive.LocalMachine, RegistryView.Registry64, run);
            AddRunKey(g, seen, RegistryHive.LocalMachine, RegistryView.Registry32, run);
            AddRunKey(g, seen, RegistryHive.CurrentUser, RegistryView.Default, run);

            string[] dirs = { Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                              Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) };
            foreach (string dir in dirs)
            {
                try
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        if (string.Equals(Path.GetFileName(f), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        string n = Path.GetFileNameWithoutExtension(f);
                        if (string.IsNullOrWhiteSpace(n)) continue;
                        if (!seen.Add("startup|" + f)) continue;
                        g.Add(n, "Startup folder");
                    }
                }
                catch { }
            }

            int count = g.Items.Count;
            if (count == 0)
            {
                g.Add("Status", "None found");
                s.Brief.Add("Startup: none");
            }
            else
            {
                s.Brief.Add("Startup: " + count + " auto-start " + (count == 1 ? "entry" : "entries"));
            }
            return s;
        }

        // ---------- Top processes ----------
        static long Ws(Process p)
        {
            try { return p.WorkingSet64; }
            catch { return 0; }
        }

        static string PName(Process p)
        {
            try { return p.ProcessName; }
            catch { return "?"; }
        }

        public static Section GetTopProcesses()
        {
            Section s = new Section("Processes");
            Process[] all = Process.GetProcesses();
            try
            {
                Block g = s.G("Top 5 by working set");
                var top = all.GroupBy(p => PName(p))
                    .Select(grp => new { Name = grp.Key, Mem = grp.Sum(q => Ws(q)), Count = grp.Count() })
                    .OrderByDescending(x => x.Mem)
                    .Take(5)
                    .ToList();
                foreach (var x in top)
                    g.Add(x.Name + (x.Count > 1 ? " (x" + x.Count + ")" : ""), U.Size(x.Mem));
                g.Add("Running", all.Length + " processes");
                s.Brief.Add("Top: " + string.Join(", ", top.Select(x => x.Name + " " + U.Size(x.Mem)).ToArray()));
            }
            finally
            {
                foreach (Process p in all) { try { p.Dispose(); } catch { } }
            }
            return s;
        }

        // ---------- Tool & environment ----------
        public static Section GetEnv()
        {
            Section s = new Section("Tool & Environment");
            Block g = s.G();
            g.Add("Report tool", "SystemReport " + Cfg.Version);
            g.Add(".NET runtime", Environment.Version.ToString());
            g.Add("Process", Environment.Is64BitProcess ? "64-bit" : "32-bit");
            g.Add("System language", CultureInfo.InstalledUICulture.DisplayName);
            g.Add("Run as admin", Cfg.IsAdmin ? "Yes" : "No");
            return s;
        }
    }

    // ===================================================================
    //  Renderers
    // ===================================================================
    static class Render
    {
        const int Width = 76;

        static void Row(StringBuilder sb, string key, string value)
        {
            string k = key ?? "";
            string text = value ?? "";

            // Very long keys (e.g. an Edge auto-launch entry) get their own line,
            // otherwise the value column becomes too narrow to read.
            if (k.Length > 28)
            {
                sb.AppendLine("  " + k + ":");
                List<string> longLines = Wrap(text, Width - 4);
                for (int i = 0; i < longLines.Count; i++) sb.AppendLine("    " + longLines[i]);
                return;
            }

            string head = "  " + (k.Length >= 24 ? k : k.PadRight(24)) + ": ";
            int avail = Width - head.Length;
            if (avail < 30) avail = 30;

            if (text.Length <= avail)
            {
                sb.AppendLine(head + text);
                return;
            }

            List<string> lines = Wrap(text, avail);
            sb.AppendLine(head + lines[0]);
            string indent = new string(' ', head.Length);
            for (int i = 1; i < lines.Count; i++) sb.AppendLine(indent + lines[i]);
        }

        static List<string> Wrap(string text, int width)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }

            string[] words = text.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder cur = new StringBuilder();
            foreach (string raw in words)
            {
                string word = raw;
                while (word.Length > width)
                {
                    if (cur.Length > 0) { lines.Add(cur.ToString()); cur.Length = 0; }

                    // Prefer a folder boundary in long paths over cutting a file name in half.
                    int cut = width;
                    int slash = word.LastIndexOfAny(new char[] { '\\', '/' }, width - 1, width);
                    if (slash >= width / 2) cut = slash + 1;

                    lines.Add(word.Substring(0, cut));
                    word = word.Substring(cut);
                }
                if (word.Length == 0) continue;

                if (cur.Length == 0) cur.Append(word);
                else if (cur.Length + 1 + word.Length <= width) cur.Append(' ').Append(word);
                else
                {
                    lines.Add(cur.ToString());
                    cur.Length = 0;
                    cur.Append(word);
                }
            }
            if (cur.Length > 0) lines.Add(cur.ToString());
            if (lines.Count == 0) lines.Add("");
            return lines;
        }

        // Full boxed text report
        public static string Text(Report r)
        {
            StringBuilder sb = new StringBuilder();
            string line = new string('=', Width);
            sb.AppendLine(line);
            sb.AppendLine("   S Y S T E M   R E P O R T");
            sb.AppendLine(line);
            Row(sb, "Computer", r.Computer);
            Row(sb, "Generated", r.Generated);
            Row(sb, "Run as admin", r.IsAdmin ? "Yes" : "No (some security items are limited)");
            if (r.DurationSec > 0)
                Row(sb, "Collected in", r.DurationSec.ToString("0.0", U.IC) + " s");

            foreach (Section s in r.Sections)
            {
                sb.AppendLine();
                sb.AppendLine("+" + new string('=', Width - 2) + "+");
                string title = s.Title.ToUpperInvariant();
                if (title.Length > Width - 4) title = title.Substring(0, Width - 7) + "...";
                sb.AppendLine("|  " + title.PadRight(Width - 4) + "|");
                sb.AppendLine("+" + new string('=', Width - 2) + "+");
                foreach (Block b in s.Blocks)
                {
                    if (!string.IsNullOrEmpty(b.Name))
                    {
                        sb.AppendLine();
                        sb.AppendLine("  >> " + U.Shorten(b.Name, Width - 6));
                        sb.AppendLine("  " + new string('-', Width - 4));
                    }
                    foreach (Entry e in b.Items) Row(sb, e.Key, e.Text(true));
                }
            }

            sb.AppendLine();
            sb.AppendLine(line);
            sb.AppendLine("   End of report  (SystemReport " + Cfg.Version + ")");
            sb.AppendLine(line);
            return sb.ToString();
        }

        // Token-light: one short line per item, no decoration
        public static string Minimal(Report r)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Section s in r.Sections)
                foreach (string line in s.Brief)
                    sb.AppendLine(line);
            return sb.ToString();
        }

        // JSON (hand-rolled, no dependencies)
        public static string Json(Report r)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"tool\": " + Q("SystemReport " + Cfg.Version) + ",\n");
            sb.Append("  \"computer\": " + Q(r.Computer) + ",\n");
            sb.Append("  \"generated\": " + Q(r.Generated) + ",\n");
            sb.Append("  \"admin\": " + (r.IsAdmin ? "true" : "false") + ",\n");
            sb.Append("  \"duration_sec\": " + r.DurationSec.ToString("0.0", U.IC) + ",\n");
            sb.Append("  \"warnings\": " + QArray(Cfg.Warnings) + ",\n");
            sb.Append("  \"notes\": " + QArray(Cfg.Notes) + ",\n");
            sb.Append("  \"sections\": {\n");

            List<string> secs = new List<string>();
            foreach (Section s in r.Sections)
            {
                List<string> members = new List<string>();
                foreach (Block b in s.Blocks)
                {
                    if (string.IsNullOrEmpty(b.Name))
                    {
                        foreach (Entry e in b.Items)
                            members.Add("      " + Q(e.Key) + ": " + Q(e.Text(false)));
                    }
                    else
                    {
                        List<string> inner = new List<string>();
                        foreach (Entry e in b.Items)
                            inner.Add("        " + Q(e.Key) + ": " + Q(e.Text(false)));
                        members.Add("      " + Q(b.Name) + ": {\n" + string.Join(",\n", inner.ToArray()) + "\n      }");
                    }
                }
                secs.Add("    " + Q(s.Title) + ": {\n" + string.Join(",\n", members.ToArray()) + "\n    }");
            }
            sb.Append(string.Join(",\n", secs.ToArray()));
            sb.Append("\n  }\n}\n");
            return sb.ToString();
        }

        static string QArray(List<string> items)
        {
            if (items == null || items.Count == 0) return "[]";
            List<string> parts = new List<string>();
            foreach (string s in items) parts.Add(Q(s));
            return "[" + string.Join(", ", parts.ToArray()) + "]";
        }

        static string Q(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
    // ===================================================================
    //  Program
    // ===================================================================
    class Options
    {
        public bool Minimal, Json, Redact, Copy, NoOpen, Help, Version, Elevate;
        public string Out;
    }

    static class Program
    {
        static int Main(string[] args)
        {
            // Make non-ASCII user/volume names display correctly in the console.
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

            Options o = ParseArgs(args);
            if (o == null) return 2;
            if (o.Help) { PrintHelp(); return 0; }
            if (o.Version) { Console.WriteLine("SystemReport " + Cfg.Version); return 0; }

            Cfg.IsAdmin = IsAdmin();
            bool interactive = args.Length == 0;
            if (interactive && !Menu(o)) return 0;
            Cfg.Redact = o.Redact;

            if (o.Elevate && !Cfg.IsAdmin)
            {
                if (RelaunchElevated(args)) return 0;
                Console.WriteLine("Could not elevate (UAC was cancelled).");
                if (interactive) Pause();
                return 1;
            }

            Stopwatch sw = Stopwatch.StartNew();
            Report r = Collect();
            sw.Stop();
            r.DurationSec = sw.Elapsed.TotalSeconds;

            string text = o.Json ? Render.Json(r) : (o.Minimal ? Render.Minimal(r) : Render.Text(r));
            string ext = o.Json ? ".json" : ".txt";
            string path = ExpandPath(o.Out ?? DefaultPath(o, ext));

            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (Directory.Exists(path)) throw new IOException("The output path is a directory.");
                File.WriteAllText(path, text, o.Json ? new UTF8Encoding(false) : new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not write " + path + ": " + ex.Message);
                if (interactive) Pause();
                return 1;
            }

            bool compact = o.Minimal && !o.Json;
            Console.WriteLine();
            if (compact) { Console.WriteLine(text); }
            Console.WriteLine("Saved: " + path);
            if (o.Copy || compact)
                Console.WriteLine(CopyToClipboard(text) ? "Copied to clipboard." : "Could not copy to clipboard.");

            if (!o.NoOpen && !compact)
            {
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch { }
            }

            if (interactive) Pause();
            return 0;
        }

        static Report Collect()
        {
            Report r = new Report();
            Cfg.ResetHealth();
            r.Computer = Cfg.Redact ? "[redacted]" : Environment.MachineName;
            r.Generated = DateTime.Now.ToString("dddd, dd MMMM yyyy  HH:mm:ss", U.IC);
            r.IsAdmin = Cfg.IsAdmin;

            Func<Section>[] steps =
            {
                Collectors.GetOs, Collectors.GetMachine, Collectors.GetCpu, Collectors.GetMemory,
                Collectors.GetGpu, Collectors.GetStorage, Collectors.GetDevices, Collectors.GetNetwork,
                Collectors.GetBattery, Collectors.GetSecurity, Collectors.GetStartup,
                Collectors.GetTopProcesses, Collectors.GetEnv
            };

            Console.Write("Collecting system information");
            foreach (Func<Section> step in steps)
            {
                Section s = null;
                try { s = step(); }
                catch (Exception ex)
                {
                    s = new Section(step.Method.Name.Substring(3));
                    s.G().Add("Error", ex.Message);
                    Cfg.Warn("Could not collect " + s.Title + ": " + ex.Message);
                }
                if (s != null) r.Sections.Add(s);
                Console.Write(".");
            }
            Console.WriteLine(" done");

            // The summary needs every warning, so build it last and show it first.
            r.Sections.Insert(0, Collectors.GetSummary());
            return r;
        }

        static bool Menu(Options o)
        {
            Console.WriteLine();
            Console.WriteLine("  ==============================");
            Console.WriteLine("    SYSTEM REPORT  v" + Cfg.Version);
            Console.WriteLine("  ==============================");
            Console.WriteLine();
            Console.WriteLine("  [1] Full report     (detailed, opens when done)");
            Console.WriteLine("  [2] Minimal report  (compact, copied to clipboard - ideal for AI chats)");
            Console.WriteLine("  [3] JSON report     (machine-readable)");
            Console.WriteLine("  [Q] Quit");
            Console.WriteLine();
            Console.WriteLine("  Tip: run as administrator for TPM and BitLocker details.");
            Console.WriteLine();

            while (true)
            {
                Console.Write("  Choose: ");
                string c = (Console.ReadLine() ?? "q").Trim().ToLowerInvariant();
                if (c == "q") return false;
                if (c == "1") break;
                if (c == "2") { o.Minimal = true; break; }
                if (c == "3") { o.Json = true; break; }
            }

            Console.Write("  Hide personal info (serial, MAC, IPs, username, PC name)? [y/N]: ");
            string a = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            o.Redact = a == "y" || a == "yes";
            Console.WriteLine();
            return true;
        }

        static void Pause()
        {
            Console.WriteLine();
            Console.Write("Press Enter to close...");
            Console.ReadLine();
        }

        static string DefaultPath(Options o, string ext)
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Environment.CurrentDirectory;
            string pc = Cfg.Redact ? "" : "_" + SafeName(Environment.MachineName);
            string name = "System_Report" + pc + "_" + DateTime.Now.ToString("yyyy-MM-dd", U.IC)
                          + (o.Minimal && !o.Json ? "_min" : "") + ext;
            return Path.Combine(dir, name);
        }

        static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "PC";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        static string ExpandPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (p == "~") p = home;
            else if (p.StartsWith("~\\", StringComparison.Ordinal) || p.StartsWith("~/", StringComparison.Ordinal))
                p = Path.Combine(home, p.Substring(2));
            return Environment.ExpandEnvironmentVariables(p);
        }

        static bool CopyToClipboard(string text)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("clip.exe");
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return false;
                    // The default console encoding is the OEM code page, which mangles
                    // non-ASCII volume labels and user names. clip.exe accepts UTF-8.
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                    p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    p.StandardInput.BaseStream.Flush();
                    p.StandardInput.Close();
                    if (!p.WaitForExit(5000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                }
                return true;
            }
            catch { return false; }
        }

        static bool IsAdmin()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        static bool RelaunchElevated(string[] args)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < args.Length; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(QuoteArg(args[i]));
                }

                string exe = Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo(exe, sb.ToString());
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = Environment.CurrentDirectory;
                return Process.Start(psi) != null;
            }
            catch { return false; }
        }

        static string QuoteArg(string s)
        {
            if (s == null) return "\"\"";
            if (s.IndexOf(' ') < 0 && s.IndexOf('\t') < 0 && s.IndexOf('"') < 0) return s;
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        static Options ParseArgs(string[] args)
        {
            Options o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string raw = args[i];
                string a = raw.TrimStart('-', '/').ToLowerInvariant();

                if (a.StartsWith("out=", StringComparison.Ordinal))
                {
                    o.Out = raw.Substring(raw.IndexOf('=') + 1);
                    if (o.Out.Length == 0)
                    {
                        Console.WriteLine("Missing file name after " + raw);
                        return null;
                    }
                    continue;
                }

                switch (a)
                {
                    case "m": case "minimal": o.Minimal = true; break;
                    case "j": case "json": o.Json = true; break;
                    case "r": case "redact": o.Redact = true; break;
                    case "c": case "copy": o.Copy = true; break;
                    case "n": case "no-open": case "noopen": o.NoOpen = true; break;
                    case "e": case "elevate": o.Elevate = true; break;
                    case "v": case "version": o.Version = true; break;
                    case "o": case "out":
                        if (i + 1 >= args.Length)
                        {
                            Console.WriteLine("Missing file name after " + raw);
                            return null;
                        }
                        o.Out = args[++i];
                        break;
                    case "h": case "help": case "?": o.Help = true; break;
                    default:
                        Console.WriteLine("Unknown option: " + raw);
                        Console.WriteLine("Use --help to see the options.");
                        return null;
                }
            }
            return o;
        }

        static void PrintHelp()
        {
            Console.WriteLine("SystemReport " + Cfg.Version + " - system information report for Windows");
            Console.WriteLine();
            Console.WriteLine("Usage: SystemReport.exe [options]     (no options = interactive menu)");
            Console.WriteLine();
            Console.WriteLine("  -m, --minimal    compact text, a few short lines (also copied to clipboard)");
            Console.WriteLine("  -j, --json       JSON output (overrides --minimal)");
            Console.WriteLine("  -r, --redact     hide serial, MAC, IPs, Wi-Fi name, username and PC name");
            Console.WriteLine("  -o, --out FILE   write to FILE instead of the Desktop (creates folders)");
            Console.WriteLine("  -c, --copy       also copy the result to the clipboard");
            Console.WriteLine("  -n, --no-open    do not open the file when done");
            Console.WriteLine("  -e, --elevate    relaunch with administrator rights (UAC)");
            Console.WriteLine("  -v, --version    show the version");
            Console.WriteLine("  -h, --help       show this help");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 = success, 1 = write/elevation failure, 2 = bad option.");
            Console.WriteLine("Run as administrator to include TPM and BitLocker details.");
        }
    }
}
