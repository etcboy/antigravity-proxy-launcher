using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AntigravityProxyLauncher;
class SmokeTests {
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    [STAThread]
    static void Main(string[] args) {
        try {
            if (args[0] == "success" || args[0] == "failure") { VerifyWindow(args[1], args[0] == "failure"); return; }
            Run(args[0]);
        } catch (Exception error) { Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message); Environment.ExitCode = 1; }
    }
    static void VerifyWindow(string root, bool expectFailure) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        bool timedOut = false;
        bool retained = false;
        using (Socket listening = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        using (Socket blocked = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        using (MainForm form = new MainForm())
        using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer()) {
            listening.Bind(new IPEndPoint(IPAddress.Loopback,0)); listening.Listen(2);
            blocked.Bind(new IPEndPoint(IPAddress.Loopback,0));
            int port = ((IPEndPoint)(expectFailure ? blocked.LocalEndPoint : listening.LocalEndPoint)).Port;
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Type type = typeof(MainForm);
            ((TextBox)type.GetField("appPath", flags).GetValue(form)).Text = Path.Combine(root,"ShortcutProbe.exe");
            ((TextBox)type.GetField("host",flags).GetValue(form)).Text = "127.0.0.1";
            ((NumericUpDown)type.GetField("port",flags).GetValue(form)).Value = port;
            type.GetField("settingsStore",flags).SetValue(form,new SettingsStore(Path.Combine(root,"launched-settings.json"),null));
            form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-16000,-16000);
            timer.Interval=6000;
            timer.Tick += delegate { timedOut=true; form.Close(); };
            form.Shown += async delegate {
                await (Task)type.GetMethod("LaunchApp",flags).Invoke(form,null);
                if (expectFailure) { retained=!form.IsDisposed && form.Visible; form.Close(); }
            };
            timer.Start(); Application.Run(form); timer.Stop();
        }
        Assert(!timedOut,"Launch window did not finish in time.");
        if (expectFailure) Assert(retained,"A failed launch closed its error window.");
    }
    static void RunWorker(string root,string mode) {
        ProcessStartInfo info = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,mode + " \"" + root + "\"");
        info.UseShellExecute=false; info.CreateNoWindow=true;
        using (Process process=Process.Start(info)) Assert(process.WaitForExit(10000) && process.ExitCode==0,"Window verification failed: "+mode);
    }
    static void Run(string root) {
        RunWorker(root,"success");
        string result=Path.Combine(root,"child-result.txt");
        DateTime deadline=DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(result) && DateTime.UtcNow<deadline) Thread.Sleep(50);
        Assert(File.Exists(result),"Child did not continue after launcher exit.");
        string[] values=File.ReadAllLines(result);
        Settings saved=Settings.Load(Path.Combine(root,"launched-settings.json"));
        string expected="http://127.0.0.1:"+saved.ProxyPort;
        Assert(values[0]==expected && values[1]==expected && values[2]=="localhost,127.0.0.1,::1","Child proxy environment is incorrect.");
        RunWorker(root,"failure");
        Assert(Directory.GetFiles(root,"*.lnk",SearchOption.AllDirectories).Length==0,"Simple launcher created a shortcut.");
        Console.WriteLine("PASS: successful launch saves settings and exits; child keeps proxies; failed launch preserves the window; no shortcuts created.");
        Settings settings=Settings.Defaults(); settings.ApplicationPath=Path.Combine(root,"ShortcutProbe.exe"); settings.ProxyPort=7890;
        string current=Path.Combine(root,"app-data","settings.json");
        SettingsStore store=new SettingsStore(current,null); store.Save(settings); settings.ProxyPort=8888; store.Save(settings);
        string notice; Assert(store.Load(out notice).ProxyPort==8888,"Saved port update was not restored.");
        string parentProxy=Environment.GetEnvironmentVariable("HTTPS_PROXY");
        ProcessStartInfo info=Launcher.CreateStartInfo(settings);
        Assert(info.EnvironmentVariables["ALL_PROXY"]=="http://127.0.0.1:8888","ALL_PROXY is missing.");
        Assert(parentProxy==Environment.GetEnvironmentVariable("HTTPS_PROXY"),"Parent environment was modified.");
        Console.WriteLine("PASS: configuration updates persist, and parent process environment stays unchanged.");
        string legacy=Path.Combine(root,"legacy-program","AntigravityProxyLauncher.settings.json"); settings.Save(legacy);
        SettingsStore migrated=new SettingsStore(Path.Combine(root,"migrated-data","settings.json"),legacy);
        Assert(migrated.Load(out notice).ProxyPort==8888 && !File.Exists(legacy),"Legacy configuration migration failed.");
        string blocked=Path.Combine(root,"blocked-directory"); File.WriteAllText(blocked,"Not a directory");
        string retained=Path.Combine(root,"retained-legacy","AntigravityProxyLauncher.settings.json"); settings.Save(retained);
        SettingsStore blockedStore=new SettingsStore(Path.Combine(blocked,"settings.json"),retained);
        Assert(blockedStore.Load(out notice).ProxyPort==8888 && File.Exists(retained) && notice!=null,"Failed migration lost the original configuration.");
        Console.WriteLine("PASS: legacy migration preserves settings and retains the original if migration fails.");
    }
}