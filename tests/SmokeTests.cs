using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using AntigravityProxyLauncher;
class SmokeTests {
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static object OpenLink(string path, out object shell) {
        Type type = Type.GetTypeFromProgID("WScript.Shell");
        shell = Activator.CreateInstance(type);
        return type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
    }
    static string ReadLink(string path, string property) {
        object shell; object link = OpenLink(path, out shell);
        try { return (string)link.GetType().InvokeMember(property, BindingFlags.GetProperty, null, link, null); }
        finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
    static void ConflictLink(string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        object shell; object link = OpenLink(path, out shell);
        try {
            link.GetType().InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Path.Combine(Environment.SystemDirectory, "notepad.exe") });
            link.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        } finally { Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
    }
    [STAThread]
    static void Main(string[] args) {
        try {
            if (args[0] == "silent") { SilentStart.Start(new SettingsStore(args[1], null)); return; }
            Run(args[0], args[1]);
        } catch (Exception error) { Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message); Environment.ExitCode = 1; }
    }
    static void Run(string root, string sourceExecutable) {
        string downloaded = Path.Combine(root, "download", "Antigravity-Proxy-Launcher.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(downloaded));
        File.Copy(sourceExecutable, downloaded);
        Settings settings = Settings.Defaults();
        settings.ApplicationPath = Path.Combine(root, "ShortcutProbe.exe");
        SettingsStore store = new SettingsStore(Path.Combine(root, "app-data", "settings.json"), null);
        string desktop = Path.Combine(root, "desktop");
        string programs = Path.Combine(root, "programs");
        string launchLink = ShortcutSetup.Configure(settings, store, downloaded, desktop, programs);
        string installed = Path.Combine(root, "app-data", "Antigravity-Proxy-Launcher.exe");
        string settingsLink = Path.Combine(programs, "Antigravity 代理设置.lnk");
        Assert(File.Exists(installed), "Managed launcher copy missing.");
        Assert(String.Equals(ReadLink(launchLink, "TargetPath"), installed, StringComparison.OrdinalIgnoreCase), "Desktop target is incorrect.");
        Assert(ReadLink(launchLink, "Arguments") == "--launch", "Desktop link is not silent.");
        Assert(ReadLink(settingsLink, "Arguments") == "--settings", "Settings entry is incorrect.");
        Assert(ReadLink(launchLink, "IconLocation").IndexOf("ShortcutProbe.exe", StringComparison.OrdinalIgnoreCase) >= 0, "Application icon missing.");
        File.Move(downloaded, downloaded + ".moved");
        Assert(File.Exists(ReadLink(launchLink, "TargetPath")), "Shortcut broke after moving the downloaded tool.");
        Console.WriteLine("PASS: desktop silent shortcut and Start menu settings link target a managed copy; moving the download does not break them.");

        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try {
            settings.ProxyPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            store.Save(settings);
            ProcessStartInfo start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "silent \"" + store.CurrentFile + "\"");
            start.UseShellExecute = false; start.CreateNoWindow = true;
            using (Process worker = Process.Start(start)) {
                Assert(worker.WaitForExit(5000) && worker.ExitCode == 0, "Silent worker failed to exit.");
            }
            string result = Path.Combine(root, "child-result.txt");
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!File.Exists(result) && DateTime.UtcNow < deadline) Thread.Sleep(50);
            Assert(File.Exists(result), "Child did not survive silent launcher exit.");
            string[] values = File.ReadAllLines(result);
            Assert(values[0] == "http://127.0.0.1:" + settings.ProxyPort && values[1] == values[0], "Silent startup did not read the updated saved port.");
            Assert(values[2] == "localhost,127.0.0.1,::1", "Loopback exclusions missing.");
            Console.WriteLine("PASS: saving a different port updates future silent launches; child retains proxy settings after worker exits.");
        } finally { listener.Stop(); }

        ShortcutSetup.Configure(settings, store, downloaded + ".moved", desktop, programs);
        Assert(Directory.GetFiles(desktop).Length == 1 && Directory.GetFiles(programs).Length == 1, "Reconfiguration created extra links.");
        Console.WriteLine("PASS: reconfiguration updates existing tool links without duplicates.");
        string conflictingDesktop = Path.Combine(root, "conflict-desktop");
        string conflictingLink = Path.Combine(conflictingDesktop, "Antigravity（代理）.lnk");
        ConflictLink(conflictingLink);
        string before = ReadLink(conflictingLink, "TargetPath");
        bool rejected = false;
        try { ShortcutSetup.Configure(settings, store, sourceExecutable, conflictingDesktop, programs); } catch (IOException) { rejected = true; }
        Assert(rejected && ReadLink(conflictingLink, "TargetPath") == before, "An unrelated same-name shortcut was overwritten.");
        rejected = false;
        try { SilentStart.Start(new SettingsStore(Path.Combine(root, "not-configured", "settings.json"), null)); } catch (IOException) { rejected = true; }
        Assert(rejected, "Unconfigured silent launch unexpectedly used defaults.");
        Console.WriteLine("PASS: conflicting user shortcuts are preserved, and missing configuration reports a recoverable error.");

        string legacy = Path.Combine(root, "legacy-program", "AntigravityProxyLauncher.settings.json");
        SettingsStore migratedStore = new SettingsStore(Path.Combine(root, "migrated-data", "settings.json"), legacy);
        settings.Save(legacy);
        string notice;
        Settings migrated = migratedStore.Load(out notice);
        Assert(migrated.ProxyPort == settings.ProxyPort && File.Exists(migratedStore.CurrentFile) && !File.Exists(legacy), "Legacy settings migration lost data or left a sidecar.");
        string blockedPath = Path.Combine(root, "blocked-directory");
        File.WriteAllText(blockedPath, "Not a directory");
        string retainedLegacy = Path.Combine(root, "retained-legacy", "AntigravityProxyLauncher.settings.json");
        settings.Save(retainedLegacy);
        SettingsStore blockedStore = new SettingsStore(Path.Combine(blockedPath, "settings.json"), retainedLegacy);
        Settings retained = blockedStore.Load(out notice);
        Assert(File.Exists(retainedLegacy) && retained.ProxyPort == settings.ProxyPort && notice != null, "Failed migration did not preserve the original.");
        Console.WriteLine("PASS: legacy configuration migration preserves values and retains the original on failure.");
        string parentProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        Launcher.CreateStartInfo(settings);
        Assert(Environment.GetEnvironmentVariable("HTTPS_PROXY") == parentProxy, "Creating a child environment changed the parent environment.");
        Console.WriteLine("PASS: the parent process proxy environment is unchanged.");
    }
}