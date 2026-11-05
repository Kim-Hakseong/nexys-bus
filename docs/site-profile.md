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
