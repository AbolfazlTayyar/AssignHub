namespace AssignHub.Domain;

// SPEC §4.5: rejected input from a factory or entity method; the API maps it to a 400.
public sealed class DomainException(string message) : Exception(message);
