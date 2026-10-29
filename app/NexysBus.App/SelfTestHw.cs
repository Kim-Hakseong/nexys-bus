using System.Diagnostics;
using System.IO;
using NexysView.Core.Bus;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>
/// --hw --out &lt;dir&gt; [--card 0] [--channel 0] [--seconds 10] [--project &lt;file.nxb&gt;] [--binding &lt;driver.json&gt;] [--keep-open]
/// Hardware-day measurement of <see cref="HardwareBusDevice"/> against a real card (docs/day3-fixlist.md §G): the driver binding,
/// G1 card enumeration, G2 open (and a wrong channel refused), G3 frame configuration, G4 a timed run (message count vs the frame
/// formula, poll latency), G5 time tags (Δt median vs period), G6 an unanswered RT (no-response errors), G7 the capture file
/// (record count re-read). Without a binding every gate after the first is skipped — the virtual bus is not measured here.
/// Captures: &lt;out&gt;\hw_run.mon, hw_err.mon; driver calls: calllog.txt.
/// </summary>
public static partial class SelfTest
{
    static BusConfig DefaultHwFrame()
    {
        // ten messages, RT 10 / 11, periods 10..100 ms → minor 10 ms, major 100 ms (the same shape as the dummy ICD frame)
        var cfg = new BusConfig { MinorFrameMs = 10 };
        int order = 0;
        foreach (var (rt, sa, tr, wc, per) in new[] { (10, 1, 0, 4, 10.0), (10, 2, 1, 2, 10.0), (10, 3, 0, 8, 20.0), (10, 4, 1, 8, 20.0), (10, 5, 0, 16, 50.0), (11, 1, 0, 4, 10.0), (11, 2, 1, 4, 20.0), (11, 3, 0, 32, 50.0), (11, 4, 1, 32, 100.0), (11, 5, 0, 1, 100.0) })
            cfg.Messages.Add(new BusMessage { Name = $"RT{rt}_SA{sa}_{(tr == 1 ? "TX" : "RX")}", Rt = rt, Sa = sa, Tr = tr, Wc = wc, PeriodMs = per, Order = order++, Words = { new BusWord { Name = "W1", Index = 1, BitStart = 1, BitLen = 16, Scale = 0.1 } }, Source = new BusDataSource { Kind = "sine", Value = 100, Amplitude = 50, PeriodS = 2 } });
        return cfg;
    }
    static long ExpectedMessages(BusConfig cfg, double seconds, double minorMs)
    {
        long total = 0;
        foreach (var m in cfg.Messages.Where(m => m.Enabled)) { int every = Math.Max(1, (int)Math.Round(m.PeriodMs / minorMs)); total += (long)Math.Ceiling(seconds * 1000 / (every * minorMs) - 1e-9); }
        return total;
    }

    static async Task Hw(MainWindow w)
    {
        var e = w.Engine; var sw = Stopwatch.StartNew();
        int card = int.TryParse(Arg("--card", "0"), out int c0) ? c0 : 0, channel = int.TryParse(Arg("--channel", "0"), out int ch0) ? ch0 : 0;
        double seconds = double.TryParse(Arg("--seconds", "10"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s0) ? s0 : 10;
        string bindingArg = Arg("--binding");
        var binding = DriverBinding.Load(bindingArg.Length > 0 ? new[] { bindingArg } : DriverBinding.DefaultCandidates("NexysBus"));
        GB("hw_binding", binding != null, binding != null ? $"{binding.Path}: {Path.GetFileName(binding.CardLibrary)} + {Path.GetFileName(binding.BusLibrary)} prefixes {binding.CardPrefix}/{binding.BusPrefix}" : $"no driver.json ({string.Join(" | ", bindingArg.Length > 0 ? new[] { bindingArg } : DriverBinding.DefaultCandidates("NexysBus"))}) — G1..G7 skipped");
        if (binding == null) return;
        // ---- G1 enumerate
        var t1 = Stopwatch.StartNew(); var cards = HardwareBusDevice.Enumerate(binding, maxCards: Math.Clamp(binding.MaxCards, 1, 16));
        G("hw_g1_enumerate_ms", t1.Elapsed.TotalMilliseconds, "ms");
        GB("hw_g1_enumerate", cards.Count >= 1, cards.Count == 0 ? (HardwareBusDevice.LastEnumerateError.Length > 0 ? HardwareBusDevice.LastEnumerateError : "the driver reports no card") : string.Join(" | ", cards.Select(k => $"{k.Name}: {k.Detail}")));
        if (cards.Count == 0) return;
        // ---- G2 open, wrong channel refused
        var dev = new HardwareBusDevice(card, binding, channel: channel);
        try { dev.Open(); } catch (Exception ex) { L("open failed: " + ex.Message); }
        GB("hw_g2_open", dev.IsOpen, dev.IsOpen ? $"{dev.TypeName} / {dev.Product} / S/N {dev.Serial} core {dev.CoreNumber} channel {dev.Channel}" : dev.LastError);
        if (!dev.IsOpen) { dev.Dispose(); return; }
        string wrong = "";
        try { using var d2 = new HardwareBusDevice(card, binding, channel: 15); d2.Open(); wrong = d2.IsOpen ? "channel 15 opened (card has ≥ 16 channels?)" : d2.LastError; } catch (Exception ex) { wrong = ex.Message; }
        GB("hw_g2_wrong_channel_refused", wrong.Contains("not a 1553 channel") || wrong.Contains("failed"), $"channel 15 → '{wrong}'");
        // ---- G3 configure
        BusConfig cfg; string prj = Arg("--project");
        if (prj.Length > 0 && File.Exists(prj)) { var p = BusProject.Load(prj); cfg = p.ToConfig(); L($"frame from {prj}: {cfg.Messages.Count} messages"); } else cfg = DefaultHwFrame();
        string cfgErr = "";
        try { dev.Configure(cfg); } catch (Exception ex) { cfgErr = ex.Message; }
        GB("hw_g3_configure", cfgErr.Length == 0, cfgErr.Length == 0 ? $"{cfg.Messages.Count(m => m.Enabled)} messages, minor {dev.MinorFrameMs} ms, major {dev.MajorFrameMs} ms, {dev.CallLog.Count} driver calls" : cfgErr);
        if (cfgErr.Length > 0) { File.WriteAllLines(Path.Combine(OutDir, "calllog.txt"), dev.CallLog); dev.Dispose(); return; }
        // ---- G4 timed run: poll every 100 ms into a capture file
        string runPath = Path.Combine(OutDir, "hw_run.mon"); var live = Mil1553File.Live(runPath); var buf = new List<BusRecord>(); var all = new List<BusRecord>();
        double pollMax = 0; int polls = 0, firstDataPoll = -1; string runErr = "";
        try
        {
            dev.Start(); var run = Stopwatch.StartNew();
            while (run.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(100);
                var tp = Stopwatch.StartNew(); buf.Clear(); int n = dev.Poll(buf); double ms = tp.Elapsed.TotalMilliseconds; polls++; if (ms > pollMax) pollMax = ms;
                if (n > 0 && firstDataPoll < 0) firstDataPoll = polls;
                foreach (var r in buf) { live.Append(r.TimeUs, r.Activity, r.Error, r.Cwd1, r.Cwd2, r.Swd1, r.Swd2, r.Data, r.RespTime1, r.RespTime2); all.Add(r); }
            }
            dev.Stop(); buf.Clear(); dev.Poll(buf); foreach (var r in buf) { live.Append(r.TimeUs, r.Activity, r.Error, r.Cwd1, r.Cwd2, r.Swd1, r.Swd2, r.Data, r.RespTime1, r.RespTime2); all.Add(r); }
        }
        catch (Exception ex) { runErr = ex.Message; }
        long expected = ExpectedMessages(cfg, seconds, dev.MinorFrameMs); var cnt = dev.Counters;
        G("hw_g4_messages", cnt.Messages, "", $"={expected} ±1%"); G("hw_g4_expected", expected, ""); G("hw_g4_errors", cnt.Errors, ""); G("hw_g4_load_pct", cnt.LoadPct, "%"); G("hw_g4_poll_max_ms", pollMax, "ms", "<=200"); G("hw_g4_polls", polls, ""); G("hw_g4_first_data_poll", firstDataPoll, "");
        GB("hw_g4_run", runErr.Length == 0 && expected > 0 && Math.Abs(cnt.Messages - expected) <= Math.Max(1, expected / 100) && pollMax <= 200, runErr.Length > 0 ? runErr : $"{cnt.Messages} messages in {seconds} s (expected {expected}), poll max {pollMax:F1} ms, errors {cnt.Errors}, load {cnt.LoadPct:F2} %, per RT: " + string.Join(" ", Enumerable.Range(0, 32).Where(i => cnt.PerRt[i] > 0).Select(i => $"{i}:{cnt.PerRt[i]}")));
        // ---- G5 time tags: Δt of the first scheduled message vs its period; high word present
        var first = cfg.Messages.Where(m => m.Enabled).OrderBy(m => m.Order).First(); ushort cw = first.Cwd1;
        var times = all.Where(r => r.Cwd1 == cw).Select(r => (double)r.TimeUs).ToList(); var dts = times.Zip(times.Skip(1), (a, b) => b - a).OrderBy(x => x).ToList();
        double median = dts.Count > 0 ? dts[dts.Count / 2] : 0; double per = Math.Max(1, (int)Math.Round(first.PeriodMs / dev.MinorFrameMs)) * dev.MinorFrameMs * 1000; double errPct = per > 0 ? Math.Abs(median - per) / per * 100 : 100;
        G("hw_g5_dt_median_us", median, "us", $"={per} ±1%"); G("hw_g5_dt_error_pct", errPct, "%", "<=1");
        GB("hw_g5_timetags", dts.Count >= 5 && errPct <= 1 && all.Count > 0 && all.Zip(all.Skip(1)).All(p => p.Second.TimeUs >= p.First.TimeUs), $"{first.Name}: {dts.Count} intervals, median {median:F0} us vs period {per:F0} us ({errPct:F2} %), high word {(all.Count > 0 ? (all[0].TimeUs >> 32) : 0)}, monotonic {all.Zip(all.Skip(1)).All(p => p.Second.TimeUs >= p.First.TimeUs)}");
        try { live.SaveAs(runPath); } catch (Exception ex) { L("save failed " + ex.Message); }
        // ---- G6 unanswered RT: one message to RT 20 nobody simulates, 2 s → every one a NO RESPONSE error
        string errPath = Path.Combine(OutDir, "hw_err.mon"); var liveErr = Mil1553File.Live(errPath); string g6Err = ""; long g6Exp = 0; var cntErr = new BusCounters(); var errRecs = new List<BusRecord>();
        try
        {
            var cfgErrRun = new BusConfig { MinorFrameMs = 10 };
            cfgErrRun.Messages.Add(new BusMessage { Name = "RT10_SA1_RX", Rt = 10, Sa = 1, Tr = 0, Wc = 2, PeriodMs = 10, Order = 0 });
            cfgErrRun.Messages.Add(new BusMessage { Name = "RT20_SA1_RX_ABSENT", Rt = 20, Sa = 1, Tr = 0, Wc = 2, PeriodMs = 20, Order = 1, SimulateRt = false });
            dev.Configure(cfgErrRun); dev.Start(); var run = Stopwatch.StartNew();
            while (run.Elapsed.TotalSeconds < 2) { await Task.Delay(100); buf.Clear(); dev.Poll(buf); foreach (var r in buf) { liveErr.Append(r.TimeUs, r.Activity, r.Error, r.Cwd1, r.Cwd2, r.Swd1, r.Swd2, r.Data, r.RespTime1, r.RespTime2); errRecs.Add(r); } }
            dev.Stop(); buf.Clear(); dev.Poll(buf); foreach (var r in buf) { liveErr.Append(r.TimeUs, r.Activity, r.Error, r.Cwd1, r.Cwd2, r.Swd1, r.Swd2, r.Data, r.RespTime1, r.RespTime2); errRecs.Add(r); }
            g6Exp = ExpectedMessages(cfgErrRun, 2, dev.MinorFrameMs) - (long)Math.Ceiling(2 * 1000 / 10.0 - 1e-9); cntErr = dev.Counters; liveErr.SaveAs(errPath);
        }
        catch (Exception ex) { g6Err = ex.Message; }
        long noResp = errRecs.Count(r => (r.Error & 0x8000) != 0), rt20 = errRecs.Count(r => r.Rt == 20);
        G("hw_g6_noresponse", noResp, "", $"={g6Exp} ±2"); G("hw_g6_rt20_messages", rt20, "");
        GB("hw_g6_errors", g6Err.Length == 0 && rt20 > 0 && noResp == rt20 && Math.Abs(noResp - g6Exp) <= 2 && cntErr.Errors == noResp, g6Err.Length > 0 ? g6Err : $"RT 20 messages {rt20}, NO RESPONSE {noResp} (expected {g6Exp}), counters errors {cntErr.Errors} noResponse {cntErr.NoResponse}; error words seen: {string.Join(" ", errRecs.Where(r => r.HasError).Select(r => r.Error).Distinct().Select(x => $"0x{x:X4}"))}");
        // ---- G7 capture file re-read
        long reread = -1; string g7 = "";
        try { var df = e.OpenFile(runPath); if (df != null) { reread = Mil1553DataSource.FileOf(df)?.Count ?? -1; e.CloseFile(df.Name); } } catch (Exception ex) { g7 = ex.Message; }
        GB("hw_g7_capture_file", reread == all.Count && all.Count > 0, g7.Length > 0 ? g7 : $"{runPath}: {new FileInfo(runPath).Length} bytes, {reread} records re-read = {all.Count} polled (NexysBus) — the simulator Monitor import is the manual half of G7");
        File.WriteAllLines(Path.Combine(OutDir, "calllog.txt"), dev.CallLog);
        G("hw_total_s", sw.Elapsed.TotalSeconds, "s");
        if (Arg("--keep-open") != "1") dev.Dispose();
    }
}
