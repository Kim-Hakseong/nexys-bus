// NexysBus shell: the shared shell of nexys-core (@core/shell: header · rail · page · footer · about · keys · drop · test hooks) with the
// bus pages — PROJECT · ANALYZE · SIMULATE · ICD · SETTINGS.
import { I } from '@core/icons';
import { createShell, ShellState } from '@core/shell';
import { BusMode } from './analyze';
import { IcdPage } from './icd';
import { help } from './help';
import { ProjectPage } from './project';
import { settingsPage } from './settings';
import { SimulatePage } from './simulate';
import { StartPage } from './start';

const start = new StartPage(); const project = new ProjectPage(); const analyze = new BusMode(); const simulate = new SimulatePage(); const icd = new IcdPage(); const settings = settingsPage();
const pageOf = (rail: string): HTMLElement => rail === 'PROJECT' ? project.el : rail === 'ANALYZE' ? analyze.el : rail === 'SIMULATE' ? simulate.el : rail === 'ICD' ? icd.el : rail === 'SETTINGS' ? settings.el : start.el;
createShell({
  appName: 'NexysBus',
  aboutTitle: 'MIL-STD-1553 bus simulation and capture analysis',
  aboutBeta: 'Pre-release build: the product name and screens may change, and hardware card support (driver binding + SEARCH HARDWARE) awaits its first measurement on a PC with a card. Please report problems through the feedback link below.',
  rails: [
    { id: 'PROJECT', label: 'PROJECT', icon: I.file(18), key: '1' }, { id: 'ANALYZE', label: 'ANALYZE', icon: I.table(18), key: '2' }, { id: 'SIMULATE', label: 'SIMULATE', icon: I.lineC(18), key: '3' },
    { id: 'ICD', label: 'ICD', icon: I.list(18), key: '4' }, { id: 'SETTINGS', label: 'SETTINGS', icon: I.sliders(18), key: '5' }],
  pages: S => [pageOf(S.rail)],
  route: (S: ShellState) => {
    if (S.rail === 'START') start.setState(S); else if (S.rail === 'PROJECT') project.setState(S); else if (S.rail === 'ANALYZE') analyze.setState(S); else if (S.rail === 'SIMULATE') simulate.setState(S);
    else if (S.rail === 'ICD') icd.setState(S); else if (S.rail === 'SETTINGS') settings.setState(S);
  },
  footerText: S => S.project?.name ? `${S.project.name} · ${S.project.icd?.messages ?? 0} messages` : 'Open a project or create one',
  footerRight: S => S.sim?.running ? `bus running · ${Math.round(S.sim.messages).toLocaleString('en-US')} messages · ${S.sim.loadPct.toFixed(1)} %` : '',
  help,
  busy: () => { const pv = (analyze as any).pv; return !!pv && !!pv.busy; },
  test: {
    m1553: () => analyze.testApi(), icd: () => icd.testApi(), sim: () => simulate.testApi(), project: () => project.testApi(),
    plot: () => (analyze as any).pv,
  },
});
