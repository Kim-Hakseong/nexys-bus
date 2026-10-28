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
  { id: 'keys', label: '단축키', html: () => `
    <h3>키보드 단축키</h3>
    <table class="help-keys">${[['PROJECT / ANALYZE / SIMULATE / ICD / SETTINGS', 'Ctrl + 1 · 2 · 3 · 4 · 5'], ['열기 (프로젝트 · 캡처 · ICD)', 'Ctrl + O'], ['프로젝트 저장', 'Ctrl + S'], ['테마 / 플롯 밝기', 'Ctrl + T / Ctrl + P'], ['도움말 / 닫기', 'F1 / Esc']].map(([a, b]) => `<tr><td>${a}</td><td><kbd>${b}</kbd></td></tr>`).join('')}</table>` },
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
