using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using NexysView.Core.Bus;
using NexysView.Core.Data;
using NexysView.Core.Mil1553;

namespace NexysBus.App.Services;

/// <summary>Runs the project's frame on a bus device and records every message into a live capture that ANALYZE shows while the bus
/// runs. Stop writes the capture (.mon) into the project's capture folder (default %LOCALAPPDATA%\NexysBus\captures).</summary>
public sealed class SimulationService
{
    readonly BusEngine _e;
    IBusDevice? _dev; Mil1553File? _live; DataFile? _df; DispatcherTimer? _timer; Stopwatch? _sw; string _path = "";
    readonly List<BusRecord> _buf = new(); int _seq;
    public SimulationService(BusEngine e) { _e = e; }
    public bool Running => _dev?.IsRunning == true;
    public string LastCapture { get; private set; } = "";
    public double ElapsedS => _sw?.Elapsed.TotalSeconds ?? 0;
    public int Runs { get; private set; }
    public string LastError { get; private set; } = "";

    public string CaptureDir => _e.Project.CaptureDir.Length > 0 ? _e.Project.CaptureDir : Path.Combine(AppPaths.LocalDir, "captures");

    public void Start(bool manualClock = false)
    {
        if (Running) return;
        LastError = "";
        try
        {
            var cfg = _e.Project.ToConfig();
            if (cfg.Messages.Count(m => m.Enabled) == 0) { _e.SetStatus("Nothing to run: load an ICD (ICD rail) or import a simulator project first"); return; }
            _dev?.Dispose();
            _dev = _e.Project.Device == "virtual" ? new VirtualBusDevice { ManualClock = manualClock } : new HardwareBusDevice(_e.Project.Device);
            _dev.Open(); _dev.Configure(cfg);
            Directory.CreateDirectory(CaptureDir);
            _path = Path.Combine(CaptureDir, $"{San(_e.Project.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{++_seq:00}.mon");   // unique per run (two runs inside one second must not share a file / DataFile name)
            _live = Mil1553File.Live(_path);
            _df = Mil1553DataSource.Open(_live, _path);
            if (_e.Mil1553.Hub != null) Mil1553DataSource.AttachIcd(_df, _e.Mil1553.Hub);
            _e.State.Files.Add(_df); _e.FilesChangedExternally(); _e.Mil1553.SelectFile(_df.Name);
            _dev.Start(); _sw = Stopwatch.StartNew(); Runs++;
            if (!manualClock) { _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) }; _timer.Tick += (_, _) => Pump(); _timer.Start(); }
            _e.SetLed("busy"); _e.SetStatus($"Bus running on {_dev.Info.Name}: {cfg.Messages.Count(m => m.Enabled)} messages, minor frame {(_dev as VirtualBusDevice)?.MinorFrameMs ?? 0:F1} ms → {Path.GetFileName(_path)}");
            _e.Touch();
        }
        catch (Exception ex) { LastError = ex.Message; Diag.Trace("sim start failed " + ex); _e.SetStatus("Start failed: " + ex.Message); _e.SetLed("error"); _dev?.Dispose(); _dev = null; }
    }

    void Pump()
    {
        if (_dev == null || _live == null) return;
        _buf.Clear(); _dev.Poll(_buf);
        foreach (var r in _buf) _live.Append(r.TimeUs, r.Activity, r.Error, r.Cwd1, r.Cwd2, r.Swd1, r.Swd2, r.Data, r.RespTime1, r.RespTime2);
        if (_buf.Count > 0) _e.Mil1553.Refresh();
    }

    public void Stop()
    {
        if (_dev == null) return;
        _timer?.Stop(); _timer = null;
        try { _dev.Stop(); Pump(); _dev.Close(); } catch (Exception ex) { Diag.Trace("sim stop " + ex); }
        _sw?.Stop();
        try { if (_live != null && _live.Count > 0) { _live.SaveAs(_path); LastCapture = _path; } } catch (Exception ex) { LastError = ex.Message; Diag.Trace("capture save failed " + ex); }
        var c = _dev.Counters;
        _e.SetLed("ready"); _e.SetStatus($"Bus stopped: {c.Messages:N0} messages, {c.Errors:N0} errors, load {c.LoadPct:F1} % → {Path.GetFileName(_path)}");
        _e.Touch();
    }

    /// <summary>Deterministic run of the frame for a duration (virtual clock, no waiting): used by the self-test and batch captures.</summary>
    public object RunFor(double seconds)
    {
        Stop(); Start(manualClock: true);
        if (_dev is VirtualBusDevice v) { long expected = v.ExpectedMessages(seconds); v.RunFor(seconds); Pump(); Stop(); return new { expected, messages = v.Counters.Messages, errors = v.Counters.Errors, loadPct = v.Counters.LoadPct, capture = _path, minorMs = v.MinorFrameMs, majorMs = v.MajorFrameMs }; }
        Stop(); return new { expected = 0, messages = 0 };
    }

    public object Dto()
    {
        var d = _dev; var c = d?.Counters; var v = d as VirtualBusDevice;
        return new
        {
            running = Running, device = d?.Info, elapsedS = ElapsedS, runs = Runs, lastError = LastError,
            messages = c?.Messages ?? 0, errors = c?.Errors ?? 0, busA = c?.BusA ?? 0, busB = c?.BusB ?? 0, loadPct = c?.LoadPct ?? 0,
            noResponse = c?.NoResponse ?? 0, parity = c?.Parity ?? 0, wordCount = c?.WordCount ?? 0,
            perRt = c == null ? new List<object>() : Enumerable.Range(0, 32).Where(i => c.PerRt[i] > 0).Select(i => (object)new { rt = i, count = c.PerRt[i] }).ToList(),
            minorMs = v?.MinorFrameMs ?? 0, majorMs = v?.MajorFrameMs ?? 0,
            slots = v?.Slots.Select(s => new { message = s.Message.Name, every = s.Every, durationUs = s.DurationUs, rt = s.Message.Rt, sa = s.Message.Sa, tr = s.Message.Tr, wc = s.Message.DataWords, bus = s.Message.Bus }).ToList(),
            expected10s = v?.ExpectedMessages(10) ?? 0, capture = _df?.Name, capturePath = _path, lastCapture = LastCapture, captureDir = CaptureDir,
        };
    }
    static string San(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
