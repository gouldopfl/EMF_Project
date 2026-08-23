using EMF.Discovery.Models.Email;

namespace EMF.Orchestration.Contracts;

public interface IEmailMessageParser
{
    Task<EmailMessage> ParseAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);
}
