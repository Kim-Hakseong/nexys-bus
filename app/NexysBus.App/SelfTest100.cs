using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NexysBus.App.Services;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>--v100rc: 1.0 release-candidate gates on the BUS mode + ICD module + migrated .dxd reader (all through the real web UI):
/// empty state → built-in 1553 sample → sort / keyboard / time units / filters / presets / text → stats → CSV exports → ICD import
/// (CSV mapping, optional XLSX) → edit + validation → export (project / message XML / device XML / api) with XSD + round trips →
/// ARINC 429 sample (open, decode, filter, stats) → .dxd open → settings / help / about dialogs → accent scan, memory, timing.
/// Moved from the viewer in RUN3 (the BUS mode became NexysBus ANALYZE, the ICD dialog the ICD rail).</summary>
public static partial class SelfTest
{
    static async Task V100(MainWindow w)
    {
        var e = w.Engine; var sw = Stopwatch.StartNew();
        e.Theme = "light"; e.Plot = "dark"; e.BusTime = "rel"; e.Mil1553.Presets.Clear(); e.NewProject("v100rc"); e.Touch();
        string dir = Path.Combine(OutDir, "data"); Directory.CreateDirectory(dir);
        G("memory_ws_start_mb", WsMb(), "MB");
        G("startup_process_to_ui_ms", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds - 600, "ms", "<=3000");   // process start → page ready (minus the 2×300 ms self-test settle waits)
        // ---- 1) empty state (no bus file open): BUS mode shows the empty card with OPEN / 1553 SAMPLE / 429 SAMPLE
        await JS(w, "await nx.test.rail('ANALYZE'); return true;"); await Idle(w, 200);
        bool empty = (await JS(w, "return nx.test.m1553().empty();")).GetBoolean();
        int emptyBtns = (await JS(w, "return document.querySelectorAll('.bus-empty [data-a]').length;")).GetInt32();
        GB("bus_empty_state", empty && emptyBtns == 3, $"empty={empty} buttons={emptyBtns}");
        await Shot(w, "v100_01_empty");
        // ---- 2) built-in 1553 sample (20,000 records) through the empty-state button
        var t0 = Stopwatch.StartNew();
        await JS(w, "await nx.test.clickSel('.bus-empty [data-a=sample1553]'); return true;"); await Idle(w, 300);
        int total = (await JS(w, "return nx.test.m1553().total();")).GetInt32(); string proto = (await JS(w, "return nx.test.m1553().protocol();")).GetString() ?? "";
        G("bus_sample_open_ms", t0.Elapsed.TotalMilliseconds, "ms", "<=1500");
        GB("bus_sample_1553", total == 20_000 && proto == "1553" && !(await JS(w, "return nx.test.m1553().empty();")).GetBoolean(), $"total={total} protocol={proto}");
        string modes = (await JS(w, "return Array.from(document.querySelectorAll('.rail [data-rail]')).map(b => b.dataset.rail).join(',');")).ToString();
        GB("bus_mode_button_label", modes.Contains("ANALYZE") && (await JS(w, "return nx.state.rail;")).GetString() == "ANALYZE", modes);
        await Shot(w, "v100_02_list");
        // ---- 3) sort by RT descending (header click path = m1553.sort) → first 50 rows monotone; back to capture order
        int visAfterSort = (await JS(w, "return await nx.test.m1553().sort('rt', -1);")).GetInt32();
        var rowsJson = JsonSerializer.Serialize(e.Mil1553.Rows(0, 50)); int[] rts;
        using (var jd = JsonDocument.Parse(rowsJson)) rts = jd.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("rt").GetInt32()).ToArray();
        bool mono = rts.Length == 50 && rts.Zip(rts.Skip(1)).All(p => p.First >= p.Second);
        string hdr = (await JS(w, "return document.querySelector('.m1553-scroll .thead span.on')?.textContent ?? '';")).GetString() ?? "";
        GB("bus_sort_rt_desc", mono && hdr.StartsWith("RT") && visAfterSort > 5, $"first rts={string.Join(',', rts.Take(8))} header='{hdr}'");
        await JS(w, "return await nx.test.m1553().sort('', 1);");
        // ---- 4) keyboard navigation on the list (ArrowDown ×3, PageDown, End, Home)
        await JS(w, "await nx.test.m1553().scrollTo(0); const r = document.querySelector('.m1553-scroll .trow[data-i]'); await nx.test.m1553().selectRow(+r.dataset.row); return true;");
        int r1 = (await JS(w, "await nx.test.m1553().key('ArrowDown'); await nx.test.m1553().key('ArrowDown'); return await nx.test.m1553().key('ArrowDown');")).GetInt32();
        int r2 = (await JS(w, "return await nx.test.m1553().key('PageDown');")).GetInt32();
        int r3 = (await JS(w, "return await nx.test.m1553().key('End');")).GetInt32();
        int r4 = (await JS(w, "return await nx.test.m1553().key('Home');")).GetInt32();
        GB("bus_keyboard_nav", r1 == 3 && r2 > r1 && r3 == total - 1 && r4 == 0, $"down3={r1} pgdn={r2} end={r3} home={r4}");
        // ---- 5) time units: REL s / ABS µs / Δt ms (toolbar pills → m1553.busTime → header + cells)
        await JS(w, "await nx.test.m1553().clickFilter('[data-time=abs]'); await nx.test.settle(); return true;"); await Idle(w, 150);
        string hAbs = (await JS(w, "return document.querySelector('.m1553-scroll .thead').textContent;")).GetString() ?? "";
        string cAbs = (await JS(w, "return document.querySelector('.m1553-scroll .trow[data-i] span:nth-child(2)')?.textContent ?? '';")).GetString() ?? "";
        await JS(w, "await nx.test.m1553().clickFilter('[data-time=delta]'); await nx.test.settle(); return true;"); await Idle(w, 150);
        string hDel = (await JS(w, "return document.querySelector('.m1553-scroll .thead').textContent;")).GetString() ?? "";
        await JS(w, "await nx.test.m1553().clickFilter('[data-time=rel]'); await nx.test.settle(); return true;"); await Idle(w, 150);
        string hRel = (await JS(w, "return document.querySelector('.m1553-scroll .thead').textContent;")).GetString() ?? "";
        GB("bus_time_units", hAbs.Contains("TIME (µs)") && hDel.Contains("Δt (ms)") && hRel.Contains("TIME (s)") && e.BusTime == "rel" && cAbs.Replace(",", "").All(char.IsDigit) && cAbs.Length > 6, $"abs='{cAbs}' heads: {hAbs[..Math.Min(40, hAbs.Length)]} | {hDel[..Math.Min(30, hDel.Length)]}");
        // ---- 6) filters: RT input, T/R pill, bus pill, ERRORS ONLY, TEXT; presets save / apply / delete
        int nRt3 = (await JS(w, "return await nx.test.m1553().setInput('rt', '3');")).GetInt32();
        int nTr = (await JS(w, "await nx.test.m1553().clickFilter('[data-tr=\"1\"]'); return nx.test.m1553().total();")).GetInt32();
        int nBusB = (await JS(w, "await nx.test.m1553().clickFilter('[data-bus=\"1\"]'); return nx.test.m1553().total();")).GetInt32();
        int nErr = (await JS(w, "await nx.test.m1553().clickFilter('[data-a=err]'); return nx.test.m1553().total();")).GetInt32();
        GB("bus_filter_combo", nRt3 > 0 && nRt3 < total && nTr <= nRt3 && nBusB <= nTr && nErr <= nBusB, $"rt3={nRt3} +rt→bc={nTr} +busB={nBusB} +errors={nErr} of {total}");
        await Shot(w, "v100_03_filters");
        var fr = await JS(w, "return JSON.stringify(await nx.test.m1553().filter({rt: -1, sa: -1, tr: -1, bus: -1, errorsOnly: false, text: ''}));");
        int nAll; using (var jd = JsonDocument.Parse(fr.GetString()!)) { nAll = jd.RootElement.GetProperty("n").GetInt32(); G("bus_filter_reset_ms", jd.RootElement.GetProperty("ms").GetDouble(), "ms", "<=100"); }
        int nText = (await JS(w, "return await nx.test.m1553().setInput('text', 'RT3');")).GetInt32();
        int nHex = (await JS(w, "return await nx.test.m1553().setInput('text', '0001');")).GetInt32();
        GB("bus_filter_text", nAll == total && nText == nRt3 && nHex > 0 && nHex < total, $"reset={nAll} 'RT3'={nText} (rt filter gave {nRt3}) hex '0001'={nHex}");
        await JS(w, "return await nx.test.m1553().setInput('text', '');");
        await JS(w, "await nx.call('m1553.preset.save', {name: 'RT3 errors', rt: 3, sa: -1, tr: -1, bus: -1, errorsOnly: true, text: ''}); await nx.test.settle(); return true;"); await Idle(w, 150);
        int presetOpts = (await JS(w, "return document.querySelectorAll('.m1553-filters [data-f=preset] option').length;")).GetInt32();
        int nPreset = (await JS(w, "const s = document.querySelector('.m1553-filters [data-f=preset]'); s.value = 'RT3 errors'; s.dispatchEvent(new Event('change', {bubbles: true})); await new Promise(r => setTimeout(r, 250)); return nx.test.m1553().total();")).GetInt32();
        await JS(w, "await nx.test.m1553().clickFilter('[data-a=presetDel]'); await nx.test.settle(); return true;"); await Idle(w, 150);
        int presetOpts2 = (await JS(w, "return document.querySelectorAll('.m1553-filters [data-f=preset] option').length;")).GetInt32();
        GB("bus_presets", presetOpts == 2 && nPreset > 0 && nPreset < nRt3 && presetOpts2 == 1 && e.Mil1553.Presets.Count == 0, $"options={presetOpts}→{presetOpts2} applied rows={nPreset}");
        await JS(w, "return JSON.stringify(await nx.test.m1553().filter({rt: -1, errorsOnly: false}));");
        // ---- 7) select + decode (raw hex/bin/dec) and statistics card
        int firstRow = (await JS(w, "await nx.test.m1553().scrollTo(0); const r = document.querySelector('.m1553-scroll .trow[data-i]'); return +r.dataset.row;")).GetInt32();
        int kv = (await JS(w, $"return await nx.test.m1553().selectRow({firstRow});")).GetInt32();
        int rawRows = (await JS(w, "return document.querySelectorAll('.m1553-words.raw tbody tr').length;")).GetInt32();
        GB("bus_decode_raw", kv >= 7 && rawRows >= 1, $"kv={kv} raw word rows={rawRows}");
        int bars = (await JS(w, "return await nx.test.m1553().stats();")).GetInt32(); await Idle(w, 200);
        var st = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.Stats())).RootElement;
        GB("bus_stats", bars > 0 && st.GetProperty("count").GetInt32() == total && st.GetProperty("perRt").GetArrayLength() > 0 && st.GetProperty("busLoadPct").GetDouble() > 0, $"bars={bars} count={st.GetProperty("count")} rts={st.GetProperty("perRt").GetArrayLength()} load={st.GetProperty("busLoadPct").GetDouble():F1}% errors={st.GetProperty("errors")}");
        await Shot(w, "v100_04_decode_stats");
        await JS(w, "await nx.test.m1553().clickFilter('[data-a=stats]'); return true;");
        // ---- docs/ui-feedback.md gates (3 ERR / STAT, 4 ABS tooltip, 5 filter bar, 6 RT tree, 2 bits, 7 trend header, 8 dev-only DUMMY, 9 no internal ids, 10 English)
        int blank = 0, allRows = 0, nonErr = 0; using (var jd = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.Rows(0, 5000)))) foreach (var rr in jd.RootElement.GetProperty("rows").EnumerateArray()) { allRows++; if (!rr.GetProperty("e").GetBoolean()) nonErr++; if ((rr.GetProperty("err").GetString() ?? "").Length == 0) blank++; }
        int statsErrAll = st.GetProperty("errors").GetInt32();
        var cap0 = NexysView.Core.Mil1553.Mil1553DataSource.FileOf(e.State.Files.First(f => f.Format == "MIL1553"))!; int capErr = 0; for (int i = 0; i < cap0.Count; i++) if ((cap0.Error[i] & ~1) != 0) capErr++;
        int blankAll = cap0.Count - capErr;
        string headCols = (await JS(w, "return nx.test.m1553().columns().join(',');")).GetString() ?? "";
        GB("ui3_err_blank_rows", blank == nonErr && blank > 0 && blankAll == cap0.Count - statsErrAll && !headCols.Contains("STAT") && headCols.Contains("ERR"), $"blank ERR cells (first 5000)={blank} = non-error rows {nonErr}; capture: blank={blankAll} = total {cap0.Count} − stats errors {statsErrAll}; STAT hidden by default, columns={headCols}");
        await JS(w, "await nx.test.m1553().clickFilter('[data-time=abs]'); await nx.test.settle(); return true;"); await Idle(w, 150);
        string absTip = (await JS(w, "return document.querySelector('.m1553-scroll .thead [data-sort=t]')?.title ?? '';")).GetString() ?? "";
        await JS(w, "await nx.test.m1553().clickFilter('[data-time=rel]'); await nx.test.settle(); return true;"); await Idle(w, 150);
        GB("ui4_abs_time_tooltip", absTip.Contains("not verified") && e.BusTime == "rel", $"tooltip='{absTip[..Math.Min(60, absTip.Length)]}' default={e.BusTime}");
        int fbTime = (await JS(w, "return document.querySelectorAll('.m1553-filters [data-time], .m1553-filters [data-a=csvRows], .m1553-filters [data-a=stats], .m1553-filters [data-a=columns]').length;")).GetInt32();
        int fbH = (await JS(w, "return Math.round(document.querySelector('.m1553-filters').getBoundingClientRect().height);")).GetInt32();
        int hdrActs = (await JS(w, "return document.querySelectorAll('.m1553 > .card .card-h .acts [data-a=csvRows], .m1553 > .card .card-h .acts [data-a=csvDecoded], .m1553 > .card .card-h .acts [data-a=stats]').length;")).GetInt32();
        GB("ui5_filter_bar_single_row", fbTime == 0 && fbH <= 44 && hdrActs == 3 && (await JS(w, "return !!document.querySelector('.m1553-scroll .thead .hmenu');")).GetBoolean(), $"display/export controls in the filter bar={fbTime} bar height={fbH} px header actions={hdrActs} header menu present");
        var tree = JsonDocument.Parse((await JS(w, "return JSON.stringify(nx.test.m1553().tree());")).GetString()!).RootElement;
        int rtNodes = tree.GetProperty("rts").GetInt32(); int nRt = st.GetProperty("perRt").GetArrayLength();
        int treeFiltered = (await JS(w, "return await nx.test.m1553().treeClick('.m1553-tree [data-rt=\"3\"]:not([data-sa])');")).GetInt32();
        int saNodes = (await JS(w, "return nx.test.m1553().tree().sas;")).GetInt32();
        await JS(w, "await nx.test.m1553().treeClick('.m1553-tree [data-rt=\"3\"]:not([data-sa])'); return true;");
        bool rawOn = (await JS(w, "return await nx.test.m1553().rawToggle();")).GetBoolean(); int rawCh = (await JS(w, "return nx.test.m1553().tree().channels;")).GetInt32();
        await JS(w, "await nx.test.m1553().rawToggle(); return true;");
        GB("ui6_rt_tree_nodes", rtNodes == nRt && treeFiltered == nRt3 && saNodes >= 1 && rawOn && rawCh == 43, $"RT nodes={rtNodes} = RTs {nRt}; RT 3 click → {treeFiltered} rows (filter gave {nRt3}); SA nodes={saNodes}; RAW channels={rawCh}");
        await Shot(w, "v100_04b_tree_filters");
        // ---- 8) CSV exports (rows / decoded with EU) through the service path the dialog uses
        string csvRows = Path.Combine(dir, "rows.csv"), csvDec = Path.Combine(dir, "decoded.csv");
        int nr = e.Mil1553.ExportRowsCsv(csvRows, 0, 999); int nd = e.Mil1553.ExportDecodedCsv(csvDec, 0, 99);
        int lr = File.ReadAllLines(csvRows).Length, ld = File.ReadAllLines(csvDec).Length;
        GB("bus_csv_exports", nr == 1000 && lr == 1001 && nd >= 100 && ld == nd + 1, $"rows={nr}/{lr} lines, decoded (no ICD → raw words)={nd}/{ld} lines");
        // ---- 9) ICD module: settings toggle → import CSV with column mapping → edit → validate
        bool toggled = (await JS(w, "await nx.test.rail('ICD'); return nx.state.rail === 'ICD' && !!document.querySelector('.icd-page');")).GetBoolean();
        GB("icd_module_toggle", toggled, $"ICD rail page shown={toggled}");
        string hubCsv = Path.Combine(dir, "dummy_icd.csv"); File.WriteAllText(hubCsv, IcdHub.Dummy().ToCsv());
        await JS(w, "await nx.test.icd().open('import'); return true;"); await Idle(w, 150);
        await JS(w, $"await nx.test.icd().pick({J(hubCsv)}); return true;"); await Idle(w, 200);
        var map = JsonDocument.Parse((await JS(w, "return JSON.stringify(nx.test.icd().mapping());")).GetString()!).RootElement;
        int mapped = map.EnumerateObject().Count(p => p.Value.GetInt32() >= 0);
        await Shot(w, "v100_05_icd_import");
        var ir = JsonDocument.Parse((await JS(w, "return JSON.stringify(await nx.test.icd().importGo());")).GetString()!).RootElement;
        int msgs = (await JS(w, "return nx.test.icd().messages();")).GetInt32();
        GB("icd_import_csv_mapping", mapped == NexysView.Core.Mil1553.IcdHub.Columns.Length && msgs == 10 && ir.GetProperty("words").GetInt32() == 40 && ir.GetProperty("errors").GetArrayLength() == 0, $"auto-mapped columns={mapped}/{NexysView.Core.Mil1553.IcdHub.Columns.Length} messages={msgs} words={ir.GetProperty("words")} errors={ir.GetProperty("errors").GetArrayLength()}");
        string xlsx = Arg("--xlsx");
        if (xlsx.Length > 0 && File.Exists(xlsx))
        {
            var xr = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.LoadIcd(xlsx))).RootElement; var hx = e.Mil1553.Hub!; var hc = IcdHub.FromRows(IcdHub.ReadCsvRows(hubCsv), null);
            GB("icd_import_xlsx", xr.GetProperty("messages").GetInt32() == 10 && IcdHub.SameShape(hx, hc), $"xlsx messages={xr.GetProperty("messages")} words={xr.GetProperty("words")} same shape as csv={IcdHub.SameShape(hx, hc)}");
            e.Mil1553.LoadIcd(hubCsv);
        }
        else L("icd_import_xlsx skipped (pass --xlsx <file>)");
        // edit: add a message (duplicate RT/SA → warning), widen a word (bit overlap → error), then delete it again
        await JS(w, "await nx.test.icd().open('edit'); return true;"); await Idle(w, 150);
        // the edited message is message 11 (index 10); the table shows the selected message's words, so select it after adding
        var h1 = JsonDocument.Parse((await JS(w, "return JSON.stringify(await nx.test.icd().edit({op: 'addMessage'}));")).GetString()!).RootElement;
        await JS(w, "await nx.test.icd().edit({op: 'setMessage', m: 10, field: 'rt', value: 1}); await nx.test.icd().edit({op: 'setMessage', m: 10, field: 'sa', value: 1}); await nx.test.icd().edit({op: 'setMessage', m: 10, field: 'tr', value: 0}); return true;");
        var errs1 = JsonDocument.Parse((await JS(w, "return JSON.stringify(nx.test.icd().errors());")).GetString()!).RootElement.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var hub0 = e.Mil1553.Hub!; var dup = hub0.Messages.Take(10).Any(m => m.Rt == 1 && m.Sa == 1 && m.Tr == 0);
        await JS(w, "await nx.test.icd().edit({op: 'addWord', m: 10}); await nx.test.icd().edit({op: 'addWord', m: 10}); await nx.test.icd().edit({op: 'setWord', m: 10, w: 0, field: 'bit_len', value: 32}); return true;");
        var errs2 = JsonDocument.Parse((await JS(w, "return JSON.stringify(nx.test.icd().errors());")).GetString()!).RootElement.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        int cellRows = (await JS(w, "await nx.test.clickSel('.icd-tbl.edit tr[data-m=\"10\"] td'); return document.querySelectorAll('.icd-tbl.edit tr[data-w]').length;")).GetInt32();
        string errsText = string.Join(" | ", errs2.Take(3));
        await Shot(w, "v100_06_icd_edit");
        var h2 = JsonDocument.Parse((await JS(w, "return JSON.stringify(await nx.test.icd().edit({op: 'deleteMessage', m: 10}));")).GetString()!).RootElement;
        var errs3 = JsonDocument.Parse((await JS(w, "return JSON.stringify(nx.test.icd().errors());")).GetString()!).RootElement.GetArrayLength();
        GB("icd_edit_validate", h1.GetProperty("messages").GetArrayLength() == 11 && (!dup || errs1.Any(x => x.Contains("duplicate"))) && errs2.Any(x => x.StartsWith("error") && x.Contains("overlap")) && cellRows == 2 && h2.GetProperty("messages").GetArrayLength() == 10 && errs3 == 0,
            $"add→{h1.GetProperty("messages").GetArrayLength()} dupWarn={errs1.Count(x => x.Contains("duplicate"))} overlapErr={errs2.Count(x => x.Contains("overlap"))} wordRows={cellRows} delete→{h2.GetProperty("messages").GetArrayLength()} errors after={errs3} [{errsText}]");
        // cell edit through the DOM input (change event → icd.edit setWord)
        string n0 = hub0.Messages[0].Words[0].Name;
        await JS(w, "await nx.test.clickSel('.icd-tbl.edit tr[data-m=\"0\"] td'); await nx.test.icd().setCell('.icd-tbl.edit tr[data-w=\"0\"] [data-f=name]', 'RENAMED_W1'); return true;");
        GB("icd_edit_cell", e.Mil1553.Hub!.Messages[0].Words[0].Name == "RENAMED_W1", $"'{n0}' → '{e.Mil1553.Hub!.Messages[0].Words[0].Name}'");
        await JS(w, $"await nx.test.icd().setCell('.icd-tbl.edit tr[data-w=\"0\"] [data-f=name]', {J(n0)}); return true;");
        // a hub with an out-of-range RT (only reachable from a file) must block export
        string badCsv = Path.Combine(dir, "bad_icd.csv"); File.WriteAllText(badCsv, IcdHub.Dummy().ToCsv().Replace("NAV_POS,1,1,0,", "NAV_POS,40,1,0,"));
        var bad = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.LoadIcd(badCsv))).RootElement; var badPrev = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.ExportPreview())).RootElement;
        GB("icd_validate_blocks_export", bad.GetProperty("errors").EnumerateArray().Any(x => (x.GetString() ?? "").StartsWith("error") && (x.GetString() ?? "").Contains("RT 40")) && !badPrev.GetProperty("ok").GetBoolean(), $"errors={string.Join(" | ", bad.GetProperty("errors").EnumerateArray().Select(x => x.GetString()).Take(2))} preview ok={badPrev.GetProperty("ok")}");
        e.Mil1553.LoadIcd(hubCsv); await JS(w, "await nx.test.icd().open('export'); return true;"); await Idle(w, 200);
        await Shot(w, "v100_07_icd_export");
        // ---- 10) export: project (template if given) + message XML + device XML + api; XSD validation + XML round trip + hub JSON round trip + CDV node count
        string outDir = Path.Combine(OutDir, "export", "DummyIcd"); if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        string tdir = Arg("--template"); bool tmpl = tdir.Length > 0 && Directory.Exists(tdir);
        var ex = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.Export(outDir, tmpl ? tdir : null, true, new[] { "project", "xml", "device", "api" }))).RootElement;
        bool xmlOk = ex.GetProperty("xml").GetProperty("ok").GetBoolean(), devOk = ex.GetProperty("device").GetProperty("ok").GetBoolean(), rt = ex.GetProperty("xml").GetProperty("roundTrip").GetBoolean();
        int xsdErrs = ex.GetProperty("xml").GetProperty("schemaErrors").GetArrayLength() + ex.GetProperty("device").GetProperty("schemaErrors").GetArrayLength();
        GB("icd_export_xml_xsd_roundtrip", xmlOk && devOk && rt && xsdErrs == 0 && File.Exists(Path.Combine(outDir, "DummyIcd_messages.xml")) && File.Exists(Path.Combine(outDir, "DummyIcd_device.xml")), $"xml ok={xmlOk} device ok={devOk} roundTrip={rt} schema errors={xsdErrs}");
        var hubBack = IcdHub.Load(Path.Combine(outDir, "icd_hub.json")); var xmlBack = IcdXml.FromDbXml(File.ReadAllText(Path.Combine(outDir, "DummyIcd_messages.xml")));
        GB("icd_export_file_roundtrip", IcdHub.SameShape(e.Mil1553.Hub!, hubBack) && IcdHub.SameShape(e.Mil1553.Hub!, xmlBack) && hubBack.Messages[0].Words[0].Scale == 0.5, $"hub json same={IcdHub.SameShape(e.Mil1553.Hub!, hubBack)} xml file same={IcdHub.SameShape(e.Mil1553.Hub!, xmlBack)}");
        var prj = ex.GetProperty("project"); bool prjOk = prj.GetProperty("ok").GetBoolean();
        GB("icd_export_project", (tmpl && prjOk && Directory.EnumerateFiles(outDir, "*.CDV").Any()) || (!tmpl && !prjOk && prj.GetProperty("note").GetString()!.Contains("template")), $"template={tmpl} ok={prjOk} note={prj.GetProperty("note")}");
        if (tmpl && prjOk)
        {
            var cf = CompoundFile.Read(File.ReadAllBytes(Directory.EnumerateFiles(outDir, "*.CDV").First()));
            int bm = cf.Storages.Count(kv => new Guid(kv.Value).ToString() == "1553014a-7f30-4c82-ac9b-043151722f6b"), fl = cf.Storages.Count(kv => new Guid(kv.Value).ToString() == "15530653-9400-488c-af27-8e2092c854f3");
            GB("icd_export_cdv_nodes", bm == 10 && (fl == 40 || fl == 80), $"bcmsg={bm} fields={fl} (10 BC × 4 words{(fl == 80 ? " + 10 simulated SA × 4 words" : "; template has no RT/SA node → BC only")})");
        }
        GB("icd_export_api_script", File.Exists(Path.Combine(outDir, "DummyIcd_automation.py")) && !File.ReadAllText(Path.Combine(outDir, "DummyIcd_automation.py")).Contains("ProgID(\"") , $"simulatorDetected={ex.GetProperty("api").GetProperty("simulatorDetected")}");
        await JS(w, "await nx.test.rail('ANALYZE'); return true;");
        // ICD applied to the list: ICD column + EU words in decode + trend chart
        await JS(w, $"await nx.test.settle(); await nx.test.m1553().scrollTo(0); const r = document.querySelector('.m1553-scroll .trow[data-i]'); return await nx.test.m1553().selectRow(+r.dataset.row);");
        string words = (await JS(w, "return JSON.stringify(nx.test.m1553().words());")).GetString()!;
        int icdCells = (await JS(w, "return Array.from(document.querySelectorAll('.m1553-scroll .trow[data-i]')).filter(r => r.textContent.includes('NAV_') || r.textContent.includes('_W')).length;")).GetInt32();
        var tj = await JS(w, "return await nx.test.m1553().trendFirst();"); await Idle(w, 400);
        int chart = tj.ValueKind == JsonValueKind.Number ? tj.GetInt32() : -1; var tc = e.Charts.FirstOrDefault(c => c.Id == chart);
        GB("icd_applied_decode_trend", words.Contains("\"eu\"") && JsonDocument.Parse(words).RootElement.GetArrayLength() == 4 && tc != null && tc.Model.Curves.Count == 1, $"eu words={JsonDocument.Parse(words).RootElement.GetArrayLength()} icd-named rows visible={icdCells} chart={chart} curves={tc?.Model.Curves.Count}");
        var euFit = JsonDocument.Parse((await JS(w, "const dec = document.querySelector('.m1553-dec'); const dr = dec.getBoundingClientRect(); const cells = Array.from(dec.querySelectorAll('td.eu')); const over = cells.filter(c => { const r = c.getBoundingClientRect(); return r.right > dr.right - 1 || c.scrollWidth > c.clientWidth + 1; }).length; const pr = document.querySelector('.m1553-plot').getBoundingClientRect(); return JSON.stringify({ cells: cells.length, over, ratio: Math.round(100 * dr.width / (dr.width + pr.width)) });")).GetString()!).RootElement;
        GB("ui2_2_eu_column_fits", euFit.GetProperty("cells").GetInt32() >= 4 && euFit.GetProperty("over").GetInt32() == 0 && euFit.GetProperty("ratio").GetInt32() >= 52, $"EU cells={euFit.GetProperty("cells")} clipped={euFit.GetProperty("over")} MESSAGE share={euFit.GetProperty("ratio")} %");
        string bits32 = JsonDocument.Parse(words).RootElement[0].GetProperty("bits").GetString() ?? "";
        GB("ui2_bits_grouped", System.Text.RegularExpressions.Regex.IsMatch(bits32, "^[01]{4}( [01]{4}){7}$"), $"32-bit word bits='{bits32}'");
        var tt = JsonDocument.Parse((await JS(w, "return JSON.stringify(nx.test.m1553().trendTools());")).GetString()!).RootElement;
        double winAll = (await JS(w, "return await nx.test.m1553().setWindow(0);")).GetDouble(), win10 = (await JS(w, "return await nx.test.m1553().setWindow(10);")).GetDouble();
        GB("ui7_trend_header", tt.GetProperty("wins").GetInt32() == 3 && tt.GetProperty("send").GetBoolean() && !tt.GetProperty("chartHeadVisible").GetBoolean() && (tt.GetProperty("label").GetString() ?? "").Contains("NAV_") && win10 <= 10.01 && winAll > win10, $"window pills={tt.GetProperty("wins")} send={tt.GetProperty("send")} generic chart header hidden={!tt.GetProperty("chartHeadVisible").GetBoolean()} label='{tt.GetProperty("label")}' x-range all={winAll:F2} s last10={win10:F2} s");
        string sendPath = (await JS(w, "return await nx.call('trend.sendToView', { channel: nx.test.m1553().words()[0].ch });")).GetString() ?? "";
        GB("ui7_trend_send_csv", File.Exists(sendPath) && File.ReadAllLines(sendPath).Length > 100, $"csv={sendPath} lines={(File.Exists(sendPath) ? File.ReadAllLines(sendPath).Length : 0)}");
        int treeWords = (await JS(w, "return nx.test.m1553().tree().words;")).GetInt32();
        GB("ui6_rt_tree_icd_words", treeWords >= 4, $"ICD word nodes under the opened RT={treeWords}");
        int dummyBtn = (await JS(w, "await nx.test.rail('ICD'); await nx.test.settle(); return document.querySelectorAll('.icd-f [data-a=dummy]').length;")).GetInt32();
        int dummyDev = (await JS(w, "await nx.call('setDeveloper', { on: true }); await new Promise(r => setTimeout(r, 300)); const n = document.querySelectorAll('.icd-f [data-a=dummy]').length; await nx.call('setDeveloper', { on: false }); await new Promise(r => setTimeout(r, 300)); return n;")).GetInt32();
        string exportText = (await JS(w, "await nx.test.icd().open('export'); await new Promise(r => setTimeout(r, 300)); return document.querySelector('.icd-body')?.textContent ?? '';")).GetString() ?? "";
        bool footerOk = (await JS(w, "await nx.test.icd().open('import'); await new Promise(r => setTimeout(r, 200)); const f = document.querySelector('.icd-f'); const t = f.querySelector('span'); const b = f.querySelector('.pill'); return t.getBoundingClientRect().right <= b.getBoundingClientRect().left + 1;")).GetBoolean();
        GB("ui8_dev_only_dummy", dummyBtn == 0 && dummyDev == 1, $"DUMMY buttons normal={dummyBtn} developer={dummyDev}");
        GB("ui9_no_internal_ids", !exportText.Contains("PROGID") && exportText.Contains("Simulator not installed"), $"export text has PROGID={exportText.Contains("PROGID")}");
        GB("ui1_icd_footer_text_clear", footerOk, "footer text box ends before the first button");
        string hangul = (await JS(w, "const rx = /[가-힣]/; const out = []; for (const r of ['PROJECT', 'SIMULATE', 'ICD', 'SETTINGS', 'ANALYZE']) { await nx.test.rail(r); await nx.test.settle(); const vis = (el) => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length); const t = Array.from(document.querySelectorAll('.main *')).filter(vis).map(x => (x.childNodes.length ? Array.from(x.childNodes).filter(n => n.nodeType === 3).map(n => n.textContent).join('') : '') + (x.getAttribute('placeholder') || '') + (x.getAttribute('title') || '')).join(''); if (rx.test(t)) out.push(r); } return out.join(',');")).GetString() ?? "";
        GB("ui10_english_ui", hangul.Length == 0, $"rails with Korean text: '{hangul}' (help is Korean by design)");
        // ---- docs/ui-feedback-2.md gates (1 formats blocks, 2 EU fits, 3 BUS select / PROJECT buttons, 4 start button, 5 start + about English, 7 settings cards)
        var fmtTops = JsonDocument.Parse((await JS(w, "await nx.test.icd().open('export'); await new Promise(r => setTimeout(r, 300)); return JSON.stringify(Array.from(document.querySelectorAll('.icd-page .opt')).map(o => Math.round(o.getBoundingClientRect().top)));")).GetString()!).RootElement.EnumerateArray().Select(x => x.GetInt32()).ToList();
        GB("ui2_1_export_formats_blocks", fmtTops.Count == 4 && fmtTops.Distinct().Count() == 4 && fmtTops.Zip(fmtTops.Skip(1)).All(p => p.Second - p.First >= 24), $"format rows top y = {string.Join(",", fmtTops)}");
        int busSel = (await JS(w, "await nx.test.icd().open('edit'); await new Promise(r => setTimeout(r, 250)); const s = document.querySelector('.icd-tbl.edit select[data-f=bus]'); return s ? Math.round(s.getBoundingClientRect().width) : -1;")).GetInt32();
        var pbtn = JsonDocument.Parse((await JS(w, "await nx.test.rail('PROJECT'); await nx.test.settle(); const b = document.querySelector('.project [data-a=exportSim]'); const r = b.getBoundingClientRect(); const pr = b.closest('.card').getBoundingClientRect(); return JSON.stringify({ label: b.textContent.trim(), fits: b.scrollWidth <= b.clientWidth + 1 && r.right <= pr.right, w: Math.round(r.width) });")).GetString()!).RootElement;
        var busBox = JsonDocument.Parse((await JS(w, "await nx.test.icd().open('edit'); await new Promise(r => setTimeout(r, 250)); const pane = document.querySelector('.icd-page .icd-pane.msgs'); const th = Array.from(pane.querySelectorAll('.icd-tbl.edit thead th')).find(x => x.textContent.trim() === 'BUS'); const cells = Array.from(pane.querySelectorAll('.icd-tbl.edit tbody select[data-f=bus]')); const pr = pane.getBoundingClientRect(); const inside = (el) => { const r = el.getBoundingClientRect(); return r.right <= pr.right + 0.5 && r.left >= pr.left - 0.5; }; return JSON.stringify({ th: th ? inside(th) : false, cells: cells.length, cellsIn: cells.filter(inside).length, paneW: Math.round(pr.width), scrollW: pane.querySelector('.scroll').scrollWidth, clientW: pane.querySelector('.scroll').clientWidth });")).GetString()!).RootElement;
        GB("u5_icd_bus_column_in_pane", busBox.GetProperty("th").GetBoolean() && busBox.GetProperty("cells").GetInt32() > 0 && busBox.GetProperty("cellsIn").GetInt32() == busBox.GetProperty("cells").GetInt32() && busBox.GetProperty("scrollW").GetInt32() <= busBox.GetProperty("clientW").GetInt32(), $"BUS header inside pane={busBox.GetProperty("th")} cells {busBox.GetProperty("cellsIn")}/{busBox.GetProperty("cells")} inside, pane {busBox.GetProperty("paneW")} px, table scrollWidth {busBox.GetProperty("scrollW")} ≤ clientWidth {busBox.GetProperty("clientW")}");
        GB("ui2_3_bus_column_project_buttons", busSel >= 44 && pbtn.GetProperty("label").GetString() == "EXPORT PROJECT…" && pbtn.GetProperty("fits").GetBoolean(), $"BUS select width={busSel} px, export button '{pbtn.GetProperty("label")}' width={pbtn.GetProperty("w")} fits={pbtn.GetProperty("fits")}");
        var sb = JsonDocument.Parse((await JS(w, "await nx.test.rail('START'); await nx.test.settle(); const b = document.querySelector('.start [data-a=newProject]'); const cs = getComputedStyle(b); return JSON.stringify({ text: b.textContent.trim(), color: cs.color, bg: cs.backgroundColor });")).GetString()!).RootElement;
        GB("ui2_4_start_button_label", sb.GetProperty("text").GetString() == "NEW PROJECT" && sb.GetProperty("color").GetString() != sb.GetProperty("bg").GetString(), $"text='{sb.GetProperty("text")}' color={sb.GetProperty("color")} bg={sb.GetProperty("bg")}");
        string hangul2 = (await JS(w, "const rx = /[가-힣]/; const vis = (el) => !!(el.offsetWidth || el.offsetHeight || el.getClientRects().length); const txt = (root) => Array.from(root.querySelectorAll('*')).filter(vis).map(x => Array.from(x.childNodes).filter(n => n.nodeType === 3).map(n => n.textContent).join('') + (x.getAttribute('placeholder') || '') + (x.getAttribute('title') || '')).join(''); const out = []; if (rx.test(txt(document.querySelector('.main')))) out.push('START'); await nx.test.clickSel('[data-a=about]'); await new Promise(r => setTimeout(r, 150)); if (rx.test(txt(document.querySelector('.modal.about')))) out.push('ABOUT'); document.querySelector('.modal-bg')?.remove(); if (rx.test(txt(document.querySelector('.hdr')) + txt(document.querySelector('.rail')) + txt(document.querySelector('.ftr')))) out.push('CHROME'); return out.join(',');")).GetString() ?? "";
        GB("ui2_5_english_start_about", hangul2.Length == 0, $"Korean in: '{hangul2}' (start screen, about dialog, header / rail / footer)");
        var sc = JsonDocument.Parse((await JS(w, "await nx.test.rail('SETTINGS'); await nx.test.settle(); const cards = Array.from(document.querySelectorAll('.settings .card.sec')); return JSON.stringify({ n: cards.length, xs: [...new Set(cards.map(c => Math.round(c.getBoundingClientRect().left)))].length, titles: cards.map(c => c.querySelector('.card-h .t').textContent).join(',') });")).GetString()!).RootElement;
        GB("ui2_7_settings_cards", sc.GetProperty("n").GetInt32() == 4 && sc.GetProperty("xs").GetInt32() == 2 && sc.GetProperty("titles").GetString() == "APPEARANCE,ANALYZE,PROJECT,PROFILE", $"cards={sc.GetProperty("n")} columns={sc.GetProperty("xs")} titles={sc.GetProperty("titles")}");
        await JS(w, "await nx.test.rail('ANALYZE'); await nx.test.settle(); return true;");
        await Shot(w, "v100_08_icd_trend");
        // ---- 11) ARINC 429 sample: open via the empty-state path (bridge), decode, filter by label / parity, stats
        string p429 = SampleBus.Ensure429()!; var df429 = e.OpenFile(p429)!; e.Mil1553.SelectFile(df429.Name); await JS(w, "await nx.test.settle(); return true;"); await Idle(w, 250);
        int t429 = (await JS(w, "return nx.test.m1553().total();")).GetInt32(); string pr429 = (await JS(w, "return nx.test.m1553().protocol();")).GetString() ?? "";
        string cols429 = (await JS(w, "return nx.test.m1553().columns().join(',');")).GetString() ?? "";
        GB("a429_open", t429 == 2000 && pr429 == "A429" && df429.Format == "A429" && cols429.Contains("LABEL") && cols429.Contains("SSM"), $"total={t429} protocol={pr429} format={df429.Format} cols={cols429}");
        int kv429 = (await JS(w, "await nx.test.m1553().scrollTo(0); const r = document.querySelector('.m1553-scroll .trow[data-i]'); return await nx.test.m1553().selectRow(+r.dataset.row);")).GetInt32();
        var d429 = JsonDocument.Parse((await JS(w, "return JSON.stringify(nx.test.m1553().decode());")).GetString()!).RootElement;
        var rec0 = Mil1553DataSource.FileOf429(df429)!.Get(0);
        GB("a429_decode", kv429 >= 8 && d429.GetProperty("label").GetString() == rec0.LabelOctal && d429.GetProperty("sdi").GetInt32() == rec0.Sdi && d429.GetProperty("parityOk").GetBoolean() == rec0.ParityOk, $"kv={kv429} label={d429.GetProperty("label")} sdi={d429.GetProperty("sdi")} ssm={d429.GetProperty("ssmName")} parity={rec0.ParityOk}");
        int nLabel = (await JS(w, $"return await nx.test.m1553().setInput('label', {J(rec0.LabelOctal)});")).GetInt32();
        int nPar = (await JS(w, "await nx.test.m1553().clickFilter('[data-a=parity]'); return nx.test.m1553().total();")).GetInt32();
        await JS(w, "return JSON.stringify(await nx.test.m1553().filter({label: -1, parityOnly: false, sdi: -1, ssm: -1}));");
        var s429 = JsonDocument.Parse(JsonSerializer.Serialize(e.Mil1553.Stats())).RootElement;
        int bars429 = (await JS(w, "return await nx.test.m1553().stats();")).GetInt32(); await Idle(w, 150);
        GB("a429_filter_stats", nLabel > 0 && nLabel < 2000 && nPar <= nLabel && s429.GetProperty("count").GetInt32() == 2000 && s429.GetProperty("perLabel").GetArrayLength() > 0 && bars429 > 0, $"label {rec0.LabelOctal}={nLabel} +parity errors={nPar} stats count={s429.GetProperty("count")} labels={s429.GetProperty("perLabel").GetArrayLength()} parityErrors={s429.GetProperty("parityErrors")} bars={bars429}");
        await Shot(w, "v100_09_a429");
        await JS(w, "await nx.test.m1553().clickFilter('[data-a=stats]'); return true;");
        // ---- 13) dialogs: settings (sections, pills), help (bus / icd sections), about (version only, no third-party names)
        await JS(w, "await nx.test.rail('SETTINGS'); return true;"); await Idle(w, 150);
        int secs = (await JS(w, "return document.querySelectorAll('.settings .sec').length;")).GetInt32();
        int pills = (await JS(w, "return document.querySelectorAll('.settings button[data-bt]').length;")).GetInt32();
        string tmplIn = (await JS(w, "return document.querySelector('.settings [data-f=icdTemplateDir]')?.value ?? null;")).ToString();
        await Shot(w, "v100_10_settings");
        await JS(w, "await nx.test.rail('ANALYZE'); return true;");
        GB("settings_dialog", secs == 4 && pills == 3 && tmplIn != "null", $"sections={secs} time pills={pills} template input={tmplIn != "null"}");
        bool helpBus = (await JS(w, "await nx.test.help('analyze'); return document.querySelector('.help-pane').textContent.includes('MIL-STD-1553') && !!document.querySelector('.help-nav [data-s=icd]');")).GetBoolean();
        await Shot(w, "v100_11_help_bus");
        bool helpIcd = (await JS(w, "await nx.test.help('icd'); return document.querySelector('.help-pane').textContent.includes('IMPORT') && document.querySelector('.help-pane').textContent.includes('EXPORT');")).GetBoolean();
        int helpSections = (await JS(w, "return document.querySelectorAll('.help-nav [data-s]').length;")).GetInt32();
        string helpText = (await JS(w, "return document.querySelector('.modal-bg')?.textContent ?? '';")).GetString() ?? "";
        await JS(w, "document.querySelector('.modal-bg')?.remove(); return true;");
        GB("help_sections", helpBus && helpIcd && helpSections == 8, $"bus={helpBus} icd={helpIcd} sections={helpSections}");
        await JS(w, "await nx.test.clickSel('[data-a=about]'); return true;"); await Idle(w, 150);
        string about = (await JS(w, "return document.querySelector('.modal.about')?.textContent ?? '';")).GetString() ?? "";
        await Shot(w, "v100_12_about");
        await JS(w, "document.querySelector('.modal-bg')?.remove(); return true;");
        // names that must not appear in the about / help text are supplied by the private run script (--forbid a,b,c), never in source
        var forbidden = Arg("--forbid").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hits = forbidden.Where(f => about.Contains(f, StringComparison.OrdinalIgnoreCase) || helpText.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
        GB("about_help_third_party_names", about.Contains(AppInfo.VersionText) && hits.Count == 0, $"about='{about.Replace('\n', ' ')[..Math.Min(80, about.Length)]}' hits={string.Join(",", hits)}");
        // ---- 14) dark theme capture + accent scan + memory
        e.Theme = "dark"; e.Touch(); await JS(w, "await nx.test.settle(); return true;"); await Idle(w, 200);
        var dk = JsonDocument.Parse((await JS(w, "await nx.test.rail('SIMULATE'); await nx.test.settle(); const css = getComputedStyle(document.documentElement); const toRgb = (c) => { const d = document.createElement('div'); d.style.color = c; document.body.append(d); const v = getComputedStyle(d).color; d.remove(); return v; }; const accents = new Set(['--accent', '--accent2', '--accent-line', '--red'].map(v => toRgb(css.getPropertyValue(v).trim()))); const used = new Set(); document.querySelectorAll('.main .pill.on, .main .pill.primary, .main .frame-vis i.on, .main .lane:not(.off) i.on, .rail button.on').forEach(el => { const bg = getComputedStyle(el).backgroundColor; if (accents.has(bg)) used.add(bg); }); return JSON.stringify({ used: [...used], cells: document.querySelectorAll('.frame-vis .lane:not(.off) i.on').length });")).GetString()!).RootElement;
        GB("ui2_6_dark_single_accent", dk.GetProperty("used").GetArrayLength() <= 1 && dk.GetProperty("cells").GetInt32() > 0, $"accent backgrounds in dark SIMULATE: {string.Join(" | ", dk.GetProperty("used").EnumerateArray().Select(x => x.GetString()))} cells on={dk.GetProperty("cells")}");
        await Shot(w, "v100_13b_dark_simulate");
        await JS(w, "await nx.test.rail('ANALYZE'); return true;"); await Shot(w, "v100_13_dark");
        var scan = await JS(w, "return JSON.stringify(nx.test.accentScan());");
        using (var jd = JsonDocument.Parse(scan.GetString()!)) { int ah = jd.RootElement.GetProperty("hits").GetArrayLength(); G("accent_borders_zero", ah, "", "=0"); if (ah > 0) L("accent hits: " + jd.RootElement.GetProperty("hits")); }
        e.Theme = "light"; e.Touch(); await JS(w, "await nx.test.settle(); return true;");
        await JS(w, "await nx.test.rail('START'); return true;"); await Shot(w, "v100_14_view_after");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); await Idle(w, 200);
        G("memory_ws_end_mb", WsMb(), "MB", "<=600");
        G("v100rc_total_s", sw.Elapsed.TotalSeconds, "s");
    }
}
