namespace ModelContextProtocol.AspNetCore;

/// <summary>
/// Represents a user identity claim extracted from an HTTP request for session binding.
/// </summary>
/// <param name="Type">The claim type (e.g., ClaimTypes.NameIdentifier).</param>
/// <param name="Value">The claim value (typically the user ID).</param>
/// <param name="Issuer">The claim issuer (e.g., the identity provider).</param>
/// <remarks>
/// This record is made public to support enterprise session store implementations
/// that need to bind sessions to authenticated users.
/// </remarks>
public sealed record UserIdClaim(string Type, string Value, string Issuer);
