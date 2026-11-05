// Help guide (F1 / ? button): sectioned user guide modal for NexysBus.
import { bridge } from '@core/bridge';
import { I } from '@core/icons';
import { esc, h } from '@core/util';

const SECTIONS: { id: string; label: string; html: (app: any) => string }[] = [
  { id: 'start', label: '시작하기', html: app => `
    <h3>NexysBus에 오신 것을 환영합니다!</h3>
    <p>NexysBus는 MIL-STD-1553 버스를 <b>소프트웨어로 실행</b>하고(BC 프레임 + 시뮬레이트 RT + 모니터), 캡처를 <b>분석</b>하고, <b>ICD</b>를 다루는 도구입니다. 하드웨어 카드 없이도 가상 버스로 전부 돌려볼 수 있고, 카드는 드라이버가 있는 PC에서 같은 화면으로 붙습니다.</p>
    <h4>5분 경로</h4>
    <ol><li>시작 화면 <b>NEW PROJECT</b> → PROJECT에서 이름·장치(Virtual bus)·버스 설정.</li><li><b>ICD</b> 레일에서 ICD(CSV·XLSX·허브 JSON·메시지 XML)를 가져오거나 DUMMY로 시작 → 프레임이 만들어집니다.</li><li><b>SIMULATE</b>에서 주기·순서·RT 데이터 소스·오류 주입을 정하고 <b>START</b>. 리스트는 ANALYZE에 실시간으로 쌓입니다.</li><li><b>STOP</b>하면 캡처(.mon)가 저장되고, ANALYZE에서 디코드·통계·CSV.</li></ol>
    <div class="help-note">문제나 제안은 <a href="#" data-url="${esc(app.issues || '')}">GitHub Issues</a>에 남겨 주세요. 저장소: <a href="#" data-url="${esc(app.repo || '')}">${esc(app.repo || '')}</a></div>` },
  { id: 'project', label: 'PROJECT', html: () => `
    <h3>PROJECT — 장치 · 버스 · 파일</h3>
    <div class="help-card"><b>장치</b><p>Virtual bus는 항상 있습니다. 드라이버 카드는 카드가 있는 PC에서 검색되고, 없으면 비활성으로 보입니다.</p></div>
    <div class="help-card"><b>버스 설정</b><p>MINOR FRAME(비우면 가장 짧은 메시지 주기), RT RESPONSE(0.1 µs 단위, 기본 10 µs), GAP, SEED(응답 시간 지터).</p></div>
    <div class="help-card"><b>파일</b><p>프로젝트는 <code>.nxb</code>(JSON)로 저장됩니다: 장치·버스·ICD·프레임·데이터 소스·오류 주입이 전부 들어 있어요. 시뮬레이터 프로젝트(복합문서)는 IMPORT로 메시지·필드를 읽어 오고, EXPORT는 설정의 템플릿 프로젝트를 복제해 씁니다.</p></div>` },
  { id: 'simulate', label: 'SIMULATE', html: () => `
    <h3>SIMULATE — 프레임 편집과 실행</h3>
    <p><b>FRAME</b> 표에서 순서(▲▼), ON, 주기(ms)를 바꿉니다. EVERY는 그 메시지가 몇 번째 마이너 프레임마다 나가는지(주기 ÷ 마이너), 아래 격자는 메이저 프레임 안의 배치입니다.</p>
    <p><b>SIMULATED RT DATA</b>: 메시지(서브어드레스)마다 데이터 소스 — eu(각 워드 범위의 사인, ICD scale·offset 역변환) · sine · ramp · const. 디코드 EU가 입력 파형과 같아야 정상입니다.</p>
    <p><b>RUN</b>: START/STOP(실시간, 100 ms마다 수집), 10 s BATCH(가상 시계로 즉시 생성). ERROR INJECTION은 N번째 메시지마다 무응답·패리티·워드카운트 오류를 넣습니다. 통계의 BUS LOAD는 (명령어+상태어+데이터) × 20 µs 근사입니다.</p>` },
  { id: 'analyze', label: 'ANALYZE', html: () => `
    <h3>ANALYZE — MIL-STD-1553 / ARINC 429 캡처</h3>
    <p>실행 중인 캡처와 저장된 <code>.mon</code>이 CAPTURE 칩으로 나란히 보입니다. 100만 건도 그대로 스크롤되고, 열 머리글을 누르면 정렬, COLUMNS로 열을 고릅니다. TIME은 상대 초 / 절대 µs / Δ.</p>
    <h4>필터</h4><p>RT·SA, T/R, 버스 A/B, ERRORS ONLY, TEXT(hex·ICD 이름·"RT3")를 조합하고 PRESET으로 저장합니다. 429는 LABEL(옥탈)·SDI·SSM·패리티.</p>
    <h4>디코드·추이·통계</h4><p>행을 고르면 명령어·상태어·오류·활동 비트와 데이터 워드가 보이고, ICD가 있으면 워드 이름과 EU 값이 함께 나옵니다. ▸ trend 또는 Enter로 추이 차트, STATS로 메시지 수·RT별 분포·오류·버스 로드, CSV / CSV·EU로 내보내기.</p>
    <table class="help-keys">${[['행 이동', '↑ ↓ · PgUp PgDn · Home End'], ['첫 ICD 워드 추이', 'Enter'], ['선택 행 복사', 'Ctrl + C']].map(r => `<tr><td>${r[0]}</td><td><kbd>${r[1]}</kbd></td></tr>`).join('')}</table>` },
  { id: 'icd', label: 'ICD', html: () => `
    <h3>ICD — 가져오기 · 편집 · 내보내기</h3>
    <h4>IMPORT</h4><p>CSV·XLSX(첫 시트)는 워드당 1행. 열 이름을 자동으로 알아보고(message·rt·sa·tr·wc·period_ms·bus·word·bit_start·bit_len·type·scale·offset·unit·name), 못 알아본 열은 직접 고릅니다. 허브 JSON과 메시지 XML은 바로 열립니다. 가져온 ICD는 곧바로 프로젝트의 프레임이 됩니다.</p>
    <h4>EDIT</h4><p>메시지·워드 표에서 바로 고치고 추가·삭제합니다. RT·SA 0~31, WC 1~32, 비트 겹침은 error(내보내기 차단), 중복 RT/SA/T-R은 warning.</p>
    <h4>EXPORT</h4><p>시뮬레이터 프로젝트(설정의 템플릿 프로젝트 복제) · 메시지 XML(XSD 검사 + 재판독) · 커스텀 디바이스 XML · 자동화 스크립트. 허브 JSON/CSV는 항상 함께 저장됩니다.</p>` },
  { id: 'afdx', label: 'ARINC 664', html: () => `
    <h3>ARINC 664 (AFDX) — 캡처 · 검사 · 송신 · 상호운용</h3>
    <p>시작 화면 <b>NEW ARINC 664 PROJECT</b>(또는 PROJECT › PROTOCOL)로 만들면 ICD · SIMULATE · ANALYZE가 664 화면으로 바뀌고 <b>INTEROP</b> 레일이 생깁니다. <code>.pcapng</code>/<code>.pcap</code>을 열면 자동으로 664 프로젝트가 됩니다.</p>
    <div class="help-card"><b>ICD (허브 스키마 1.1)</b><p>VL(vl_id · BAG 1~128 ms의 2^k · Lmax 64~1518 B · 네트워크 A/B/AB) → 포트(sub-VL · sampling/queuing · IP/UDP · 페이로드 크기) → 파라미터(바이트 offset · u8~f64 · endian · scale · offset_val · unit). CSV/XLSX는 파라미터당 1행(열 이름 자동 인식).</p></div>
    <div class="help-card"><b>장치</b><p>Virtual network는 항상 있습니다(A/B 루프백, 지연·지터·B 스큐·손실·중복 주입). <b>Shared virtual network</b>는 이 PC의 다른 NexysBus 창과 프레임을 주고받습니다(한 창 SIMULATE, 다른 창 ANALYZE › LISTEN — 두 인스턴스 상호운용). 이더넷 포트는 패킷 캡처 라이브러리(Npcap, 별도 설치 — NexysBus에 포함되지 않음)가 있을 때만 PROJECT › NETWORK A/B에서 고릅니다. 전용 AFDX 카드는 자리만 있습니다.</p></div>
    <div class="help-card"><b>SIMULATE</b><p>VL마다 BAG마다 한 프레임, 서브 VL 라운드로빈(큐잉 포트의 큰 메시지는 IP 단편으로 한 조각씩), 이중화 VL은 A·B 동시, SN 자동(시작 0, 이후 1~255). RUN 표의 MEAN/MIN/MAX는 실제 송신 간격, JITTER는 예정 시각 대비 편차입니다. SENDER FAULTS: SN 건너뜀 · A/B 누락 · Lmax 초과 · BAG 위반(0.1 ms 뒤 추가 프레임) · 단편 누락.</p></div>
    <div class="help-card"><b>ANALYZE 검사 규칙</b><p>무결성: SN 0 = 리셋, 다음 = 이전+1(1~255 순환), 1~127 앞서면 그만큼 손실, 그 외 역순·중복(폐기). BAG: 같은 네트워크 간격 &lt; BAG − Jmax(0.5 ms). Lmax: FCS 포함 길이 &gt; Lmax. RM: VL·SN이 같은 A/B 사본 중 먼저 온 유효 프레임 채택, 다른 쪽은 중복이며 그 지연이 스큐, 짝이 창(20 ms) 안에 없으면 그 네트워크 누락. REASM: 단편 메시지 조각 누락·역순. SUB-VL ORDER: 서브 VL 라운드로빈 순서 깨짐. <b>VL FROM</b>: 표준 MAC이 아닌 프로그램은 + IP 224.224.x.x · + ICD UDP ports · UDP MAP(포트=VL)으로 VL을 알아냅니다. <b>● LISTEN</b>: 받기만.</p></div>
    <div class="help-card"><b>INTEROP</b><p>상대 → NexysBus: 캡처를 열면 VL 수신 · ICD 밖 VL · SN · BAG · Lmax · A/B · 스큐 · 디코드 항목이 PASS/FAIL로 판정됩니다. NexysBus → 상대: 보낸 프레임 수 옆에 상대가 보고한 수신 수를 적거나 IMPORT PEER CSV…로 가져오면 판정. FAIL의 EVIDENCE 링크 = 근거 프레임(ANALYZE로 이동, Wireshark No.와 같음). WIRESHARK PROFILE…: 이 ICD의 Wireshark 열·색·파라미터 디코드. EXPORT REPORT…로 interop-report.md. TCP 링크 관찰은 LinkTap으로.</p></div>` },
  { id: 'keys', label: '단축키', html: () => `
    <h3>키보드 단축키</h3>
    <table class="help-keys">${[['PROJECT / ANALYZE / SIMULATE / ICD / SETTINGS', 'Ctrl + 1 · 2 · 3 · 4 · 5'], ['INTEROP (ARINC 664 프로젝트)', 'Ctrl + 6'], ['열기 (프로젝트 · 캡처 · ICD)', 'Ctrl + O'], ['프로젝트 저장', 'Ctrl + S'], ['테마 / 플롯 밝기', 'Ctrl + T / Ctrl + P'], ['도움말 / 닫기', 'F1 / Esc']].map(([a, b]) => `<tr><td>${a}</td><td><kbd>${b}</kbd></td></tr>`).join('')}</table>` },
  { id: 'tips', label: '활용 팁', html: () => `
    <h3>활용 팁</h3>
    <div class="help-card"><b>정답지 만들기</b><p>10 s BATCH는 가상 시계라 메시지 수가 정확히 Σ ⌈10 s / 주기⌉ 입니다. 디코드 EU와 입력 파형의 차이는 scale/2 이하여야 해요.</p></div>
    <div class="help-card"><b>하드웨어 날</b><p>드라이버 카드가 보이면 PROJECT에서 고르고 같은 프레임을 그대로 실행합니다. 캡처 형식과 ANALYZE는 동일합니다.</p></div>
    <div class="help-card"><b>파일 드롭</b><p>.nxb · .mon · 시뮬레이터 프로젝트(.CPJ/.CDV) · ICD(.csv/.xlsx/.json/.xml)를 창 어디에나 끌어다 놓으면 알맞은 레일로 열립니다.</p></div>` },
];

export class Help {
  private S: any = null;
  setState(s: any) { this.S = s; }
  show(section = 'start') {
    document.querySelector('.help-bg')?.remove();
    const app = this.S?.app ?? {};
    const dlg = h(`<div class="modal-bg help-bg"><div class="modal help" role="dialog" aria-label="사용 가이드"><div class="help-h"><b>${esc(app.name || 'NexysBus')} 사용 가이드</b><button class="xbtn" data-h="close" aria-label="닫기">${I.close()}</button></div>
      <div class="help-body"><nav class="help-nav" role="tablist">${SECTIONS.map(s => `<button role="tab" data-s="${s.id}">${s.label}</button>`).join('')}<span class="help-ver">${esc(app.versionText || '')}</span></nav><div class="help-pane"></div></div></div></div>`);
    const pane = dlg.querySelector('.help-pane') as HTMLElement;
    const select = (id: string) => { const s = SECTIONS.find(x => x.id === id) ?? SECTIONS[0]; pane.innerHTML = s.html(app); pane.scrollTop = 0; dlg.querySelectorAll('[data-s]').forEach(b => b.classList.toggle('on', (b as HTMLElement).dataset.s === s.id)); };
    dlg.addEventListener('click', e => {
      const t = e.target as HTMLElement; const u = (t.closest('[data-url]') as HTMLElement | null)?.dataset.url; if (u) { e.preventDefault(); bridge.call('openUrl', { url: u }); return; }
      const s = (t.closest('[data-s]') as HTMLElement | null)?.dataset.s; if (s) { select(s); return; }
      if ((t.closest('[data-h]') as HTMLElement | null)?.dataset.h === 'close' || t === dlg) dlg.remove();
    });
    document.body.append(dlg); select(section);
  }
}
