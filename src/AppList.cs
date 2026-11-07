using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskHand
{
    sealed class InstalledApp
    {
        public string Name;
        public string Target;
        public ImageSource Icon;
    }

    static class InstalledApps
    {
        public const string ShellPrefix = @"shell:AppsFolder\";

        static Task<List<InstalledApp>> loading;
        static readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static Task<List<InstalledApp>> LoadAsync()
        {
            if (loading != null) return loading;
            var tcs = new TaskCompletionSource<List<InstalledApp>>();
            var thread = new Thread(() =>
            {
                try { tcs.SetResult(Load()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            loading = tcs.Task;
            return loading;
        }

        public static string NameOf(string target)
        {
            string name;
            lock (names) if (names.TryGetValue(target, out name)) return name;
            if (!target.StartsWith(ShellPrefix, StringComparison.OrdinalIgnoreCase)) return Path.GetFileNameWithoutExtension(target.TrimEnd('\\'));
            string id = target.Substring(ShellPrefix.Length);
            int bang = id.IndexOf('!');
            if (bang > 0) id = id.Substring(0, bang);
            int under = id.IndexOf('_');
            if (under > 0) id = id.Substring(0, under);
            int dot = id.LastIndexOf('.');
            return dot >= 0 && dot < id.Length - 1 ? id.Substring(dot + 1) : id;
        }

        public static void Remember(InstalledApp app)
        {
            lock (names) names[app.Target] = app.Name;
        }

        static List<InstalledApp> Load()
        {
            var result = new List<InstalledApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Type shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return result;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object folder = Call(shell, "NameSpace", "shell:AppsFolder");
                if (folder == null) return result;
                object items = Call(folder, "Items");
                int count = (int)Get(items, "Count");
                for (int i = 0; i < count; i++)
                {
                    object item = Call(items, "Item", i);
                    if (item == null) continue;
                    try
                    {
                        string name = ((string)Get(item, "Name") ?? "").Trim();
                        string path = ((string)Get(item, "Path") ?? "").Trim();
                        if (name.Length == 0 || path.Length == 0) continue;
                        if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("Удал", StringComparison.OrdinalIgnoreCase)) continue;
                        string target = Resolve(path);
                        if (target == null || !seen.Add(name + "|" + target)) continue;
                        var app = new InstalledApp { Name = name, Target = target, Icon = IconOf(ShellPrefix + path) };
                        result.Add(app);
                        Remember(app);
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
                Marshal.ReleaseComObject(items);
                Marshal.ReleaseComObject(folder);
            }
            finally { Marshal.ReleaseComObject(shell); }
            return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static string Resolve(string path)
        {
            string file = null;
            if (path.StartsWith("{"))
            {
                int close = path.IndexOf('}');
                Guid id;
                if (close > 0 && close + 2 < path.Length && Guid.TryParse(path.Substring(0, close + 1), out id))
                {
                    IntPtr folder;
                    if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out folder) == 0)
                    {
                        try { file = Path.Combine(Marshal.PtrToStringUni(folder), path.Substring(close + 2)); }
                        finally { Marshal.FreeCoTaskMem(folder); }
                    }
                }
            }
            else if (path.Length > 2 && path[1] == ':') file = path;

            if (file != null)
            {
                if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
                return File.Exists(file) ? file : null;
            }
            if (path.IndexOf('\\') >= 0 || path.IndexOf("://", StringComparison.Ordinal) >= 0) return null;
            return ShellPrefix + path;
        }

        static ImageSource IconOf(string parsingName)
        {
            IntPtr pidl;
            uint attrs;
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out attrs) != 0 || pidl == IntPtr.Zero) return null;
            try
            {
                var info = new SHFILEINFO();
                if (SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x100 | 0x8) == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                try
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    return src;
                }
                finally { DestroyIcon(info.hIcon); }
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }

        static object Call(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
        }

        static object Get(object target, string name)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attributes, ref SHFILEINFO info, uint size, uint flags);
        [DllImport("shell32.dll")]
        static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr icon);
    }
}
