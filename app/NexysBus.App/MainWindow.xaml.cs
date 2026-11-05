using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using NexysBus.App.Services;

namespace NexysBus.App;

/// <summary>One WPF window hosting the WebView2 page (bus/web → wwwroot). Dialogs and the window stay native; the page talks to
/// <see cref="BusEngine"/> through the shared <see cref="Bridge"/>; bus-only commands are dispatched by the engine.</summary>
public partial class MainWindow : Window
{
    public BusEngine Engine { get; } = new();
    public Bridge? Bridge { get; private set; }
    readonly Branding _brand = Branding.Load();
    readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WhenReady => _ready.Task;
    public string RuntimeVersion { get; private set; } = "";
    public string DisplayName => _brand.displayName;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{_brand.displayName} {AppInfo.VersionText}";
        try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/brand/AppIcon.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad); } catch { }
        SourceInitialized += (_, _) => AppId.ApplyWindow(new System.Windows.Interop.WindowInteropHelper(this).Handle, _brand.displayName);
        Loaded += async (_, _) => await InitAsync();
        Closing += (_, _) => { Engine.StopAll(); BusStore.Save(this); };
        Engine.Changed += () => { if (Bridge?.PageReady == true) Dispatcher.BeginInvoke(() => BusStore.Save(this), System.Windows.Threading.DispatcherPriority.ApplicationIdle); };
    }

    async Task InitAsync()
    {
        try
        {
            string? rt = WebShell.ProbeRuntime(); string? fixedDir = null;
            if (rt == null)
            {
                string d = Path.Combine(AppContext.BaseDirectory, "WebView2Runtime");
                if (Directory.Exists(d)) fixedDir = d; else throw new InvalidOperationException("Microsoft Edge WebView2 Runtime is not installed");
            }
            var env = await WebShell.CreateEnvironmentAsync(fixedDir, WebShell.UserDataFolder(SelfTest.Enabled ? "WebView2-auto" : Program.Fresh ? "WebView2-fresh" : "WebView2"));
            RuntimeVersion = env.BrowserVersionString;
            Web.DefaultBackgroundColor = System.Drawing.ColorTranslator.FromHtml("#ECEAE5");
            await Web.EnsureCoreWebView2Async(env);
            var core = Web.CoreWebView2;
            if (!Directory.Exists(WebShell.WwwRoot)) { Boot.Text = "wwwroot missing — run `npm run build` in bus/web and rebuild"; Diag.Trace("wwwroot missing: " + WebShell.WwwRoot); }
            WebShell.ConfigureCore(core);
            Bridge = new Bridge(core, env, Engine, Dispatcher, HostCommand, Engine.Dispatch);
            Bridge.Ready += () => { Diag.Trace("page ready"); Dispatcher.BeginInvoke(() => { BusStore.Load(this); Bridge.PushSnapshot(); _ready.TrySetResult(); }); };
            core.Navigate(WebShell.Origin + "index.html");
        }
        catch (Exception ex)
        {
            Diag.Trace("webview init failed " + ex);
            Boot.Text = "WebView2 runtime not available: " + ex.Message + "\nInstall the Evergreen WebView2 Runtime or place a Fixed Version runtime in the WebView2Runtime folder next to NexysBus.exe, then start again.";
            _ready.TrySetException(ex);
        }
    }

    /// <summary>Host-only commands (WPF dialogs, files); the shared ones live in <see cref="HostCommands"/>.</summary>
    public async Task<object?> HostCommand(string cmd, JsonElement args)
    {
        switch (cmd)
        {
            case "openDialog":
                {
                    var dlg = new OpenFileDialog { Filter = "Bus files|*.nxb;*.mon;*.pcapng;*.pcap;*.CPJ;*.CDV;*.csv;*.xlsx;*.json;*.xml|NexysBus project|*.nxb|1553 capture|*.mon|ARINC 664 capture|*.pcapng;*.pcap|Simulator project|*.CPJ;*.CDV|ICD|*.csv;*.xlsx;*.json;*.xml|All|*.*", Multiselect = true };
                    if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FileNames) Engine.OpenAny(f);
                    return null;
                }
            case "project.open":
                {
                    var dlg = new OpenFileDialog { Title = "Open project", Filter = "NexysBus project|*.nxb|All|*.*" };
                    if (dlg.ShowDialog(this) != true) return null; Engine.OpenProject(dlg.FileName); return dlg.FileName;
                }
            case "project.save":
                {
                    if (Engine.Project.Path.Length > 0 && !Bridge.B(args, "as")) return Engine.SaveProject(null);
                    var dlg = new SaveFileDialog { Title = "Save project", Filter = "NexysBus project|*.nxb", FileName = Engine.Project.Name + ".nxb" };
                    if (dlg.ShowDialog(this) != true) return null; return Engine.SaveProject(dlg.FileName);
                }
            case "project.importSim":
                {
                    var dlg = new OpenFileDialog { Title = "Simulator project (pick the .CPJ or the 1553 device .CDV)", Filter = "Simulator project|*.CPJ;*.CDV|All|*.*" };
                    if (dlg.ShowDialog(this) != true) return null; return Engine.ImportSimulator(dlg.FileName);
                }
            case "project.exportSim":
                {
                    var dlg = new SaveFileDialog { Title = "Export folder (choose the folder name)", Filter = "Export folder|*.folder", FileName = Engine.Project.Name.Replace(' ', '_') + ".folder" };
                    if (dlg.ShowDialog(this) != true) return null;
                    string dir = Path.Combine(Path.GetDirectoryName(dlg.FileName)!, Path.GetFileNameWithoutExtension(dlg.FileName));
                    return Engine.ExportSimulator(dir, Bridge.SL(args, "formats") is { Count: > 0 } fm ? fm.ToArray() : null);
                }
            case "project.pickCaptureDir":
                {
                    var dlg = new SaveFileDialog { Title = "Capture folder (choose the folder)", Filter = "Folder|*.folder", FileName = "captures.folder" };
                    if (dlg.ShowDialog(this) != true) return null; Engine.Project.CaptureDir = Path.GetDirectoryName(dlg.FileName) ?? ""; Engine.Dirty = true; Engine.Touch(); return Engine.Project.CaptureDir;
                }
            case "trend.sendToView":   // the trend channel as CSV (time, value) → Nexys VIEW when installed, else the file stays for a manual open
                {
                    string id = Bridge.S(args, "channel"); var ch = Engine.Find(id); if (ch == null) { Engine.SetStatus("No trend channel"); return null; }
                    string dir = Path.Combine(AppPaths.LocalDir, "trend"); Directory.CreateDirectory(dir);
                    string safe = new string(ch.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()); string path = Path.Combine(dir, $"{safe}_{DateTime.Now:HHmmss}.csv");
                    using (var w = new StreamWriter(path, false, new System.Text.UTF8Encoding(true)))
                    {
                        w.WriteLine($"time_s,{safe}" + (ch.Unit.Length > 0 ? $" [{ch.Unit}]" : "")); var buf = new double[4096];
                        for (long i = 0; i < ch.Length; i += buf.Length) { int got = ch.Source.Read(i, buf.AsSpan(0, (int)Math.Min(buf.Length, ch.Length - i))); for (int k = 0; k < got; k++) w.WriteLine($"{ch.XAt(i + k).ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{buf[k].ToString("R", System.Globalization.CultureInfo.InvariantCulture)}"); }
                    }
                    var candidates = new[] { Path.Combine(AppContext.BaseDirectory, "NexysView.exe"), Path.Combine(AppContext.BaseDirectory, "..", "Nexys VIEW", "NexysView.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Nexys VIEW", "NexysView.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexys VIEW", "NexysView.exe") };
                    var exe = candidates.FirstOrDefault(File.Exists);
                    if (exe != null && !SelfTest.Enabled) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"\"{path}\"") { UseShellExecute = true }); Engine.SetStatus($"Sent {ch.Name} to Nexys VIEW ({ch.Length:N0} samples)"); }
                    else Engine.SetStatus((exe == null ? "Nexys VIEW is not installed — " : "") + $"trend written to {path}");
                    return path;
                }
            case "afdx.open":
                {
                    var dlg = new OpenFileDialog { Title = "Open ARINC 664 capture", Filter = "Packet capture|*.pcapng;*.pcap|All|*.*" };
                    if (dlg.ShowDialog(this) != true) return null; Engine.OpenAny(dlg.FileName); return dlg.FileName;
                }
            case "afdx.interop.export":
                {
                    var dlg = new SaveFileDialog { Title = "Interoperability report", Filter = "Markdown|*.md", FileName = "interop-report.md" };
                    if (dlg.ShowDialog(this) != true) return null; return Engine.Afdx.ExportReport(dlg.FileName);
                }
            case "afdx.icd.exportCsv":
                {
                    var dlg = new SaveFileDialog { Title = "ARINC 664 ICD (CSV)", Filter = "CSV|*.csv", FileName = Engine.Project.Name.Replace(' ', '_') + "_afdx_icd.csv" };
                    if (dlg.ShowDialog(this) != true) return null; File.WriteAllText(dlg.FileName, NexysView.Core.Arinc664.AfdxIcd.ToCsv(Engine.Project.Icd)); Engine.SetStatus("ICD → " + dlg.FileName); return dlg.FileName;
                }
            case "afdx.icd.import":
                {
                    var dlg = new OpenFileDialog { Title = "ARINC 664 ICD (CSV / XLSX / hub JSON)", Filter = "ICD|*.csv;*.xlsx;*.json|All|*.*" };
                    if (dlg.ShowDialog(this) != true) return null; Engine.Mil1553.LoadIcd(dlg.FileName); return dlg.FileName;
                }
            case "capture": { string path = args.GetProperty("path").GetString()!; await CaptureAsync(path); return path; }
            case "quit": Close(); return null;
        }
        var (handled, result) = await HostCommands.TryAsync(this, Engine, cmd, args);
        if (handled) return result;
        throw new NotSupportedException("unknown command " + cmd);
    }

    /// <summary>Screenshot of the page (what the user sees) as PNG.</summary>
    public async Task CaptureAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        await Web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
    }
}
