using System.Diagnostics;
using System.IO;
using System.Text.Json;
using NexysView.Core.Arinc664;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>
/// --site with a profile that has an "afdx" section (docs/site-profile.md): N0 packet-capture library and ports (roles A / B),
/// N1 receive-only capture of the peer for listenSeconds → checks + interop checklist, N2 (txVls given) transmit those VLs for
/// sendSeconds while capturing. Writes afdx_listen.pcapng, afdx_send.pcapng, interop-report.md and afdx_summary.json into --out.
/// Nothing is transmitted unless the profile lists txVls.
/// </summary>
public static partial class SelfTest
{
    static async Task<bool> SiteAfdx(MainWindow w, SiteProfile prof)
    {
        var a = prof.Afdx!; var e = w.Engine; var sum = new Dictionary<string, object?>();
        // ---- N0 library, ports, roles
        bool lib = Pcap.Available;
        GB("afdx_site_library", lib, lib ? Pcap.LibraryPath : "no packet-capture library on this PC (" + Pcap.LastError + ") — install Npcap separately, then run again; the AFDX steps are skipped");
        if (!lib) return false;
        var ports = Pcap.Ports(); sum["ports"] = ports;
        L("ports: " + string.Join(" | ", ports.Select(p => $"{p.Name} [{p.Id}]")));
        string? Find(string want) => want.Length == 0 ? null : ports.FirstOrDefault(p => p.Id == want || p.Name == want || p.Name.Contains(want, StringComparison.OrdinalIgnoreCase))?.Id ?? want;
        var roles = NetworkRoles.Assign(ports, Find(a.NicA), Find(a.NicB));
        GB("afdx_site_ports", roles.NicA != null || roles.NicB != null, $"A {roles.NicA ?? "-"} · B {roles.NicB ?? "-"}" + (roles.Notes.Count > 0 ? " · " + string.Join(" · ", roles.Notes) : ""));
        if (roles.NicA == null && roles.NicB == null) return false;
        // project for the checks / report
        e.NewProject(prof.Name.Length > 0 ? prof.Name : "site", "arinc664");
        if (a.Icd.Length > 0 && File.Exists(a.Icd)) e.Mil1553.LoadIcd(a.Icd);
        var s = e.Project.Afdx!; s.NicA = roles.NicA ?? ""; s.NicB = roles.NicB ?? ""; s.JmaxMs = a.JmaxMs; s.SkewMaxMs = a.SkewMaxMs; s.PeerName = a.PeerName;
        var icdVls = e.Project.Icd.Vls ?? new(); GB("afdx_site_icd", icdVls.Count > 0, $"{a.Icd}: {icdVls.Count} VLs");
        // ---- N1 receive only
        var cap = await CaptureFor(new PcapNetworkDevice(roles.NicA, roles.NicB), null, a.ListenSeconds);
        string p1 = Path.Combine(OutDir, "afdx_listen.pcapng"); cap.Save(p1); e.Afdx.OpenCapture(p1);
        var an = e.Afdx.Analysis!; var expect = a.ExpectVls.Count > 0 ? a.ExpectVls : icdVls.Select(v => v.VlId).Where(id => !a.TxVls.Contains(id)).ToList();
        var missing = expect.Where(id => !an.Vls.ContainsKey(id)).ToList();
        GB("afdx_site_listen", cap.Count > 0 && missing.Count == 0, $"{a.ListenSeconds:G} s: {cap.Count:N0} frames, {an.Vls.Count} VLs, findings {an.TotalErrors}, ARP {an.Arp} ICMP {an.Icmp}; expected VLs not received: {(missing.Count == 0 ? "none" : string.Join(", ", missing))}");
        var checks = e.Afdx.PeerChecks(); foreach (var c in checks) L($"N1 {c.Id} {c.Verdict}: {c.Detail}");
        sum["listen"] = new { frames = cap.Count, vls = an.Vls.Count, findings = an.TotalErrors, checks };
        // ---- N2 transmit (only the listed VLs) while capturing
        if (a.TxVls.Count > 0)
        {
            var tx = icdVls.Where(v => a.TxVls.Contains(v.VlId)).ToList();
            var dev = new PcapNetworkDevice(roles.NicA, roles.NicB); var snd = new AfdxSender(dev, tx, s.SourcesById());
            var cap2 = await CaptureFor(dev, snd, a.SendSeconds);
            string p2 = Path.Combine(OutDir, "afdx_send.pcapng"); cap2.Save(p2);
            long sent = snd.Vls.Sum(v => v.Sent[0] + v.Sent[1]); long seen = Enumerable.Range(0, cap2.Count).Count(i => a.TxVls.Contains(cap2.Vl(i)));
            GB("afdx_site_send", sent > 0 && dev.Counters.SendErrors == 0, $"{a.SendSeconds:G} s: sent {sent:N0} frames on {tx.Count} VLs, send errors {dev.Counters.SendErrors}, own frames seen back on the capture {seen:N0} (a switch may not mirror them)");
            sum["send"] = snd.Vls.Select(v => new { vl = v.Vl.VlId, sentA = v.Sent[0], sentB = v.Sent[1], meanMs = v.IntMeanMs, lateStdUs = v.LateStdUs, slips = v.Slips }).ToList();
        }
        e.Afdx.OpenCapture(p1);
        string rep = e.Afdx.ExportReport(Path.Combine(OutDir, "interop-report.md")); L("report " + rep);
        File.WriteAllText(Path.Combine(OutDir, "afdx_summary.json"), JsonSerializer.Serialize(sum, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    static async Task<AfdxCapture> CaptureFor(INetworkDevice dev, AfdxSender? snd, double seconds)
    {
        var cap = new AfdxCapture(1 << 24); dev.Open(); dev.Start(); snd?.Start(); var sw = Stopwatch.StartNew();
        try { while (sw.Elapsed.TotalSeconds < seconds) { await Task.Delay(50); dev.Poll((n, t, f) => cap.Add(n, t, f)); } }
        finally { snd?.Stop(); await Task.Delay(100); dev.Poll((n, t, f) => cap.Add(n, t, f)); dev.Dispose(); }
        return cap;
    }
}
