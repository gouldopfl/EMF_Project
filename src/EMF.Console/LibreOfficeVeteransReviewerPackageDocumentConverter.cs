using System.Diagnostics;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.ConsoleApplication;

internal sealed class LibreOfficeVeteransReviewerPackageDocumentConverter :
    IVeteransReviewerPackageDocumentConverter
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

    public async Task<byte[]> ConvertDocxToPdfAsync(
        ReadOnlyMemory<byte> docx,
        CancellationToken cancellationToken = default)
    {
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

        Directory.CreateDirectory(workingDirectory);

        var profileDirectory =
            Path.Combine(
                workingDirectory,
                "profile");

        Directory.CreateDirectory(profileDirectory);

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

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "LibreOffice PDF conversion failed with exit code " +
                    $"{process.ExitCode}. " +
                    BuildDiagnostic(stdout, stderr));
            }

            if (!File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    "LibreOffice PDF conversion completed without producing a PDF. " +
                    BuildDiagnostic(stdout, stderr));
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
            return pdf;
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
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

    private static string BuildDiagnostic(
        string stdout,
        string stderr)
    {
        var diagnostic =
            string.Join(
                " ",
                new[] { stdout, stderr }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(diagnostic)
            ? "No converter diagnostics were returned."
            : diagnostic;
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
