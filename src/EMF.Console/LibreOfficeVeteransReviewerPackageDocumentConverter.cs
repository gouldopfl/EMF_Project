using EMF.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.ConsoleApplication;

internal sealed class LibreOfficeVeteransReviewerPackageDocumentConverter :
    IVeteransReviewerPackageDocumentConverter,
    IVeteransReviewerPackageDocumentConverterInfoProvider
{
    internal const long DefaultMaxInputBytes = 100L * 1024 * 1024;
    internal const long DefaultMaxOutputBytes = 100L * 1024 * 1024;
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly string _executablePath;
    private readonly TimeSpan _timeout;
    private readonly long _maxInputBytes;
    private readonly long _maxOutputBytes;

    public LibreOfficeVeteransReviewerPackageDocumentConverter(
        string? executablePath = null,
        TimeSpan? timeout = null,
        long maxInputBytes = DefaultMaxInputBytes,
        long maxOutputBytes = DefaultMaxOutputBytes)
    {
        _executablePath =
            string.IsNullOrWhiteSpace(executablePath)
                ? ResolveExecutablePath()
                : executablePath.Trim();

        _timeout = timeout ?? DefaultTimeout;

        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        if (maxInputBytes <= 0 || maxInputBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maxInputBytes));

        if (maxOutputBytes <= 0 || maxOutputBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));

        _maxInputBytes = maxInputBytes;
        _maxOutputBytes = maxOutputBytes;
    }

    public async Task<VeteransReviewerPackageDocumentConverterInfo> GetDocumentConverterInfoAsync(
        CancellationToken cancellationToken = default)
    {
        using var performanceTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.ConverterIdentity);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo =
            new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

        ApplyControlledEnvironment(startInfo);
        startInfo.ArgumentList.Add("--version");

        using var process = new Process
        {
            StartInfo = startInfo
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    "LibreOffice version process could not be started.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException(
                "LibreOffice identity could not be determined.",
                ex);
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadBoundedAsync(process.StandardError, cancellationToken);

        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                "LibreOffice version query exceeded the allowed time.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "LibreOffice version query failed with exit code " +
                $"{process.ExitCode}.");
        }

        var version =
            new[] { stdout, stderr }
                .SelectMany(value => value.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .FirstOrDefault(value => value.Length > 0);

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidDataException(
                "LibreOffice version query returned no identity text.");
        }

        return new VeteransReviewerPackageDocumentConverterInfo(
            "LibreOffice",
            version + " | emf-pdf-profile-v1:" + CaptureProfileHash());
    }

    public async Task<byte[]> ConvertDocxToPdfAsync(
        ReadOnlyMemory<byte> docx,
        CancellationToken cancellationToken = default)
    {
        using var performanceTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.ConverterTotal);
        cancellationToken.ThrowIfCancellationRequested();

        if (docx.Length == 0)
        {
            throw new InvalidDataException(
                "Reviewer package DOCX content is empty.");
        }

        if (docx.Length > _maxInputBytes)
        {
            throw new InvalidDataException(
                "Reviewer package DOCX exceeds the maximum PDF-conversion input size.");
        }

        var workingDirectory =
            Path.Combine(
                Path.GetTempPath(),
                $"emf-reviewer-pdf-{Guid.NewGuid():N}");

        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(workingDirectory);
        else
            Directory.CreateDirectory(workingDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var profileDirectory =
            Path.Combine(
                workingDirectory,
                "profile");

        Directory.CreateDirectory(profileDirectory);

        // A fresh user profile, fixed locale/timezone, and explicit font directories
        // are presentation inputs, not ambient user preferences. See the contract.
        await File.WriteAllTextAsync(Path.Combine(profileDirectory, "registrymodifications.xcu"),
            "<oor:items xmlns:oor=\"http://openoffice.org/2001/registry\"><item oor:path=\"/org.openoffice.Setup/L10N\"><prop oor:name=\"ooLocale\" oor:op=\"fuse\"><value>en-US</value></prop></item></oor:items>", cancellationToken);
        var fontConfig = Path.Combine(workingDirectory, "fonts.conf");
        await File.WriteAllTextAsync(fontConfig,
            "<?xml version=\"1.0\"?><!DOCTYPE fontconfig SYSTEM \"fonts.dtd\"><fontconfig><dir>/usr/share/fonts</dir><dir>/usr/local/share/fonts</dir><cachedir>" +
            System.Security.SecurityElement.Escape(Path.Combine(workingDirectory, "font-cache")) + "</cachedir></fontconfig>", cancellationToken);
        var conversionProfile = CaptureProfileHash();

        var inputPath =
            Path.Combine(
                workingDirectory,
                "reviewer-package.docx");

        var outputPath =
            Path.Combine(
                workingDirectory,
                "reviewer-package.pdf");

        try
        {
            await File.WriteAllBytesAsync(
                inputPath,
                docx.ToArray(),
                cancellationToken);

            var startInfo =
                new ProcessStartInfo
                {
                    FileName = _executablePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

            ApplyControlledEnvironment(startInfo);
            startInfo.Environment["FONTCONFIG_FILE"] = fontConfig;
            startInfo.ArgumentList.Add("--headless");
            startInfo.ArgumentList.Add("--nologo");
            startInfo.ArgumentList.Add("--nodefault");
            startInfo.ArgumentList.Add("--nofirststartwizard");
            startInfo.ArgumentList.Add(
                $"-env:UserInstallation={new Uri(Path.GetFullPath(profileDirectory) + Path.DirectorySeparatorChar).AbsoluteUri}");
            startInfo.ArgumentList.Add("--convert-to");
            startInfo.ArgumentList.Add("pdf:writer_pdf_Export");
            startInfo.ArgumentList.Add("--outdir");
            startInfo.ArgumentList.Add(workingDirectory);
            startInfo.ArgumentList.Add(inputPath);

            using var process = new Process
            {
                StartInfo = startInfo
            };

            using var processTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.LibreOfficeConversion);
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "LibreOffice PDF conversion process could not be started.");
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    "LibreOffice is required for PDF reviewer-package output. " +
                    "Install LibreOffice or set EMF_LIBREOFFICE_PATH to its executable.",
                    ex);
            }

            var stdoutTask = ReadBoundedAsync(process.StandardOutput, cancellationToken);
            var stderrTask = ReadBoundedAsync(process.StandardError, cancellationToken);

            using var timeoutSource =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);

            timeoutSource.CancelAfter(_timeout);

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new TimeoutException(
                    "LibreOffice PDF conversion exceeded the allowed time.");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            // Drain subprocess output without exposing document text or paths in diagnostics.
            await stdoutTask;
            await stderrTask;
            processTiming.Dispose();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "LibreOffice PDF conversion failed with exit code " +
                    $"{process.ExitCode}.");
            }

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    "LibreOffice PDF conversion completed without producing a PDF.");
            }

            var outputInfo = new FileInfo(outputPath);

            if (outputInfo.Length <= 0 || outputInfo.Length > _maxOutputBytes)
            {
                throw new InvalidDataException(
                    "Converted reviewer-package PDF has an invalid size.");
            }

            var pdf =
                await File.ReadAllBytesAsync(
                    outputPath,
                    cancellationToken);

            ValidatePdfSignature(pdf);
            if (!string.Equals(conversionProfile, CaptureProfileHash(), StringComparison.Ordinal))
                throw new InvalidDataException("PDF conversion environment changed during preparation.");
            return pdf;
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    internal static void ApplyControlledEnvironment(ProcessStartInfo startInfo)
    {
        // Ambient desktop, font and loader settings must not become unrecorded
        // presentation inputs. Pin the shared profile, never a claim-specific one.
        foreach (var key in startInfo.Environment.Keys.ToArray())
            if (new[] { "SAL_", "OOO_", "LO_", "FONTCONFIG_", "GTK_", "GDK_", "QT_", "UNO_", "XDG_" }
                .Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)) ||
                key is "DISPLAY" or "WAYLAND_DISPLAY" or "LD_PRELOAD" or "LD_LIBRARY_PATH")
                startInfo.Environment.Remove(key);
        startInfo.Environment["LC_ALL"] = "C.UTF-8";
        startInfo.Environment["LANG"] = "C.UTF-8";
        startInfo.Environment["LANGUAGE"] = "en_US";
        startInfo.Environment["TZ"] = "UTC";
        startInfo.Environment["SAL_USE_VCLPLUGIN"] = "svp";
        startInfo.Environment["SAL_FORCEDPI"] = "96";
    }

    private string CaptureProfileHash()
    {
        using var performanceTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.ConverterProfile);
        if (!OperatingSystem.IsLinux())
            throw new NotSupportedException("The controlled reviewer PDF profile currently requires Linux.");
        var executable = Path.IsPathRooted(_executablePath) ? _executablePath :
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
                .Select(dir => Path.Combine(dir, _executablePath)).FirstOrDefault(File.Exists)
                ?? throw new InvalidDataException("Converter executable cannot be resolved.");
        executable = Path.GetFullPath(executable);
        var target = File.ResolveLinkTarget(executable, returnFinalTarget: true)?.FullName ?? executable;
        var files = new SortedSet<string>(StringComparer.Ordinal) { executable, target };
        // Pin the installed layout engine, dictionaries/registry, fallback fonts,
        // and shaping dependencies. User font/config directories are excluded.
        foreach (var root in new[] { Path.GetDirectoryName(target)!,
            Path.Combine(Path.GetDirectoryName(target)!, "..", "share", "registry"),
            "/usr/share/fonts", "/usr/local/share/fonts" })
            if (Directory.Exists(root))
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    files.Add(Path.GetFullPath(file));
        var dependencies = "/usr/lib/" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant() switch
        {
            "/usr/lib/x64" => "/usr/lib/x86_64-linux-gnu",
            "/usr/lib/arm64" => "/usr/lib/aarch64-linux-gnu",
            var other => other
        };
        if (Directory.Exists(dependencies))
            foreach (var prefix in new[] { "libfreetype", "libfontconfig", "libharfbuzz", "libcairo", "libpango", "libicu", "libgraphite", "libpng", "libstdc++", "libgcc_s", "libz.so" })
                foreach (var file in Directory.EnumerateFiles(dependencies, prefix + "*")) files.Add(file);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("emf-pdf-profile-v1|C.UTF-8|UTC|en-US|svp|96dpi|writer_pdf_Export|" +
            RuntimeInformation.OSDescription + "|" + RuntimeInformation.ProcessArchitecture));
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file + "\n"));
            using var stream = File.OpenRead(file);
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string ResolveExecutablePath()
    {
        var configured =
            Environment.GetEnvironmentVariable(
                "EMF_LIBREOFFICE_PATH");

        return string.IsNullOrWhiteSpace(configured)
            ? "libreoffice"
            : configured.Trim();
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        const int maxCharacters = 16 * 1024;
        var buffer = new char[1024];
        var result = new System.Text.StringBuilder();

        while (true)
        {
            var read =
                await reader.ReadAsync(
                    buffer.AsMemory(),
                    cancellationToken);

            if (read == 0)
                break;

            if (result.Length < maxCharacters)
            {
                var remaining = maxCharacters - result.Length;
                result.Append(
                    buffer,
                    0,
                    Math.Min(read, remaining));
            }
        }

        return result.ToString().Trim();
    }

    private static void ValidatePdfSignature(byte[] pdf)
    {
        if (pdf.Length < 5 ||
            pdf[0] != (byte)'%' ||
            pdf[1] != (byte)'P' ||
            pdf[2] != (byte)'D' ||
            pdf[3] != (byte)'F' ||
            pdf[4] != (byte)'-')
        {
            throw new InvalidDataException(
                "LibreOffice conversion output is not a PDF document.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
