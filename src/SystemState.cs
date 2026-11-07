using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskHand
{
    static class SystemState
    {
        public static bool ProgramRunning(string name)
        {
            var list = Process.GetProcessesByName(name);
            foreach (var p in list) p.Dispose();
            return list.Length > 0;
        }

        public static string ActiveProgram()
        {
            uint pid;
            Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
            if (pid == 0) return null;
            IntPtr h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? Path.GetFileNameWithoutExtension(sb.ToString()) : null;
            }
            finally { Native.CloseHandle(h); }
        }

        public static string WifiName()
        {
            try
            {
                var info = new ProcessStartInfo("netsh.exe", "wlan show interfaces")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
                };
                using (var p = Process.Start(info))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    foreach (string raw in text.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (!line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase)) continue;
                        int colon = line.IndexOf(':');
                        if (colon < 0) continue;
                        string name = line.Substring(colon + 1).Trim();
                        if (name.Length > 0) return name;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (IOException) { }
            catch (InvalidOperationException) { }
            return null;
        }

        public static bool Internet()
        {
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    var type = nic.NetworkInterfaceType;
                    if (type == System.Net.NetworkInformation.NetworkInterfaceType.Loopback || type == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                    foreach (var gw in nic.GetIPProperties().GatewayAddresses)
                        if (gw.Address != null && !gw.Address.Equals(System.Net.IPAddress.Any) && !gw.Address.Equals(System.Net.IPAddress.IPv6Any)) return true;
                }
            }
            catch (System.Net.NetworkInformation.NetworkInformationException) { }
            return false;
        }

        public static bool OnBattery()
        {
            var status = System.Windows.Forms.SystemInformation.PowerStatus;
            return status.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
        }

        public static int BatteryPercent()
        {
            var status = System.Windows.Forms.SystemInformation.PowerStatus;
            if (status.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery)) return -1;
            return (int)Math.Round(status.BatteryLifePercent * 100);
        }

        public static int ScreenCount() { return System.Windows.Forms.Screen.AllScreens.Length; }

        public static bool DeviceConnected(string part)
        {
            foreach (var name in Devices().Keys)
                if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public static Dictionary<string, int> Devices()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 0x2 | 0x4);
            if (set == new IntPtr(-1)) return result;
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                var buffer = new byte[1024];
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    string name = Property(set, ref data, 0x0C, buffer) ?? Property(set, ref data, 0x00, buffer);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    int n;
                    result.TryGetValue(name, out n);
                    result[name] = n + 1;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return result;
        }

        static string Property(IntPtr set, ref SP_DEVINFO_DATA data, uint property, byte[] buffer)
        {
            uint type, needed;
            if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out type, buffer, (uint)buffer.Length, out needed)) return null;
            string text = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(needed, (uint)buffer.Length));
            int zero = text.IndexOf('\0');
            return (zero >= 0 ? text.Substring(0, zero) : text).Trim();
        }

        public static string SoundOutput()
        {
            return ScenarioRunner.DefaultSoundName();
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref SP_DEVINFO_DATA data, uint property, out uint type, byte[] buffer, uint size, out uint needed);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    }
}
