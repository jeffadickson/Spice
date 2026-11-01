#:project ../dotnet/Simulator.Syntax/Simulator.Syntax.csproj
#:property PublishAot=false
using Simulator.Syntax;

// This example inspects declarations. Parameter values remain source syntax;
// no subcircuit instance is expanded and no numerical model is executed.
var parsed=DeckParser.Parse("""
Declaration catalog example
.model rectifier d (is=1p)
.subckt cell p n gain={base*2}
.param gain=3
.ends cell
.end
""",collectDeclarations:true);
if(parsed.Status!="accepted_structure"||parsed.Declarations is not { } catalog)
    throw new InvalidDataException("Example syntax did not produce a declaration catalog");
var cell=catalog.Lookup(DeclarationKind.Subcircuit,"CELL",0).Candidates.Single();
var scope=cell.BodyScopeId!.Value;
var model=catalog.Lookup(DeclarationKind.Model,"RECTIFIER",scope);
Console.WriteLine($"Catalog: {catalog.Status}; scopes: {catalog.Scopes.Length}");
Console.WriteLine($"Ancestor model: {model.Status}; family: {model.Candidates.Single().ModelFamily}");
foreach(var assignment in catalog.LookupParameter("GAIN",scope).Candidates)
    Console.WriteLine($"Ordered parameter syntax: {assignment.Syntax.Value.Text}, physical line {assignment.Syntax.Range.Line}");
var summary=DeclarationSummary.From(catalog);
Console.WriteLine($"Self-lookups: {summary.SelfLookups}; inconsistencies: {summary.SelfLookupFailures}");
