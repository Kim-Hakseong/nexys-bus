using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexysView.Core.Bus;
using NexysView.Core.Mil1553;

namespace NexysBus.App;

/// <summary>
/// Site profile for the <c>--site</c> self-test: everything that belongs to one bench (terminal address, bus, the frame, the words the
/// terminal is known to send, the counter word) lives in a JSON file next to the measurement results, never in the code.
/// Schema: docs/site-profile.md; example: site-profiles/example.json.
/// </summary>
public sealed class SiteProfile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    /// <summary>Remote terminal address the frame addresses.</summary>
    [JsonPropertyName("rt")] public int Rt { get; set; } = 4;
    /// <summary>Bus the terminal is reachable on ("A" | "B"); H2 switches to the other one.</summary>
    [JsonPropertyName("bus")] public string Bus { get; set; } = "A";
    /// <summary>Default message period (ms) for messages without one.</summary>
    [JsonPropertyName("periodMs")] public double PeriodMs { get; set; } = 20;
    /// <summary>Second card channel to run as a bus monitor (−1 = none).</summary>
    [JsonPropertyName("monitorChannel")] public int MonitorChannel { get; set; } = -1;
    /// <summary>ICD CSV (relative to the profile file) that defines the frame; when absent, <see cref="Messages"/> does.</summary>
    [JsonPropertyName("icd")] public string Icd { get; set; } = "";
    [JsonPropertyName("messages")] public List<SiteMessage> Messages { get; set; } = new();
    /// <summary>Words the terminal is known to send: subaddress → word index → hex value (e.g. "3": {"0": "8000"}).</summary>
    [JsonPropertyName("expect")] public Dictionary<string, Dictionary<string, string>> Expect { get; set; } = new();
    [JsonPropertyName("counter")] public SiteCounter? Counter { get; set; }
    /// <summary>Free notes (wiring, who owns the terminal) — carried into the report only.</summary>
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    /// <summary>ARINC 664 bench (optional): ports, ICD, receive / transmit durations — see docs/site-profile.md "afdx".</summary>
    [JsonPropertyName("afdx")] public SiteAfdx? Afdx { get; set; }

    public sealed class SiteAfdx
    {
        /// <summary>ARINC 664 ICD (CSV / XLSX / hub JSON), relative to the profile file.</summary>
        [JsonPropertyName("icd")] public string Icd { get; set; } = "";
        /// <summary>Packet-capture port of network A / B (device name or description as the port list shows it).</summary>
        [JsonPropertyName("nicA")] public string NicA { get; set; } = "";
        [JsonPropertyName("nicB")] public string NicB { get; set; } = "";
        /// <summary>Receive-only capture (N1), seconds.</summary>
        [JsonPropertyName("listenSeconds")] public double ListenSeconds { get; set; } = 30;
        /// <summary>N2: also transmit these VL ids (empty = no transmission) for sendSeconds.</summary>
        [JsonPropertyName("txVls")] public List<int> TxVls { get; set; } = new();
        [JsonPropertyName("sendSeconds")] public double SendSeconds { get; set; } = 30;
        /// <summary>VL ids the peer is expected to send (N1 gate); empty = every ICD VL not in txVls.</summary>
        [JsonPropertyName("expectVls")] public List<int> ExpectVls { get; set; } = new();
        [JsonPropertyName("peerName")] public string PeerName { get; set; } = "";
        [JsonPropertyName("jmaxMs")] public double JmaxMs { get; set; } = 0.5;
        [JsonPropertyName("skewMaxMs")] public double SkewMaxMs { get; set; } = 10;
    }
    [JsonIgnore] public string Path { get; set; } = "";

    public sealed class SiteMessage
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("sa")] public int Sa { get; set; }
        /// <summary>0 = BC→RT, 1 = RT→BC</summary>
        [JsonPropertyName("tr")] public int Tr { get; set; }
        [JsonPropertyName("wc")] public int Wc { get; set; } = 32;
        /// <summary>Mode code number (−1 = subaddress message).</summary>
        [JsonPropertyName("modeCode")] public int ModeCode { get; set; } = -1;
        [JsonPropertyName("periodMs")] public double PeriodMs { get; set; }
        /// <summary>Data source of a BC→RT message: kind const | ramp | sine | eu, value, amplitude, periodS (word 1 of the message).</summary>
        [JsonPropertyName("source")] public BusDataSource? Source { get; set; }
    }
    public sealed class SiteCounter
    {
        [JsonPropertyName("sa")] public int Sa { get; set; }
        [JsonPropertyName("word")] public int Word { get; set; }
        /// <summary>Expected tick length in ms (0 = unknown, measured only).</summary>
        [JsonPropertyName("msPerTick")] public double MsPerTick { get; set; }
    }

    public static SiteProfile Load(string path)
    {
        var p = JsonSerializer.Deserialize<SiteProfile>(File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? throw new InvalidDataException("empty profile");
        p.Path = System.IO.Path.GetFullPath(path); p.Bus = p.Bus.ToUpperInvariant() == "B" ? "B" : "A";
        if (p.Icd.Length > 0 && !System.IO.Path.IsPathRooted(p.Icd)) p.Icd = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(p.Path) ?? "", p.Icd);
        if (p.Afdx != null && p.Afdx.Icd.Length > 0 && !System.IO.Path.IsPathRooted(p.Afdx.Icd)) p.Afdx.Icd = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(p.Path) ?? "", p.Afdx.Icd);
        return p;
    }

    /// <summary>Parsed expectation table: subaddress → word index → value.</summary>
    public Dictionary<int, Dictionary<int, ushort>> ExpectWords()
    {
        var d = new Dictionary<int, Dictionary<int, ushort>>();
        foreach (var (sa, words) in Expect) { var w = new Dictionary<int, ushort>(); foreach (var (k, v) in words) w[int.Parse(k)] = Convert.ToUInt16(v.Trim(), 16); d[int.Parse(sa.StartsWith("sa", StringComparison.OrdinalIgnoreCase) ? sa[2..] : sa)] = w; }
        return d;
    }

    /// <summary>The frame for one bus: from the ICD file when given, else from <see cref="Messages"/>; nothing is simulated (a real terminal answers).</summary>
    public BusConfig ToConfig(string bus)
    {
        var cfg = new BusConfig(); int order = 0;
        if (Icd.Length > 0 && File.Exists(Icd))
        {
            foreach (var m in IcdHub.FromCsv(Icd).Messages) { var bm = BusMessage.FromIcd(m, order++); bm.Bus = bus; bm.SimulateRt = false; if (bm.PeriodMs <= 0) bm.PeriodMs = PeriodMs; ApplySource(bm, null); cfg.Messages.Add(bm); }
        }
        else foreach (var m in Messages)
        {
            var bm = new BusMessage { Name = m.Name.Length > 0 ? m.Name : (m.ModeCode >= 0 ? $"RT{Rt}_MC{m.ModeCode}" : $"RT{Rt}_SA{m.Sa}_{(m.Tr == 1 ? "TX" : "RX")}"), Rt = Rt, Sa = m.Sa, Tr = m.Tr, Wc = m.Wc, ModeCode = m.ModeCode, PeriodMs = m.PeriodMs > 0 ? m.PeriodMs : PeriodMs, Bus = bus, Order = order++, SimulateRt = false };
            if (bm.Tr == 0 && bm.DataWords > 0) bm.Words.Add(new BusWord { Name = "W01", Index = 1, BitStart = 1, BitLen = 16, Type = "UINT" });
            ApplySource(bm, m.Source); cfg.Messages.Add(bm);
        }
        cfg.MinorFrameMs = cfg.Messages.Count > 0 ? cfg.Messages.Min(x => x.PeriodMs) : PeriodMs;
        return cfg;
    }
    static void ApplySource(BusMessage bm, BusDataSource? src)
    {
        if (bm.Tr != 0 || bm.Words.Count == 0) return;
        bm.Source = src ?? (bm.Sa == 1 ? new BusDataSource { Kind = "sine", Value = 1000, Amplitude = 500, PeriodS = 2 } : bm.Sa == 2 ? new BusDataSource { Kind = "ramp", Value = 0, Amplitude = 1000, PeriodS = 10 } : new BusDataSource { Kind = "const", Value = 0 });
    }
}
