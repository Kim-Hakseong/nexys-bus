// NexysBus shell: header (brand · project chip · tools · LED) / left rail (PROJECT · ANALYZE · SIMULATE · ICD · SETTINGS) / page / footer.
// State fan-out from the host snapshot (camelCase JSON), keyboard, file drop, test hooks (window.nx.test.*).
import { bridge } from '@core/bridge';
import { I } from '@core/icons';
import { State } from '@core/types';
import { esc, h } from '@core/util';
import { BusMode } from './analyze';
import { IcdPage } from './icd';
import { Help } from './help';
import { ProjectPage } from './project';
import { SettingsPage } from './settings';
import { SimulatePage } from './simulate';
import { StartPage } from './start';

export type BusState = State & { rail: string; project: any; sim: any; devices: any[]; recent: any[]; app?: any };
let S: BusState | null = null;
const app = document.getElementById('app')!;
const hdr = h(`<header class="hdr"></header>`);
const body = h(`<div class="body"></div>`);
const rail = h(`<nav class="rail" aria-label="Sections"></nav>`);
const main = h(`<main class="main"></main>`);
const ftr = h(`<footer class="ftr"><span class="d"></span><span class="txt"></span><span class="r"></span></footer>`);
body.append(rail, main); app.append(hdr, body, ftr);
const help = new Help();
const start = new StartPage(); const project = new ProjectPage(); const analyze = new BusMode(); const simulate = new SimulatePage(); const icd = new IcdPage(); const settings = new SettingsPage();
const RAILS: { id: string; label: string; icon: string; key: string }[] = [
  { id: 'PROJECT', label: 'PROJECT', icon: I.file(18), key: '1' }, { id: 'ANALYZE', label: 'ANALYZE', icon: I.table(18), key: '2' }, { id: 'SIMULATE', label: 'SIMULATE', icon: I.lineC(18), key: '3' },
  { id: 'ICD', label: 'ICD', icon: I.list(18), key: '4' }, { id: 'SETTINGS', label: 'SETTINGS', icon: I.sliders(18), key: '5' }];

function renderHeader() {
  if (!S) return; const a = S.app ?? {}; const p = S.project ?? {};
  const tool = (act: string, icon: string, label: string, title: string, disabled = false) => `<button type="button" class="tool" data-a="${act}" aria-label="${label}" title="${title}"${disabled ? ' disabled' : ''}>${icon}<span>${label}</span></button>`;
  const chip = p.name && S.rail !== 'START' ? `<span class="proj" title="${esc(p.path || '(unsaved)')}">${esc(p.name)} <span class="dim">${esc(p.path ? p.path.split(/[\\/]/).pop() : 'unsaved')}${p.dirty ? ' · modified' : ''}</span></span>` : `<span class="proj">No project <span class="dim">—</span></span>`;
  hdr.innerHTML = `<div class="hdr-brand"><div class="hdr-logo" data-a="about" title="About NexysBus (F1 = help)"><img src="brand/Logo.png" alt="Nexys"></div><div class="hdr-name"><b>${esc(a.name || 'NexysBus')}</b><small>${esc((a.versionText || '').toUpperCase())}</small></div></div>
    <div class="hdr-tools">${chip}<div class="vsep"></div>${tool('open', I.folder(), 'OPEN', 'Open project / capture / ICD (Ctrl+O)')}${tool('save', I.save(), 'SAVE', 'Save project (Ctrl+S)', !p.name)}<div class="vsep"></div>${tool('theme', S.theme === 'dark' ? I.sun() : I.theme(), 'THEME', 'Light / dark (Ctrl+T)')}${tool('plot', I.plot(), 'PLOT · ' + S.plot.toUpperCase(), 'Plot light / dark (Ctrl+P)')}<div class="vsep"></div><button type="button" class="tool icon" data-a="help" aria-label="HELP" title="사용 가이드 (F1)">${I.help()}</button>
    <div class="led-pill"><span class="led ${S.status.led}"></span><span class="led-text">${S.status.led.toUpperCase()}</span></div></div>`;
}
function renderRail() {
  if (!S) return;
  rail.innerHTML = `<button type="button" class="home" data-home title="Start">${I.grip(16)}</button>` + RAILS.map(r => `<button type="button" data-rail="${r.id}" class="${S!.rail === r.id ? 'on' : ''}" title="${r.label} (Ctrl+${r.key})"${r.id === 'SETTINGS' ? ' style="margin-top:auto"' : ''}>${r.icon}<span>${r.label}</span></button>`).join('');
}
rail.addEventListener('click', e => { const t = e.target as HTMLElement; if (t.closest('[data-home]')) { setRail('START'); return; } const r = (t.closest('[data-rail]') as HTMLElement | null)?.dataset.rail; if (r) setRail(r); });
function setRail(r: string) { bridge.call('rail', { name: r }); }
hdr.addEventListener('click', e => {
  const a = ((e.target as HTMLElement).closest('[data-a]') as HTMLElement | null)?.dataset.a; if (!a) return;
  if (a === 'about') showAbout(); else if (a === 'help') help.show(); else if (a === 'open') bridge.call('openDialog'); else if (a === 'save') bridge.call('project.save');
  else if (a === 'theme') bridge.call('setTheme', { theme: S?.theme === 'dark' ? 'light' : 'dark' }); else if (a === 'plot') bridge.call('setPlot', { plot: S?.plot === 'dark' ? 'light' : 'dark' });
});
function renderMain() {
  if (!S) return;
  const want: HTMLElement[] = S.rail === 'PROJECT' ? [project.el] : S.rail === 'ANALYZE' ? [analyze.el] : S.rail === 'SIMULATE' ? [simulate.el] : S.rail === 'ICD' ? [icd.el] : S.rail === 'SETTINGS' ? [settings.el] : [start.el];
  want.forEach((el, i) => { if (main.children[i] !== el) main.insertBefore(el, main.children[i] ?? null); });
  while (main.children.length > want.length) main.lastElementChild!.remove();
}
function renderFooter() {
  if (!S) return;
  (ftr.querySelector('.d') as HTMLElement).className = 'd ' + S.status.led;
  ftr.querySelector('.txt')!.textContent = S.status.text || (S.project?.name ? `${S.project.name} · ${S.project.icd?.messages ?? 0} messages` : 'Open a project or create one');
  ftr.querySelector('.r')!.textContent = S.sim?.running ? `bus running · ${Math.round(S.sim.messages).toLocaleString('en-US')} messages · ${S.sim.loadPct.toFixed(1)} %` : '';
}
function applyState(next: BusState) {
  S = next; document.documentElement.dataset.theme = S.theme; document.documentElement.dataset.plot = S.plot;
  renderHeader(); renderRail(); renderMain(); renderFooter(); help.setState(S);
  if (S.rail === 'START') start.setState(S); else if (S.rail === 'PROJECT') project.setState(S); else if (S.rail === 'ANALYZE') analyze.setState(S); else if (S.rail === 'SIMULATE') simulate.setState(S); else if (S.rail === 'ICD') icd.setState(S); else if (S.rail === 'SETTINGS') settings.setState(S);
}
bridge.on('state', m => { const st = m.state as BusState; if (!st.files && S) st.files = S.files; else if (!st.files) st.files = []; applyState(st); });
bridge.on('status', m => { if (!S) return; S.status = { text: m.text, led: m.led }; renderFooter(); const led = hdr.querySelector('.led'); if (led) { led.className = 'led ' + m.led; (hdr.querySelector('.led-text') as HTMLElement).textContent = m.led.toUpperCase(); } });
bridge.on('invalidate', () => { /* the ANALYZE trend chart requests its own frames */ });
bridge.on('undo', () => { });

// ---------- About (logo): version, pre-release notice, feedback link
function showAbout() {
  if (!S || document.querySelector('.about')) return; const a = S.app ?? {};
  const dlg = h(`<div class="modal-bg"><div class="modal about"><div style="display:flex;align-items:center;gap:10px"><img src="brand/Logo.png" alt="" style="height:18px"><span class="ver">${esc(a.versionText || '')}</span></div>
    <div class="ttl">${esc(a.name || 'NexysBus')} — MIL-STD-1553 bus simulation and capture analysis</div>
    <p>${a.beta ? '정식 출시 전 버전입니다. 앱 이름과 화면이 바뀔 수 있고, 하드웨어 카드 지원은 다음 단계입니다. 문제는 아래 피드백으로 알려 주세요.' : ''}</p>
    <p>피드백: <a href="#" data-url="${esc(a.issues || '')}" style="color:var(--accent2);text-decoration:underline">GitHub Issues</a> · 저장소: <a href="#" data-url="${esc(a.repo || '')}" style="color:var(--accent2);text-decoration:underline">${esc(a.repo || '')}</a></p>
    <div class="btns"><button class="pill h30 primary" data-b="ok">CLOSE</button></div></div></div>`);
  dlg.addEventListener('click', e => { const t = e.target as HTMLElement; const u = (t.closest('[data-url]') as HTMLElement | null)?.dataset.url; if (u) { e.preventDefault(); bridge.call('openUrl', { url: u }); return; } if ((t.closest('[data-b]') as HTMLElement | null)?.dataset.b === 'ok' || t === dlg) dlg.remove(); });
  document.body.append(dlg);
}
window.addEventListener('keydown', e => { if (e.key === 'F1') { e.preventDefault(); help.show(); } if (e.key === 'Escape') document.querySelector('.modal-bg')?.remove(); });
document.addEventListener('keydown', e => {
  const inInput = ['INPUT', 'TEXTAREA', 'SELECT'].includes((e.target as HTMLElement).tagName);
  if (!(e.ctrlKey || e.metaKey)) { if (e.key === 'F5') e.preventDefault(); return; }
  const k = e.key.toLowerCase();
  if (inInput && !['o', 's', '1', '2', '3', '4', '5'].includes(k)) return;
  if (k === 'o') { e.preventDefault(); bridge.call('openDialog'); } else if (k === 's') { e.preventDefault(); bridge.call('project.save'); }
  else if (k === 't') { e.preventDefault(); bridge.call('setTheme', { theme: S?.theme === 'dark' ? 'light' : 'dark' }); } else if (k === 'p') { e.preventDefault(); bridge.call('setPlot', { plot: S?.plot === 'dark' ? 'light' : 'dark' }); }
  else { const r = RAILS.find(x => x.key === k); if (r) { e.preventDefault(); setRail(r.id); } }
});
document.addEventListener('dragover', e => { if (e.dataTransfer?.types.includes('Files')) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } });
document.addEventListener('drop', e => { if (e.dataTransfer?.types.includes('Files')) { e.preventDefault(); bridge.postFiles(e.dataTransfer.files); } else if (!e.defaultPrevented) e.preventDefault(); });

// ---------- test hooks (real DOM events; awaited by the host self-tests)
const raf = () => new Promise<void>(r => requestAnimationFrame(() => r()));
async function settle(ms = 30) { for (let i = 0; i < 100; i++) { await raf(); const pv = (analyze as any).pv; if (!pv || !pv.busy) break; await new Promise(r => setTimeout(r, 10)); } await new Promise(r => setTimeout(r, ms)); await raf(); }
const test = {
  async run(tid: number, fn: () => Promise<any>) { let result: any; try { result = await fn(); } catch (e: any) { result = { error: String(e?.message ?? e) }; bridge.log('test error ' + (e?.stack ?? e)); } bridge.post({ cmd: 'test', tid, result }); },
  settle, state: () => S,
  async rail(name: string) { setRail(name); for (let i = 0; i < 60 && S?.rail !== name; i++) await new Promise(r => setTimeout(r, 20)); await settle(); return S?.rail; },
  async clickSel(sel: string) { const el = document.querySelector(sel) as HTMLElement | null; if (!el) throw new Error('no element ' + sel); el.click(); await new Promise(r => setTimeout(r, 80)); await settle(); return true; },
  async help(section = 'start') { help.show(section); await new Promise(r => setTimeout(r, 100)); return !!document.querySelector('.help-pane'); },
  m1553: () => analyze.testApi(), icd: () => icd.testApi(), sim: () => simulate.testApi(), project: () => project.testApi(),
  plot: () => (analyze as any).pv,
  /** Elements whose border / outline uses an accent colour (must be 0). */
  accentScan() {
    const css = getComputedStyle(document.documentElement); const accents = ['--accent2', '--accent-line', '--accent'].map(v => css.getPropertyValue(v).trim()).filter(Boolean);
    const toRgb = (c: string) => { const d = document.createElement('div'); d.style.color = c; document.body.append(d); const v = getComputedStyle(d).color; d.remove(); return v; };
    const set = new Set(accents.map(toRgb)); const hits: string[] = [];
    document.querySelectorAll<HTMLElement>('.card, .chart, .chart-h, .tool, .pill, .menu, .dev, .rail button, .start .recent button, .frame-tbl td, .rt-tbl td, .stat4 > div, .stat-row > div, .tour-card').forEach(el => {
      const cs = getComputedStyle(el);
      for (const k of ['borderTopColor', 'borderLeftColor', 'borderRightColor', 'borderBottomColor', 'outlineColor'] as const) { const w = k === 'outlineColor' ? cs.outlineWidth : cs[k.replace('Color', 'Width') as 'borderTopWidth']; if (set.has(cs[k]) && parseFloat(w) > 0 && cs.outlineStyle !== 'none') hits.push(el.className + ':' + k); }
    });
    return { accents: Array.from(set), hits };
  },
  layout() { const g = (sel: string) => { const el = document.querySelector(sel) as HTMLElement | null; if (!el) return null; const r = el.getBoundingClientRect(); return { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) }; }; return { header: g('.hdr'), rail: g('.rail'), main: g('.main'), footer: g('.ftr') }; },
};
window.nx = { bridge, test, get state() { return S; }, call: (c: string, a?: any) => bridge.call(c, a) };
document.fonts.ready.then(() => bridge.post({ cmd: 'ready' }));
if (!bridge.hosted) bridge.call<BusState>('state').then(s => applyState({ ...s, rail: 'START', project: {}, sim: {}, devices: [], recent: [] } as BusState));
