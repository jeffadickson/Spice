#:property PublishAot=false
#:project ../dotnet/Simulator.RequestedQualification/Simulator.RequestedQualification.csproj
using Simulator.RequestedQualification;
// The shared qualification coordinator owns validation, bounds and provenance.
return await RequestedCorpus.RunAsync(args);
