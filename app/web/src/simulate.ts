// SIMULATE rail: frame table (order, enabled, period → slot every n minor frames), minor-frame visualisation over the major frame,
// RUN panel (start / stop / batch, counters, error injection), simulated RT data sources per message.
import { bridge } from '@core/bridge';
import { esc, fmtInt, h } from '@core/util';

const KINDS = ['eu', 'sine', 'ramp', 'const'];
const gcd = (a: number, b: number): number => b ? gcd(b, a % b) : a; const lcm = (a: number, b: number) => a / gcd(a, b) * b;

export class SimulatePage {
  el = h(`<div class="simulate"><section class="card frame"><div class="card-h"><span class="t">FRAME</span><span class="m" data-m></span></div><div class="scroll"><table class="frame-tbl"><thead><tr><th>#</th><th>ON</th><th>MESSAGE</th><th>RT</th><th>SA</th><th>T/R</th><th>WC</th><th>PERIOD ms</th><th>EVERY</th><th>BUS</th></tr></thead><tbody></tbody></table></div><div class="frame-vis"></div></section>
    <section class="card run"><div class="card-h"><span class="t">RUN</span><span class="m" data-rm></span></div><div class="ctl"></div><div class="stat4"></div><div class="errs"></div><div class="bars"></div><div class="cap"></div></section>
    <section class="card rt" style="grid-column:1 / -1"><div class="card-h"><span class="t">SIMULATED RT DATA</span><span class="m">per message (subaddress) · EU waveform packed through the ICD bit fields</span></div><div class="scroll"><table class="rt-tbl"><thead><tr><th>MESSAGE</th><th>RT/SA</th><th>WORDS</th><th>SOURCE</th><th>VALUE</th><th>AMPLITUDE</th><th>PERIOD s</th><th>NOTE</th></tr></thead><tbody></tbody></table></div></section></div>`);
  private key = ''; private runKey = ''; private tick = 0; private expectedLoad = 0;
  constructor() {
    this.el.addEventListener('click', e => {
      const t = e.target as HTMLElement; const a = (t.closest('[data-a]') as HTMLElement | null)?.dataset.a; const row = (t.closest('tr[data-m]') as HTMLElement | null)?.dataset.m;
      if (a === 'up' && row) bridge.call('sim.move', { message: row, dir: -1 }); else if (a === 'down' && row) bridge.call('sim.move', { message: row, dir: 1 });
      else if (a === 'start') bridge.call('sim.start'); else if (a === 'stop') bridge.call('sim.stop'); else if (a === 'batch') bridge.call('sim.runFor', { seconds: 10 }); else if (a === 'analyze') bridge.call('rail', { name: 'ANALYZE' });
    });
    this.el.addEventListener('change', e => {
      const t = e.target as HTMLInputElement; const f = t.dataset.f; if (!f) return; const row = (t.closest('tr[data-m]') as HTMLElement | null)?.dataset.m;
      if (t.dataset.err) { bridge.call('sim.errors.set', { field: t.dataset.err, every: parseInt(t.value, 10) || 0 }); return; }
      if (!row) return;
      if (t.dataset.src) bridge.call('sim.source.set', { message: row, field: f, value: f === 'kind' ? t.value : parseFloat(t.value) || 0 });
      else bridge.call('sim.schedule.set', { message: row, field: f, value: f === 'enabled' ? t.checked : f === 'periodMs' ? parseFloat(t.value) || 0 : parseInt(t.value, 10) || 0 });
    });
    setInterval(() => { if (this.el.isConnected && (this as any)._S?.sim?.running) bridge.call('sim.status').then(s => { if (s) this.renderRun((this as any)._S, s); }); }, 250);
  }
  setState(S: any) {
    (this as any)._S = S; const p = S.project ?? {}; const sched: any[] = p.schedule ?? [];
    const key = JSON.stringify([sched, p.bus, p.errors]); if (key !== this.key) { this.key = key; this.renderFrame(p, sched); this.renderRt(sched); }
    this.renderRun(S, S.sim ?? {});
  }
  private renderFrame(p: any, sched: any[]) {
    const minorIn = p.bus?.minorFrameMs > 0 ? p.bus.minorFrameMs : Math.max(1, Math.min(...sched.filter(s => s.enabled).map(s => s.periodMs), 100));
    const every = (s: any) => Math.max(1, Math.round(s.periodMs / minorIn)); const en = sched.filter(s => s.enabled);
    const major = en.length ? minorIn * en.map(every).reduce((a, b) => lcm(a, b), 1) : minorIn;
    // expected bus load from the frame table: Σ rate(msg/s) × (command + status + data words) × 20 µs — the statistics panel's approximation
    this.expectedLoad = en.reduce((a, s) => a + (1000 / (every(s) * minorIn)) * (2 + (s.wc || 32)) * 20e-6 * 100, 0);
    (this.el.querySelector('[data-m]') as HTMLElement).textContent = `${sched.length} messages · minor ${minorIn.toFixed(1)} ms · major ${major.toFixed(0)} ms`;
    this.el.querySelector('.frame-tbl tbody')!.innerHTML = sched.map((s, i) => `<tr data-m="${esc(s.message)}" class="${s.enabled ? '' : 'off'}"><td><span class="mv"><button data-a="up" title="Move up">▲</button><button data-a="down" title="Move down">▼</button></span> ${i + 1}</td><td><input type="checkbox" data-f="enabled"${s.enabled ? ' checked' : ''}></td><td>${esc(s.message)}</td><td>${s.rt}</td><td>${s.sa}</td><td>${s.tr ? 'RT→BC' : 'BC→RT'}</td><td>${s.wc}</td><td><input class="cell" data-f="periodMs" value="${esc(s.periodMs)}"></td><td>${every(s)}</td><td>${esc(s.bus)}</td></tr>`).join('') || `<tr><td colspan="10" class="muted11">No messages — load an ICD (ICD rail) or import a simulator project (PROJECT).</td></tr>`;
    const n = Math.min(64, Math.round(major / minorIn)); const vis = this.el.querySelector('.frame-vis')!;
    vis.innerHTML = `<div class="vh">MINOR FRAMES<span class="m">${n} of ${Math.round(major / minorIn)} shown · ${minorIn.toFixed(1)} ms each</span></div>` + sched.map(s => `<div class="lane${s.enabled ? '' : ' off'}"><span class="n" title="${esc(s.message)}">${esc(s.message)}</span><div class="cells" style="grid-template-columns:repeat(${n},1fr)">${Array.from({ length: n }, (_, k) => `<i${k % every(s) === 0 ? ' class="on"' : ''}></i>`).join('')}</div></div>`).join('');
  }
  private renderRt(sched: any[]) {
    this.el.querySelector('.rt-tbl tbody')!.innerHTML = sched.map(s => { const src = s.source ?? {}; return `<tr data-m="${esc(s.message)}"><td>${esc(s.message)}</td><td>${s.rt}/${s.sa}</td><td>${s.words}</td><td><select class="cell" data-src="1" data-f="kind">${KINDS.map(k => `<option${src.kind === k ? ' selected' : ''}>${k}</option>`).join('')}</select></td><td><input class="cell" data-src="1" data-f="value" value="${esc(src.value)}"></td><td><input class="cell" data-src="1" data-f="amplitude" value="${esc(src.amplitude)}"></td><td><input class="cell" data-src="1" data-f="periodS" value="${esc(src.periodS)}"></td><td class="muted11">${src.kind === 'eu' ? 'sine over 80 % of each word range (inverse scale · offset)' : src.kind === 'const' ? 'constant EU value' : src.kind === 'ramp' ? 'EU sawtooth value … value + amplitude' : 'EU sine value ± amplitude'}</td></tr>`; }).join('') || `<tr><td colspan="8" class="muted11">No messages.</td></tr>`;
  }
  private renderRun(S: any, sim: any) {
    const p = S.project ?? {}; const er = p.errors ?? {}; const key = JSON.stringify([sim.running, sim.device, sim.capture, er]);
    const running = !!sim.running; const ctl = this.el.querySelector('.ctl')!;
    if (key !== this.runKey) {
      this.runKey = key;
      (this.el.querySelector('[data-rm]') as HTMLElement).textContent = `${sim.device?.name ?? p.device ?? ''}${sim.capture ? ' · capture ' + sim.capture : ''}`;
      ctl.innerHTML = `<button class="pill h32 primary" data-a="start"${running ? ' disabled' : ''}>▶ START</button><button class="pill h32" data-a="stop"${running ? '' : ' disabled'}>■ STOP</button><button class="pill h24" data-a="batch" title="Generate 10 s of bus time at once on the virtual clock (no waiting)"${running ? ' disabled' : ''}>10 s BATCH</button><button class="pill h24" data-a="analyze" title="Go to the capture list">ANALYZE →</button><span class="elapsed" data-el>00:00.0</span>`;
      this.el.querySelector('.errs')!.innerHTML = `<span class="k">ERROR INJECTION · every N</span><span>no response</span><input class="in" data-err="noResponse" data-f="e" value="${er.noResponse ?? 0}"><span>parity</span><input class="in" data-err="parity" data-f="e" value="${er.parity ?? 0}"><span>word count</span><input class="in" data-err="wordCount" data-f="e" value="${er.wordCount ?? 0}"><span class="k">0 = off</span>`;
    }
    const t = sim.elapsedS ?? 0; (ctl.querySelector('[data-el]') as HTMLElement).textContent = `${String(Math.floor(t / 60)).padStart(2, '0')}:${(t % 60).toFixed(1).padStart(4, '0')}`;
    const stat = (k: string, v: string) => `<div><div class="k">${k}</div><div class="v">${v}</div></div>`;
    this.el.querySelector('.stat4')!.innerHTML = stat('MESSAGES', `<b>${fmtInt(sim.messages ?? 0)}</b>`) + stat('ERRORS', `<b class="${sim.errors ? 'errv' : ''}">${fmtInt(sim.errors ?? 0)}</b>`) + stat('BUS LOAD', `<b>${(sim.loadPct ?? 0).toFixed(1)}</b><span>%</span><span class="exp" data-exp="${this.expectedLoad.toFixed(3)}" title="From the frame table: Σ rate × (2 + data words) × 20 µs">expected ${this.expectedLoad.toFixed(1)} %</span>`) + stat('A / B', `<b>${fmtInt(sim.busA ?? 0)}</b><span>/ ${fmtInt(sim.busB ?? 0)}</span>`);
    const per: any[] = sim.perRt ?? []; const max = Math.max(1, ...per.map(x => x.count));
    this.el.querySelector('.bars')!.innerHTML = per.slice(0, 8).map(x => `<div class="bar"><span>RT ${x.rt}</span><i style="width:${(100 * x.count / max).toFixed(1)}%"></i><span>${fmtInt(x.count)}</span></div>`).join('');
    (this.el.querySelector('.cap') as HTMLElement).textContent = running ? `→ ANALYZE shows the capture live · stop writes ${sim.capturePath ?? ''}` : sim.lastCapture ? `last capture: ${sim.lastCapture}` : `captures go to ${sim.captureDir ?? ''}`;
  }
  testApi() {
    const self = this; const fire = async (sel: string, v: string) => { const el = self.el.querySelector(sel) as HTMLInputElement; if (!el) throw new Error('no ' + sel); if (el.type === 'checkbox') el.checked = v === 'true'; else el.value = v; el.dispatchEvent(new Event('change', { bubbles: true })); await new Promise(r => setTimeout(r, 150)); return true; };
    return {
      rows: () => self.el.querySelectorAll('.frame-tbl tr[data-m]').length, lanes: () => self.el.querySelectorAll('.frame-vis .lane').length,
      setCell: (m: string, f: string, v: string) => fire(`.frame-tbl tr[data-m="${m}"] [data-f="${f}"]`, v),
      setSource: (m: string, f: string, v: string) => fire(`.rt-tbl tr[data-m="${m}"] [data-f="${f}"]`, v),
      setError: (f: string, v: string) => fire(`.errs [data-err="${f}"]`, v),
      async move(m: string, dir: number) { (self.el.querySelector(`.frame-tbl tr[data-m="${m}"] [data-a="${dir < 0 ? 'up' : 'down'}"]`) as HTMLElement).click(); await new Promise(r => setTimeout(r, 150)); return true; },
      runFor: (seconds: number) => bridge.call('sim.runFor', { seconds }),
      expectedLoad: () => +((self.el.querySelector('[data-exp]') as HTMLElement | null)?.dataset.exp ?? '-1'),
    };
  }
}
