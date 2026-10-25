using OpenClicky.Providers;

namespace OpenClicky.Core.Tests;

public class OpenCodeWorkspaceServiceTests
{
    [Fact]
    public void StartInfo_IsHiddenPureLoopbackAndApprovalGated()
    {
        const string password = "test-only-password";
        var executable = typeof(OpenCodeWorkspaceService).Assembly.Location;
        var workspace = Path.GetDirectoryName(executable)!;

        var start = OpenCodeWorkspaceService.CreateStartInfo(executable, workspace, 43210, password);

        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
        Assert.True(start.RedirectStandardOutput);
        Assert.True(start.RedirectStandardError);
        Assert.Equal(workspace, start.WorkingDirectory);
        Assert.Equal(
            ["serve", "--pure", "--hostname", "127.0.0.1", "--port", "43210"],
            start.ArgumentList);
        Assert.Equal(password, start.Environment["OPENCODE_SERVER_PASSWORD"]);
        Assert.Equal(
            "{\"permission\":{\"*\":\"ask\",\"external_directory\":\"deny\"}}",
            start.Environment["OPENCODE_CONFIG_CONTENT"]);
    }

    [Fact]
    public void ValidateWorkspace_RejectsDriveRoot()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;

        Assert.Throws<ArgumentException>(() => OpenCodeWorkspaceService.ValidateWorkspace(root));
    }

    [Fact]
    public void ValidateWorkspace_ReturnsFullExistingPath()
    {
        var current = Directory.GetCurrentDirectory();

        Assert.Equal(Path.GetFullPath(current), OpenCodeWorkspaceService.ValidateWorkspace(current));
    }

    [Fact]
    public void ResolveExecutable_PrefersExplicitPathOverBundledRuntime()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "OpenClicky-tests-" + Guid.NewGuid().ToString("N"));
        var runtimeDirectory = Path.Combine(baseDirectory, "runtime");
        var executableName = OperatingSystem.IsWindows() ? "opencode.exe" : "opencode";
        var bundled = Path.Combine(runtimeDirectory, executableName);
        var explicitPath = Path.Combine(baseDirectory, "explicit-" + executableName);
        Directory.CreateDirectory(runtimeDirectory);
        File.WriteAllText(bundled, "bundled");
        File.WriteAllText(explicitPath, "explicit");
        try
        {
            Assert.Equal(
                Path.GetFullPath(explicitPath),
                OpenCodeWorkspaceService.ResolveExecutable(explicitPath, baseDirectory));
        }
        finally
        {
            File.Delete(bundled);
            File.Delete(explicitPath);
            Directory.Delete(runtimeDirectory);
            Directory.Delete(baseDirectory);
        }
    }

    [Fact]
    public void ResolveExecutable_UsesAppBaseRuntimeWhenProcessDirectoryHasNone()
    {
        var baseDirectory = Path.Combine(Path.GetTempPath(), "OpenClicky-tests-" + Guid.NewGuid().ToString("N"));
        var runtimeDirectory = Path.Combine(baseDirectory, "runtime");
        var processDirectory = Path.Combine(baseDirectory, "process");
        var executableName = OperatingSystem.IsWindows() ? "opencode.exe" : "opencode";
        var bundled = Path.Combine(runtimeDirectory, executableName);
        var processPath = Path.Combine(processDirectory, OperatingSystem.IsWindows() ? "testhost.exe" : "testhost");
        Directory.CreateDirectory(runtimeDirectory);
        Directory.CreateDirectory(processDirectory);
        File.WriteAllText(bundled, "bundled");
        try
        {
            Assert.Equal(
                Path.GetFullPath(bundled),
                OpenCodeWorkspaceService.ResolveExecutable(null, baseDirectory, processPath));
        }
        finally
        {
            File.Delete(bundled);
            Directory.Delete(runtimeDirectory);
            Directory.Delete(processDirectory);
            Directory.Delete(baseDirectory);
        }
    }

    [Fact]
    public void ResolveExecutable_PrefersRuntimeBesideActualExecutableOverExtractionBase()
    {
        var root = Path.Combine(Path.GetTempPath(), "OpenClicky-tests-" + Guid.NewGuid().ToString("N"));
        var installedDirectory = Path.Combine(root, "installed");
        var extractedDirectory = Path.Combine(root, "extracted");
        var installedRuntimeDirectory = Path.Combine(installedDirectory, "runtime");
        var extractedRuntimeDirectory = Path.Combine(extractedDirectory, "runtime");
        var executableName = OperatingSystem.IsWindows() ? "opencode.exe" : "opencode";
        var installedRuntime = Path.Combine(installedRuntimeDirectory, executableName);
        var extractedRuntime = Path.Combine(extractedRuntimeDirectory, executableName);
        var processPath = Path.Combine(installedDirectory, OperatingSystem.IsWindows() ? "OpenClicky.exe" : "OpenClicky");
        Directory.CreateDirectory(installedRuntimeDirectory);
        Directory.CreateDirectory(extractedRuntimeDirectory);
        File.WriteAllText(processPath, "host");
        File.WriteAllText(installedRuntime, "installed runtime");
        File.WriteAllText(extractedRuntime, "extracted runtime");
        try
        {
            Assert.Equal(
                Path.GetFullPath(installedRuntime),
                OpenCodeWorkspaceService.ResolveExecutable(null, extractedDirectory, processPath));
        }
        finally
        {
            File.Delete(installedRuntime);
            File.Delete(extractedRuntime);
            File.Delete(processPath);
            Directory.Delete(installedRuntimeDirectory);
            Directory.Delete(extractedRuntimeDirectory);
            Directory.Delete(installedDirectory);
            Directory.Delete(extractedDirectory);
            Directory.Delete(root);
        }
    }

    [Fact]
    public void SupportedVersion_AcceptsPrivateRuntimeAndRejectsOlderGlobalRuntime()
    {
        OpenCodeWorkspaceService.EnsureSupportedVersion("1.18.0");

        var error = Assert.Throws<NotSupportedException>(
            () => OpenCodeWorkspaceService.EnsureSupportedVersion("1.17.18"));
        Assert.Contains("app-owned OpenCode 1.18.0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TerminalAssistantEvidence_RejectsBusyIdleWithoutAssistantResult()
    {
        var onlyUser = new[]
        {
            new OpenCodeMessageSummary("msg_user", "user", "Create demo.txt", null, null),
        };
        var incompleteAssistant = new[]
        {
            new OpenCodeMessageSummary("msg_ai", "assistant", "", null, null),
        };
        var emptyFinishedAssistant = new[]
        {
            new OpenCodeMessageSummary("msg_ai", "assistant", "", "stop", null),
        };
        var toolCallAssistant = new[]
        {
            new OpenCodeMessageSummary("msg_ai", "assistant", "Writing the file", "tool-calls", null),
        };
        var completedAssistant = new[]
        {
            new OpenCodeMessageSummary("msg_ai", "assistant", "Done", "stop", null),
        };

        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(onlyUser));
        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(incompleteAssistant));
        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(emptyFinishedAssistant));
        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(toolCallAssistant));
        Assert.True(OpenCodeWorkspaceService.HasTerminalAssistantMessage(completedAssistant));
    }

    [Fact]
    public void TerminalAssistantEvidence_UsesLatestAssistantStep()
    {
        var messages = new[]
        {
            new OpenCodeMessageSummary("msg_finished", "assistant", "Earlier result", "stop", null),
            new OpenCodeMessageSummary("msg_tool", "assistant", "Calling a tool", "tool-calls", null),
        };

        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(messages));
    }

    [Fact]
    public void TerminalAssistantEvidence_IgnoresAnAnswerOlderThanTheUsersLatestMessage()
    {
        // A steer posted as the engine finished: the finished answer predates it, so the run is not over yet.
        var messages = new[]
        {
            new OpenCodeMessageSummary("msg_user", "user", "Do the task", null, null),
            new OpenCodeMessageSummary("msg_ai", "assistant", "Done", "stop", null),
            new OpenCodeMessageSummary("msg_steer", "user", "Update from the user: use March", null, null),
        };

        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(messages));
        Assert.True(OpenCodeWorkspaceService.HasTerminalAssistantMessage(
            [.. messages, new OpenCodeMessageSummary("msg_ai2", "assistant", "Done with March", "stop", null)]));
    }

    [Fact]
    public void AssistantError_IsTerminalFailureEvidence()
    {
        var messages = new[]
        {
            new OpenCodeMessageSummary(
                "msg_ai",
                "assistant",
                "",
                null,
                "ProviderAuthError: Provider rejected the request"),
        };

        Assert.Equal(
            "ProviderAuthError: Provider rejected the request",
            OpenCodeWorkspaceService.GetAssistantError(messages));
        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(messages));
    }

    [Fact]
    public void AssistantFinishErrorWithoutDetails_IsTerminalFailureEvidence()
    {
        var messages = new[]
        {
            new OpenCodeMessageSummary("msg_ai", "assistant", "", "error", null),
        };

        Assert.Equal(
            "OpenCode returned an assistant error without details.",
            OpenCodeWorkspaceService.GetAssistantError(messages));
        Assert.False(OpenCodeWorkspaceService.HasTerminalAssistantMessage(messages));
    }

    [Fact]
    public void IdleWithoutTerminalEvidence_HasBoundedGracePeriod()
    {
        var idleSince = DateTimeOffset.Parse("2026-10-12T12:00:00Z");

        Assert.False(OpenCodeWorkspaceService.MissingTerminalTimedOut(
            idleSince,
            idleSince + OpenCodeWorkspaceService.MissingTerminalTimeout - TimeSpan.FromMilliseconds(1)));
        Assert.True(OpenCodeWorkspaceService.MissingTerminalTimedOut(
            idleSince,
            idleSince + OpenCodeWorkspaceService.MissingTerminalTimeout));
    }

    [Fact]
    public void MessageProgress_SkipsBlankAndUnchangedMessages()
    {
        var blank = new OpenCodeMessageSummary("msg_blank", "assistant", "", "stop", null);
        var visible = new OpenCodeMessageSummary("msg_visible", "assistant", "Done", "stop", null);

        Assert.False(OpenCodeWorkspaceService.ShouldReportMessage(null, blank));
        Assert.False(OpenCodeWorkspaceService.ShouldReportMessage("Done", visible));
        Assert.True(OpenCodeWorkspaceService.ShouldReportMessage(null, visible));
    }

    [Fact]
    public void StatusProgress_IncludesChangedRetryReasonAndSkipsBlankMessage()
    {
        var first = OpenCodeWorkspaceService.FormatStatusProgress(
            new OpenCodeSessionStatus("retry", " Provider busy; retrying in 2 seconds. "));
        var changed = OpenCodeWorkspaceService.FormatStatusProgress(
            new OpenCodeSessionStatus("retry", "Provider busy; retrying in 4 seconds."));
        var blank = OpenCodeWorkspaceService.FormatStatusProgress(
            new OpenCodeSessionStatus("busy", "   "));

        Assert.Equal("retry: Provider busy; retrying in 2 seconds.", first);
        Assert.Equal("retry: Provider busy; retrying in 4 seconds.", changed);
        Assert.NotEqual(first, changed);
        Assert.Equal("busy", blank);
    }

    [Fact]
    public void AnAnswerBeforeAnUnansweredSteerIsTheRunsAnswer()
    {
        OpenCodeMessageSummary[] steered =
        [
            new("msg_user", "user", "Do the task", null, null),
            new("msg_ai", "assistant", "Done", "stop", null),
            new("msg_steer", "user", "Update from the user: use March", null, null),
        ];
        Assert.True(OpenCodeWorkspaceService.HasAnswerBeforeLaterUserMessages(steered));
        Assert.False(OpenCodeWorkspaceService.HasAnswerBeforeLaterUserMessages([steered[0]])); // never answered
        Assert.False(OpenCodeWorkspaceService.HasAnswerBeforeLaterUserMessages([steered[0], steered[1]])); // nothing unanswered: the normal end
        Assert.False(OpenCodeWorkspaceService.HasAnswerBeforeLaterUserMessages(
            [steered[0], new("msg_ai", "assistant", "", "stop", "ProviderAuthError: no"), steered[2]])); // a failed answer is not one
    }
}
