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
    this.el.querySelector('.start-in')!.innerHTML = `<h1>${esc(S.app?.name || 'NexysBus')}<small>MIL-STD-1553 bus simulation · capture analysis · ICD — on the virtual bus right now, on a hardware card where the driver is installed.</small></h1>
      <div class="actions"><button class="pill h32 primary" data-a="newProject">${I.file(15)}NEW PROJECT</button><button class="pill h32" data-a="openProject">${I.folder(15)}OPEN PROJECT…</button><button class="pill h32" data-a="searchHw"${hw ? '' : ' disabled'} title="${hw ? '' : 'Enabled on a PC with a driver card'}">${I.search(15)}SEARCH HARDWARE</button><div class="note">${hw ? 'Hardware cards found — pick one in PROJECT.' : 'Hardware search is enabled on a PC with a driver card. Build and run a frame on the virtual bus meanwhile.'}</div></div>
      <div class="recent"><div class="lblh">RECENT PROJECTS</div>${recent.length ? recent.map(r => `<button data-path="${esc(r.path)}" title="${esc(r.path)}"><b>${esc(r.name)}</b><span>${esc(r.path)}</span></button>`).join('') : '<div class="faint11">None yet — start with NEW PROJECT and SAVE it as an .nxb file.</div>'}</div>`;
  }
}
