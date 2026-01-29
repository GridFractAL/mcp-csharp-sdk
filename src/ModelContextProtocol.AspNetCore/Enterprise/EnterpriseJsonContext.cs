// Modified by GridFractAL - Enterprise fork
// Source-generated JSON serialization context for AOT compatibility

using System.Text.Json.Serialization;

namespace ModelContextProtocol.AspNetCore.Enterprise;

/// <summary>
/// JSON serialization context for Enterprise session store types.
/// Required for AOT/trimming compatibility.
/// </summary>
[JsonSerializable(typeof(SessionMetadata))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class EnterpriseJsonContext : JsonSerializerContext;
