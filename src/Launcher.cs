using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("Antigravity Proxy Launcher")]
[assembly: AssemblyDescription("A local, per-process proxy launcher for Antigravity")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace AntigravityProxyLauncher
{
    [DataContract]
    public sealed class Settings
    {
        [DataMember] public string ApplicationPath;
        [DataMember] public string ProxyHost;
        [DataMember] public int ProxyPort;

        public static Settings Defaults()
        {
            return new Settings {
                ApplicationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "Antigravity.exe"),
                ProxyHost = "127.0.0.1", ProxyPort = 10808
            };
        }

        public Uri ProxyUri()
        {
            string host = (ProxyHost ?? "").Trim().Trim('[', ']');
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown || ProxyPort < 1 || ProxyPort > 65535)
                throw new ArgumentException("请填写有效的代理地址和端口，例如 127.0.0.1 和 10808。");
            return new UriBuilder(Uri.UriSchemeHttp, host, ProxyPort).Uri;
        }

        public static Settings Load(string file)
        {
            using (FileStream stream = File.OpenRead(file)) {
                if (stream.Length > 65536) throw new InvalidDataException("Settings file is too large.");
                Settings settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                settings.ProxyUri();
                if (String.IsNullOrWhiteSpace(settings.ApplicationPath)) throw new InvalidDataException("Missing application path.");
                return settings;
            }
        }

        public void Save(string file)
        {
            // Write to a temporary sibling first, so a failed write preserves the previous settings.
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            string temp = file + ".tmp";
            try {
                using (FileStream stream = File.Create(temp))
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, this);
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            } finally {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }

    public sealed class SettingsStore
    {
        public readonly string CurrentFile;
        public readonly string LegacyFile;

        public SettingsStore(string currentFile, string legacyFile)
        {
            CurrentFile = currentFile;
            LegacyFile = legacyFile;
        }

        public static SettingsStore ForCurrentUser()
        {
            return new SettingsStore(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AntigravityProxyLauncher", "settings.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AntigravityProxyLauncher.settings.json"));
        }

        public Settings Load(out string notice)
        {
            notice = null;
            if (File.Exists(CurrentFile)) {
                try { return Settings.Load(CurrentFile); }
                catch {
                    notice = "上次设置无法读取，已恢复默认值。请检查程序位置和代理端口。";
                    return Settings.Defaults();
                }
            }
            if (!String.IsNullOrEmpty(LegacyFile) && File.Exists(LegacyFile)) {
                Settings previous;
                try { previous = Settings.Load(LegacyFile); }
                catch {
                    notice = "旧版设置无法读取，已恢复默认值。原配置文件已保留。";
                    return Settings.Defaults();
                }
                try { previous.Save(CurrentFile); }
                catch {
                    notice = "已读取旧版设置，但暂时无法迁移保存。原配置文件已保留。";
                    return previous;
                }
                // Remove only the known legacy sidecar after a successful, atomic save.
                try {
                    File.Delete(LegacyFile);
                    notice = "已迁移旧版设置。以后移动本工具也会记住当前设置。";
                } catch {
                    notice = "已迁移旧版设置；软件旁的旧配置未能移除，可以手动删除。";
                }
                return previous;
            }
            return Settings.Defaults();
        }

        public void Save(Settings settings) { settings.Save(CurrentFile); }
    }
    public static class Launcher
    {
        public static ProcessStartInfo CreateStartInfo(Settings settings)
        {
            Uri proxy = settings.ProxyUri();
            string path = Path.GetFullPath(settings.ApplicationPath);
            if (!File.Exists(path) || !String.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException("找不到 Antigravity 程序，请点击“浏览”选择正确的 exe 文件。");
            if (String.Equals(path, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase) || String.Equals(Path.GetFileName(path), "Antigravity-Proxy-Launcher.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("请选择 Antigravity 主程序，而不是代理启动器本身。");
            ProcessStartInfo info = new ProcessStartInfo(path);
            info.UseShellExecute = false;
            info.WorkingDirectory = Path.GetDirectoryName(path);
            string proxyUrl = proxy.GetLeftPart(UriPartial.Authority);
            info.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
            info.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
            info.EnvironmentVariables["ALL_PROXY"] = proxyUrl;
            info.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.1,::1";
            return info;
        }

        public static void CheckLocalProxy(Settings settings)
        {
            Uri uri = settings.ProxyUri();
            using (TcpClient client = new TcpClient()) {
                IAsyncResult pending = client.BeginConnect(uri.Host, uri.Port, null, null);
                using (System.Threading.WaitHandle wait = pending.AsyncWaitHandle) {
                    if (!wait.WaitOne(2500)) throw new TimeoutException("代理端口连接超时，请确认代理软件正在运行，地址和端口填写正确。");
                    try { client.EndConnect(pending); }
                    catch (SocketException) { throw new IOException("无法连接代理端口，请确认代理软件正在运行，地址和端口填写正确。"); }
                }
            }
        }

        public static void ProbeGoogle(Settings settings)
        {
            CheckLocalProxy(settings);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://oauth2.googleapis.com/token");
            request.Method = "HEAD";
            request.Proxy = new WebProxy(settings.ProxyUri());
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.AllowAutoRedirect = false;
            request.KeepAlive = false;
            try {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    CheckStatus(response);
            } catch (WebException error) {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response != null) {
                    using (response) CheckStatus(response);
                    return;
                }
                if (error.Status == WebExceptionStatus.Timeout)
                    throw new IOException("代理端口可连接，但 Google 登录接口连接超时。请检查或更换节点。", error);
                if (error.Status == WebExceptionStatus.TrustFailure || error.Status == WebExceptionStatus.SecureChannelFailure)
                    throw new IOException("无法与 Google 建立安全连接，请检查节点和系统时间。", error);
                throw new IOException("代理端口可连接，但尚未连通 Google 登录接口。请检查节点或 DNS。", error);
            }
        }

        private static void CheckStatus(HttpWebResponse response)
        {
            int status = (int)response.StatusCode;
            // An unauthenticated HEAD can return 400/401/403/404/405: all prove HTTP connectivity.
            if (status == 407) throw new IOException("代理要求身份验证。请使用代理软件提供的本机无需认证的 HTTP 或 mixed 端口。");
            if (status >= 500) throw new IOException("Google 登录接口或代理返回异常，请更换节点后重试。");
        }
    }

    internal sealed class BrandMark : Control
    {
        public BrandMark() { DoubleBuffered = true; Size = new Size(48, 48); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush back = new SolidBrush(Color.FromArgb(37, 52, 83)))
                e.Graphics.FillEllipse(back, 0, 0, 47, 47);
            using (Pen line = new Pen(Color.FromArgb(115, 178, 255), 3)) {
                e.Graphics.DrawLine(line, 13, 32, 24, 13);
                e.Graphics.DrawLine(line, 24, 13, 36, 32);
                e.Graphics.DrawLine(line, 18, 26, 31, 26);
            }
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(162, 212, 255)))
                e.Graphics.FillEllipse(dot, 32, 28, 7, 7);
        }
    }

    public sealed class MainForm : Form
    {
        private readonly TextBox appPath = new TextBox();
        private readonly TextBox host = new TextBox();
        private readonly NumericUpDown port = new NumericUpDown();
        private readonly Button browse = new Button();
        private readonly Button test = new Button();
        private readonly Button launch = new Button();
        private readonly Button save = new Button();
        private readonly Button setup = new Button();
        private readonly Panel statusBox = new Panel();
        private readonly Label status = new Label();
        private readonly SettingsStore settingsStore = SettingsStore.ForCurrentUser();
        private bool busy;
        private static readonly Color Ink = Color.FromArgb(27, 39, 62);
        private static readonly Color Muted = Color.FromArgb(99, 114, 137);
        private static readonly Color Blue = Color.FromArgb(40, 103, 225);

        public MainForm() : this(null) { }

        public MainForm(string initialNotice)
        {
            Text = "Antigravity 代理启动器";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 584);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 251);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildUi();
            string settingsNotice;
            Settings settings = settingsStore.Load(out settingsNotice);

            appPath.Text = settings.ApplicationPath;
            host.Text = settings.ProxyHost;
            port.Value = settings.ProxyPort;
            ShowStatus(initialNotice ?? settingsNotice ?? "首次使用请填写连接设置，再点击“配置启动图标”。以后只需点击桌面图标。", false, initialNotice != null);
        }

        private Label MakeLabel(Control parent, string text, int x, int y, int width, int height, Color color, float size, bool bold)
        {
            Label label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height),
                ForeColor = color, BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft };
            parent.Controls.Add(label);
            return label;
        }

        private void StyleButton(Button button, string text, Rectangle bounds, bool primary)
        {
            button.Text = text;
            button.Bounds = bounds;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(210, 219, 232);
            button.BackColor = primary ? Blue : Color.White;
            button.ForeColor = primary ? Color.White : Ink;
            button.Font = new Font("Microsoft YaHei UI", 10F, primary ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        private void BuildUi()
        {
            Panel header = new Panel { Bounds = new Rectangle(0, 0, 720, 104), BackColor = Ink };
            Controls.Add(header);
            header.Controls.Add(new BrandMark { Location = new Point(25, 27) });
            MakeLabel(header, "Antigravity 代理启动器", 91, 23, 580, 34, Color.White, 19F, true);
            MakeLabel(header, "连接本地代理，让登录和后台请求使用同一代理", 93, 62, 590, 21, Color.FromArgb(189, 205, 229), 9F, false);

            Panel card = new Panel { Bounds = new Rectangle(24, 124, 672, 332), BackColor = Color.White };
            Controls.Add(card);
            MakeLabel(card, "连接设置", 20, 15, 610, 29, Ink, 12F, true);
            MakeLabel(card, "支持 HTTP / mixed 代理；代理软件需保持运行，无需开启 TUN。", 20, 49, 630, 24, Muted, 9F, false);
            MakeLabel(card, "Antigravity 程序", 20, 80, 610, 24, Ink, 9F, false);
            appPath.Bounds = new Rectangle(20, 108, 532, 32);
            appPath.AutoSize = false;
            appPath.Font = new Font("Segoe UI", 10F);
            appPath.AccessibleName = "Antigravity 程序路径";
            appPath.TabIndex = 0;
            card.Controls.Add(appPath);
            StyleButton(browse, "浏览…", new Rectangle(564, 106, 88, 34), false);
            browse.TabIndex = 1;
            card.Controls.Add(browse);
            browse.Click += delegate {
                using (OpenFileDialog dialog = new OpenFileDialog()) {
                    dialog.Title = "选择 Antigravity 程序";
                    dialog.Filter = "Windows 程序 (*.exe)|*.exe";
                    dialog.CheckFileExists = true;
                    if (File.Exists(appPath.Text)) dialog.FileName = appPath.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) appPath.Text = dialog.FileName;
                }
            };

            MakeLabel(card, "HTTP / mixed 代理地址", 20, 156, 420, 24, Ink, 9F, false);
            MakeLabel(card, "端口", 464, 156, 180, 24, Ink, 9F, false);
            host.Bounds = new Rectangle(20, 184, 420, 32);
            host.AutoSize = false;
            host.Font = new Font("Segoe UI", 11F);
            host.AccessibleName = "代理地址";
            host.TabIndex = 2;
            card.Controls.Add(host);
            port.Bounds = new Rectangle(464, 184, 188, 32);
            port.Font = new Font("Segoe UI", 11F);
            port.Minimum = 1; port.Maximum = 65535;
            port.AccessibleName = "代理端口";
            port.TabIndex = 3;
            card.Controls.Add(port);

            statusBox.Bounds = new Rectangle(20, 238, 632, 72);
            card.Controls.Add(statusBox);
            status.Bounds = new Rectangle(15, 8, 602, 56);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.UseMnemonic = false;
            status.AccessibleName = "连接状态";
            statusBox.Controls.Add(status);

            StyleButton(test, "测试连接", new Rectangle(24, 478, 128, 44), false);
            StyleButton(save, "保存设置", new Rectangle(166, 478, 128, 44), false);
            StyleButton(launch, "启动一次", new Rectangle(308, 478, 128, 44), false);
            StyleButton(setup, "配置启动图标", new Rectangle(450, 478, 246, 44), true);
            test.TabIndex = 4; save.TabIndex = 5; launch.TabIndex = 6; setup.TabIndex = 7;
            Controls.Add(test); Controls.Add(save); Controls.Add(launch); Controls.Add(setup);
            test.Click += async delegate { await TestConnection(); };
            launch.Click += async delegate { await LaunchApp(); };
            save.Click += delegate { SaveSettings(); };
            setup.Click += delegate { ConfigureShortcuts(); };
            MakeLabel(this, "修改配置：双击本工具，或在开始菜单搜索“Antigravity 代理设置”。", 25, 539, 670, 25, Muted, 9F, false);
            AcceptButton = setup;
        }

        private Settings ReadSettings()
        {
            Settings settings = new Settings { ApplicationPath = appPath.Text.Trim().Trim('"'), ProxyHost = host.Text.Trim(), ProxyPort = (int)port.Value };
            settings.ProxyUri();
            return settings;
        }

        private void ShowStatus(string text, bool success, bool error)
        {
            status.Text = text;
            status.ForeColor = error ? Color.FromArgb(164, 49, 49) : success ? Color.FromArgb(24, 112, 78) : Muted;
            statusBox.BackColor = error ? Color.FromArgb(255, 240, 240) : success ? Color.FromArgb(234, 247, 240) : Color.FromArgb(242, 246, 252);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            browse.Enabled = test.Enabled = launch.Enabled = save.Enabled = setup.Enabled = appPath.Enabled = host.Enabled = port.Enabled = !value;
            UseWaitCursor = value;
        }

        private void SaveSettings()
        {
            if (busy) return;
            try {
                Settings settings = ReadSettings();
                Launcher.CreateStartInfo(settings);
                settingsStore.Save(settings);
                ShowStatus("设置已保存。已配置的代理启动图标下次会自动使用新设置。", true, false);
            } catch (Exception error) { ShowStatus(error.Message, false, true); }
        }

        private void ConfigureShortcuts()
        {
            if (busy) return;
            try {
                Settings settings = ReadSettings();
                SetBusy(true);
                ShortcutSetup.Configure(settings, settingsStore, Application.ExecutablePath,
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs));
                ShowStatus("已配置完成！以后点击桌面“Antigravity（代理）”即可静默启动。修改设置可从开始菜单打开“Antigravity 代理设置”。", true, false);
            } catch (Exception error) { ShowStatus(error.Message, false, true); }
            finally { SetBusy(false); }
        }
        private async Task TestConnection()
        {
            if (busy) return;
            try {
                Settings settings = ReadSettings();
                SetBusy(true);
                ShowStatus("正在通过代理检查 Google 登录接口，请稍候…", false, false);
                await Task.Run(delegate { Launcher.ProbeGoogle(settings); });
                if (!IsDisposed) ShowStatus("已通过代理连接 Google 登录接口。可以启动并测试登录。", true, false);
            } catch (Exception error) {
                if (!IsDisposed) ShowStatus(error.Message, false, true);
            } finally { if (!IsDisposed) SetBusy(false); }
        }

        private async Task LaunchApp()
        {
            if (busy) return;
            try {
                Settings settings = ReadSettings();
                ProcessStartInfo startInfo = Launcher.CreateStartInfo(settings);
                string processName = Path.GetFileNameWithoutExtension(startInfo.FileName);
                Process[] running = Process.GetProcessesByName(processName);
                bool alreadyRunning = running.Length != 0;
                foreach (Process process in running) process.Dispose();
                if (alreadyRunning) {
                    ShowStatus("Antigravity 已在运行。请保存工作并完全退出，再点击“启动一次”。", false, true);
                    return;
                }
                SetBusy(true);
                ShowStatus("正在检查本地代理并启动 Antigravity…", false, false);
                await Task.Run(delegate { Launcher.CheckLocalProxy(settings); });
                if (IsDisposed) return;
                bool saved = true;
                try { settingsStore.Save(settings); } catch { saved = false; }
                using (Process child = Process.Start(startInfo)) {
                    if (child == null) throw new IOException("未能启动 Antigravity，请检查程序位置。");
                }
                ShowStatus(saved ? "已启动。请在 Antigravity 中完成登录，再发送一条 AI 消息验证。" : "已启动，但本次设置未能保存。下次可能需要重新填写。", saved, !saved);
                if (saved) Close();
            } catch (Exception error) {
                if (!IsDisposed) ShowStatus(error.Message, false, true);
            } finally { if (!IsDisposed) SetBusy(false); }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool silent = Array.Exists(args, delegate(string arg) { return String.Equals(arg, "--launch", StringComparison.OrdinalIgnoreCase); });
            if (silent) {
                try { SilentStart.Start(SettingsStore.ForCurrentUser()); return; }
                catch (Exception error) { Application.Run(new MainForm(error.Message)); return; }
            }
            Application.Run(new MainForm());
        }
    }
}