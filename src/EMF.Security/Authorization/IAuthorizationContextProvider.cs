namespace EMF.Security.Authorization;

public interface IAuthorizationContextProvider
{
    Task<AuthorizationContext?> GetContextAsync(
        string subjectId,
        CancellationToken cancellationToken = default);
}
