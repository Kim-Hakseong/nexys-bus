# NexysBus 0.1.0-beta 보고 (2026-10-29)

가상 버스와 더미 ICD로 측정한 수치. 하드웨어 카드는 `HardwareBusDevice` 서명만(다음 단계).

## 셀프테스트 `--bus` (사용자 경로, 개발 빌드)
| 관문 | 값 | 기준 |
|---|---|---|
| 시작 → 페이지 준비 | 2.1–2.8 s (3회) | ≤ 3 s |
| 시작 화면 | 버튼 3 · SEARCH HARDWARE 비활성 · 레일 5 | — |
| 새 프로젝트 → PROJECT | 장치 2(virtual 선택) · 이름·마이너 프레임 입력 반영 | — |
| ICD IMPORT(CSV 열 매핑) → 프로젝트 | 10 메시지 · 스케줄 10 · 데이터 소스 10 | — |
| SIMULATE 편집 | 10행 · 격자 10레인 · 주기 20 ms · 순서 이동 · 소스 eu · 무응답 every 50 | — |
| 10 s 가상 실행 메시지 수 | 3,360 = Σ ⌈10 s / 유효 주기⌉ (장치·독립 계산 동일), minor 10 ms · major 1000 ms | ±0 |
| 버스 로드 장치 vs ANALYZE 통계 | 5.715 % = 5.715 %, 레코드 3,360 = 통계 3,360 | 일치 |
| 캡처 파일 | 메타 + 8 + 3,360 × 120 B = 404,744 B | 동일 |
| 라이브 실행(벽시계) | 0.6 s 226 → 1.2 s 445, STOP 시 .mon 저장 | 증가 |
| 디코드 EU vs 입력 파형(NAV_POS eu, 800 워드) | 최대 오차 0 (양자화 scale/2) | 숫자 |
| ERRORS ONLY vs 주입 | 67 = 67 | 동일 |
| 프로젝트 저장 → 닫기 → 최근에서 열기 | JSON 동일 | — |
| 템플릿 내보내기 → 다시 가져오기 | 비트 범위·타입·scale 동일(프로젝트 파일에 offset/unit 없음) · 템플릿 자체 가져오기 1메시지 | — |
| 설정 4절 · 도움말 7절 · 정보 타사 이름 0 · accent 테두리 0 | — | — |
| 종료 Working Set | 198–211 MB | ≤ 600 |

## 뷰어에서 옮겨 온 관문
- `--v100rc` 31/31 (샘플 20,000 열기 488 ms, 필터 초기화 3.9 ms, 프리셋 13행, 통계 20,000·RT 7·7.4 %, CSV 1,000/540, ICD 매핑 15/15, XSD 0, 프로젝트 BC 10·필드 80, 429 2,000, 설정 4절, 도움말 7절, WS 202 MB).
- `--v0100` 14/14 + 숫자: 1M 열기 479/491 ms (≤ 1 s), 필터 RT 6.6 ms · 오류 16 ms, paint 3.9/5.0 ms, 50× 열기/닫기 예외 0 · −24 MB.
- `--stress --iter 50`: 레일 순환 + 0.5 s 가상 실행 + 10k 캡처 열기/닫기 50회 → 예외 0 · 정지 0 · +9 MB · 잔여 파일 0 · 52 s.

## 배포 빌드 스모크 (`--bus`)
포터블 첫 실행 15.2 s(%TEMP% 추출) · 재실행 2.3 s · 설치 폴더 2.7 s, 모두 0 FAIL.
`NexysBus-0.1.0-beta-setup.exe` 59,138,392 B (sha256 2ee04bcf…) · `NexysBus-0.1.0-beta-portable.exe` 78,251,031 B (sha256 c7314ffd…).

## 설계 요약
- 가상 버스: BC 스케줄러(마이너 = min 주기 또는 설정, every = round(주기/마이너), 메이저 = 마이너 × lcm), 메시지 시간 = 20 µs × (1 + 데이터 워드 + 1) + 응답(0.1 µs 단위, 시드 지터 ±2 µs) + 갭. 시뮬레이트 RT 데이터는 메시지별 소스(eu·sine·ramp·const)를 ICD 워드의 scale·offset 역변환으로 비트 패킹. 오류 주입은 N번째 메시지(무응답: 상태어 0 + 활동 비트 제거, 패리티·워드카운트: 오류 비트 + 상태어 MSG ERROR).
- 라이브 캡처: 메모리에서 자라는 `Mil1553File.Live` → ANALYZE 리스트가 100 ms마다 갱신, STOP에서 같은 레코드 형식으로 저장.
- 하드웨어: `IBusDevice` 메서드 순서 = 드라이버 호출 순서(Open: CardOpen→CoreOpen→ChIs1553 · Configure: BCConfig/BCCreateMsg/BCSchedFrame/RTConfig/MonConfig/SeqConfig · Poll: SeqBlkRd→SeqFindNext). 구현은 하드웨어가 있는 날.

## 미확인 (UNVERIFIED)
1553 타이밍은 근사(실카드 Δt로 보정), 캡처 메타 스텁(실 캡처 구조로 교체 예정), 시뮬레이터 프로젝트의 offset/unit 위치.

## 0.1.0-beta.2 — UI feedback (10 items)
ANALYZE: ERR column shows error bits only (HIT is a status flag → STAT column, hidden by default; 19,828 blank = 20,000 − 172 errors, the ERRORS ONLY / statistics definition), absolute time carries an "upper 32 bits not verified" tooltip with relative time as default, one-row filter bar (time display and columns behind the header ⋯ menu, CSV · CSV·EU · STATS on the panel header), BUS TREE navigator (RT → SA → ICD words with message / error counts; RAW toggle lists channels; 7 RT nodes = statistics), bits in 4-bit groups, trend card header (word · unit · LAST 10 s / 60 s / ALL · SEND TO VIEW as CSV). ICD: footer text never hidden, DUMMY only in developer mode (Ctrl+Shift+D), no internal identifiers in the export notes. UI text English throughout (help stays Korean). Fix: an ICD loaded before a capture now attaches to it.
Gates added to `--v100rc` (ui1 … ui10, 12 gates): 42/42 PASS; `--bus` 14/14, `--v0100` 14/14, `--stress 50` 0 exceptions. Before/after captures: `--uishots --prefix before|after`.

## 0.3.0-beta.2 — site profiles, capability-based roles, device-configuration import (2026-10-30)
- `--site --profile <json>`: everything bench-specific (terminal address, bus, frame or ICD file, expected words, counter word) comes from a site profile (docs/site-profile.md, `site-profiles/example.json`); without a profile the self-test only enumerates, opens and reports channel capabilities and role assignment.
- Channel roles in one place (`ChannelRoles`): multi-function channels host the BC with its simulated RTs and its own monitor; single-function channels host the BC only and the BC's transactions come from the message blocks; a second channel is recommended as a bus monitor. PROJECT shows the capability of every channel. Contract tests cover both kinds.
- ICD IMPORT reads a 1553 custom-device configuration XML (remote terminals, subaddresses, mode codes, message buffers) and the matching parameters XML (terminal, subaddress, direction, word count); the two are merged when they sit in the same folder (word count from the parameters file, else the buffer size). `--icdxml` self-test compares such a pair with a reference ICD CSV.
- Mode-code messages validate (no word count; one data word for codes 16..31).

## 0.3.0-beta.1 — first measurements on a real card and a real remote terminal (2026-10-30)
- Single-function channels (one of BC / RT / monitor per channel, ChGetInfo MULTIMODE = 0): RTConfig / MonConfig next to the BC are not attempted (on the measured card the refused call also wiped the BC schedule). BC transactions then come from the message blocks (time tag, status, data of the last transaction per message), polled from a 5 ms thread; records are held back until every message's last tag + period has passed so the capture stays in time order.
- Bus B per message (ICD `bus` column), MONITOR CH project setting (a second channel of the card as a bus monitor), driver version in the device detail, BCSchedFrame / BCSchedMsg return values handled as schedule indices.
- Mode codes: ICD column `mode_code` (16..31 carry one data word), SIMULATE shows `MC16` in the SA column, virtual and hardware devices build the command word (subaddress field 0, word-count field = code).
- `--site` self-test (H0 enumerate/open/configure, H1 frame against the terminal, H2 the other bus and back, H3 long run) with `--icd`, `--retain 0` (records only in the live capture), expected words and a counter word as gates; `--shot --click/--js` hooks.
- Measured: a terminal at address 4 on bus B — 30 s frame 6,896 / 6,900 commands, 100 % response, status word 0x2000, DATA COUNT errors 0, mode code 16 vector word and mode code 17 synchronize answered, 10 min run 120,003 / 120,001 with 63 transactions not read (0.05 %), time tag = microseconds (1.0000–1.0006 × wall clock).

## 0.2.0-beta.2 — repository source fix (2026-10-29)
- Same application as beta.1. The core submodule now locates the .dxd setup element by shape (the exported source could not open .dxd files since rc.2; the shipped binaries were built from the unsanitized tree and were not affected).
- Export tool: code files are no longer rewritten by the name sanitizer — a third-party name in a code file fails the export; a fresh-clone gate (submodule init → web + app build → core tests → `--bus` / `--v094`) runs before every tag.

## 0.2.0-beta.1 — hardware device + driver binding (2026-10-29)
- `HardwareBusDevice` implemented over `IDriverApi` (`NativeDriverApi` binds the driver libraries named in `driver.json` at run time through `NativeLibrary`; nothing vendor-specific in the source or the release). PROJECT / start screen SEARCH HARDWARE enumerates cards when a binding exists; `hw0…` device ids; `BusMessage.SimulateRt`.
- Contract tests (fake driver API, 5): enumerate without binding → empty + no exception; enumerate lists every card the driver opens; the call order is the documented order (open → reset/BC/frame/RT/monitor/sequential → start → poll → stop → close); sequential records decode like virtual-bus records (time tag high word, error bits, counters); the virtual bus uses the same error-bit vocabulary (NO RESPONSE 0x8000 | ANY 0x4000, PARITY 0x4010, DATA COUNT 0x4800).
- On a PC with the driver libraries but no card: all 24 exports bind, CardOpen(0) returns −13 (no card) in 42 ms, the app stays on the virtual bus. **Measured on a real card: not yet** — `--hw` self-test (G1–G7 gates) is the field procedure.
- ICD EDIT: the messages pane is 560 px wide (stylesheet), BUS column inside the pane — gate `u5_icd_bus_column_in_pane` (table scrollWidth ≤ clientWidth).
- Regression: `--bus` 15/15, `--v100rc` 50/50, `--v0100` 14/14, `--stress --iter 50` 0 exceptions / 0 stalls.

## 0.1.0-beta.3 — UI feedback round 2 (8 items)
EXPORT formats as blocks (page-mode CSS), MESSAGE : TREND = 55 : 45 so EU values never clip, ICD EDIT BUS / T·R selects wide enough and shorter PROJECT button labels (IMPORT PROJECT… / EXPORT PROJECT…), the start screen's primary button keeps its on-accent text, English checked on the start screen / about dialog / chrome too, one accent colour in the dark theme (minor-frame cells use the theme accent), SETTINGS as four cards in the PROJECT grid, and the RUN panel shows the expected bus load from the frame table before a run (5.772 % expected vs 5.715 % measured on the 10 s batch, no-response messages carry no data words).
Gates: `--v100rc` 49/49 (7 new ui2_* gates), `--bus` 15/15 (ui2_8), `--v0100` 14/14, `--stress 50` 0 exceptions. Captures: `--uishots2 --prefix before|after`.
