using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security;
using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class ReviewerDocumentConstructorCompatibilityTests
{
    [Fact]
    public void OriginalThreeArgumentSignatureAndAdditiveSignatures_ArePresent()
    {
        var original = new[] { typeof(IVeteransReviewerPackageDocumentConverter),
            typeof(IVeteransReviewerRegulatoryTextProvider), typeof(IEvidencePackageRepository) };
        var type = typeof(VeteransReviewerPackageDocumentOutputService);
        var constructor = type.GetConstructor(original);
        Assert.NotNull(constructor);
        Assert.All(constructor!.GetParameters(), parameter =>
        {
            Assert.True(parameter.IsOptional);
            Assert.Null(parameter.DefaultValue);
        });
        Assert.NotNull(type.GetConstructor(original.Append(typeof(EmfBuildManifest)).ToArray()));
        Assert.NotNull(type.GetConstructor(original.Append(typeof(EmfBuildManifest)).Append(typeof(EmfVerifiedRuntimeIdentity)).ToArray()));
    }

    [Fact]
    public async Task CallerCompiledAgainstM91Constructor_BindsCurrentImplementation()
    {
        // Compile against a fixed M91 contract, not the current constructor. The
        // caller's emitted MemberRef must bind when only today's implementation
        // is available at runtime. No git checkout or changes to repository output.
        var directory = Path.Combine(Path.GetTempPath(), "emf-m91-abi-" + Guid.NewGuid().ToString("N"));
        var contractDirectory = Path.Combine(directory, "contract");
        var callerDirectory = Path.Combine(directory, "caller");
        Directory.CreateDirectory(contractDirectory);
        Directory.CreateDirectory(callerDirectory);
        try
        {
            var modelPath = SecurityElement.Escape(typeof(IEvidencePackageRepository).Assembly.Location);
            var current = typeof(VeteransReviewerPackageDocumentOutputService).Assembly.GetName();
            await File.WriteAllTextAsync(Path.Combine(contractDirectory, "contract.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>{{current.Name}}</AssemblyName>
                    <AssemblyVersion>{{current.Version}}</AssemblyVersion>
                    <NuGetAudit>false</NuGetAudit>
                  </PropertyGroup>
                  <ItemGroup><Reference Include="EMF.Extensions.VeteransClaims"><HintPath>{{modelPath}}</HintPath></Reference></ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(contractDirectory, "Contract.cs"), """
                namespace EMF.Extensions.VeteransClaims.Orchestration;
                public interface IVeteransReviewerPackageDocumentConverter {}
                public interface IVeteransReviewerRegulatoryTextProvider {}
                public sealed class VeteransReviewerPackageDocumentOutputService
                {
                    public VeteransReviewerPackageDocumentOutputService(
                        IVeteransReviewerPackageDocumentConverter converter = null,
                        IVeteransReviewerRegulatoryTextProvider regulatoryTextProvider = null,
                        EMF.Extensions.VeteransClaims.Contracts.IEvidencePackageRepository snapshotRepository = null) {}
                }
                """);
            var callerName = "M91Caller" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(Path.Combine(callerDirectory, "caller.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>{{callerName}}</AssemblyName><NuGetAudit>false</NuGetAudit></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../contract/contract.csproj" />
                    <Reference Include="EMF.Extensions.VeteransClaims"><HintPath>{{modelPath}}</HintPath></Reference>
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(callerDirectory, "Caller.cs"), """
                public static class M91Caller
                {
                    public static object Create() => new EMF.Extensions.VeteransClaims.Orchestration.VeteransReviewerPackageDocumentOutputService();
                }
                """);
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = callerDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "build", "caller.csproj", "--nologo", "--verbosity", "quiet", "-p:RestoreIgnoreFailedSources=true" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True(process.ExitCode == 0, await stdout + await stderr);

            var caller = AssemblyLoadContext.Default.LoadFromAssemblyPath(
                Path.Combine(callerDirectory, "bin", "Debug", "net10.0", callerName + ".dll"));
            var instance = caller.GetType("M91Caller")!.GetMethod("Create")!.Invoke(null, null);
            Assert.IsType<VeteransReviewerPackageDocumentOutputService>(instance);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
