// PROJECT rail: name / file / capture folder · device choice (virtual, driver cards) · bus settings · ICD + frame summary with simulator project import/export.
import { bridge } from '@core/bridge';
import { esc, h } from '@core/util';

export class ProjectPage {
  el = h(`<div class="project"></div>`);
  private key = '';
  constructor() {
    this.el.addEventListener('click', async e => {
      const t = e.target as HTMLElement; const dev = (t.closest('[data-dev]') as HTMLElement | null); if (dev && !dev.classList.contains('off')) { bridge.call('project.set', { field: 'device', value: dev.dataset.dev }); return; }
      const a = (t.closest('[data-a]') as HTMLElement | null)?.dataset.a; if (!a) return;
      if (a === 'save') bridge.call('project.save'); else if (a === 'saveAs') bridge.call('project.save', { as: true }); else if (a === 'close') bridge.call('project.close');
      else if (a === 'pickCapture') bridge.call('project.pickCaptureDir'); else if (a === 'icd') bridge.call('rail', { name: 'ICD' }); else if (a === 'simulate') bridge.call('rail', { name: 'SIMULATE' });
      else if (a === 'importSim') bridge.call('project.importSim'); else if (a === 'exportSim') bridge.call('project.exportSim'); else if (a === 'searchHw') bridge.call('devices.search'); else if (a === 'settings') bridge.call('rail', { name: 'SETTINGS' });
    });
    this.el.addEventListener('change', e => {
      const t = e.target as HTMLInputElement; const f = t.dataset.f; if (!f) return;
      const num = ['channel', 'responseTenthUs', 'gapUs', 'seed'].includes(f); const dbl = f === 'minorFrameMs';
      bridge.call('project.set', { field: f, value: num ? parseInt(t.value, 10) || 0 : dbl ? parseFloat(t.value) || 0 : t.value });
    });
  }
  setState(S: any) {
    const p = S.project ?? {}; const devs: any[] = S.devices ?? []; const key = JSON.stringify([p, devs]); if (key === this.key) return; this.key = key;
    const b = p.bus ?? {}; const icd = p.icd ?? {};
    const row = (k: string, body: string) => `<div class="row2"><span class="k">${k}</span>${body}</div>`;
    const inp = (f: string, v: any, cls = 'in', extra = '') => `<input class="${cls}" data-f="${f}" value="${esc(v ?? '')}"${extra}>`;
    this.el.innerHTML = `
      <section class="card"><div class="card-h"><span class="t">PROJECT</span><span class="m">${esc(p.path ? p.path.split(/[\\/]/).pop() : 'unsaved')}</span></div><div class="body-p">
        ${row('NAME', inp('name', p.name))}
        ${row('FILE', `<span class="mono muted11" style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(p.path || '(not saved yet)')}</span><span class="d"><button class="pill h24" data-a="save">SAVE</button> <button class="pill h24" data-a="saveAs">SAVE AS…</button> <button class="pill h24" data-a="close">CLOSE</button></span>`)}
        ${row('CAPTURE FOLDER', inp('captureDir', p.captureDir, 'in', ' placeholder="%LOCALAPPDATA%\\NexysBus\\captures"') + `<button class="pill h24" data-a="pickCapture">PICK…</button>`)}
      </div></section>
      <section class="card"><div class="card-h"><span class="t">DEVICE</span><span class="m">${devs.filter(d => d.available).length} available</span></div><div class="body-p"><div class="dev-list">
        ${devs.map(d => `<div class="dev${d.id === p.device ? ' on' : ''}${d.available ? '' : ' off'}" data-dev="${esc(d.id)}"><span class="ic">${d.kind === 'virtual' ? '▣' : '▢'}</span><div><div class="nm">${esc(d.name)}</div><div class="dt">${esc(d.detail)}</div></div><span class="chip">${d.id === p.device ? 'SELECTED' : d.available ? 'AVAILABLE' : 'UNAVAILABLE'}</span></div>`).join('')}
        ${devs.some(d => d.kind === 'hardware') ? '' : `<div class="dev off" data-dev="hw"><span class="ic">▢</span><div><div class="nm">1553 card (driver)</div><div class="dt">no cards found — hardware support comes with the driver</div></div><span class="chip">UNAVAILABLE</span></div>`}
      </div>${row('', `<button class="pill h24" data-a="searchHw"${devs.some(d => d.kind === 'hardware') ? '' : ' disabled'}>SEARCH HARDWARE</button><span class="d">Enabled on a PC with a driver card</span>`)}</div></section>
      <section class="card"><div class="card-h"><span class="t">BUS</span><span class="m">channel ${b.channel ?? 0} · 1553B</span></div><div class="body-p">
        ${row('CHANNEL', `<select class="sel" data-f="channel">${[0, 1, 2, 3].map(c => `<option value="${c}"${b.channel === c ? ' selected' : ''}>${c}</option>`).join('')}</select><span class="d">Card channel (the virtual bus has one)</span>`)}
        ${row('MINOR FRAME', inp('minorFrameMs', b.minorFrameMs || '', 'in w80', ' placeholder="auto"') + `<span class="muted11">ms</span><span class="d">Empty = shortest message period</span>`)}
        ${row('RT RESPONSE', inp('responseTenthUs', b.responseTenthUs, 'in w80') + `<span class="muted11">× 0.1 µs</span><span class="d">8–12 µs response delay</span>`)}
        ${row('GAP', inp('gapUs', b.gapUs, 'in w80') + `<span class="muted11">µs</span><span class="d">Inter-message gap</span>`)}
        ${row('SEED', inp('seed', b.seed, 'in w80') + `<span class="d">Response-time jitter seed</span>`)}
      </div></section>
      <section class="card"><div class="card-h"><span class="t">ICD · FRAME</span><span class="m">${esc(icd.source || 'no ICD')}</span></div><div class="body-p">
        <div class="stat-row"><div><div class="k">MESSAGES</div><div class="v">${icd.messages ?? 0}</div></div><div><div class="k">WORDS</div><div class="v">${icd.words ?? 0}</div></div><div><div class="k">ERRORS</div><div class="v${icd.errors ? ' errv' : ''}">${icd.errors ?? 0}</div></div></div>
        ${row('', `<button class="pill h24 primary" data-a="icd">ICD →</button><button class="pill h24" data-a="simulate">SIMULATE →</button><button class="pill h24" data-a="importSim" title="Import a simulator project (compound document)">IMPORT PROJECT…</button><button class="pill h24" data-a="exportSim"${icd.messages ? '' : ' disabled'} title="Export as a simulator project (template folder from SETTINGS)">EXPORT PROJECT…</button>`)}
        ${row('TEMPLATE', `<span class="mono muted11" style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(p.templateDir || '(none — project files are skipped on export)')}</span><span class="d"><button class="pill h24" data-a="settings">SETTINGS</button></span>`)}
      </div></section>`;
  }
  testApi() {
    const self = this;
    return {
      async setField(f: string, v: string) { const inp = self.el.querySelector(`[data-f="${f}"]`) as HTMLInputElement; if (!inp) throw new Error('no field ' + f); inp.value = v; inp.dispatchEvent(new Event('change', { bubbles: true })); await new Promise(r => setTimeout(r, 150)); return true; },
      devices: () => Array.from(self.el.querySelectorAll<HTMLElement>('[data-dev]')).map(d => ({ id: d.dataset.dev, on: d.classList.contains('on'), off: d.classList.contains('off') })),
    };
  }
}
