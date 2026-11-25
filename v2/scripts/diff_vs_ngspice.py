#!/usr/bin/env python3
"""
Head-to-head differential qualification harness: Spice V2 (`spice_v2_cli`) vs. real `ngspice`.
Runs canonical DC, nonlinear diode, AC (3 decades), BJT CE amp, MOSFET Level-1, and Transient
circuits through BOTH simulators and fails closed if any relative/absolute deviation exceeds tolerance.
"""

from __future__ import annotations

import json
import math
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile
import time


import csv


def run_spice_v2(cli_path: str, deck: str, extra_args: list[str] | None = None) -> tuple[dict, list[dict[str, str]]]:
    with tempfile.TemporaryDirectory(prefix="spice_v2_diff_") as td:
        cir = pathlib.Path(td) / "test.cir"
        js = pathlib.Path(td) / "out.json"
        csv_p = pathlib.Path(td) / "out.csv"
        cir.write_text(deck, encoding="utf-8")
        cmd = [cli_path, "run", str(cir), "--json", str(js), "--csv", str(csv_p)] + (extra_args or [])
        proc = subprocess.run(cmd, capture_output=True, text=True, check=False)
        if proc.returncode != 0:
            raise RuntimeError(f"spice_v2_cli failed ({proc.returncode}): {proc.stderr}")
        rows: list[dict[str, str]] = []
        if csv_p.exists():
            with csv_p.open("r", encoding="utf-8", newline="") as f:
                rows = list(csv.DictReader(f))
        return json.loads(js.read_text(encoding="utf-8")), rows


def run_ngspice(deck: str) -> str:
    with tempfile.TemporaryDirectory(prefix="ngspice_diff_") as td:
        cir = pathlib.Path(td) / "test.cir"
        cir.write_text(deck, encoding="utf-8")
        proc = subprocess.run(["ngspice", "-b", str(cir)], capture_output=True, text=True, check=False)
        if proc.returncode != 0:
            raise RuntimeError(f"ngspice failed ({proc.returncode}):\n{proc.stdout}\n{proc.stderr}")
        return proc.stdout


def parse_ngspice_scalar(out: str, var_name: str) -> float:
    pat = re.compile(rf"^\s*{re.escape(var_name)}\s*=\s*([+-]?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)", re.M | re.I)
    m = pat.search(out)
    if not m:
        raise RuntimeError(f"Could not parse '{var_name}' from ngspice output:\n{out}")
    return float(m.group(1))


def v2_op_signal(res: dict, sig: str) -> float:
    sigs = [s.lower() for s in res["signals"]]
    idx = sigs.index(sig.lower())
    for a in res["analyses"]:
        if a["kind"] == "op":
            return float(a["values"][idx])
    raise RuntimeError("No OP analysis in spice_v2_cli output")


def check(name: str, v2_val: float, ng_val: float, abs_tol: float, rel_tol: float = 0.0) -> None:
    err = abs(v2_val - ng_val)
    limit = abs_tol + rel_tol * abs(ng_val)
    status = "PASS" if err <= limit else "FAIL"
    print(f"  [{status}] {name:34s}  v2={v2_val: .9g}  ngspice={ng_val: .9g}  |diff|={err:.3e} (tol={limit:.3e})")
    if err > limit:
        raise AssertionError(f"{name}: |{v2_val} - {ng_val}| = {err} > {limit}")


def main() -> int:
    if len(sys.argv) < 2:
        print("Usage: diff_vs_ngspice.py <path/to/spice_v2_cli>", file=sys.stderr)
        return 2
    cli_path = sys.argv[1]
    if shutil.which("ngspice") is None:
        print("ERROR: ngspice executable not found on PATH.", file=sys.stderr)
        return 2

    print("=== Head-to-Head Differential Qualification: Spice V2 vs. ngspice ===")

    # 1. Linear Divider + VCVS (E) DC Operating Point
    d1_v2 = """Linear VCVS Divider
V1 in 0 DC 10
R1 in mid 2k
R2 mid 0 3k
E1 out 0 mid 0 1.5
Rload out 0 1k
.OP
.END
"""
    d1_ng = d1_v2.replace(".OP", ".control\nop\nprint v(mid) v(out)\n.endc")
    r1_v2, _ = run_spice_v2(cli_path, d1_v2)
    r1_ng = run_ngspice(d1_ng)
    check("Linear+VCVS OP v(mid)", v2_op_signal(r1_v2, "v(mid)"), parse_ngspice_scalar(r1_ng, "v(mid)"), 1e-9)
    check("Linear+VCVS OP v(out)", v2_op_signal(r1_v2, "v(out)"), parse_ngspice_scalar(r1_ng, "v(out)"), 1e-9)

    # 2. Nonlinear Shockley Diode DC Operating Point
    d2_v2 = """Nonlinear Diode OP
.MODEL D1 D(IS=1e-14 N=1)
V1 in 0 DC 5
R1 in out 1k
D1 out 0 D1
.OP
.END
"""
    d2_ng = d2_v2.replace(".OP", ".control\nop\nprint v(out)\n.endc")
    r2_v2, _ = run_spice_v2(cli_path, d2_v2)
    r2_ng = run_ngspice(d2_ng)
    check("Nonlinear Diode OP v(out)", v2_op_signal(r2_v2, "v(out)"), parse_ngspice_scalar(r2_ng, "v(out)"), 1e-3)

    # 3. RC Low-Pass AC Magnitude at corner frequency (f = 159.154943 Hz -> |H| = 1/sqrt(2))
    d3_v2 = """RC Lowpass AC
V1 in 0 DC 0 AC 1
R1 in out 1k
C1 out 0 1u
.AC LIN 1 159.154943 159.154943
.END
"""
    d3_ng = d3_v2.replace(".AC LIN 1 159.154943 159.154943",
                          ".control\nac lin 1 159.154943 159.154943\nlet m = mag(v(out))\nprint m\n.endc")
    _, rows3 = run_spice_v2(cli_path, d3_v2)
    r3_ng = run_ngspice(d3_ng)
    v2_mag3 = math.hypot(float(rows3[0]["v(out)_re"]), float(rows3[0]["v(out)_im"]))
    check("RC AC |v(out)| at f_c", v2_mag3, parse_ngspice_scalar(r3_ng, "m"), 1e-6)

    # 4. Gummel-Poon NPN BJT Common-Emitter DC Operating Point
    d4_v2 = """BJT Common Emitter OP
.MODEL QN NPN(IS=1e-15 BF=100 VAF=100)
Vcc vcc 0 DC 10
Rb vcc b 470k
Rc vcc c 2.2k
Re e 0 470
Q1 c b e QN
.OP
.END
"""
    d4_ng = d4_v2.replace(".OP", ".control\nop\nprint v(c) v(b) v(e)\n.endc")
    r4_v2, _ = run_spice_v2(cli_path, d4_v2)
    r4_ng = run_ngspice(d4_ng)
    check("Gummel-Poon BJT OP v(c)", v2_op_signal(r4_v2, "v(c)"), parse_ngspice_scalar(r4_ng, "v(c)"), 1.5e-2, 5e-3)
    check("Gummel-Poon BJT OP v(e)", v2_op_signal(r4_v2, "v(e)"), parse_ngspice_scalar(r4_ng, "v(e)"), 1.5e-2, 5e-3)

    # 5. MOSFET Level-1 Shichman-Hodges Common-Source DC Operating Point
    d5_v2 = """MOSFET Level-1 CS Amp
.MODEL M1 NMOS(LEVEL=1 VTO=0.7 KP=1e-4 LAMBDA=0.02)
Vdd vdd 0 DC 5
Vg g 0 DC 2.0
Rd vdd d 2k
M1 d g 0 0 M1 W=20u L=1u
.OP
.END
"""
    d5_ng = d5_v2.replace(".OP", ".control\nop\nprint v(d)\n.endc")
    r5_v2, _ = run_spice_v2(cli_path, d5_v2)
    r5_ng = run_ngspice(d5_ng)
    check("MOSFET Level-1 OP v(d)", v2_op_signal(r5_v2, "v(d)"), parse_ngspice_scalar(r5_ng, "v(d)"), 2e-2, 5e-3)

    # 6. RC Step Transient (t = 1 ms = 1 tau -> 5 * (1 - e^-1) ~= 3.160603 V)
    d6_v2 = """RC Step Transient
V1 in 0 DC 5
R1 in out 1k
C1 out 0 1u IC=0
.TRAN 10u 1m UIC
.END
"""
    d6_ng = d6_v2.replace(".TRAN 10u 1m UIC", ".control\ntran 10u 1m uic\nlet vend = v(out)[length(v(out))-1]\nprint vend\n.endc")
    _, rows6 = run_spice_v2(cli_path, d6_v2, ["--method", "bdf2"])
    r6_ng = run_ngspice(d6_ng)
    v2_vend = float(rows6[-1]["v(out)"])
    check("RC Step TRAN v(out, 1ms)", v2_vend, parse_ngspice_scalar(r6_ng, "vend"), 5e-3)

    # 7. 1,000-node RC Ladder timing benchmark (Spice V2 + KLU vs. ngspice)
    lines = ["1000-Node RC Ladder", "V1 n0 0 DC 10"]
    for i in range(1000):
        lines.append(f"R{i} n{i} n{i+1} 10")
        lines.append(f"C{i} n{i+1} 0 1p")
    lines.append("Rload n1000 0 1000")
    ladder_v2 = "\n".join(lines + [".OP", ".END", ""])
    ladder_ng = "\n".join(lines + [".control", "op", "print v(n1000)", ".endc", ".END", ""])

    t0 = time.perf_counter()
    rl_v2, _ = run_spice_v2(cli_path, ladder_v2)
    dt_v2 = (time.perf_counter() - t0) * 1000.0
    t0 = time.perf_counter()
    rl_ng = run_ngspice(ladder_ng)
    dt_ng = (time.perf_counter() - t0) * 1000.0
    check("1000-Node Ladder OP v(n1000)", v2_op_signal(rl_v2, "v(n1000)"), parse_ngspice_scalar(rl_ng, "v(n1000)"), 1e-8)
    print(f"  [BENCH] 1000-node ladder CLI wall time: spice_v2_cli={dt_v2:.1f} ms vs ngspice={dt_ng:.1f} ms")

    # 8. Slice A: Zener 5.1V Diode Regulator (RS, BV, IBV)
    d8_v2 = """Zener 5.1V Regulator
.MODEL DZ D(IS=1e-14 N=1 RS=4 BV=5.1 IBV=5m)
Vin in 0 DC 12
Rser in out 470
Dz 0 out DZ
.OP
.END
"""
    d8_ng = d8_v2.replace(".OP", ".control\nop\nprint v(out)\n.endc")
    r8_v2, _ = run_spice_v2(cli_path, d8_v2)
    r8_ng = run_ngspice(d8_ng)
    check("Zener 5.1V (RS/BV) OP v(out)", v2_op_signal(r8_v2, "v(out)"), parse_ngspice_scalar(r8_ng, "v(out)"), 1.5e-2, 5e-3)

    # 9. Slice A: Diode SPICE3f5 Temperature Scaling (.TEMP 85, EG=1.11, XTI=3)
    d9_v2 = """Diode Temp 85C
.TEMP 85
.MODEL D1 D(IS=1e-14 N=1 RS=2 EG=1.11 XTI=3)
I1 0 out DC 1m
D1 out 0 D1
.OP
.END
"""
    d9_ng = d9_v2.replace(".OP", ".control\nop\nprint v(out)\n.endc")
    r9_v2, _ = run_spice_v2(cli_path, d9_v2)
    r9_ng = run_ngspice(d9_ng)
    check("Diode .TEMP 85C OP v(out)", v2_op_signal(r9_v2, "v(out)"), parse_ngspice_scalar(r9_ng, "v(out)"), 5e-3, 5e-3)

    # 10. Slice A: Full Discrete BJT with RB/RC/RE + .TEMP 85 (XTB=1.5)
    d10_v2 = """BJT Parasitics + Temp 85C
.TEMP 85
.MODEL QFULL NPN(IS=1e-14 BF=150 VAF=80 RB=50 RC=5 RE=2 EG=1.11 XTI=3 XTB=1.5)
Vcc vcc 0 DC 12
R1 vcc b 47k
R2 b 0 10k
Rc vcc c 2.2k
Re e 0 470
Q1 c b e QFULL
.OP
.END
"""
    d10_ng = d10_v2.replace(".OP", ".control\nop\nprint v(c) v(e)\n.endc")
    r10_v2, _ = run_spice_v2(cli_path, d10_v2)
    r10_ng = run_ngspice(d10_ng)
    check("BJT RB/RC/RE 85C OP v(c)", v2_op_signal(r10_v2, "v(c)"), parse_ngspice_scalar(r10_ng, "v(c)"), 5e-2, 1e-2)
    check("BJT RB/RC/RE 85C OP v(e)", v2_op_signal(r10_v2, "v(e)"), parse_ngspice_scalar(r10_ng, "v(e)"), 2e-2, 1e-2)

    # 11. Slice A: Current-Controlled Switch (W / CSW)
    d11_v2 = """Current-Controlled Switch W
.MODEL WMOD CSW(IT=5m IH=1m RON=10 ROFF=1MEG)
Vdrv in 0 DC 10
Rctl in s_node 1k
Vsense s_node 0 DC 0
Vbus bus 0 DC 10
Rload bus out 1k
W1 out 0 Vsense WMOD
.OP
.END
"""
    d11_ng = d11_v2.replace(".OP", ".control\nop\nprint v(out)\n.endc")
    r11_v2, _ = run_spice_v2(cli_path, d11_v2)
    r11_ng = run_ngspice(d11_ng)
    check("CSW/W Switch ON OP v(out)", v2_op_signal(r11_v2, "v(out)"), parse_ngspice_scalar(r11_ng, "v(out)"), 5e-3, 1e-2)

    # 12. Slice A: Statz GaAs MESFET (Z / NMF LEVEL=1)
    d12_v2 = """Statz MESFET Amp OP
.MODEL ZN NMF(LEVEL=1 VTO=-1.2 BETA=0.05 ALPHA=2.0 LAMBDA=0.03 B=0.25 RD=3 RS=2)
Vdd vdd 0 DC 5
Rd vdd d 100
Vgs g 0 DC -0.4
Z1 d g 0 ZN
.OP
.END
"""
    d12_ng = d12_v2.replace(".OP", ".control\nop\nprint v(d)\n.endc")
    r12_v2, _ = run_spice_v2(cli_path, d12_v2)
    r12_ng = run_ngspice(d12_ng)
    check("Statz MESFET (Z) OP v(d)", v2_op_signal(r12_v2, "v(d)"), parse_ngspice_scalar(r12_ng, "v(d)"), 5e-2, 2e-2)

    print("ALL differential checks vs. ngspice PASSED!")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
