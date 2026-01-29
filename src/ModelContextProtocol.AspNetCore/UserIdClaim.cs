namespace ModelContextProtocol.AspNetCore;

// Modified by GridFractAL - Enterprise fork
// Changes: Made public (was internal sealed) for StreamableHttpSession extensibility

/// <summary>
/// Represents a user identity claim extracted from the HTTP request.
/// Enterprise fork: Record is public (not internal sealed) to support class extensibility.
/// </summary>
public sealed record UserIdClaim(string Type, string Value, string Issuer);
