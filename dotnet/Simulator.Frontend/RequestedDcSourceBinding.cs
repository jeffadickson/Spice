using System.Runtime.CompilerServices;
using Simulator.Client;
using Simulator.Syntax;
[assembly:InternalsVisibleTo("Simulator.Frontend.Tests")]
namespace Simulator.Frontend;
// A DC target is an original root independent source, never an implementation
// source synthesized while lowering behavioral or external model expressions.
internal static class RequestedDcSourceBinding
{
 internal static (Element? Source,string? Error) Resolve(DeclarationCatalog catalog,BridgeResult prepared,string name){
  var declarations=catalog.Primitives.Where(p=>p.ScopeId==0&&p.Family is "V" or "I"&&p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
  if(declarations.Length!=1)return (null,declarations.Length==0?"unsupported_dc_source":"ambiguous_dc_source_declaration");
  var declaration=declarations[0];
  var origins=prepared.Origins.Where(o=>o.InstancePath.IsEmpty&&o.LocalName.Equals(declaration.Name,StringComparison.OrdinalIgnoreCase)&&o.Range==declaration.Range).ToArray();
  if(origins.Length!=1)return (null,"ambiguous_dc_source_origin");
  var elements=prepared.Request?.Circuit.Elements.Where(e=>e.Id==origins[0].WireId).ToArray()??[];
  if(elements.Length!=1||!(declaration.Family=="V"&&elements[0] is Voltage||declaration.Family=="I"&&elements[0] is Current))return (null,"dc_source_wire_mapping_gap");
  return (elements[0],null);
 }
}
