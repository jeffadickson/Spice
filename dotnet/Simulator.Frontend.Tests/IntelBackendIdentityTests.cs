using Simulator.Client;
using Simulator.RequestedQualification;
using Simulator.Frontend;
using Simulator.Syntax;
using Xunit;
namespace Simulator.Frontend.Tests;

/// <summary>GPU comparison evidence requires both the exact provider and physical placement metadata.</summary>
public sealed class IntelBackendIdentityTests
{
 [Fact]
 public void OriginalDeckPreparationRetainsExplicitIntelBackend()
 {
  var syntax=DeckParser.Parse("Intel deck\nV1 a 0 5\nR1 a 0 1k\n.op\n.end\n",collectDeclarations:true);
  var plan=RequestedAnalysis.Prepare(syntax,backend:"intel");
  Assert.True(plan.Success);
  Assert.All(plan.Operations.SelectMany(o=>o.Requests),request=>Assert.Equal("intel",request.Backend));
 }
 [Theory]
 [InlineData("intel_opencl_viennacl_gmres_experimental","gpu",true,true)]
 [InlineData("intel_opencl_viennacl_gmres_experimental","cpu",false,false)]
 [InlineData("intel_opencl_viennacl_gmres_experimental","gpu",false,false)]
 [InlineData("intel_opencl_viennacl_gmres_experimental","cpu",true,false)]
 [InlineData("nalgebra_dense_lu_reference","gpu",true,false)]
 [InlineData("nalgebra_dense_lu_reference","cpu",false,false)]
 public void IntelRequestsCannotPassWithCpuFallback(string id,string placement,bool gpu,bool expected)
 {
  var actual=new BackendInfo(id,placement,256,false,gpu,true,false,"test");
  Assert.Equal(expected,RequestedComparison.BackendIdentityMatches("intel",actual));
 }
 [Theory]
 [InlineData("lu","nalgebra_dense_lu_reference")]
 [InlineData("faer","faer_sparse_lu")]
 [InlineData("qr","nalgebra_dense_qr_reference")]
 public void CpuIdentityRemainsExplicit(string requested,string id)
 {
  var actual=new BackendInfo(id,"cpu",256,false,false,true,false,"test");
  Assert.True(RequestedComparison.BackendIdentityMatches(requested,actual));
  Assert.False(RequestedComparison.BackendIdentityMatches("unknown",actual));
  Assert.False(RequestedComparison.BackendIdentityMatches(requested,actual with{Gpu=true}));
 }
}
