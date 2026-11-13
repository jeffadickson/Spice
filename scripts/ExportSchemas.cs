#:project ../dotnet/Simulator.Client/Simulator.Client.csproj
#:property PublishAot=false
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Simulator.Client;

// Export actual source-generated DTO metadata. Rust's unit/topology/byte-budget validation
// remains authoritative; JSON Schema does not establish that a circuit is solvable.
WireFormat.Options.MakeReadOnly();
var input=WireFormat.Options.GetJsonSchemaAsNode(typeof(SimulationRequest),new(){TreatNullObliviousAsNonNullable=true});
void Constrain(JsonNode node)
{
    if(node is JsonObject obj)
    {
        if(obj["properties"] is JsonObject properties)
        {
            obj["additionalProperties"]=false;
            foreach(var(key,value) in properties.ToArray())
            {
                if(value is not JsonObject field)continue;
                if(key is not ("experiment" or "initial_state" or "adaptive"))field["not"]=JsonNode.Parse("{\"type\":\"null\"}");
                if(key is "id" or "case_id"){field["minLength"]=1;field["maxLength"]=256;}
                if(key=="signal")field["maxLength"]=512;
                if(key=="unit")field["maxLength"]=64;
                if(key is "ohms" or "farads" or "henries" or "frequency_hz" or "stop_s" or "step_s" or "max_step_s" or "min_step_s" or "relative" or "voltage_absolute" or "current_absolute")field["exclusiveMinimum"]=0;
                if(key=="relative")field["exclusiveMaximum"]=1;
                if(key is "p" or "n" or "cp" or "cn"){field["minimum"]=0;field["maximum"]=127;}
                // Early charge controls are not independent voltage sources:
                // their VAF domain is strictly positive, while source volts may be signed.
                if(key=="volts"&&properties.ContainsKey("cp")&&properties.ContainsKey("cn"))field["exclusiveMinimum"]=0;
                if(key=="time_s"||key=="start_s")field["minimum"]=0;
                if(key=="method")field["enum"]=new JsonArray("backward_euler","trapezoidal");
                if(key=="max_steps"){field["minimum"]=1;field["maximum"]=100000;}
                if(key=="max_attempts"){field["minimum"]=1;field["maximum"]=1000000;}
                var maximum=key switch {"nodes"=>128,"elements"=>1024,"sources"=>128,"points"=>4096,"frequencies_hz"=>10000,"measurements"=>256,"assertions"=>512,"initial_state"=>256,_=>0};
                if(maximum!=0)field["maxItems"]=maximum;
                if(key is "nodes" or "points" or "frequencies_hz")field["minItems"]=1;
                if(key=="frequencies_hz"&&field["items"] is JsonObject item)item["exclusiveMinimum"]=0;
            }
        }
        if(obj["items"] is JsonObject items)items["not"]=JsonNode.Parse("{\"type\":\"null\"}");
        foreach(var child in obj.ToArray())if(child.Value is not null)Constrain(child.Value);
    }
    else if(node is JsonArray array)foreach(var child in array)if(child is not null)Constrain(child);
}
Constrain(input);
input["$schema"]="https://json-schema.org/draft/2020-12/schema";
input["title"]="Experimental simulator request v1";
input["$comment"]="Exported from managed DTOs; Rust additionally validates finite values, UTF-8 byte lengths, dimensions, IDs, source references, initial states, solver capabilities and budgets. Export is not independent schema qualification.";
input["properties"]!["schema_version"]!["const"]="1";
input["required"]=new JsonArray("schema_version","case_id","circuit","analysis");
var success=WireFormat.Options.GetJsonSchemaAsNode(typeof(ResultManifest),new(){TreatNullObliviousAsNonNullable=true});
success["properties"]!["schema_version"]!["const"]="1";
success["properties"]!["status"]!["const"]="succeeded";
success["properties"]!["complete"]!["const"]=true;
if(success["required"] is JsonArray required)
    for(var i=required.Count-1;i>=0;i--)if(required[i]!.GetValue<string>() is "integration" or "dc_power")required.RemoveAt(i);
// Exported repeated-type references are rooted at the original success schema.
void Relocate(JsonNode node)
{
    if(node is JsonObject obj)
    {
        if(obj["$ref"] is JsonValue value&&value.TryGetValue<string>(out var reference)&&reference.StartsWith('#'))obj["$ref"]="#/oneOf/0"+reference[1..];
        foreach(var child in obj.ToArray())if(child.Value is not null)Relocate(child.Value);
    }
    else if(node is JsonArray array)foreach(var child in array)if(child is not null)Relocate(child);
}
Relocate(success);
var failure=JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["schema_version","status","complete","error"],"properties":{"schema_version":{"const":"1"},"status":{"const":"failed"},"complete":{"const":false},"error":{"type":"object","required":["code","message"],"properties":{"code":{"type":"string"},"message":{"type":"string"}}}}}""")!;
var output=new JsonObject{["$schema"]="https://json-schema.org/draft/2020-12/schema",["title"]="Experimental simulator result v1",["oneOf"]=new JsonArray(success,failure)};
Directory.CreateDirectory("schemas");
File.WriteAllText("schemas/request-v1.json",input.ToJsonString(new(){WriteIndented=true}));
File.WriteAllText("schemas/result-v1.json",output.ToJsonString(new(){WriteIndented=true}));
Console.WriteLine("Exported request/result schemas from current DTO metadata.");
