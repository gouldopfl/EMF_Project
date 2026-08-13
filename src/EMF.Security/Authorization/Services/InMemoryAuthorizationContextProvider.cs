using EMF.Security.Models.Identities;

namespace EMF.Security.Authorization.Services;

public sealed class InMemoryAuthorizationContextProvider
    : IAuthorizationContextProvider
{
    private readonly IReadOnlyDictionary<
        string,
        AuthorizationContext> _contexts;

    public InMemoryAuthorizationContextProvider(
        IEnumerable<AuthorizationContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        _contexts = contexts.ToDictionary(
            context => context.SubjectId,
            StringComparer.Ordinal);
    }

    public Task<AuthorizationContext?> GetContextAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectId))
        {
            return Task.FromResult<AuthorizationContext?>(null);
        }

        _contexts.TryGetValue(
            subjectId,
            out var context);

        return Task.FromResult(context);
    }
}
