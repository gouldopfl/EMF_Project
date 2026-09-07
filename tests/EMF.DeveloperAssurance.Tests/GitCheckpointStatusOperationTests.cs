using EMF.DeveloperAssurance;
using Xunit;

namespace EMF.DeveloperAssurance.Tests;

public sealed class GitCheckpointStatusOperationTests
{
    [Fact]
    public void Capture_ReturnsStructuredGitStatus()
    {
        var runner =
            new FakeGitCommandRunner(
                new GitCommandResult(
                    0,
                    "## main...origin/main\n M file.cs\n",
                    ""),
                new GitCommandResult(
                    0,
                    """
                    abc1234 First checkpoint
                    def5678 Second checkpoint
                    """,
                    ""));

        var operation =
            new GitCheckpointStatusOperation(runner);

        var result =
            operation.Capture("/tmp/emf-test");

        Assert.Equal(
            Path.GetFullPath("/tmp/emf-test"),
            result.RepositoryRoot);

        Assert.Contains(
            "## main...origin/main",
            result.WorkingTree);

        Assert.Equal(2, result.RecentCheckpoints.Count);
        Assert.Equal(
            "abc1234 First checkpoint",
            result.RecentCheckpoints[0]);

        Assert.Equal(
            ["status", "--short", "--branch"],
            runner.Calls[0]);

        Assert.Equal(
            ["log", "--oneline", "--decorate", "-6"],
            runner.Calls[1]);
    }

    [Fact]
    public void Capture_RejectsGitStatusFailure()
    {
        var runner =
            new FakeGitCommandRunner(
                new GitCommandResult(
                    128,
                    "",
                    "not a git repository"));

        var operation =
            new GitCheckpointStatusOperation(runner);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => operation.Capture("/tmp/emf-test"));

        Assert.Contains(
            "Git status failed",
            exception.Message);

        Assert.Contains(
            "not a git repository",
            exception.Message);
    }

    [Fact]
    public void Capture_RejectsGitLogFailure()
    {
        var runner =
            new FakeGitCommandRunner(
                new GitCommandResult(
                    0,
                    "## main...origin/main\n",
                    ""),
                new GitCommandResult(
                    128,
                    "",
                    "log unavailable"));

        var operation =
            new GitCheckpointStatusOperation(runner);

        var exception =
            Assert.Throws<InvalidOperationException>(
                () => operation.Capture("/tmp/emf-test"));

        Assert.Contains(
            "Git log failed",
            exception.Message);
    }

    private sealed class FakeGitCommandRunner :
        IGitCommandRunner
    {
        private readonly Queue<GitCommandResult> _results;

        public FakeGitCommandRunner(
            params GitCommandResult[] results)
        {
            _results = new Queue<GitCommandResult>(results);
        }

        public List<string[]> Calls { get; } = [];

        public GitCommandResult Run(
            string repositoryRoot,
            IReadOnlyList<string> arguments)
        {
            Calls.Add(arguments.ToArray());

            return _results.Dequeue();
        }
    }
}
