namespace SlyTrial;

/// <summary>A fixed independent acceptance expectation; challenge cases are not scored as implemented coverage.</summary>
public sealed record Fixture(string Id,string Family,string Input,bool Expected,string Scope="shared");
/// <summary>Small repeatable examples drawn from the existing parser-family tests and ngspice syntax forms.</summary>
public static class Cases
{
    /// <summary>Declared trial fixtures, including deliberately unimplemented dialect extensions.</summary>
    public static IReadOnlyList<Fixture> All { get; } = [
        new("scalar","port","A1 in out gain",true),
        new("vector","port","a1 [~in out] gate",true),
        new("differential","port","a1 %vd(in 0) out diff",true),
        new("custom-type","port","a1 %my_udn(input) out custom",true),
        new("continuation","port","a1 [in\n+ out] gate",true),
        new("hyphen-node","port","a1 in-node out/name gain",true),
        new("missing-vector-close","port","a1 [in out model",false),
        new("missing-differential-node","port","a1 %vd(in) out diff",false),
        new("extra-differential-node","port","a1 %vd(in 0 extra) out diff",false),
        new("empty-vector","port","a1 [] out gate",false),
        new("missing-port","port","a1 model",false),
        new("precedence","expression","1+2*3",true),
        new("unary","expression","-(1+2)/3",true),
        new("probe","expression","v(in)-0.3",true),
        new("nested-call","expression","exp(v(in)/vt)-1",true),
        new("multiple-arguments","expression","max(v(in),1)",true),
        new("missing-operand","expression","1+",false),
        new("missing-group-close","expression","(1+2",false),
        new("extra-close","expression","v(in))",false),
        new("trailing-garbage","expression","1 garbage",false),
        new("inert-control","control",".control\nshell echo forbidden\n.endc",true),
        new("quoted-end-marker","control",".control\necho \".endc\"\n.endc",true),
        new("missing-end-marker","control",".control\nquit",false),
        new("typed-vector","port","a1 %i[1 2] out model",true,"unimplemented-production"),
        new("wrapped-vector","port","a1 %vd([out1 0]) filesrc",true,"unimplemented-production"),
        new("numeric-model-reference","port","a1 in out 1",true,"unimplemented-production"),
        new("ternary","expression","v(1)>0 ? 1 : 0",true,"unimplemented-production"),
        new("boolean","expression","a & b",true,"unimplemented-production"),
        new("end-marker-prefix","control",".control\n.endc_extra\n.endc",true,"boundary-challenge")
    ];
}
