using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NexysView.Core.Bus;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>
/// --site --out &lt;dir&gt; [--card 0] [--channel 0] [--mon 1 | -1] [--rt 4] [--wc 32] [--period 20] [--seconds 30] [--bseconds 30] [--minutes 10]
/// [--poll 10] [--tagunit 1] [--binding driver.json] [--expect "sa3:0=8000,1=0081,8=93C7,10=EB0D;sa4:8=FFFE,9=FFFE,10=FFFE"] [--counter sa4:1]
/// Site measurement against a real remote terminal on the bus (run-site.md H0..H3): the card is the bus controller on --channel, an
/// optional second channel of the same core is a bus monitor (--mon), nothing is simulated. H0 enumerate / open / configure, H1 the
/// frame RT n SA1·SA2 (BC→RT, sine / ramp) + SA3·SA4 (RT→BC) on bus A for --seconds, H2 the same frame on bus B then back on A,
/// H3 a long run (--minutes) with per-minute counts, no-response episodes and memory. Every phase writes &lt;tag&gt;_bc.mon (the BC's own
/// transactions: sequential monitor on the BC channel, or the message blocks on a single-function channel) and &lt;tag&gt;_bm.mon (the
/// monitor channel), plus &lt;tag&gt;_sa_values.txt (received RT→BC words per second, for the comparison with the terminal's own display).
/// </summary>
public static partial class SelfTest
{
    sealed class Phase
    {
        public string Tag = ""; public List<BusRecord> Bc = new(), Bm = new(); public List<(double wallS, ulong tagUs)> Marks = new();
        public double PollMax, PollMean, WallS; public int Polls; public string Err = ""; public long Missed; public int BlockReads; public List<string> Warnings = new();
    }
    static readonly Dictionary<string, object> SiteSum = new();
    static double SiteTickMs;   // expected counter tick (profile), 0 = measure only
    static bool Has(string a) => SelfTestKit.Has(a);

    /// <summary>--icdxml --dev &lt;device.xml&gt; [--par &lt;parameters.xml&gt;] --truth &lt;icd.csv&gt;: the device-configuration import must give the same
    /// message set (rt / sa or mode code / direction / word count) as the reference ICD CSV.</summary>
    static void IcdXmlGate()
    {
        string dev = Arg("--dev"), par = Arg("--par"), truth = Arg("--truth");
        IcdHub? hd = dev.Length > 0 ? IcdDeviceImport.FromDeviceXml(File.ReadAllText(dev)) : null, hp = par.Length > 0 ? IcdDeviceImport.FromParametersXml(File.ReadAllText(par)) : null;
        var hub = IcdDeviceImport.Merge(hd, hp); var tr = IcdHub.FromCsv(truth);
        var a = hub.Messages.Select(IcdDeviceImport.Key).OrderBy(x => x).ToList(); var b = tr.Messages.Select(IcdDeviceImport.Key).OrderBy(x => x).ToList();
        var missing = b.Except(a).ToList(); var extra = a.Except(b).ToList();
        G("icdxml_device_messages", hd?.Messages.Count ?? 0, ""); G("icdxml_parameters_messages", hp?.Messages.Count ?? 0, ""); G("icdxml_merged_messages", hub.Messages.Count, ""); G("icdxml_truth_messages", tr.Messages.Count, "");
        GB("icdxml_same_as_truth", missing.Count == 0 && extra.Count == 0 && a.Count > 0, $"merged {string.Join(", ", a)} | missing vs truth: {string.Join(", ", missing)} | extra: {string.Join(", ", extra)}");
        GB("icdxml_validates", IcdHub.Validate(hub).Count == 0, string.Join("; ", IcdHub.Validate(hub)));
        if (dev.Length > 0) { var byFile = IcdDeviceImport.FromXmlFile(dev, out string desc); GB("icdxml_sibling_merge", byFile != null && byFile.Messages.Select(IcdDeviceImport.Key).OrderBy(x => x).SequenceEqual(a), $"FromXmlFile: {desc}, {byFile?.Messages.Count ?? 0} messages"); }
    }

    static BusConfig SiteFrame(int rt, int wc, double periodMs, string bus)
    {
        var cfg = new BusConfig { MinorFrameMs = periodMs };
        cfg.Messages.Add(new BusMessage { Name = $"RT{rt}_SA1_RX", Rt = rt, Sa = 1, Tr = 0, Wc = wc, PeriodMs = periodMs, Bus = bus, Order = 0, SimulateRt = false, Words = { new BusWord { Name = "W01", Index = 1, BitStart = 1, BitLen = 16, Type = "UINT" } }, Source = new BusDataSource { Kind = "sine", Value = 1000, Amplitude = 500, PeriodS = 2 } });
        cfg.Messages.Add(new BusMessage { Name = $"RT{rt}_SA2_RX", Rt = rt, Sa = 2, Tr = 0, Wc = wc, PeriodMs = periodMs, Bus = bus, Order = 1, SimulateRt = false, Words = { new BusWord { Name = "W01", Index = 1, BitStart = 1, BitLen = 16, Type = "UINT" } }, Source = new BusDataSource { Kind = "ramp", Value = 0, Amplitude = 1000, PeriodS = 10 } });
        cfg.Messages.Add(new BusMessage { Name = $"RT{rt}_SA3_TX", Rt = rt, Sa = 3, Tr = 1, Wc = wc, PeriodMs = periodMs, Bus = bus, Order = 2, SimulateRt = false });
        cfg.Messages.Add(new BusMessage { Name = $"RT{rt}_SA4_TX", Rt = rt, Sa = 4, Tr = 1, Wc = wc, PeriodMs = periodMs, Bus = bus, Order = 3, SimulateRt = false });
        return cfg;
    }

    /// <summary>Frame from an ICD CSV (every message on <paramref name="bus"/>, period from the ICD, nothing simulated) — the site structure file.</summary>
    static BusConfig IcdFrame(string path, string bus, double defaultPeriodMs)
    {
        var hub = IcdHub.FromCsv(path); var cfg = new BusConfig(); int order = 0;
        foreach (var m in hub.Messages)
        {
            var bm = BusMessage.FromIcd(m, order++); bm.Bus = bus; bm.SimulateRt = false; if (bm.PeriodMs <= 0) bm.PeriodMs = defaultPeriodMs;
            if (bm.Tr == 0 && bm.Words.Count > 0) bm.Source = bm.Sa == 1 ? new BusDataSource { Kind = "sine", Value = 1000, Amplitude = 500, PeriodS = 2 } : bm.Sa == 2 ? new BusDataSource { Kind = "ramp", Value = 0, Amplitude = 1000, PeriodS = 10 } : new BusDataSource { Kind = "const", Value = 0x5A5A };
            cfg.Messages.Add(bm);
        }
        cfg.MinorFrameMs = cfg.Messages.Count > 0 ? cfg.Messages.Min(m => m.PeriodMs) : defaultPeriodMs;
        return cfg;
    }

    static Dictionary<int, Dictionary<int, ushort>> ParseExpect(string s)
    {
        var d = new Dictionary<int, Dictionary<int, ushort>>();
        foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':'); if (kv.Length != 2 || !kv[0].Trim().StartsWith("sa")) continue;
            int sa = int.Parse(kv[0].Trim()[2..]); var words = new Dictionary<int, ushort>();
            foreach (var w in kv[1].Split(',', StringSplitOptions.RemoveEmptyEntries)) { var p = w.Split('='); if (p.Length == 2) words[int.Parse(p[0].Trim())] = Convert.ToUInt16(p[1].Trim(), 16); }
            d[sa] = words;
        }
        return d;
    }
    static double Med(List<double> v) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
    static void SaveMon(List<BusRecord> recs, string path) { if (recs.Count == 0) return; var live = Mil1553File.Live(path); foreach (var r in recs) live.Append(r.TimeUs, r.Activity, r.Error, r.Cwd1, r.Cwd2, r.Swd1, r.Swd2, r.Data, r.RespTime1, r.RespTime2); live.SaveAs(path); }

    static async Task<Phase> RunPhase(HardwareBusDevice dev, BusConfig cfg, double seconds, int pollMs, string tag, bool retain = true, int keepSa = -1, int keepWord = 0)
    {
        var r = new Phase { Tag = tag }; var buf = new List<BusRecord>(); Mil1553File? live = retain ? null : Mil1553File.Live(Path.Combine(OutDir, tag + "_bc.mon"));
        // retain = false (memory measurement, the application's own behaviour): records go to the live capture only; the analysis keeps
        // data-less copies (plus the counter word of the counter subaddress) so the gates still run
        BusRecord Slim(BusRecord x) { if (x.Tr == 1 && x.Sa == keepSa && x.Data.Length > keepWord) return x with { Data = new[] { x.Data[keepWord], x.Data[keepWord] } }; return x with { Data = Array.Empty<ushort>() }; }
        void Split() { foreach (var rec in buf) { if (!retain) { live!.Append(rec.TimeUs, rec.Activity, rec.Error, rec.Cwd1, rec.Cwd2, rec.Swd1, rec.Swd2, rec.Data, rec.RespTime1, rec.RespTime2); } var keep = retain ? rec : Slim(rec); if (((rec.Activity >> 12) & 0xF) == dev.Channel) r.Bc.Add(keep); else r.Bm.Add(keep); } }
        try
        {
            dev.Configure(cfg); r.Warnings.AddRange(dev.Warnings); foreach (var w in dev.Warnings) L($"{tag} warning: {w}");
            dev.Start(); var run = Stopwatch.StartNew(); double sum = 0; double next = pollMs;
            while (run.Elapsed.TotalSeconds < seconds)
            {
                // Task.Delay has the 15.6 ms system-timer granularity; a message-block read must happen at least once per period (20 ms),
                // so the pacing uses Sleep(1) against the stopwatch (≈ 1–2 ms steps) and yields to the dispatcher every poll
                while (run.Elapsed.TotalMilliseconds < next) Thread.Sleep(1);
                next = run.Elapsed.TotalMilliseconds + pollMs; await Task.Yield();
                var tp = Stopwatch.StartNew(); buf.Clear(); dev.Poll(buf); double ms = tp.Elapsed.TotalMilliseconds; r.Polls++; sum += ms; if (ms > r.PollMax) r.PollMax = ms;
                if (buf.Count > 0) r.Marks.Add((run.Elapsed.TotalSeconds, buf.Max(x => x.TimeUs)));
                Split();
            }
            dev.Stop(); buf.Clear(); dev.Poll(buf); Split();
            r.WallS = run.Elapsed.TotalSeconds; r.PollMean = sum / Math.Max(1, r.Polls); r.Missed = dev.MissedTransactions; r.BlockReads = dev.BlockReads;
        }
        catch (Exception ex) { r.Err = ex.Message; L($"{tag} exception: {ex}"); try { dev.Stop(); } catch { } }
        try { if (retain) { SaveMon(r.Bc, Path.Combine(OutDir, tag + "_bc.mon")); SaveMon(r.Bm, Path.Combine(OutDir, tag + "_bm.mon")); } else live!.SaveAs(Path.Combine(OutDir, tag + "_bc.mon")); } catch (Exception ex) { L("save failed " + ex.Message); }
        L($"{tag}: {r.WallS:F1} s, polls {r.Polls} (mean {r.PollMean:F2} ms, max {r.PollMax:F1} ms), bc records {r.Bc.Count}, bm records {r.Bm.Count}, missed(est) {r.Missed}{(r.Err.Length > 0 ? ", ERROR " + r.Err : "")}");
        return r;
    }

    /// <summary>Gates of one phase on one record stream (BC transactions or the monitor channel).</summary>
    static void Analyse(string tag, List<BusRecord> recs, int rt, int sas, double periodMs, double seconds, string expectBus, Dictionary<int, Dictionary<int, ushort>> expect, (int sa, int w) counter, long missed, bool transactionMode, List<(double wallS, ulong tagUs)> marks, long expectedOverride = -1)
    {
        double perUs = periodMs * 1000; long expected = expectedOverride >= 0 ? expectedOverride : (long)Math.Round(sas * seconds * 1000 / periodMs);
        var mine = recs.Where(r => r.Rt == rt).ToList();
        G($"{tag}_records", recs.Count, ""); G($"{tag}_rt{rt}_records", mine.Count, ""); G($"{tag}_other_rt_records", recs.Count - mine.Count, "");
        long cmds = mine.Count + (transactionMode ? missed : 0);
        G($"{tag}_commands", cmds, "", $"={expected} ±1%");
        GB($"{tag}_command_count", expected > 0 && Math.Abs(cmds - expected) <= Math.Max(2, expected / 100), $"{mine.Count} records{(transactionMode ? $" + {missed} missed between block reads" : "")} = {cmds} vs {expected} expected ({sas} SA × {seconds} s / {periodMs} ms){(transactionMode ? " [message-block mode: only the last transaction per message is readable, the missed count comes from time-tag deltas]" : "")}");
        // response
        long noResp = mine.Count(r => (r.Error & 0x8000) != 0); long answered = mine.Count - noResp; double rate = mine.Count > 0 ? 100.0 * answered / mine.Count : 0;
        G($"{tag}_no_response", noResp, ""); G($"{tag}_response_rate_pct", rate, "%", ">=99");
        GB($"{tag}_rt_responds", mine.Count > 0 && rate >= 99, $"RT {rt}: {answered} answered / {mine.Count} ({rate:F2} %), NO RESPONSE {noResp}; error words seen: {string.Join(" ", mine.Where(r => r.HasError).Select(r => r.Error).Distinct().OrderBy(x => x).Select(x => $"0x{x:X4}"))}");
        // status word
        var ok = mine.Where(r => (r.Error & 0x8000) == 0).ToList(); ushort swExp = (ushort)(rt << 11);
        long swOk = ok.Count(r => r.Swd1 == swExp); G($"{tag}_status_ok_pct", ok.Count > 0 ? 100.0 * swOk / ok.Count : 0, "%", "=100");
        GB($"{tag}_status_word", ok.Count > 0 && swOk == ok.Count, $"swd1 == 0x{swExp:X4} in {swOk} / {ok.Count}; values: {string.Join(" ", ok.Select(r => r.Swd1).Distinct().OrderBy(x => x).Select(x => $"0x{x:X4}"))}");
        // bus
        long onB = mine.Count(r => r.BusB); double busPct = mine.Count > 0 ? 100.0 * (expectBus == "B" ? onB : mine.Count - onB) / mine.Count : 0;
        G($"{tag}_bus_{expectBus}_pct", busPct, "%", "=100"); GB($"{tag}_bus", mine.Count > 0 && busPct == 100, $"bus A {mine.Count - onB} / bus B {onB} (expected all on {expectBus}; activity bit 0)");
        // Δt of SA1 commands → time-stamp unit; in transaction mode normalise by the number of periods between reads
        var t1 = mine.Where(r => r.Sa == 1 && r.Tr == 0).Select(r => (double)r.TimeUs).ToList(); var dts = new List<double>();
        for (int i = 1; i < t1.Count; i++) { double d = t1[i] - t1[i - 1]; if (d <= 0) continue; double k = Math.Max(1, Math.Round(d / perUs)); dts.Add(transactionMode ? d / k : d); }
        double med = Med(dts); double errPct = med > 0 ? Math.Abs(med - perUs) / perUs * 100 : 100;
        // out-of-order pairs: in message-block mode a read that straddles a frame (a poll slower than the period) can append an older
        // transaction after a newer one of another message; the count must stay below 0.1 % of the records (a sequential monitor gives 0)
        int outOfOrder = mine.Zip(mine.Skip(1)).Count(p => p.Second.TimeUs < p.First.TimeUs); double oooPct = mine.Count > 1 ? 100.0 * outOfOrder / (mine.Count - 1) : 0;
        G($"{tag}_dt_median_us", med, "us", $"={perUs} ±1%"); G($"{tag}_dt_error_pct", errPct, "%", "<=1"); G($"{tag}_out_of_order", outOfOrder, "", "<=0.1%"); G($"{tag}_out_of_order_pct", oooPct, "%", "<=0.1");
        GB($"{tag}_timetags", dts.Count >= 5 && errPct <= 1 && oooPct <= 0.1, $"SA1 {dts.Count} intervals, median {med:F0} us vs period {perUs:F0} us ({errPct:F2} %), out-of-order pairs {outOfOrder} ({oooPct:F3} %), first 0x{(mine.Count > 0 ? mine[0].TimeUs : 0):X} high word {(mine.Count > 0 ? mine[0].TimeUs >> 32 : 0)}");
        if (marks.Count >= 2) { var a = marks[0]; var b = marks[^1]; double ratio = (b.wallS - a.wallS) > 0 ? ((double)b.tagUs - a.tagUs) / ((b.wallS - a.wallS) * 1e6) : 0; G($"{tag}_timetag_per_wall_us", ratio, "", "=1 ±1%"); GB($"{tag}_timetag_unit_us", Math.Abs(ratio - 1) <= 0.01, $"time tag advance / wall clock = {ratio:F5} (1.0 = microseconds) over {b.wallS - a.wallS:F1} s"); }
        // word-count / data-count errors (the terminal's configured buffer sizes vs the commanded WC) and the other error bits
        long dcErr = mine.Count(r => (r.Error & 0x0800) != 0), anyErr = mine.Count(r => (r.Error & 0x4000) != 0);
        G($"{tag}_datacount_errors", dcErr, "", "=0"); GB($"{tag}_data_count", mine.Count > 0 && dcErr == 0, $"DATA COUNT (0x0800) errors {dcErr}, ANY ERROR (0x4000) {anyErr} in {mine.Count} records; per command: " + string.Join(" ", mine.GroupBy(r => r.Cwd1).OrderBy(g => g.Key).Select(g => $"{(g.First().Sa is 0 or 31 ? "MC" + (g.Key & 0x1f) : "SA" + g.First().Sa)}{(g.First().Tr == 1 ? "T" : "R")}:{g.Count()}/{g.Count(r => r.HasError)}err")));
        // mode codes: the vector word (MC16 → one data word from the RT) and the status word around a synchronize-with-data (MC17)
        var mc16 = mine.Where(r => r.Sa is 0 or 31 && (r.Cwd1 & 0x1f) == 16).ToList();
        bool lite = mine.Count > 0 && mine.All(r => r.Data.Length == 0 || (r.Tr == 1 && r.Sa == counter.sa));   // --retain 0: data words were not kept, word gates are information only
        if (mc16.Count > 0 && lite) G($"{tag}_mc16_records", mc16.Count, "", "data words not retained (--retain 0): see the retained run");
        else if (mc16.Count > 0) { var vals = mc16.Where(r => r.Data.Length > 0).Select(r => r.Data[0]).Distinct().OrderBy(x => x).ToList(); G($"{tag}_mc16_records", mc16.Count, ""); G($"{tag}_mc16_no_response", mc16.Count(r => (r.Error & 0x8000) != 0), "", "=0"); GB($"{tag}_mc16_vector_word", mc16.Count > 0 && mc16.All(r => (r.Error & 0x8000) == 0) && mc16.All(r => r.Data.Length == 1), $"MC16 transmit vector word: {mc16.Count} commands, data words per record {string.Join("/", mc16.Select(r => r.Data.Length).Distinct())}, vector values: {string.Join(" ", vals.Take(12).Select(v => $"0x{v:X4}"))}{(vals.Count > 12 ? " …" : "")}, swd1 {string.Join(" ", mc16.Select(r => r.Swd1).Distinct().Select(v => $"0x{v:X4}"))}"); }
        var mc17 = mine.Where(r => r.Sa is 0 or 31 && (r.Cwd1 & 0x1f) == 17).ToList();
        if (mc17.Count > 0)
        {
            var afterSw = new List<ushort>(); var beforeSw = new List<ushort>();
            foreach (var m17 in mc17) { var after = mine.FirstOrDefault(r => r.TimeUs > m17.TimeUs && !(r.Sa is 0 or 31) && (r.Error & 0x8000) == 0); var before = mine.LastOrDefault(r => r.TimeUs < m17.TimeUs && !(r.Sa is 0 or 31) && (r.Error & 0x8000) == 0); if (after.Cwd1 != 0) afterSw.Add(after.Swd1); if (before.Cwd1 != 0) beforeSw.Add(before.Swd1); }
            G($"{tag}_mc17_records", mc17.Count, ""); G($"{tag}_mc17_no_response", mc17.Count(r => (r.Error & 0x8000) != 0), "", "=0");
            GB($"{tag}_mc17_sync", mc17.Count > 0 && mc17.All(r => (r.Error & 0x8000) == 0), $"MC17 synchronize with data word: {mc17.Count} commands (data sent {string.Join("/", mc17.Select(r => r.Data.Length).Distinct())} words), swd1 of the MC17 itself {string.Join(" ", mc17.Select(r => r.Swd1).Distinct().Select(v => $"0x{v:X4}"))}, status word of the next SA message after MC17 {string.Join(" ", afterSw.Distinct().Select(v => $"0x{v:X4}"))} vs before {string.Join(" ", beforeSw.Distinct().Select(v => $"0x{v:X4}"))} → {(afterSw.Distinct().SequenceEqual(beforeSw.Distinct()) ? "no status change" : "status changed")}");
        }
        var sa24 = mine.Where(r => r.Sa == 24).ToList();
        if (sa24.Count > 0) G($"{tag}_sa24_records", sa24.Count, ""); if (sa24.Count > 0 && !lite) GB($"{tag}_sa24_response", sa24.All(r => (r.Error & 0x8000) == 0), $"SA24 RT→BC: {sa24.Count} commands, NO RESPONSE {sa24.Count(r => (r.Error & 0x8000) != 0)}, data words {string.Join("/", sa24.Select(r => r.Data.Length).Distinct())}, DATA COUNT errors {sa24.Count(r => (r.Error & 0x0800) != 0)}, first words {(sa24[^1].Data.Length > 0 ? string.Join(" ", sa24[^1].Data.Take(8).Select(w => w.ToString("X4"))) : "-")}");
        // response time
        var rts = ok.Where(r => r.RespTime1 > 0).Select(r => (double)r.RespTime1).ToList(); G($"{tag}_resptime1_median", Med(rts), "raw", "unit: 0.1 us (UNVERIFIED)"); G($"{tag}_resptime1_min", rts.Count > 0 ? rts.Min() : 0, "raw"); G($"{tag}_resptime1_max", rts.Count > 0 ? rts.Max() : 0, "raw");
        // RT→BC words vs the terminal's own display
        foreach (var sa in ok.Where(r => r.Tr == 1).Select(r => r.Sa).Distinct().OrderBy(x => x))
        {
            var rs = ok.Where(r => r.Tr == 1 && r.Sa == sa).ToList(); var last = rs[^1];
            L($"{tag} SA{sa:00} last ({rs.Count} records, {last.Data.Length} words): {string.Join(" ", last.Data.Select(w => w.ToString("X4")))}");
            int nWords = rs.Max(r => r.Data.Length); var distinct = new List<string>();
            for (int w = 0; w < nWords; w++) { var vals = rs.Where(r => r.Data.Length > w).Select(r => r.Data[w]).Distinct().ToList(); distinct.Add(vals.Count == 1 ? $"W{w:00}={vals[0]:X4}" : $"W{w:00}={vals.Count}vals[{vals.Min():X4}..{vals.Max():X4}]"); }
            L($"{tag} SA{sa:00} per word: {string.Join(" ", distinct)}");
            if (expect.TryGetValue(sa, out var ew))
            {
                int match = 0; var det = new List<string>();
                foreach (var (w, v) in ew.OrderBy(k => k.Key)) { int hits = rs.Count(r => r.Data.Length > w && r.Data[w] == v); bool hit = hits == rs.Count && rs.Count > 0; if (hit) match++; det.Add($"W{w:00} exp {v:X4}: {hits}/{rs.Count}{(hit ? "" : $" (last {(last.Data.Length > w ? last.Data[w].ToString("X4") : "-")})")}"); }
                G($"{tag}_sa{sa}_expected_match", match, "", $"={ew.Count}"); L($"{tag} SA{sa:00} vs expected: {string.Join("; ", det)}");
            }
        }
        // the counter word
        var cr = ok.Where(r => r.Tr == 1 && r.Sa == counter.sa && r.Data.Length > counter.w).Select(r => (t: (double)r.TimeUs, v: (int)r.Data[counter.w])).ToList();
        if (cr.Count >= 2)
        {
            int decreases = 0, wraps = 0; var incs = new List<int>(); var times = new List<double>();
            for (int i = 1; i < cr.Count; i++) { int d = cr[i].v - cr[i - 1].v; if (d < -32768) { d += 65536; wraps++; } if (d < 0) decreases++; incs.Add(d); times.Add(cr[i].t - cr[i - 1].t); }
            var pos = incs.Where(d => d > 0).ToList(); int mode = pos.Count > 0 ? pos.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key : 0;
            double totalTicks = cr[^1].v - cr[0].v + wraps * 65536.0; double spanMs = (cr[^1].t - cr[0].t) / 1000; double msPerTick = totalTicks > 0 ? spanMs / totalTicks : 0;
            int zero = incs.Count(d => d == 0); int jumps = incs.Count(d => d > mode && mode > 0); long missing = mode > 0 ? incs.Where(d => d > mode).Sum(d => d / mode - 1) : 0;
            double ticksPerPeriod = msPerTick > 0 ? periodMs / msPerTick : 0;
            G($"{tag}_counter_decreases", decreases, "", "=0"); G($"{tag}_counter_mode_increase", mode, "ticks"); G($"{tag}_counter_ms_per_tick", msPerTick, "ms"); G($"{tag}_counter_ticks_per_period", ticksPerPeriod, ""); G($"{tag}_counter_repeats", zero, ""); G($"{tag}_counter_jumps", jumps, ""); G($"{tag}_counter_missing_est", missing, "", "=0");
            if (SiteTickMs > 0) GB($"{tag}_counter_tick_unit", msPerTick > 0 && Math.Abs(msPerTick - SiteTickMs) / SiteTickMs <= 0.05, $"measured {msPerTick:F3} ms/tick vs profile {SiteTickMs} ms (±5 %)");
            GB($"{tag}_counter_monotonic", decreases == 0, $"SA{counter.sa} W{counter.w:00}: {cr.Count} samples {cr[0].v} → {cr[^1].v} (wraps {wraps}), decreases {decreases}, increase mode {mode} ticks per sample, {msPerTick:F3} ms/tick, repeats {zero}, jumps {jumps} (≈ {missing} missing samples)");
            SiteSum[$"{tag}_counter"] = new { samples = cr.Count, first = cr[0].v, last = cr[^1].v, decreases, mode, msPerTick, ticksPerPeriod, repeats = zero, jumps, missing };
        }
        else G($"{tag}_counter_samples", cr.Count, "", ">=2");
        SiteSum[tag] = new { records = recs.Count, rt = mine.Count, commands = cmds, expected, noResponse = noResp, responseRatePct = rate, statusOk = swOk, busB = onB, dtMedianUs = med, dtErrorPct = errPct, respTime1Median = Med(rts), missed, transactionMode };
    }

    static void WriteSaValues(string tag, List<BusRecord> recs, int rt)
    {
        var lines = new List<string> { "# wall(s)\ttime_us\tSA\terr\tswd1\twords(hex)" }; ulong t0 = recs.Count > 0 ? recs[0].TimeUs : 0; var lastSec = new Dictionary<int, long>();
        foreach (var r in recs.Where(r => r.Rt == rt && r.Tr == 1))
        {
            long sec = (long)((r.TimeUs - t0) / 1_000_000); if (lastSec.TryGetValue(r.Sa, out long ls) && ls == sec) continue; lastSec[r.Sa] = sec;
            lines.Add($"{(r.TimeUs - t0) / 1e6:F3}\t{r.TimeUs}\tSA{r.Sa:00}\t{r.Error:X4}\t{r.Swd1:X4}\t{string.Join(" ", r.Data.Select(w => w.ToString("X4")))}");
        }
        File.WriteAllLines(Path.Combine(OutDir, tag + "_sa_values.txt"), lines);
    }

    static async Task Site(MainWindow w)
    {
        var sw = Stopwatch.StartNew();
        // bench values come from the site profile (docs/site-profile.md); explicit command-line values override it; without either only H0 runs
        SiteProfile? prof = null; string profArg = Arg("--profile");
        if (profArg.Length > 0) { prof = SiteProfile.Load(profArg); L($"profile {prof.Name} ({prof.Path}): {prof.Description}"); if (prof.Notes.Length > 0) L("profile notes: " + prof.Notes); SiteSum["profile"] = new { prof.Name, prof.Path, prof.Description, prof.Notes }; }
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        int card = int.Parse(Arg("--card", "0")), channel = int.Parse(Arg("--channel", "0")), mon = Has("--mon") ? int.Parse(Arg("--mon")) : prof?.MonitorChannel ?? -1, rt = Has("--rt") ? int.Parse(Arg("--rt")) : prof?.Rt ?? 4, wc = int.Parse(Arg("--wc", "32")), poll = int.Parse(Arg("--poll", "5"));
        double period = Has("--period") ? double.Parse(Arg("--period"), ci) : prof?.PeriodMs ?? 20, seconds = double.Parse(Arg("--seconds", "30"), ci), bseconds = double.Parse(Arg("--bseconds", "30"), ci), minutes = double.Parse(Arg("--minutes", "10"), ci), tagUnit = double.Parse(Arg("--tagunit", "1"), ci);
        var expect = Has("--expect") ? ParseExpect(Arg("--expect")) : prof?.ExpectWords() ?? new();
        (int sa, int w) counter = (-1, 0); if (Has("--counter")) { var cp = Arg("--counter").Split(':'); counter = (int.Parse(cp[0][2..]), int.Parse(cp[1])); } else if (prof?.Counter != null) counter = (prof.Counter.Sa, prof.Counter.Word);
        SiteTickMs = prof?.Counter?.MsPerTick ?? 0;
        string mainBus = Has("--bus") ? (Arg("--bus").ToUpperInvariant() == "B" ? "B" : "A") : prof?.Bus ?? "A", otherBus = mainBus == "A" ? "B" : "A"; string icd = Has("--icd") ? Arg("--icd") : prof?.Icd ?? ""; bool retain = Arg("--retain", "1") != "0";
        bool haveFrame = prof != null || icd.Length > 0 || Has("--rt");
        L($"site: card {card} channel {channel} monitor channel {mon} RT {rt} WC {wc} period {period} ms poll {poll} ms, H1 {seconds} s on bus {mainBus}, H2 {bseconds} s on bus {otherBus}, H3 {minutes} min on bus {mainBus}");
        // ---- H0 binding, enumerate, open, channel capabilities
        string bindingArg = Arg("--binding");
        var binding = DriverBinding.Load(bindingArg.Length > 0 ? new[] { bindingArg } : DriverBinding.DefaultCandidates("NexysBus"));
        GB("site_binding", binding != null, binding != null ? $"{binding.Path}: {Path.GetFileName(binding.CardLibrary)} + {Path.GetFileName(binding.BusLibrary)}" : "no driver.json");
        if (binding == null) return;
        var cards = HardwareBusDevice.Enumerate(binding, maxCards: Math.Clamp(binding.MaxCards, 1, 16));
        GB("site_enumerate", cards.Count >= 1, cards.Count == 0 ? HardwareBusDevice.LastEnumerateError : string.Join(" | ", cards.Select(k => $"{k.Name}: {k.Detail}")));
        if (cards.Count == 0) return;
        var dev = new HardwareBusDevice(card, binding, channel: channel); if (mon >= 0) dev.MonitorChannels.Add(mon); dev.TimetagUnitUs = tagUnit;
        try { dev.Open(); } catch (Exception ex) { L("open failed: " + ex.Message); }
        GB("site_open", dev.IsOpen, dev.IsOpen ? $"{dev.TypeName} / {dev.Product} / S/N {dev.Serial} / driver version 0x{dev.DriverVersion:X} / core {dev.CoreNumber} BC channel {dev.Channel}" : dev.LastError);
        if (!dev.IsOpen) { dev.Dispose(); return; }
        var caps = dev.Capabilities.Select(c => $"ch{c.Channel}: {c.Text}{(c.ConcurrentMonitor ? ", concurrent monitor" : "")}").ToList();
        GB("site_channel_info", dev.Capabilities.Count > 0, string.Join(" | ", caps)); SiteSum["channels"] = dev.Capabilities;
        var roles = ChannelRoles.Assign(dev.Capabilities, channel, mon >= 0 ? new[] { mon } : Array.Empty<int>());
        GB("site_roles", true, $"BC ch {roles.BcChannel}, RT next to BC {roles.RtOnBcChannel}, monitor next to BC {roles.MonitorOnBcChannel}, monitor ch {(roles.MonitorChannel >= 0 ? roles.MonitorChannel.ToString() : "none")}, BC records {(roles.BcTransactionRecords ? "message blocks" : "sequential monitor")}; {string.Join(" / ", roles.Notes)}");
        if (!haveFrame) { GB("site_no_frame", true, "no --profile / --icd / --rt given: enumerate, open, channel capabilities and role assignment only (nothing transmitted)"); File.WriteAllLines(Path.Combine(OutDir, "calllog.txt"), dev.CallLog); File.WriteAllText(Path.Combine(OutDir, "site_summary.json"), JsonSerializer.Serialize(SiteSum, new JsonSerializerOptions { WriteIndented = true })); dev.Dispose(); return; }
        // ---- H0 configure once (frame A) to learn the channel's limits
        var cfgA = icd.Length > 0 && File.Exists(icd) ? IcdFrame(icd, mainBus, period) : prof != null && prof.Messages.Count > 0 ? prof.ToConfig(mainBus) : SiteFrame(rt, wc, period, mainBus); string cfgErr = "";
        L($"frame: {(icd.Length > 0 ? icd : "built-in SA1..SA4")} — " + string.Join(", ", cfgA.Messages.Select(m => $"{m.Name}[{(m.IsModeCode ? "MC" + m.ModeCode : "SA" + m.Sa)}{(m.Tr == 1 ? "T" : "R")} wc {m.DataWords} {m.PeriodMs} ms]")));
        try { dev.Configure(cfgA); } catch (Exception ex) { cfgErr = ex.Message; }
        GB("site_configure", cfgErr.Length == 0, cfgErr.Length == 0 ? $"{cfgA.Messages.Count} messages ({(icd.Length > 0 ? Path.GetFileName(icd) : $"RT {rt} SA1/SA2 BC→RT WC {wc}, SA3/SA4 RT→BC WC {wc}")}, minor {dev.MinorFrameMs} ms major {dev.MajorFrameMs} ms), {dev.CallLog.Count} driver calls, warnings {dev.Warnings.Count}" : cfgErr);
        foreach (var wn in dev.Warnings) L("configure warning: " + wn);
        bool monCh = mon >= 0 && !dev.Warnings.Any(x => x.Contains("monitor channel"));
        GB("site_monitor_mode", cfgErr.Length == 0, $"BC channel monitor {(dev.MonitorOnBcChannel ? "yes (sequential monitor carries BC transactions)" : "no → message-block transaction records")}; monitor channel {(mon >= 0 ? mon.ToString() : "none")} {(mon >= 0 ? (monCh ? "configured" : "REFUSED") : "")}");
        if (cfgErr.Length > 0) { File.WriteAllLines(Path.Combine(OutDir, "calllog.txt"), dev.CallLog); dev.Dispose(); return; }
        // ---- H1 bus A
        var h1 = await RunPhase(dev, cfgA, seconds, poll, "h1");
        GB("site_h1_run", h1.Err.Length == 0 && h1.PollMax <= 200, h1.Err.Length > 0 ? h1.Err : $"{h1.WallS:F1} s, polls {h1.Polls}, poll mean {h1.PollMean:F2} ms max {h1.PollMax:F1} ms, block reads/poll {h1.BlockReads}");
        G("site_h1_poll_max_ms", h1.PollMax, "ms", "<=200"); G("site_h1_poll_mean_ms", h1.PollMean, "ms");
        GB("site_h1_ch1_wired", h1.Bm.Count > 0, mon < 0 ? "no monitor channel requested" : !monCh ? $"monitor channel {mon} refused by the driver" : h1.Bm.Count > 0 ? $"monitor channel {mon} saw {h1.Bm.Count} records while the BC ran on channel {channel} → wired to the same bus" : $"monitor channel {mon} saw 0 records in {h1.WallS:F0} s → not wired (or wired to another bus)");
        int sas = cfgA.Messages.Count(m => m.Enabled && Math.Abs(m.PeriodMs - cfgA.Messages.Min(x => x.PeriodMs)) < 1e-9); long expectedH1 = cfgA.Messages.Where(m => m.Enabled).Sum(m => (long)Math.Round(h1.WallS * 1000 / m.PeriodMs));
        Analyse("site_h1_bc", h1.Bc, rt, 4, period, h1.WallS, mainBus, expect, counter, h1.Missed, dev.BcTransactionRecords, h1.Marks, expectedH1);
        if (h1.Bm.Count > 0) Analyse("site_h1_bm", h1.Bm, rt, 4, period, h1.WallS, mainBus, expect, counter, 0, false, h1.Marks);
        WriteSaValues("h1", h1.Bm.Count > 0 ? h1.Bm : h1.Bc, rt);
        int lastCounterH1 = (h1.Bm.Count > 0 ? h1.Bm : h1.Bc).Where(r => r.Rt == rt && r.Tr == 1 && r.Sa == counter.sa && (r.Error & 0x8000) == 0 && r.Data.Length > counter.w).Select(r => (int)r.Data[counter.w]).LastOrDefault(-1);
        // ---- H2 bus B, then back to A
        if (bseconds > 0)
        {
            var cfgB = icd.Length > 0 && File.Exists(icd) ? IcdFrame(icd, otherBus, period) : prof != null && prof.Messages.Count > 0 ? prof.ToConfig(otherBus) : SiteFrame(rt, wc, period, otherBus);
            var h2 = await RunPhase(dev, cfgB, bseconds, poll, "h2_bus" + otherBus);
            GB("site_h2_run", h2.Err.Length == 0, h2.Err.Length > 0 ? h2.Err : $"{h2.WallS:F1} s on bus {otherBus}, bc records {h2.Bc.Count}, bm records {h2.Bm.Count}");
            Analyse("site_h2_bc", h2.Bc, rt, 4, period, h2.WallS, otherBus, expect, counter, h2.Missed, dev.BcTransactionRecords, h2.Marks, cfgB.Messages.Where(m => m.Enabled).Sum(m => (long)Math.Round(h2.WallS * 1000 / m.PeriodMs)));
            if (h2.Bm.Count > 0) Analyse("site_h2_bm", h2.Bm, rt, 4, period, h2.WallS, otherBus, expect, counter, 0, false, h2.Marks);
            int firstCounterH2 = (h2.Bm.Count > 0 ? h2.Bm : h2.Bc).Where(r => r.Rt == rt && r.Tr == 1 && r.Sa == counter.sa && (r.Error & 0x8000) == 0 && r.Data.Length > counter.w).Select(r => (int)r.Data[counter.w]).FirstOrDefault(-1);
            if (lastCounterH1 >= 0 && firstCounterH2 >= 0) { int gap = firstCounterH2 - lastCounterH1; if (gap < 0) gap += 65536; G("site_h2_counter_gap_ticks", gap, "ticks", $"switch {mainBus}→{otherBus} (reconfigure + restart)"); }
            var h2a = await RunPhase(dev, cfgA, 5, poll, "h2_back_bus" + mainBus);
            bool backOk = h2a.Bc.Concat(h2a.Bm).Any(r => r.Rt == rt && (r.Error & 0x8000) == 0 && (r.BusB == (mainBus == "B")));
            GB("site_h2_back_to_main_bus", h2a.Err.Length == 0 && backOk, h2a.Err.Length > 0 ? h2a.Err : $"5 s back on bus {mainBus}: bc {h2a.Bc.Count} (bus B {h2a.Bc.Count(r => r.BusB)}), bm {h2a.Bm.Count}, NO RESPONSE {h2a.Bc.Count(r => (r.Error & 0x8000) != 0)}, answered {h2a.Bc.Count(r => (r.Error & 0x8000) == 0)}");
        }
        // ---- H3 long run
        if (minutes > 0)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); long ws0 = WsMb(); var h3 = await RunPhase(dev, cfgA, minutes * 60, poll, "h3", retain, counter.sa, counter.w);
            GC.Collect(); GC.WaitForPendingFinalizers(); long ws1 = WsMb(); var src = h3.Bm.Count > 0 ? h3.Bm : h3.Bc;
            GB("site_h3_run", h3.Err.Length == 0, h3.Err.Length > 0 ? h3.Err : $"{h3.WallS / 60:F1} min, polls {h3.Polls}, poll max {h3.PollMax:F1} ms, bc records {h3.Bc.Count}, bm records {h3.Bm.Count}, retain {retain}");
            G("site_h3_memory_delta_mb", ws1 - ws0, "MB", "<=50"); GB("site_h3_memory", ws1 - ws0 <= 50, $"working set {ws0} → {ws1} MB after GC ({(retain ? "self-test keeps every record in memory" : "records only in the live capture, as the application does")})");
            Analyse("site_h3_bc", h3.Bc, rt, 4, period, h3.WallS, mainBus, retain ? expect : new(), counter, h3.Missed, dev.BcTransactionRecords, h3.Marks, cfgA.Messages.Where(m => m.Enabled).Sum(m => (long)Math.Round(h3.WallS * 1000 / m.PeriodMs)));
            if (h3.Bm.Count > 0) Analyse("site_h3_bm", h3.Bm, rt, 4, period, h3.WallS, mainBus, expect, counter, 0, false, h3.Marks);
            WriteSaValues("h3", src, rt);
            // per-minute counts and no-response episodes (the terminal stopped / restarted by the operator)
            var mine = src.Where(r => r.Rt == rt).ToList(); ulong t0 = mine.Count > 0 ? mine[0].TimeUs : 0;
            var perMin = mine.GroupBy(r => (int)((r.TimeUs - t0) / 60_000_000)).OrderBy(g => g.Key).Select(g => $"min {g.Key}: {g.Count()} msgs, {g.Count(r => (r.Error & 0x8000) != 0)} no-resp").ToList();
            foreach (var pm in perMin) L("h3 " + pm); SiteSum["h3_per_minute"] = perMin;
            var episodes = new List<string>(); bool inEp = false; ulong epStart = 0; int epCount = 0;
            foreach (var r in mine)
            {
                bool nr = (r.Error & 0x8000) != 0;
                if (nr && !inEp) { inEp = true; epStart = r.TimeUs; epCount = 0; }
                if (nr) epCount++;
                if (!nr && inEp) { inEp = false; episodes.Add($"{(epStart - t0) / 1e6:F1}s → {(r.TimeUs - t0) / 1e6:F1}s: {epCount} no-response, recovered"); }
            }
            if (inEp) episodes.Add($"{(epStart - t0) / 1e6:F1}s → end: {epCount} no-response, not recovered");
            G("site_h3_noresponse_episodes", episodes.Count, ""); foreach (var e in episodes) L("h3 episode " + e); SiteSum["h3_episodes"] = episodes;
        }
        File.WriteAllLines(Path.Combine(OutDir, "calllog.txt"), dev.CallLog.TakeLast(400));
        SiteSum["warnings"] = dev.Warnings; SiteSum["device"] = new { dev.TypeName, dev.Product, dev.Serial, driverVersion = dev.DriverVersion, dev.Channel, monitor = mon, dev.MonitorOnBcChannel, dev.BcTransactionRecords, dev.TimetagUnitUs };
        File.WriteAllText(Path.Combine(OutDir, "site_summary.json"), JsonSerializer.Serialize(SiteSum, new JsonSerializerOptions { WriteIndented = true }));
        G("site_total_s", sw.Elapsed.TotalSeconds, "s");
        dev.Dispose();
    }
}
