using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Microsoft.Win32;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("PAVVPN")]
[assembly: AssemblyDescription("PAVVPN Native desktop controller")]
[assembly: AssemblyCompany("PAVVPN contributors")]
[assembly: AssemblyProduct("PAVVPN")]
[assembly: AssemblyCopyright("Copyright (c) 2026 PAVVPN contributors")]
[assembly: AssemblyVersion("5.0.0.0")]
[assembly: AssemblyFileVersion("5.0.0.0")]

namespace PavVpnDesktop
{
    static class App
    {
        const string MutexName = @"Local\PAVVPN.Native.UI";
        const string ShowEventName = @"Local\PAVVPN.Native.UI.Show";

        [STAThread]
        static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--self-test") >= 0) return SelfTest();
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                    return 0;
                }
                bool eventCreated;
                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName, out eventCreated))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var form = new MainForm(Array.IndexOf(args, "--startup") >= 0);
                    var waiter = new Thread(delegate()
                    {
                        while (!form.IsDisposed)
                        {
                            showEvent.WaitOne();
                            if (form.IsDisposed) break;
                            try { form.BeginInvoke(new Action(form.ShowFromTray)); } catch { break; }
                        }
                    });
                    waiter.IsBackground = true; waiter.Start();
                    Application.Run(form);
                }
            }
            return 0;
        }

        static int SelfTest()
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                if (!File.Exists(Path.Combine(dir, "PAVVPN.Native.v5.exe"))) throw new IOException("Native motor bulunamadi.");
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PAVVPN.Logo"))
                {
                    if (stream == null || stream.Length < 1024) throw new IOException("Gomulu logo bulunamadi.");
                }
                Console.WriteLine("[OK] PAVVPN GUI motor ve logo dogrulamasi gecti.");
                return 0;
            }
            catch (Exception ex) { Console.WriteLine("[HATA] " + ex.Message); return 1; }
        }
    }

    sealed class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(10, 13, 19);
        static readonly Color Card = Color.FromArgb(20, 25, 35);
        static readonly Color Border = Color.FromArgb(40, 49, 65);
        static readonly Color Gold = Color.FromArgb(244, 183, 47);
        static readonly Color Green = Color.FromArgb(48, 209, 88);
        static readonly Color Muted = Color.FromArgb(143, 153, 173);
        const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string StartupValueName = "PAVVPN";
        readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly string EnginePath;
        readonly string LegacyStartupPath;
        readonly StatusToggle Toggle;
        readonly Label Status;
        readonly Label Detail;
        readonly SettingSwitch StartWithWindows;
        readonly NotifyIcon Tray;
        readonly System.Windows.Forms.Timer Poll;
        bool Updating;
        bool AllowClose;
        bool Busy;
        bool HealthCheckRunning;

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        public MainForm(bool startHidden)
        {
            EnginePath = Path.Combine(BaseDir, "PAVVPN.Native.v5.exe");
            LegacyStartupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "PAVVPN_AutoStart.vbs");
            Text = "PAVVPN"; ClientSize = new Size(440, 610); BackColor = Bg; ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.None; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true; Padding = new Padding(1);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            var top = new Panel { BackColor = Color.FromArgb(13, 17, 24), Location = new Point(1, 1), Size = new Size(438, 44) };
            Controls.Add(top);
            var miniLogo = new PictureBox { Size = new Size(28, 28), Location = new Point(14, 8), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Image = LoadLogo() };
            top.Controls.Add(miniLogo);
            var appName = NewLabel("PAVVPN", 10, FontStyle.Bold, Color.White, new Rectangle(50, 11, 90, 23)); top.Controls.Add(appName);
            var nativeBadge = NewLabel("NATIVE 5", 7, FontStyle.Bold, Gold, new Rectangle(115, 13, 65, 18)); top.Controls.Add(nativeBadge);
            var minimize = WindowButton("—", 350); minimize.Click += delegate { WindowState = FormWindowState.Minimized; }; top.Controls.Add(minimize);
            var close = WindowButton("×", 394); close.Click += delegate { Close(); }; top.Controls.Add(close);
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } };
            top.MouseDown += drag; appName.MouseDown += drag; nativeBadge.MouseDown += drag;

            var logo = new PictureBox { Size = new Size(126, 126), Location = new Point(157, 63), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Image = LoadLogo() };
            Controls.Add(logo);

            var title = NewLabel("PAVVPN", 25, FontStyle.Bold, Color.White, new Rectangle(20, 190, 400, 42)); title.TextAlign = ContentAlignment.MiddleCenter; Controls.Add(title);
            var sub = NewLabel("Discord için hızlı, yerel ve sürücüsüz bağlantı", 9, FontStyle.Regular, Muted, new Rectangle(20, 231, 400, 24)); sub.TextAlign = ContentAlignment.MiddleCenter; Controls.Add(sub);

            var card = new RoundedPanel { FillColor = Card, BorderColor = Card, Radius = 20, Location = new Point(34, 270), Size = new Size(372, 218) }; Controls.Add(card);
            var cardTitle = NewLabel("DISCORD BAĞLANTISI", 7, FontStyle.Bold, Gold, new Rectangle(20, 17, 332, 18)); cardTitle.TextAlign = ContentAlignment.MiddleCenter; card.Controls.Add(cardTitle);
            Status = NewLabel("Kontrol ediliyor", 15, FontStyle.Bold, Color.White, new Rectangle(20, 42, 332, 31)); Status.TextAlign = ContentAlignment.MiddleCenter; card.Controls.Add(Status);
            Detail = NewLabel("127.0.0.1:1088", 9, FontStyle.Regular, Muted, new Rectangle(20, 74, 332, 22)); Detail.TextAlign = ContentAlignment.MiddleCenter; card.Controls.Add(Detail);
            Toggle = new StatusToggle { Location = new Point(105, 108), Size = new Size(162, 62), Cursor = Cursors.Hand };
            Toggle.Click += delegate { SetConnection(!Toggle.IsOn); }; card.Controls.Add(Toggle);
            var scope = NewLabel("YALNIZ DISCORD   •   127.0.0.1   •   SÜRÜCÜSÜZ", 7, FontStyle.Bold, Muted, new Rectangle(15, 184, 342, 18)); scope.TextAlign = ContentAlignment.MiddleCenter; card.Controls.Add(scope);

            StartWithWindows = new SettingSwitch { Text = "Windows ile otomatik başlat", Location = new Point(90, 507), Size = new Size(260, 42), ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9), Cursor = Cursors.Hand };
            StartWithWindows.CheckedChanged += delegate { if (!Updating) SetStartup(StartWithWindows.Checked); }; Controls.Add(StartWithWindows);
            var remove = new LinkLabel { Text = "PAVVPN'i tamamen kaldır", AutoSize = true, Location = new Point(151, 558), LinkColor = Muted, ActiveLinkColor = Gold, VisitedLinkColor = Muted, Font = new Font("Segoe UI", 8) };
            remove.LinkClicked += delegate { RunUninstaller(); }; Controls.Add(remove);
            var footer = NewLabel("Açık kaynak  •  MIT  •  v5.0", 7, FontStyle.Regular, Color.FromArgb(78, 88, 106), new Rectangle(20, 584, 400, 16)); footer.TextAlign = ContentAlignment.MiddleCenter; Controls.Add(footer);

            var menu = new ContextMenuStrip();
            menu.Items.Add("PAVVPN'i Aç", null, delegate { ShowFromTray(); });
            menu.Items.Add("Bağlantıyı Aç / Kapat", null, delegate { SetConnection(!Toggle.IsOn); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Arayüzden Çık", null, delegate { AllowClose = true; Close(); });
            Tray = new NotifyIcon { Icon = Icon, Text = "PAVVPN Native", Visible = true, ContextMenuStrip = menu };
            Tray.DoubleClick += delegate { ShowFromTray(); };

            Poll = new System.Windows.Forms.Timer { Interval = 1000 };
            Poll.Tick += delegate { BeginHealthCheck(); };
            Poll.Start();
            Shown += delegate
            {
                ApplyRoundedRegion();
                Updating = true; StartWithWindows.Checked = IsStartupEnabled(); Updating = false;
                if (startHidden) { Hide(); ShowInTaskbar = false; SetConnection(true, true); }
                else BeginHealthCheck();
            };
            FormClosing += OnClosing;
            Resize += delegate { ApplyRoundedRegion(); };
        }

        Image LoadLogo()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PAVVPN.Logo"))
            using (var temporary = stream == null ? null : new Bitmap(stream))
                return temporary == null ? null : new Bitmap(temporary);
        }

        Button WindowButton(string text, int x)
        {
            var button = new Button { Text = text, Location = new Point(x, 1), Size = new Size(44, 42), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = Muted, Font = new Font("Segoe UI", 13), TabStop = false };
            button.FlatAppearance.BorderSize = 0; button.FlatAppearance.MouseOverBackColor = Color.FromArgb(31, 37, 48); button.FlatAppearance.MouseDownBackColor = Color.FromArgb(42, 49, 62);
            return button;
        }

        void ApplyRoundedRegion()
        {
            using (var path = Ui.RoundRect(new Rectangle(0, 0, Width, Height), 18)) Region = new Region(path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
        }

        static Label NewLabel(string text, float size, FontStyle style, Color color, Rectangle bounds)
        { return new Label { Text = text, Font = new Font("Segoe UI", size, style), ForeColor = color, BackColor = Color.Transparent, Bounds = bounds }; }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!AllowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; Hide(); ShowInTaskbar = false;
            }
            else { Tray.Visible = false; Poll.Stop(); }
        }

        public void ShowFromTray()
        { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); BringToFront(); }

        bool IsEngineRunning()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:1088/pavdns-ready");
                request.Proxy = null; request.Timeout = 600; request.ReadWriteTimeout = 600;
                using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd() == "PAVVPN/5.0-native";
            }
            catch { return false; }
        }

        bool IsProxyListening()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:1088/pavdns-version");
                request.Proxy = null; request.Timeout = 400; request.ReadWriteTimeout = 400;
                using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
                    return reader.ReadToEnd() == "PAVVPN/5.0-native";
            }
            catch { return false; }
        }

        void RefreshState()
        { RefreshState(IsEngineRunning()); }

        void BeginHealthCheck()
        {
            if (Busy || HealthCheckRunning || IsDisposed) return;
            HealthCheckRunning = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool active = IsEngineRunning();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        HealthCheckRunning = false;
                        if (!Busy) RefreshState(active);
                    }));
                }
                catch { HealthCheckRunning = false; }
            });
        }

        void RefreshState(bool active)
        {
            Toggle.IsOn = active;
            Status.Text = active ? "Bağlantı Açık" : "Bağlantı Kapalı";
            Status.ForeColor = active ? Green : Color.White;
            Detail.Text = active ? "Discord koruması etkin" : "Açmak için anahtara bas";
            Tray.Text = active ? "PAVVPN - Bağlantı Açık" : "PAVVPN - Bağlantı Kapalı";
        }

        void SetConnection(bool enable)
        { SetConnection(enable, false); }

        void SetConnection(bool enable, bool automatic)
        {
            if (Busy) return;
            Busy = true; Toggle.Enabled = false; Poll.Stop(); Toggle.IsOn = enable;
            Status.Text = enable ? "Bağlantı Açılıyor…" : "Bağlantı Kapatılıyor…";
            Status.ForeColor = Gold; Detail.Text = "Ayarlar güvenli biçimde uygulanıyor";
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool active = false;
                if (enable)
                {
                    // Windows/VDS oturum acilisinda ag gec hazir olabilir. Arayuzu
                    // gizli tut, hata penceresi cikarma ve sinirli olarak yeniden dene.
                    if (automatic) Thread.Sleep(8000);
                    int attempts = automatic ? 8 : 1;
                    for (int attempt = 0; attempt < attempts && !active; attempt++)
                    {
                        active = StartEngine();
                        if (!active && automatic && attempt + 1 < attempts)
                            Thread.Sleep(Math.Min(15000, 3000 + attempt * 2000));
                    }
                }
                else
                {
                    StopEngine();
                    active = IsEngineRunning();
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        Busy = false; Toggle.Enabled = true; RefreshState(active); Poll.Start();
                        if (enable && !active && !automatic) MessageBox.Show("Bağlantı başlatılamadı. pav_debug.log dosyasını kontrol edin.", "PAVVPN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));
                }
                catch { }
            });
        }

        bool StartEngine()
        {
            if (!File.Exists(EnginePath)) return false;
            if (IsEngineRunning()) return true;
            if (IsProxyListening())
            {
                for (int i = 0; i < 120 && IsProxyListening() && !IsEngineRunning(); i++) Thread.Sleep(250);
                if (IsEngineRunning()) return true;
                if (IsProxyListening()) return false;
            }
            Process process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo(EnginePath, "--launch") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = BaseDir });
                for (int i = 0; i < 120; i++)
                {
                    if (IsEngineRunning()) return true;
                    try { if (process != null && process.HasExited && !IsProxyListening()) break; } catch { break; }
                    Thread.Sleep(250);
                }
                return IsEngineRunning();
            }
            catch { return false; }
            finally { if (process != null) process.Dispose(); }
        }

        void StopEngine()
        {
            if (!File.Exists(EnginePath)) return;
            using (Process process = Process.Start(new ProcessStartInfo(EnginePath, "--stop") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = BaseDir }))
                if (process != null) process.WaitForExit(5000);
            for (int i = 0; i < 40 && IsProxyListening(); i++) Thread.Sleep(100);
        }

        void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistryPath))
                {
                    if (enabled) key.SetValue(StartupValueName, StartupCommand(), RegistryValueKind.String);
                    else key.DeleteValue(StartupValueName, false);
                }
                if (File.Exists(LegacyStartupPath)) File.Delete(LegacyStartupPath);
            }
            catch (Exception ex) { MessageBox.Show("Başlangıç ayarı değiştirilemedi: " + ex.Message, "PAVVPN", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        string StartupCommand()
        { return "\"" + Application.ExecutablePath + "\" --startup"; }

        bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
                    return key != null && string.Equals(Convert.ToString(key.GetValue(StartupValueName, "")), StartupCommand(), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        void RunUninstaller()
        {
            if (MessageBox.Show("PAVVPN tamamen kaldırılsın mı?", "PAVVPN", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string remover = Path.Combine(BaseDir, "service_remove.bat");
            if (!File.Exists(remover)) { MessageBox.Show("Kaldırma dosyası bulunamadı.", "PAVVPN"); return; }
            Process.Start(new ProcessStartInfo(remover) { UseShellExecute = true, WorkingDirectory = BaseDir });
            AllowClose = true; Close();
        }
    }

    sealed class StatusToggle : Control
    {
        readonly System.Windows.Forms.Timer Animation;
        float Position;
        bool TargetOn;
        public bool IsOn
        {
            get { return TargetOn; }
            set
            {
                if (TargetOn == value && Animation.Enabled) return;
                TargetOn = value;
                if (Math.Abs(Position - (value ? 1f : 0f)) < 0.01f) { Position = value ? 1f : 0f; Invalidate(); }
                else Animation.Start();
            }
        }
        public StatusToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; TabStop = false;
            Animation = new System.Windows.Forms.Timer { Interval = 15 };
            Animation.Tick += delegate
            {
                float destination = TargetOn ? 1f : 0f;
                Position += (destination - Position) * 0.24f;
                if (Math.Abs(destination - Position) < 0.012f) { Position = destination; Animation.Stop(); }
                Invalidate();
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) Animation.Dispose(); base.Dispose(disposing); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color track = Blend(Color.FromArgb(58, 67, 84), Color.FromArgb(42, 196, 83), Position);
            using (var path = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2)) using (var brush = new SolidBrush(track)) e.Graphics.FillPath(brush, path);
            int d = Height - 12; int x = 6 + (int)Math.Round((Width - d - 12) * Position);
            using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) e.Graphics.FillEllipse(shadow, x + 1, 8, d, d);
            using (var brush = new SolidBrush(Color.White)) e.Graphics.FillEllipse(brush, x, 6, d, d);
            string text = TargetOn ? "AÇIK" : "KAPALI";
            using (var font = new Font("Segoe UI", 9, FontStyle.Bold)) using (var brush = new SolidBrush(Color.White))
            {
                Rectangle area = TargetOn ? new Rectangle(8, 0, Width - d - 18, Height) : new Rectangle(d + 14, 0, Width - d - 18, Height);
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center }) e.Graphics.DrawString(text, font, brush, area, format);
            }
        }
        static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
        }
    }

    sealed class SettingSwitch : Control
    {
        readonly System.Windows.Forms.Timer Animation;
        float Position;
        bool Value;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return Value; }
            set
            {
                if (Value == value && Animation.Enabled) return;
                bool changed = Value != value; Value = value;
                if (Math.Abs(Position - (value ? 1f : 0f)) < 0.01f) { Position = value ? 1f : 0f; Invalidate(); }
                else Animation.Start();
                if (changed && CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }
        public SettingSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; TabStop = false;
            Animation = new System.Windows.Forms.Timer { Interval = 15 };
            Animation.Tick += delegate
            {
                float destination = Value ? 1f : 0f;
                Position += (destination - Position) * 0.25f;
                if (Math.Abs(destination - Position) < 0.012f) { Position = destination; Animation.Stop(); }
                Invalidate();
            };
        }
        protected override void Dispose(bool disposing) { if (disposing) Animation.Dispose(); base.Dispose(disposing); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var textBrush = new SolidBrush(ForeColor)) using (var format = new StringFormat { LineAlignment = StringAlignment.Center })
                e.Graphics.DrawString(Text, Font, textBrush, new Rectangle(0, 0, Width - 60, Height), format);
            Rectangle trackBounds = new Rectangle(Width - 50, 10, 46, 24);
            Color track = Blend(Color.FromArgb(49, 57, 72), Color.FromArgb(42, 196, 83), Position);
            using (var path = Ui.RoundRect(trackBounds, 12)) using (var brush = new SolidBrush(track)) e.Graphics.FillPath(brush, path);
            int knobX = trackBounds.X + 3 + (int)Math.Round(22 * Position);
            using (var brush = new SolidBrush(Color.White)) e.Graphics.FillEllipse(brush, knobX, trackBounds.Y + 3, 18, 18);
        }
        static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Max(0f, Math.Min(1f, amount));
            return Color.FromArgb((int)(from.R + (to.R - from.R) * amount), (int)(from.G + (to.G - from.G) * amount), (int)(from.B + (to.B - from.B) * amount));
        }
    }

    sealed class RoundedPanel : Panel
    {
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public int Radius { get; set; }
        public RoundedPanel() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent; }
        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (Width > 0 && Height > 0) using (var path = Ui.RoundRect(new Rectangle(0, 0, Width, Height), Radius)) Region = new Region(path);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var brush = new SolidBrush(FillColor)) using (var pen = new Pen(BorderColor)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }
    }

    static class Ui
    {
        public static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
    }
}
