namespace Spice.App;

/// <summary>
/// Self-contained interactive EDA schematic studio and simulation workbench UI served by
/// <see cref="WorkbenchServer"/>. Delivers RSpice-parity schematic capture (component shelf,
/// pan/zoom canvas, snap grids &amp; cyan alignment guides, orthogonal wiring, mirror/rotate/align,
/// undo/redo, keyboard shortcuts, ERC problems, multi-sheet &amp; subckt-from-sheet, custom
/// component &amp; attached-model library studio, multi-dialect netlist/bundle/BOM/SVG export,
/// versioned .spice.json save/load, live DC V/I overlays, and interactive crosshair waveforms).
/// </summary>
public static class WorkbenchUi
{
    public const string Html = """
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Spice V2 Workbench — EDA Schematic Studio &amp; Native C++20 Engine</title>
  <style>
    :root {
      --bg: #070b14;
      --surface: #0f172a;
      --surface-2: #162033;
      --surface-3: #1e293b;
      --border: #263449;
      --border-hi: #38bdf8;
      --text: #f1f5f9;
      --muted: #94a3b8;
      --dim: #64748b;
      --accent: #38bdf8;
      --accent-dark: #0284c7;
      --green: #10b981;
      --amber: #f59e0b;
      --rose: #f43f5e;
      --indigo: #818cf8;
      --mono: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      --sans: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
    }
    * { box-sizing: border-box; }
    html, body { height: 100%; }
    body {
      margin: 0; font-family: var(--sans); background: var(--bg); color: var(--text);
      display: flex; flex-direction: column; overflow: hidden;
    }
    header {
      display: flex; align-items: center; justify-content: space-between; gap: 12px;
      padding: 8px 16px; background: linear-gradient(180deg, #111b2e 0%, #0c1322 100%);
      border-bottom: 1px solid var(--border); flex-shrink: 0;
    }
    .brand { display: flex; align-items: center; gap: 10px; font-weight: 700; font-size: 14px; letter-spacing: 0.02em; }
    .logo-mark {
      width: 24px; height: 24px; border-radius: 6px;
      background: linear-gradient(135deg, #0ea5e9, #6366f1);
      display: grid; place-items: center; font-family: var(--mono); font-size: 12px; color: #fff; font-weight: 800;
      box-shadow: 0 0 12px rgba(56, 189, 248, 0.35);
    }
    .badge {
      font-size: 11px; padding: 3px 9px; border-radius: 999px;
      background: rgba(56, 189, 248, 0.12); color: var(--accent); border: 1px solid rgba(56, 189, 248, 0.3);
      font-family: var(--mono); white-space: nowrap;
    }
    .badge.ok { background: rgba(16, 185, 129, 0.14); color: #34d399; border-color: rgba(16, 185, 129, 0.35); }
    .nav-tabs { display: flex; gap: 4px; align-items: center; }
    .tab-btn {
      background: transparent; color: var(--muted); border: 1px solid transparent;
      padding: 6px 12px; border-radius: 6px; cursor: pointer; font-weight: 600; font-size: 12.5px;
      transition: all 0.15s;
    }
    .tab-btn:hover { color: var(--text); background: rgba(255,255,255,0.04); }
    .tab-btn.active { background: var(--surface-3); color: #fff; border-color: var(--border); box-shadow: inset 0 -2px 0 var(--accent); }
    .icon-btn {
      background: var(--surface-2); color: var(--text); border: 1px solid var(--border);
      border-radius: 6px; padding: 5px 9px; font-size: 12px; cursor: pointer; display: inline-flex; align-items: center; gap: 5px;
      transition: all 0.12s; white-space: nowrap;
    }
    .icon-btn:hover { background: var(--surface-3); border-color: #475569; }
    .icon-btn.primary { background: var(--accent-dark); border-color: var(--accent); color: #fff; font-weight: 600; }
    .icon-btn.primary:hover { background: #0369a1; }
    .icon-btn.sim-btn { background: #059669; border-color: #34d399; color: #fff; font-weight: 700; padding: 5px 14px; }
    .icon-btn.sim-btn:hover { background: #047857; }
    .icon-btn.mode-on { background: #b45309; border-color: var(--amber); color: #fff; font-weight: 600; }
    .icon-btn.danger { color: #fda4af; border-color: rgba(244, 63, 94, 0.35); }
    .icon-btn.danger:hover { background: rgba(244, 63, 94, 0.18); }

    select, input, textarea {
      background: var(--surface-2); color: var(--text); border: 1px solid var(--border);
      border-radius: 6px; padding: 5px 8px; font-size: 12px; font-family: var(--sans);
    }
    input:focus, select:focus, textarea:focus { outline: none; border-color: var(--accent); }
    textarea { font-family: var(--mono); width: 100%; resize: vertical; line-height: 1.45; }

    main { flex: 1; display: flex; flex-direction: column; min-height: 0; overflow: hidden; }
    .tab-pane { display: none; flex: 1; min-height: 0; overflow: hidden; }
    .tab-pane.active { display: flex; flex-direction: column; }

    /* ---- RIBBON TOOLBAR ---- */
    .ribbon {
      display: flex; flex-wrap: wrap; align-items: center; gap: 8px;
      padding: 7px 14px; background: var(--surface); border-bottom: 1px solid var(--border);
      flex-shrink: 0;
    }
    .rib-group {
      display: inline-flex; align-items: center; gap: 4px;
      padding-right: 8px; border-right: 1px solid var(--border);
    }
    .rib-group:last-child { border-right: none; padding-right: 0; }
    label.chk { display: inline-flex; align-items: center; gap: 4px; font-size: 11.5px; color: var(--muted); cursor: pointer; }

    /* ---- 3-PANE EDA SCHEMATIC STUDIO ---- */
    .studio-grid {
      flex: 1; min-height: 0; display: grid;
      grid-template-columns: 248px minmax(420px, 1fr) 410px;
      overflow: hidden;
    }
    .dock {
      background: var(--surface); border-right: 1px solid var(--border);
      display: flex; flex-direction: column; min-height: 0; overflow: hidden;
    }
    .dock.right { border-right: none; border-left: 1px solid var(--border); }
    .dock-tabs {
      display: flex; background: #0b1220; border-bottom: 1px solid var(--border); flex-shrink: 0;
    }
    .dock-tab {
      flex: 1; background: transparent; border: none; color: var(--muted);
      padding: 8px 6px; font-size: 11.5px; font-weight: 600; cursor: pointer;
      border-bottom: 2px solid transparent;
    }
    .dock-tab.active { color: var(--accent); border-bottom-color: var(--accent); background: rgba(56,189,248,0.06); }
    .dock-body { flex: 1; min-height: 0; overflow-y: auto; padding: 10px; display: flex; flex-direction: column; gap: 10px; }
    .dock-section { display: none; flex-direction: column; gap: 10px; }
    .dock-section.active { display: flex; }

    .sec-title {
      font-size: 10.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.08em;
      color: var(--dim); margin: 2px 0; display: flex; justify-content: space-between; align-items: center;
    }

    /* ---- COMPONENT SHELF ---- */
    .shelf-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 6px; }
    .shelf-item {
      background: var(--surface-2); border: 1px solid var(--border); border-radius: 7px;
      padding: 6px; cursor: pointer; display: flex; flex-direction: column; align-items: center; gap: 4px;
      transition: all 0.12s; text-align: center;
    }
    .shelf-item:hover { border-color: var(--accent); background: #1a2740; transform: translateY(-1px); }
    .shelf-item svg { width: 58px; height: 34px; }
    .shelf-item .sname { font-size: 11px; font-weight: 600; color: var(--text); line-height: 1.2; }
    .shelf-item .stag { font-size: 9.5px; font-family: var(--mono); color: var(--accent); }
    .shelf-item.custom-part { border-color: rgba(129, 140, 248, 0.4); }
    .shelf-item.custom-part .stag { color: #c7d2fe; }

    /* ---- CENTER CANVAS ---- */
    .canvas-stage {
      position: relative; display: flex; flex-direction: column; min-height: 0; background: var(--bg); overflow: hidden;
    }
    .sheet-bar {
      display: flex; align-items: center; justify-content: space-between; gap: 6px;
      padding: 4px 10px; background: #0a101e; border-bottom: 1px solid var(--border); flex-shrink: 0;
    }
    .sheet-tabs { display: flex; align-items: center; gap: 4px; overflow-x: auto; }
    .sheet-tab {
      background: var(--surface-2); color: var(--muted); border: 1px solid var(--border);
      border-radius: 5px; padding: 3px 10px; font-size: 11.5px; font-weight: 600; cursor: pointer;
      display: inline-flex; align-items: center; gap: 6px; white-space: nowrap;
    }
    .sheet-tab.active {
      background: rgba(56, 189, 248, 0.16); color: #fff; border-color: var(--accent);
    }
    .sheet-tab .sbadge {
      font-family: var(--mono); font-size: 9.5px; color: var(--dim);
    }
    #svg-host {
      flex: 1; min-height: 0; width: 100%; display: flex; align-items: stretch; justify-content: stretch;
      overflow: hidden; user-select: none; cursor: default;
    }
    #svg-host svg { width: 100%; height: 100%; touch-action: none; }
    #svg-host .spice-net { cursor: pointer; }
    #svg-host .spice-net:hover .spice-wire-path { stroke: #f59e0b; stroke-width: 3.6; }
    #svg-host .spice-net.probed .spice-wire-path { stroke: #f43f5e; stroke-width: 3.6; }
    #svg-host .spice-component { cursor: grab; }
    #svg-host .spice-component.dragging { cursor: grabbing; opacity: 0.9; }
    #svg-host .spice-component.selected rect:first-of-type { stroke: #38bdf8; stroke-width: 2.2; filter: drop-shadow(0 0 6px rgba(56,189,248,0.45)); }
    #svg-host .spice-pin { cursor: crosshair; transition: r 0.1s; }
    #svg-host .spice-pin:hover { fill: #f59e0b; stroke: #fef3c7; r: 6; }
    #svg-host .spice-pin.wire-start { fill: #10b981; stroke: #a7f3d0; r: 6.5; }
    .zoom-hud {
      position: absolute; right: 12px; bottom: 38px;
      display: flex; align-items: center; gap: 3px;
      background: rgba(15, 23, 42, 0.9); border: 1px solid var(--border); border-radius: 8px; padding: 3px;
      backdrop-filter: blur(6px);
    }
    .zoom-hud button { background: transparent; border: none; color: var(--text); padding: 3px 7px; border-radius: 4px; cursor: pointer; font-family: var(--mono); font-size: 11.5px; }
    .zoom-hud button:hover { background: var(--surface-3); }
    .canvas-statusbar {
      display: flex; align-items: center; justify-content: space-between; gap: 10px;
      padding: 5px 12px; background: #0a101e; border-top: 1px solid var(--border);
      font-family: var(--mono); font-size: 11px; color: var(--muted); flex-shrink: 0;
    }

    /* ---- WAVEFORM & TABLES ---- */
    canvas.plot { width: 100%; height: 225px; background: #080e1a; border: 1px solid var(--border); border-radius: 8px; cursor: crosshair; }
    .chips { display: flex; flex-wrap: wrap; gap: 5px; }
    .chip {
      font-family: var(--mono); font-size: 11px; padding: 3px 8px; border-radius: 999px;
      border: 1px solid var(--border); background: var(--surface-2); color: var(--muted); cursor: pointer;
    }
    .chip.on { color: #fff; border-color: var(--accent); background: rgba(56, 189, 248, 0.22); }
    table { width: 100%; border-collapse: collapse; font-size: 11.5px; font-family: var(--mono); }
    th, td { border-bottom: 1px solid var(--border); padding: 4px 6px; text-align: left; }
    th { color: var(--dim); font-weight: 600; font-size: 10.5px; }
    .op-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .ok { color: #34d399; }
    .err { color: #fb7185; }
    .amber { color: #fcd34d; }
    .nav-list { display: flex; flex-direction: column; gap: 3px; }
    .nav-row {
      display: flex; justify-content: space-between; align-items: center; gap: 6px;
      padding: 5px 8px; border-radius: 6px; background: var(--surface-2); border: 1px solid transparent;
      font-family: var(--mono); font-size: 11.5px; cursor: pointer;
    }
    .nav-row:hover { border-color: var(--border-hi); }
    .nav-row.active { border-color: var(--accent); background: rgba(56,189,248,0.12); }

    /* ---- LIBRARY STUDIO & OTHER WORKSPACES ---- */
    .split-workspace {
      flex: 1; min-height: 0; display: grid; grid-template-columns: minmax(380px, 1.05fr) minmax(420px, 1.15fr);
      gap: 14px; padding: 14px; overflow-y: auto;
    }
    .card {
      background: var(--surface); border: 1px solid var(--border); border-radius: 10px;
      padding: 14px; display: flex; flex-direction: column; gap: 10px; min-height: 0;
    }
    .card h3 { margin: 0; font-size: 11.5px; text-transform: uppercase; letter-spacing: 0.07em; color: var(--muted); }
    .lib-cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 10px; overflow-y: auto; padding-right: 4px; }
    .lib-card {
      background: var(--surface-2); border: 1px solid var(--border); border-radius: 9px; padding: 10px;
      display: flex; flex-direction: column; gap: 6px;
    }
    .lib-card.user-owned { border-color: rgba(129, 140, 248, 0.55); }
    .lib-card-head { display: flex; align-items: center; gap: 8px; }
    .lib-card-head svg { width: 54px; height: 36px; background: #080e1a; border-radius: 6px; border: 1px solid var(--border); flex-shrink: 0; }
    .form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .field { display: flex; flex-direction: column; gap: 3px; }
    .field label { font-size: 11px; color: var(--muted); font-weight: 600; }

    /* ---- VECTOR SYMBOL DRAWING CANVAS ---- */
    .sym-studio {
      background: #080e1a; border: 1px solid var(--border); border-radius: 9px;
      padding: 8px; display: flex; flex-direction: column; gap: 7px;
    }
    .sym-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; }
    .sym-swatch {
      width: 18px; height: 18px; border-radius: 4px; border: 2px solid transparent;
      cursor: pointer; padding: 0;
    }
    .sym-swatch.active { border-color: #fff; box-shadow: 0 0 0 1px var(--accent); }
    #sym-canvas {
      width: 100%; height: 220px; background: #060a13; border: 1px solid var(--border);
      border-radius: 7px; cursor: crosshair; user-select: none; touch-action: none;
    }
    #sym-canvas .sym-pin-node { cursor: grab; }
    #sym-canvas .sym-pin-node:hover circle { fill: #f59e0b; r: 4.5; }
    .sym-meta {
      display: flex; justify-content: space-between; align-items: center;
      font-family: var(--mono); font-size: 10.5px; color: var(--muted);
    }

    /* ---- MODAL ---- */
    .modal-backdrop {
      position: fixed; inset: 0; background: rgba(2, 6, 23, 0.72); backdrop-filter: blur(4px);
      display: none; align-items: center; justify-content: center; z-index: 50;
    }
    .modal-backdrop.open { display: flex; }
    .modal {
      background: var(--surface); border: 1px solid var(--border); border-radius: 12px;
      width: min(640px, 92vw); max-height: 85vh; overflow-y: auto; padding: 18px;
      display: flex; flex-direction: column; gap: 12px; box-shadow: 0 24px 60px rgba(0,0,0,0.6);
    }
    .kbd {
      font-family: var(--mono); font-size: 11px; padding: 2px 6px; border-radius: 4px;
      background: var(--surface-3); border: 1px solid #475569; color: var(--text);
    }
    @media (max-width: 1120px) {
      body { overflow: auto; }
      .studio-grid { grid-template-columns: 1fr; }
      .dock { max-height: 360px; border: none; border-bottom: 1px solid var(--border); }
      .canvas-stage { min-height: 460px; }
      .split-workspace { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <div class="logo-mark">S2</div>
      <span>Spice V2 Workbench</span>
      <span id="abi-badge" class="badge">Connecting...</span>
    </div>
    <div class="nav-tabs">
      <button class="tab-btn active" data-tab="schematic">1. Schematic Studio</button>
      <button class="tab-btn" data-tab="library">2. Component &amp; Model Library</button>
      <button class="tab-btn" data-tab="deck">3. SPICE / Spectre / RF Deck</button>
      <button class="tab-btn" data-tab="campaign">4. Parallel Campaign</button>
      <button class="tab-btn" data-tab="cameo">5. Cameo Studio</button>
    </div>
    <div style="display:flex;align-items:center;gap:8px">
      <span id="runtime-info" class="badge">Native C++20</span>
      <button id="btn-shortcuts" class="icon-btn" title="Keyboard Shortcuts">? Keys</button>
    </div>
  </header>

  <main>
    <!-- ========================================================================= -->
    <!-- TAB 1: EDA SCHEMATIC STUDIO                                               -->
    <!-- ========================================================================= -->
    <section id="tab-schematic" class="tab-pane active">
      <div class="ribbon">
        <div class="rib-group">
          <select id="schem-preset" title="Load circuit template">
            <option value="divider">Resistive Divider (10V, 1k, 2k)</option>
            <option value="rc">RC Low-Pass Filter (5V, 1k, 1uF)</option>
            <option value="multisheet_rc">Multi-Sheet: 2-Stage Cascaded RC Filter</option>
            <option value="diode">1N4148 Diode Forward Bias</option>
            <option value="bsim4_inv">BSIM4 65nm CMOS Inverter</option>
            <option value="bjt_amp">2N2222 NPN Common-Emitter Amp</option>
            <option value="opamp_buf">Ideal Op-Amp Non-Inverting (Gain=3)</option>
          </select>
        </div>

        <div class="rib-group">
          <button id="btn-save-schem" class="icon-btn" title="Save versioned .spice.json (Ctrl+S)">Save</button>
          <button id="btn-open-schem" class="icon-btn" title="Open saved .spice.json file">Open</button>
          <input id="file-open-schem" type="file" accept=".json,.spice.json" style="display:none" />
          <button id="btn-import-spice" class="icon-btn" title="Import legacy SPICE deck onto schematic">Import SPICE</button>
          <button id="btn-import-spicenet" class="icon-btn primary" title="Import hierarchical .spicenet, polyglot .spice.svg, or VeloSim JSON (auto-layout)">Import .spicenet</button>
        </div>

        <div class="rib-group">
          <button id="btn-undo" class="icon-btn" title="Undo (Ctrl+Z)">Undo</button>
          <button id="btn-redo" class="icon-btn" title="Redo (Ctrl+Y)">Redo</button>
          <button id="btn-rotate" class="icon-btn" title="Rotate selected 90° (Ctrl+R)">Rot 90°</button>
          <button id="btn-mirror" class="icon-btn" title="Mirror horizontal (Ctrl+M)">Mirror</button>
          <button id="btn-align-x" class="icon-btn" title="Align all components horizontally">Align Y</button>
          <button id="btn-snap-all" class="icon-btn" title="Snap all components to grid">Snap</button>
          <button id="btn-delete" class="icon-btn danger" title="Delete selected component (Del)">Del</button>
        </div>

        <div class="rib-group">
          <button id="mode-move" class="icon-btn mode-on" title="Select, move with snap lines, or click nets to probe (Esc)">Select / Probe</button>
          <button id="mode-wire" class="icon-btn" title="Click two pins to draw an orthogonal wire (W)">Wire (W)</button>
          <button id="btn-add-directive" class="icon-btn" title="Add SPICE directive / note to sheet (T)">+ Directive</button>
          <button id="btn-add-port" class="icon-btn" title="Add off-sheet / interface port pin (P)">+ Port</button>
        </div>

        <div class="rib-group">
          <select id="grid-style" title="Grid display style">
            <option value="lines">Grid: Lines</option>
            <option value="dots">Grid: Dots</option>
            <option value="off">Grid: Off</option>
          </select>
          <select id="grid-pitch" title="Snap grid pitch">
            <option value="10">10px</option>
            <option value="20" selected>20px</option>
            <option value="40">40px</option>
          </select>
          <label class="chk"><input id="chk-op" type="checkbox" checked /> DC V/I</label>
        </div>

        <div class="rib-group">
          <select id="schem-analysis" title="Simulation analysis">
            <option value="tran">TRAN (BDF2) + DC OP</option>
            <option value="ac">AC Sweep + DC OP</option>
            <option value="dc">DC Sweep + DC OP</option>
            <option value="op">DC Operating Point</option>
          </select>
          <button id="schem-run" class="icon-btn sim-btn" title="Run native C++20 simulation (F5)">&#9654; Simulate</button>
        </div>
      </div>

      <div class="studio-grid">
        <!-- LEFT DOCK: COMPONENT SHELF & NAVIGATOR -->
        <aside class="dock">
          <div class="dock-tabs">
            <button class="dock-tab active" data-dock="left" data-pane="shelf">Component Shelf</button>
            <button class="dock-tab" data-dock="left" data-pane="navigator">Navigator &amp; ERC</button>
          </div>
          <div class="dock-body">
            <div id="left-shelf" class="dock-section active">
              <input id="shelf-filter" placeholder="Search parts &amp; models..." />
              <div class="sec-title"><span>Primitives</span><span>Keys: R C L V I D</span></div>
              <div id="shelf-primitives" class="shelf-grid"></div>
              <div class="sec-title">
                <span>Foundation &amp; Custom Library</span>
                <button id="btn-jump-lib" class="icon-btn" style="padding:2px 6px;font-size:10.5px">+ Custom</button>
              </div>
              <div id="shelf-library" class="shelf-grid"></div>
            </div>

            <div id="left-navigator" class="dock-section">
              <div class="sec-title">
                <span>Sheets &amp; Subsheets</span>
                <button id="btn-sheet-to-part" class="icon-btn primary" style="padding:2px 7px;font-size:10.5px">Save as .SUBCKT</button>
              </div>
              <div id="nav-sheets" class="nav-list"></div>

              <div class="sec-title"><span>Instances (Active Sheet)</span><span id="nav-inst-count">0</span></div>
              <div id="nav-instances" class="nav-list"></div>

              <div class="sec-title"><span>Nets (Click to Probe)</span><span id="nav-net-count">0</span></div>
              <div id="nav-nets" class="nav-list"></div>

              <div class="sec-title"><span>ERC Diagnostics</span><span id="nav-erc-count">0</span></div>
              <div id="nav-erc" class="nav-list"></div>
            </div>
          </div>
        </aside>

        <!-- CENTER STAGE: MULTI-SHEET TAB BAR + INTERACTIVE PAN/ZOOM SCHEMATIC CANVAS -->
        <section class="canvas-stage">
          <div id="sheet-bar" class="sheet-bar">
            <div id="sheet-tabs" class="sheet-tabs"></div>
            <div style="display:flex;align-items:center;gap:4px;flex-shrink:0">
              <button id="btn-sheet-parent" class="icon-btn" style="padding:2px 7px;font-size:11px;display:none" title="Return to Root Sheet (Shift+H)">&#8592; Root</button>
              <button id="btn-sheet-add" class="icon-btn" style="padding:2px 7px;font-size:11px" title="Add a new linked subsheet">+ Sheet</button>
              <button id="btn-sheet-rename" class="icon-btn" style="padding:2px 7px;font-size:11px" title="Rename active sheet">Rename</button>
              <button id="btn-sheet-del" class="icon-btn danger" style="padding:2px 7px;font-size:11px;display:none" title="Delete active subsheet">Del Sheet</button>
            </div>
          </div>
          <div id="svg-host"></div>
          <div class="zoom-hud">
            <button id="zoom-out" title="Zoom out (-)">−</button>
            <button id="zoom-reset" title="Reset 100%">100%</button>
            <button id="zoom-in" title="Zoom in (+)">+</button>
            <button id="zoom-fit" title="Fit sheet (F)">Fit</button>
          </div>
          <div class="canvas-statusbar">
            <span id="erc-status">Ready — drag parts to see cyan alignment snap guides, or click nets to cross-probe.</span>
            <span id="cursor-coords">X: 0  Y: 0  |  Grid: 20px</span>
          </div>
        </section>

        <!-- RIGHT DOCK: WAVEFORMS/DC OP, INSPECTOR, MULTI-FORMAT EXPORT -->
        <aside class="dock right">
          <div class="dock-tabs">
            <button class="dock-tab active" data-dock="right" data-pane="results">Results &amp; DC V/I</button>
            <button class="dock-tab" data-dock="right" data-pane="inspector">Inspector</button>
            <button class="dock-tab" data-dock="right" data-pane="export">Netlist &amp; Export</button>
          </div>
          <div class="dock-body">
            <!-- RESULTS & WAVEFORMS -->
            <div id="right-results" class="dock-section active">
              <div class="sec-title">
                <span>Live Cross-Probed Waveforms</span>
                <div style="display:flex;gap:4px">
                  <button id="btn-bode-toggle" class="icon-btn" style="padding:2px 6px;font-size:10.5px">dB: Off</button>
                  <button id="btn-csv-wave" class="icon-btn" style="padding:2px 6px;font-size:10.5px">CSV</button>
                </div>
              </div>
              <div class="chips" id="schem-signals"></div>
              <canvas id="schem-canvas" class="plot" width="700" height="260"></canvas>
              <div id="schem-readout" style="font-family:var(--mono);font-size:11px;color:var(--muted)">Hover plot for cursor readout. Click schematic nets to probe.</div>

              <div class="sec-title"><span>DC Operating Point (Live Node V &amp; Branch I)</span></div>
              <div class="op-grid">
                <table id="op-v-table">
                  <thead><tr><th>Net</th><th>DC Voltage</th></tr></thead>
                  <tbody></tbody>
                </table>
                <table id="op-i-table">
                  <thead><tr><th>Element</th><th>DC Current</th></tr></thead>
                  <tbody></tbody>
                </table>
              </div>
            </div>

            <!-- INSPECTOR -->
            <div id="right-inspector" class="dock-section">
              <div class="sec-title"><span>Selected Component Properties</span></div>
              <div id="inspector-host" style="display:flex;flex-direction:column;gap:8px"></div>
              <div class="sec-title"><span>All Sheet Components</span></div>
              <div id="comp-editor"></div>
            </div>

            <!-- MULTI-DIALECT NETLIST & ARTIFACT EXPORT -->
            <div id="right-export" class="dock-section">
              <div class="sec-title"><span>Netlist &amp; Publication Export Options</span></div>
              <div style="display:flex;gap:6px;flex-wrap:wrap">
                <select id="export-format" style="flex:1">
                  <option value="spicenet">Flagship Hierarchical Netlist (.spicenet)</option>
                  <option value="polyglot_svg">Polyglot Self-Viewing Schematic (.spice.svg)</option>
                  <option value="velosim">VeloSim Ingestion Package (.velosim.json)</option>
                  <option value="circuit_json">tscircuit-Aligned Circuit JSON (.circuit.json)</option>
                  <option value="spice" selected>SPICE3f5 Canonical (.cir)</option>
                  <option value="ngspice">ngspice Dialect (.cir + .SAVE)</option>
                  <option value="xyce">Xyce Dialect (.cir + .PRINT CSV)</option>
                  <option value="ltspice">LTspice Compatible (.net + .backanno)</option>
                  <option value="spectre">Spectre Dialect (.scs)</option>
                  <option value="cameo">Cameo Script (.cameo)</option>
                  <option value="bom">Bill of Materials (.bom.csv)</option>
                  <option value="bundle">Self-Contained Bundle + SHA-256 (.bundle.json)</option>
                  <option value="json">Versioned Schematic Save (.spice.json)</option>
                  <option value="svg">Vector SVG — Dark Studio (.svg)</option>
                  <option value="svg_light">Publication SVG — Light Print (.svg)</option>
                </select>
                <button id="btn-export-copy" class="icon-btn">Copy</button>
                <button id="btn-export-download" class="icon-btn primary">Download</button>
              </div>
              <div id="export-meta" style="font-family:var(--mono);font-size:11px;color:var(--dim)"></div>
              <textarea id="schem-spice" rows="18" readonly></textarea>
            </div>
          </div>
        </aside>
      </div>
    </section>

    <!-- ========================================================================= -->
    <!-- TAB 2: CUSTOM COMPONENT, VECTOR SYMBOL CANVAS & MODEL LIBRARY STUDIO      -->
    <!-- ========================================================================= -->
    <section id="tab-library" class="tab-pane">
      <div class="split-workspace">
        <div class="card">
          <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap">
            <h3>Component &amp; Model Library (Foundation + Persistent User Library)</h3>
            <div style="display:flex;gap:5px;align-items:center">
              <button id="btn-lib-export-json" class="icon-btn" title="Export custom library as .spice-lib.json">Export JSON</button>
              <button id="btn-lib-import-json" class="icon-btn" title="Import .spice-lib.json file">Import JSON</button>
              <input id="file-lib-import-json" type="file" accept=".json,.spice-lib.json" style="display:none" />
              <span id="lib-path-badge" class="badge">library.spice-lib.json</span>
            </div>
          </div>
          <div style="display:flex;gap:6px">
            <input id="lib-search" placeholder="Filter by name, category, or attached model..." style="flex:1" />
            <button id="btn-lib-new" class="icon-btn primary">+ New Blank Part</button>
          </div>

          <!-- BULK .LIB / .MOD / .SUB SPICE LIBRARY IMPORTER -->
          <div style="background:var(--surface-2);border:1px solid var(--border);border-radius:8px;padding:10px;display:flex;flex-direction:column;gap:8px">
            <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap">
              <span style="font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:0.06em;color:var(--accent)">Bulk Import SPICE .LIB / .MOD / .SUB</span>
              <div style="display:flex;gap:6px;align-items:center">
                <button id="btn-bulk-load-file" class="icon-btn" style="padding:3px 8px;font-size:11px">Load .LIB/.MOD/.SUB File</button>
                <input id="file-bulk-spice" type="file" accept=".lib,.mod,.sub,.cir,.sp,.txt" style="display:none" />
              </div>
            </div>
            <textarea id="bulk-spice-text" rows="4" placeholder="Paste vendor .LIB / .MOD / .SUB text containing .MODEL cards (D, NPN, PNP, NMOS, PMOS, VDMOS, NJF, PJF, NMF, SW, CSW, CORE) and .SUBCKT ... .ENDS blocks..."></textarea>
            <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap">
              <input id="bulk-spice-cat" value="Vendor Import" placeholder="Default category" style="width:155px" />
              <label class="chk"><input id="bulk-spice-overwrite" type="checkbox" /> Overwrite existing</label>
              <button id="btn-bulk-preview" class="icon-btn">Preview (Dry Run)</button>
              <button id="btn-bulk-import" class="icon-btn sim-btn">Bulk Import All</button>
            </div>
            <div id="bulk-import-status" style="font-family:var(--mono);font-size:11px;color:var(--muted)"></div>
            <div id="bulk-import-preview" style="display:grid;grid-template-columns:repeat(auto-fill,minmax(175px,1fr));gap:6px"></div>
            <div id="bulk-import-diags" style="display:flex;flex-direction:column;gap:3px;font-family:var(--mono);font-size:11px"></div>
          </div>

          <div id="lib-cards" class="lib-cards"></div>
        </div>

        <div class="card" style="overflow-y:auto">
          <div style="display:flex;justify-content:space-between;align-items:center;gap:8px">
            <h3>Custom Symbol Canvas &amp; Attached Model Studio</h3>
            <div style="display:flex;gap:4px">
              <button class="icon-btn" data-tpl="diode">Tpl: Diode</button>
              <button class="icon-btn" data-tpl="mosfet">Tpl: BSIM4</button>
              <button class="icon-btn" data-tpl="subckt">Tpl: .SUBCKT</button>
            </div>
          </div>

          <!-- INTERACTIVE VECTOR SYMBOL DRAWING CANVAS -->
          <div class="sym-studio">
            <div class="sym-toolbar">
              <button class="icon-btn mode-on" data-sym-tool="line" title="Draw snapped line">Line</button>
              <button class="icon-btn" data-sym-tool="rect" title="Draw snapped rectangle">Rect</button>
              <button class="icon-btn" data-sym-tool="circle" title="Draw snapped circle">Circle</button>
              <button class="icon-btn" data-sym-tool="polygon" title="Click points, double-click to finish polygon">Poly</button>
              <button class="icon-btn" data-sym-tool="text" title="Place monospace label on symbol">Text</button>
              <button class="icon-btn" data-sym-tool="pin" title="Place electrical terminal pin">Pin</button>
              <button class="icon-btn" data-sym-tool="select" title="Drag pins on 10px grid">Move Pin</button>
              <span style="width:1px;height:18px;background:var(--border);margin:0 2px"></span>
              <button class="sym-swatch active" data-sym-color="#38bdf8" style="background:#38bdf8" title="Sky"></button>
              <button class="sym-swatch" data-sym-color="#e2e8f0" style="background:#e2e8f0" title="White"></button>
              <button class="sym-swatch" data-sym-color="#10b981" style="background:#10b981" title="Emerald"></button>
              <button class="sym-swatch" data-sym-color="#f59e0b" style="background:#f59e0b" title="Amber"></button>
              <button class="sym-swatch" data-sym-color="#f43f5e" style="background:#f43f5e" title="Rose"></button>
              <label class="chk" style="margin-left:3px"><input id="sym-fill-chk" type="checkbox" /> Fill</label>
              <span style="width:1px;height:18px;background:var(--border);margin:0 2px"></span>
              <button id="sym-undo" class="icon-btn" title="Undo last drawn shape">Undo</button>
              <button id="sym-clear" class="icon-btn danger" title="Clear custom vector shapes (reverts to Symbol Style preset)">Clear</button>
            </div>
            <div class="sym-toolbar">
              <span style="font-size:10.5px;color:var(--dim);font-weight:600">Starters:</span>
              <button class="icon-btn" data-sym-starter="ic" style="padding:2px 7px;font-size:11px">IC Box</button>
              <button class="icon-btn" data-sym-starter="opamp" style="padding:2px 7px;font-size:11px">Op-Amp</button>
              <button class="icon-btn" data-sym-starter="diode" style="padding:2px 7px;font-size:11px">Diode / LED</button>
              <button class="icon-btn" data-sym-starter="pi" style="padding:2px 7px;font-size:11px">Pi Network</button>
              <button class="icon-btn" data-sym-starter="xfmr" style="padding:2px 7px;font-size:11px">Buffer / Amp</button>
            </div>
            <svg id="sym-canvas" viewBox="-60 -40 120 80" xmlns="http://www.w3.org/2000/svg"></svg>
            <div class="sym-meta">
              <span id="sym-status">Draw shapes or place pins on the 10px snap grid (Origin 0,0 marked by crosshair).</span>
              <span id="sym-counts">0 shapes · 2 pins</span>
            </div>
          </div>

          <div class="form-grid">
            <div class="field"><label>Component ID (unique)</label><input id="lib-id" value="user.diode.led_red" /></div>
            <div class="field"><label>Display Name</label><input id="lib-name" value="Red LED (1.8V)" /></div>
            <div class="field"><label>Category</label><input id="lib-cat" value="Custom Library" /></div>
            <div class="field"><label>Base Device Kind</label>
              <select id="lib-kind">
                <option value="spice.diode">spice.diode (D)</option>
                <option value="spice.mosfet">spice.mosfet (M — BSIM/MOS)</option>
                <option value="spice.vdmos">spice.vdmos (M — Power VDMOS)</option>
                <option value="spice.bjt">spice.bjt (Q — Gummel-Poon)</option>
                <option value="spice.jfet">spice.jfet (J — JFET)</option>
                <option value="spice.mesfet">spice.mesfet (Z — Statz/Curtice MESFET)</option>
                <option value="spice.switch">spice.switch (S — Voltage Switch)</option>
                <option value="spice.cswitch">spice.cswitch (W — Current Switch)</option>
                <option value="spice.core">spice.core (L+CORE — Saturable Core)</option>
                <option value="spice.subckt">spice.subckt (X — Custom Macro)</option>
                <option value="spice.osdi">spice.osdi (X — Compiled Verilog-A)</option>
                <option value="spice.resistor">spice.resistor (R)</option>
                <option value="spice.capacitor">spice.capacitor (C)</option>
                <option value="spice.inductor">spice.inductor (L)</option>
              </select>
            </div>
            <div class="field"><label>Fallback Symbol Style</label>
              <select id="lib-sym">
                <option value="custom">Custom Vector Canvas</option>
                <option value="diode" selected>Diode</option>
                <option value="zener">Zener Diode</option>
                <option value="led">LED</option>
                <option value="schottky">Schottky Diode</option>
                <option value="nmos">NMOS MOSFET</option>
                <option value="pmos">PMOS MOSFET</option>
                <option value="vdmos">N-Ch Power VDMOS</option>
                <option value="pvdmos">P-Ch Power VDMOS</option>
                <option value="npn">NPN BJT</option>
                <option value="pnp">PNP BJT</option>
                <option value="jfet">JFET</option>
                <option value="mesfet">RF MESFET</option>
                <option value="switch">Voltage Switch (SW)</option>
                <option value="cswitch">Current Switch (CSW)</option>
                <option value="core">Saturable Core (CORE)</option>
                <option value="opamp">Op-Amp Triangle</option>
                <option value="ic_box">IC / Subcircuit Block</option>
              </select>
            </div>
            <div class="field"><label>Attached Model Kind</label>
              <select id="lib-mkind">
                <option value="spiceModelCard">SPICE .MODEL Card</option>
                <option value="subcircuit">SPICE .SUBCKT Block</option>
                <option value="osdi">Compiled Verilog-A (.OSDI)</option>
              </select>
            </div>
          </div>
          <div class="form-grid">
            <div class="field"><label>Attached Model / Subckt Name</label><input id="lib-mname" value="LED_RED" /></div>
            <div class="field"><label>Description</label><input id="lib-desc" value="Custom red GaAsP LED with attached SPICE .MODEL card." /></div>
          </div>
          <div class="form-grid">
            <div class="field">
              <label>Ordered Pins (name,x,y,role — Netlist Order!)</label>
              <textarea id="lib-pins" rows="3">p,-40,0,anode
n,40,0,cathode</textarea>
            </div>
            <div class="field">
              <label>Default Parameters (key=value per line)</label>
              <textarea id="lib-params" rows="3">is_amps=1e-20
n_emission=1.85</textarea>
            </div>
          </div>
          <div class="field">
            <label>Attached SPICE Model / .SUBCKT Definition</label>
            <textarea id="lib-model-text" rows="5">.MODEL LED_RED D(IS=1e-20 N=1.85 RS=4.0 CJO=15p)</textarea>
          </div>
          <div style="display:flex;gap:8px;align-items:center">
            <button id="btn-lib-save" class="icon-btn primary">Save Component to Library</button>
            <button id="btn-lib-save-place" class="icon-btn sim-btn">Save &amp; Place on Schematic</button>
            <span id="lib-status" style="font-family:var(--mono);font-size:11.5px;color:var(--muted)"></span>
          </div>
        </div>
      </div>
    </section>

    <!-- ========================================================================= -->
    <!-- TAB 3: FULL-ENGINE SPICE3f5 / SPECTRE / BSIM4 / RF DECK WORKBENCH         -->
    <!-- ========================================================================= -->
    <section id="tab-deck" class="tab-pane">
      <div class="split-workspace">
        <div class="card">
          <h3>SPICE3f5 / Spectre / BSIM4 / RF / LTRA / TRNOISE Netlist Studio</h3>
          <div style="display:flex;flex-wrap:wrap;gap:6px">
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
            <button id="deck-run" class="icon-btn sim-btn">&#9654; Run Native C++20 Engine</button>
          </div>
          <textarea id="deck-text" rows="16"></textarea>
          <div style="display:flex;flex-wrap:wrap;gap:8px;align-items:center;font-size:12px">
            <label>Source: <input id="deck-src" value="Vin" size="8" /></label>
            <label>Start: <input id="deck-start" value="0" size="7" /></label>
            <label>Stop: <input id="deck-stop" value="1.2" size="7" /></label>
            <label>Step/Pts: <input id="deck-step" value="0.05" size="7" /></label>
          </div>
          <div id="deck-status" style="font-family:var(--mono);font-size:11.5px;color:var(--muted)"></div>
        </div>
        <div class="card">
          <h3>Engine Output Waveform</h3>
          <div class="chips" id="deck-signals"></div>
          <canvas id="deck-canvas" class="plot" style="height:340px" width="700" height="340"></canvas>
          <div id="deck-readout" style="font-family:var(--mono);font-size:11.5px;color:var(--muted)"></div>
        </div>
      </div>
    </section>

    <!-- ========================================================================= -->
    <!-- TAB 4: PARALLEL CORNER CAMPAIGN                                           -->
    <!-- ========================================================================= -->
    <section id="tab-campaign" class="tab-pane">
      <div class="split-workspace">
        <div class="card">
          <h3>Parallel Multi-Corner Campaign (CampaignRunner)</h3>
          <div style="display:flex;flex-wrap:wrap;gap:8px;align-items:center;font-size:12px">
            <label>Vdd (V): <input id="camp-vdd" value="10" size="6" /></label>
            <label>R1 (Ohm): <input id="camp-r1" value="1000" size="7" /></label>
            <label>R2 Corners: <input id="camp-r2" value="500,1000,2000,4000,9000" size="24" /></label>
            <button id="camp-run" class="icon-btn sim-btn">&#9654; Run Parallel Sweep</button>
          </div>
          <table id="camp-table">
            <thead><tr><th>Case</th><th>R2 (Ohm)</th><th>Status</th><th>V(out) (V)</th></tr></thead>
            <tbody></tbody>
          </table>
        </div>
        <div class="card">
          <h3>Corner Trajectory Plot</h3>
          <canvas id="camp-canvas" class="plot" style="height:320px" width="700" height="320"></canvas>
        </div>
      </div>
    </section>

    <!-- ========================================================================= -->
    <!-- TAB 5: CAMEO SCRIPT STUDIO (HERMETIC .CAMEO + SHARPUNITS + LIVE SIM)      -->
    <!-- ========================================================================= -->
    <section id="tab-cameo" class="tab-pane">
      <div class="split-workspace">
        <div class="card">
          <h3>Cameo Netlist &amp; Simulation Studio (.cameo + SharpUnits)</h3>
          <div style="display:flex;flex-wrap:wrap;gap:6px;align-items:center">
            <select id="cameo-preset" style="flex:1;min-width:200px">
              <option value="rc_units">RC Low-Pass Filter + SharpUnits + Live AC/TRAN</option>
              <option value="velosim_pcb">Mixed-Signal VeloSim + PCB Layout + DRC</option>
              <option value="corner_sweep">4-Corner Parallel Divider Sweep</option>
            </select>
            <button id="cameo-run" class="icon-btn sim-btn">&#9654; Run .cameo Live</button>
            <button id="cameo-load-schem" class="icon-btn primary" disabled>Load into Schematic</button>
          </div>
          <textarea id="cameo-editor" rows="20" style="font-family:var(--mono);font-size:12px;line-height:1.45"></textarea>
          <div id="cameo-status" style="font-family:var(--mono);font-size:11.5px;color:var(--muted)">Ready — edit Cameo script and click Run .cameo Live.</div>
        </div>
        <div class="card">
          <div style="display:flex;justify-content:space-between;align-items:center;gap:8px;flex-wrap:wrap">
            <h3>Assertions, Provenance &amp; Generated Artifacts</h3>
            <span id="cameo-hashes" class="badge" style="font-family:var(--mono);font-size:10.5px">Hashes: —</span>
          </div>
          <table id="cameo-assert-table">
            <thead><tr><th>Assertion</th><th>Status</th><th>Detail</th></tr></thead>
            <tbody><tr><td colspan="3" style="color:var(--muted)">Run a .cameo script to see live simulation assertions.</td></tr></tbody>
          </table>
          <div style="display:flex;gap:6px;align-items:center">
            <select id="cameo-out-view" style="flex:1">
              <option value="spicenet">Generated Flagship Netlist (.spicenet)</option>
              <option value="velosim">VeloSim Ingestion Package (velosim.netlist.v1)</option>
              <option value="circuit_json">tscircuit / FieldworkPCB Circuit JSON</option>
              <option value="stdout">Stdout &amp; Diagnostics</option>
            </select>
            <button id="cameo-copy-out" class="icon-btn">Copy</button>
          </div>
          <textarea id="cameo-output" rows="12" readonly style="font-family:var(--mono);font-size:11.5px"></textarea>
        </div>
      </div>
    </section>
  </main>

  <!-- KEYBOARD SHORTCUTS & IMPORT SPICE MODAL -->
  <div id="modal-backdrop" class="modal-backdrop">
    <div class="modal" id="modal-content"></div>
  </div>

  <script>
    const COLORS = ['#38bdf8', '#f43f5e', '#10b981', '#f59e0b', '#a855f7', '#14b8a6'];

    function fmtEng(v, unit) {
      if (!Number.isFinite(v)) return 'NaN ' + unit;
      const a = Math.abs(v);
      if (a < 1e-15) return '0.00 ' + unit;
      if (a >= 1e9) return (v / 1e9).toFixed(2) + ' G' + unit;
      if (a >= 1e6) return (v / 1e6).toFixed(2) + ' M' + unit;
      if (a >= 1e3) return (v / 1e3).toFixed(2) + ' k' + unit;
      if (a >= 1) return v.toFixed(2) + ' ' + unit;
      if (a >= 1e-3) return (v * 1e3).toFixed(2) + ' m' + unit;
      if (a >= 1e-6) return (v * 1e6).toFixed(2) + ' u' + unit;
      if (a >= 1e-9) return (v * 1e9).toFixed(2) + ' n' + unit;
      if (a >= 1e-12) return (v * 1e12).toFixed(2) + ' p' + unit;
      return (v * 1e15).toFixed(2) + ' f' + unit;
    }

    const PRIMITIVE_SHELF = [
      { kind: 'spice.resistor', prefix: 'R', name: 'Resistor', tag: 'R (Key: R)', params: { ohms: 1000.0 }, svg: '<svg viewBox="-45 -22 90 44"><polyline points="-38,0 -22,0 -18,-9 -10,9 -2,-9 6,9 14,-9 20,0 38,0" fill="none" stroke="#e2e8f0" stroke-width="2.4"/></svg>' },
      { kind: 'spice.capacitor', prefix: 'C', name: 'Capacitor', tag: 'C (Key: C)', params: { farads: 1e-6, ic_volts: 0.0 }, svg: '<svg viewBox="-45 -22 90 44"><line x1="-38" y1="0" x2="-5" y2="0" stroke="#e2e8f0" stroke-width="2.4"/><line x1="-5" y1="-13" x2="-5" y2="13" stroke="#e2e8f0" stroke-width="2.8"/><line x1="5" y1="-13" x2="5" y2="13" stroke="#e2e8f0" stroke-width="2.8"/><line x1="5" y1="0" x2="38" y2="0" stroke="#e2e8f0" stroke-width="2.4"/></svg>' },
      { kind: 'spice.inductor', prefix: 'L', name: 'Inductor', tag: 'L (Key: L)', params: { henries: 1e-3 }, svg: '<svg viewBox="-45 -22 90 44"><path d="M -38 0 L -20 0 A 5 5 0 0 1 -10 0 A 5 5 0 0 1 0 0 A 5 5 0 0 1 10 0 A 5 5 0 0 1 20 0 L 38 0" fill="none" stroke="#e2e8f0" stroke-width="2.4"/></svg>' },
      { kind: 'spice.vsrc', prefix: 'V', name: 'Voltage Src', tag: 'V (Key: V)', params: { dc_volts: 5.0, ac_mag: 1.0 }, svg: '<svg viewBox="-45 -22 90 44"><circle cx="0" cy="0" r="15" fill="none" stroke="#38bdf8" stroke-width="2.2"/><text x="-6" y="4" fill="#f8fafc" font-size="11" font-family="monospace">+</text><text x="4" y="4" fill="#94a3b8" font-size="11" font-family="monospace">-</text></svg>' },
      { kind: 'spice.isrc', prefix: 'I', name: 'Current Src', tag: 'I (Key: I)', params: { dc_amps: 1e-3 }, svg: '<svg viewBox="-45 -22 90 44"><circle cx="0" cy="0" r="15" fill="none" stroke="#a855f7" stroke-width="2.2"/><line x1="-8" y1="0" x2="8" y2="0" stroke="#f8fafc" stroke-width="2"/><polyline points="3,-5 9,0 3,5" fill="none" stroke="#f8fafc" stroke-width="2"/></svg>' },
      { kind: 'spice.diode', prefix: 'D', name: 'Diode', tag: 'D (Key: D)', params: { is_amps: 1e-14, n_emission: 1.0 }, svg: '<svg viewBox="-45 -22 90 44"><line x1="-38" y1="0" x2="-10" y2="0" stroke="#e2e8f0" stroke-width="2.2"/><polygon points="-10,-10 -10,10 10,0" fill="#1e293b" stroke="#f43f5e" stroke-width="2.2"/><line x1="10" y1="-10" x2="10" y2="10" stroke="#f43f5e" stroke-width="2.5"/><line x1="10" y1="0" x2="38" y2="0" stroke="#e2e8f0" stroke-width="2.2"/></svg>' },
      { kind: 'spice.vcvs', prefix: 'E', name: 'VCVS (E)', tag: 'E-source', params: { gain: 10.0 }, svg: '<svg viewBox="-45 -22 90 44"><polygon points="0,-16 20,0 0,16 -20,0" fill="#1e293b" stroke="#38bdf8" stroke-width="2"/><text x="0" y="4" text-anchor="middle" fill="#f8fafc" font-size="11" font-family="monospace">E</text></svg>' },
      { kind: 'spice.vccs', prefix: 'G', name: 'VCCS (G)', tag: 'G-source', params: { siemens: 1e-3 }, svg: '<svg viewBox="-45 -22 90 44"><polygon points="0,-16 20,0 0,16 -20,0" fill="#1e293b" stroke="#38bdf8" stroke-width="2"/><text x="0" y="4" text-anchor="middle" fill="#f8fafc" font-size="11" font-family="monospace">G</text></svg>' }
    ];

    const SCHEM_PRESETS = {
      divider: {
        schemaVersion: '2.1', formatId: 'spice.schematic.v2', name: 'resistive_divider',
        rootSheet: {
          sheetId: 'root', title: 'Resistive Divider', gridPitch: 20, gridStyle: 'lines',
          components: [
            { instanceId: 'V1', deviceKindId: 'spice.vsrc', x: 80, y: 160, rotationDegrees: 90, parameters: { dc_volts: 10.0, ac_mag: 1.0 } },
            { instanceId: 'R1', deviceKindId: 'spice.resistor', x: 220, y: 80, rotationDegrees: 0, parameters: { ohms: 1000.0 } },
            { instanceId: 'R2', deviceKindId: 'spice.resistor', x: 340, y: 160, rotationDegrees: 90, parameters: { ohms: 2000.0 } }
          ],
          nets: [
            { netName: 'in', connectedPins: [{ instanceId: 'V1', pinName: 'p' }, { instanceId: 'R1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'R1', pinName: 'n' }, { instanceId: 'R2', pinName: 'p' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'V1', pinName: 'n' }, { instanceId: 'R2', pinName: 'n' }], isGlobalGround: true }
          ],
          ports: [{ name: 'in', x: 80, y: 60, direction: 'in' }, { name: 'out', x: 420, y: 80, direction: 'out' }]
        }
      },
      rc: {
        schemaVersion: '2.1', formatId: 'spice.schematic.v2', name: 'rc_lowpass_filter',
        rootSheet: {
          sheetId: 'root', title: 'RC Low-Pass Filter', gridPitch: 20, gridStyle: 'lines',
          components: [
            { instanceId: 'V1', deviceKindId: 'spice.vsrc', x: 80, y: 160, rotationDegrees: 90, parameters: { dc_volts: 5.0, ac_mag: 1.0 } },
            { instanceId: 'R1', deviceKindId: 'spice.resistor', x: 220, y: 80, rotationDegrees: 0, parameters: { ohms: 1000.0 } },
            { instanceId: 'C1', deviceKindId: 'spice.capacitor', x: 340, y: 160, rotationDegrees: 90, parameters: { farads: 1e-6, ic_volts: 0.0 } }
          ],
          nets: [
            { netName: 'in', connectedPins: [{ instanceId: 'V1', pinName: 'p' }, { instanceId: 'R1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'R1', pinName: 'n' }, { instanceId: 'C1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'V1', pinName: 'n' }, { instanceId: 'C1', pinName: 'n' }], isGlobalGround: true }
          ]
        }
      },
      diode: {
        schemaVersion: '2.1', formatId: 'spice.schematic.v2', name: 'diode_1n4148_bias',
        rootSheet: {
          sheetId: 'root', title: '1N4148 Diode Bias', gridPitch: 20, gridStyle: 'lines',
          components: [
            { instanceId: 'V1', deviceKindId: 'spice.vsrc', x: 80, y: 160, rotationDegrees: 90, parameters: { dc_volts: 5.0, ac_mag: 0.1 } },
            { instanceId: 'R1', deviceKindId: 'spice.resistor', x: 220, y: 80, rotationDegrees: 0, parameters: { ohms: 1000.0 } },
            { instanceId: 'D1', deviceKindId: 'spice.diode', x: 340, y: 160, rotationDegrees: 90, parameters: { is_amps: 2.52e-9, n_emission: 1.752 }, modelName: 'D1N4148', libraryPartId: 'foundation.diode.1n4148' }
          ],
          nets: [
            { netName: 'in', connectedPins: [{ instanceId: 'V1', pinName: 'p' }, { instanceId: 'R1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'R1', pinName: 'n' }, { instanceId: 'D1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'V1', pinName: 'n' }, { instanceId: 'D1', pinName: 'n' }], isGlobalGround: true }
          ]
        }
      },
      bsim4_inv: {
        schemaVersion: '2.1', formatId: 'spice.schematic.v2', name: 'bsim4_cmos_inverter',
        rootSheet: {
          sheetId: 'root', title: 'BSIM4 65nm CMOS Inverter', gridPitch: 20, gridStyle: 'lines',
          components: [
            { instanceId: 'Vdd', deviceKindId: 'spice.vsrc', x: 80, y: 100, rotationDegrees: 90, parameters: { dc_volts: 1.2 } },
            { instanceId: 'Vin', deviceKindId: 'spice.vsrc', x: 80, y: 220, rotationDegrees: 90, parameters: { dc_volts: 0.6, ac_mag: 0.01 } },
            { instanceId: 'Mp', deviceKindId: 'spice.mosfet', x: 260, y: 100, rotationDegrees: 0, parameters: { w_m: 2.2e-6, l_m: 65e-9, level: 54, vth0_volts: -0.35 }, modelName: 'PCH65', libraryPartId: 'foundation.mos.bsim4_pch65' },
            { instanceId: 'Mn', deviceKindId: 'spice.mosfet', x: 260, y: 220, rotationDegrees: 0, parameters: { w_m: 1e-6, l_m: 65e-9, level: 54, vth0_volts: 0.35 }, modelName: 'NCH65', libraryPartId: 'foundation.mos.bsim4_nch65' },
            { instanceId: 'Cload', deviceKindId: 'spice.capacitor', x: 400, y: 200, rotationDegrees: 90, parameters: { farads: 10e-15, ic_volts: 0.6 } }
          ],
          nets: [
            { netName: 'vdd', connectedPins: [{ instanceId: 'Vdd', pinName: 'p' }, { instanceId: 'Mp', pinName: 's' }, { instanceId: 'Mp', pinName: 'b' }], isGlobalGround: false },
            { netName: 'in', connectedPins: [{ instanceId: 'Vin', pinName: 'p' }, { instanceId: 'Mp', pinName: 'g' }, { instanceId: 'Mn', pinName: 'g' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'Mp', pinName: 'd' }, { instanceId: 'Mn', pinName: 'd' }, { instanceId: 'Cload', pinName: 'p' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'Vdd', pinName: 'n' }, { instanceId: 'Vin', pinName: 'n' }, { instanceId: 'Mn', pinName: 's' }, { instanceId: 'Mn', pinName: 'b' }, { instanceId: 'Cload', pinName: 'n' }], isGlobalGround: true }
          ]
        }
      },
      bjt_amp: {
        schemaVersion: '2.1', formatId: 'spice.schematic.v2', name: 'bjt_2n2222_ce_amp',
        rootSheet: {
          sheetId: 'root', title: '2N2222 NPN Common-Emitter Amp', gridPitch: 20, gridStyle: 'lines',
          components: [
            { instanceId: 'Vcc', deviceKindId: 'spice.vsrc', x: 80, y: 100, rotationDegrees: 90, parameters: { dc_volts: 12.0 } },
            { instanceId: 'Vin', deviceKindId: 'spice.vsrc', x: 80, y: 220, rotationDegrees: 90, parameters: { dc_volts: 0.72, ac_mag: 0.01 } },
            { instanceId: 'Rc', deviceKindId: 'spice.resistor', x: 240, y: 80, rotationDegrees: 0, parameters: { ohms: 2200.0 } },
            { instanceId: 'Rb', deviceKindId: 'spice.resistor', x: 180, y: 200, rotationDegrees: 0, parameters: { ohms: 10000.0 } },
            { instanceId: 'Q1', deviceKindId: 'spice.bjt', x: 340, y: 180, rotationDegrees: 0, parameters: { area: 1.0, bf: 255.9, is_amps: 1.434e-14 }, modelName: 'Q2N2222', libraryPartId: 'foundation.bjt.2n2222' }
          ],
          nets: [
            { netName: 'vcc', connectedPins: [{ instanceId: 'Vcc', pinName: 'p' }, { instanceId: 'Rc', pinName: 'p' }], isGlobalGround: false },
            { netName: 'in', connectedPins: [{ instanceId: 'Vin', pinName: 'p' }, { instanceId: 'Rb', pinName: 'p' }], isGlobalGround: false },
            { netName: 'base', connectedPins: [{ instanceId: 'Rb', pinName: 'n' }, { instanceId: 'Q1', pinName: 'b' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'Rc', pinName: 'n' }, { instanceId: 'Q1', pinName: 'c' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'Vcc', pinName: 'n' }, { instanceId: 'Vin', pinName: 'n' }, { instanceId: 'Q1', pinName: 'e' }], isGlobalGround: true }
          ]
        }
      },
      opamp_buf: {
        schemaVersion: '2.1', formatId: 'spice.schematic.v2', name: 'opamp_noninv_amp',
        rootSheet: {
          sheetId: 'root', title: 'Ideal Op-Amp Non-Inverting (Gain = 3)', gridPitch: 20, gridStyle: 'lines',
          components: [
            { instanceId: 'Vin', deviceKindId: 'spice.vsrc', x: 80, y: 140, rotationDegrees: 90, parameters: { dc_volts: 1.5, ac_mag: 1.0 } },
            { instanceId: 'X1', deviceKindId: 'spice.subckt', x: 240, y: 140, rotationDegrees: 0, parameters: {}, modelName: 'IDEAL_OPAMP', libraryPartId: 'foundation.subckt.ideal_opamp' },
            { instanceId: 'Rf', deviceKindId: 'spice.resistor', x: 340, y: 220, rotationDegrees: 0, parameters: { ohms: 20000.0 } },
            { instanceId: 'Rg', deviceKindId: 'spice.resistor', x: 180, y: 240, rotationDegrees: 90, parameters: { ohms: 10000.0 } }
          ],
          nets: [
            { netName: 'in', connectedPins: [{ instanceId: 'Vin', pinName: 'p' }, { instanceId: 'X1', pinName: 'inp' }], isGlobalGround: false },
            { netName: 'fb', connectedPins: [{ instanceId: 'X1', pinName: 'inn' }, { instanceId: 'Rf', pinName: 'p' }, { instanceId: 'Rg', pinName: 'p' }], isGlobalGround: false },
            { netName: 'out', connectedPins: [{ instanceId: 'X1', pinName: 'out' }, { instanceId: 'Rf', pinName: 'n' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'Vin', pinName: 'n' }, { instanceId: 'Rg', pinName: 'n' }], isGlobalGround: true }
          ]
        }
      },
      multisheet_rc: {
        schemaVersion: '2.1', formatId: 'spice.schematic.v2', name: 'multisheet_2stage_rc',
        rootSheet: {
          sheetId: 'root', title: 'Stage 1: Input RC', gridPitch: 20, gridStyle: 'lines',
          components: [
            { instanceId: 'V1', deviceKindId: 'spice.vsrc', x: 80, y: 160, rotationDegrees: 90, parameters: { dc_volts: 10.0, ac_mag: 1.0 } },
            { instanceId: 'R1', deviceKindId: 'spice.resistor', x: 220, y: 80, rotationDegrees: 0, parameters: { ohms: 1000.0 } },
            { instanceId: 'C1', deviceKindId: 'spice.capacitor', x: 340, y: 160, rotationDegrees: 90, parameters: { farads: 1e-6, ic_volts: 0.0 } }
          ],
          nets: [
            { netName: 'in', connectedPins: [{ instanceId: 'V1', pinName: 'p' }, { instanceId: 'R1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'mid', connectedPins: [{ instanceId: 'R1', pinName: 'n' }, { instanceId: 'C1', pinName: 'p' }], isGlobalGround: false },
            { netName: 'GND', connectedPins: [{ instanceId: 'V1', pinName: 'n' }, { instanceId: 'C1', pinName: 'n' }], isGlobalGround: true }
          ],
          ports: [{ name: 'mid', x: 420, y: 80, direction: 'out' }]
        },
        subSheets: [
          {
            sheetId: 'stage2', title: 'Stage 2: Output RC + Load', gridPitch: 20, gridStyle: 'lines',
            components: [
              { instanceId: 'R1', deviceKindId: 'spice.resistor', x: 180, y: 80, rotationDegrees: 0, parameters: { ohms: 1000.0 } },
              { instanceId: 'C1', deviceKindId: 'spice.capacitor', x: 300, y: 160, rotationDegrees: 90, parameters: { farads: 1e-6, ic_volts: 0.0 } },
              { instanceId: 'Rload', deviceKindId: 'spice.resistor', x: 400, y: 160, rotationDegrees: 90, parameters: { ohms: 2000.0 } }
            ],
            nets: [
              { netName: 'mid', connectedPins: [{ instanceId: 'R1', pinName: 'p' }], isGlobalGround: false },
              { netName: 'out', connectedPins: [{ instanceId: 'R1', pinName: 'n' }, { instanceId: 'C1', pinName: 'p' }, { instanceId: 'Rload', pinName: 'p' }], isGlobalGround: false },
              { netName: 'GND', connectedPins: [{ instanceId: 'C1', pinName: 'n' }, { instanceId: 'Rload', pinName: 'n' }], isGlobalGround: true }
            ],
            ports: [
              { name: 'mid', x: 80, y: 80, direction: 'in' },
              { name: 'out', x: 480, y: 80, direction: 'out' }
            ]
          }
        ]
      }
    };

    const DECK_PRESETS = {
      bsim4: { analysis: 'dc', src: 'Vin', start: '0', stop: '1.2', step: '0.05', text: `BSIM4 CMOS Inverter\n.MODEL nch4 NMOS(LEVEL=54 VTH0=0.35 TOXE=2.2n U0=420 VSAT=1.1e5)\n.MODEL pch4 PMOS(LEVEL=54 VTH0=-0.35 TOXE=2.2n U0=160 VSAT=0.9e5)\nVdd vdd 0 DC 1.2\nVin in 0 DC 0.0\nMn out in 0 0 nch4 W=1u L=65n\nMp out in vdd vdd pch4 W=2.2u L=65n\n.END` },
      spectre: { analysis: 'dc', src: 'Vin', start: '0', stop: '1.2', step: '0.05', text: `// Spectre Dialect BSIM4 CMOS Inverter\nsimulator lang=spectre\nparameters vdd_v=1.2 rload=20k\nmodel nch bsim4 type=n vth0=0.35 toxe=2.2n u0=420 vsat=1.1e5\nmodel pch bsim4 type=p vth0=-0.35 toxe=2.2n u0=160 vsat=0.9e5\nsubckt inv (in out vdd gnd)\n  Mn (out in gnd gnd) nch w=1u l=65n\n  Mp (out in vdd vdd) pch w=2.2u l=65n\nends inv\nVdd (vdd 0) vsource dc=vdd_v\nVin (in 0) vsource dc=0\nX1 (in out vdd 0) inv\nR1 (out 0) resistor r=rload` },
      rf_hb: { analysis: 'hb', src: 'Vlo', start: '1e6', stop: '7', step: '1', text: `Pumped Diode RF Mixer (.HB)\n.MODEL D1 D(IS=1e-14 CJO=5p)\nVlo in 0 DC 0.4 SIN(0.4 0.8 1Meg)\nR1 in out 500\nC1 out 0 200p\nD1 out 0 D1\n.END` },
      rf_pss: { analysis: 'pss', src: 'Vlo', start: '1e6', stop: '64', step: '1', text: `Pumped Diode RF Detector (.PSS)\n.MODEL D1 D(IS=1e-14 CJO=5p)\nVlo in 0 DC 0.4 SIN(0.4 0.8 1Meg)\nR1 in out 500\nC1 out 0 200p\nD1 out 0 D1\n.END` },
      ltra: { analysis: 'ac', src: 'V1', start: '1e6', stop: '1e8', step: '15', text: `Lossy 50-Ohm LTRA Transmission Line\n.MODEL lossy50 LTRA(R=20 L=250n G=0 C=100p LEN=1.0 NSEGS=32)\nV1 in 0 DC 5 AC 1\nO1 in 0 out 0 lossy50\nRload out 0 50\n.END` },
      trnoise: { analysis: 'tran', src: 'Vn', start: '0', stop: '2e-4', step: '1e-6', text: `TRNOISE Deterministic Gaussian Noise RC Filter\nVn in 0 DC 0 TRNOISE(0.5 1u 0 0 42)\nR1 in out 1k\nC1 out 0 10n\n.END` }
    };

    let currentSchem = structuredClone(SCHEM_PRESETS.divider);
    let activeSheetId = 'root';
    let undoStack = [];
    let redoStack = [];
    let libraryEntries = [];
    let lastSchemResult = null;
    let schemProbed = new Set(['v(out)']);
    let editMode = 'move';
    let selectedInstance = null;
    let wireStartPin = null;
    let bodeDb = false;
    let lastDeckResult = null;
    let deckSelected = new Set();
    let zoomState = { scale: 1.0, vb: null, baseVb: null };

    function allSheets() {
      currentSchem.subSheets = currentSchem.subSheets || [];
      return [currentSchem.rootSheet, ...currentSchem.subSheets];
    }
    function activeSheet() {
      const sheets = allSheets();
      const found = sheets.find(s => s.sheetId === activeSheetId);
      if (found) return found;
      activeSheetId = currentSchem.rootSheet.sheetId;
      return currentSchem.rootSheet;
    }
    function resolvedNetSignal(sheet, net) {
      if (net.isGlobalGround) return '';
      if (sheet === currentSchem.rootSheet) return `v(${net.netName.toLowerCase()})`;
      const nameLower = net.netName.toLowerCase();
      const isPort = (sheet.ports || []).some(p => p.name.toLowerCase() === nameLower) ||
        (currentSchem.rootSheet.ports || []).some(p => p.name.toLowerCase() === nameLower) ||
        (currentSchem.rootSheet.nets || []).some(n => !n.isGlobalGround && n.netName.toLowerCase() === nameLower);
      return isPort ? `v(${nameLower})` : `v(${sheet.sheetId.toLowerCase()}_${nameLower})`;
    }
    function setActiveSheet(sheetId) {
      const sheets = allSheets();
      const target = sheets.find(s => s.sheetId === sheetId) || currentSchem.rootSheet;
      activeSheetId = target.sheetId;
      selectedInstance = null;
      wireStartPin = null;
      zoomState = { scale: 1.0, vb: null, baseVb: null };
      refreshAllSchematicViews();
      runSchematicSim();
    }
    function addSubSheet() {
      const existing = allSheets();
      const defaultTitle = `Subsheet ${existing.length}`;
      const title = prompt('New subsheet title (share net names via Off-Sheet Ports):', defaultTitle);
      if (!title || !title.trim()) return;
      pushUndo();
      let idx = existing.length + 1;
      let sid = 'sheet' + idx;
      while (existing.some(s => s.sheetId.toLowerCase() === sid.toLowerCase())) {
        ++idx; sid = 'sheet' + idx;
      }
      currentSchem.subSheets = currentSchem.subSheets || [];
      currentSchem.subSheets.push({
        sheetId: sid,
        title: title.trim(),
        gridPitch: gridPitch(),
        gridStyle: gridStyle(),
        components: [],
        nets: [{ netName: 'GND', connectedPins: [], isGlobalGround: true }],
        ports: []
      });
      setActiveSheet(sid);
    }
    function renameActiveSheet() {
      const sheet = activeSheet();
      const next = prompt('Rename sheet:', sheet.title || sheet.sheetId);
      if (!next || !next.trim()) return;
      pushUndo();
      sheet.title = next.trim();
      refreshAllSchematicViews();
      runSchematicSim();
    }
    function deleteActiveSheet() {
      const sheet = activeSheet();
      if (sheet === currentSchem.rootSheet) return;
      pushUndo();
      currentSchem.subSheets = (currentSchem.subSheets || []).filter(s => s.sheetId !== sheet.sheetId);
      setActiveSheet(currentSchem.rootSheet.sheetId);
    }
    function renderSheetBar() {
      const tabsEl = document.getElementById('sheet-tabs');
      if (!tabsEl) return;
      const cur = activeSheet();
      tabsEl.innerHTML = '';
      allSheets().forEach((s, idx) => {
        const btn = document.createElement('button');
        btn.className = 'sheet-tab' + (s.sheetId === cur.sheetId ? ' active' : '');
        const label = idx === 0 ? `Root: ${s.title || s.sheetId}` : `${s.title || s.sheetId}`;
        btn.innerHTML = `${escapeHtmlAttr(label)} <span class="sheet-badge">${(s.components || []).length}</span>`;
        btn.title = `Sheet ID: ${s.sheetId} (${(s.components || []).length} components, ${(s.ports || []).length} off-sheet ports)`;
        btn.onclick = () => setActiveSheet(s.sheetId);
        tabsEl.appendChild(btn);
      });
      const isRoot = cur === currentSchem.rootSheet;
      const parentBtn = document.getElementById('btn-sheet-parent');
      const delBtn = document.getElementById('btn-sheet-del');
      if (parentBtn) parentBtn.disabled = isRoot;
      if (delBtn) delBtn.disabled = isRoot;
    }
    document.getElementById('btn-sheet-parent').addEventListener('click', () => setActiveSheet(currentSchem.rootSheet.sheetId));
    document.getElementById('btn-sheet-add').addEventListener('click', addSubSheet);
    document.getElementById('btn-sheet-rename').addEventListener('click', renameActiveSheet);
    document.getElementById('btn-sheet-del').addEventListener('click', deleteActiveSheet);

    function gridPitch() { return Number(document.getElementById('grid-pitch').value) || 20; }
    function gridStyle() { return document.getElementById('grid-style').value || 'lines'; }

    function pushUndo() {
      undoStack.push(JSON.stringify(currentSchem));
      if (undoStack.length > 40) undoStack.shift();
      redoStack = [];
    }
    function doUndo() {
      if (undoStack.length === 0) return;
      redoStack.push(JSON.stringify(currentSchem));
      currentSchem = JSON.parse(undoStack.pop());
      selectedInstance = null;
      activeSheet();
      refreshAllSchematicViews();
      runSchematicSim();
    }
    function doRedo() {
      if (redoStack.length === 0) return;
      undoStack.push(JSON.stringify(currentSchem));
      currentSchem = JSON.parse(redoStack.pop());
      selectedInstance = null;
      activeSheet();
      refreshAllSchematicViews();
      runSchematicSim();
    }

    // ---- TOP WORKSPACE & DOCK TABS ----
    function activateWorkspace(tabName) {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.toggle('active', b.dataset.tab === tabName));
      document.querySelectorAll('.tab-pane').forEach(p => p.classList.toggle('active', p.id === 'tab-' + tabName));
    }
    document.querySelectorAll('.tab-btn').forEach(btn => btn.addEventListener('click', () => activateWorkspace(btn.dataset.tab)));

    function activateDockTab(dock, pane) {
      document.querySelectorAll(`.dock-tab[data-dock="${dock}"]`).forEach(b => b.classList.toggle('active', b.dataset.pane === pane));
      document.querySelectorAll(`#${dock}-shelf, #${dock}-navigator, #${dock}-results, #${dock}-inspector, #${dock}-export`).forEach(sec => {
        if (sec.id.startsWith(dock + '-')) sec.classList.toggle('active', sec.id === dock + '-' + pane);
      });
    }
    document.querySelectorAll('.dock-tab').forEach(btn => btn.addEventListener('click', () => activateDockTab(btn.dataset.dock, btn.dataset.pane)));

    // ---- MODAL HELPERS ----
    function openModal(html) {
      document.getElementById('modal-content').innerHTML = html;
      document.getElementById('modal-backdrop').classList.add('open');
    }
    function closeModal() { document.getElementById('modal-backdrop').classList.remove('open'); }
    document.getElementById('modal-backdrop').addEventListener('click', e => { if (e.target.id === 'modal-backdrop') closeModal(); });

    document.getElementById('btn-shortcuts').addEventListener('click', () => openModal(`
      <div style="display:flex;justify-content:space-between;align-items:center">
        <h3 style="margin:0">Spice V2 Schematic Shortcuts (RSpice Parity)</h3>
        <button class="icon-btn" onclick="closeModal()">Close</button>
      </div>
      <table>
        <thead><tr><th>Key</th><th>Action</th><th>Key</th><th>Action</th></tr></thead>
        <tbody>
          <tr><td><span class="kbd">R / C / L</span></td><td>Place Resistor / Cap / Inductor</td><td><span class="kbd">W</span></td><td>Orthogonal Wire mode</td></tr>
          <tr><td><span class="kbd">V / I / D</span></td><td>Place V-Src / I-Src / Diode</td><td><span class="kbd">Esc</span></td><td>Return to Select/Probe</td></tr>
          <tr><td><span class="kbd">Ctrl+R</span></td><td>Rotate selected 90°</td><td><span class="kbd">Ctrl+M</span></td><td>Mirror selected horizontally</td></tr>
          <tr><td><span class="kbd">Ctrl+Z / Y</span></td><td>Undo / Redo</td><td><span class="kbd">Del</span></td><td>Delete selected component</td></tr>
          <tr><td><span class="kbd">+ / − / F</span></td><td>Zoom In / Out / Fit Sheet</td><td><span class="kbd">Ctrl+S</span></td><td>Save versioned .spice.json</td></tr>
          <tr><td><span class="kbd">PgUp / PgDn</span></td><td>Cycle Schematic Sheets</td><td><span class="kbd">Shift+H</span></td><td>Return to Root Sheet</td></tr>
          <tr><td><span class="kbd">F5</span></td><td>Run Native C++20 Simulation</td><td><span class="kbd">Alt+1..4</span></td><td>Switch Workbench Tabs</td></tr>
        </tbody>
      </table>
    `));

    // ---- DOWNLOAD HELPER ----
    function downloadText(filename, text, mime = 'text/plain;charset=utf-8') {
      const blob = new Blob([text], { type: mime });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = filename; a.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    // ---- LIBRARY LOADING & SHELF RENDERING ----
    async function loadLibrary() {
      const data = await (await fetch('/api/library')).json();
      libraryEntries = data.components || [];
      const pathBadge = document.getElementById('lib-path-badge');
      if (data.storage_path) pathBadge.textContent = data.storage_path.split(/[\\/]/).pop();
      renderShelves();
      renderLibraryStudio();
    }

    function renderShelves() {
      const q = (document.getElementById('shelf-filter').value || '').trim().toLowerCase();
      const primHost = document.getElementById('shelf-primitives');
      primHost.innerHTML = '';
      PRIMITIVE_SHELF.filter(p => !q || p.name.toLowerCase().includes(q) || p.kind.includes(q)).forEach(p => {
        const el = document.createElement('div');
        el.className = 'shelf-item';
        el.innerHTML = `${p.svg}<div class="sname">${p.name}</div><div class="stag">${p.tag}</div>`;
        el.onclick = () => placePrimitive(p.kind, p.prefix, structuredClone(p.params));
        primHost.appendChild(el);
      });

      const libHost = document.getElementById('shelf-library');
      libHost.innerHTML = '';
      libraryEntries.filter(item => {
        const p = item.part;
        return !q || p.name.toLowerCase().includes(q) || p.category.toLowerCase().includes(q) ||
          (p.attachedModel?.modelName || '').toLowerCase().includes(q);
      }).forEach(item => {
        const p = item.part;
        const el = document.createElement('div');
        el.className = 'shelf-item' + (p.isBuiltIn ? '' : ' custom-part');
        el.title = `${p.description}\nModel: ${p.attachedModel?.modelName || 'none'}`;
        el.innerHTML = `${item.preview_svg}<div class="sname">${p.name}</div><div class="stag">${p.attachedModel?.modelName || p.spicePrefix}</div>`;
        el.onclick = () => placeLibraryPart(p);
        libHost.appendChild(el);
      });
    }
    document.getElementById('shelf-filter').addEventListener('input', renderShelves);
    document.getElementById('btn-jump-lib').addEventListener('click', () => activateWorkspace('library'));

    function nextInstanceId(prefix) {
      let n = 1;
      const comps = activeSheet().components || [];
      while (comps.some(c => c.instanceId.toLowerCase() === (prefix + n).toLowerCase())) ++n;
      return prefix + n;
    }

    function autoConnectNewPart(id, pins) {
      const sheet = activeSheet();
      const outNet = sheet.nets.find(nt => nt.netName.toLowerCase() === 'out') || sheet.nets.find(nt => !nt.isGlobalGround);
      const gndNet = sheet.nets.find(nt => nt.isGlobalGround);
      if (pins.length === 2 && outNet && gndNet) {
        outNet.connectedPins.push({ instanceId: id, pinName: pins[0] });
        gndNet.connectedPins.push({ instanceId: id, pinName: pins[1] });
      }
    }

    function placePrimitive(kindId, prefix, defaultParams) {
      pushUndo();
      const sheet = activeSheet();
      const G = gridPitch();
      const id = nextInstanceId(prefix);
      const count = sheet.components.length;
      const x = Math.round((160 + (count * 60) % 240) / G) * G;
      const y = Math.round((240) / G) * G;
      sheet.components.push({
        instanceId: id, deviceKindId: kindId, x, y, rotationDegrees: 0, parameters: defaultParams,
        mirrorX: false, mirrorY: false
      });
      if (['spice.resistor','spice.capacitor','spice.inductor','spice.diode'].includes(kindId)) {
        autoConnectNewPart(id, ['p', 'n']);
      }
      selectedInstance = id;
      refreshAllSchematicViews();
      runSchematicSim();
    }

    function placeLibraryPart(part) {
      pushUndo();
      const sheet = activeSheet();
      const G = gridPitch();
      const id = nextInstanceId(part.spicePrefix || 'U');
      const count = sheet.components.length;
      const x = Math.round((160 + (count * 60) % 240) / G) * G;
      const y = Math.round((240) / G) * G;
      sheet.components.push({
        instanceId: id,
        deviceKindId: part.baseDeviceKindId,
        x, y, rotationDegrees: 0,
        parameters: structuredClone(part.defaultParameters || {}),
        mirrorX: false, mirrorY: false,
        modelName: part.attachedModel?.modelName || null,
        libraryPartId: part.id,
        displayValue: part.attachedModel?.modelName || part.name
      });
      if (part.pins && part.pins.length === 2) {
        autoConnectNewPart(id, [part.pins[0].name, part.pins[1].name]);
      }
      selectedInstance = id;
      activateWorkspace('schematic');
      refreshAllSchematicViews();
      runSchematicSim();
    }

    // ---- MODE, EDIT & ARRANGEMENT COMMANDS ----
    function setMode(m) {
      editMode = m;
      wireStartPin = null;
      document.getElementById('mode-move').classList.toggle('mode-on', m === 'move');
      document.getElementById('mode-wire').classList.toggle('mode-on', m === 'wire');
      document.getElementById('erc-status').textContent = m === 'wire'
        ? 'WIRE MODE: Click any source pin, then click destination pin to snap an orthogonal wire.'
        : 'SELECT / PROBE MODE: Drag parts with cyan alignment snap guides, or click wires to probe.';
    }
    document.getElementById('mode-move').addEventListener('click', () => setMode('move'));
    document.getElementById('mode-wire').addEventListener('click', () => setMode('wire'));
    document.getElementById('btn-undo').addEventListener('click', doUndo);
    document.getElementById('btn-redo').addEventListener('click', doRedo);

    function rotateSelected() {
      const sheet = activeSheet();
      const target = sheet.components.find(c => c.instanceId === selectedInstance) || sheet.components[0];
      if (!target) return;
      pushUndo();
      target.rotationDegrees = (target.rotationDegrees + 90) % 360;
      refreshAllSchematicViews();
      runSchematicSim();
    }
    function mirrorSelected() {
      const sheet = activeSheet();
      const target = sheet.components.find(c => c.instanceId === selectedInstance) || sheet.components[0];
      if (!target) return;
      pushUndo();
      target.mirrorX = !target.mirrorX;
      refreshAllSchematicViews();
      runSchematicSim();
    }
    function deleteSelected() {
      if (!selectedInstance) return;
      pushUndo();
      const sheet = activeSheet();
      sheet.components = sheet.components.filter(c => c.instanceId !== selectedInstance);
      sheet.nets.forEach(n => {
        n.connectedPins = n.connectedPins.filter(p => p.instanceId !== selectedInstance);
      });
      sheet.nets = sheet.nets.filter(n => n.connectedPins.length > 0 || n.isGlobalGround);
      selectedInstance = null;
      refreshAllSchematicViews();
      runSchematicSim();
    }
    document.getElementById('btn-rotate').addEventListener('click', rotateSelected);
    document.getElementById('btn-mirror').addEventListener('click', mirrorSelected);
    document.getElementById('btn-delete').addEventListener('click', deleteSelected);
    document.getElementById('btn-align-x').addEventListener('click', () => {
      const comps = activeSheet().components;
      if (comps.length < 2) return;
      pushUndo();
      const G = gridPitch();
      const avgY = Math.round((comps.reduce((s, c) => s + c.y, 0) / comps.length) / G) * G;
      comps.forEach(c => { c.y = avgY; });
      refreshAllSchematicViews();
      runSchematicSim();
    });
    document.getElementById('btn-snap-all').addEventListener('click', () => {
      pushUndo();
      const G = gridPitch();
      activeSheet().components.forEach(c => {
        c.x = Math.round(c.x / G) * G;
        c.y = Math.round(c.y / G) * G;
      });
      refreshAllSchematicViews();
      runSchematicSim();
    });

    document.getElementById('btn-add-directive').addEventListener('click', () => {
      const text = prompt('Enter SPICE directive or sheet note (e.g. .PARAM Rload=2k or .TEMP 27):', '.TEMP 27');
      if (!text) return;
      pushUndo();
      const sheet = activeSheet();
      sheet.annotations = sheet.annotations || [];
      const idx = sheet.annotations.length;
      sheet.annotations.push({
        id: 'ann_' + (idx + 1), x: 80, y: 260 + idx * 24, text: text.trim(),
        isSpiceDirective: text.trim().startsWith('.')
      });
      runSchematicSim();
    });

    document.getElementById('btn-add-port').addEventListener('click', () => {
      const name = prompt('Enter off-sheet / hierarchical port name (shared net name across sheets, e.g. mid or out):', 'mid');
      if (!name) return;
      pushUndo();
      const sheet = activeSheet();
      sheet.ports = sheet.ports || [];
      const idx = sheet.ports.length;
      sheet.ports.push({ name: name.trim(), x: 420, y: 80 + idx * 32, direction: 'inout' });
      refreshAllSchematicViews();
      runSchematicSim();
    });

    // ---- SAVE / OPEN / IMPORT SPICE / SAVE SHEET AS SUBCKT ----
    document.getElementById('btn-save-schem').addEventListener('click', async () => {
      const resp = await fetch('/api/schematic/save', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ document: currentSchem, author: 'Spice V2 Studio' })
      });
      const data = await resp.json();
      if (data.content) {
        downloadText(data.filename, data.content, 'application/json;charset=utf-8');
        document.getElementById('erc-status').textContent = `Saved ${data.filename} (schema v${data.schema_version}, sha256=${(data.sha256||'').slice(0,12)}...)`;
      }
    });

    document.getElementById('btn-open-schem').addEventListener('click', () => document.getElementById('file-open-schem').click());
    document.getElementById('file-open-schem').addEventListener('change', async (e) => {
      const file = e.target.files?.[0];
      if (!file) return;
      const content = await file.text();
      const resp = await fetch('/api/schematic/load', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content, verifyChecksum: true })
      });
      const data = await resp.json();
      if (!data.loaded) {
        alert('Failed to load schematic (fail-closed): ' + data.error);
        return;
      }
      pushUndo();
      currentSchem = data.document;
      activeSheetId = currentSchem.rootSheet?.sheetId || 'root';
      selectedInstance = null;
      refreshAllSchematicViews();
      runSchematicSim();
      e.target.value = '';
    });

    document.getElementById('btn-import-spice').addEventListener('click', () => {
      openModal(`
        <div style="display:flex;justify-content:space-between;align-items:center">
          <h3 style="margin:0">Import SPICE Netlist to Schematic</h3>
          <button class="icon-btn" onclick="closeModal()">Close</button>
        </div>
        <textarea id="import-spice-box" rows="10">* Paste any SPICE deck (R/C/L/V/I/D/M/Q/J + .MODEL)
V1 in 0 DC 12 AC 1
R1 in mid 1k
R2 mid out 2k
C1 out 0 100n
.END</textarea>
        <div style="display:flex;justify-content:flex-end;gap:8px">
          <button class="icon-btn" onclick="closeModal()">Cancel</button>
          <button class="icon-btn primary" id="btn-do-import">Import &amp; Layout</button>
        </div>
      `);
      document.getElementById('btn-do-import').onclick = async () => {
        const spiceText = document.getElementById('import-spice-box').value;
        const resp = await fetch('/api/schematic/import-spice', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ spiceText })
        });
        const data = await resp.json();
        if (data.imported) {
          pushUndo();
          currentSchem = data.document;
          activeSheetId = currentSchem.rootSheet?.sheetId || 'root';
          selectedInstance = null;
          closeModal();
          refreshAllSchematicViews();
          runSchematicSim();
        }
      };
    });

    document.getElementById('btn-import-spicenet').addEventListener('click', () => {
      openModal(`
        <div style="display:flex;justify-content:space-between;align-items:center">
          <h3 style="margin:0">Import Flagship Netlist (.spicenet / .spice.svg / VeloSim JSON)</h3>
          <button class="icon-btn" onclick="closeModal()">Close</button>
        </div>
        <div style="display:flex;gap:8px;align-items:center">
          <button class="icon-btn" id="btn-spicenet-file">Load File (.spicenet / .spice.svg / .velosim.json)</button>
          <input id="file-spicenet-in" type="file" accept=".spicenet,.svg,.json,.txt" style="display:none" />
          <span style="font-size:11px;color:var(--muted)">Omit layout{} to auto-place left-to-right!</span>
        </div>
        <textarea id="import-spicenet-box" rows="13">// Hierarchical .spicenet example (zero coordinates required!)
netlist "two_stage_rc" format="spice.netlist.v3" {
  module RcStage(r=1k, c=1uF) {
    ports { in:in, out:out, gnd:power }
    part R1 "spice.resistor" ohms=$r footprint="0603"
    part C1 "spice.capacitor" farads=$c footprint="0603"
    chain in -> R1 -> out -> C1 -> gnd
  }
  circuit {
    part V1 "spice.vsrc" dc_volts=5V ac_mag=1V
    use s1 = RcStage(r=1k, c=1uF)
    use s2 = RcStage(r=2.2k, c=470nF)
    net in  { V1.p; s1.in }
    net mid { s1.out; s2.in }
    net out { s2.out }
    net GND ground=true { V1.n; s1.gnd; s2.gnd }
  }
}</textarea>
        <div id="spicenet-import-err" class="err" style="font-family:var(--mono);font-size:11px"></div>
        <div style="display:flex;justify-content:flex-end;gap:8px">
          <button class="icon-btn" onclick="closeModal()">Cancel</button>
          <button class="icon-btn sim-btn" id="btn-do-spicenet">Elaborate, Auto-Layout &amp; Simulate</button>
        </div>
      `);
      document.getElementById('btn-spicenet-file').onclick = () => document.getElementById('file-spicenet-in').click();
      document.getElementById('file-spicenet-in').onchange = async (e) => {
        const f = e.target.files?.[0];
        if (f) document.getElementById('import-spicenet-box').value = await f.text();
      };
      document.getElementById('btn-do-spicenet').onclick = async () => {
        const content = document.getElementById('import-spicenet-box').value;
        const resp = await fetch('/api/schematic/import-netlist', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ content })
        });
        const data = await resp.json();
        if (!resp.ok || !data.imported) {
          document.getElementById('spicenet-import-err').textContent =
            (data.diagnostics || []).map(d => `${d.code}: ${d.message}`).join('; ') || data.error || 'Import failed';
          return;
        }
        pushUndo();
        currentSchem = data.document;
        activeSheetId = currentSchem.rootSheet?.sheetId || 'root';
        selectedInstance = null;
        closeModal();
        refreshAllSchematicViews();
        runSchematicSim();
      };
    });

    document.getElementById('btn-sheet-to-part').addEventListener('click', async () => {
      const name = prompt('Create reusable .SUBCKT component in library from this schematic.\nComponent Name:', currentSchem.name + '_block');
      if (!name) return;
      const resp = await fetch('/api/library/from-schematic', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ document: currentSchem, name, category: 'Custom Subcircuits' })
      });
      const data = await resp.json();
      if (!data.saved) {
        alert('Failed: ' + (data.error || 'unknown error'));
        return;
      }
      await loadLibrary();
      document.getElementById('erc-status').textContent = `Saved '${data.part.name}' (${data.part.attachedModel.modelName}) to persistent library!`;
    });

    // ---- INSPECTOR & NAVIGATOR ----
    function renderInspector() {
      const host = document.getElementById('inspector-host');
      const sheet = activeSheet();
      const comp = sheet.components.find(c => c.instanceId === selectedInstance);
      if (!comp) {
        host.innerHTML = `<div style="font-size:12px;color:var(--dim)">Active sheet: <b>${escapeHtmlAttr(sheet.title || sheet.sheetId)}</b>. Click any component on the schematic to inspect/edit its refdes, attached model, position, rotation, and parameters.</div>`;
      } else {
        const compatModels = libraryEntries.filter(e => e.part.baseDeviceKindId === comp.deviceKindId && e.part.attachedModel);
        const modelOpts = ['<option value="">(Default / Inline Parameters)</option>']
          .concat(compatModels.map(e => {
            const m = e.part.attachedModel.modelName;
            const sel = (comp.modelName === m || comp.libraryPartId === e.part.id) ? 'selected' : '';
            return `<option value="${e.part.id}" ${sel}>${e.part.name} [${m}]</option>`;
          })).join('');
        const paramRows = Object.entries(comp.parameters || {}).map(([k, v]) => `
          <div class="field"><label>${k}</label><input data-insp-param="${k}" value="${v}" /></div>
        `).join('');
        host.innerHTML = `
          <div class="form-grid">
            <div class="field"><label>RefDes (ID)</label><input id="insp-id" value="${comp.instanceId}" /></div>
            <div class="field"><label>Device Kind</label><input value="${comp.deviceKindId}" disabled /></div>
            <div class="field"><label>X (snapped)</label><input id="insp-x" type="number" step="20" value="${comp.x}" /></div>
            <div class="field"><label>Y (snapped)</label><input id="insp-y" type="number" step="20" value="${comp.y}" /></div>
          </div>
          ${compatModels.length > 0 ? `<div class="field"><label>Attached Library Model</label><select id="insp-model">${modelOpts}</select></div>` : ''}
          <div class="form-grid">${paramRows}</div>
          <div style="display:flex;gap:6px">
            <button id="insp-rot" class="icon-btn">Rotate 90°</button>
            <button id="insp-mir" class="icon-btn">Mirror X</button>
            <button id="insp-del" class="icon-btn danger">Delete</button>
          </div>
        `;
        document.getElementById('insp-id').onchange = (e) => {
          const newId = e.target.value.trim();
          if (!newId || newId === comp.instanceId) return;
          pushUndo();
          const oldId = comp.instanceId;
          comp.instanceId = newId;
          sheet.nets.forEach(n => n.connectedPins.forEach(p => { if (p.instanceId === oldId) p.instanceId = newId; }));
          selectedInstance = newId;
          refreshAllSchematicViews();
          runSchematicSim();
        };
        document.getElementById('insp-x').onchange = (e) => { pushUndo(); comp.x = Number(e.target.value) || comp.x; runSchematicSim(); };
        document.getElementById('insp-y').onchange = (e) => { pushUndo(); comp.y = Number(e.target.value) || comp.y; runSchematicSim(); };
        const msel = document.getElementById('insp-model');
        if (msel) {
          msel.onchange = () => {
            pushUndo();
            const found = libraryEntries.find(x => x.part.id === msel.value)?.part;
            if (found) {
              comp.libraryPartId = found.id;
              comp.modelName = found.attachedModel?.modelName || null;
              comp.displayValue = found.attachedModel?.modelName || found.name;
              comp.parameters = structuredClone(found.defaultParameters || comp.parameters);
            } else {
              comp.libraryPartId = null;
              comp.modelName = null;
              comp.displayValue = null;
            }
            refreshAllSchematicViews();
            runSchematicSim();
          };
        }
        host.querySelectorAll('input[data-insp-param]').forEach(inp => {
          inp.onchange = () => {
            const v = Number(inp.value);
            if (Number.isFinite(v)) {
              pushUndo();
              comp.parameters[inp.dataset.inspParam] = v;
              runSchematicSim();
            }
          };
        });
        document.getElementById('insp-rot').onclick = rotateSelected;
        document.getElementById('insp-mir').onclick = mirrorSelected;
        document.getElementById('insp-del').onclick = deleteSelected;
      }

      // Compact live table of all components on active sheet
      const tblHost = document.getElementById('comp-editor');
      tblHost.innerHTML = '';
      const tbl = document.createElement('table');
      tbl.innerHTML = '<thead><tr><th>Part</th><th>Model / Value</th><th>Pos</th></tr></thead>';
      const tb = document.createElement('tbody');
      sheet.components.forEach(c => {
        const tr = document.createElement('tr');
        tr.style.cursor = 'pointer';
        const summary = c.modelName || Object.entries(c.parameters || {}).map(([k,v]) => `${k}=${v}`).slice(0,1).join('');
        tr.innerHTML = `<td><b>${c.instanceId}</b></td><td>${summary}</td><td>(${c.x},${c.y}) ${c.rotationDegrees}°</td>`;
        tr.onclick = () => { selectedInstance = c.instanceId; renderInspector(); };
        tb.appendChild(tr);
      });
      tbl.appendChild(tb);
      tblHost.appendChild(tbl);
    }

    function renderNavigator(diagnostics = []) {
      const sheet = activeSheet();
      const sheetsEl = document.getElementById('nav-sheets');
      sheetsEl.innerHTML = '';
      allSheets().forEach((s, idx) => {
        const row = document.createElement('div');
        row.className = 'nav-row' + (s.sheetId === sheet.sheetId ? ' active' : '');
        row.innerHTML = `<span>${idx === 0 ? 'Root: ' : ''}${escapeHtmlAttr(s.title || s.sheetId)}</span><span style="color:var(--dim)">${s.sheetId} (${(s.components || []).length})</span>`;
        row.onclick = () => setActiveSheet(s.sheetId);
        sheetsEl.appendChild(row);
      });

      const instEl = document.getElementById('nav-instances');
      instEl.innerHTML = '';
      document.getElementById('nav-inst-count').textContent = sheet.components.length;
      sheet.components.forEach(c => {
        const row = document.createElement('div');
        row.className = 'nav-row' + (c.instanceId === selectedInstance ? ' active' : '');
        row.innerHTML = `<span><b>${c.instanceId}</b> ${c.modelName || c.deviceKindId.replace('spice.','')}</span><span style="color:var(--dim)">(${c.x},${c.y})</span>`;
        row.onclick = () => { selectedInstance = c.instanceId; activateDockTab('right', 'inspector'); renderInspector(); renderNavigator(diagnostics); };
        instEl.appendChild(row);
      });

      const netEl = document.getElementById('nav-nets');
      netEl.innerHTML = '';
      document.getElementById('nav-net-count').textContent = sheet.nets.length;
      sheet.nets.forEach(n => {
        const sig = resolvedNetSignal(sheet, n);
        const row = document.createElement('div');
        row.className = 'nav-row' + (sig && schemProbed.has(sig) ? ' active' : '');
        row.innerHTML = `<span>${n.netName}${n.isGlobalGround ? ' (0)' : ''}</span><span style="color:var(--dim)">${n.connectedPins.length} pins</span>`;
        if (sig) {
          row.onclick = () => { if (schemProbed.has(sig)) schemProbed.delete(sig); else schemProbed.add(sig); renderSchemPlot(); renderNavigator(diagnostics); };
        }
        netEl.appendChild(row);
      });

      const ercEl = document.getElementById('nav-erc');
      ercEl.innerHTML = '';
      document.getElementById('nav-erc-count').textContent = diagnostics.length;
      if (diagnostics.length === 0) {
        ercEl.innerHTML = '<div class="nav-row"><span class="ok">0 ERC violations</span></div>';
      } else {
        diagnostics.forEach(d => {
          const row = document.createElement('div');
          row.className = 'nav-row';
          row.innerHTML = `<span class="${d.severity === 'error' ? 'err' : 'amber'}">[${d.code}] ${d.message}</span>`;
          ercEl.appendChild(row);
        });
      }
    }

    function refreshAllSchematicViews() {
      renderSheetBar();
      renderInspector();
      renderNavigator(lastSchemResult?.diagnostics || []);
    }

    // ---- PAN / ZOOM & SVG INTERACTIONS ----
    function applyViewBox(svg) {
      if (!svg || !zoomState.vb) return;
      const v = zoomState.vb;
      svg.setAttribute('viewBox', `${v.x.toFixed(1)} ${v.y.toFixed(1)} ${v.w.toFixed(1)} ${v.h.toFixed(1)}`);
      document.getElementById('zoom-reset').textContent = Math.round(zoomState.scale * 100) + '%';
    }
    function zoomBy(factor) {
      const svg = document.querySelector('#svg-host svg');
      if (!svg || !zoomState.vb) return;
      const cx = zoomState.vb.x + zoomState.vb.w * 0.5;
      const cy = zoomState.vb.y + zoomState.vb.h * 0.5;
      zoomState.scale = Math.min(4.0, Math.max(0.35, zoomState.scale * factor));
      zoomState.vb.w = zoomState.baseVb.w / zoomState.scale;
      zoomState.vb.h = zoomState.baseVb.h / zoomState.scale;
      zoomState.vb.x = cx - zoomState.vb.w * 0.5;
      zoomState.vb.y = cy - zoomState.vb.h * 0.5;
      applyViewBox(svg);
    }
    function fitCanvas() {
      const svg = document.querySelector('#svg-host svg');
      if (!svg || !zoomState.baseVb) return;
      zoomState.scale = 1.0;
      zoomState.vb = { ...zoomState.baseVb };
      applyViewBox(svg);
    }
    document.getElementById('zoom-in').addEventListener('click', () => zoomBy(1.2));
    document.getElementById('zoom-out').addEventListener('click', () => zoomBy(1 / 1.2));
    document.getElementById('zoom-reset').addEventListener('click', fitCanvas);
    document.getElementById('zoom-fit').addEventListener('click', fitCanvas);

    function svgPoint(svg, clientX, clientY) {
      const pt = svg.createSVGPoint();
      pt.x = clientX; pt.y = clientY;
      return pt.matrixTransform(svg.getScreenCTM().inverse());
    }

    function connectPins(pinA, pinB) {
      if (pinA.instanceId === pinB.instanceId && pinA.pinName === pinB.pinName) return;
      pushUndo();
      const nets = activeSheet().nets;
      const findNet = (p) => nets.find(n => n.connectedPins.some(cp => cp.instanceId === p.instanceId && cp.pinName === p.pinName));
      const netA = findNet(pinA), netB = findNet(pinB);
      if (netA && netB && netA === netB) return;
      if (netA && netB) {
        const keep = netA.isGlobalGround ? netA : (netB.isGlobalGround ? netB : netA);
        const drop = (keep === netA) ? netB : netA;
        for (const cp of drop.connectedPins) {
          if (!keep.connectedPins.some(x => x.instanceId === cp.instanceId && x.pinName === cp.pinName)) {
            keep.connectedPins.push(cp);
          }
        }
        nets.splice(nets.indexOf(drop), 1);
      } else if (netA) {
        netA.connectedPins.push(pinB);
      } else if (netB) {
        netB.connectedPins.push(pinA);
      } else {
        nets.push({ netName: 'n' + (nets.length + 1), connectedPins: [pinA, pinB], isGlobalGround: false });
      }
      refreshAllSchematicViews();
      runSchematicSim();
    }

    function bindSvgInteractions() {
      const host = document.getElementById('svg-host');
      const svg = host.querySelector('svg');
      if (!svg) return;
      const vbParts = (svg.getAttribute('viewBox') || '0 0 520 320').split(/\s+/).map(Number);
      zoomState.baseVb = { x: vbParts[0], y: vbParts[1], w: vbParts[2], h: vbParts[3] };
      if (!zoomState.vb || Math.abs(zoomState.scale - 1.0) < 0.01) {
        zoomState.vb = { ...zoomState.baseVb };
      } else {
        applyViewBox(svg);
      }

      const snapV = svg.getElementById('snap-v');
      const snapH = svg.getElementById('snap-h');
      const wirePrev = svg.getElementById('wire-preview');
      const G = gridPitch();

      // Mouse wheel zoom + background drag pan
      svg.addEventListener('wheel', (e) => {
        e.preventDefault();
        zoomBy(e.deltaY < 0 ? 1.12 : 1 / 1.12);
      }, { passive: false });

      let panning = false, panStart = null, panVb = null;
      svg.addEventListener('pointerdown', (e) => {
        if (e.target.closest('.spice-component') || e.target.closest('.spice-pin') || e.target.closest('.spice-net')) return;
        panning = true;
        panStart = { x: e.clientX, y: e.clientY };
        panVb = { ...zoomState.vb };
        svg.style.cursor = 'grabbing';
      });
      svg.addEventListener('pointermove', (e) => {
        const pt = svgPoint(svg, e.clientX, e.clientY);
        const sx = Math.round(pt.x / G) * G, sy = Math.round(pt.y / G) * G;
        document.getElementById('cursor-coords').textContent = `X: ${sx}  Y: ${sy}  |  Grid: ${G}px (${gridStyle()})`;
        if (panning && panStart && panVb) {
          const rect = svg.getBoundingClientRect();
          const dx = (e.clientX - panStart.x) * (panVb.w / Math.max(1, rect.width));
          const dy = (e.clientY - panStart.y) * (panVb.h / Math.max(1, rect.height));
          zoomState.vb = { x: panVb.x - dx, y: panVb.y - dy, w: panVb.w, h: panVb.h };
          applyViewBox(svg);
          return;
        }
        if (editMode === 'wire' && wireStartPin && wirePrev) {
          const midX = Math.round((0.5 * (wireStartPin.sx + sx)) / G) * G;
          wirePrev.setAttribute('points', `${wireStartPin.sx},${wireStartPin.sy} ${midX},${wireStartPin.sy} ${midX},${sy} ${sx},${sy}`);
          wirePrev.setAttribute('visibility', 'visible');
        }
      });
      window.addEventListener('pointerup', () => { if (panning) { panning = false; svg.style.cursor = 'default'; } });

      // Net click -> toggle waveform cross-probe
      svg.querySelectorAll('.spice-net').forEach(el => {
        const sig = el.getAttribute('data-signal');
        if (!sig) return;
        if (schemProbed.has(sig)) el.classList.add('probed');
        el.addEventListener('click', (e) => {
          if (editMode !== 'move') return;
          e.stopPropagation();
          if (schemProbed.has(sig)) schemProbed.delete(sig); else schemProbed.add(sig);
          renderSchemPlot();
          renderNavigator(lastSchemResult?.diagnostics || []);
        });
      });

      // Pin click -> orthogonal snap wire drawing
      svg.querySelectorAll('.spice-pin').forEach(pinEl => {
        pinEl.addEventListener('click', (e) => {
          e.stopPropagation();
          const pin = { instanceId: pinEl.getAttribute('data-instance'), pinName: pinEl.getAttribute('data-pin') };
          if (!wireStartPin) {
            setMode('wire');
            const pt = svgPoint(svg, e.clientX, e.clientY);
            wireStartPin = { ...pin, sx: Math.round(pt.x / G) * G, sy: Math.round(pt.y / G) * G };
            pinEl.classList.add('wire-start');
          } else {
            const start = wireStartPin;
            wireStartPin = null;
            if (wirePrev) wirePrev.setAttribute('visibility', 'hidden');
            connectPins({ instanceId: start.instanceId, pinName: start.pinName }, pin);
            setMode('move');
          }
        });
      });

      // Component drag with snap grid + cyan alignment snap guide lines
      svg.querySelectorAll('.spice-component').forEach(compEl => {
        const instId = compEl.getAttribute('data-instance');
        if (instId === selectedInstance) compEl.classList.add('selected');
        let dragging = false, moved = false, startPt = null, origX = 0, origY = 0;

        compEl.addEventListener('pointerdown', (e) => {
          if (editMode !== 'move' || e.target.classList.contains('spice-pin')) return;
          const comp = activeSheet().components.find(c => c.instanceId === instId);
          if (!comp) return;
          selectedInstance = instId;
          svg.querySelectorAll('.spice-component').forEach(el => el.classList.toggle('selected', el === compEl));
          renderInspector();
          renderNavigator(lastSchemResult?.diagnostics || []);
          dragging = true; moved = false;
          startPt = svgPoint(svg, e.clientX, e.clientY);
          origX = comp.x; origY = comp.y;
          compEl.classList.add('dragging');
          compEl.setPointerCapture(e.pointerId);
          e.stopPropagation();
        });

        compEl.addEventListener('pointermove', (e) => {
          if (!dragging) return;
          const pt = svgPoint(svg, e.clientX, e.clientY);
          const dx = pt.x - startPt.x, dy = pt.y - startPt.y;
          if (Math.hypot(dx, dy) > 3) moved = true;
          const useGrid = gridStyle() !== 'off';
          let nx = useGrid ? Math.round((origX + dx) / G) * G : (origX + dx);
          let ny = useGrid ? Math.round((origY + dy) / G) * G : (origY + dy);

          let alignedX = false, alignedY = false;
          for (const other of activeSheet().components) {
            if (other.instanceId === instId) continue;
            if (Math.abs(nx - other.x) <= G * 0.6) { nx = other.x; alignedX = true; }
            if (Math.abs(ny - other.y) <= G * 0.6) { ny = other.y; alignedY = true; }
          }
          if (snapV) { snapV.setAttribute('x1', nx); snapV.setAttribute('x2', nx); snapV.setAttribute('visibility', alignedX ? 'visible' : 'hidden'); }
          if (snapH) { snapH.setAttribute('y1', ny); snapH.setAttribute('y2', ny); snapH.setAttribute('visibility', alignedY ? 'visible' : 'hidden'); }
          compEl.setAttribute('transform', `translate(${nx.toFixed(1)},${ny.toFixed(1)})`);
          compEl.dataset.nx = nx; compEl.dataset.ny = ny;
        });

        compEl.addEventListener('pointerup', (e) => {
          if (!dragging) return;
          dragging = false;
          compEl.classList.remove('dragging');
          if (snapV) snapV.setAttribute('visibility', 'hidden');
          if (snapH) snapH.setAttribute('visibility', 'hidden');
          try { compEl.releasePointerCapture(e.pointerId); } catch {}
          if (moved && compEl.dataset.nx !== undefined) {
            const comp = activeSheet().components.find(c => c.instanceId === instId);
            if (comp) {
              pushUndo();
              comp.x = Number(compEl.dataset.nx);
              comp.y = Number(compEl.dataset.ny);
              refreshAllSchematicViews();
              runSchematicSim();
            }
          } else {
            const sig = compEl.getAttribute('data-signal');
            if (sig) {
              if (schemProbed.has(sig)) schemProbed.delete(sig); else schemProbed.add(sig);
              renderSchemPlot();
            }
          }
        });
      });
    }

    // ---- INTERACTIVE CROSSHAIR WAVEFORM PLOTTER ----
    function drawPlot(canvas, axis, seriesMap, activeKeys, logX = false, barMode = false, dbMode = false, cursorIdx = -1) {
      const ctx = canvas.getContext('2d');
      const W = canvas.width, H = canvas.height;
      ctx.clearRect(0, 0, W, H);
      ctx.fillStyle = '#080e1a';
      ctx.fillRect(0, 0, W, H);
      const pad = { l: 58, r: 16, t: 16, b: 28 };
      const pw = W - pad.l - pad.r, ph = H - pad.t - pad.b;

      const keys = [...activeKeys].filter(k => seriesMap[k] && seriesMap[k].length > 0);
      if (!axis || axis.length === 0 || keys.length === 0) {
        ctx.fillStyle = '#64748b'; ctx.font = '12.5px monospace';
        ctx.fillText('Click any net wire or signal chip to plot waveform.', pad.l + 14, H / 2);
        return null;
      }

      const txVal = (y) => dbMode ? 20.0 * Math.log10(Math.max(Math.abs(y), 1e-12)) : y;
      const xs = logX ? axis.map(v => Math.log10(Math.max(v, 1e-30))) : axis;
      let minX = Math.min(...xs), maxX = Math.max(...xs);
      if (maxX === minX) { minX -= 1; maxX += 1; }
      let minY = Infinity, maxY = -Infinity;
      for (const k of keys) {
        for (const rawY of seriesMap[k]) {
          const y = txVal(rawY);
          if (Number.isFinite(y)) { minY = Math.min(minY, y); maxY = Math.max(maxY, y); }
        }
      }
      if (!Number.isFinite(minY)) return null;
      if (barMode) minY = Math.min(0, minY);
      if (maxY === minY) { minY -= 0.5; maxY += 0.5; }
      const margin = (maxY - minY) * 0.08;
      minY -= margin; maxY += margin;

      const toX = x => pad.l + ((x - minX) / (maxX - minX)) * pw;
      const toY = y => pad.t + ph - ((y - minY) / (maxY - minY)) * ph;

      ctx.strokeStyle = '#172235'; ctx.lineWidth = 1;
      for (let g = 0; g <= 4; ++g) {
        const y = pad.t + (ph * g) / 4;
        ctx.beginPath(); ctx.moveTo(pad.l, y); ctx.lineTo(W - pad.r, y); ctx.stroke();
        const val = maxY - ((maxY - minY) * g) / 4;
        ctx.fillStyle = '#94a3b8'; ctx.font = '10.5px monospace';
        ctx.fillText(val.toPrecision(3) + (dbMode ? 'dB' : ''), 4, y + 4);
      }

      keys.forEach((k, idx) => {
        const col = COLORS[idx % COLORS.length];
        const ys = seriesMap[k].map(txVal);
        ctx.strokeStyle = col; ctx.fillStyle = col; ctx.lineWidth = 2;
        if (barMode || ys.length === 1) {
          ys.forEach((y, i) => {
            const x = toX(xs[i]), y0 = toY(0), y1 = toY(y);
            ctx.fillRect(x - 6, Math.min(y0, y1), 12, Math.max(2, Math.abs(y1 - y0)));
          });
        } else {
          ctx.beginPath();
          ys.forEach((y, i) => { const x = toX(xs[i]), yy = toY(y); if (i === 0) ctx.moveTo(x, yy); else ctx.lineTo(x, yy); });
          ctx.stroke();
        }
      });

      if (cursorIdx >= 0 && cursorIdx < xs.length) {
        const cx = toX(xs[cursorIdx]);
        ctx.strokeStyle = 'rgba(248, 250, 252, 0.45)'; ctx.setLineDash([3, 3]); ctx.lineWidth = 1;
        ctx.beginPath(); ctx.moveTo(cx, pad.t); ctx.lineTo(cx, pad.t + ph); ctx.stroke();
        ctx.setLineDash([]);
        keys.forEach((k, idx) => {
          const y = txVal(seriesMap[k][cursorIdx]);
          ctx.fillStyle = COLORS[idx % COLORS.length];
          ctx.beginPath(); ctx.arc(cx, toY(y), 4, 0, Math.PI * 2); ctx.fill();
        });
      }

      ctx.fillStyle = '#94a3b8'; ctx.font = '10.5px monospace';
      ctx.fillText(axis[0].toPrecision(3), pad.l, H - 8);
      ctx.fillText(axis[axis.length - 1].toPrecision(3), W - pad.r - 58, H - 8);
      return { xs, minX, maxX, pad, pw };
    }

    function renderOpTables(data) {
      const vBody = document.querySelector('#op-v-table tbody');
      const iBody = document.querySelector('#op-i-table tbody');
      vBody.innerHTML = ''; iBody.innerHTML = '';
      for (const [net, val] of Object.entries(data.op_voltages || {})) {
        const tr = document.createElement('tr');
        tr.innerHTML = `<td>v(${net})</td><td class="ok">${fmtEng(val, 'V')}</td>`;
        vBody.appendChild(tr);
      }
      for (const [inst, val] of Object.entries(data.op_currents || {})) {
        const tr = document.createElement('tr');
        const arrow = Math.abs(val) < 1e-12 ? '•' : (val >= 0 ? '→' : '←');
        tr.innerHTML = `<td>i(${inst})</td><td class="amber">${arrow} ${fmtEng(Math.abs(val), 'A')}</td>`;
        iBody.appendChild(tr);
      }
    }

    let lastPlotMeta = null;
    function renderSchemPlot(cursorIdx = -1) {
      if (!lastSchemResult) return;
      const host = document.getElementById('svg-host');
      host.querySelectorAll('.spice-net').forEach(el => {
        const sig = el.getAttribute('data-signal');
        el.classList.toggle('probed', !!sig && schemProbed.has(sig));
      });
      const chips = document.getElementById('schem-signals');
      chips.innerHTML = '';
      const series = lastSchemResult.series || {};
      const keys = Object.keys(series);
      if (keys.length > 0 && ![...schemProbed].some(k => series[k])) {
        schemProbed = new Set([keys.includes('v(out)') ? 'v(out)' : keys[0]]);
      }
      keys.forEach(sig => {
        const b = document.createElement('button');
        b.className = 'chip' + (schemProbed.has(sig) ? ' on' : '');
        b.textContent = sig;
        b.onclick = () => { if (schemProbed.has(sig)) schemProbed.delete(sig); else schemProbed.add(sig); renderSchemPlot(); };
        chips.appendChild(b);
      });
      const isAc = document.getElementById('schem-analysis').value === 'ac';
      lastPlotMeta = drawPlot(document.getElementById('schem-canvas'), lastSchemResult.axis || [], series, schemProbed, isAc, false, isAc && bodeDb, cursorIdx);
      const axis = lastSchemResult.axis || [];
      if (cursorIdx >= 0 && cursorIdx < axis.length) {
        const vals = [...schemProbed].filter(s => series[s]).map(s => `${s}=${series[s][cursorIdx].toPrecision(5)}`).join('  |  ');
        document.getElementById('schem-readout').textContent = `Cursor X=${axis[cursorIdx].toPrecision(5)}  |  ${vals}`;
      } else {
        const summary = [...schemProbed].filter(s => series[s]).map(s => {
          const arr = series[s];
          return `${s}: ${arr[0].toPrecision(4)} → ${arr[arr.length - 1].toPrecision(4)}`;
        }).join('  |  ');
        document.getElementById('schem-readout').textContent = summary || 'Click any net wire on the schematic to cross-probe.';
      }
    }

    document.getElementById('schem-canvas').addEventListener('pointermove', (e) => {
      if (!lastPlotMeta || !lastSchemResult?.axis?.length) return;
      const canvas = e.target;
      const rect = canvas.getBoundingClientRect();
      const x = (e.clientX - rect.left) * (canvas.width / rect.width);
      const frac = Math.min(1, Math.max(0, (x - lastPlotMeta.pad.l) / Math.max(1, lastPlotMeta.pw)));
      const targetX = lastPlotMeta.minX + frac * (lastPlotMeta.maxX - lastPlotMeta.minX);
      let best = 0, bestD = Infinity;
      lastPlotMeta.xs.forEach((v, i) => { const d = Math.abs(v - targetX); if (d < bestD) { bestD = d; best = i; } });
      renderSchemPlot(best);
    });
    document.getElementById('schem-canvas').addEventListener('pointerleave', () => renderSchemPlot(-1));
    document.getElementById('btn-bode-toggle').addEventListener('click', (e) => {
      bodeDb = !bodeDb;
      e.target.textContent = 'dB: ' + (bodeDb ? 'On' : 'Off');
      renderSchemPlot();
    });
    document.getElementById('btn-csv-wave').addEventListener('click', () => {
      if (!lastSchemResult?.axis?.length) return;
      const cols = Object.keys(lastSchemResult.series || {});
      const rows = ['axis,' + cols.join(',')];
      lastSchemResult.axis.forEach((x, i) => rows.push([x, ...cols.map(c => lastSchemResult.series[c][i])].join(',')));
      downloadText(`${currentSchem.name}_waveforms.csv`, rows.join('\n'), 'text/csv;charset=utf-8');
    });

    // ---- MULTI-FORMAT EXPORT PREVIEW & DOWNLOAD ----
    let lastExport = null;
    async function refreshExportPreview() {
      const format = document.getElementById('export-format').value;
      const analysis = document.getElementById('schem-analysis').value;
      const resp = await fetch('/api/schematic/export', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          document: currentSchem, format, analysis,
          gridSize: gridPitch(), showGrid: gridStyle() !== 'off', gridStyle: gridStyle(),
          includeOp: document.getElementById('chk-op').checked,
          activeSheetId: activeSheet().sheetId
        })
      });
      if (!resp.ok) {
        const err = await resp.json();
        document.getElementById('schem-spice').value = `Export unavailable: ${err.error || 'ERC failed'}`;
        return;
      }
      lastExport = await resp.json();
      document.getElementById('schem-spice').value = lastExport.content || '';
      document.getElementById('export-meta').textContent = `File: ${lastExport.filename} (${(lastExport.content || '').length} chars)`;
    }
    document.getElementById('export-format').addEventListener('change', refreshExportPreview);
    document.getElementById('btn-export-copy').addEventListener('click', () => {
      navigator.clipboard?.writeText(document.getElementById('schem-spice').value || '');
    });
    document.getElementById('btn-export-download').addEventListener('click', async () => {
      await refreshExportPreview();
      if (lastExport?.content) downloadText(lastExport.filename, lastExport.content, lastExport.content_type);
    });

    // ---- LIVE SCHEMATIC SIMULATION ----
    async function runSchematicSim() {
      const analysis = document.getElementById('schem-analysis').value;
      const curSheet = activeSheet();
      const resp = await fetch('/api/schematic/simulate', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          document: currentSchem,
          analysis,
          gridSize: gridPitch(),
          showGrid: gridStyle() !== 'off',
          gridStyle: gridStyle(),
          showOp: document.getElementById('chk-op').checked,
          activeSheetId: curSheet.sheetId
        })
      });
      const data = await resp.json();
      document.getElementById('svg-host').innerHTML = data.svg || '';
      const ercEl = document.getElementById('erc-status');
      renderSheetBar();
      renderNavigator(data.diagnostics || []);
      if (!data.erc_ok) {
        ercEl.className = 'err';
        ercEl.textContent = `ERC Failed (${(data.diagnostics || []).length} issue(s)) — check Navigator & ERC tab.`;
        bindSvgInteractions();
        return;
      }
      ercEl.className = data.status === 'Succeeded' ? 'ok' : 'err';
      const sheetCount = allSheets().length;
      const sheetNote = sheetCount > 1 ? ` | Sheet '${curSheet.title || curSheet.sheetId}' (${sheetCount} sheets)` : '';
      ercEl.textContent = `ERC PASS | ${data.status} (${analysis.toUpperCase()}) | Samples=${(data.axis || []).length}${sheetNote} | Live DC V/I overlaid`;
      lastSchemResult = data;
      bindSvgInteractions();
      renderOpTables(data);
      renderSchemPlot();
      refreshExportPreview();
    }

    // ---- COMPONENT & MODEL LIBRARY STUDIO ----
    function renderLibraryStudio() {
      const q = (document.getElementById('lib-search').value || '').trim().toLowerCase();
      const host = document.getElementById('lib-cards');
      host.innerHTML = '';
      libraryEntries.filter(item => {
        const p = item.part;
        return !q || p.name.toLowerCase().includes(q) || p.category.toLowerCase().includes(q) || (p.attachedModel?.modelName || '').toLowerCase().includes(q);
      }).forEach(item => {
        const p = item.part;
        const card = document.createElement('div');
        card.className = 'lib-card' + (p.isBuiltIn ? '' : ' user-owned');
        card.innerHTML = `
          <div class="lib-card-head">
            ${item.preview_svg}
            <div>
              <div style="font-weight:700;font-size:12.5px">${p.name}</div>
              <div style="font-size:10.5px;color:var(--muted)">${p.category} · ${p.isBuiltIn ? 'Foundation' : 'User Custom'}</div>
              <div style="font-size:10.5px;font-family:var(--mono);color:var(--accent)">${p.attachedModel?.modelName || p.baseDeviceKindId} (${(p.pins||[]).map(x=>x.name).join(',')})</div>
            </div>
          </div>
          <div style="font-size:11px;color:var(--dim)">${p.description || ''}</div>
          <div style="display:flex;gap:5px;flex-wrap:wrap;margin-top:auto">
            <button class="icon-btn primary btn-place" style="padding:3px 8px;font-size:11px">Place</button>
            <button class="icon-btn btn-edit" style="padding:3px 8px;font-size:11px">${p.isBuiltIn ? 'Clone' : 'Edit'}</button>
            ${p.isBuiltIn ? '' : '<button class="icon-btn danger btn-del" style="padding:3px 8px;font-size:11px">Delete</button>'}
          </div>
        `;
        card.querySelector('.btn-place').onclick = () => placeLibraryPart(p);
        card.querySelector('.btn-edit').onclick = () => loadPartIntoEditor(p, p.isBuiltIn);
        const delBtn = card.querySelector('.btn-del');
        if (delBtn) {
          delBtn.onclick = async () => {
            await fetch('/api/library/' + encodeURIComponent(p.id), { method: 'DELETE' });
            await loadLibrary();
          };
        }
        host.appendChild(card);
      });
    }
    document.getElementById('lib-search').addEventListener('input', renderLibraryStudio);

    // ---- INTERACTIVE VECTOR SYMBOL DRAWING CANVAS ----
    const SYM_GRID = 10;
    let symPrimitives = [];
    let symTool = 'line'; // 'line' | 'rect' | 'circle' | 'polygon' | 'text' | 'pin' | 'select'
    let symColor = '#38bdf8';
    let symDrag = null;
    let symPolyPts = [];
    let symDraggingPinIdx = -1;

    function parseEditorPins() {
      return document.getElementById('lib-pins').value.split('\n').map(l => l.trim()).filter(Boolean).map(l => {
        const [name, x, y, role] = l.split(',').map(s => s.trim());
        return { name: name || 'p', localX: Number(x) || 0, localY: Number(y) || 0, role: role || 'passive' };
      });
    }
    function writeEditorPins(pins) {
      document.getElementById('lib-pins').value = pins.map(p => `${p.name},${p.localX},${p.localY},${p.role || 'passive'}`).join('\n');
    }

    const SYM_STARTERS = {
      ic: () => [
        { kind: 'rect', x1: -28, y1: -20, x2: 28, y2: 20, stroke: '#38bdf8', fill: '#172554', strokeWidth: 2 },
        { kind: 'text', x1: 0, y1: 4, text: 'IC', stroke: '#bfdbfe', strokeWidth: 1.5 }
      ],
      opamp: () => [
        { kind: 'polygon', points: '-24,-22 -24,22 26,0', stroke: '#38bdf8', fill: '#1e293b', strokeWidth: 2 },
        { kind: 'line', x1: -40, y1: -10, x2: -24, y2: -10, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'line', x1: -40, y1: 10, x2: -24, y2: 10, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'line', x1: 26, y1: 0, x2: 40, y2: 0, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'text', x1: -16, y1: -6, text: '+', stroke: '#e2e8f0', strokeWidth: 1.5 },
        { kind: 'text', x1: -16, y1: 13, text: '-', stroke: '#e2e8f0', strokeWidth: 1.5 }
      ],
      diode: () => [
        { kind: 'line', x1: -40, y1: 0, x2: -10, y2: 0, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'polygon', points: '-10,-12 -10,12 12,0', stroke: '#f43f5e', fill: '#1e293b', strokeWidth: 2 },
        { kind: 'line', x1: 12, y1: -12, x2: 12, y2: 12, stroke: '#f43f5e', strokeWidth: 2.5 },
        { kind: 'line', x1: 12, y1: 0, x2: 40, y2: 0, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'line', x1: 4, y1: -18, x2: 14, y2: -26, stroke: '#f59e0b', strokeWidth: 1.6 },
        { kind: 'line', x1: 12, y1: -16, x2: 22, y2: -24, stroke: '#f59e0b', strokeWidth: 1.6 }
      ],
      pi: () => [
        { kind: 'rect', x1: -28, y1: -18, x2: 28, y2: 18, stroke: '#10b981', fill: '#064e3b', strokeWidth: 2 },
        { kind: 'line', x1: -40, y1: 0, x2: -28, y2: 0, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'line', x1: 28, y1: 0, x2: 40, y2: 0, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'line', x1: 0, y1: 18, x2: 0, y2: 20, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'text', x1: 0, y1: 4, text: 'PI', stroke: '#a7f3d0', strokeWidth: 1.5 }
      ],
      xfmr: () => [
        { kind: 'polygon', points: '-20,-18 -20,18 22,0', stroke: '#a855f7', fill: '#1e1b4b', strokeWidth: 2 },
        { kind: 'circle', x1: 26, y1: 0, radius: 4, stroke: '#a855f7', fill: '#080e1a', strokeWidth: 2 },
        { kind: 'line', x1: -40, y1: 0, x2: -20, y2: 0, stroke: '#e2e8f0', strokeWidth: 2 },
        { kind: 'line', x1: 30, y1: 0, x2: 40, y2: 0, stroke: '#e2e8f0', strokeWidth: 2 }
      ]
    };

    function escapeHtmlAttr(s) {
      return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function renderSymbolCanvas(previewMarkup = '') {
      const svg = document.getElementById('sym-canvas');
      if (!svg) return;
      const pins = parseEditorPins();
      let body = `
        <defs>
          <pattern id="sym-grid-10" width="10" height="10" patternUnits="userSpaceOnUse">
            <path d="M 10 0 L 0 0 0 10" fill="none" stroke="#152033" stroke-width="0.6" />
          </pattern>
        </defs>
        <rect x="-60" y="-40" width="120" height="80" fill="url(#sym-grid-10)" />
        <line x1="-60" y1="0" x2="60" y2="0" stroke="#1e293b" stroke-width="0.8" stroke-dasharray="2 2" />
        <line x1="0" y1="-40" x2="0" y2="40" stroke="#1e293b" stroke-width="0.8" stroke-dasharray="2 2" />
      `;
      symPrimitives.forEach((p, idx) => {
        const stroke = escapeHtmlAttr(p.stroke || '#e2e8f0');
        const fill = escapeHtmlAttr(p.fill || 'none');
        const sw = p.strokeWidth || 2;
        if (p.kind === 'line') {
          body += `<line data-prim="${idx}" x1="${p.x1}" y1="${p.y1}" x2="${p.x2}" y2="${p.y2}" stroke="${stroke}" stroke-width="${sw}" stroke-linecap="round" />`;
        } else if (p.kind === 'rect') {
          const rx = Math.min(p.x1, p.x2), ry = Math.min(p.y1, p.y2);
          body += `<rect data-prim="${idx}" x="${rx}" y="${ry}" width="${Math.abs(p.x2 - p.x1)}" height="${Math.abs(p.y2 - p.y1)}" rx="2" fill="${fill}" stroke="${stroke}" stroke-width="${sw}" />`;
        } else if (p.kind === 'circle') {
          body += `<circle data-prim="${idx}" cx="${p.x1}" cy="${p.y1}" r="${p.radius || 8}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}" />`;
        } else if (p.kind === 'polyline') {
          body += `<polyline data-prim="${idx}" points="${escapeHtmlAttr(p.points)}" fill="none" stroke="${stroke}" stroke-width="${sw}" stroke-linejoin="round" />`;
        } else if (p.kind === 'polygon') {
          body += `<polygon data-prim="${idx}" points="${escapeHtmlAttr(p.points)}" fill="${fill}" stroke="${stroke}" stroke-width="${sw}" stroke-linejoin="round" />`;
        } else if (p.kind === 'text') {
          body += `<text data-prim="${idx}" x="${p.x1}" y="${p.y1}" text-anchor="middle" fill="${stroke}" font-family="monospace" font-size="9" font-weight="bold">${escapeHtmlAttr(p.text)}</text>`;
        }
      });
      if (symPolyPts.length > 0) {
        body += `<polyline points="${symPolyPts.map(pt => `${pt.x},${pt.y}`).join(' ')}" fill="none" stroke="#f59e0b" stroke-width="1.5" stroke-dasharray="3 2" />`;
      }
      if (previewMarkup) body += previewMarkup;
      pins.forEach((pin, idx) => {
        body += `<g class="sym-pin-node" data-pin-idx="${idx}" transform="translate(${pin.localX},${pin.localY})">
          <circle cx="0" cy="0" r="3.4" fill="#0f172a" stroke="#38bdf8" stroke-width="1.5" />
          <text x="0" y="-6" text-anchor="middle" fill="#7dd3fc" font-family="monospace" font-size="6.5">${escapeHtmlAttr(pin.name)}</text>
        </g>`;
      });
      svg.innerHTML = body;
      document.getElementById('sym-counts').textContent = `${symPrimitives.length} shape(s) · ${pins.length} pin(s)`;
    }

    function symSvgCoords(e) {
      const svg = document.getElementById('sym-canvas');
      const pt = svgPoint(svg, e.clientX, e.clientY);
      return {
        x: Math.max(-55, Math.min(55, Math.round(pt.x / SYM_GRID) * SYM_GRID)),
        y: Math.max(-35, Math.min(35, Math.round(pt.y / SYM_GRID) * SYM_GRID))
      };
    }

    function setSymTool(tool) {
      symTool = tool;
      symPolyPts = [];
      document.querySelectorAll('[data-sym-tool]').forEach(b => b.classList.toggle('mode-on', b.dataset.symTool === tool));
      const hints = {
        line: 'LINE: Drag on 10px grid to draw a vector line segment.',
        rect: 'RECT: Drag on 10px grid to draw a box/body.',
        circle: 'CIRCLE: Drag from center outward to set radius.',
        polygon: 'POLY: Click vertices, then double-click to close polygon.',
        text: 'TEXT: Click anywhere to place a monospace symbol label.',
        pin: 'PIN: Click grid point to add a new ordered netlist pin.',
        select: 'MOVE PIN: Drag any blue pin circle to reposition it on the 10px grid.'
      };
      document.getElementById('sym-status').textContent = hints[tool] || '';
      renderSymbolCanvas();
    }
    document.querySelectorAll('[data-sym-tool]').forEach(b => b.addEventListener('click', () => setSymTool(b.dataset.symTool)));
    document.querySelectorAll('[data-sym-color]').forEach(b => b.addEventListener('click', () => {
      symColor = b.dataset.symColor;
      document.querySelectorAll('[data-sym-color]').forEach(s => s.classList.toggle('active', s === b));
    }));
    document.getElementById('sym-undo').addEventListener('click', () => {
      if (symPolyPts.length > 0) symPolyPts.pop();
      else symPrimitives.pop();
      renderSymbolCanvas();
    });
    document.getElementById('sym-clear').addEventListener('click', () => {
      symPrimitives = [];
      symPolyPts = [];
      renderSymbolCanvas();
    });
    document.querySelectorAll('[data-sym-starter]').forEach(b => b.addEventListener('click', () => {
      symPrimitives = SYM_STARTERS[b.dataset.symStarter]();
      document.getElementById('lib-sym').value = 'custom';
      renderSymbolCanvas();
    }));
    document.getElementById('lib-pins').addEventListener('input', () => renderSymbolCanvas());

    const symSvgEl = document.getElementById('sym-canvas');
    symSvgEl.addEventListener('pointerdown', (e) => {
      const pt = symSvgCoords(e);
      const fillVal = document.getElementById('sym-fill-chk').checked ? '#172554' : 'none';
      if (symTool === 'select') {
        const pinNode = e.target.closest('.sym-pin-node');
        if (pinNode) {
          symDraggingPinIdx = Number(pinNode.dataset.pinIdx);
          symSvgEl.setPointerCapture(e.pointerId);
        }
        return;
      }
      if (symTool === 'pin') {
        const pins = parseEditorPins();
        const name = prompt('New pin name (appended in netlist order):', 'p' + (pins.length + 1));
        if (!name) return;
        pins.push({ name: name.trim(), localX: pt.x, localY: pt.y, role: 'passive' });
        writeEditorPins(pins);
        renderSymbolCanvas();
        return;
      }
      if (symTool === 'text') {
        const txt = prompt('Symbol text label (e.g. +, -, CLK, OUT):', '+');
        if (!txt) return;
        symPrimitives.push({ kind: 'text', x1: pt.x, y1: pt.y, text: txt.trim(), stroke: symColor, strokeWidth: 1.5 });
        document.getElementById('lib-sym').value = 'custom';
        renderSymbolCanvas();
        return;
      }
      if (symTool === 'polygon') {
        symPolyPts.push(pt);
        renderSymbolCanvas();
        return;
      }
      symDrag = { x1: pt.x, y1: pt.y, x2: pt.x, y2: pt.y, fill: fillVal };
      symSvgEl.setPointerCapture(e.pointerId);
    });

    symSvgEl.addEventListener('pointermove', (e) => {
      const pt = symSvgCoords(e);
      if (symDraggingPinIdx >= 0) {
        const pins = parseEditorPins();
        if (pins[symDraggingPinIdx]) {
          pins[symDraggingPinIdx].localX = pt.x;
          pins[symDraggingPinIdx].localY = pt.y;
          writeEditorPins(pins);
          renderSymbolCanvas();
        }
        return;
      }
      if (!symDrag) return;
      symDrag.x2 = pt.x; symDrag.y2 = pt.y;
      let prev = '';
      if (symTool === 'line') {
        prev = `<line x1="${symDrag.x1}" y1="${symDrag.y1}" x2="${pt.x}" y2="${pt.y}" stroke="${symColor}" stroke-width="2" stroke-dasharray="3 2" />`;
      } else if (symTool === 'rect') {
        prev = `<rect x="${Math.min(symDrag.x1, pt.x)}" y="${Math.min(symDrag.y1, pt.y)}" width="${Math.abs(pt.x - symDrag.x1)}" height="${Math.abs(pt.y - symDrag.y1)}" fill="${symDrag.fill}" stroke="${symColor}" stroke-width="2" stroke-dasharray="3 2" />`;
      } else if (symTool === 'circle') {
        const r = Math.max(4, Math.round(Math.hypot(pt.x - symDrag.x1, pt.y - symDrag.y1)));
        prev = `<circle cx="${symDrag.x1}" cy="${symDrag.y1}" r="${r}" fill="${symDrag.fill}" stroke="${symColor}" stroke-width="2" stroke-dasharray="3 2" />`;
      }
      renderSymbolCanvas(prev);
    });

    symSvgEl.addEventListener('pointerup', (e) => {
      if (symDraggingPinIdx >= 0) {
        symDraggingPinIdx = -1;
        try { symSvgEl.releasePointerCapture(e.pointerId); } catch {}
        return;
      }
      if (!symDrag) return;
      const d = symDrag; symDrag = null;
      try { symSvgEl.releasePointerCapture(e.pointerId); } catch {}
      if (symTool === 'line' && (d.x1 !== d.x2 || d.y1 !== d.y2)) {
        symPrimitives.push({ kind: 'line', x1: d.x1, y1: d.y1, x2: d.x2, y2: d.y2, stroke: symColor, strokeWidth: 2 });
      } else if (symTool === 'rect' && (d.x1 !== d.x2 || d.y1 !== d.y2)) {
        symPrimitives.push({ kind: 'rect', x1: d.x1, y1: d.y1, x2: d.x2, y2: d.y2, stroke: symColor, fill: d.fill, strokeWidth: 2 });
      } else if (symTool === 'circle') {
        const r = Math.max(4, Math.round(Math.hypot(d.x2 - d.x1, d.y2 - d.y1)));
        symPrimitives.push({ kind: 'circle', x1: d.x1, y1: d.y1, radius: r, stroke: symColor, fill: d.fill, strokeWidth: 2 });
      }
      if (symPrimitives.length > 0) document.getElementById('lib-sym').value = 'custom';
      renderSymbolCanvas();
    });

    symSvgEl.addEventListener('dblclick', () => {
      if (symTool === 'polygon' && symPolyPts.length >= 3) {
        const fillVal = document.getElementById('sym-fill-chk').checked ? '#172554' : 'none';
        symPrimitives.push({
          kind: 'polygon',
          points: symPolyPts.map(p => `${p.x},${p.y}`).join(' '),
          stroke: symColor, fill: fillVal, strokeWidth: 2
        });
        symPolyPts = [];
        document.getElementById('lib-sym').value = 'custom';
        renderSymbolCanvas();
      }
    });

    function loadPartIntoEditor(p, asClone = false) {
      document.getElementById('lib-id').value = asClone ? 'user.' + p.id.replace(/^foundation\./, '') + '_custom' : p.id;
      document.getElementById('lib-name').value = asClone ? p.name + ' (Custom)' : p.name;
      document.getElementById('lib-cat').value = asClone ? 'Custom Library' : p.category;
      document.getElementById('lib-kind').value = p.baseDeviceKindId;
      document.getElementById('lib-sym').value = p.symbolPrimitives?.length ? 'custom' : (p.symbolStyle || 'ic_box');
      document.getElementById('lib-mkind').value = p.attachedModel?.kind || 'spiceModelCard';
      document.getElementById('lib-mname').value = p.attachedModel?.modelName || '';
      document.getElementById('lib-desc').value = p.description || '';
      document.getElementById('lib-pins').value = (p.pins || []).map(x => `${x.name},${x.localX},${x.localY},${x.role || 'passive'}`).join('\n');
      document.getElementById('lib-params').value = Object.entries(p.defaultParameters || {}).map(([k, v]) => `${k}=${v}`).join('\n');
      document.getElementById('lib-model-text').value = p.attachedModel?.spiceText || '';
      symPrimitives = structuredClone(p.symbolPrimitives || []);
      renderSymbolCanvas();
    }

    const LIB_TEMPLATES = {
      diode: {
        id: 'user.diode.led_red', name: 'Red LED (1.8V)', cat: 'Custom Diodes', kind: 'spice.diode', sym: 'custom', mkind: 'spiceModelCard', mname: 'LED_RED',
        desc: 'Custom GaAsP red LED with custom vector symbol & .MODEL card.',
        pins: 'p,-40,0,anode\nn,40,0,cathode', params: 'is_amps=1e-20\nn_emission=1.85',
        model: '.MODEL LED_RED D(IS=1e-20 N=1.85 RS=4.0 CJO=15p)', starter: 'diode'
      },
      mosfet: {
        id: 'user.mos.nch_lv', name: 'Low-Vt BSIM4 NMOS', cat: 'Custom Transistors', kind: 'spice.mosfet', sym: 'nmos', mkind: 'spiceModelCard', mname: 'NCH_LV',
        desc: 'Custom low-Vt BSIM4 (LEVEL=54) N-channel MOSFET.',
        pins: 'd,20,-20,drain\ng,-40,0,gate\ns,20,20,source\nb,40,0,bulk', params: 'w_m=2e-6\nl_m=65e-9\nlevel=54\nvth0_volts=0.22',
        model: '.MODEL NCH_LV NMOS(LEVEL=54 VTH0=0.22 TOXE=2.0n U0=460 VSAT=1.2e5)', starter: null
      },
      subckt: {
        id: 'user.subckt.pi_attenuator', name: '6dB Pi Attenuator', cat: 'Custom Subcircuits', kind: 'spice.subckt', sym: 'custom', mkind: 'subcircuit', mname: 'PI_ATTEN_6DB',
        desc: '50-Ohm 6 dB resistive Pi attenuator macro (.SUBCKT).',
        pins: 'in,-40,0,input\nout,40,0,output\ngnd,0,20,ground', params: '',
        model: '.SUBCKT PI_ATTEN_6DB in out gnd\n  Rsh1 in gnd 150.5\n  Rser in out 37.35\n  Rsh2 out gnd 150.5\n.ENDS PI_ATTEN_6DB', starter: 'pi'
      }
    };
    document.querySelectorAll('[data-tpl]').forEach(b => b.addEventListener('click', () => {
      const t = LIB_TEMPLATES[b.dataset.tpl];
      document.getElementById('lib-id').value = t.id;
      document.getElementById('lib-name').value = t.name;
      document.getElementById('lib-cat').value = t.cat;
      document.getElementById('lib-kind').value = t.kind;
      document.getElementById('lib-sym').value = t.sym;
      document.getElementById('lib-mkind').value = t.mkind;
      document.getElementById('lib-mname').value = t.mname;
      document.getElementById('lib-desc').value = t.desc;
      document.getElementById('lib-pins').value = t.pins;
      document.getElementById('lib-params').value = t.params;
      document.getElementById('lib-model-text').value = t.model;
      symPrimitives = t.starter ? SYM_STARTERS[t.starter]() : [];
      renderSymbolCanvas();
    }));
    document.getElementById('btn-lib-new').addEventListener('click', () => document.querySelector('[data-tpl="subckt"]').click());

    async function saveCustomPart(alsoPlace = false) {
      const pins = parseEditorPins();
      const defaultParameters = {};
      document.getElementById('lib-params').value.split('\n').map(l => l.trim()).filter(Boolean).forEach(l => {
        const [k, v] = l.split('=').map(s => s.trim());
        if (k && Number.isFinite(Number(v))) defaultParameters[k] = Number(v);
      });
      const mText = document.getElementById('lib-model-text').value.trim();
      const mName = document.getElementById('lib-mname').value.trim();
      const payload = {
        id: document.getElementById('lib-id').value.trim(),
        name: document.getElementById('lib-name').value.trim(),
        category: document.getElementById('lib-cat').value.trim(),
        description: document.getElementById('lib-desc').value.trim(),
        baseDeviceKindId: document.getElementById('lib-kind').value,
        spicePrefix: '',
        symbolStyle: symPrimitives.length > 0 ? 'custom' : document.getElementById('lib-sym').value,
        pins,
        defaultParameters,
        attachedModel: mText ? { kind: document.getElementById('lib-mkind').value, modelName: mName, spiceText: mText } : null,
        symbolPrimitives: symPrimitives.length > 0 ? symPrimitives : null
      };
      const st = document.getElementById('lib-status');
      const resp = await fetch('/api/library', {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload)
      });
      const data = await resp.json();
      if (!resp.ok) {
        st.className = 'err';
        st.textContent = 'Validation failed: ' + (data.diagnostics ? data.diagnostics.map(d => d.message).join('; ') : data.error);
        return;
      }
      st.className = 'ok';
      st.textContent = `Saved '${data.part.name}' (${symPrimitives.length} vector shapes) to persistent library!`;
      await loadLibrary();
      if (alsoPlace) placeLibraryPart(data.part);
    }
    document.getElementById('btn-lib-save').addEventListener('click', () => saveCustomPart(false));
    document.getElementById('btn-lib-save-place').addEventListener('click', () => saveCustomPart(true));

    // ---- BULK .LIB / .MOD / .SUB IMPORTER & JSON EXPORT/IMPORT ----
    async function runBulkSpiceImport(dryRun) {
      const spiceText = document.getElementById('bulk-spice-text').value;
      const defaultCategory = document.getElementById('bulk-spice-cat').value.trim() || 'Vendor Import';
      const overwrite = document.getElementById('bulk-spice-overwrite').checked;
      const st = document.getElementById('bulk-import-status');
      const prev = document.getElementById('bulk-import-preview');
      const diagsEl = document.getElementById('bulk-import-diags');
      prev.innerHTML = '';
      diagsEl.innerHTML = '';
      if (!spiceText.trim()) {
        st.className = 'err';
        st.textContent = 'Paste or load a .LIB / .MOD / .SUB file first.';
        return;
      }
      const resp = await fetch('/api/library/import-spice', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ spiceText, defaultCategory, overwrite, dryRun })
      });
      const data = await resp.json();
      if (!resp.ok) {
        st.className = 'err';
        st.textContent = 'Import error: ' + (data.error || resp.statusText);
        return;
      }
      st.className = data.error_count > 0 ? 'amber' : 'ok';
      st.textContent = `${data.dry_run ? 'Dry-Run Preview' : 'Imported'}: found=${data.total_found} | ${data.dry_run ? 'valid' : 'imported'}=${data.imported_count} | skipped=${data.skipped_count} | errors=${data.error_count}`;
      (data.components || []).forEach(entry => {
        const p = entry.part;
        const card = document.createElement('div');
        card.className = 'lib-card user-owned';
        card.style.padding = '6px';
        card.innerHTML = `
          <div class="lib-card-head">
            ${entry.preview_svg || ''}
            <div>
              <div style="font-weight:700;font-size:11px">${escapeHtmlAttr(p.name)}</div>
              <div style="font-size:9.5px;color:var(--accent);font-family:var(--mono)">${escapeHtmlAttr(p.baseDeviceKindId)} · ${(p.pins || []).length} pins</div>
            </div>
          </div>`;
        prev.appendChild(card);
      });
      (data.diagnostics || []).forEach(d => {
        const row = document.createElement('div');
        row.className = d.severity === 'error' ? 'err' : 'amber';
        const loc = d.lineNumber ? ` [L${d.lineNumber}]` : '';
        const item = d.itemName ? ` (${d.itemName})` : '';
        row.textContent = `${String(d.severity || '').toUpperCase()}${loc}${item} ${d.code}: ${d.message}`;
        diagsEl.appendChild(row);
      });
      if (!dryRun) {
        await loadLibrary();
      }
    }
    document.getElementById('btn-bulk-load-file').addEventListener('click', () => document.getElementById('file-bulk-spice').click());
    document.getElementById('file-bulk-spice').addEventListener('change', async (e) => {
      const f = e.target.files?.[0];
      if (!f) return;
      document.getElementById('bulk-spice-text').value = await f.text();
      await runBulkSpiceImport(true);
      e.target.value = '';
    });
    document.getElementById('btn-bulk-preview').addEventListener('click', () => runBulkSpiceImport(true));
    document.getElementById('btn-bulk-import').addEventListener('click', () => runBulkSpiceImport(false));

    document.getElementById('btn-lib-export-json').addEventListener('click', async () => {
      const resp = await fetch('/api/library/export');
      const text = await resp.text();
      const blob = new Blob([text], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'custom-library.spice-lib.json';
      a.click();
      URL.revokeObjectURL(url);
    });
    document.getElementById('btn-lib-import-json').addEventListener('click', () => document.getElementById('file-lib-import-json').click());
    document.getElementById('file-lib-import-json').addEventListener('change', async (e) => {
      const f = e.target.files?.[0];
      if (!f) return;
      const content = await f.text();
      const st = document.getElementById('bulk-import-status');
      const resp = await fetch('/api/library/import-json', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content })
      });
      const data = await resp.json();
      if (!resp.ok) {
        st.className = 'err';
        st.textContent = 'JSON import failed: ' + (data.error || resp.statusText);
      } else {
        st.className = 'ok';
        st.textContent = `Imported ${data.imported} custom component(s) from ${f.name}.`;
        await loadLibrary();
      }
      e.target.value = '';
    });

    // Initialize canvas with LED starter shapes
    symPrimitives = SYM_STARTERS.diode();
    renderSymbolCanvas();

    // ---- DECK & CAMPAIGN TABS ----
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
        b.onclick = () => { if (deckSelected.has(sig)) deckSelected.delete(sig); else deckSelected.add(sig); renderDeckPlot(); };
        chips.appendChild(b);
      });
      const an = document.getElementById('deck-analysis').value;
      drawPlot(document.getElementById('deck-canvas'), lastDeckResult.axis || [], series, deckSelected, an === 'ac' || an === 'noise', an === 'hb');
    }
    async function runDeckSim() {
      const an = document.getElementById('deck-analysis').value;
      const resp = await fetch('/api/deck/simulate', {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          deck: document.getElementById('deck-text').value, analysis: an,
          source: document.getElementById('deck-src').value,
          start: Number(document.getElementById('deck-start').value),
          stop: Number(document.getElementById('deck-stop').value),
          step: Number(document.getElementById('deck-step').value)
        })
      });
      const data = await resp.json();
      const st = document.getElementById('deck-status');
      if (data.status !== 'Succeeded') { st.className = 'err'; st.textContent = `Error: ${data.error_message || data.status}`; return; }
      st.className = 'ok'; st.textContent = `Succeeded (${an.toUpperCase()}) | Samples=${(data.axis || []).length} | Backend=${data.backend}`;
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

    // ---- KEYBOARD SHORTCUTS (RSpice Parity) ----
    window.addEventListener('keydown', (e) => {
      if (e.altKey && ['1','2','3','4'].includes(e.key)) {
        e.preventDefault();
        activateWorkspace(['schematic','library','deck','campaign'][Number(e.key) - 1]);
        return;
      }
      if (e.key === 'F5') { e.preventDefault(); runSchematicSim(); return; }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); document.getElementById('btn-save-schem').click(); return; }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') { e.preventDefault(); doUndo(); return; }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') { e.preventDefault(); doRedo(); return; }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'r') { e.preventDefault(); rotateSelected(); return; }
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'm') { e.preventDefault(); mirrorSelected(); return; }
      if (['INPUT','TEXTAREA','SELECT'].includes(document.activeElement?.tagName)) return;
      if (e.key === 'PageDown' || e.key === 'PageUp') {
        const sheets = allSheets();
        if (sheets.length > 1) {
          e.preventDefault();
          const idx = sheets.findIndex(s => s.sheetId === activeSheet().sheetId);
          const nextIdx = (idx + (e.key === 'PageDown' ? 1 : -1) + sheets.length) % sheets.length;
          setActiveSheet(sheets[nextIdx].sheetId);
        }
        return;
      }
      if (e.shiftKey && e.key.toLowerCase() === 'h') {
        e.preventDefault();
        setActiveSheet(currentSchem.rootSheet.sheetId);
        return;
      }
      const k = e.key.toLowerCase();
      if (k === 'escape') { closeModal(); setMode('move'); }
      else if (k === 'w') setMode('wire');
      else if (k === 'r') placePrimitive('spice.resistor', 'R', { ohms: 1000.0 });
      else if (k === 'c') placePrimitive('spice.capacitor', 'C', { farads: 1e-6, ic_volts: 0.0 });
      else if (k === 'l') placePrimitive('spice.inductor', 'L', { henries: 1e-3 });
      else if (k === 'v') placePrimitive('spice.vsrc', 'V', { dc_volts: 5.0, ac_mag: 1.0 });
      else if (k === 'i') placePrimitive('spice.isrc', 'I', { dc_amps: 1e-3 });
      else if (k === 'd') placePrimitive('spice.diode', 'D', { is_amps: 1e-14, n_emission: 1.0 });
      else if (k === 'f') fitCanvas();
      else if (k === '+' || k === '=') zoomBy(1.2);
      else if (k === '-') zoomBy(1 / 1.2);
      else if (k === 'delete' || k === 'backspace') deleteSelected();
    });

    // ---- CONTROLS WIRING ----
    document.getElementById('schem-preset').addEventListener('change', e => {
      pushUndo();
      currentSchem = structuredClone(SCHEM_PRESETS[e.target.value]);
      activeSheetId = currentSchem.rootSheet?.sheetId || 'root';
      selectedInstance = null;
      zoomState = { scale: 1.0, vb: null, baseVb: null };
      if (e.target.value === 'bsim4_inv' || e.target.value === 'bjt_amp' || e.target.value === 'opamp_buf') {
        document.getElementById('schem-analysis').value = 'op';
      } else if (e.target.value === 'multisheet_rc') {
        document.getElementById('schem-analysis').value = 'tran';
      }
      refreshAllSchematicViews();
      runSchematicSim();
    });
    document.getElementById('schem-analysis').addEventListener('change', runSchematicSim);
    document.getElementById('schem-run').addEventListener('click', runSchematicSim);
    document.getElementById('grid-style').addEventListener('change', runSchematicSim);
    document.getElementById('grid-pitch').addEventListener('change', runSchematicSim);
    document.getElementById('chk-op').addEventListener('change', runSchematicSim);
    document.getElementById('deck-preset').addEventListener('change', e => { loadDeckPreset(e.target.value); runDeckSim(); });
    document.getElementById('deck-run').addEventListener('click', runDeckSim);
    document.getElementById('camp-run').addEventListener('click', runCampaign);

    // ---- CAMEO STUDIO ----
    const CAMEO_PRESETS = {
      rc_units: `-- First-order RC low-pass filter with ISO 80000 SharpUnits
net.design("cameo_rc_filter")
net.vsrc("V1", "in", "GND", units.qty(5.0, "V"), units.qty(1.0, "V"))
net.resistor("R1", "in", "out", units.qty(1.0, "kOhm"), "0603")
net.capacitor("C1", "out", "GND", units.qty(159.155, "nF"), "0603")

let op_ok = sim.op()
sim.assert_true("op_converged", op_ok)
sim.assert_near("dc_pass", sim.v("out"), 5.0, 1e-6, 1e-4)

let ac_ok = sim.ac(units.qty(10.0, "Hz"), units.qty(100.0, "kHz"), 40)
sim.assert_true("ac_converged", ac_ok)
let fc = sim.cutoff_hz("v(out)", -3.0)
sim.assert_near("cutoff_1kHz", fc, 1000.0, 15.0, 0.02)

let tr_ok = sim.tran(units.qty(5.0, "us"), units.qty(1.0, "ms"), true)
sim.assert_true("tran_converged", tr_ok)
print($"Measured -3 dB cutoff: {fc} Hz")`,
      velosim_pcb: `-- Mixed-signal VeloSim RTL + PCB geometry + DRC check
net.design("mixed_signal_board")
net.vsrc("V1", "in", "GND", units.qty(3.3, "V"))
net.resistor("R1", "in", "out", units.qty(2.2, "kOhm"), "0603")
net.capacitor("C1", "out", "GND", units.qty(47.0, "nF"), "0603")

net.rtl("pwm_ctrl", "clk", "velosim.rtl.v1")
net.rtl_port("clk", "input", 1)
net.rtl_port("pwm_out", "output", 1)
net.bridge("b_dac", "dac", "pwm_out", "in", 0.0, 3.3, 1.65, 0.05, 50.0)
net.end_rtl()

net.pcb_board(units.qty(40.0, "mm"), units.qty(25.0, "mm"), 4)
net.pcb_rule("in", units.qty(0.2, "mm"), units.qty(0.25, "mm"))
net.pcb_place("R1", units.qty(12.0, "mm"), units.qty(12.0, "mm"), "top", 0, "0603")
net.pcb_place("C1", units.qty(20.0, "mm"), units.qty(12.0, "mm"), "top", 90, "0603")
net.pcb_trace("in", "top", units.qty(0.25, "mm"), { 6.0, 12.0, 11.2, 12.0 })
net.pcb_via("GND", units.qty(20.0, "mm"), units.qty(16.0, "mm"), units.qty(0.3, "mm"), units.qty(0.6, "mm"))

let drc = net.pcb_drc()
sim.assert_true("pcb_drc_clean", #drc == 0)
sim.op()
sim.assert_near("v_out_3v3", sim.v("out"), 3.3, 1e-6, 1e-4)`,
      corner_sweep: `-- Deterministic 4-corner parallel sweep on Spice V2 C++20 engine
net.design("cameo_divider_sweep")
net.vsrc("V1", "in", "GND", units.qty(10.0, "V"))
net.resistor("R1", "in", "out", units.qty(1.0, "kOhm"), "0603")
net.resistor("R2", "out", "GND", units.qty(1.0, "kOhm"), "0603")

let corners = { 1000.0, 3000.0, 9000.0, 19000.0 }
let v_outs = sim.sweep_op("R2", "ohms", corners, "out", 4)
sim.assert_near("corner_1k_5V", v_outs[1], 5.0, 1e-5, 1e-4)
sim.assert_near("corner_3k_7p5V", v_outs[2], 7.5, 1e-5, 1e-4)
sim.assert_near("corner_9k_9V", v_outs[3], 9.0, 1e-5, 1e-4)
sim.assert_near("corner_19k_9p5V", v_outs[4], 9.5, 1e-5, 1e-4)
print($"Corners: {v_outs[1]}, {v_outs[2]}, {v_outs[3]}, {v_outs[4]} V")`
    };

    let lastCameoResult = null;

    function renderCameoOutput() {
      const view = document.getElementById('cameo-out-view').value;
      const outEl = document.getElementById('cameo-output');
      if (!lastCameoResult) { outEl.value = ''; return; }
      if (view === 'spicenet') outEl.value = lastCameoResult.spicenet || '';
      else if (view === 'velosim') outEl.value = lastCameoResult.velosim_json || '';
      else if (view === 'circuit_json') outEl.value = lastCameoResult.circuit_json || '';
      else {
        const lines = [];
        if (lastCameoResult.stdout) lines.push('--- stdout ---\n' + lastCameoResult.stdout.trim());
        if (lastCameoResult.diagnostics?.length) {
          lines.push('--- diagnostics ---\n' + lastCameoResult.diagnostics.map(d => `[${d.severity}:${d.code}] ${d.message}`).join('\n'));
        }
        outEl.value = lines.join('\n\n') || '(no stdout or diagnostics)';
      }
    }

    async function runCameoStudio() {
      const script = document.getElementById('cameo-editor').value;
      const statusEl = document.getElementById('cameo-status');
      const loadBtn = document.getElementById('cameo-load-schem');
      statusEl.textContent = 'Running hermetic Cameo VM + native C++20 engine...';
      try {
        const resp = await fetch('/api/cameo/run', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ script, fileName: 'workbench.cameo' })
        });
        const data = await resp.json();
        lastCameoResult = data;
        const eHash = data.electrical_sha256 ? data.electrical_sha256.slice(0, 10) : '—';
        const lHash = data.layout_sha256 ? data.layout_sha256.slice(0, 10) : '—';
        document.getElementById('cameo-hashes').textContent = `elec:${eHash} | layout:${lHash}`;
        loadBtn.disabled = !data.document;

        const tbody = document.querySelector('#cameo-assert-table tbody');
        tbody.innerHTML = '';
        const asserts = data.assertions || [];
        if (asserts.length === 0) {
          tbody.innerHTML = '<tr><td colspan="3" style="color:var(--muted)">No assertions recorded.</td></tr>';
        } else {
          for (const a of asserts) {
            const tr = document.createElement('tr');
            const badge = a.passed
              ? '<span style="color:#10b981;font-weight:700">PASS</span>'
              : '<span style="color:#f43f5e;font-weight:700">FAIL</span>';
            tr.innerHTML = `<td style="font-family:var(--mono)">${a.label}</td><td>${badge}</td><td style="font-family:var(--mono);font-size:11px">${a.detail || ''}</td>`;
            tbody.appendChild(tr);
          }
        }
        renderCameoOutput();
        statusEl.textContent = data.succeeded
          ? `Succeeded: design="${data.design || 'deck'}" | ${data.runs?.length || 0} sim run(s) | ${asserts.length} assertion(s) passed.`
          : `Failed: ${(data.diagnostics || []).map(d => `[${d.code}] ${d.message}`).join(' ; ') || data.error || 'unknown error'}`;
      } catch (err) {
        statusEl.textContent = `Error: ${err.message}`;
      }
    }

    document.getElementById('cameo-preset').addEventListener('change', e => {
      document.getElementById('cameo-editor').value = CAMEO_PRESETS[e.target.value] || '';
    });
    document.getElementById('cameo-run').addEventListener('click', runCameoStudio);
    document.getElementById('cameo-out-view').addEventListener('change', renderCameoOutput);
    document.getElementById('cameo-copy-out').addEventListener('click', () => {
      navigator.clipboard?.writeText(document.getElementById('cameo-output').value || '');
    });
    document.getElementById('cameo-load-schem').addEventListener('click', () => {
      if (!lastCameoResult?.document) return;
      pushUndo();
      currentSchem = structuredClone(lastCameoResult.document);
      activeSheetId = currentSchem.rootSheet?.sheetId || 'root';
      selectedInstance = null;
      zoomState = { scale: 1.0, vb: null, baseVb: null };
      refreshAllSchematicViews();
      document.querySelector('.tab-btn[data-tab="schematic"]')?.click();
      runSchematicSim();
    });

    async function boot() {
      const info = await (await fetch('/api/info')).json();
      const badge = document.getElementById('abi-badge');
      badge.textContent = `C++20 ABI ${info.abi} | KLU=${info.klu_available} | .NET ${info.dotnet_runtime}`;
      badge.classList.add('ok');
      document.getElementById('runtime-info').textContent = `OSDI=${info.osdi_available} | Lib=${info.library_count} parts`;
      document.getElementById('cameo-editor').value = CAMEO_PRESETS.rc_units;
      await loadLibrary();
      refreshAllSchematicViews();
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
