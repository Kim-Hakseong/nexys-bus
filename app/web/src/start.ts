// Start screen: new project / open project / hardware search (disabled until the driver reports a card) / recent projects.
import { bridge } from '@core/bridge';
import { I } from '@core/icons';
import { esc, h } from '@core/util';

export class StartPage {
  el = h(`<section class="start"><div class="start-in"></div></section>`);
  private last = '';
  constructor() {
    this.el.addEventListener('click', e => {
      const t = e.target as HTMLElement; const p = (t.closest('[data-path]') as HTMLElement | null)?.dataset.path; if (p) { bridge.call('project.openPath', { path: p }); return; }
      const a = (t.closest('[data-a]') as HTMLElement | null)?.dataset.a; if (!a) return;
      if (a === 'newProject') bridge.call('project.new', { name: 'New project' }); else if (a === 'openProject') bridge.call('project.open'); else if (a === 'searchHw') bridge.call('devices.search');
    });
  }
  setState(S: any) {
    const recent: any[] = S.recent ?? []; const hw = (S.devices ?? []).some((d: any) => d.kind === 'hardware' && d.available);
    const key = JSON.stringify([recent, hw, S.app?.versionText]); if (key === this.last) return; this.last = key;
    this.el.querySelector('.start-in')!.innerHTML = `<h1>${esc(S.app?.name || 'NexysBus')}<small>MIL-STD-1553 버스 시뮬레이션 · 캡처 분석 · ICD — 가상 버스로 지금 바로, 하드웨어 카드는 드라이버가 있는 PC에서.</small></h1>
      <div class="actions"><button class="pill h32 primary" data-a="newProject">${I.file(15)}NEW PROJECT</button><button class="pill h32" data-a="openProject">${I.folder(15)}OPEN PROJECT…</button><button class="pill h32" data-a="searchHw"${hw ? '' : ' disabled'} title="${hw ? '' : '드라이버 카드가 있는 PC에서 활성화됩니다 (하드웨어 날)'}">${I.search(15)}SEARCH HARDWARE</button><div class="note">${hw ? '하드웨어 카드가 보입니다 — PROJECT에서 고르세요.' : '하드웨어 검색은 드라이버 카드가 있는 PC에서 활성화됩니다 (하드웨어 날). 지금은 가상 버스로 프레임을 만들고 실행해 보세요.'}</div></div>
      <div class="recent"><div class="lblh">RECENT PROJECTS</div>${recent.length ? recent.map(r => `<button data-path="${esc(r.path)}" title="${esc(r.path)}"><b>${esc(r.name)}</b><span>${esc(r.path)}</span></button>`).join('') : '<div class="faint11">아직 없습니다. NEW PROJECT로 시작하고 SAVE로 .nxb 파일을 만드세요.</div>'}</div>`;
  }
}
