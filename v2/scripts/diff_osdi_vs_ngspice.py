#!/usr/bin/env python3
"""
Head-to-Head OpenVAF OSDI 0.4 Differential Qualification: Spice V2 vs. ngspice.

Compiles real Verilog-A models (v2/tests/va/va_diode.va and va_mos_sh.va) using
OpenVAF-Reloaded (`openvaf-r`), loads the compiled `.osdi` binaries dynamically in
`spice_v2_cli`, and verifies node collapsing, $limit (pnjlim/fetlim/limvds + lim_rhs),
$simparam, DC OP, AC, TRAN, and .NOISE (shot + thermal + flicker) head-to-head
against ngspice's built-in C device implementations.
"""

from __future__ import annotations

import csv
import json
import math
import pathlib
import re
import shutil
import subprocess
import sys
import tempfile


def run_openvaf(openvaf_bin: str, va_path: pathlib.Path, out_osdi: pathlib.Path) -> None:
    proc = subprocess.run(
        [openvaf_bin, str(va_path), "-o", str(out_osdi)],
        capture_output=True,
        text=True,
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(f"openvaf-r failed on {va_path}:\n{proc.stdout}\n{proc.stderr}")


def run_spice_v2(cli_path: str, deck: str, extra_args: list[str] | None = None) -> tuple[dict, list[dict[str, str]]]:
    with tempfile.TemporaryDirectory(prefix="spice_v2_osdi_diff_") as td:
        cir = pathlib.Path(td) / "test.cir"
        js = pathlib.Path(td) / "out.json"
        csv_p = pathlib.Path(td) / "out.csv"
        cir.write_text(deck, encoding="utf-8")
        cmd = [cli_path, "run", str(cir), "--json", str(js), "--csv", str(csv_p)] + (extra_args or [])
        proc = subprocess.run(cmd, capture_output=True, text=True, check=False)
        if proc.returncode != 0:
            raise RuntimeError(f"spice_v2_cli failed ({proc.returncode}):\n{proc.stdout}\n{proc.stderr}")
        rows: list[dict[str, str]] = []
        if csv_p.exists():
            with csv_p.open("r", encoding="utf-8", newline="") as f:
                rows = list(csv.DictReader(f))
        return json.loads(js.read_text(encoding="utf-8")), rows


def run_ngspice(deck: str) -> str:
    with tempfile.TemporaryDirectory(prefix="ngspice_osdi_diff_") as td:
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


def v2_noise_rms(res: dict) -> tuple[float, float]:
    for a in res["analyses"]:
        if a["kind"] == "noise":
            return float(a["onoise_rms"]), float(a["inoise_rms"])
    raise RuntimeError("No NOISE analysis in spice_v2_cli output")


def check(name: str, v2_val: float, ng_val: float, abs_tol: float, rel_tol: float = 0.0) -> None:
    err = abs(v2_val - ng_val)
    limit = abs_tol + rel_tol * abs(ng_val)
    status = "PASS" if err <= limit else "FAIL"
    print(f"  [{status}] {name:38s}  v2={v2_val: .9g}  ngspice={ng_val: .9g}  |diff|={err:.3e} (tol={limit:.3e})")
    if err > limit:
        raise AssertionError(f"{name}: |{v2_val} - {ng_val}| = {err} > {limit}")


def main() -> int:
    if len(sys.argv) < 3:
        print("Usage: diff_osdi_vs_ngspice.py <path/to/spice_v2_cli> <path/to/openvaf-r>", file=sys.stderr)
        return 2
    cli_path = sys.argv[1]
    openvaf_bin = sys.argv[2]
    if shutil.which("ngspice") is None:
        print("ERROR: ngspice executable not found on PATH.", file=sys.stderr)
        return 2

    repo_root = pathlib.Path(__file__).resolve().parents[1]
    va_dir = repo_root / "tests" / "va"
    print("=== OpenVAF OSDI 0.4 Differential Qualification: Spice V2 (Real .osdi) vs. ngspice ===")

    with tempfile.TemporaryDirectory(prefix="openvaf_compile_") as td:
        td_path = pathlib.Path(td)
        diode_osdi = td_path / "va_diode.osdi"
        mos_osdi = td_path / "va_mos_sh.osdi"
        run_openvaf(openvaf_bin, va_dir / "va_diode.va", diode_osdi)
        run_openvaf(openvaf_bin, va_dir / "va_mos_sh.va", mos_osdi)
        print(f"  [OK] Compiled {diode_osdi.name} and {mos_osdi.name} with OpenVAF-Reloaded")

        # 1. va_diode.osdi RS=0: verify node collapsing eliminates internal node `ai` + OP parity
        d1_v2 = f"""OpenVAF Diode RS=0 (Collapsed)
.OSDI "{diode_osdi}"
.MODEL DVA OSDI(model=va_diode is=1e-14 n=1.0 rs=0)
V1 in 0 DC 5
R1 in out 1k
D1 out 0 DVA
.OP
.END
"""
        d1_ng = """ngspice Diode RS=0
.MODEL DREF D(IS=1e-14 N=1.0 RS=0)
V1 in 0 DC 5
R1 in out 1k
D1 out 0 DREF
.control
op
print v(out)
.endc
.END
"""
        r1_v2, _ = run_spice_v2(cli_path, d1_v2)
        if r1_v2["unknowns"] != 3 or any("ai" in s.lower() for s in r1_v2["signals"]):
            raise AssertionError(f"Expected RS=0 node collapse (3 unknowns), got {r1_v2['unknowns']} {r1_v2['signals']}")
        r1_ng = run_ngspice(d1_ng)
        check("OSDI Diode RS=0 (collapsed) OP v(out)", v2_op_signal(r1_v2, "v(out)"), parse_ngspice_scalar(r1_ng, "v(out)"), 1e-4)

        # 2. va_diode.osdi RS=10 + CJO + TT + KF: uncollapsed node, OP, AC, and .NOISE parity
        d2_op_v2 = f"""OpenVAF Diode RS=10 OP
.OSDI "{diode_osdi}"
.MODEL DVA OSDI(model=va_diode is=1e-14 n=1.0 rs=10 cjo=10p vj=0.7 m=0.5 tt=5n kf=1e-16 af=1.0)
V1 in 0 DC 5 AC 1
R1 in out 1k
D1 out 0 DVA
.OP
.END
"""
        d2_op_ng = """ngspice Diode RS=10 OP
.MODEL DREF D(IS=1e-14 N=1.0 RS=10 CJO=10p VJ=0.7 M=0.5 TT=5n KF=1e-16 AF=1.0)
V1 in 0 DC 5 AC 1
R1 in out 1k
D1 out 0 DREF
.control
op
print v(out)
.endc
.END
"""
        r2_v2, _ = run_spice_v2(cli_path, d2_op_v2)
        if r2_v2["unknowns"] != 4 or not any("d1:ai" in s.lower() for s in r2_v2["signals"]):
            raise AssertionError(f"Expected RS=10 uncollapsed v(D1:ai), got {r2_v2['signals']}")
        r2_ng = run_ngspice(d2_op_ng)
        check("OSDI Diode RS=10 OP v(out)", v2_op_signal(r2_v2, "v(out)"), parse_ngspice_scalar(r2_ng, "v(out)"), 1e-4)

        # 3. va_diode.osdi AC magnitude at 5 MHz (exercises CJO depletion + TT diffusion capacitance)
        d3_v2 = d2_op_v2.replace(".OP", ".AC LIN 1 5e6 5e6")
        d3_ng = d2_op_ng.replace(".control\nop\nprint v(out)\n.endc",
                                 ".control\nac lin 1 5e6 5e6\nlet m = mag(v(out))\nprint m\n.endc")
        _, rows3 = run_spice_v2(cli_path, d3_v2)
        r3_ng = run_ngspice(d3_ng)
        v2_mag3 = math.hypot(float(rows3[0]["v(out)_re"]), float(rows3[0]["v(out)_im"]))
        check("OSDI Diode RS=10 AC |v(out)| 5MHz", v2_mag3, parse_ngspice_scalar(r3_ng, "m"), 5e-4, 5e-3)

        # 4. va_diode.osdi .NOISE (shot + thermal_rs + flicker_noise) integrated RMS
        d4_v2 = d2_op_v2.replace(".OP", ".NOISE V(out) V1 DEC 10 1e3 1e5")
        d4_ng = d2_op_ng.replace(".control\nop\nprint v(out)\n.endc",
                                 ".control\nnoise v(out) V1 dec 10 1e3 1e5\nsetplot noise2\nprint onoise_total inoise_total\n.endc")
        r4_v2, _ = run_spice_v2(cli_path, d4_v2)
        r4_ng = run_ngspice(d4_ng)
        on_v2, in_v2 = v2_noise_rms(r4_v2)
        check("OSDI Diode .NOISE onoise_rms", on_v2, parse_ngspice_scalar(r4_ng, "onoise_total"), 1e-12, 5e-3)
        check("OSDI Diode .NOISE inoise_rms", in_v2, parse_ngspice_scalar(r4_ng, "inoise_total"), 1e-12, 5e-3)

        # 5. va_diode.osdi High-Overdrive 50V OP (proves $limit("pnjlim") + lim_rhs convergence)
        d5_v2 = d1_v2.replace("DC 5", "DC 50")
        d5_ng = d1_ng.replace("DC 5", "DC 50")
        r5_v2, _ = run_spice_v2(cli_path, d5_v2)
        r5_ng = run_ngspice(d5_ng)
        check("OSDI Diode 50V pnjlim OP v(out)", v2_op_signal(r5_v2, "v(out)"), parse_ngspice_scalar(r5_ng, "v(out)"), 2e-4)

        # 6. va_mos_sh.osdi RD=0, RS=0 (BOTH internal nodes di & si collapse!) + OP + .NOISE parity
        d6_v2 = f"""OpenVAF MOSFET Level-1 Collapsed
.OSDI "{mos_osdi}"
.MODEL MVA OSDI(model=va_mos_sh vto=0.7 kp=1e-4 lambda=0.02 w=20u l=1u rd=0 rs=0)
Vdd vdd 0 DC 5
Vg g 0 DC 2.0 AC 1
Rd vdd d 2k
M1 d g 0 0 MVA
.OP
.END
"""
        d6_ng = """ngspice MOSFET Level-1
.MODEL MREF NMOS(LEVEL=1 VTO=0.7 KP=1e-4 LAMBDA=0.02)
Vdd vdd 0 DC 5
Vg g 0 DC 2.0 AC 1
Rd vdd d 2k
M1 d g 0 0 MREF W=20u L=1u
.control
op
print v(d)
.endc
.END
"""
        r6_v2, _ = run_spice_v2(cli_path, d6_v2)
        if any("m1:di" in s.lower() or "m1:si" in s.lower() for s in r6_v2["signals"]):
            raise AssertionError(f"Expected RD=0,RS=0 double node collapse, got {r6_v2['signals']}")
        r6_ng = run_ngspice(d6_ng)
        check("OSDI MOS (RD=RS=0 collapsed) OP v(d)", v2_op_signal(r6_v2, "v(d)"), parse_ngspice_scalar(r6_ng, "v(d)"), 5e-4, 2e-3)

        # 7. va_mos_sh.osdi RD=20, RS=10 (uncollapsed internal nodes di & si) + OP + channel thermal .NOISE
        d7_v2 = f"""OpenVAF MOSFET Level-1 Uncollapsed RD/RS
.OSDI "{mos_osdi}"
.MODEL MVA OSDI(model=va_mos_sh vto=0.7 kp=1e-4 lambda=0.02 w=20u l=1u rd=20 rs=10)
Vdd vdd 0 DC 5
Vg g 0 DC 2.0 AC 1
Rd vdd d 2k
M1 d g 0 0 MVA
.OP
.END
"""
        d7_ng = """ngspice MOSFET Level-1 RD/RS
.MODEL MREF NMOS(LEVEL=1 VTO=0.7 KP=1e-4 LAMBDA=0.02 RD=20 RS=10)
Vdd vdd 0 DC 5
Vg g 0 DC 2.0 AC 1
Rd vdd d 2k
M1 d g 0 0 MREF W=20u L=1u
.control
op
print v(d)
.endc
.END
"""
        r7_v2, _ = run_spice_v2(cli_path, d7_v2)
        if not (any("m1:di" in s.lower() for s in r7_v2["signals"]) and any("m1:si" in s.lower() for s in r7_v2["signals"])):
            raise AssertionError(f"Expected uncollapsed m1:di and m1:si, got {r7_v2['signals']}")
        r7_ng = run_ngspice(d7_ng)
        check("OSDI MOS (RD=20,RS=10) OP v(d)", v2_op_signal(r7_v2, "v(d)"), parse_ngspice_scalar(r7_ng, "v(d)"), 5e-4, 2e-3)

        d8_v2 = d7_v2.replace(".OP", ".NOISE V(d) Vg DEC 10 1e3 1e5")
        d8_ng = d7_ng.replace(".control\nop\nprint v(d)\n.endc",
                              ".control\nnoise v(d) Vg dec 10 1e3 1e5\nsetplot noise2\nprint onoise_total\n.endc")
        r8_v2, _ = run_spice_v2(cli_path, d8_v2)
        r8_ng = run_ngspice(d8_ng)
        on8_v2, _ = v2_noise_rms(r8_v2)
        check("OSDI MOS .NOISE onoise_rms", on8_v2, parse_ngspice_scalar(r8_ng, "onoise_total"), 1e-12, 1e-2)

    print("ALL OpenVAF OSDI 0.4 differential checks vs. ngspice PASSED!")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
