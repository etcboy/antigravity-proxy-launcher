using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace AntigravityProxyLauncher
{
    internal sealed class ShortcutInfo
    {
        public string TargetPath;
        public string Arguments;
        public string IconLocation;
    }

    // Use Unicode Shell interfaces explicitly, independently of the Windows display language.
    internal static class NativeShortcut
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, IntPtr findData, uint flags);
            void GetIDList(out IntPtr itemList);
            void SetIDList(IntPtr itemList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int capacity);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int command);
            void SetShowCmd(int command);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int capacity, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr owner, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }

        public static ShortcutInfo Read(string file)
        {
            object shell = new ShellLink();
            try {
                ((IPersistFile)shell).Load(file, 0);
                IShellLinkW link = (IShellLinkW)shell;
                StringBuilder target = new StringBuilder(32768);
                StringBuilder arguments = new StringBuilder(32768);
                StringBuilder icon = new StringBuilder(32768);
                int iconIndex;
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH
                link.GetArguments(arguments, arguments.Capacity);
                link.GetIconLocation(icon, icon.Capacity, out iconIndex);
                return new ShortcutInfo { TargetPath = target.ToString(), Arguments = arguments.ToString(), IconLocation = icon.ToString() + "," + iconIndex };
            } finally { Marshal.FinalReleaseComObject(shell); }
        }

        public static void Write(string file, string target, string arguments, string icon, string workingDirectory, string description)
        {
            object shell = new ShellLink();
            try {
                IShellLinkW link = (IShellLinkW)shell;
                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetIconLocation(icon, 0);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(description);
                link.SetShowCmd(1);
                ((IPersistFile)shell).Save(file, true);
            } finally { Marshal.FinalReleaseComObject(shell); }
        }
    }
}