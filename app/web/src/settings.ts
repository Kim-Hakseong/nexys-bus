// SETTINGS rail (a page, not a dialog): appearance, bus view, project folders (template for simulator exports), profile.
import { bridge } from '@core/bridge';
import { esc, h } from '@core/util';

export class SettingsPage {
  el = h(`<div class="settings-page"><div class="settings"></div></div>`);
  private key = '';
  constructor() {
    this.el.addEventListener('click', e => {
      const b = (e.target as HTMLElement).closest('button') as HTMLElement | null; if (!b) return; const d = b.dataset;
      if (d.theme) bridge.call('setTheme', { theme: d.theme }); else if (d.plot) bridge.call('setPlot', { plot: d.plot }); else if (d.bt) bridge.call('m1553.busTime', { mode: d.bt });
      else if (d.a === 'pickTemplate') bridge.call('icd.pickTemplate'); else if (d.a === 'openProfile') bridge.call('openProfileFolder'); else if (d.a === 'pickCapture') bridge.call('project.pickCaptureDir');
    });
    this.el.addEventListener('change', e => { const t = e.target as HTMLInputElement; if (t.dataset.f === 'icdTemplateDir') bridge.call('setFeature', { name: 'icdTemplateDir', value: t.value }); if (t.dataset.f === 'captureDir') bridge.call('project.set', { field: 'captureDir', value: t.value }); });
  }
  setState(S: any) {
    const key = JSON.stringify([S.theme, S.plot, S.busTime, S.mil1553?.templateDir, S.project?.captureDir, S.sim?.captureDir]); if (key === this.key) return; this.key = key;
    const pill = (attr: string, label: string, on: boolean) => `<button class="pill h24${on ? ' on' : ''}" ${attr}>${label}</button>`; const bt = S.busTime ?? 'rel';
    (this.el.querySelector('.settings') as HTMLElement).innerHTML = `<div class="lblh">SETTINGS</div>
      <div class="sec"><h4>APPEARANCE</h4><div class="row">Chrome theme ${pill('data-theme="light"', 'LIGHT', S.theme === 'light')}${pill('data-theme="dark"', 'DARK', S.theme === 'dark')}<span class="d">Ctrl+T</span></div><div class="row">Plot theme ${pill('data-plot="light"', 'LIGHT', S.plot === 'light')}${pill('data-plot="dark"', 'DARK', S.plot === 'dark')}<span class="d">Ctrl+P</span></div></div>
      <div class="sec"><h4>ANALYZE</h4><div class="row">Time display ${pill('data-bt="rel"', 'RELATIVE s', bt === 'rel')}${pill('data-bt="abs"', 'ABSOLUTE µs', bt === 'abs')}${pill('data-bt="delta"', 'DELTA', bt === 'delta')}<span class="d">TIME column of the list (absolute µs: upper 32 bits not verified yet)</span></div></div>
      <div class="sec"><h4>PROJECT</h4><div class="row">Template project <input class="in mono" data-f="icdTemplateDir" value="${esc(S.mil1553?.templateDir || '')}" placeholder="Simulator 1553 template project folder (a template with RT/SA nodes)" spellcheck="false"><button class="pill h24" data-a="pickTemplate">PICK…</button></div>
        <div class="row">Capture folder <input class="in mono" data-f="captureDir" value="${esc(S.project?.captureDir || '')}" placeholder="${esc(S.sim?.captureDir || '%LOCALAPPDATA%\\NexysBus\\captures')}" spellcheck="false"><button class="pill h24" data-a="pickCapture">PICK…</button></div></div>
      <div class="sec"><h4>PROFILE</h4><div class="row">Settings file <span class="mono muted11">%LOCALAPPDATA%\\NexysBus\\settings.json</span><button class="pill h24" data-a="openProfile">OPEN FOLDER</button></div><div class="row">Samples <span class="mono muted11">…\\NexysBus\\sample\\</span><span class="d">Sample1553.mon · Sample429.mon (ANALYZE empty state)</span></div></div>`;
  }
}
