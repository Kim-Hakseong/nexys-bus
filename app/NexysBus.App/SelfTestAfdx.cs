using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NexysView.Core.Arinc664;
using NexysView.Core.Bus;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>
/// --afdx [--corpus dir] [--frames 1000000]: the ARINC 664 path on the real UI — new 664 project → ICD import → VL edit (+ validation)
/// → virtual send (batch and real time) → ANALYZE (list, filter, decode, trend) → INTEROP (checklist, peer table, report) — plus the
/// truth corpus (every item of every truth JSON), error detection, 1,000 VL-seconds without loss, BAG accuracy of the real-time sender
/// and the 1M-frame performance gates. A 1553 project must not load the 664 side (lazy-load gate).
/// </summary>
public static partial class SelfTest
{
    static async Task Afdx(MainWindow w)
    {
        var e = w.Engine; string dir = Path.Combine(OutDir, "data"); Directory.CreateDirectory(dir); var sw = Stopwatch.StartNew();
        string corpus = Arg("--corpus", "");
        // ---------------------------------------------------------------- 0. a 1553 project does not load the 664 side
        e.NewProject("lazy-1553"); e.Mil1553.LoadDummyIcd(); await Idle(w, 300);
        bool web664 = (await JS(w, "return nx.test.afdxBuilt();")).GetBoolean();
        GB("lazy_1553_project_no_664", !e.AfdxLoaded && !web664, $"1553 project: AfdxService created {e.AfdxLoaded}, 664 pages built {web664}");

        // ---------------------------------------------------------------- 1. new ARINC 664 project from START
        await JS(w, "await nx.test.rail('START'); await nx.test.clickSel('[data-a=new664]'); return true;"); await Idle(w, 300);
        GB("new_664_project", e.Project.IsAfdx && e.Project.Afdx != null, $"protocol {e.Project.Protocol}, rail {e.Mode}");

        // ---------------------------------------------------------------- 2. ICD import (CSV, one row per parameter)
        string icdCsv = corpus.Length > 0 && File.Exists(Path.Combine(corpus, "c02_vl8_icd.csv")) ? Path.Combine(corpus, "c02_vl8_icd.csv") : Path.Combine(dir, "afdx_icd.csv");
        if (!File.Exists(icdCsv)) File.WriteAllText(icdCsv, AfdxIcd.ToCsv(AfdxIcd.Dummy()));
        e.Mil1553.LoadIcd(icdCsv); await Idle(w, 300);
        await JS(w, "await nx.test.rail('ICD'); return true;"); await Idle(w, 400);
        int nVl = e.Project.Icd.Vls?.Count ?? 0; int rows = (await JS(w, "return nx.test.afdx().icd.rows();")).GetInt32();
        GB("icd_import", nVl > 0 && rows == nVl && e.Project.Icd.Schema == "1.1", $"{Path.GetFileName(icdCsv)}: {nVl} VLs, {e.Project.Icd.Vls!.Sum(v => v.Ports.Sum(p => p.Params.Count))} parameters, ICD rail rows {rows}, schema {e.Project.Icd.Schema}");
        await Shot(w, "afdx_icd");
        // ---------------------------------------------------------------- 3. VL edit through the table + validation
        var v0 = e.Project.Icd.Vls![0]; int id0 = v0.VlId;
        await JS(w, $"await nx.test.afdx().icd.setVl({id0}, 'lmax', '40'); return true;"); await Idle(w, 300);
        var val = (await JS(w, "return nx.test.afdx().icd.validation();")).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        bool sawErr = val.Any(x => x.Contains("Lmax 40"));
        await JS(w, $"await nx.test.afdx().icd.setVl({id0}, 'lmax', '1518'); await nx.test.afdx().icd.setVl({id0}, 'bag_ms', '16'); return true;"); await Idle(w, 300);
        GB("vl_edit_validation", sawErr && v0.Lmax == 1518 && v0.BagMs == 16 && AfdxIcd.Validate(e.Project.Icd).All(x => !x.StartsWith("error")), $"Lmax 40 → '{val.FirstOrDefault(x => x.Contains("Lmax")) ?? "no error"}'; restored Lmax {v0.Lmax}, BAG edited to {v0.BagMs} ms");
        e.SaveProject(Path.Combine(dir, "afdx.nxb")); var back = BusProject.Load(Path.Combine(dir, "afdx.nxb"));
        GB("project_roundtrip", back.IsAfdx && back.Icd.Vls?.Count == nVl && back.Icd.Vls[0].BagMs == 16, $"afdx.nxb: protocol {back.Protocol}, {back.Icd.Vls?.Count} VLs, VL {id0} BAG {back.Icd.Vls?[0].BagMs}");

        // ---------------------------------------------------------------- 4. virtual send: 10 s batch from the SIMULATE rail
        await JS(w, "await nx.test.rail('SIMULATE'); return true;"); await Idle(w, 300);
        await JS(w, "await nx.test.afdx().sim.click('batch'); return true;"); await Idle(w, 600);
        var cap = e.Afdx.Capture!; var an = e.Afdx.Analysis!; var st = e.Afdx.RunDto(); long sent = JsonSerializer.SerializeToElement(st).GetProperty("sent").GetInt64();
        GB("batch_send_receive", cap.Count == sent && sent > 0 && an.TotalErrors == 0, $"10 s batch: sent {sent:N0}, received {cap.Count:N0}, check findings {an.TotalErrors}");
        var bagOk = an.Vls.Values.All(v => Math.Abs(v.MeanIntervalMs(0) + v.MeanIntervalMs(1) - (v.Frames[0] > 0 && v.Frames[1] > 0 ? 2 : 1) * v.BagMs) < 1e-6);
        GB("batch_bag_exact", bagOk, "virtual clock: every VL's mean interval = BAG exactly");
        await Shot(w, "afdx_simulate");

        // ---------------------------------------------------------------- 5. ANALYZE: list, filter, decode, trend
        await JS(w, "await nx.test.rail('ANALYZE'); return true;"); await Idle(w, 800);
        int total = (await JS(w, "return nx.test.afdx().an.total();")).GetInt32();
        int vlF = (await JS(w, $"return await nx.test.afdx().an.filter({{ vl: {id0} }});")).GetInt32();
        long vlFrames = an.Vls[id0].Frames[0] + an.Vls[id0].Frames[1];
        string dec = (await JS(w, "return await nx.test.afdx().an.selectRow(3);")).GetString() ?? ""; int nPar = (await JS(w, "return nx.test.afdx().an.decodedParams();")).GetInt32();
        int chart = (await JS(w, "return await nx.test.afdx().an.trendFirst();")).GetInt32(); await Idle(w, 500);
        var ch = e.Charts.FirstOrDefault(c => c.Id == chart);
        GB("analyze_list_filter", total == cap.Count && vlF == vlFrames, $"list {total:N0} = capture {cap.Count:N0}; VL {id0} filter {vlF} = {vlFrames}");
        GB("analyze_decode_trend", nPar > 0 && ch != null && ch.Model.Curves.Count == 1 && ch.Model.Curves[0].Channel.Length > 0, $"decoded parameters {nPar}; trend chart {chart} curves {ch?.Model.Curves.Count} samples {ch?.Model.Curves.FirstOrDefault()?.Channel.Length}");
        await Shot(w, "afdx_analyze");
        int errOnly = (await JS(w, "return await nx.test.afdx().an.filter({ errors: true });")).GetInt32();
        // inject sender faults, batch again: the checks must report exactly what was injected
        var se = e.Project.Afdx!.Errors; se.SnSkipEvery = 100; se.DropBEvery = 240; se.EarlyEvery = 300; se.OversizeEvery = 400;
        e.Afdx.RunFor(2); var a2 = e.Afdx.Analysis!; var snd = e.Afdx.LastSender!;
        int detected = 0, expectedItems = 0; var lines = new List<string>();
        foreach (var vs in a2.Vls.Values)
        {
            // expected counts from the injection rules, frame by frame (r = regular frame number of the VL, 1-based)
            var tv = snd.Vls.First(x => x.Vl.VlId == vs.Vl); var vl = tv.Vl; long reg = tv.Regular; bool hasA = vl.Network != "B", hasB = vl.Network != "A";
            long[] gaps = new long[2], bagv = new long[2]; long lmaxv = 0, missB = 0;
            for (long r = 1; r <= reg; r++)
            {
                bool skip = r % 100 == 0, dropB = hasB && r % 240 == 0, early = r % 300 == 0, over = r % 400 == 0; double tMs = 1 + (r - 1) * vl.BagMs;
                for (int n = 0; n < 2; n++)
                {
                    if (n == 0 && !hasA || n == 1 && !hasB) continue; bool dropped = n == 1 && dropB;
                    if (skip) gaps[n]++; if (dropped) gaps[n]++;
                    if (early && !dropped) bagv[n]++; if (over && !dropped) lmaxv++;
                }
                if (dropB && hasA && 2000 - tMs > 25) missB++;
            }
            void Ex(string what, long want, long got) { expectedItems++; if (want == got) detected++; else lines.Add($"VL {vs.Vl} {what}: injected {want} found {got}"); }
            Ex("sn gaps A", gaps[0], vs.SnGaps[0]); Ex("sn gaps B", gaps[1], vs.SnGaps[1]);
            Ex("bag violations A", bagv[0], vs.BagViolations[0]); Ex("bag violations B", bagv[1], vs.BagViolations[1]);
            Ex("lmax violations", lmaxv, vs.LmaxViolations);
            if (vl.Network == "AB") Ex("missing B", missB, vs.Missing[1]);
        }
        GB("sender_faults_detected", detected == expectedItems, $"{detected}/{expectedItems} injected-fault counts found exactly" + (lines.Count > 0 ? ": " + string.Join("; ", lines.Take(6)) : ""));
        se.SnSkipEvery = se.DropBEvery = se.EarlyEvery = se.OversizeEvery = 0;
        GB("errors_only_clean_run", errOnly == 0, $"ERRORS ONLY on the clean batch: {errOnly} frames");

        // ---------------------------------------------------------------- 6. INTEROP
        string c04 = corpus.Length > 0 ? Path.Combine(corpus, "c04_errors.pcapng") : "";
        if (File.Exists(c04))
        {
            e.Mil1553.LoadIcd(Path.Combine(corpus, "c04_errors_icd.csv")); e.Afdx.OpenCapture(c04);
            await JS(w, "await nx.test.rail('INTEROP'); await nx.test.afdx().iop.reload(); return true;"); await Idle(w, 400);
            var checks = (await JS(w, "return nx.test.afdx().iop.checks();")).EnumerateArray().ToDictionary(x => x.GetProperty("id").GetString()!, x => x.GetProperty("verdict").GetString()!);
            bool want = checks.GetValueOrDefault("sn") == "FAIL" && checks.GetValueOrDefault("bag") == "FAIL" && checks.GetValueOrDefault("lmax") == "FAIL" && checks.GetValueOrDefault("ab") == "FAIL" && checks.GetValueOrDefault("vl_seen") == "PASS";
            GB("interop_faulty_capture", want, "c04_errors: " + string.Join(" ", checks.Select(kv => $"{kv.Key}={kv.Value}")));
            await Shot(w, "afdx_interop_faults");
            e.Mil1553.LoadIcd(Path.Combine(corpus, "c02_vl8_icd.csv")); e.Afdx.OpenCapture(Path.Combine(corpus, "c02_vl8.pcapng"));
            await JS(w, "await nx.test.rail('INTEROP'); await nx.test.afdx().iop.reload(); return true;"); await Idle(w, 400);
            var c2 = (await JS(w, "return nx.test.afdx().iop.checks();")).EnumerateArray().Select(x => (id: x.GetProperty("id").GetString()!, v: x.GetProperty("verdict").GetString()!)).ToList();
            GB("interop_clean_capture", c2.All(x => x.v != "FAIL") && c2.Count(x => x.v == "PASS") >= 8, "c02_vl8: " + string.Join(" ", c2.Select(x => $"{x.id}={x.v}")));
        }
        else L("interop corpus checks skipped (no --corpus)");
        // NexysBus → peer: run, then type what the "peer" reported
        e.Afdx.RunFor(1); await JS(w, "await nx.test.rail('INTEROP'); await nx.test.afdx().iop.reload(); return true;"); await Idle(w, 300);
        var tx = e.Afdx.TxTable(); var r0 = tx[0]; var r1 = tx.Count > 1 ? tx[1] : tx[0];
        await JS(w, $"await nx.test.afdx().iop.setPeer({r0.Vl}, 'rxA', '{r0.SentA}'); await nx.test.afdx().iop.setPeer({r0.Vl}, 'rxB', '{r0.SentB}'); await nx.test.afdx().iop.setPeer({r1.Vl}, 'rxA', '{Math.Max(0, r1.SentA - 3)}'); return true;"); await Idle(w, 300);
        var tx2 = e.Afdx.TxTable();
        GB("interop_peer_table", tx2[0].Verdict == "PASS" && (tx.Count < 2 || tx2[1].Verdict == "FAIL"), $"VL {r0.Vl} peer = sent → {tx2[0].Verdict}; VL {r1.Vl} peer 3 short → {(tx.Count > 1 ? tx2[1].Verdict : "-")}");
        e.Project.Afdx!.PeerName = "self-test peer";
        string rep = e.Afdx.ExportReport(Path.Combine(OutDir, "interop-report.md")); string rtext = File.ReadAllText(rep);
        GB("interop_report", rtext.Contains("## 1. Peer → NexysBus") && rtext.Contains("## 2. NexysBus → peer") && rtext.Contains("| " + r0.Vl + " |"), $"{rep} {rtext.Length} chars");
        await Shot(w, "afdx_interop");

        // ---------------------------------------------------------------- 7. truth corpus + error detection
        if (corpus.Length > 0 && Directory.Exists(corpus))
        {
            int okAll = 0, totAll = 0, errOk = 0, errTot = 0; var notes = new List<string>();
            foreach (var f in Directory.EnumerateFiles(corpus, "*.pcapng").OrderBy(x => x))
            {
                var (ok, tot, diffs, eok, etot) = CompareTruth(f); okAll += ok; totAll += tot; errOk += eok; errTot += etot;
                L($"truth {Path.GetFileName(f)}: {ok}/{tot}" + (diffs.Count > 0 ? " — " + string.Join("; ", diffs.Take(5)) : "")); notes.Add($"{Path.GetFileNameWithoutExtension(f)} {ok}/{tot}");
            }
            GB("truth_corpus", okAll == totAll && totAll > 0, $"{okAll}/{totAll} truth items ({string.Join(", ", notes)})");
            GB("error_detection", errOk == errTot && errTot > 0, $"{errOk}/{errTot} injected-error items detected with the exact count");
        }

        // ---------------------------------------------------------------- 8. 1,000 VL-seconds on the virtual network without loss
        {
            var vls = Enumerable.Range(0, 100).Select(i => new IcdVl { VlId = 2000 + i, Name = $"L{i}", BagMs = 1 << (i % 6), Network = "AB", Lmax = 200, Ports = { new IcdAfdxPort { Name = $"L{i}", UdpDst = 40000 + i, Size = 100, Params = { new IcdAfdxParam { Name = $"L{i}_CNT", Type = "u16" } } } } }).ToList();
            var dev = new VirtualNetworkDevice { ManualClock = true }; dev.Open(); dev.Start(); var txs = new AfdxSender(dev, vls); var cp = new AfdxCapture(1 << 24);
            var t0 = Stopwatch.StartNew(); txs.RunBatch(dev, 10, (n, t, f) => cp.Add(n, t, f)); double ms = t0.Elapsed.TotalMilliseconds;
            var aa = new AfdxAnalysis(cp, new IcdHub { Vls = vls }); long s = txs.Vls.Sum(v => v.Sent[0] + v.Sent[1]);
            GB("virtual_1000_vl_seconds", s == cp.Count && aa.TotalErrors == 0 && aa.Vls.Values.All(v => v.Missing[0] + v.Missing[1] == 0), $"100 VLs × 10 s: sent {s:N0}, received {cp.Count:N0}, lost {s - cp.Count}, findings {aa.TotalErrors} ({ms:F0} ms)");
        }

        // ---------------------------------------------------------------- 9. real-time sender accuracy (1 ms BAG) on the virtual network
        {
            var vls = new List<IcdVl> { new() { VlId = 3000, Name = "RT1", BagMs = 1, Network = "AB", Lmax = 200, Ports = { new IcdAfdxPort { Name = "RT1", UdpDst = 41000, Size = 64 } } } };
            for (int i = 1; i < 16; i++) vls.Add(new IcdVl { VlId = 3000 + i, Name = $"RT{i + 1}", BagMs = 1 << (i % 5), Network = "AB", Lmax = 300, Ports = { new IcdAfdxPort { Name = $"RT{i + 1}", UdpDst = 41000 + i, Size = 200 } } });
            double secs = double.TryParse(Arg("--rtseconds", "5"), System.Globalization.CultureInfo.InvariantCulture, out var rs) ? rs : 5;
            var dev = new VirtualNetworkDevice { Faults = new NetFaults { DelayUs = 20, SkewBUs = 50 } }; dev.Open(); dev.Start(); var txr = new AfdxSender(dev, vls); var cp = new AfdxCapture(1 << 24);
            txr.Start(); var t0 = Stopwatch.StartNew();
            while (t0.Elapsed.TotalSeconds < secs) { await Task.Delay(50); dev.Poll((n, t, f) => cp.Add(n, t, f)); }
            txr.Stop(); await Task.Delay(50); dev.Poll((n, t, f) => cp.Add(n, t, f));
            var dev1 = new List<double>(); long last = long.MinValue;
            for (int i = 0; i < cp.Count; i++) { if (cp.Vl(i) != 3000 || cp.Network(i) != 0) continue; long t = cp.TimeNs(i); if (last != long.MinValue) dev1.Add(Math.Abs((t - last) / 1e6 - 1.0)); last = t; }
            dev1.Sort(); double P(double q) => dev1.Count == 0 ? double.NaN : dev1[Math.Min(dev1.Count - 1, (int)(q * dev1.Count))];
            var t1 = txr.Vls[0]; var aa = new AfdxAnalysis(cp, new IcdHub { Vls = vls });
            G("rt_bag1_interval_dev_p50_ms", P(0.5), "ms"); G("rt_bag1_interval_dev_p99_ms", P(0.99), "ms", "≤ 0.5"); G("rt_bag1_interval_dev_max_ms", dev1.Count > 0 ? dev1[^1] : double.NaN, "ms");
            G("rt_bag1_late_sigma_us", t1.LateStdUs, "µs"); G("rt_bag1_late_max_us", t1.LateMaxUs, "µs"); G("rt_bag1_schedule_slips", t1.Slips, "slips"); G("rt_all_bag_violations", aa.Vls.Values.Sum(v => v.BagViolations[0] + v.BagViolations[1]), "intervals", "0");
            GB("rt_bag1_jitter_gate", P(0.99) <= 0.5 && aa.Vls.Values.Sum(v => v.BagViolations[0] + v.BagViolations[1]) == 0, $"1 ms BAG for {secs:G} s with 15 other VLs: |interval − 1 ms| p50 {P(0.5):F4} · p99 {P(0.99):F4} · max {(dev1.Count > 0 ? dev1[^1] : 0):F4} ms over {dev1.Count:N0} intervals; BAG violations (interval < BAG − 0.5 ms) VL 3000 {aa.Vls[3000].BagViolations[0]}, all VLs {aa.Vls.Values.Sum(v => v.BagViolations[0] + v.BagViolations[1])}: " + string.Join("; ", aa.Vls.Values.Where(v => v.BagViolations[0] + v.BagViolations[1] > 0).Take(6).Select(v => $"VL {v.Vl} BAG {v.BagMs} A {v.BagViolations[0]} B {v.BagViolations[1]} min {v.IntervalMinMs[0]:F3}/{v.IntervalMinMs[1]:F3} ms slips {txr.Vls.First(x => x.Vl.VlId == v.Vl).Slips}")));
        }

        // ---------------------------------------------------------------- 10. 1M-frame performance
        {
            int target = int.TryParse(Arg("--frames", "1000000"), out var tf) ? tf : 1_000_000;
            var vls = Enumerable.Range(0, 100).Select(i => new IcdVl { VlId = 4000 + i, Name = $"P{i}", BagMs = 1, Network = "AB", Lmax = 200, Ports = { new IcdAfdxPort { Name = $"P{i}", UdpDst = 42000 + i, Size = 64, Params = { new IcdAfdxParam { Name = $"P{i}_CNT", Type = "u16" }, new IcdAfdxParam { Name = $"P{i}_SIN", Offset = 2, Type = "f32" } } } } }).ToList();
            var dev = new VirtualNetworkDevice { ManualClock = true }; dev.Open(); dev.Start(); var txp = new AfdxSender(dev, vls); var big = new AfdxCapture(1 << 26);
            txp.RunBatch(dev, target / 200 / 1000.0 + 0.0005, (n, t, f) => big.Add(n, t, f));
            string path = Path.Combine(dir, "afdx_1m.pcapng"); big.Save(path); long fileMb = new FileInfo(path).Length >> 20; big = null!; GC.Collect();
            var hubP = new IcdHub { Source = "perf", Schema = "1.1", Vls = vls };
            e.Project.Icd.Vls = hubP.Vls; e.Project.Icd.Source = "perf"; long ws0 = WsMb();
            var t0 = Stopwatch.StartNew(); var o = JsonSerializer.SerializeToElement(e.Afdx.OpenCapture(path)); double openMs = t0.Elapsed.TotalMilliseconds; long ws1 = WsMb();
            int frames = o.GetProperty("frames").GetInt32();
            G("perf_frames", frames, "frames"); G("perf_open_and_check_ms", openMs, "ms", "≤ 5000"); G("perf_file_mb", fileMb, "MB"); G("perf_ws_delta_mb", ws1 - ws0, "MB");
            t0.Restart(); var fr = JsonSerializer.SerializeToElement(e.Afdx.SetFilter(JsonSerializer.SerializeToElement(new { vl = 4050 }))); double filterMs = t0.Elapsed.TotalMilliseconds;
            t0.Restart(); e.Afdx.SetFilter(JsonSerializer.SerializeToElement(new { errors = true })); double errMs = t0.Elapsed.TotalMilliseconds;
            e.Afdx.SetFilter(JsonSerializer.SerializeToElement(new { })); t0.Restart(); for (int k = 0; k < 20; k++) e.Afdx.Frames(k * 50_000, 200); double pageMs = t0.Elapsed.TotalMilliseconds / 20;
            t0.Restart(); e.Afdx.Stats(); double statMs = t0.Elapsed.TotalMilliseconds;
            G("perf_filter_vl_ms", filterMs, "ms", "≤ 1000"); G("perf_filter_errors_ms", errMs, "ms", "≤ 1000"); G("perf_page_200_ms", pageMs, "ms", "≤ 50"); G("perf_stats_ms", statMs, "ms");
            await JS(w, "await nx.test.rail('ANALYZE'); return true;"); await Idle(w, 1500);
            t0.Restart(); int shown = (await JS(w, "const a = nx.test.afdx().an; await a.filter({}); return a.rowsShown();")).GetInt32(); double uiMs = t0.Elapsed.TotalMilliseconds;
            G("perf_ui_first_page_ms", uiMs, "ms");
            GB("perf_1m_gates", frames >= target * 0.99 && openMs <= 5000 && filterMs <= 1000 && errMs <= 1000 && pageMs <= 50 && shown > 0, $"{frames:N0} frames: open+checks {openMs:F0} ms, VL filter {filterMs:F0} ms ({fr.GetProperty("total").GetInt32():N0}), errors filter {errMs:F0} ms, page {pageMs:F2} ms, stats {statMs:F0} ms, UI rows {shown} in {uiMs:F0} ms");
            await Shot(w, "afdx_analyze_1m");
        }
        L($"afdx self-test {sw.Elapsed.TotalSeconds:F1} s");
    }

    /// <summary>Compare a corpus capture with its truth JSON (+ ICD CSV): all items, and the subset that are injected errors (non-zero counts).</summary>
    static (int ok, int total, List<string> diffs, int errOk, int errTotal) CompareTruth(string pcap)
    {
        string stem = pcap[..^".pcapng".Length];
        var truth = JsonDocument.Parse(File.ReadAllText(stem + ".truth.json")).RootElement; var icd = IcdHub.FromCsv(stem + "_icd.csv");
        var cap = AfdxCapture.Open(pcap); var a = new AfdxAnalysis(cap, icd, new AfdxCheckOptions { JmaxMs = truth.GetProperty("jmax_ms").GetDouble() });
        int ok = 0, total = 0, eok = 0, etot = 0; var diffs = new List<string>();
        void Eq(string what, double want, double got, double tol = 0, bool err = false) { total++; bool good = Math.Abs(want - got) <= tol; if (good) ok++; else diffs.Add($"{what}: truth {want} got {got}"); if (err && want != 0) { etot++; if (good) eok++; } }
        Eq("frames", truth.GetProperty("frames_total").GetInt32(), cap.Count); Eq("arp", truth.GetProperty("arp").GetInt32(), a.Arp); Eq("icmp", truth.GetProperty("icmp").GetInt32(), a.Icmp);
        foreach (var vp in truth.GetProperty("vls").EnumerateObject())
        {
            int vl = int.Parse(vp.Name); var t = vp.Value; if (!a.Vls.TryGetValue(vl, out var s)) { total++; diffs.Add($"VL {vl} missing"); continue; }
            for (int n = 0; n < 2; n++)
            {
                string x = n == 0 ? "a" : "b";
                Eq($"VL {vl} frames_{x}", t.GetProperty("frames_" + x).GetInt32(), s.Frames[n]);
                Eq($"VL {vl} sn_gaps_{x}", t.GetProperty("sn_gaps_" + x).GetInt32(), s.SnGaps[n], err: true); Eq($"VL {vl} sn_reversed_{x}", t.GetProperty("sn_reversed_" + x).GetInt32(), s.SnReversed[n], err: true);
                Eq($"VL {vl} sn_resets_{x}", t.GetProperty("sn_resets_" + x).GetInt32(), s.SnResets[n], err: true); Eq($"VL {vl} bag_violations_{x}", t.GetProperty("bag_violations_" + x).GetInt32(), s.BagViolations[n], err: true);
                Eq($"VL {vl} missing_{x}", t.GetProperty("missing_" + x).GetInt32(), s.Missing[n], err: true);
            }
            Eq($"VL {vl} lmax_violations", t.GetProperty("lmax_violations").GetInt32(), s.LmaxViolations, err: true); Eq($"VL {vl} accepted", t.GetProperty("accepted").GetInt32(), s.Accepted);
            if (t.GetProperty("skew_us").ValueKind == JsonValueKind.Number) { double sk = t.GetProperty("skew_us").GetDouble(); Eq($"VL {vl} skew_mean", sk, s.SkewMeanUs, 0.001); Eq($"VL {vl} skew_max", sk, s.SkewMaxUs, 0.001); }
            var acc = a.Accepted.Where(i => cap.Vl(i) == vl).ToList();
            foreach (var d in t.GetProperty("decode").EnumerateArray())
            {
                int idx = d.GetProperty("index").GetInt32(); if (idx >= acc.Count) { total++; diffs.Add($"VL {vl} decode #{idx}"); continue; }
                var dec = a.Decode(acc[idx]).ToDictionary(x => x.name, x => x.eu);
                foreach (var pv in d.GetProperty("values").EnumerateObject()) Eq($"VL {vl} decode #{idx} {pv.Name}", pv.Value.GetDouble(), dec.GetValueOrDefault(pv.Name, double.NaN), 1e-6);
            }
        }
        return (ok, total, diffs, eok, etot);
    }
}
