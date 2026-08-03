using Intropy.Topology;

// The platform services the system's blocks call through Dapr service invocation.
// Every extractor and loader uses both (the framework's block builders require
// idempotency and business-incident routing), wired in the system definition via
// `.Uses(...)`. A ref that no component uses never enters the topology. The
// development definition substitutes OpenAPI-backed mocks for local runs.
public static class Services
{
    /// <summary>The idempotency service (Dapr app id 'idempotency-service').</summary>
    public static readonly ServiceRef Idempotency = ServiceRef.Define("idempotency-service");

    /// <summary>The business-incident service (Dapr app id 'business-incident-service').</summary>
    public static readonly ServiceRef BusinessIncidents = ServiceRef.Define("business-incident-service");
}
