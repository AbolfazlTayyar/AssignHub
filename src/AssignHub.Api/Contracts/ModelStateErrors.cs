using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AssignHub.Api.Contracts;

internal static class ModelStateErrors
{
    // System.Text.Json reports unreadable values (e.g. an unknown enum name) under a "$.field" key with
    // a message naming CLR types; replace those with a message that names the field instead.
    public static IEnumerable<string> From(ModelStateDictionary modelState) =>
        modelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => Describe(entry.Key, error)))
            .Distinct();

    private static string Describe(string key, ModelError error) =>
        key.StartsWith('$')
            ? key.Length > 2 ? $"The value of '{key[2..]}' is invalid." : "The request body is not valid JSON."
            : string.IsNullOrEmpty(error.ErrorMessage) ? $"The value of '{key}' is invalid." : error.ErrorMessage;
}
