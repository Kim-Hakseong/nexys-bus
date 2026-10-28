using System.IO;
using System.Text.Json;
using NexysView.Core.Bus;
using NexysView.Core.Data;
using NexysView.Core.Mil1553;

namespace NexysBus.App.Services;

/// <summary>NexysBus application model: the shared chart/file engine (<see cref="EngineBase"/>, nexys-core) plus the project (.nxb),
/// the simulation (<see cref="SimulationService"/>) and the rail (START / PROJECT / ANALYZE / SIMULATE / ICD / SETTINGS = Mode).</summary>
public sealed class BusEngine : EngineBase
{
    public BusProject Project { get; private set; } = new();
    public bool Dirty { get; set; }
    public List<string> Recent { get; } = new();
    public SimulationService Sim { get; }
    public static readonly string[] Rails = { "START", "PROJECT", "ANALYZE", "SIMULATE", "ICD", "SETTINGS" };
    public override string BusModeName => "ANALYZE";

    public BusEngine()
    {
        Mode = "START"; Sim = new SimulationService(this);
        Mil1553.SetHub(Project.Icd, "(project)");
        Mil1553.HubChanged += hub => { if (!ReferenceEquals(hub, Project.Icd)) { Project.SetIcd(hub); Dirty = true; } Touch(); };
    }

    // ------------------------------------------------------------------ project
    public void NewProject(string? name)
    {
        Sim.Stop();
        Project = new BusProject { Name = string.IsNullOrWhiteSpace(name) ? "New project" : name!.Trim() };
        Mil1553.SetHub(Project.Icd, "(project)"); Dirty = false; Mode = "PROJECT"; SetStatus($"Project '{Project.Name}' created — pick a device, load an ICD, then SIMULATE"); Touch();
    }
    public void OpenProject(string path)
    {
        Sim.Stop();
        Project = BusProject.Load(path); Mil1553.SetHub(Project.Icd, path); AddRecent(path); Dirty = false; Mode = "PROJECT";
        SetStatus($"Project {Path.GetFileName(path)}: {Project.Icd.Messages.Count} messages, device {Project.Device}"); Touch();
    }
    public string SaveProject(string? path)
    {
        path ??= Project.Path; if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("no project path");
        Project.Save(path); AddRecent(path); Dirty = false; SetStatus("Saved " + path); Touch(); return path;
    }
    void AddRecent(string path) { Recent.Remove(path); Recent.Insert(0, path); while (Recent.Count > 8) Recent.RemoveAt(Recent.Count - 1); }

    /// <summary>Open whatever was dropped / given on the command line: project, capture, simulator project, ICD.</summary>
    public void OpenAny(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext == ".nxb") OpenProject(path);
            else if (ext == ".mon") { OpenFile(path); Mode = "ANALYZE"; Touch(); }
            else if (ext is ".cpj" or ".cdv") ImportSimulator(path);
            else if (ext is ".csv" or ".xlsx" or ".json" or ".xml") { Mil1553.LoadIcd(path); Mode = "ICD"; Touch(); }
            else SetStatus("Unsupported file: " + Path.GetFileName(path));
        }
        catch (Exception ex) { Diag.Trace("open failed " + ex); SetStatus("Open failed: " + ex.Message); SetLed("error"); }
    }

    public object ImportSimulator(string path)
    {
        var hub = BusProject.ImportSimulatorProject(path);
        Mil1553.SetHub(hub, path); Mode = "SIMULATE"; Touch();
        return new { messages = hub.Messages.Count, words = hub.Messages.Sum(m => m.Words.Count), source = hub.Source };
    }
    public object ExportSimulator(string dir, string[]? formats = null) => Mil1553.Export(dir, IcdTemplateDir.Length > 0 ? IcdTemplateDir : null, true, formats ?? new[] { "project", "xml", "api" });

    public void SetProjectField(string field, JsonElement v)
    {
        var p = Project;
        switch (field)
        {
            case "name": p.Name = v.GetString() ?? p.Name; break;
            case "device": p.Device = v.GetString() == "virtual" ? "virtual" : v.GetString() ?? "virtual"; break;
            case "channel": p.Bus.Channel = Math.Clamp(v.GetInt32(), 0, 3); break;
            case "minorFrameMs": p.Bus.MinorFrameMs = Math.Max(0, v.GetDouble()); break;
            case "responseTenthUs": p.Bus.ResponseTenthUs = Math.Clamp(v.GetInt32(), 1, 10000); break;
            case "gapUs": p.Bus.GapUs = Math.Clamp(v.GetInt32(), 0, 1000); break;
            case "seed": p.Bus.Seed = v.GetInt32(); break;
            case "captureDir": p.CaptureDir = v.GetString() ?? ""; break;
        }
        Dirty = true; Touch();
    }
    public void SetSchedule(string message, string field, JsonElement v)
    {
        var s = Project.Schedule.FirstOrDefault(x => x.Message == message); if (s == null) return;
        switch (field) { case "enabled": s.Enabled = v.GetBoolean(); break; case "periodMs": s.PeriodMs = Math.Max(0.1, v.GetDouble()); break; case "order": s.Order = v.GetInt32(); break; }
        Renumber(); Dirty = true; Touch();
    }
    public void MoveSchedule(string message, int dir)
    {
        var list = Project.Schedule.OrderBy(s => s.Order).ToList(); int i = list.FindIndex(s => s.Message == message), j = i + dir;
        if (i < 0 || j < 0 || j >= list.Count) return; (list[i], list[j]) = (list[j], list[i]); for (int k = 0; k < list.Count; k++) list[k].Order = k; Dirty = true; Touch();
    }
    void Renumber() { int k = 0; foreach (var s in Project.Schedule.OrderBy(s => s.Order)) s.Order = k++; }
    public void SetSource(string message, string field, JsonElement v)
    {
        if (!Project.Sources.TryGetValue(message, out var src)) Project.Sources[message] = src = new BusDataSource();
        switch (field) { case "kind": src.Kind = v.GetString() is "const" or "ramp" or "sine" or "eu" ? v.GetString()! : "sine"; break; case "value": src.Value = v.GetDouble(); break; case "amplitude": src.Amplitude = v.GetDouble(); break; case "periodS": src.PeriodS = Math.Max(0.01, v.GetDouble()); break; }
        Dirty = true; Touch();
    }
    public void SetError(string field, int every)
    {
        var e = Project.Errors; every = Math.Max(0, every);
        switch (field) { case "noResponse": e.NoResponseEvery = every; break; case "parity": e.ParityEvery = every; break; case "wordCount": e.WordCountEvery = every; break; }
        Dirty = true; Touch();
    }

    /// <summary>Devices offered in PROJECT: the virtual bus always, hardware cards when the driver reports any (hardware day).</summary>
    public List<object> Devices()
    {
        var l = new List<object> { new { id = "virtual", kind = "virtual", name = "Virtual bus", detail = "software BC + simulated RTs + monitor", available = true } };
        foreach (var d in HardwareBusDevice.Enumerate()) l.Add(new { id = d.Name, kind = d.Kind, name = d.Name, detail = d.Detail, available = false });
        return l;
    }

    // ------------------------------------------------------------------ bridge commands of this application
    public bool Dispatch(string cmd, JsonElement r, out object? result)
    {
        result = null;
        switch (cmd)
        {
            case "rail": { string m = Bridge.S(r, "name", "START").ToUpperInvariant(); Mode = Rails.Contains(m) ? m : "START"; Touch(); return true; }
            case "project.new": NewProject(Bridge.S(r, "name")); return true;
            case "project.openPath": OpenProject(Bridge.S(r, "path")); return true;
            case "project.saveTo": result = SaveProject(Bridge.S(r, "path")); return true;
            case "project.set": SetProjectField(Bridge.S(r, "field"), r.GetProperty("value")); return true;
            case "project.importSimPath": result = ImportSimulator(Bridge.S(r, "path")); return true;
            case "project.exportSimTo": result = ExportSimulator(Bridge.S(r, "dir"), Bridge.SL(r, "formats") is { Count: > 0 } fm ? fm.ToArray() : null); return true;
            case "project.useIcd": if (Mil1553.Hub != null) { Project.SetIcd(Mil1553.Hub); Dirty = true; Touch(); } return true;
            case "project.close": Sim.Stop(); Project = new BusProject(); Mil1553.SetHub(Project.Icd, "(project)"); Dirty = false; Mode = "START"; Touch(); return true;
            case "devices.search": SetStatus("Hardware search: no driver cards found on this PC (hardware support is scheduled for the hardware day)"); result = Devices(); return true;
            case "sim.schedule.set": SetSchedule(Bridge.S(r, "message"), Bridge.S(r, "field"), r.GetProperty("value")); return true;
            case "sim.move": MoveSchedule(Bridge.S(r, "message"), Bridge.I(r, "dir", 1)); return true;
            case "sim.source.set": SetSource(Bridge.S(r, "message"), Bridge.S(r, "field"), r.GetProperty("value")); return true;
            case "sim.errors.set": SetError(Bridge.S(r, "field"), Bridge.I(r, "every")); return true;
            case "sim.start": Sim.Start(); return true;
            case "sim.stop": Sim.Stop(); return true;
            case "sim.runFor": result = Sim.RunFor(Bridge.D(r, "seconds", 10)); return true;
            case "sim.status": result = Sim.Dto(); return true;
            case "setFeature": if (Bridge.S(r, "name") == "icdTemplateDir") IcdTemplateDir = Bridge.S(r, "value").Trim(); Touch(); return true;
        }
        return false;
    }

    protected override void ExtendSnapshot(Dictionary<string, object?> s)
    {
        s["rail"] = Mode; s["recent"] = Recent.Where(File.Exists).Select(p => new { path = p, name = Path.GetFileNameWithoutExtension(p), dir = Path.GetDirectoryName(p) }).ToList();
        s["devices"] = Devices(); s["project"] = ProjectDto(); s["sim"] = Sim.Dto();
    }

    object ProjectDto()
    {
        var p = Project; var byName = p.Icd.Messages.ToDictionary(m => m.Name, m => m);
        return new
        {
            name = p.Name, path = p.Path, dirty = Dirty, device = p.Device, bus = new { channel = p.Bus.Channel, minorFrameMs = p.Bus.MinorFrameMs, responseTenthUs = p.Bus.ResponseTenthUs, gapUs = p.Bus.GapUs, seed = p.Bus.Seed },
            icd = new { source = p.Icd.Source, messages = p.Icd.Messages.Count, words = p.Icd.Messages.Sum(m => m.Words.Count), errors = IcdHub.Validate(p.Icd).Count(x => x.StartsWith("error")) },
            schedule = p.Schedule.OrderBy(x => x.Order).Select(x => { byName.TryGetValue(x.Message, out var m); var src = p.Sources.GetValueOrDefault(x.Message) ?? new BusDataSource(); return new { message = x.Message, order = x.Order, enabled = x.Enabled, periodMs = x.PeriodMs, rt = m?.Rt ?? 0, sa = m?.Sa ?? 0, tr = m?.Tr ?? 0, wc = m?.Wc ?? 0, bus = m?.Bus ?? "A", words = m?.Words.Count ?? 0, source = new { kind = src.Kind, value = src.Value, amplitude = src.Amplitude, periodS = src.PeriodS } }; }).ToList(),
            errors = new { noResponse = p.Errors.NoResponseEvery, parity = p.Errors.ParityEvery, wordCount = p.Errors.WordCountEvery },
            captureDir = p.CaptureDir, templateDir = IcdTemplateDir, templateOk = IcdTemplateDir.Length > 0 && Directory.Exists(IcdTemplateDir) && Directory.EnumerateFiles(IcdTemplateDir, "*.CDV").Any(),
        };
    }
}

/// <summary>settings.json under %LOCALAPPDATA%\NexysBus: theme, plot theme, bus time display, template folder, recent projects, filter presets, window.</summary>
public static class BusStore
{
    public sealed class Dto
    {
        public string Theme { get; set; } = "light"; public string Plot { get; set; } = "dark"; public string BusTime { get; set; } = "rel";
        public string TemplateDir { get; set; } = ""; public List<string> Recent { get; set; } = new(); public List<Mil1553Service.FilterPreset> BusFilters { get; set; } = new();
        public double W { get; set; } = 1440; public double H { get; set; } = 900; public double X { get; set; } = double.NaN; public double Y { get; set; } = double.NaN; public bool Maximized { get; set; }
        public bool TourSeen { get; set; }
    }
    public static string PathFor() => Path.Combine(AppPaths.LocalDir, "settings.json");
    static bool _loading;
    public static void Save(MainWindow w)
    {
        if (_loading || SelfTest.Enabled || Program.Fresh) return;
        try
        {
            var e = w.Engine;
            var d = new Dto { Theme = e.Theme, Plot = e.Plot, BusTime = e.BusTime, TemplateDir = e.IcdTemplateDir, Recent = e.Recent.ToList(), BusFilters = e.Mil1553.Presets.ToList(), W = w.Width, H = w.Height, X = w.Left, Y = w.Top, Maximized = w.WindowState == System.Windows.WindowState.Maximized, TourSeen = e.TourSeen };
            Directory.CreateDirectory(Path.GetDirectoryName(PathFor())!);
            File.WriteAllText(PathFor(), JsonSerializer.Serialize(d, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
    public static void Load(MainWindow w)
    {
        if (!File.Exists(PathFor()) || Program.Fresh) return;
        _loading = true;
        try
        {
            var d = JsonSerializer.Deserialize<Dto>(File.ReadAllText(PathFor())); if (d == null) return; var e = w.Engine;
            e.Theme = d.Theme is "light" or "dark" ? d.Theme : "light"; e.Plot = d.Plot is "light" or "dark" ? d.Plot : "dark";
            if (SelfTest.Enabled) return;
            e.BusTime = d.BusTime is "abs" or "delta" ? d.BusTime : "rel"; e.IcdTemplateDir = d.TemplateDir ?? ""; e.Recent.Clear(); e.Recent.AddRange(d.Recent.Where(File.Exists)); e.Mil1553.Presets.Clear(); e.Mil1553.Presets.AddRange(d.BusFilters ?? new()); e.TourSeen = d.TourSeen;
            if (!double.IsNaN(d.X) && d.W > 300) { w.Left = d.X; w.Top = d.Y; w.Width = d.W; w.Height = d.H; }
            if (d.Maximized) w.WindowState = System.Windows.WindowState.Maximized;
        }
        catch { }
        finally { _loading = false; }
    }
}
