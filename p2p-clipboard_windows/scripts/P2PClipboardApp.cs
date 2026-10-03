using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class DesktopAccess
{
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder text, int length, out int needed);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr handle);
    public static string CurrentName
    {
        get
        {
            int needed; StringBuilder text = new StringBuilder(512);
            return GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, text, 1024, out needed) ? text.ToString() : "unknown";
        }
    }
}

public sealed class ConnectionTracker
{
    public const string MacPeerId = "12D3KooWBgMhRLxRLAcQPZjiJAjVJoLDY3ankxxN1jTXLok1hRtK";
    private readonly HashSet<string> peers = new HashSet<string>();
    public string LocalPeerId { get; private set; }
    public string LastEvent { get; private set; }
    public bool Listening { get; private set; }
    public bool MacConnected { get { return peers.Contains(MacPeerId); } }
    public int PeerCount { get { return peers.Count; } }
    public ConnectionTracker() { Reset(); }
    public void Reset()
    {
        peers.Clear(); Listening = false; LocalPeerId = "等待核心启动"; LastEvent = "暂无连接事件";
    }
    public void ClearConnections() { peers.Clear(); Listening = false; }
    public void ReadLine(string line)
    {
        Match match = Regex.Match(line, @"Local peer id:\s*(\S+)");
        if (match.Success) LocalPeerId = match.Groups[1].Value;
        if (line.Contains("Local node is listening on /ip4/100.64.79.70/tcp/55515")) Listening = true;
        match = Regex.Match(line, @"Connected to peer\s+(\S+)");
        if (match.Success)
        {
            string peer = match.Groups[1].Value;
            peers.Add(peer);
            LastEvent = DateTime.Now.ToString("HH:mm:ss") + " 已连接 " + (peer == MacPeerId ? "Mac" : peer);
        }
        match = Regex.Match(line, @"Peer\s+(\S+)\s+has disconnected");
        if (match.Success)
        {
            string peer = match.Groups[1].Value;
            peers.Remove(peer);
            LastEvent = DateTime.Now.ToString("HH:mm:ss") + " 已断开 " + (peer == MacPeerId ? "Mac" : peer);
        }
    }
}

internal sealed class ClipboardWindow : Form
{
    private readonly string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    private readonly ConnectionTracker connection = new ConnectionTracker();
    private readonly Label stateLabel = new Label();
    private readonly Label networkLabel = new Label();
    private readonly Label listenerLabel = new Label();
    private readonly Label peerLabel = new Label();
    private readonly Label eventLabel = new Label();
    private readonly Button startButton = new Button();
    private readonly Button stopButton = new Button();
    private readonly NotifyIcon tray = new NotifyIcon();
    private readonly ToolStripMenuItem trayState = new ToolStripMenuItem();
    private readonly ToolStripMenuItem trayNetwork = new ToolStripMenuItem();
    private readonly ToolStripMenuItem trayEvent = new ToolStripMenuItem();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly EventWaitHandle showEvent;
    private readonly EventWaitHandle exitEvent;
    private readonly object logGate = new object();
    private StreamWriter output;
    private StreamWriter errors;
    private Process worker;
    private bool enabled = true;
    private bool quitting;
    private bool networkReady;
    private bool inTick;
    private bool storageWarning;
    private DateTime retryAfter = DateTime.MinValue;
    private string failure = "";
    private string snapshotPath;

    public ClipboardWindow(EventWaitHandle show, EventWaitHandle stop)
    {
        showEvent = show; exitEvent = stop;
        Text = "P2P Clipboard";
        Font = new Font("Microsoft YaHei UI", 10);
        BackColor = Color.White;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = new Size(640, 340);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        stateLabel.SetBounds(24, 22, 590, 42);
        stateLabel.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
        Controls.Add(stateLabel);
        AddRow(networkLabel, 82);
        AddRow(listenerLabel, 116);
        Label remote = new Label(); remote.Text = "Mac 地址：100.64.25.71:55516（由 Mac 主动连接）"; AddRow(remote, 150);
        AddRow(peerLabel, 184); peerLabel.Font = new Font("Consolas", 8.5f);
        AddRow(eventLabel, 218);
        startButton.Text = "启动 / 重试"; startButton.SetBounds(24, 279, 132, 36);
        startButton.Click += delegate { enabled = true; retryAfter = DateTime.MinValue; failure = ""; Tick(); };
        Controls.Add(startButton);
        stopButton.Text = "停止同步"; stopButton.SetBounds(172, 279, 132, 36);
        stopButton.Click += delegate { enabled = false; StopWorker(); RefreshDisplay(); SaveSnapshot(); };
        Controls.Add(stopButton);
        Button logsButton = new Button(); logsButton.Text = "查看日志"; logsButton.SetBounds(320, 279, 132, 36);
        logsButton.Click += delegate { OpenLogs(); }; Controls.Add(logsButton);
        Button exitButton = new Button(); exitButton.Text = "退出软件"; exitButton.SetBounds(468, 279, 132, 36);
        exitButton.Click += delegate { Quit(); }; Controls.Add(exitButton);

        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("打开状态窗口", null, delegate { ShowWindow(); });
        trayState.Enabled = trayNetwork.Enabled = trayEvent.Enabled = false;
        menu.Items.Add(trayState); menu.Items.Add(trayNetwork);
        menu.Items.Add(new ToolStripMenuItem("本机监听：100.64.79.70:55515") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Mac 地址：100.64.25.71:55516") { Enabled = false });
        menu.Items.Add(trayEvent); menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("启动 / 重试", null, delegate { enabled = true; retryAfter = DateTime.MinValue; failure = ""; Tick(); });
        menu.Items.Add("停止同步", null, delegate { enabled = false; StopWorker(); RefreshDisplay(); SaveSnapshot(); });
        menu.Items.Add("查看日志", null, delegate { OpenLogs(); });
        menu.Items.Add("退出", null, delegate { Quit(); });
        tray.Icon = Icon; tray.Text = "P2P Clipboard"; tray.ContextMenuStrip = menu; tray.Visible = true;
        tray.DoubleClick += delegate { ShowWindow(); };
        menu.Opening += delegate { Tick(); };

        string logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        snapshotPath = Path.Combine(logs, "app-status.json");
        output = OpenWriter(Path.Combine(logs, "p2p-clipboard.log"));
        errors = OpenWriter(Path.Combine(logs, "p2p-clipboard-error.log"));
        timer.Interval = 1000;
        timer.Tick += delegate { Tick(); };
        Shown += delegate { timer.Start(); Tick(); };
        FormClosing += ClosingWindow;
    }

    private void AddRow(Label label, int y) { label.SetBounds(24, y, 590, 27); label.AutoEllipsis = true; Controls.Add(label); }
    private static StreamWriter OpenWriter(string path)
    {
        StreamWriter writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
        writer.AutoFlush = true; return writer;
    }
    private static bool NetworkReady()
    {
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (UnicastIPAddressInformation address in nic.GetIPProperties().UnicastAddresses)
                if (address.Address.ToString() == "100.64.79.70") return true;
        }
        return false;
    }
    private void Tick()
    {
        if (quitting || inTick) return;
        inTick = true;
        try
        {
            if (exitEvent.WaitOne(0)) { Quit(); return; }
            if (showEvent.WaitOne(0)) ShowWindow();
            networkReady = NetworkReady();
            if (worker != null && worker.HasExited)
            {
                int code = worker.ExitCode;
                worker.WaitForExit(); worker.Dispose(); worker = null;
                connection.ClearConnections(); retryAfter = DateTime.UtcNow.AddSeconds(5);
                failure = "核心已退出（" + code + "），即将重试";
                WriteLine("Core exited: " + code, false);
            }
            if (!networkReady && worker != null) { StopWorker(); failure = ""; }
            if (enabled && networkReady && worker == null && DateTime.UtcNow >= retryAfter) StartWorker();
            RefreshDisplay(); SaveSnapshot();
        }
        catch (Exception ex)
        {
            failure = ex.Message; retryAfter = DateTime.UtcNow.AddSeconds(5);
            WriteLine("App error: " + ex, true); RefreshDisplay();
        }
        finally { inTick = false; }
    }
    private void StartWorker()
    {
        string binary = Path.Combine(root, "bin", "p2p-clipboard.exe");
        if (!File.Exists(binary)) { enabled = false; failure = "缺少 bin\\p2p-clipboard.exe"; return; }
        foreach (Process existing in Process.GetProcessesByName("p2p-clipboard"))
        {
            try
            {
                if (String.Equals(existing.MainModule.FileName, binary, StringComparison.OrdinalIgnoreCase))
                { failure = "外部同步进程正在运行，请先通过 stop.cmd 停止"; retryAfter = DateTime.UtcNow.AddSeconds(5); return; }
            }
            catch (System.ComponentModel.Win32Exception) { }
            finally { existing.Dispose(); }
        }
        connection.Reset(); failure = "";
        Process process = new Process();
        process.StartInfo = new ProcessStartInfo(binary, "--listen 100.64.79.70:55515 --no-mdns");
        process.StartInfo.WorkingDirectory = root;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args) { Receive(process, args.Data, false); };
        process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs args) { Receive(process, args.Data, true); };
        try
        {
            process.Start(); worker = process;
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            WriteLine("App started listener; PID=" + process.Id, false);
        }
        catch { process.Dispose(); throw; }
    }
    private void Receive(Process source, string line, bool error)
    {
        if (line == null) return;
        WriteLine(line, error);
        try
        {
            if (IsHandleCreated && !IsDisposed && !quitting)
                BeginInvoke((MethodInvoker)delegate
                {
                    if (worker != source || quitting) return;
                    connection.ReadLine(line); RefreshDisplay(); SaveSnapshot();
                });
        }
        catch (InvalidOperationException) { }
    }
    private void WriteLine(string line, bool error)
    {
        lock (logGate)
        {
            StreamWriter writer = error ? errors : output;
            if (writer != null) writer.WriteLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + line);
        }
    }
    private void StopWorker()
    {
        Process process = worker; worker = null;
        connection.ClearConnections();
        if (process != null)
        {
            try { if (!process.HasExited) process.Kill(); process.WaitForExit(); }
            finally { process.Dispose(); }
            WriteLine("App stopped listener.", false);
        }
    }
    private string StateText()
    {
        if (!enabled) return String.IsNullOrEmpty(failure) ? "已停止同步" : failure;
        if (!networkReady) return "等待 NetBird 上线";
        if (worker == null) return String.IsNullOrEmpty(failure) ? "正在启动" : failure;
        if (connection.MacConnected) return "已连接 Mac";
        if (connection.PeerCount > 0) return "已连接其他设备（" + connection.PeerCount + "）";
        return connection.Listening ? "等待 Mac 连接" : "正在启动监听";
    }
    private void RefreshDisplay()
    {
        string state = StateText();
        stateLabel.Text = state;
        stateLabel.ForeColor = connection.MacConnected && networkReady && worker != null ? Color.FromArgb(30, 137, 75) : enabled ? Color.FromArgb(176, 111, 0) : Color.DimGray;
        networkLabel.Text = "NetBird：" + (networkReady ? "已就绪（100.64.79.70）" : "未就绪");
        listenerLabel.Text = "本机监听：100.64.79.70:55515 · " + (worker != null && connection.Listening ? "正在监听" : "未监听");
        peerLabel.Text = "Windows Peer ID: " + connection.LocalPeerId;
        eventLabel.Text = "最近连接事件：" + connection.LastEvent;
        startButton.Enabled = !enabled || worker == null;
        stopButton.Enabled = enabled || worker != null;
        trayState.Text = "连接状态：" + state;
        trayNetwork.Text = networkLabel.Text;
        trayEvent.Text = eventLabel.Text;
        string tooltip = "P2P Clipboard：" + state;
        tray.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
    }
    private void SaveSnapshot()
    {
        if (quitting) return;
        try
        {
            using (Process self = Process.GetCurrentProcess())
            {
                var status = new {
                    ProcessId = self.Id, StartTicks = self.StartTime.ToUniversalTime().Ticks.ToString(), Desktop = DesktopAccess.CurrentName,
                    UpdatedUtc = DateTime.UtcNow.ToString("o"), Enabled = enabled, NetBirdReady = networkReady,
                    ListenerRunning = worker != null && !worker.HasExited, Listening = connection.Listening,
                    ConnectedToMac = worker != null && networkReady && connection.MacConnected,
                    State = StateText(), PeerId = connection.LocalPeerId, MacPeerId = ConnectionTracker.MacPeerId,
                    LastEvent = connection.LastEvent, CoreProcessId = worker == null ? 0 : worker.Id
                };
                string temp = snapshotPath + ".tmp";
                File.WriteAllText(temp, new JavaScriptSerializer().Serialize(status), new UTF8Encoding(false));
                if (File.Exists(snapshotPath)) File.Replace(temp, snapshotPath, null);
                else File.Move(temp, snapshotPath);
            }
        }
        catch (Exception ex)
        {
            if (!storageWarning) { WriteLine("Cannot save status: " + ex.Message, true); storageWarning = true; }
        }
    }
    private void OpenLogs() { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Path.Combine(root, "logs") + "\"") { UseShellExecute = true }); }
    private void ShowWindow() { Show(); WindowState = FormWindowState.Normal; BringToFront(); Activate(); DesktopAccess.SetForegroundWindow(Handle); }
    private void Quit() { quitting = true; enabled = false; Close(); }
    private void ClosingWindow(object sender, FormClosingEventArgs args)
    {
        if (!quitting && args.CloseReason == CloseReason.UserClosing)
        {
            args.Cancel = true; Hide();
            tray.ShowBalloonTip(2500, "P2P Clipboard", "同步继续在后台运行。双击托盘图标可查看状态。", ToolTipIcon.Info);
            return;
        }
        quitting = true; timer.Stop(); StopWorker();
        tray.Visible = false; tray.Dispose();
        lock (logGate) { output.Dispose(); errors.Dispose(); output = errors = null; }
        if (File.Exists(snapshotPath)) File.Delete(snapshotPath);
    }
}

internal static class Program
{
    private const string MutexName = @"Local\P2PClipboard-Windows-55515";
    private const string ShowName = @"Local\P2PClipboard-Windows-Show";
    private const string StopName = @"Local\P2PClipboard-Windows-Stop";
    private static bool Signal(string name)
    {
        try { using (EventWaitHandle handle = EventWaitHandle.OpenExisting(name)) return handle.Set(); }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }
    private static Dictionary<string, object> ExistingInstance()
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app-status.json");
            var state = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            using (Process process = Process.GetProcessById(Convert.ToInt32(state["ProcessId"])))
            {
                if (!String.Equals(process.MainModule.FileName, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return null;
                if (process.StartTime.ToUniversalTime().Ticks.ToString() != Convert.ToString(state["StartTicks"])) return null;
            }
            return state;
        }
        catch { return null; }
    }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--stop") { Signal(StopName); return; }
        using (Mutex mutex = new Mutex(false, MutexName))
        {
            bool owned;
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                var existing = ExistingInstance();
                bool hiddenToolInstance = existing != null && existing.ContainsKey("Desktop") &&
                    Convert.ToString(existing["Desktop"]).StartsWith("CodexSandboxDesktop-", StringComparison.Ordinal) &&
                    String.Equals(DesktopAccess.CurrentName, "Default", StringComparison.OrdinalIgnoreCase);
                if (hiddenToolInstance)
                {
                    // A user launch replaces an invisible test instance, releasing its core and port.
                    Signal(StopName);
                    try { owned = mutex.WaitOne(10000); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned)
                    {
                        MessageBox.Show("后台测试实例未能退出。请双击 stop.cmd 后重新打开软件。", "P2P Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
                else
                {
                    if (existing != null) DesktopAccess.AllowSetForegroundWindow(Convert.ToInt32(existing["ProcessId"]));
                    if (!Signal(ShowName))
                        MessageBox.Show("另一个启动器正在运行。请先通过 stop.cmd 停止，再打开软件。", "P2P Clipboard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            try
            {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                string data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
                Environment.SetEnvironmentVariable("TEMP", data); Environment.SetEnvironmentVariable("TMP", data);
                Environment.SetEnvironmentVariable("APPDATA", data); Environment.SetEnvironmentVariable("LOCALAPPDATA", data);
                Environment.SetEnvironmentVariable("HOME", data); Environment.SetEnvironmentVariable("RUST_LOG", "info");
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName))
                using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.AutoReset, StopName))
                    Application.Run(new ClipboardWindow(show, stop));
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "P2P Clipboard 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { mutex.ReleaseMutex(); }
        }
    }
}
