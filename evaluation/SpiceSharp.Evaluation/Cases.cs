internal static class Cases
{
    // Original small fixtures. Expected values follow independent circuit laws;
    // they are not golden outputs obtained from the library being assessed.
    internal static IEnumerable<OwnedCase> All()
    {
        yield return new("divider","owned\nV1 in 0 5\nR1 in out 1k\nR2 out 0 2k\n.op\n.save V(out) I(V1)\n.end\n",[[new("V(out)",10d/3),new("I(V1)",-5d/3000)]]);
        yield return new("param-subckt","owned\n.param rv=2k\nV1 in 0 1\nX1 in out div r={rv}\n.subckt div a b params: r=1k\nR1 a b {r}\nR2 b 0 {r}\n.ends div\n.op\n.save V(out) I(V1)\n.end\n",[[new("V(out)",.5),new("I(V1)",-.00025)]]);
        var w=2*Math.PI*100;var h=System.Numerics.Complex.One/new System.Numerics.Complex(1,w*.001);
        yield return new("rc-ac","owned\nV1 in 0 DC 0 AC 1\nR1 in out 1k\nC1 out 0 1u\n.ac lin 1 100 100\n.save VR(out) VI(out)\n.end\n",[[new("VR(out)",h.Real),new("VI(out)",h.Imaginary)]]);
        var current=System.Numerics.Complex.One/new System.Numerics.Complex(1000,w);
        yield return new("rl-ac","owned\nV1 in 0 DC 0 AC 1\nR1 in out 1k\nL1 out 0 1\n.ac lin 1 100 100\n.save IR(L1) II(L1)\n.end\n",[[new("IR(L1)",current.Real),new("II(L1)",current.Imaginary)]]);
        var final=1-1000*(1-Math.Exp(-.001))*Math.Exp(-.989);
        yield return new("rc-transient","owned\nV1 in 0 PULSE(0 1 10u 1u 1u 1 2)\nR1 in out 1k\nC1 out 0 1u\n.tran 2u 1m 0 2u\n.save V(out) I(V1)\n.end\n",[[new("V(out)",final,5e-5),new("I(V1)",-(1-final)/1000,1e-7),new("$time",.001,1e-12)]]);
        yield return new("multiple-analyses","owned\nV1 out 0 DC 1 AC 2\nR1 out 0 1k\n.op\n.ac lin 1 100 100\n.tran 1m 2m\n.save V(out)\n.end\n",[[new("V(out)",1)],[new("V(out)",2)],[new("V(out)",1),new("$time",.002,1e-12)]]);
        yield return new("bjt-npn","owned\nV1 vcc 0 5\nIB 0 base 10u\nQ1 collector base 0 qm\nR1 vcc collector 2k\n.model qm NPN(IS=1e-15 BF=100 BR=1)\n.temp 27\n.op\n.save V(collector) I(V1)\n.end\n",[[new("V(collector)",3,1e-5),new("I(V1)",-.001,1e-8)]]);
        yield return new("mos-level1","owned\nVD drain 0 5\nVG gate 0 2\nM1 drain gate 0 0 mm W=10u L=1u\n.model mm NMOS(LEVEL=1 VTO=1 KP=100u LAMBDA=0 GAMMA=0 PHI=.6)\n.temp 27\n.op\n.save I(VD) V(drain)\n.end\n",[[new("I(VD)",-.0005,1e-8),new("V(drain)",5)]]);
        var vt=1.380649e-23*300.15/1.602176634e-19;
        yield return new("diode","owned\nV1 out 0 .6\nD1 out 0 dm\n.model dm D(IS=1e-12 N=1 TNOM=27)\n.temp 27\n.op\n.save I(V1)\n.end\n",[[new("I(V1)",-1e-12*(Math.Exp(.6/vt)-1),1e-7,1e-4)]]);
        yield return new("malformed-expression","owned\nV1 out 0 1\nR1 out 0 {\n.op\n.end\n",[],"input_rejected");
        yield return new("malformed-subckt","owned\n.subckt bad a b\nR1 a b 1k\n.end\n",[],"input_rejected");
    }
}
