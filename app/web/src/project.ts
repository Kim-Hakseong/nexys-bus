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
      else if (a === 'icd664') bridge.call('rail', { name: 'ICD' }); else if (a === 'interop') bridge.call('rail', { name: 'INTEROP' });
      else if (a === 'importSim') bridge.call('project.importSim'); else if (a === 'exportSim') bridge.call('project.exportSim'); else if (a === 'searchHw') bridge.call('devices.search'); else if (a === 'settings') bridge.call('rail', { name: 'SETTINGS' });
    });
    this.el.addEventListener('change', e => {
      const t = e.target as HTMLInputElement; if (t.dataset.nic) { bridge.call('afdx.set', { field: t.dataset.nic, value: t.value }); return; } const f = t.dataset.f; if (!f) return;
      const num = ['channel', 'monitorChannel', 'responseTenthUs', 'gapUs', 'seed'].includes(f); const dbl = f === 'minorFrameMs';
      bridge.call('project.set', { field: f, value: num ? parseInt(t.value, 10) || 0 : dbl ? parseFloat(t.value) || 0 : t.value });
    });
  }
  setState(S: any) {
    const p = S.project ?? {}; const devs: any[] = S.devices ?? []; const drv: any = S.driver ?? { bound: false, path: '', searched: false, cards: 0 }; const key = JSON.stringify([p, devs, drv, S.afdx?.settings, S.afdx?.icd, this.ports]); if (key === this.key) return; this.key = key;
    if (p.protocol === 'arinc664') { this.render664(S, p, devs); return; }
    const b = p.bus ?? {}; const icd = p.icd ?? {};
    const row = (k: string, body: string) => `<div class="row2"><span class="k">${k}</span>${body}</div>`;
    const inp = (f: string, v: any, cls = 'in', extra = '') => `<input class="${cls}" data-f="${f}" value="${esc(v ?? '')}"${extra}>`;
    this.el.innerHTML = `
      <section class="card"><div class="card-h"><span class="t">PROJECT</span><span class="m">${esc(p.path ? p.path.split(/[\\/]/).pop() : 'unsaved')}</span></div><div class="body-p">
        ${row('NAME', inp('name', p.name))}
        ${row('PROTOCOL', this.protoSel(p))}
        ${row('FILE', `<span class="mono muted11" style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(p.path || '(not saved yet)')}</span><span class="d"><button class="pill h24" data-a="save">SAVE</button> <button class="pill h24" data-a="saveAs">SAVE AS…</button> <button class="pill h24" data-a="close">CLOSE</button></span>`)}
        ${row('CAPTURE FOLDER', inp('captureDir', p.captureDir, 'in', ' placeholder="%LOCALAPPDATA%\\NexysBus\\captures"') + `<button class="pill h24" data-a="pickCapture">PICK…</button>`)}
      </div></section>
      <section class="card"><div class="card-h"><span class="t">DEVICE</span><span class="m">${devs.filter(d => d.available).length} available</span></div><div class="body-p"><div class="dev-list">
        ${devs.map(d => `<div class="dev${d.id === p.device ? ' on' : ''}${d.available ? '' : ' off'}" data-dev="${esc(d.id)}"><span class="ic">${d.kind === 'virtual' ? '▣' : '▢'}</span><div><div class="nm">${esc(d.name)}</div><div class="dt">${esc(d.detail)}</div>${d.channels ? `<div class="dt">${d.channels.filter((c: any) => c.is1553).map((c: any) => `ch${c.channel}: ${esc(c.text)}`).join(' · ')}${d.channels.some((c: any) => c.is1553 && !c.multiMode) ? ' — BC and bus monitor need two channels (MONITOR CH)' : ''}</div>` : ''}</div><span class="chip">${d.id === p.device ? 'SELECTED' : d.available ? 'AVAILABLE' : 'UNAVAILABLE'}</span></div>`).join('')}
        ${devs.some(d => d.kind === 'hardware') ? '' : `<div class="dev off" data-dev="hw"><span class="ic">▢</span><div><div class="nm">1553 card (driver)</div><div class="dt">${drv.bound ? (drv.searched ? 'the driver reports no card — check the card, then SEARCH HARDWARE again' : 'driver binding found — press SEARCH HARDWARE') : 'no driver binding on this PC (driver.json next to the exe or in %LOCALAPPDATA%\\NexysBus)'}</div></div><span class="chip">UNAVAILABLE</span></div>`}
      </div>${row('', `<button class="pill h24" data-a="searchHw"${drv.bound ? '' : ' disabled'}>SEARCH HARDWARE</button><span class="d">${drv.bound ? esc('Binding: ' + drv.path) : 'Enabled when a driver binding (driver.json) is present'}</span>`)}</div></section>
      <section class="card"><div class="card-h"><span class="t">BUS</span><span class="m">channel ${b.channel ?? 0} · 1553B</span></div><div class="body-p">
        ${row('CHANNEL', `<select class="sel" data-f="channel">${[0, 1, 2, 3].map(c => `<option value="${c}"${b.channel === c ? ' selected' : ''}>${c}</option>`).join('')}</select><span class="d">Card channel (the virtual bus has one)</span>`)}
        ${row('MONITOR CH', `<select class="sel" data-f="monitorChannel">${[-1, 0, 1, 2, 3].map(c => `<option value="${c}"${(b.monitorChannel ?? -1) === c ? ' selected' : ''}>${c < 0 ? 'none' : c}</option>`).join('')}</select><span class="d">Second card channel wired to the same bus, run as a bus monitor (single-function cards: the BC channel cannot monitor itself)</span>`)}
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
  private ports: any = null;
  private protoSel(p: any) { return `<select class="sel" data-f="protocol"><option value="mil1553"${p.protocol !== 'arinc664' ? ' selected' : ''}>MIL-STD-1553</option><option value="arinc664"${p.protocol === 'arinc664' ? ' selected' : ''}>ARINC 664 (AFDX)</option></select><span class="d">Switching keeps the ICD hub; each protocol keeps its own settings</span>`; }
  private render664(S: any, p: any, devs: any[]) {
    const a = S.afdx ?? {}; const st = a.settings ?? {}; const icd = a.icd ?? {};
    if (!this.ports) bridge.call('afdx.ports').then(r => { this.ports = r; this.key = ''; this.setState(S); });
    const ports: any[] = this.ports?.ports ?? [];
    const row = (k: string, body: string) => `<div class="row2"><span class="k">${k}</span>${body}</div>`;
    const inp = (f: string, v: any, cls = 'in', extra = '') => `<input class="${cls}" data-f="${f}" value="${esc(v ?? '')}"${extra}>`;
    const nicSel = (f: string, v: string) => `<select class="sel" data-nic="${f}"${ports.length ? '' : ' disabled'}><option value="">— not used —</option>${ports.map(x => `<option value="${esc(x.id)}"${x.id === v ? ' selected' : ''}>${esc(x.name)}</option>`).join('')}</select>`;
    this.el.innerHTML = `
      <section class="card"><div class="card-h"><span class="t">PROJECT</span><span class="m">${esc(p.path ? p.path.split(/[\\/]/).pop() : 'unsaved')}</span></div><div class="body-p">
        ${row('NAME', inp('name', p.name))}
        ${row('PROTOCOL', this.protoSel(p))}
        ${row('FILE', `<span class="mono muted11" style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${esc(p.path || '(not saved yet)')}</span><span class="d"><button class="pill h24" data-a="save">SAVE</button> <button class="pill h24" data-a="saveAs">SAVE AS…</button> <button class="pill h24" data-a="close">CLOSE</button></span>`)}
        ${row('CAPTURE FOLDER', inp('captureDir', p.captureDir, 'in', ' placeholder="%LOCALAPPDATA%\\NexysBus\\captures"') + `<button class="pill h24" data-a="pickCapture">PICK…</button>`)}
      </div></section>
      <section class="card"><div class="card-h"><span class="t">DEVICE</span><span class="m">${devs.filter(d => d.available).length} available</span></div><div class="body-p"><div class="dev-list">
        ${devs.map(d => `<div class="dev${d.id === p.device ? ' on' : ''}${d.available ? '' : ' off'}" data-dev="${esc(d.id)}"><span class="ic">${d.kind === 'virtual' ? '▣' : '▢'}</span><div><div class="nm">${esc(d.name)}</div><div class="dt">${esc(d.detail)}</div></div><span class="chip">${d.id === p.device ? 'SELECTED' : d.available ? 'AVAILABLE' : 'UNAVAILABLE'}</span></div>`).join('')}
      </div></div></section>
      <section class="card"><div class="card-h"><span class="t">NETWORK</span><span class="m">${this.ports?.available ? 'packet-capture library: ' + esc(this.ports.library) : 'no packet-capture library — virtual network only'}</span></div><div class="body-p">
        ${row('NETWORK A', nicSel('nicA', st.nicA ?? '') + `<span class="d">Ethernet port of network A</span>`)}
        ${row('NETWORK B', nicSel('nicB', st.nicB ?? '') + `<span class="d">Port of network B (the same port = frames told apart by source MAC)</span>`)}
        ${row('CHECKS', `<span class="muted11">Jmax ${st.jmaxMs ?? 0.5} ms · skew limit ${st.skewMaxMs ?? 10} ms · pair window ${st.pairWindowMs ?? 20} ms</span><span class="d"><button class="pill h24" data-a="interop">INTEROP →</button></span>`)}
      </div></section>
      <section class="card"><div class="card-h"><span class="t">ICD · VIRTUAL LINKS</span><span class="m">${esc(icd.source || 'no ICD')}</span></div><div class="body-p">
        <div class="stat-row"><div><div class="k">VLs</div><div class="v">${icd.vls ?? 0}</div></div><div><div class="k">PORTS</div><div class="v">${icd.ports ?? 0}</div></div><div><div class="k">PARAMS</div><div class="v">${icd.params ?? 0}</div></div><div><div class="k">ERRORS</div><div class="v${icd.errors ? ' errv' : ''}">${icd.errors ?? 0}</div></div></div>
        ${row('', `<button class="pill h24 primary" data-a="icd664">ICD →</button><button class="pill h24" data-a="simulate">SIMULATE →</button>`)}
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
