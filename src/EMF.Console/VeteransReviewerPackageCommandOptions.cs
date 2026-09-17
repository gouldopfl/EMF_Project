namespace EMF.ConsoleApplication;

internal sealed record VeteransReviewerPackageCommandOptions(
    string? BasisId,
    VeteransReviewerPackageOutputRequest? Output);

internal static class VeteransReviewerPackageCommandOptionsParser
{
    public static bool TryParse(
        string[] args,
        out VeteransReviewerPackageCommandOptions? options,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        error = null;

        if (args.Length < 4)
        {
            error = "Reviewer command requires a database path and claim-issue identifier.";
            return false;
        }

        string? basisId = null;
        string? outputPath = null;
        string? format = null;

        for (var index = 4; index < args.Length; index++)
        {
            var value = args[index];

            if (string.Equals(value, "--basis", StringComparison.Ordinal))
            {
                if (basisId is not null)
                {
                    error = "Reviewer command contains more than one --basis option.";
                    return false;
                }

                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    error = "Reviewer command --basis option requires a basis identifier.";
                    return false;
                }

                basisId = args[index].Trim();
                continue;
            }

            if (string.Equals(value, "--format", StringComparison.Ordinal))
            {
                if (format is not null)
                {
                    error = "Reviewer command contains more than one --format option.";
                    return false;
                }

                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
                {
                    error = "Reviewer command --format option requires docx, pdf, or both.";
                    return false;
                }

                format = args[index].Trim();
                continue;
            }

            if (value.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unknown reviewer command option: {value}";
                return false;
            }

            if (outputPath is not null)
            {
                error = "Reviewer command accepts only one output path.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Reviewer command output path is empty.";
                return false;
            }

            outputPath = value.Trim();
        }

        if (format is not null && outputPath is null)
        {
            error = "Reviewer command --format requires an output path.";
            return false;
        }

        VeteransReviewerPackageOutputRequest? output = null;

        if (outputPath is not null)
        {
            try
            {
                output =
                    VeteransReviewerPackageOutputRequestResolver.Resolve(
                        outputPath,
                        format);
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        options =
            new VeteransReviewerPackageCommandOptions(
                basisId,
                output);

        return true;
    }
}
