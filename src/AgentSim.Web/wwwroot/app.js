'use strict';

const $ = (id) => document.getElementById(id);

const elements = {
  scenario: $('scenario'),
  btnNew: $('btn-new'),
  btnTick: $('btn-tick'),
  btnTick30: $('btn-tick30'),
  btnRun: $('btn-run'),
  btnStop: $('btn-stop'),
  speed: $('speed'),
  statusStrip: $('status-strip'),
  overview: $('overview'),
  services: $('services'),
  sectorsBody: document.querySelector('#sectors-table tbody'),
  structuresBody: document.querySelector('#structures-table tbody'),
  mapCanvas: $('map'),
};

let pollHandle = null;

// ===== API =====

async function api(path, opts = {}) {
  const res = await fetch(path, { method: opts.method ?? 'GET', headers: { 'Content-Type': 'application/json' } });
  if (!res.ok) throw new Error(`${path} ${res.status}`);
  return await res.json();
}

async function newSim() {
  const scenario = elements.scenario.value;
  const snap = await api(`/api/sim/new?scenario=${scenario}`, { method: 'POST' });
  render(snap);
}

async function tick(days = 1) {
  const snap = await api(`/api/sim/tick?days=${days}`, { method: 'POST' });
  render(snap);
}

async function startRun() {
  const snap = await api(`/api/sim/run/start`, { method: 'POST' });
  render(snap);
  startPolling();
}

async function stopRun() {
  const snap = await api(`/api/sim/run/stop`, { method: 'POST' });
  render(snap);
  stopPolling();
}

async function setSpeed(tps) {
  await api(`/api/sim/run/speed?ticksPerSecond=${tps}`, { method: 'POST' });
}

async function refresh() {
  try {
    const snap = await api('/api/sim/state');
    render(snap);
    if (!snap.isRunning) stopPolling();
  } catch (e) {
    console.error('refresh failed', e);
  }
}

function startPolling() {
  if (pollHandle) return;
  pollHandle = setInterval(refresh, 250);
}

function stopPolling() {
  if (pollHandle) { clearInterval(pollHandle); pollHandle = null; }
}

// ===== Render =====

function fmtMoney(n) {
  const sign = n < 0 ? '-' : '';
  const abs = Math.abs(n);
  return sign + '$' + abs.toLocaleString();
}

function moneyClass(n) {
  if (n < 0) return 'money danger';
  if (n > 0) return 'money good';
  return 'money';
}

function pctBar(pct) {
  const p = Math.max(0, Math.min(100, pct));
  const cls = p < 25 ? 'bad' : p < 60 ? 'med' : '';
  return `<span class="bar"><span class="bar-fill ${cls}" style="width:${p}%"></span></span>${p.toFixed(0)}%`;
}

function render(snap) {
  // Top status strip
  elements.statusStrip.innerHTML = [
    `<span class="chip"><strong>Scenario:</strong> ${snap.scenario}</span>`,
    `<span class="chip"><strong>Day:</strong> ${snap.day} (M${snap.month})</span>`,
    `<span class="chip"><strong>Phase:</strong> ${snap.isFoundingPhase ? 'founding' : 'normal'}</span>`,
    `<span class="chip"><strong>Running:</strong> ${snap.isRunning ? `▶ ${snap.ticksPerSecond}/s` : '⏸'}</span>`,
    snap.city.gameOver ? `<span class="chip" style="background:#bf616a;color:#fff"><strong>GAME OVER</strong></span>` : '',
  ].join('');

  // Run controls
  elements.btnRun.disabled = snap.isRunning;
  elements.btnStop.disabled = !snap.isRunning;

  // Overview
  const o = snap.city, r = snap.region;
  elements.overview.innerHTML = [
    row('Population', `${o.population} (${o.employed} employed)`),
    row('Treasury', fmtMoney(o.treasury), o.treasury < 0 ? 'danger' : ''),
    row('Total agent savings', fmtMoney(o.totalSavings)),
    row('Upkeep funding', pctBar(o.upkeepFundingFraction * 100)),
    row('Bankrupt months', `${o.consecutiveMonthsBankrupt} / 6`),
    row('Climate / Nature', `${(r.climate * 100).toFixed(0)}% / ${(r.nature * 100).toFixed(0)}%`),
    row('Reservoir', r.reservoirTotal.toLocaleString()),
  ].join('');

  // Services
  const s = snap.services;
  elements.services.innerHTML = [
    row('Civic', pctBar(s.civic)),
    row('Healthcare', pctBar(s.healthcare)),
    row('Utility', pctBar(s.utility)),
    row('Environmental', pctBar(s.environmental)),
    row('Primary edu', pctBar(s.primaryEducation)),
    row('Secondary edu', pctBar(s.secondaryEducation)),
    row('College edu', pctBar(s.collegeEducation)),
  ].join('');

  // Sectors
  elements.sectorsBody.innerHTML = snap.sectors.map(sec =>
    `<tr><td>${sec.sector}</td><td>${fmtMoney(sec.monthlyDemand)}</td><td>${sec.activeShops}/${sec.totalShops}</td><td>${sec.mfgs}</td></tr>`
  ).join('');

  // Structures (skip residential which weren't sent anyway)
  const grouped = snap.structures.slice().sort((a, b) => {
    if (a.category !== b.category) return a.category.localeCompare(b.category);
    return a.type.localeCompare(b.type);
  });
  elements.structuresBody.innerHTML = grouped.map(s => {
    const tag = s.sector ?? s.industry ?? '';
    const status = s.underConstruction ? '<span class="status-building">building</span>'
      : s.inactive ? '<span class="status-inactive">inactive</span>'
      : '<span class="status-active">active</span>';
    return `<tr>
      <td>${s.type}</td>
      <td>${tag}</td>
      <td>${status}</td>
      <td class="${moneyClass(s.cash)}">${fmtMoney(s.cash)}</td>
      <td>${s.employees}/${s.jobSlots}</td>
      <td class="${moneyClass(s.monthlyRevenue)}">${fmtMoney(s.monthlyRevenue)}</td>
      <td class="${moneyClass(-s.monthlyExpenses)}">${fmtMoney(s.monthlyExpenses)}</td>
    </tr>`;
  }).join('');

  drawMap(snap);
}

function row(label, value, cls = '') {
  return `<div class="lbl">${label}</div><div class="val ${cls}">${value}</div>`;
}

function drawMap(snap) {
  const ctx = elements.mapCanvas.getContext('2d');
  const w = elements.mapCanvas.width, h = elements.mapCanvas.height;
  ctx.clearRect(0, 0, w, h);
  // Placeholder: grid + colored circles for structures, no real positions yet.
  ctx.strokeStyle = '#242938';
  for (let x = 0; x < w; x += 30) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke(); }
  for (let y = 0; y < h; y += 30) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke(); }

  // Lay structures left-to-right in a row, color by category.
  const colors = {
    Commercial: '#88c0d0',
    IndustrialExtractor: '#d08770',
    IndustrialProcessor: '#bf616a',
    IndustrialManufacturer: '#a3be8c',
    Civic: '#ebcb8b',
    Healthcare: '#b48ead',
    Education: '#5e81ac',
    Utility: '#81a1c1',
    Restoration: '#a3be8c',
  };
  let x = 20, y = 20;
  for (const s of snap.structures) {
    const c = colors[s.category] ?? '#4c566a';
    ctx.fillStyle = s.inactive ? '#4c566a' : c;
    ctx.beginPath(); ctx.arc(x, y, 8, 0, Math.PI * 2); ctx.fill();
    x += 22; if (x > w - 20) { x = 20; y += 22; }
  }
}

// ===== Wire up =====

elements.btnNew.addEventListener('click', newSim);
elements.btnTick.addEventListener('click', () => tick(1));
elements.btnTick30.addEventListener('click', () => tick(30));
elements.btnRun.addEventListener('click', startRun);
elements.btnStop.addEventListener('click', stopRun);
elements.speed.addEventListener('change', (e) => setSpeed(parseInt(e.target.value, 10)));

// Initial load.
refresh();
