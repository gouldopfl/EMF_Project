using EMF.Security.Azure.Keys;

namespace EMF.Security.Azure.Cryptography;

public interface IAzureKeyCryptographyFactory
{
    IAzureKeyCryptography Create(AzureKeyReference keyReference);
}
