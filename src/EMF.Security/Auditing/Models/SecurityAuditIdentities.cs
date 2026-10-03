using System.Text.Json.Serialization;
namespace EMF.Security.Auditing.Models;

public static class SecurityAuditIdentity
{
    public static string Validate(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':')))
            throw new ArgumentException("Audit identity is invalid.");
        return value;
    }
}
public readonly record struct SecurityAuditEventId
{
    public string Value { get; }
    [JsonConstructor] public SecurityAuditEventId(string value) => Value = SecurityAuditIdentity.Validate(value);
    public static SecurityAuditEventId New() => new(Guid.NewGuid().ToString("N"));
}
public readonly record struct SecurityMutationOperationId
{
    public string Value { get; }
    [JsonConstructor] public SecurityMutationOperationId(string value) => Value = SecurityAuditIdentity.Validate(value);
    public static SecurityMutationOperationId New() => new(Guid.NewGuid().ToString("N"));
}
