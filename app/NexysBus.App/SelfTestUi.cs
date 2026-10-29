using System.IO;
using System.Text.Json;
using NexysBus.App.Services;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>--uishots --out dir --prefix before|after: the ten scenes of docs/ui-feedback.md (ANALYZE list / decode / filters / tree /
/// trend / ABS time, ICD import footer / footer buttons / export, SIMULATE language) captured as ui-NN-&lt;prefix&gt;.png.</summary>
public static partial class SelfTest
{
    static async Task UiShots(MainWindow w)
    {
        var e = w.Engine; string pre = Arg("--prefix", "after"); string dir = Path.Combine(OutDir, "data"); Directory.CreateDirectory(dir);
        e.Theme = "light"; e.Plot = "dark"; e.BusTime = "rel"; e.NewProject("UI feedback"); e.Touch();
        string hubCsv = Path.Combine(dir, "dummy_icd.csv"); File.WriteAllText(hubCsv, IcdHub.Dummy().ToCsv());
        async Task S(int n, string name) { await Shot(w, $"ui-{n:00}-{pre}-{name}"); }
        // ICD import (footer + preview) = item 1; footer buttons = item 8; export = item 9
        await JS(w, "await nx.test.icd().open('import'); return true;"); await Idle(w, 150);
        await JS(w, $"await nx.test.icd().pick({J(hubCsv)}); return true;"); await Idle(w, 250);
        await S(1, "icd-import-footer");
        await JS(w, "return JSON.stringify(await nx.test.icd().importGo());"); await Idle(w, 200);
        await S(8, "icd-footer-dummy");
        await JS(w, "await nx.test.icd().open('export'); return true;"); await Idle(w, 250);
        await S(9, "icd-export-text");
        // ANALYZE with the built-in sample: list (ERR column) = 3, filter bar = 5, navigator = 6, decode bits = 2, trend = 7, ABS = 4
        await JS(w, "await nx.test.rail('ANALYZE'); await nx.test.clickSel('.bus-empty [data-a=sample1553]'); return true;"); await Idle(w, 400);
        await JS(w, "await nx.test.settle(); return true;");
        await S(3, "list-err-column");
        await S(5, "filter-bar");
        await S(6, "navigator");
        await JS(w, "await nx.test.m1553().scrollTo(0); const r = document.querySelector('.m1553-scroll .trow[data-i]'); await nx.test.m1553().selectRow(+r.dataset.row); return true;"); await Idle(w, 200);
        await S(2, "decode-bits");
        await JS(w, "try { await nx.test.m1553().trendFirst(); } catch {} await nx.test.settle(); return true;"); await Idle(w, 1500); await JS(w, "await nx.test.settle(); return true;");
        await S(7, "trend-card");
        await JS(w, "try { await nx.test.m1553().clickFilter('[data-time=abs]'); } catch {} await nx.test.settle(); return true;"); await Idle(w, 200);
        await S(4, "abs-time");
        await JS(w, "try { await nx.test.m1553().clickFilter('[data-time=rel]'); } catch {} return true;");
        await JS(w, "await nx.test.rail('SIMULATE'); return true;"); await Idle(w, 250);
        await S(10, "simulate-language");
        L("uishots done " + pre);
    }

    /// <summary>--uishots2 --prefix before|after: docs/ui-feedback-2.md scenes (ICD export formats, decode EU column, ICD edit BUS column,
    /// PROJECT buttons / hints, start button, dark SIMULATE accents, SETTINGS layout, RUN panel expected load).</summary>
    static async Task UiShots2(MainWindow w)
    {
        var e = w.Engine; string pre = Arg("--prefix", "after"); string dir = Path.Combine(OutDir, "data"); Directory.CreateDirectory(dir);
        e.Theme = "light"; e.Plot = "dark"; e.BusTime = "rel"; e.Touch();
        async Task S(int n, string name) { await Shot(w, $"ui2-{n:00}-{pre}-{name}"); }
        await JS(w, "await nx.test.rail('START'); return true;"); await Idle(w, 250);
        await S(4, "start-button");
        e.NewProject("UI feedback 2"); string hubCsv = Path.Combine(dir, "dummy_icd.csv"); File.WriteAllText(hubCsv, IcdHub.Dummy().ToCsv()); e.Mil1553.LoadIcd(hubCsv);
        await JS(w, "await nx.test.rail('PROJECT'); return true;"); await Idle(w, 250);
        await S(5, "project-hints");
        await JS(w, "await nx.test.icd().open('export'); return true;"); await Idle(w, 300);
        await S(1, "icd-export-formats");
        await JS(w, "await nx.test.icd().open('edit'); return true;"); await Idle(w, 250);
        await S(3, "icd-edit-bus-column");
        await JS(w, "await nx.test.rail('ANALYZE'); await nx.test.clickSel('.bus-empty [data-a=sample1553]'); await nx.test.settle(); return true;"); await Idle(w, 400);
        await JS(w, "await nx.test.m1553().scrollTo(0); const r = document.querySelector('.m1553-scroll .trow[data-i]'); await nx.test.m1553().selectRow(+r.dataset.row); return true;"); await Idle(w, 250);
        await S(2, "decode-eu-column");
        await JS(w, "await nx.test.rail('SETTINGS'); return true;"); await Idle(w, 250);
        await S(7, "settings-layout");
        await JS(w, "await nx.test.rail('SIMULATE'); return true;"); await Idle(w, 250);
        await S(8, "run-panel-expected");
        e.Theme = "dark"; e.Touch(); await JS(w, "await nx.test.settle(); return true;"); await Idle(w, 250);
        await S(6, "dark-simulate-accents");
        e.Theme = "light"; e.Touch();
        L("uishots2 done " + pre);
    }
}
