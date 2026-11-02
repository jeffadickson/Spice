#:property PublishAot=false
using System.Text.Json;
// Synthetic protocol faults only. This executable is never numerical evidence
// and is passed explicitly by the corpus harness's negative integration tests.
Console.InputEncoding=new System.Text.UTF8Encoding(false,true);
Console.OutputEncoding=new System.Text.UTF8Encoding(false);
using var document=JsonDocument.Parse(await Console.In.ReadToEndAsync());
var root=document.RootElement;
var mode=Environment.GetEnvironmentVariable("SPICE_FRONTEND_TEST_FAULT")??throw new InvalidOperationException("Synthetic test fault must be selected explicitly");
if(mode is not ("missing" or "wrong-unit" or "non-finite" or "mismatch" or "case-id" or "provider"))throw new ArgumentException("Unknown test fault");
var backend=root.GetProperty("backend").GetString();
var nodes=root.GetProperty("circuit").GetProperty("nodes").EnumerateArray().Skip(1).Select(n=>n.GetString()!).ToArray();
var signals=(mode=="missing"?Array.Empty<string>():nodes).Select(n=>new{id="v("+n+")",quantity="voltage",unit=mode=="wrong-unit"?"A":"V"}).ToArray();
var payload=JsonSerializer.Serialize(new{schema_version="1",case_id=mode=="case-id"?"wrong_case":root.GetProperty("case_id").GetString(),status="succeeded",complete=true,
 signals,samples=new[]{new{time_s=0.0,values=signals.Select(_=>123456.0).ToArray()}},ac_samples=Array.Empty<object>(),measurements=Array.Empty<object>(),assertions=Array.Empty<object>(),
 backend=new{id=mode=="provider"?"wrong_provider":backend=="lu"?"nalgebra_dense_lu_reference":"faer_sparse_lu",placement="cpu",max_unknowns=256,sparse=backend=="faer",gpu=false,complex=true,prepared_real=true,provider_version="SYNTHETIC TEST FIXTURE"},request_sha256=new string('0',64),synthetic_test_only=true});
if(mode=="non-finite")payload=payload.Replace("123456","1e999",StringComparison.Ordinal);
await Console.Out.WriteAsync(payload);
