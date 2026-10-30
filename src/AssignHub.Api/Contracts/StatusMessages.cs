using Microsoft.AspNetCore.WebUtilities;

namespace AssignHub.Api.Contracts;

internal static class StatusMessages
{
    public static string For(int statusCode) =>
        ReasonPhrases.GetReasonPhrase(statusCode) is { Length: > 0 } phrase ? $"{phrase}." : $"HTTP {statusCode}.";
}
