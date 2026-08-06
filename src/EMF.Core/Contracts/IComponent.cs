namespace EMF.Core.Contracts;

public interface IComponent
{
    string ComponentId { get; }

    string DisplayName { get; }

    Version ComponentVersion { get; }
}
