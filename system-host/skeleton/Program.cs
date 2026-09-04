using System.Reflection;
using Intropy.Topology.Aspire;
using Intropy.Topology.Generation;

// One entry point, two backends of the same discovered truth. F5 (no args, or `run`) translates
// the topology into Aspire resources and hands them to DCP; the other verbs generate/check/graph
// from the identical model without touching Aspire. `graph` goes through MessageGraph, which adds
// the messagegroups section the topology model does not carry.
//
//   dotnet run                       -> Aspire (dashboard: components + Redis + Dapr sidecars)
//   dotnet run -- check              -> validate the topology
//   dotnet run -- graph              -> print the SystemTopology JSON
//   dotnet run -- generate ./out     -> write Dapr YAML + per-component config

var assembly = Assembly.GetExecutingAssembly();

return args switch
{
    ["run", ..] or [] => await IntropyAspire.RunAsync(assembly, args),
    ["graph", ..] => MessageGraph.Run(assembly, args),
    _ => IntropyGenerate.Run(assembly, args),
};
