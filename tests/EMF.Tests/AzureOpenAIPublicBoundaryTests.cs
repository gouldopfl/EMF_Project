using System.Reflection;
using EMF.Intelligence.AzureOpenAI.Configuration;

namespace EMF.Tests;

public sealed class AzureOpenAIPublicBoundaryTests
{
    [Fact]
    public void PublicApi_DoesNotExposeProviderSdkTypes()
    {
        var assembly =
            typeof(AzureOpenAIOptions).Assembly;

        var exposedTypes =
            assembly.GetExportedTypes()
                .SelectMany(GetSignatureTypes)
                .ToArray();

        Assert.DoesNotContain(
            exposedTypes,
            IsProviderSdkType);
    }

    private static IEnumerable<Type> GetSignatureTypes(
        Type type)
    {
        if (type.BaseType is not null)
            yield return type.BaseType;

        foreach (var contract in type.GetInterfaces())
            yield return contract;

        foreach (var constructor in
            type.GetConstructors())
        {
            foreach (var parameter in
                constructor.GetParameters())
                yield return parameter.ParameterType;
        }

        foreach (var method in type.GetMethods(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;

            foreach (var parameter in
                method.GetParameters())
                yield return parameter.ParameterType;
        }
    }

    private static bool IsProviderSdkType(Type type)
    {
        if (type.IsArray || type.IsByRef)
            return IsProviderSdkType(
                type.GetElementType()!);

        if (type.IsGenericType &&
            type.GetGenericArguments().Any(
                IsProviderSdkType))
            return true;

        var name = type.FullName ?? string.Empty;

        return name.StartsWith("Azure.AI.OpenAI.") ||
            name.StartsWith("OpenAI.") ||
            name.StartsWith("System.ClientModel.");
    }
}
