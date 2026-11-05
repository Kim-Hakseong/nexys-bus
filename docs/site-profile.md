# Site profile (`--site --profile <file.json>`)

A site profile holds everything that belongs to one bench — which terminal, which bus, which messages, which words the terminal is known to send — so that the `--site` self-test carries no bench-specific values in its code. Keep real profiles next to the measurement results; the repository ships only `site-profiles/example.json`.

```
NexysBus.exe --site --out <dir> --profile site-profiles\example.json [--seconds 30] [--bseconds 30] [--minutes 10] [--poll 5] [--retain 0]
NexysBus.exe --site --out <dir>                      (no profile: enumerate, open, channel capabilities, configure — no frame on the bus)
```
Command-line values (`--rt`, `--bus`, `--period`, `--mon`, `--icd`, `--expect`, `--counter`) override the profile when given.

## Fields
| field | type | meaning |
|---|---|---|
| `name`, `description`, `notes` | string | Free text, copied into the log and the summary. |
| `rt` | int 0..30 | Terminal address the frame addresses (default 4). |
| `bus` | "A" \| "B" | Bus the terminal is reachable on; H2 switches to the other bus and back. |
| `periodMs` | number | Default period for messages without `periodMs` (default 20). |
| `monitorChannel` | int | Second card channel run as a bus monitor; −1 = none. On a single-function card this is the only way to get a sequential-monitor record stream. |
| `icd` | path | ICD CSV (relative to the profile) that defines the frame; columns as in the ICD IMPORT rail, including `mode_code`. When present, `messages` is ignored. |
| `messages[]` | list | The frame when no `icd` is given. Each: `name`, `sa` (1..30), `tr` (0 = BC→RT, 1 = RT→BC), `wc` (1..32), `modeCode` (−1 or 0..31; codes 16..31 carry one data word, `sa`/`wc` are then ignored), `periodMs`, `source` (BC→RT only: `kind` const \| ramp \| sine \| eu, `value`, `amplitude`, `periodS` — drives word 1). |
| `expect` | object | Words the terminal is known to send: `"<sa>": { "<word index>": "<hex>" }`. Gate `*_sa<n>_expected_match` counts matching words over the whole run. |
| `counter` | object | A word that counts: `sa`, `word`, `msPerTick` (0 = measure only). Gates: monotonic, tick length (±5 % of `msPerTick`), repeats, jumps. |

## What the gates check
H0 binding → enumerate → open → channel capabilities (multi- / single-function per channel) → configure (role assignment notes). H1 (`--seconds` on `bus`): command count = Σ period counts ±1 %, response rate ≥ 99 %, status word = address << 11, bus, Δt of the first message = period ±1 %, time-tag unit vs wall clock, DATA COUNT errors = 0, mode code 16 vector word (one data word), mode code 17 status before / after, SA24-style transmit subaddresses, expected words, counter word. H2 (`--bseconds`): the other bus, then 5 s back. H3 (`--minutes`): per-minute counts, no-response episodes, working set delta ≤ 50 MB (`--retain 0` keeps records only in the live capture, as the application does).

## Files written
`<tag>_bc.mon` (BC transactions), `<tag>_bm.mon` (monitor channel, when wired), `<tag>_sa_values.txt` (one line per second: received RT→BC words), `gates.txt`, `selftest.log`, `calllog.txt` (driver calls), `site_summary.json`.

## ARINC 664 bench (`afdx` section)
A profile may carry an `afdx` object. `--site --profile <file>` then runs the ARINC 664 steps first; a profile with only an `afdx` section (no `messages`, no `icd`) stops after them. Example: `site-profiles/example-afdx.json`.

| field | type | meaning |
|---|---|---|
| `afdx.icd` | path | ARINC 664 ICD (CSV / XLSX: one row per parameter, columns `vl_id`, `bag_ms`, `lmax`, `network`, `port`, `udp_dst`, `size`, `param`, `offset`, `type`, `endian`, `scale`, `offset_val`, `unit`; or a hub JSON with `vls`), relative to the profile. |
| `afdx.nicA`, `afdx.nicB` | string | Port of network A / B: the packet-capture device name, or a part of its description as the port list in the log shows it. The same port twice = one port for both networks (frames assigned by the source-MAC interface id). |
| `afdx.listenSeconds` | number | N1 receive-only capture (default 30). Nothing is transmitted in N1. |
| `afdx.txVls` | int list | N2: VL ids NexysBus transmits for `sendSeconds` (empty = N2 skipped, nothing is ever sent). |
| `afdx.expectVls` | int list | VLs the peer must send (N1 gate); empty = every ICD VL not in `txVls`. |
| `afdx.peerName` | string | Written into the report. |
| `afdx.jmaxMs`, `afdx.skewMaxMs` | number | Check settings (defaults 0.5 and 10). |
| `afdx.expectPeer` | bool | `false` = a bench without a peer (one port looped back to itself): N1 passes on any received traffic, N2 also checks that every own frame comes back on the capture (`afdx_site_loopback`). Default `true`. |
| `afdx.n3Minutes` | number | N3 long receive run in minutes (0 = skipped). |
| `afdx.n3Send` | bool | N3 also transmits `txVls` (own traffic to check for losses when no peer sends). |

Steps and gates: **N0** packet-capture library present (`afdx_site_library`; without it the steps are skipped — the library is installed separately and never shipped), ports and roles (`afdx_site_ports`), ICD loaded (`afdx_site_icd`). **N1** capture for `listenSeconds` (`afdx_site_listen`: frames received, every expected VL seen; the interoperability checklist is written to the log). **N2** transmit `txVls` while capturing (`afdx_site_send`: frames handed to the port, accepted, send errors 0 — a driver that refuses raw frames, e.g. a Wi-Fi adapter, shows its message here; `afdx_site_loopback` with `expectPeer: false`: own frames seen back = handed to the port, no SN gap; `afdx_site_send_bag`: |interval − BAG| p50 / p99 / max on the capture's timestamps and intervals < BAG − Jmax = 0). **N3** (`n3Minutes` > 0) long receive (`afdx_site_long`: frames in every minute, SN gaps + reversed 0, own frames sent = seen when `n3Send`, working set per minute). Files: `afdx_listen.pcapng`, `afdx_send.pcapng`, `afdx_long.pcapng`, `interop-report.md` (checklist of the N1 capture + the NexysBus → peer table), `afdx_summary.json`.
