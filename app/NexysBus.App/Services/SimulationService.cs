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
    // a hardware card is polled from its own thread (5 ms pacing): a single-function BC keeps only the last transaction per message, so the
    // 100 ms dispatcher timer would lose four of five 20 ms transactions; the thread fills _pending, Pump drains it into the capture
    Thread? _poller; volatile bool _pollStop; readonly object _lock = new(); readonly List<BusRecord> _pending = new(); string _pollError = "";
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
            if (_e.Project.Device == "virtual") _dev = new VirtualBusDevice { ManualClock = manualClock };
            else { var hw = new HardwareBusDevice(BusEngine.CardIndex(_e.Project.Device), _e.Driver, channel: _e.Project.Bus.Channel); if (_e.Project.Bus.MonitorChannel >= 0 && _e.Project.Bus.MonitorChannel != _e.Project.Bus.Channel) hw.MonitorChannels.Add(_e.Project.Bus.MonitorChannel); _dev = hw; }
            _dev.Open(); _dev.Configure(cfg);
            if (_dev is HardwareBusDevice h0) foreach (var wn in h0.Warnings) Diag.Trace("hardware configure: " + wn);
            Directory.CreateDirectory(CaptureDir);
            _path = Path.Combine(CaptureDir, $"{San(_e.Project.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{++_seq:00}.mon");   // unique per run (two runs inside one second must not share a file / DataFile name)
            _live = Mil1553File.Live(_path);
            _df = Mil1553DataSource.Open(_live, _path);
            if (_e.Mil1553.Hub != null) Mil1553DataSource.AttachIcd(_df, _e.Mil1553.Hub);
            _e.State.Files.Add(_df); _e.FilesChangedExternally(); _e.Mil1553.SelectFile(_df.Name);
            _dev.Start(); _sw = Stopwatch.StartNew(); Runs++;
            if (_dev is HardwareBusDevice hwDev && !manualClock) StartPoller(hwDev);
            if (!manualClock) { _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) }; _timer.Tick += (_, _) => Pump(); _timer.Start(); }
            double minor = _dev is VirtualBusDevice vb ? vb.MinorFrameMs : _dev is HardwareBusDevice hb ? hb.MinorFrameMs : 0;
            _e.SetLed("busy"); _e.SetStatus($"Bus running on {_dev.Info.Name}: {cfg.Messages.Count(m => m.Enabled)} messages, minor frame {minor:F1} ms{(_dev is HardwareBusDevice hm && hm.BcTransactionRecords ? " (BC message blocks)" : "")} → {Path.GetFileName(_path)}");
            _e.Touch();
        }
        catch (Exception ex) { LastError = ex.Message; Diag.Trace("sim start failed " + ex); _e.SetStatus("Start failed: " + ex.Message); _e.SetLed("error"); _dev?.Dispose(); _dev = null; }
    }

    void StartPoller(HardwareBusDevice hw)
    {
        _pollStop = false; _pollError = "";
        _poller = new Thread(() =>
        {
            var tmp = new List<BusRecord>(); var sw = Stopwatch.StartNew(); double next = 0;
            while (!_pollStop)
            {
                while (sw.Elapsed.TotalMilliseconds < next && !_pollStop) Thread.Sleep(1);
                next = sw.Elapsed.TotalMilliseconds + 5; tmp.Clear();
                try { hw.Poll(tmp); } catch (Exception ex) { _pollError = ex.Message; Diag.Trace("hardware poll " + ex); break; }
                if (tmp.Count > 0) lock (_lock) _pending.AddRange(tmp);
            }
        }) { IsBackground = true, Name = "bus-poll" };
        _poller.Start();
    }
    void StopPoller() { _pollStop = true; try { _poller?.Join(2000); } catch { } _poller = null; }

    void Pump()
    {
        if (_dev == null || _live == null) return;
        _buf.Clear();
        if (_poller != null) { lock (_lock) { _buf.AddRange(_pending); _pending.Clear(); } if (_pollError.Length > 0) { LastError = _pollError; _e.SetStatus("Hardware poll failed: " + _pollError); _e.SetLed("error"); } }
        else _dev.Poll(_buf);
        foreach (var r in _buf) _live.Append(r.TimeUs, r.Activity, r.Error, r.Cwd1, r.Cwd2, r.Swd1, r.Swd2, r.Data, r.RespTime1, r.RespTime2);
        if (_buf.Count > 0) { ReattachIcd(false); _e.Mil1553.Refresh(); }
    }

    // The ICD engineering channels of a capture are built from the records present when they are attached (one group per message
    // that has records). A run starts with an empty live capture, so they are rebuilt as the capture grows (at most once a second)
    // and once more when the run stops — otherwise ANALYZE › TREND finds no channel for a live or BATCH capture and stays empty.
    long _attachedAt = -1; readonly Stopwatch _attachClock = Stopwatch.StartNew();
    void ReattachIcd(bool force)
    {
        if (_df == null || _live == null || _e.Mil1553.Hub == null || !_e.Mil1553.IcdApplied) return;
        if (!force && (_live.Count == _attachedAt || _attachClock.ElapsedMilliseconds < 1000)) return;
        Mil1553DataSource.AttachIcd(_df, _e.Mil1553.Hub); _attachedAt = _live.Count; _attachClock.Restart();
        RebindCurves();
    }

    // AttachIcd replaces the capture's ICD channels; curves already showing one of them (ANALYZE › TREND, or any chart) still hold the
    // old channel object with the old record set — swap each such curve for the new channel of the same id so a trend opened during
    // the run keeps growing without a second click.
    void RebindCurves()
    {
        if (_df == null) return; string prefix = _df.Name + "|ICD:"; var touched = new HashSet<int>();
        foreach (var chart in _e.Charts.ToList())
        {
            var curves = chart.Model.Curves;
            for (int i = curves.Count - 1; i >= 0; i--)
            {
                var old = curves[i].Channel; if (!old.Id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var fresh = _e.Find(old.Id); if (fresh == null || ReferenceEquals(fresh, old)) continue;
                _e.RemoveCurve(chart.Id, i, record: false); _e.AddCurve(chart.Id, old.Id, "plot", record: false); touched.Add(chart.Id);
            }
        }
        foreach (int id in touched) _e.TouchChart(id);
    }

    public void Stop()
    {
        if (_dev == null) return;
        _timer?.Stop(); _timer = null;
        try { StopPoller(); _dev.Stop(); Pump(); _dev.Close(); } catch (Exception ex) { Diag.Trace("sim stop " + ex); }
        _sw?.Stop();
        ReattachIcd(true);
        try { if (_live != null && _live.Count > 0) { _live.SaveAs(_path); LastCapture = _path; } } catch (Exception ex) { LastError = ex.Message; Diag.Trace("capture save failed " + ex); }
        var c = _dev.Counters; string missed = _dev is HardwareBusDevice hs && hs.BcTransactionRecords ? $", {hs.MissedTransactions:N0} not read (BC message blocks)" : "";
        _e.SetLed("ready"); _e.SetStatus($"Bus stopped: {c.Messages:N0} messages, {c.Errors:N0} errors, load {c.LoadPct:F1} %{missed} → {Path.GetFileName(_path)}");
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
        var d = _dev; var c = d?.Counters; var v = d as VirtualBusDevice; var hw = d as HardwareBusDevice;
        return new
        {
            running = Running, device = d?.Info, elapsedS = ElapsedS, runs = Runs, lastError = LastError,
            messages = c?.Messages ?? 0, errors = c?.Errors ?? 0, busA = c?.BusA ?? 0, busB = c?.BusB ?? 0, loadPct = c?.LoadPct ?? 0,
            noResponse = c?.NoResponse ?? 0, parity = c?.Parity ?? 0, wordCount = c?.WordCount ?? 0,
            perRt = c == null ? new List<object>() : Enumerable.Range(0, 32).Where(i => c.PerRt[i] > 0).Select(i => (object)new { rt = i, count = c.PerRt[i] }).ToList(),
            minorMs = v?.MinorFrameMs ?? hw?.MinorFrameMs ?? 0, majorMs = v?.MajorFrameMs ?? hw?.MajorFrameMs ?? 0,
            hardware = hw == null ? null : new { hw.TypeName, hw.Product, hw.Serial, hw.Channel, monitorChannels = hw.MonitorChannels, hw.MonitorOnBcChannel, hw.BcTransactionRecords, hw.MissedTransactions, warnings = hw.Warnings, driverVersion = hw.DriverVersion },
            slots = v?.Slots.Select(s => new { message = s.Message.Name, every = s.Every, durationUs = s.DurationUs, rt = s.Message.Rt, sa = s.Message.Sa, tr = s.Message.Tr, wc = s.Message.DataWords, bus = s.Message.Bus }).ToList(),
            expected10s = v?.ExpectedMessages(10) ?? 0, capture = _df?.Name, capturePath = _path, lastCapture = LastCapture, captureDir = CaptureDir,
        };
    }
    static string San(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
}
