using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Intropy.Topology.Generation;

/// <summary>
/// The <c>graph</c> verb with this system's messages folded into the record.
/// Intropy.Topology materializes topics from the topology's edges and models no
/// messages, so the generation backend prints the record without them; the
/// messagegroups section is composed here from <see cref="Messages"/> — the
/// registry <c>intropy sys create</c> rendered — and merged into that JSON.
/// Every other verb delegates to the backend untouched.
/// </summary>
internal static class MessageGraph
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Runs the graph verb and prints its record carrying the messagegroups section.</summary>
    /// <param name="assembly">The assembly declaring the system.</param>
    /// <param name="args">Command-line arguments, verb included.</param>
    /// <returns>The backend's exit code.</returns>
    public static int Run(Assembly assembly, string[] args)
    {
        // stdout is captured, not redirected: the backend writes the record with
        // Console.WriteLine, and stderr (its diagnostics) must stay live.
        var stdout = Console.Out;
        var captured = new StringWriter();
        int exit;
        Console.SetOut(captured);
        try
        {
            exit = IntropyGenerate.Run(assembly, args);
        }
        finally
        {
            Console.SetOut(stdout);
        }

        stdout.Write(WithMessages(captured.ToString()));
        return exit;
    }

    // A system with no internal messages emits no section, matching how the
    // backend omits its own empty collections. Anything unparseable — a failed
    // run's diagnostics — passes through verbatim rather than turning a
    // diagnosed failure into a serialization error.
    private static string WithMessages(string printed)
    {
        if (Messages.All.Count == 0 || string.IsNullOrWhiteSpace(printed))
        {
            return printed;
        }

        JsonObject? document;
        try
        {
            document = JsonNode.Parse(printed) as JsonObject;
        }
        catch (JsonException)
        {
            return printed;
        }

        if (document is null)
        {
            return printed;
        }

        document["messagegroups"] = JsonSerializer.SerializeToNode(Messages.All, s_json);
        return document.ToJsonString(s_json) + Environment.NewLine;
    }
}
