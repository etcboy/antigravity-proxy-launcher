using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AntigravityProxyLauncher
{
    public static class SilentStart
    {
        public static void Start(SettingsStore store)
        {
            string notice;
            store.Load(out notice);
            if (!File.Exists(store.CurrentFile))
                throw new IOException("请先填写程序位置和代理端口，再点击“配置启动图标”。");
            Settings settings;
            try { settings = Settings.Load(store.CurrentFile); }
            catch { throw new IOException("已保存的设置无法读取，请重新填写并保存设置。"); }
            ProcessStartInfo info = Launcher.CreateStartInfo(settings);
            Process[] running = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(info.FileName));
            bool alreadyRunning = running.Length != 0;
            foreach (Process process in running) process.Dispose();
            if (alreadyRunning)
                throw new IOException("Antigravity 已在运行。请保存工作并完全退出后，再用代理图标启动。");
            Launcher.CheckLocalProxy(settings);
            using (Process process = Process.Start(info)) {
                if (process == null) throw new IOException("未能启动 Antigravity，请检查程序位置。");
            }
        }
    }

    public static class ShortcutSetup
    {
        public static string Configure(Settings settings, SettingsStore store, string sourceExecutable, string desktopDirectory, string programsDirectory)
        {
            ProcessStartInfo application = Launcher.CreateStartInfo(settings);
            string installDirectory = Path.GetDirectoryName(Path.GetFullPath(store.CurrentFile));
            string installedExecutable = Path.Combine(installDirectory, "Antigravity-Proxy-Launcher.exe");
            string launchShortcut = Path.Combine(desktopDirectory, "Antigravity（代理）.lnk");
            string settingsShortcut = Path.Combine(programsDirectory, "Antigravity 代理设置.lnk");
            CheckShortcutOwnership(launchShortcut, installedExecutable);
            CheckShortcutOwnership(settingsShortcut, installedExecutable);
            Directory.CreateDirectory(installDirectory);
            if (!String.Equals(Path.GetFullPath(sourceExecutable), installedExecutable, StringComparison.OrdinalIgnoreCase)) {
                string temporary = installedExecutable + ".update-" + Guid.NewGuid().ToString("N");
                try {
                    File.Copy(sourceExecutable, temporary);
                    if (File.Exists(installedExecutable)) File.Replace(temporary, installedExecutable, null);
                    else File.Move(temporary, installedExecutable);
                } catch (IOException error) {
                    throw new IOException("无法更新启动组件。请退出其他已打开的代理设置窗口后重试。", error);
                } finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            store.Save(settings);
            WriteShortcut(launchShortcut, installedExecutable, "--launch", application.FileName + ",0", application.WorkingDirectory, "使用已保存的代理配置启动 Antigravity");
            WriteShortcut(settingsShortcut, installedExecutable, "--settings", installedExecutable + ",0", installDirectory, "修改 Antigravity 的程序位置和代理设置");
            return launchShortcut;
        }

        private static object CreateShortcutObject(string file, out object shell)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new IOException("当前 Windows 无法创建快捷方式。");
            shell = Activator.CreateInstance(shellType);
            try { return shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { file }); }
            catch { Marshal.FinalReleaseComObject(shell); throw; }
        }

        private static void CheckShortcutOwnership(string file, string target)
        {
            if (!File.Exists(file)) return;
            object shell;
            object shortcut = CreateShortcutObject(file, out shell);
            try {
                string existingTarget = (string)shortcut.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
                if (!String.Equals(existingTarget, target, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("已有同名图标，但它不属于本工具。请先重命名该图标，再重新配置：" + Path.GetFileNameWithoutExtension(file));
            } finally {
                Marshal.FinalReleaseComObject(shortcut);
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static void WriteShortcut(string file, string target, string arguments, string icon, string workingDirectory, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temporary = Path.Combine(Path.GetDirectoryName(file), ".antigravity-" + Guid.NewGuid().ToString("N") + ".lnk");
            try {
                object shell;
                object shortcut = CreateShortcutObject(temporary, out shell);
                try {
                    Type type = shortcut.GetType();
                    type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                    type.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                    type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { icon });
                    type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                    type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                    type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                } finally {
                    Marshal.FinalReleaseComObject(shortcut);
                    Marshal.FinalReleaseComObject(shell);
                }
                if (File.Exists(file)) File.Replace(temporary, file, null);
                else File.Move(temporary, file);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}