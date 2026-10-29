using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using NexysBus.App.Services;
using NexysView.Core.Bus;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>Command-line automation on the web UI (real DOM events through window.nx.test.*, awaited through the bridge):
/// --bus (the user path: new project → ICD import → frame edit → virtual run → ANALYZE → export), --v100rc (ANALYZE + ICD gates
/// moved from the viewer), --v0100 (1M-record performance gates), --stress --iter N, --shot. Captures are WebView2 previews.</summary>
public static partial class SelfTest
{
    public static string[] Args = Array.Empty<string>();
    public static bool Enabled => Args.Length > 0 && Args[0] is "--bus" or "--v100rc" or "--v0100" or "--stress" or "--shot" or "--uishots" or "--uishots2";
    static readonly List<string> Log = new();
    public static string OutDir = "";
    static readonly List<string> Gate = new();
    static readonly Random Rng = new(7);

    public static void L(string s) { lock (Log) Log.Add($"{DateTime.Now:HH:mm:ss.fff} {s}"); Console.WriteLine(s); }
    static void G(string k, double v, string unit, string? gate = null) { var s = $"{k}\t{v:F3} {unit}" + (gate != null ? $"\tgate {gate}" : ""); Gate.Add(s); L(s); }
    static void GB(string k, bool ok, string detail) { var s = $"{k}\t{(ok ? "PASS" : "FAIL")}\t{detail}"; Gate.Add(s); L(s); }
    static string Arg(string name, string dflt = "") { int i = Array.IndexOf(Args, name); return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : dflt; }
    static string J(string s) => JsonSerializer.Serialize(s);
    static long WsMb() => Process.GetCurrentProcess().WorkingSet64 >> 20;

    public static async Task RunAsync(MainWindow w)
    {
        try
        {
            OutDir = Arg("--out", Path.Combine(Directory.GetCurrentDirectory(), "selftest"));
            Directory.CreateDirectory(OutDir);
            await Idle(w, 300);
            w.Width += 1440 - w.Web.ActualWidth; w.Height += 900 - w.Web.ActualHeight;   // page = 1440×900 CSS px
            await Idle(w, 300);
            L($"page size {w.Web.ActualWidth}x{w.Web.ActualHeight} dpr {VisualTreeHelper.GetDpi(w).DpiScaleX}");
            if (Args[0] == "--bus") await BusPath(w);
            else if (Args[0] == "--v100rc") await V100(w);
            else if (Args[0] == "--v0100") await V0100(w);
            else if (Args[0] == "--stress") await Stress(w);
            else if (Args[0] == "--uishots") await UiShots(w);
            else if (Args[0] == "--uishots2") await UiShots2(w);
            else if (Args[0] == "--shot") { await JS(w, $"await nx.test.rail({J(Arg("--rail", "START"))}); return true;"); await Shot(w, Arg("--name", "shot")); }
        }
        catch (Exception ex) { L("FATAL " + ex); }
        finally
        {
            File.WriteAllLines(Path.Combine(OutDir, "selftest.log"), Log);
            if (Gate.Count > 0) File.WriteAllLines(Path.Combine(OutDir, "gates.txt"), Gate);
            L($"DONE fail={Gate.Count(s => s.Contains("\tFAIL"))}");
            await Task.Delay(200);
            Application.Current.Shutdown(0);
        }
    }

    static async Task Idle(MainWindow w, int ms = 50) { await w.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle); await Task.Delay(ms); }
    static Task<JsonElement> JS(MainWindow w, string body, int timeoutMs = 60000) => w.Bridge!.RunTest(body, timeoutMs);
    static async Task Shot(MainWindow w, string name) { await JS(w, "await nx.test.settle(); return true;"); await Idle(w, 150); await w.CaptureAsync(Path.Combine(OutDir, name + ".png")); L($"shot {name}.png"); }
    static string RepoRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent; return d?.FullName ?? Directory.GetCurrentDirectory(); }

    // ================================================================== --bus: the real user path
    static async Task BusPath(MainWindow w)
    {
        var e = w.Engine; var sw = Stopwatch.StartNew(); string dir = Path.Combine(OutDir, "data"); Directory.CreateDirectory(dir);
        G("memory_ws_start_mb", WsMb(), "MB");
        G("startup_process_to_ui_ms", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds - 600, "ms", "<=3000");
        // ---- 1) start screen: NEW PROJECT / OPEN / recent / hardware search (disabled)
        int startBtns = (await JS(w, "await nx.test.rail('START'); return document.querySelectorAll('.start [data-a]').length;")).GetInt32();
        bool hwDisabled = (await JS(w, "const b = document.querySelector('.start [data-a=searchHw]'); return !!b && b.disabled;")).GetBoolean();
        int rails = (await JS(w, "return document.querySelectorAll('.rail [data-rail]').length;")).GetInt32();
        GB("start_screen", startBtns >= 3 && hwDisabled && rails == 5, $"buttons={startBtns} hardwareSearchDisabled={hwDisabled} rails={rails}");
        await Shot(w, "bus_01_start");
        // ---- 2) new project → PROJECT rail: device list (virtual available), bus settings inputs
        await JS(w, "await nx.test.clickSel('.start [data-a=newProject]'); return true;"); await Idle(w, 200);
        int devs = (await JS(w, "return document.querySelectorAll('.project [data-dev]').length;")).GetInt32();
        string rail = (await JS(w, "return nx.state.rail;")).GetString() ?? "";
        await JS(w, "await nx.test.project().setField('name', 'SelfTest project'); await nx.test.project().setField('minorFrameMs', '10'); return true;");
        GB("project_new", rail == "PROJECT" && devs >= 1 && e.Project.Name == "SelfTest project" && e.Project.Bus.MinorFrameMs == 10 && e.Project.Device == "virtual", $"rail={rail} devices={devs} name='{e.Project.Name}' minor={e.Project.Bus.MinorFrameMs} device={e.Project.Device}");
        await Shot(w, "bus_02_project");
        // ---- 3) ICD rail: import the dummy CSV through the import dialog (column mapping) → project takes the ICD
        string hubCsv = Path.Combine(dir, "dummy_icd.csv"); File.WriteAllText(hubCsv, IcdHub.Dummy().ToCsv());
        await JS(w, "await nx.test.icd().open('import'); return true;"); await Idle(w, 150);
        await JS(w, $"await nx.test.icd().pick({J(hubCsv)}); return true;"); await Idle(w, 200);
        var ir = JsonDocument.Parse((await JS(w, "return JSON.stringify(await nx.test.icd().importGo());")).GetString()!).RootElement;
        GB("icd_import_to_project", ir.GetProperty("messages").GetInt32() == 10 && e.Project.Icd.Messages.Count == 10 && e.Project.Schedule.Count == 10 && e.Project.Sources.Count == 10, $"imported={ir.GetProperty("messages")} project messages={e.Project.Icd.Messages.Count} schedule={e.Project.Schedule.Count} sources={e.Project.Sources.Count}");
        await Shot(w, "bus_03_icd");
        // ---- 4) SIMULATE: frame table (10 rows), minor-frame visualisation, edit a period, move a row, set an RT source and error injection
        await JS(w, "await nx.test.rail('SIMULATE'); return true;"); await Idle(w, 200);
        int rows = (await JS(w, "return document.querySelectorAll('.simulate .frame-tbl tr[data-m]').length;")).GetInt32();
        int lanes = (await JS(w, "return document.querySelectorAll('.simulate .frame-vis .lane').length;")).GetInt32();
        await JS(w, "await nx.test.sim().setCell('NAV_POS', 'periodMs', '20'); await nx.test.sim().move('TIME_SYNC', -1); await nx.test.sim().setSource('NAV_POS', 'kind', 'eu'); await nx.test.sim().setSource('NAV_POS', 'periodS', '2'); await nx.test.sim().setError('noResponse', '50'); await nx.test.sim().setError('parity', '0'); await nx.test.sim().setError('wordCount', '0'); return true;");
        var ts = e.Project.Schedule.First(s => s.Message == "TIME_SYNC"); var np = e.Project.Schedule.First(s => s.Message == "NAV_POS");
        GB("simulate_frame_edit", rows == 10 && lanes >= 10 && np.PeriodMs == 20 && ts.Order == 8 && e.Project.Sources["NAV_POS"].Kind == "eu" && e.Project.Errors.NoResponseEvery == 50, $"rows={rows} lanes={lanes} NAV_POS period={np.PeriodMs} TIME_SYNC order={ts.Order} source={e.Project.Sources["NAV_POS"].Kind} noResponseEvery={e.Project.Errors.NoResponseEvery}");
        await Shot(w, "bus_04_simulate_edit");
        // ---- 5) run the frame for exactly 10 s of bus time (virtual clock): message count = Σ ⌈10 s / period⌉, bus load = statistics panel
        var run = JsonDocument.Parse((await JS(w, "return JSON.stringify(await nx.test.sim().runFor(10));")).GetString()!).RootElement;
        long expected = run.GetProperty("expected").GetInt64(), got = run.GetProperty("messages").GetInt64(); double loadDev = run.GetProperty("loadPct").GetDouble();
        // the expectation computed independently of the device: Σ ceil(10 s / effective period) over the 10 messages
        var cfg = e.Project.ToConfig(); double minor = cfg.MinorFrameMs > 0 ? cfg.MinorFrameMs : cfg.Messages.Min(m => m.PeriodMs);
        long independent = cfg.Messages.Where(m => m.Enabled).Sum(m => (long)Math.Ceiling(10_000.0 / (Math.Max(1, Math.Round(m.PeriodMs / minor)) * minor) - 1e-9));
        GB("sim_run_message_count", got == expected && expected == independent && got > 0, $"messages={got} expected(device)={expected} expected(independent)={independent} minor={run.GetProperty("minorMs")} ms major={run.GetProperty("majorMs")} ms");
        string capture = run.GetProperty("capture").GetString() ?? ""; var df = e.State.Files.FirstOrDefault(f => f.Path == capture); var cap = df != null ? Mil1553DataSource.FileOf(df) : null;
        var st = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.Stats())).RootElement; double loadStats = st.GetProperty("busLoadPct").GetDouble();
        GB("sim_bus_load_matches_stats", cap != null && cap.Count == got && Math.Abs(loadDev - loadStats) < 0.01 && st.GetProperty("count").GetInt64() == got, $"device load={loadDev:F3} % stats load={loadStats:F3} % records={cap?.Count} stats count={st.GetProperty("count")}");
        double expLoad = (await JS(w, "return nx.test.sim().expectedLoad();")).GetDouble();
        GB("ui2_8_expected_load_matches_run", expLoad > 0 && Math.Abs(expLoad - loadDev) <= 0.2, $"frame-table expected={expLoad:F3} % run measured={loadDev:F3} % (|Δ| ≤ 0.2 %p; no-response messages carry no data words)");
        GB("sim_capture_file_written", File.Exists(capture) && new FileInfo(capture).Length == cap!.Metadata.Length + 8 + (long)cap.Count * Mil1553File.RecordSize, $"{capture} {new FileInfo(capture).Length} B");
        // ---- 6) live run through the START/STOP buttons (wall clock, 1.2 s) → ANALYZE list grows while running
        await JS(w, "await nx.test.clickSel('.simulate [data-a=start]'); return true;"); await Idle(w, 600);
        long live1 = e.Sim.Running ? (e.State.Files.LastOrDefault(f => f.Format == "MIL1553") is { } lf ? Mil1553DataSource.FileOf(lf)!.Count : 0) : -1;
        await JS(w, "await nx.test.rail('ANALYZE'); return true;"); await Idle(w, 600);
        int liveTotal = (await JS(w, "return nx.test.m1553().total();")).GetInt32();
        await Shot(w, "bus_05_running_analyze");
        await JS(w, "await nx.test.rail('SIMULATE'); await nx.test.clickSel('.simulate [data-a=stop]'); return true;"); await Idle(w, 300);
        GB("sim_live_run", live1 > 0 && liveTotal > live1 && !e.Sim.Running && File.Exists(e.Sim.LastCapture), $"after 0.6 s={live1} after 1.2 s (ANALYZE total)={liveTotal} running={e.Sim.Running} capture={Path.GetFileName(e.Sim.LastCapture)}");
        await Shot(w, "bus_06_simulate_stopped");
        // ---- 7) ANALYZE the deterministic capture: decode EU = input waveform (NAV_POS kind eu, sine of the word's range), errors-only = injected count
        e.Mil1553.SelectFile(df!.Name); await JS(w, "await nx.test.rail('ANALYZE'); await nx.test.settle(); return true;"); await Idle(w, 200);
        var hub = e.Project.Icd; var navPos = hub.Messages.First(m => m.Name == "NAV_POS"); var idx = cap!.Filter(navPos.Rt, navPos.Sa, navPos.Tr);
        double maxErr = 0; int checkedWords = 0; var src = e.Project.Sources["NAV_POS"];
        foreach (int i in idx.Take(200))
        {
            double t = (cap.TimeUs[i] - cap.TimeUs[0]) * 1e-6;   // the device's record times start at the frame start → t of the device clock
            foreach (var wd in navPos.Words)
            {
                var bw = BusWord.FromIcd(wd); double want = src.Eu(t, bw); long raw = bw.Encode(want); double quant = raw * wd.Scale + wd.Offset;
                double got2 = wd.Decode(cap, i); if (double.IsNaN(got2)) continue; maxErr = Math.Max(maxErr, Math.Abs(got2 - quant)); checkedWords++;
            }
        }
        GB("analyze_decode_eu_matches_waveform", checkedWords >= 100 && maxErr < 1e-9, $"words checked={checkedWords} max |decoded − quantised input|={maxErr:E2} (quantisation = scale/2)");
        int nErr = (await JS(w, "return (await nx.test.m1553().filter({rt: -1, sa: -1, tr: -1, bus: -1, errorsOnly: true, text: ''})).n;")).GetInt32();
        long injected = got / 50;   // every 50th message gets "no response"
        GB("analyze_errors_only_equals_injected", nErr == injected, $"ERRORS ONLY rows={nErr} injected(no response)={injected} of {got}");
        await JS(w, "await nx.test.m1553().scrollTo(0); const r = document.querySelector('.m1553-scroll .trow[data-i]'); await nx.test.m1553().selectRow(+r.dataset.row); return true;");
        await Shot(w, "bus_07_analyze_errors");
        await JS(w, "return JSON.stringify(await nx.test.m1553().filter({errorsOnly: false}));");
        // ---- 8) project save → close → reopen (round trip), simulator project export (template) → import → same shape
        string nxb = Path.Combine(dir, "selftest.nxb"); e.SaveProject(nxb); string json1 = e.Project.ToJson();
        await JS(w, "await nx.call('project.close'); await nx.test.settle(); return true;"); await Idle(w, 150);
        bool startAgain = (await JS(w, "return nx.state.rail === 'START' && document.querySelectorAll('.start .recent [data-path]').length >= 1;")).GetBoolean();
        await JS(w, "await nx.test.clickSel('.start .recent [data-path]'); return true;"); await Idle(w, 200);
        GB("project_save_reopen", startAgain && e.Project.Name == "SelfTest project" && e.Project.ToJson() == json1 && e.Project.Errors.NoResponseEvery == 50, $"recent shown={startAgain} name='{e.Project.Name}' identical json={e.Project.ToJson() == json1}");
        string tdir = Arg("--template"); bool tmpl = tdir.Length > 0 && Directory.Exists(tdir);
        if (tmpl)
        {
            e.IcdTemplateDir = tdir; string outDir = Path.Combine(OutDir, "export", "SelfTestProject"); if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            var ex = JsonDocument.Parse(JsonSerializer.Serialize(e.ExportSimulator(outDir))).RootElement; bool ok = ex.GetProperty("project").GetProperty("ok").GetBoolean();
            var back = BusProject.ImportSimulatorProject(Directory.EnumerateFiles(outDir, "*.CPJ").First());
            GB("project_export_import_roundtrip", ok && IcdHub.SameShape(e.Project.Icd, back, true) && back.Messages.Count == 10, $"export ok={ok} imported messages={back.Messages.Count} same shape (bit fields · type · scale; the project file carries no offset/unit)={IcdHub.SameShape(e.Project.Icd, back, true)}");
            // import the template itself (the hardware-day starting point): messages appear in SIMULATE
            var imp = JsonDocument.Parse(JsonSerializer.Serialize(e.ImportSimulator(Directory.EnumerateFiles(tdir, "*.CPJ").First()))).RootElement;
            int simRows = (await JS(w, "await nx.test.rail('SIMULATE'); await nx.test.settle(); return document.querySelectorAll('.simulate .frame-tbl tr[data-m]').length;")).GetInt32();
            GB("project_import_template", imp.GetProperty("messages").GetInt32() >= 1 && simRows == imp.GetProperty("messages").GetInt32(), $"template messages={imp.GetProperty("messages")} words={imp.GetProperty("words")} SIMULATE rows={simRows}");
            e.OpenProject(nxb);
        }
        else L("project_export_import_roundtrip / project_import_template skipped (pass --template <folder>)");
        // ---- 9) settings / help / about / dark captures / accent scan / memory
        await JS(w, "await nx.test.rail('SETTINGS'); return true;"); await Idle(w, 150);
        int secs = (await JS(w, "return document.querySelectorAll('.settings .sec').length;")).GetInt32();
        await Shot(w, "bus_08_settings");
        bool helpOk = (await JS(w, "await nx.test.help('simulate'); return document.querySelector('.help-pane').textContent.includes('SIMULATE');")).GetBoolean();
        int helpSections = (await JS(w, "return document.querySelectorAll('.help-nav [data-s]').length;")).GetInt32();
        string helpText = (await JS(w, "return document.querySelector('.modal-bg')?.textContent ?? '';")).GetString() ?? "";
        await Shot(w, "bus_09_help");
        await JS(w, "document.querySelector('.modal-bg')?.remove(); await nx.test.clickSel('[data-a=about]'); return true;"); await Idle(w, 150);
        string about = (await JS(w, "return document.querySelector('.modal.about')?.textContent ?? '';")).GetString() ?? "";
        await Shot(w, "bus_10_about"); await JS(w, "document.querySelector('.modal-bg')?.remove(); return true;");
        var forbidden = Arg("--forbid").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = forbidden.Where(f => about.Contains(f, StringComparison.OrdinalIgnoreCase) || helpText.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        GB("settings_help_about", secs == 4 && helpOk && helpSections == 7 && about.Contains(AppInfo.VersionText) && hits.Count == 0, $"settings sections={secs} help simulate={helpOk} sections={helpSections} about version={about.Contains(AppInfo.VersionText)} third-party hits={hits.Count}");
        e.Theme = "dark"; e.Touch(); await JS(w, "await nx.test.rail('SIMULATE'); await nx.test.settle(); return true;"); await Idle(w, 200); await Shot(w, "bus_11_dark_simulate");
        await JS(w, "await nx.test.rail('ANALYZE'); await nx.test.settle(); return true;"); await Idle(w, 200); await Shot(w, "bus_12_dark_analyze");
        var scan = await JS(w, "return JSON.stringify(nx.test.accentScan());");
        using (var jd = JsonDocument.Parse(scan.GetString()!)) { int ah = jd.RootElement.GetProperty("hits").GetArrayLength(); G("accent_borders_zero", ah, "", "=0"); if (ah > 0) L("accent hits: " + jd.RootElement.GetProperty("hits")); }
        e.Theme = "light"; e.Touch(); await JS(w, "await nx.test.rail('START'); await nx.test.settle(); return true;");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Idle(w, 200);
        G("memory_ws_end_mb", WsMb(), "MB", "<=600");
        G("bus_total_s", sw.Elapsed.TotalSeconds, "s");
    }

    // ================================================================== --stress: rail switching, run/stop cycles, capture open/close
    static async Task Stress(MainWindow w)
    {
        var e = w.Engine; int iters = int.Parse(Arg("--iter", "50")); var sw = Stopwatch.StartNew();
        Diag.StallThresholdMs = 300;
        e.NewProject("Stress"); e.Mil1553.LoadDummyIcd(); string dir = Path.Combine(OutDir, "data"); Directory.CreateDirectory(dir);
        string mon = Path.Combine(dir, "stress_10k.mon"); if (!File.Exists(mon)) Mil1553File.WriteVirtual(mon, 10_000, 3);
        await JS(w, "await nx.test.settle(); return true;"); Diag.ResetStats(); GC.Collect(); long ws0 = WsMb(); int exceptions = 0, runs = 0, opens = 0;
        for (int it = 0; it < iters; it++)
        {
            try
            {
                foreach (var r in new[] { "PROJECT", "SIMULATE", "ICD", "SETTINGS", "ANALYZE" }) { await JS(w, $"await nx.test.rail('{r}'); return true;"); await Idle(w, 30 + Rng.Next(60)); }
                var run = JsonDocument.Parse((await JS(w, "return JSON.stringify(await nx.test.sim().runFor(0.5));")).GetString()!).RootElement; runs++;
                if (run.GetProperty("messages").GetInt64() != run.GetProperty("expected").GetInt64()) { exceptions++; L($"iter {it}: run count mismatch {run}"); }
                var f = e.OpenFile(mon)!; opens++; await JS(w, "await nx.test.rail('ANALYZE'); await nx.test.settle(); return true;");
                await JS(w, "await nx.test.m1553().scrollTo(5000); const r = document.querySelector('.m1553-scroll .trow[data-i]'); if (r) await nx.test.m1553().selectRow(+r.dataset.row); return true;");
                e.CloseFile(f.Name);
                foreach (var lf in e.State.Files.Where(x => x.Format == "MIL1553").ToList()) e.CloseFile(lf.Name);   // run captures
            }
            catch (Exception ex) { exceptions++; L($"iter {it}: {ex.GetType().Name}: {ex.Message}"); }
            if (it % 10 == 9) L($"iter {it + 1}: stalls={Diag.StallCount} maxStall={Diag.MaxStallMs:F0} ms ws={WsMb()} MB files={e.State.Files.Count}");
        }
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true); GC.WaitForPendingFinalizers(); await Task.Delay(300);
        G("stress_iterations", iters, ""); G("stress_runs", runs, ""); G("stress_capture_opens", opens, "");
        G("stress_exceptions", exceptions, "", "=0"); G("stress_ui_stalls_300ms", Diag.StallCount, "", "=0"); G("stress_max_ui_stall_ms", Diag.MaxStallMs, "ms", "<=200");
        G("stress_mem_growth_mb", WsMb() - ws0, "MB", "<=100"); G("stress_files_left", e.State.Files.Count, "", "=0"); G("stress_total_s", sw.Elapsed.TotalSeconds, "s");
        await Shot(w, "stress_end");
    }
}
