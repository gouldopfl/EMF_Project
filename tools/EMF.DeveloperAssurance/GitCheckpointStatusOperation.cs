using System.Diagnostics;

namespace EMF.DeveloperAssurance;

internal sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface IGitCommandRunner
{
    GitCommandResult Run(
        string repositoryRoot,
        IReadOnlyList<string> arguments);
}

internal sealed class ProcessGitCommandRunner : IGitCommandRunner
{
    public GitCommandResult Run(
        string repositoryRoot,
        IReadOnlyList<string> arguments)
    {
        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
            throw new InvalidOperationException("Git could not be started.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return new GitCommandResult(
            process.ExitCode,
            output,
            error);
    }
}

internal sealed record GitCheckpointStatusResult(
    string RepositoryRoot,
    string WorkingTree,
    IReadOnlyList<string> RecentCheckpoints);

internal sealed class GitCheckpointStatusOperation
{
    private readonly IGitCommandRunner _git;

    public GitCheckpointStatusOperation()
        : this(new ProcessGitCommandRunner())
    {
    }

    internal GitCheckpointStatusOperation(
        IGitCommandRunner git)
    {
        ArgumentNullException.ThrowIfNull(git);
        _git = git;
    }

    public int Run()
    {
        var repositoryRoot = FindRepositoryRoot();

        if (repositoryRoot is null)
        {
            global::System.Console.Error.WriteLine(
                "EMF repository root was not found.");
            return 1;
        }

        try
        {
            var result = Capture(repositoryRoot);

            global::System.Console.WriteLine(
                "===== GIT / CHECKPOINT STATUS =====");
            global::System.Console.WriteLine(
                $"Repository: {result.RepositoryRoot}");
            global::System.Console.WriteLine();

            global::System.Console.WriteLine(
                "----- WORKING TREE -----");
            global::System.Console.Write(result.WorkingTree);

            global::System.Console.WriteLine();
            global::System.Console.WriteLine(
                "----- RECENT CHECKPOINTS -----");

            foreach (var checkpoint in result.RecentCheckpoints)
                global::System.Console.WriteLine(checkpoint);

            return 0;
        }
        catch (InvalidOperationException exception)
        {
            global::System.Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    internal GitCheckpointStatusResult Capture(
        string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var status =
            _git.Run(
                repositoryRoot,
                ["status", "--short", "--branch"]);

        EnsureSuccess("Git status", status);

        var log =
            _git.Run(
                repositoryRoot,
                ["log", "--oneline", "--decorate", "-6"]);

        EnsureSuccess("Git log", log);

        var checkpoints =
            log.StandardOutput
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries)
                .ToArray();

        return new GitCheckpointStatusResult(
            Path.GetFullPath(repositoryRoot),
            status.StandardOutput,
            checkpoints);
    }

    private static void EnsureSuccess(
        string operation,
        GitCommandResult result)
    {
        if (result.ExitCode == 0)
            return;

        var detail =
            string.IsNullOrWhiteSpace(result.StandardError)
                ? "No error detail was returned."
                : result.StandardError.Trim();

        throw new InvalidOperationException(
            $"{operation} failed: {detail}");
    }

    private static string? FindRepositoryRoot()
    {
        var starts =
            new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory
            };

        foreach (var start in starts)
        {
            var directory =
                new DirectoryInfo(
                    Path.GetFullPath(start));

            while (directory is not null)
            {
                if (File.Exists(
                        Path.Combine(
                            directory.FullName,
                            "EMF.sln")))
                    return directory.FullName;

                directory = directory.Parent;
            }
        }

        return null;
    }
}
