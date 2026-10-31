using System.Text.Json.Serialization;
namespace Simulator.Benchmarks;

// Mutable outcomes are checkpointed after each deck; identities and observed
// hashes are immutable. Pending/unsupported outcomes never imply a pass.
internal sealed record CorpusRow(string Path,string? Sha256,string Scope,string[] FeatureHints)
{
    public string Status {get;set;}="pending";
    public string Diagnostic {get;set;}="";
    public string? Analysis {get;set;}
    public string? ExecutedDeckSha256 {get;set;}
    public string? RequestSha256 {get;set;}
    public string ObservationPolicy {get;set;}="";
    public List<CorpusBackend> Backends {get;set;}=[];
}
internal sealed record CorpusBackend(string Backend,string Status,string Detail,List<CorpusSignal> Signals);
internal sealed record CorpusObservable(string Name,string Quantity,string Unit);
// Discrepancies and absolute bounds use the signal's explicit unit, never a
// voltage tolerance silently applied to amperes.
internal sealed record CorpusSignal(string Observable,string Unit,double AbsoluteTolerance,int Points,int FailedPoints,double MaximumAbsoluteDifference,
    double WorstCoordinate,double ExpectedReal,double ExpectedImag,double ActualReal,double ActualImag);
internal sealed record CorpusHistory(string RunId,string Inventory,int Total,int Passed,string FinishedUtc);
internal sealed class CorpusReport
{
    public string SchemaVersion {get;set;}="2";
    public string RunId {get;set;}="";
    public string State {get;set;}="running";
    public string SourceCommit {get;set;}="";
    public string CorpusCommit {get;set;}="";
    public bool SourceDirty {get;set;}
    public bool? CorpusDirty {get;set;}
    public string? Failure {get;set;}
    public string CorpusRoot {get;set;}="";
    public string Inventory {get;set;}="";
    public string InventoryMode {get;set;}="tracked";
    public string ComparisonPolicy {get;set;}="node-voltage+independent-voltage-source-current-v3; max200; ac-axis-abs1e-12-rel5e-6; transient-endpoint-4ulp; no-extrapolation";
    public Dictionary<string,string> UnclassifiedAssets {get;set;}=[];
    public string NgspiceVersion {get;set;}="";
    public string HelperMode {get;set;}="";
    public string StartedUtc {get;set;}="";
    public string? FinishedUtc {get;set;}
    public string RawDirectory {get;set;}="";
    public double AbsoluteToleranceV {get;set;}=1e-5;
    public double AbsoluteToleranceA {get;set;}=1e-9;
    public double RelativeTolerance {get;set;}=1e-4;
    public Dictionary<string,string> Hashes {get;set;}=[];
    public List<CorpusRow> Rows {get;set;}=[];
    public Dictionary<string,int> Counts {get;set;}=[];
    public string IssueState {get;set;}="not_requested";
    public string? IssueUrl {get;set;}
    public string? IssueError {get;set;}
    public string[] Limitations {get;set;}=[
        "Passing means finite named node-voltage and independent voltage-source current comparisons on LU and faer, not full SPICE conformance.",
        "At most 200 representative coordinates per signal; transient interpolation and AC print-axis rounding are explicit.",
        "Control scripts and external-resource directives are not executed; lexical hints are not isolated root causes.",
        "Recognized deck extensions plus bounded extensionless .end decks; other assets are not silently counted as circuits.",
        "Other device/controlled-source/inductor currents, global waveform bounds, vendor/GPU and production qualification remain unverified.",
        "Unfinished, rejected and unsupported cases remain remaining work; no historical five-case benchmark is a whole-corpus baseline."
    ];
}
[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.SnakeCaseLower,WriteIndented=true)]
[JsonSerializable(typeof(CorpusReport))]
[JsonSerializable(typeof(CorpusHistory[]))]
internal sealed partial class CorpusJson:JsonSerializerContext;
