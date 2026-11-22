namespace Spice.App;

/// <summary>
/// Self-contained interactive desktop/browser workbench UI served by <see cref="WorkbenchServer"/>.
/// Every simulation executes live in-process via libspice_capi ABI v2.1 through ISimulationBackend.
/// </summary>
public static class WorkbenchUi
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Spice V2 Workbench - Live Native C++20 Engine (.NET 11)</title>
  <style>
    :root {
      --bg: #0b1120;
      --panel: #111827;
      --panel-2: #1e293b;
      --border: #334155;
      --text: #f1f5f9;
      --muted: #94a3b8;
      --accent: #38bdf8;
      --green: #22c55e;
      --amber: #f59e0b;
      --rose: #f43f5e;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0; font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
      background: var(--bg); color: var(--text); min-height: 100vh; display: flex; flex-direction: column;
    }
    header {
      display: flex; align-items: center; justify-content: space-between;
      padding: 10px 18px; background: var(--panel); border-bottom: 1px solid var(--border);
    }
    .brand { display: flex; align-items: center; gap: 10px; font-weight: 700; letter-spacing: 0.02em; }
    .badge {
      font-size: 11px; padding: 3px 8px; border-radius: 999px;
      background: rgba(56, 189, 248, 0.14); color: var(--accent); border: 1px solid rgba(56, 189, 248, 0.35);
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    }
    .badge.ok { background: rgba(34, 197, 94, 0.14); color: var(--green); border-color: rgba(34, 197, 94, 0.35); }
    nav { display: flex; gap: 6px; padding: 8px 18px; background: #0f172a; border-bottom: 1px solid var(--border); }
    .tab-btn {
      background: transparent; color: var(--muted); border: 1px solid transparent;
      padding: 6px 14px; border-radius: 6px; cursor: pointer; font-weight: 600; font-size: 13px;
    }
    .tab-btn.active { background: var(--panel-2); color: var(--text); border-color: var(--border); }
    main { flex: 1; padding: 14px 18px; display: flex; flex-direction: column; gap: 14px; }
    .tab-pane { display: none; gap: 14px; }
    .tab-pane.active { display: grid; grid-template-columns: minmax(340px, 1.05fr) minmax(420px, 1.25fr); }
    .card {
      background: var(--panel); border: 1px solid var(--border); border-radius: 10px;
      padding: 12px 14px; display: flex; flex-direction: column; gap: 10px;
    }
    .card h3 { margin: 0; font-size: 13px; text-transform: uppercase; letter-spacing: 0.06em; color: var(--muted); }
    .toolbar { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
    select, input, button, textarea {
      background: var(--panel-2); color: var(--text); border: 1px solid var(--border);
      border-radius: 6px; padding: 6px 10px; font-size: 13px;
    }
    button.primary { background: #0284c7; border-color: #38bdf8; font-weight: 600; cursor: pointer; }
    button.primary:hover { background: #0369a1; }
    button.ghost { cursor: pointer; }
    textarea {
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px;
      width: 100%; resize: vertical; line-height: 1.45;
    }
    #svg-host {
      background: #0f172a; border: 1px solid var(--border); border-radius: 8px;
      min-height: 270px; display: flex; align-items: center; justify-content: center; overflow: hidden;
    }
    #svg-host svg { width: 100%; height: 270px; }
    #svg-host .spice-net { cursor: pointer; }
    #svg-host .spice-net:hover line:last-of-type { stroke: #f59e0b; stroke-width: 4; }
    #svg-host .spice-net.probed line:last-of-type { stroke: #f43f5e; stroke-width: 4; }
    #svg-host .spice-component { cursor: pointer; }
    #svg-host .spice-component:hover rect { stroke: #f59e0b; stroke-width: 2.5; }
    canvas { width: 100%; height: 300px; background: #0f172a; border: 1px solid var(--border); border-radius: 8px; }
    .chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .chip {
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 11px;
      padding: 4px 8px; border-radius: 999px; border: 1px solid var(--border);
      background: var(--panel-2); color: var(--muted); cursor: pointer;
    }
    .chip.on { color: #fff; border-color: var(--accent); background: rgba(56, 189, 248, 0.2); }
    table { width: 100%; border-collapse: collapse; font-size: 12px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    th, td { border-bottom: 1px solid var(--border); padding: 5px 6px; text-align: left; }
    th { color: var(--muted); font-weight: 600; }
    .status-line { font-size: 12px; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; color: var(--muted); }
    .err { color: var(--rose); }
    .ok { color: var(--green); }
    @media (max-width: 960px) { .tab-pane.active { grid-template-columns: 1fr; } }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <span>Spice V2 Workbench</span>
      <span id="abi-badge" class="badge">Connecting...</span>
    </div>
    <div class="status-line" id="runtime-info">Live Native C++20 Engine via .NET 11</div>
  </header>

  <nav>
    <button class="tab-btn active" data-tab="schematic">1. Schematic Editor &amp; Cross-Probing</button>
    <button class="tab-btn" data-tab="deck">2. SPICE3f5 / Spectre / BSIM4 / RF Deck</button>
    <button class="tab-btn" data-tab="campaign">3. Parallel Corner Campaign</button>
  </nav>

  <main>
    <!-- TAB 1: SCHEMATIC EDITOR & LIVE CROSS-PROBING -->
    <section id="tab-schematic" class="tab-pane active">
      <div class="card">
        <h3>Interactive Schematic Sheet (Click Wires / Sources to Probe)</h3>
        <div class="toolbar">
          <select id="schem-preset">
            <option value="rc">RC Low-Pass Filter (5V, 1k, 1uF)</option>
            <option value="divider">Resistive Divider (10V, 1k, 2k)</option>
          </select>
          <select id="schem-analysis">
            <option value="tran">Transient (BDF2, UIC)</option>
            <option value="ac">AC Sweep (10 Hz - 100 kHz)</option>
            <option value="op">DC Operating Point</option>
          </select>
          <button id="schem-run" class="primary">Compile ERC &amp; Simulate</button>
        </div>
        <div id="svg-host"></div>
        <div id="erc-status" class="status-line">Ready. Click any net on the schematic to cross-probe its waveform.</div>
        <h3>Component Parameters (Live Edit)</h3>
        <div id="comp-editor"></div>
      </div>

      <div class="card">
        <h3>Live Cross-Probed Waveform &amp; Operating Point</h3>
        <div class="chips" id="schem-signals"></div>
        <canvas id="schem-canvas" width="700" height="300"></canvas>
        <div id="schem-readout" class="status-line"></div>
        <h3>Generated Canonical SPICE Netlist</h3>
        <textarea id="schem-spice" rows="7" readonly></textarea>
      </div>
    </section>

    <!-- TAB 2: FULL-ENGINE DECK WORKBENCH -->
    <section id="tab-deck" class="tab-pane">
      <div class="card">
        <h3>SPICE3f5 / Spectre / BSIM4 / RF / LTRA / TRNOISE Netlist</h3>
        <div class="toolbar">
          <select id="deck-preset">
            <option value="bsim4">BSIM4 (LEVEL=54) CMOS Inverter (.DC)</option>
            <option value="spectre">Spectre Dialect CMOS Subckt (.DC)</option>
            <option value="rf_hb">Pumped Diode RF Mixer (.HB)</option>
            <option value="rf_pss">Pumped Diode RF Detector (.PSS)</option>
            <option value="ltra">Lossy 50-Ohm LTRA Line (.AC)</option>
            <option value="trnoise">TRNOISE RC Low-Pass Filter (.TRAN)</option>
          </select>
          <select id="deck-analysis">
            <option value="dc">DC Sweep</option>
            <option value="op">DC OP</option>
            <option value="ac">AC Sweep</option>
            <option value="tran">Transient</option>
            <option value="hb">Harmonic Balance (.HB)</option>
            <option value="pss">Periodic Steady-State (.PSS)</option>
            <option value="noise">Noise (.NOISE)</option>
            <option value="sp">S-Parameters (.SP)</option>
          </select>
          <button id="deck-run" class="primary">Run Native C++20 Engine</button>
        </div>
        <textarea id="deck-text" rows="16"></textarea>
        <div class="toolbar" id="deck-params">
          <label>Source: <input id="deck-src" value="Vin" size="8" /></label>
          <label>Start: <input id="deck-start" value="0" size="7" /></label>
          <label>Stop: <input id="deck-stop" value="1.2" size="7" /></label>
          <label>Step/Pts: <input id="deck-step" value="0.05" size="7" /></label>
        </div>
        <div id="deck-status" class="status-line"></div>
      </div>

      <div class="card">
        <h3>Engine Output Plot</h3>
        <div class="chips" id="deck-signals"></div>
        <canvas id="deck-canvas" width="700" height="320"></canvas>
        <div id="deck-readout" class="status-line"></div>
      </div>
    </section>

    <!-- TAB 3: PARALLEL CAMPAIGN -->
    <section id="tab-campaign" class="tab-pane">
      <div class="card">
        <h3>Parallel Multi-Corner Campaign (CampaignRunner)</h3>
        <div class="toolbar">
          <label>Vdd (V): <input id="camp-vdd" value="10" size="6" /></label>
          <label>R1 (Ohm): <input id="camp-r1" value="1000" size="7" /></label>
          <label>R2 Corners (comma-sep): <input id="camp-r2" value="500,1000,2000,4000,9000" size="24" /></label>
          <button id="camp-run" class="primary">Run Parallel Sweep</button>
        </div>
        <table id="camp-table">
          <thead><tr><th>Case</th><th>R2 (Ohm)</th><th>Status</th><th>V(out) (V)</th></tr></thead>
          <tbody></tbody>
        </table>
      </div>
      <div class="card">
        <h3>Corner Trajectory Plot</h3>
        <canvas id="camp-canvas" width="700" height="300"></canvas>
      </div>
    </section>
  </main>

  <script>
    const COLORS = ['#38bdf8', '#f43f5e', '#22c55e', '#f59e0b', '#a855f7', '#14b8a6'];

    const SCHEM_PRESETS = {
      rc: {
        schemaVersion: '2.0',
        name: 'rc_lowpass_workbench',
        rootSheet: {
          sheetId: 'root', title: 'RC Low-Pass Filter',
          components: [
            { instanceId: 'V1', deviceKindId: 'spice.vsrc', x: 70, y: 120, rotationDegrees: 0, parameters: { dc_volts: 5.0, ac_mag: 1.0 } },
            { instanceId: 'R1', deviceKindId: 'spice.resistor', x: 190, y: 80, rotationDegrees: 0, parameters: { ohms: 1000.0 } },
            { instanceId: 'C1', deviceKindId: 'spice.capacitor', x: 310, y: 120, rotationDegrees: 0, parameters: { farads: 1e-6, ic_volts: 0.0 } }
          ],
          nets: [
            { netName: 'in', connectedPins: [{ instanceId: 'V1', pinName: 'p' }, { instanceId: 'R1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'R1', pinName: 'n' }, { instanceId: 'C1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'V1', pinName: 'n' }, { instanceId: 'C1', pinName: 'n' }], isGlobalGround: true }
          ]
        }
      },
      divider: {
        schemaVersion: '2.0',
        name: 'divider_workbench',
        rootSheet: {
          sheetId: 'root', title: 'Resistive Divider',
          components: [
            { instanceId: 'V1', deviceKindId: 'spice.vsrc', x: 70, y: 120, rotationDegrees: 0, parameters: { dc_volts: 10.0, ac_mag: 1.0 } },
            { instanceId: 'R1', deviceKindId: 'spice.resistor', x: 190, y: 80, rotationDegrees: 0, parameters: { ohms: 1000.0 } },
            { instanceId: 'R2', deviceKindId: 'spice.resistor', x: 310, y: 120, rotationDegrees: 0, parameters: { ohms: 2000.0 } }
          ],
          nets: [
            { netName: 'in', connectedPins: [{ instanceId: 'V1', pinName: 'p' }, { instanceId: 'R1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'R1', pinName: 'n' }, { instanceId: 'R2', pinName: 'p' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'V1', pinName: 'n' }, { instanceId: 'R2', pinName: 'n' }], isGlobalGround: true }
          ]
        }
      }
    };

    const DECK_PRESETS = {
      bsim4: {
        analysis: 'dc', src: 'Vin', start: '0', stop: '1.2', step: '0.05',
        text: `BSIM4 CMOS Inverter
.MODEL nch4 NMOS(LEVEL=54 VTH0=0.35 TOXE=2.2n U0=420 VSAT=1.1e5)
.MODEL pch4 PMOS(LEVEL=54 VTH0=-0.35 TOXE=2.2n U0=160 VSAT=0.9e5)
Vdd vdd 0 DC 1.2
Vin in 0 DC 0.0
Mn out in 0 0 nch4 W=1u L=65n
Mp out in vdd vdd pch4 W=2.2u L=65n
.END`
      },
      spectre: {
        analysis: 'dc', src: 'Vin', start: '0', stop: '1.2', step: '0.05',
        text: `// Spectre Dialect BSIM4 CMOS Inverter
simulator lang=spectre
parameters vdd_v=1.2 rload=20k
model nch bsim4 type=n vth0=0.35 toxe=2.2n u0=420 vsat=1.1e5
model pch bsim4 type=p vth0=-0.35 toxe=2.2n u0=160 vsat=0.9e5
subckt inv (in out vdd gnd)
  Mn (out in gnd gnd) nch w=1u l=65n
  Mp (out in vdd vdd) pch w=2.2u l=65n
ends inv
Vdd (vdd 0) vsource dc=vdd_v
Vin (in 0) vsource dc=0
X1 (in out vdd 0) inv
R1 (out 0) resistor r=rload`
      },
      rf_hb: {
        analysis: 'hb', src: 'Vlo', start: '1e6', stop: '7', step: '1',
        text: `Pumped Diode RF Mixer (.HB)
.MODEL D1 D(IS=1e-14 CJO=5p)
Vlo in 0 DC 0.4 SIN(0.4 0.8 1Meg)
R1 in out 500
C1 out 0 200p
D1 out 0 D1
.END`
      },
      rf_pss: {
        analysis: 'pss', src: 'Vlo', start: '1e6', stop: '64', step: '1',
        text: `Pumped Diode RF Detector (.PSS)
.MODEL D1 D(IS=1e-14 CJO=5p)
Vlo in 0 DC 0.4 SIN(0.4 0.8 1Meg)
R1 in out 500
C1 out 0 200p
D1 out 0 D1
.END`
      },
      ltra: {
        analysis: 'ac', src: 'V1', start: '1e6', stop: '1e8', step: '15',
        text: `Lossy 50-Ohm LTRA Transmission Line
.MODEL lossy50 LTRA(R=20 L=250n G=0 C=100p LEN=1.0 NSEGS=32)
V1 in 0 DC 5 AC 1
O1 in 0 out 0 lossy50
Rload out 0 50
.END`
      },
      trnoise: {
        analysis: 'tran', src: 'Vn', start: '0', stop: '2e-4', step: '1e-6',
        text: `TRNOISE Deterministic Gaussian Noise RC Filter
Vn in 0 DC 0 TRNOISE(0.5 1u 0 0 42)
R1 in out 1k
C1 out 0 10n
.END`
      }
    };

    let currentSchem = structuredClone(SCHEM_PRESETS.rc);
    let lastSchemResult = null;
    let schemProbed = new Set(['v(out)']);
    let lastDeckResult = null;
    let deckSelected = new Set();

    document.querySelectorAll('.tab-btn').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));
        btn.classList.add('active');
        document.getElementById('tab-' + btn.dataset.tab).classList.add('active');
      });
    });

    function drawPlot(canvas, axis, seriesMap, activeKeys, logX = false, barMode = false) {
      const ctx = canvas.getContext('2d');
      const W = canvas.width, H = canvas.height;
      ctx.clearRect(0, 0, W, H);
      ctx.fillStyle = '#0f172a';
      ctx.fillRect(0, 0, W, H);
      const pad = { l: 58, r: 18, t: 18, b: 32 };
      const pw = W - pad.l - pad.r, ph = H - pad.t - pad.b;

      const keys = [...activeKeys].filter(k => seriesMap[k] && seriesMap[k].length > 0);
      if (!axis || axis.length === 0 || keys.length === 0) {
        ctx.fillStyle = '#94a3b8';
        ctx.font = '13px monospace';
        ctx.fillText('Click a net/signal chip to plot trace.', pad.l + 16, H / 2);
        return;
      }
      const xs = logX ? axis.map(v => Math.log10(Math.max(v, 1e-30))) : axis;
      let minX = Math.min(...xs), maxX = Math.max(...xs);
      if (maxX === minX) { minX -= 1; maxX += 1; }
      let minY = Infinity, maxY = -Infinity;
      for (const k of keys) {
        for (const y of seriesMap[k]) {
          if (Number.isFinite(y)) { minY = Math.min(minY, y); maxY = Math.max(maxY, y); }
        }
      }
      if (!Number.isFinite(minY)) return;
      if (barMode) minY = Math.min(0, minY);
      if (maxY === minY) { minY -= 0.5; maxY += 0.5; }
      const margin = (maxY - minY) * 0.08;
      minY -= margin; maxY += margin;

      const toX = x => pad.l + ((x - minX) / (maxX - minX)) * pw;
      const toY = y => pad.t + ph - ((y - minY) / (maxY - minY)) * ph;

      ctx.strokeStyle = '#1e293b'; ctx.lineWidth = 1;
      for (let g = 0; g <= 4; ++g) {
        const y = pad.t + (ph * g) / 4;
        ctx.beginPath(); ctx.moveTo(pad.l, y); ctx.lineTo(W - pad.r, y); ctx.stroke();
        const val = maxY - ((maxY - minY) * g) / 4;
        ctx.fillStyle = '#94a3b8'; ctx.font = '11px monospace';
        ctx.fillText(val.toPrecision(3), 4, y + 4);
      }

      keys.forEach((k, idx) => {
        const col = COLORS[idx % COLORS.length];
        const ys = seriesMap[k];
        ctx.strokeStyle = col; ctx.fillStyle = col; ctx.lineWidth = 2;
        if (barMode || ys.length === 1) {
          ys.forEach((y, i) => {
            const x = toX(xs[i]);
            const y0 = toY(0), y1 = toY(y);
            ctx.fillRect(x - 6, Math.min(y0, y1), 12, Math.max(2, Math.abs(y1 - y0)));
          });
        } else {
          ctx.beginPath();
          ys.forEach((y, i) => {
            const x = toX(xs[i]), yy = toY(y);
            if (i === 0) ctx.moveTo(x, yy); else ctx.lineTo(x, yy);
          });
          ctx.stroke();
        }
      });

      ctx.fillStyle = '#94a3b8'; ctx.font = '11px monospace';
      ctx.fillText((logX ? 'log10(' : '') + axis[0].toPrecision(3) + (logX ? ')' : ''), pad.l, H - 10);
      ctx.fillText(axis[axis.length - 1].toPrecision(3), W - pad.r - 60, H - 10);
    }

    function renderCompEditor() {
      const host = document.getElementById('comp-editor');
      host.innerHTML = '';
      const tbl = document.createElement('table');
      tbl.innerHTML = '<thead><tr><th>Instance</th><th>Kind</th><th>Parameters</th></tr></thead>';
      const tb = document.createElement('tbody');
      currentSchem.rootSheet.components.forEach((c) => {
        const tr = document.createElement('tr');
        const pInputs = Object.entries(c.parameters).map(([k, v]) =>
          `<label style="margin-right:8px">${k}=<input data-inst="${c.instanceId}" data-key="${k}" value="${v}" size="7" /></label>`
        ).join('');
        tr.innerHTML = `<td>${c.instanceId}</td><td>${c.deviceKindId}</td><td>${pInputs}</td>`;
        tb.appendChild(tr);
      });
      tbl.appendChild(tb);
      host.appendChild(tbl);
      host.querySelectorAll('input').forEach(inp => {
        inp.addEventListener('change', () => {
          const comp = currentSchem.rootSheet.components.find(c => c.instanceId === inp.dataset.inst);
          const v = Number(inp.value);
          if (comp && Number.isFinite(v)) {
            comp.parameters[inp.dataset.key] = v;
            runSchematicSim();
          }
        });
      });
    }

    function bindSvgCrossProbe() {
      const host = document.getElementById('svg-host');
      host.querySelectorAll('.spice-net, .spice-component').forEach(el => {
        const sig = el.getAttribute('data-signal');
        if (!sig) return;
        if (schemProbed.has(sig)) el.classList.add('probed');
        el.addEventListener('click', () => {
          if (schemProbed.has(sig)) schemProbed.delete(sig); else schemProbed.add(sig);
          renderSchemPlot();
        });
      });
    }

    function renderSchemPlot() {
      if (!lastSchemResult) return;
      const host = document.getElementById('svg-host');
      host.querySelectorAll('.spice-net, .spice-component').forEach(el => {
        const sig = el.getAttribute('data-signal');
        el.classList.toggle('probed', !!sig && schemProbed.has(sig));
      });
      const chips = document.getElementById('schem-signals');
      chips.innerHTML = '';
      const series = lastSchemResult.series || {};
      Object.keys(series).forEach(sig => {
        const b = document.createElement('button');
        b.className = 'chip' + (schemProbed.has(sig) ? ' on' : '');
        b.textContent = sig;
        b.onclick = () => {
          if (schemProbed.has(sig)) schemProbed.delete(sig); else schemProbed.add(sig);
          renderSchemPlot();
        };
        chips.appendChild(b);
      });
      const isAc = document.getElementById('schem-analysis').value === 'ac';
      drawPlot(document.getElementById('schem-canvas'), lastSchemResult.axis || [], series, schemProbed, isAc, false);
      const summary = [...schemProbed].filter(s => series[s]).map(s => {
        const arr = series[s];
        return `${s}: first=${arr[0].toPrecision(5)}, last=${arr[arr.length - 1].toPrecision(5)}`;
      }).join('  |  ');
      document.getElementById('schem-readout').textContent = summary || 'Select a net on the schematic to inspect.';
    }

    async function runSchematicSim() {
      const analysis = document.getElementById('schem-analysis').value;
      const resp = await fetch('/api/schematic/simulate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ document: currentSchem, analysis })
      });
      const data = await resp.json();
      document.getElementById('svg-host').innerHTML = data.svg || '';
      document.getElementById('schem-spice').value = data.spice || '';
      const ercEl = document.getElementById('erc-status');
      if (!data.erc_ok) {
        ercEl.className = 'status-line err';
        ercEl.textContent = 'ERC failed: ' + JSON.stringify(data.diagnostics);
        return;
      }
      ercEl.className = 'status-line ok';
      ercEl.textContent = `ERC PASS | Status=${data.status} | Samples=${(data.axis || []).length} | Click any blue wire or source to cross-probe.`;
      lastSchemResult = data;
      bindSvgCrossProbe();
      renderSchemPlot();
    }

    function loadDeckPreset(key) {
      const p = DECK_PRESETS[key];
      document.getElementById('deck-text').value = p.text;
      document.getElementById('deck-analysis').value = p.analysis;
      document.getElementById('deck-src').value = p.src;
      document.getElementById('deck-start').value = p.start;
      document.getElementById('deck-stop').value = p.stop;
      document.getElementById('deck-step').value = p.step;
    }

    function renderDeckPlot() {
      if (!lastDeckResult) return;
      const chips = document.getElementById('deck-signals');
      chips.innerHTML = '';
      const series = lastDeckResult.series || {};
      Object.keys(series).forEach(sig => {
        const b = document.createElement('button');
        b.className = 'chip' + (deckSelected.has(sig) ? ' on' : '');
        b.textContent = sig;
        b.onclick = () => {
          if (deckSelected.has(sig)) deckSelected.delete(sig); else deckSelected.add(sig);
          renderDeckPlot();
        };
        chips.appendChild(b);
      });
      const an = document.getElementById('deck-analysis').value;
      drawPlot(document.getElementById('deck-canvas'), lastDeckResult.axis || [], series, deckSelected, an === 'ac' || an === 'noise', an === 'hb');
    }

    async function runDeckSim() {
      const an = document.getElementById('deck-analysis').value;
      const payload = {
        deck: document.getElementById('deck-text').value,
        analysis: an,
        source: document.getElementById('deck-src').value,
        start: Number(document.getElementById('deck-start').value),
        stop: Number(document.getElementById('deck-stop').value),
        step: Number(document.getElementById('deck-step').value)
      };
      const resp = await fetch('/api/deck/simulate', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
      });
      const data = await resp.json();
      const st = document.getElementById('deck-status');
      if (data.status !== 'Succeeded') {
        st.className = 'status-line err';
        st.textContent = `Error: ${data.error_message || data.status}`;
        return;
      }
      st.className = 'status-line ok';
      st.textContent = `Succeeded (${an.toUpperCase()}) | Samples=${(data.axis || []).length} | Backend=${data.backend}`;
      lastDeckResult = data;
      const keys = Object.keys(data.series || {});
      deckSelected = new Set(keys.includes('v(out)') ? ['v(out)'] : keys.slice(0, 2));
      renderDeckPlot();
    }

    async function runCampaign() {
      const vdd = Number(document.getElementById('camp-vdd').value);
      const r1 = Number(document.getElementById('camp-r1').value);
      const r2Values = document.getElementById('camp-r2').value.split(',').map(s => Number(s.trim())).filter(Number.isFinite);
      const resp = await fetch('/api/campaign/run', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ vdd, r1, r2Values })
      });
      const data = await resp.json();
      const tb = document.querySelector('#camp-table tbody');
      tb.innerHTML = '';
      const axis = [], vals = [];
      (data.runs || []).forEach(r => {
        axis.push(r.r2_ohms); vals.push(r.v_out);
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>${r.case_id}</td><td>${r.r2_ohms}</td><td class="ok">${r.status}</td><td>${r.v_out.toFixed(6)}</td>`;
        tb.appendChild(tr);
      });
      drawPlot(document.getElementById('camp-canvas'), axis, { 'v(out)': vals }, new Set(['v(out)']), false, false);
    }

    document.getElementById('schem-preset').addEventListener('change', e => {
      currentSchem = structuredClone(SCHEM_PRESETS[e.target.value]);
      renderCompEditor();
      runSchematicSim();
    });
    document.getElementById('schem-analysis').addEventListener('change', runSchematicSim);
    document.getElementById('schem-run').addEventListener('click', runSchematicSim);
    document.getElementById('deck-preset').addEventListener('change', e => { loadDeckPreset(e.target.value); runDeckSim(); });
    document.getElementById('deck-run').addEventListener('click', runDeckSim);
    document.getElementById('camp-run').addEventListener('click', runCampaign);

    async function boot() {
      const info = await (await fetch('/api/info')).json();
      const badge = document.getElementById('abi-badge');
      badge.textContent = `libspice_capi ABI ${info.abi} | .NET ${info.dotnet_runtime}`;
      badge.classList.add('ok');
      document.getElementById('runtime-info').textContent =
        `OSDI=${info.osdi_available} | Audits=${info.audits_available} | Devices=${info.device_kinds.length}`;
      renderCompEditor();
      loadDeckPreset('bsim4');
      await runSchematicSim();
      await runDeckSim();
      await runCampaign();
    }
    boot();
  </script>
</body>
</html>
""";
}
