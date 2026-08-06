namespace EMF.Core.Contracts;

public interface ICapability
{
    string CapabilityId { get; }

    Version CapabilityVersion { get; }

    IReadOnlyDictionary<string, string> Attributes { get; }
}
