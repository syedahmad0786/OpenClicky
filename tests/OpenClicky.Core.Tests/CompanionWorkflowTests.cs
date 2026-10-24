using OpenClicky.Core;
using OpenClicky.Core.Agent;
using OpenClicky.Providers;
using System.Text.Json;

namespace OpenClicky.Core.Tests;

public class CompanionWorkflowTests
{
    [Fact]
    public async Task ActionTagsAreStrippedFromSpeechAndHistoryAndRaisedOnce()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-actions-{Guid.NewGuid():N}.json");
        var tts = new RecordingTts();
        var messages = new List<string>();
        var raised = new List<IReadOnlyList<TalkAction>>();
        try
        {
            using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
                () => new Talk((_, _) => Task.FromResult("Opening it now. [OPEN:https://example.com] Copied. [COPY:hi there] [POINT:none]")), tts,
                () => new AppSettings { PersistConversationHistory = true, SpeakReplies = true }, log: _ => { }, historyPath: historyPath);
            talk.MessageReceived += (role, text) => { if (role == "OpenClicky") messages.Add(text); };
            talk.ActionsRequested += raised.Add;
            await talk.SubmitTextAsync("open example and copy hi");
            var saved = Assert.Single(ConversationHistoryStore.Load(historyPath, 100));
            Assert.Equal("Opening it now. Copied.", saved.assistant);
            Assert.Equal(["Opening it now. Copied."], messages);
            Assert.Equal("Opening it now. Copied.", string.Join(" ", tts.Spoken));
            var actions = Assert.Single(raised);
            Assert.Equal([new TalkAction(TalkActionKind.Open, "https://example.com"), new TalkAction(TalkActionKind.Copy, "hi there")], actions);
        }
        finally { ConversationHistoryStore.Clear(historyPath); }
    }

    [Fact]
    public async Task TaskReceiptIsAvailableInTheSameConversation()
    {
        IReadOnlyList<(string role, string text)> seen = [];
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new Talk((_, _) => Task.FromResult("The draft is in the selected field."), history => seen = history), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.ReportTaskResult("Type my draft", "Verified the draft in the field. No message was sent.");
        await talk.SubmitTextAsync("What did you do?");
        Assert.Contains(("user", "Type my draft"), seen);
        Assert.Contains(("assistant", "Verified the draft in the field. No message was sent."), seen);
    }

    [Theory]
    [InlineData(false, "Explain this screen", "Explain this screen")]
    [InlineData(true, "Find the button", "Guide me: Find the button")]
    public async Task ProviderFailurePersistsDisplayedTurnOnceWithoutLearningMemory(
        bool walkthrough, string request, string expectedSavedRequest)
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-failed-turn-{Guid.NewGuid():N}.json");
        var memoryPath = Path.Combine(Path.GetTempPath(), $"openclicky-failed-memory-{Guid.NewGuid():N}.json");
        try
        {
            using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
                () => new Talk((_, _) => throw new InvalidOperationException("synthetic provider failure")), new Tts(),
                () => new AppSettings { PersistConversationHistory = true }, log: _ => { },
                historyPath: historyPath, memoryPath: memoryPath);

            if (walkthrough) await talk.StartWalkthroughAsync(request);
            else await talk.SubmitTextAsync(request);

            var saved = Assert.Single(ConversationHistoryStore.Load(historyPath, 100));
            Assert.Equal(expectedSavedRequest, saved.user);
            Assert.Contains("synthetic provider failure", saved.assistant);
            Assert.Empty(CompanionMemoryStore.Load(memoryPath).Memory?.Facts ?? []);
        }
        finally
        {
            ConversationHistoryStore.Clear(historyPath, memoryPath);
            CompanionMemoryStore.Clear(memoryPath);
        }
    }

    [Fact]
    public void FailedTurnHonorsDisabledPersistence()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-disabled-failure-{Guid.NewGuid():N}.json");
        var memoryPath = Path.Combine(Path.GetTempPath(), $"openclicky-disabled-memory-{Guid.NewGuid():N}.json");
        try
        {
            using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
                () => null, new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
                historyPath: historyPath, memoryPath: memoryPath);
            talk.ReportLocalReply("request", "failure");
            Assert.Empty(ConversationHistoryStore.Load(historyPath, 100));
            Assert.Empty(CompanionMemoryStore.Load(memoryPath).Memory?.Facts ?? []);
        }
        finally
        {
            ConversationHistoryStore.Clear(historyPath, memoryPath);
            CompanionMemoryStore.Clear(memoryPath);
        }
    }

    [Fact]
    public async Task TypedLocalCommandPersistsExactlyOnceAndRestoresAfterRestart()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-local-turn-{Guid.NewGuid():N}.json");
        var memoryPath = Path.Combine(Path.GetTempPath(), $"openclicky-local-memory-{Guid.NewGuid():N}.json");
        var capture = new Capture();
        var messages = new List<(string Role, string Text)>();
        TalkOrchestrator? talk = null;
        try
        {
            talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
                () => throw new Exception("A local command must not create a provider"), new Tts(),
                () => new AppSettings { PersistConversationHistory = true },
                agentRunner: (_, _) => throw new Exception("A local command must not start Agent mode"), log: _ => { },
                requestInterceptor: (text, _) =>
                {
                    Assert.Equal(TaskVoiceCommandKind.Pause, TaskVoiceCommands.Parse(text).Kind);
                    talk!.ReportLocalReply(text, "There isn't an active desktop task to control.");
                    return Task.FromResult(true);
                }, historyPath: historyPath, memoryPath: memoryPath);
            talk.MessageReceived += (role, text) => messages.Add((role, text));

            await talk.SubmitLocalTextAsync("pause task");

            Assert.Equal(("You", "pause task"), messages[0]);
            Assert.Equal(("OpenClicky", "There isn't an active desktop task to control."), messages[1]);
            Assert.Equal(0, capture.Calls);
            Assert.Equal(0, capture.RegionCalls);
            Assert.Single(ConversationHistoryStore.Load(historyPath, 100));
            talk.Dispose();
            talk = null;

            var restored = new List<(string Role, string Text)>();
            using var restarted = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
                () => null, new Tts(), () => new AppSettings { PersistConversationHistory = true }, log: _ => { },
                historyPath: historyPath, memoryPath: memoryPath);
            restarted.MessageReceived += (role, text) => restored.Add((role, text));
            restarted.Start();
            Assert.Equal(new[] { ("You", "pause task"), ("OpenClicky", "There isn't an active desktop task to control.") }, restored);
        }
        finally
        {
            talk?.Dispose();
            ConversationHistoryStore.Clear(historyPath, memoryPath);
            CompanionMemoryStore.Clear(memoryPath);
        }
    }

    [Fact]
    public async Task ExplicitNonRetainedDemoDoesNotReachDurableHistory()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-demo-turn-{Guid.NewGuid():N}.json");
        var memoryPath = Path.Combine(Path.GetTempPath(), $"openclicky-demo-memory-{Guid.NewGuid():N}.json");
        try
        {
            using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
                () => new Talk((_, _) => Task.FromResult("Synthetic demo reply. [POINT:none]")), new Tts(),
                () => new AppSettings { PersistConversationHistory = true, SpeakReplies = true }, log: _ => { },
                historyPath: historyPath, memoryPath: memoryPath);

            await talk.SubmitTextAsync("Synthetic demo request", remember: false);

            Assert.Empty(ConversationHistoryStore.Load(historyPath, 100));
        }
        finally
        {
            ConversationHistoryStore.Clear(historyPath, memoryPath);
            CompanionMemoryStore.Clear(memoryPath);
        }
    }

    [Fact]
    public async Task RegionAndWalkthroughRepliesUseDurableConversationHistory()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-guided-turn-{Guid.NewGuid():N}.json");
        var memoryPath = Path.Combine(Path.GetTempPath(), $"openclicky-guided-memory-{Guid.NewGuid():N}.json");
        var capture = new Capture { Region = new([], 100, 50, 0, 0, 200, 100, true, "selected") };
        var calls = 0;
        try
        {
            using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
                () => new Talk((_, _) => Task.FromResult(++calls == 1
                    ? "The selected area contains a form. [POINT:none]"
                    : "[GUIDE:step] Choose the highlighted field. [POINT:none]")), new Tts(),
                () => new AppSettings { PersistConversationHistory = true, SpeakReplies = true }, log: _ => { },
                historyPath: historyPath, memoryPath: memoryPath);

            await talk.SubmitRegionAsync("Explain selection", (0, 0, 200, 100));
            await talk.StartWalkthroughAsync("Find button");

            var saved = ConversationHistoryStore.Load(historyPath, 100);
            Assert.Equal(2, saved.Count);
            Assert.Equal("Explain selection", saved[0].user);
            Assert.Equal("Guide me: Find button", saved[1].user);
        }
        finally
        {
            ConversationHistoryStore.Clear(historyPath, memoryPath);
            CompanionMemoryStore.Clear(memoryPath);
        }
    }
    [Fact]
    public async Task Walkthrough_VoiceCannotFallThroughToAgentOrTaskInterceptor()
    {
        var live = new LiveSpeech("open Notepad");
        var actions = 0;
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => live,
            () => new Talk((_, _) => Task.FromResult("[GUIDE:step] Click green. [POINT:none]")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true },
            agentRunner: (_, _) => { actions++; return Task.CompletedTask; }, log: _ => { },
            requestInterceptor: (_, _) => throw new Exception("No unrelated task commands during guide"));
        await talk.StartWalkthroughAsync("Find green");
        talk.BeginListening();
        await talk.EndListeningAsync();
        Assert.True(talk.Walkthrough.View.Active);
        Assert.Equal(1, talk.Walkthrough.View.Step);
        Assert.Equal(0, actions);
    }

    [Fact]
    public async Task Walkthrough_NavigationCapturesAgainAndDoesNotInvokeAgent()
    {
        var capture = new Capture { Region = new([], 100, 50, 0, 0, 200, 100, true, "fresh window") };
        var overlay = new Overlay();
        var calls = 0;
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, capture, new Mic(), () => null,
            () => new Talk((text, token) => Task.FromResult(++calls == 1
                ? "[GUIDE:step] Click green. [MARK:circle:10,10,20,20:green] [POINT:20,20:green]"
                : "[GUIDE:wait] The result is not visible. [POINT:none]")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true },
            agentRunner: (_, _) => throw new Exception("Guides must never execute actions"), log: _ => { },
            windowBounds: () => (0, 0, 200, 100));
        await talk.StartWalkthroughAsync("open Slack then type a draft");
        Assert.Equal(1, talk.Walkthrough.View.Step);
        Assert.DoesNotContain("MARK:", talk.Walkthrough.View.Instruction);
        await talk.MoveWalkthroughAsync(WalkthroughMove.Next);
        Assert.Equal(0, capture.Calls);
        Assert.Equal(2, capture.RegionCalls);
        Assert.Equal(1, talk.Walkthrough.View.Step);
        Assert.False(talk.Walkthrough.View.Busy);
        talk.StopWalkthrough();
        Assert.False(talk.Walkthrough.View.Active);
    }

    [Fact]
    public async Task Walkthrough_ChangedHandleWithSameTitleDiscardsReplyAndAllowsRetry()
    {
        nint handle = 1;
        var overlay = new Overlay();
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, new Capture(), new Mic(), () => null,
            () => new Talk((_, _) => { handle = 2; return Task.FromResult("[GUIDE:step] Stale. [POINT:20,20:old]"); }),
            new Tts(), () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true },
            log: _ => { }, windowTitle: () => "Same title", windowHandle: () => handle);
        await talk.StartWalkthroughAsync("Find the field");
        Assert.Null(overlay.LastPoint);
        Assert.Equal(0, talk.Walkthrough.View.Step);
        Assert.False(talk.Walkthrough.View.Busy);
        Assert.True(talk.Walkthrough.View.Active);
    }

    [Fact]
    public async Task Walkthrough_StopDuringProviderDropsLateResponse()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var overlay = new Overlay();
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, new Capture(), new Mic(), () => null,
            () => new Talk((_, _) => { entered.SetResult(); return reply.Task; }), new Tts(),
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { });
        var run = talk.StartWalkthroughAsync("Find green");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        talk.StopWalkthrough();
        reply.SetResult("[GUIDE:step] Old reply. [POINT:20,20:old]");
        await run;
        Assert.False(talk.Walkthrough.View.Active);
        Assert.Null(overlay.LastPoint);
    }

    [Fact]
    public async Task Conversation_WithMarksRearmsAfterSpeechWithoutEightSecondHold()
    {
        var listeners = new List<LiveSpeech>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech("point there"); listeners.Add(listener); return listener; },
            () => new Talk((_, _) => Task.FromResult("Here. [MARK:circle:10,10,20,20:green] [POINT:20,20:green]")),
            new Tts(), () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { });
        talk.StartConversation();
        await talk.EndListeningAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, listeners.Count);
        Assert.Equal(CompanionVoiceState.Listening, talk.State);
        talk.StopConversation();
    }

    [Fact]
    public async Task ActiveWindowCapture_UsesOnlyFreshRelevantRegionAndMapsItsPoint()
    {
        var capture = new Capture { Region = new([], 100, 50, 300, 400, 200, 100, false, "detail") };
        var overlay = new Overlay();
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, capture, new Mic(), () => null,
            () => new Talk((text, token) => Task.FromResult("Here it is. [POINT:50,25:field:screen1]"), inspectScreens: screens =>
            {
                var screen = Assert.Single(screens);
                Assert.Contains("current app only", screen.Label);
                Assert.Contains(":screen1", screen.Label);
            }), new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowTitle: () => "Claude", windowBounds: () => (300, 400, 200, 100));
        await talk.SubmitTextAsync(AppKnowledge.ScreenHelpRequest, false);
        Assert.Equal(0, capture.Calls);
        Assert.Equal(1, capture.RegionCalls);
        Assert.Equal((400, 450), overlay.LastPoint);
    }

    [Theory]
    [InlineData(AppKnowledge.ScreenHelpRequest, 0)]
    [InlineData(AppKnowledge.WebHelpPrefix + "Claude projects", 0)]
    [InlineData("Explain that further", 2)]
    public async Task ScreenOrientation_DoesNotReuseOldTasks_OrdinaryFollowupsKeepHistory(string request, int expectedHistory)
    {
        var counts = new List<int>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new Talk((text, token) => Task.FromResult("A reply. [POINT:none]"), history => counts.Add(history.Count)),
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        await talk.SubmitTextAsync("An earlier sandbox task");
        await talk.SubmitTextAsync(request, false);
        Assert.Equal(new[] { 0, expectedHistory }, counts);
    }

    [Fact]
    public async Task LiveSpeech_PreviewsWhileListening_AndSkipsRecordedWavTranscription()
    {
        var live = new LiveSpeech("Circle the button");
        var mic = new Mic();
        var previews = new List<string>();
        var overlay = new Overlay();
        var capture = new Capture { Region = new([], 100, 50, 0, 0, 200, 100, true, "fresh window") };
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, capture, mic, () => live,
            () => new Talk((text, token) => Task.FromResult("Here it is. [MARK:circle:10,10,30,20:button] [POINT:25,20:button]")),
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowBounds: () => (0, 0, 200, 100));
        talk.TranscriptChanged += previews.Add;
        talk.BeginListening();
        live.Emit("Circle the");
        Assert.Contains("Circle the", previews);
        await talk.EndListeningAsync();
        Assert.Equal(0, mic.Starts);
        Assert.Equal(0, capture.Calls);
        Assert.Equal(1, capture.RegionCalls);
        Assert.Single(overlay.Marks);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task EmptyLiveSpeech_OffersScreenHelpAndNeverRunsAgent()
    {
        var requests = new List<string>();
        var messages = new List<(string,string)>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => new LiveSpeech(""),
            () => new Talk((text, token) => { requests.Add(text); return Task.FromResult("This is a project screen. [POINT:none]"); }),
            new Tts(), () => new AppSettings { PersistConversationHistory = false },
            agentRunner: (text, ct) => throw new Exception("Silent requests must never run Agent"), log: _ => { });
        talk.MessageReceived += (role, text) => messages.Add((role,text));
        talk.BeginListening();
        await talk.EndListeningAsync();
        Assert.Equal(AppKnowledge.ScreenHelpRequest, Assert.Single(requests));
        Assert.DoesNotContain(messages, m => m.Item1 == "You");
        Assert.Contains(messages, m => m.Item2.Contains("No words were recognized"));
    }

    [Fact]
    public async Task ChangedWindow_DiscardsOldAnswerAndMarks()
    {
        var title = "ChatGPT";
        var overlay = new Overlay();
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, new Capture(), new Mic(), () => null,
            () => new Talk((text, token) => { title = "Claude"; return Task.FromResult("Old target. [MARK:circle:10,10,30,20:old] [POINT:25,20:old]"); }),
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { }, windowTitle: () => title);
        talk.MessageReceived += (_, text) => messages.Add(text);
        await talk.SubmitTextAsync("show projects", false);
        Assert.Null(overlay.LastPoint);
        Assert.Empty(overlay.Marks);
        Assert.DoesNotContain("Old target.", messages);
        Assert.Contains(messages, m => m.Contains("window changed"));
    }

    [Fact]
    public async Task CancelDuringLiveSpeechCompletion_ReturnsToIdle()
    {
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => new LiveSpeech("", completion.Task),
            () => throw new Exception("Cancelled speech must not call AI"), new Tts(), () => new AppSettings(), log: _ => { });
        talk.BeginListening();
        var pending = talk.EndListeningAsync();
        talk.Cancel();
        await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task Conversation_EndsUtteranceAndListensAgainAfterReply()
    {
        var listeners = new List<LiveSpeech>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech("open Slack"); listeners.Add(listener); return listener; },
            () => new Talk((_, _) => Task.FromResult("Okay [POINT:none]")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { });

        talk.StartConversation();
        listeners[0].EndUtterance();

        await WaitUntilAsync(() => listeners.Count == 2 && talk.State == CompanionVoiceState.Listening);
        Assert.True(talk.ConversationActive);
        talk.StopConversation();
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
        Assert.False(talk.ConversationActive);
    }

    [Fact]
    public void Conversation_MicrophoneFailureStopsSession()
    {
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => throw new InvalidOperationException("unavailable"), () => null, new Tts(), () => new AppSettings(), log: _ => { });

        talk.StartConversation();

        Assert.False(talk.ConversationActive);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public void Conversation_RuntimeMicrophoneFailureStopsWithoutRetry()
    {
        var listeners = new List<LiveSpeech>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech(""); listeners.Add(listener); return listener; },
            () => null, new Tts(), () => new AppSettings(), log: _ => { });

        talk.StartConversation();
        listeners[0].Fail(new InvalidOperationException("disconnected"));

        Assert.Single(listeners);
        Assert.False(talk.ConversationActive);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task Conversation_StopDuringThinkingDoesNotRestart()
    {
        var listeners = new List<LiveSpeech>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech("hello"); listeners.Add(listener); return listener; },
            () => new Talk(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return "unreachable"; }),
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });

        talk.StartConversation();
        listeners[0].EndUtterance();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        talk.StopConversation();
        await Task.Delay(50);

        Assert.Single(listeners);
        Assert.False(talk.ConversationActive);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task Interceptor_CanStopConversationAfterSavingWithoutCancellingItself()
    {
        var live = new LiveSpeech("saved words");
        TalkOrchestrator? talk = null;
        var interceptorCompleted = false;
        talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => live,
            () => throw new Exception("Handled dictation must not call the model"), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            requestInterceptor: (_, token) =>
            {
                talk!.StopConversationAfterCurrentTurn();
                Assert.False(token.IsCancellationRequested);
                interceptorCompleted = true;
                return Task.FromResult(true);
            });

        talk.StartConversation();
        live.EndUtterance();
        await WaitUntilAsync(() => interceptorCompleted && talk.State == CompanionVoiceState.Idle);

        Assert.False(talk.ConversationActive);
        talk.Dispose();
    }

    [Fact]
    public async Task Conversation_StaleWindowCancellationRearmsWithoutOldTurnResettingIt()
    {
        var listeners = new List<LiveSpeech>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech("what is here"); listeners.Add(listener); return listener; },
            () => new Talk(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return "unreachable"; }),
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });

        talk.StartConversation();
        listeners[0].EndUtterance();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        talk.CancelCurrentTurn();
        await WaitUntilAsync(() => listeners.Count == 2 && talk.State == CompanionVoiceState.Listening);
        await Task.Delay(50);

        Assert.True(talk.ConversationActive);
        Assert.Equal(CompanionVoiceState.Listening, talk.State);
        talk.StopConversation();
    }

    [Fact]
    public async Task Conversation_VerifiedEchoCancelledSpeechInterruptsAndKeepsInitialAudio()
    {
        var listeners = new List<LiveSpeech>();
        var requests = new List<string>();
        var tts = new InterruptibleTts();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech(listeners.Count == 0 ? "first" : "don't delete", echoCancelled: listeners.Count > 0); listeners.Add(listener); return listener; },
            () => new Talk((text, _) => { requests.Add(text); return Task.FromResult("Answer. [POINT:none]"); }),
            tts, () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { }, canInterruptSpeech: () => true);

        talk.StartConversation();
        var firstTurn = talk.EndListeningAsync();
        await tts.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, listeners.Count);

        listeners[1].StartSpeech();
        Assert.True(tts.Stopped);
        Assert.Equal(CompanionVoiceState.Listening, talk.State);
        talk.StopConversationAfterCurrentTurn();
        listeners[1].EndUtterance();

        await WaitUntilAsync(() => requests.Count == 2 && talk.State == CompanionVoiceState.Idle);
        await firstTurn.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first", "don't delete"], requests);
        Assert.Equal(2, listeners.Count);
    }

    [Fact]
    public async Task Conversation_UnverifiedCaptureCannotInterruptPlayback()
    {
        var ordinary = new List<LiveSpeech>();
        var interruptions = new List<LiveSpeech>();
        var tts = new InterruptibleTts();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech("first"); ordinary.Add(listener); return listener; },
            () => new Talk((_, _) => Task.FromResult("Answer. [POINT:none]")), tts,
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { },
            canInterruptSpeech: () => true,
            interruptionSttFactory: () => { var listener = new LiveSpeech("must not replace ordinary capture"); interruptions.Add(listener); return listener; });

        talk.StartConversation();
        var turn = talk.EndListeningAsync();
        await tts.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Single(ordinary);
        var rejected = Assert.Single(interruptions);
        Assert.True(rejected.Disposed);
        rejected.StartSpeech();

        Assert.False(tts.Stopped);
        Assert.NotEqual(CompanionVoiceState.Listening, talk.State);
        talk.StopConversation();
        await turn.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Conversation_SynchronousSpeechEventCannotRaceUnverifiedAecProbe()
    {
        var listeners = new List<LiveSpeech>();
        var tts = new InterruptibleTts();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech("first", speechOnStart: listeners.Count > 0); listeners.Add(listener); return listener; },
            () => new Talk((_, _) => Task.FromResult("Answer. [POINT:none]")), tts,
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { }, canInterruptSpeech: () => true);

        talk.StartConversation();
        var turn = talk.EndListeningAsync();
        await tts.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(tts.Stopped);
        Assert.NotEqual(CompanionVoiceState.Listening, talk.State);
        talk.StopConversation();
        await turn.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Conversation_AdoptedInterruptionFailureStopsSession()
    {
        var listeners = new List<LiveSpeech>();
        var tts = new InterruptibleTts();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(),
            () => { var listener = new LiveSpeech("first", echoCancelled: listeners.Count > 0); listeners.Add(listener); return listener; },
            () => new Talk((_, _) => Task.FromResult("Answer. [POINT:none]")), tts,
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { }, canInterruptSpeech: () => true);

        talk.StartConversation();
        var turn = talk.EndListeningAsync();
        await tts.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        listeners[1].StartSpeech();
        listeners[1].Fail(new InvalidOperationException("disconnected"));

        await turn.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(talk.ConversationActive);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class LiveSpeech(string text, Task<string>? completion = null, bool echoCancelled = false, bool speechOnStart = false) : ILiveSttClient
    {
        public event Action<string>? TranscriptChanged;
        public event Action? SpeechStarted;
        public event Action? UtteranceEnded;
        public event Action<Exception>? ListeningFailed;
        public bool SupportsEchoCancelledInterruption => echoCancelled;
        public bool Disposed { get; private set; }
        public void Emit(string partial) => TranscriptChanged?.Invoke(partial);
        public void StartSpeech() => SpeechStarted?.Invoke();
        public void EndUtterance() => UtteranceEnded?.Invoke();
        public void Fail(Exception error) => ListeningFailed?.Invoke(error);
        public void StartListening() { if (speechOnStart) SpeechStarted?.Invoke(); }
        public Task<string> FinishListeningAsync(CancellationToken token) => completion is null ? Task.FromResult(text) : completion.WaitAsync(token);
        public void CancelListening() { }
        public void Dispose() => Disposed = true;
        public Task<string> TranscribeAsync(byte[] wav, CancellationToken token = default) => throw new Exception("Must use live speech");
    }
    [Fact]
    public async Task TypedRequest_BypassesMicrophoneAndReportsReply()
    {
        var mic = new Mic();
        var capture = new Capture();
        var overlay = new Overlay();
        var messages = new List<string>();
        capture.Region = new([], 100, 50, 0, 0, 200, 100, true, "fresh window");
        using var talk = new TalkOrchestrator(new Hotkey(), overlay, capture, mic, () => throw new Exception("STT must not run"),
            () => new Talk((text, token) => Task.FromResult("Found it. [POINT:50,25:button]")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowBounds: () => (0, 0, 200, 100));
        talk.MessageReceived += (_, text) => messages.Add(text);
        await talk.SubmitTextAsync("Find the button", remember: false);
        Assert.Equal(0, mic.Starts);
        Assert.Equal(0, capture.Calls);
        Assert.Equal(1, capture.RegionCalls);
        Assert.Contains("Found it.", messages);
        Assert.Equal((100, 50), overlay.LastPoint);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task StreamingReply_NeverSpeaksOrShowsActionTags()
    {
        var tts = new RecordingTts();
        var partials = new List<string>();
        var raised = 0;
        const string tag = @"[COPY:a \] b]";
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new StreamingTalk(["Done.", @"Done. [COPY:a \] b", "Done. " + tag + " Next.", "Done. " + tag + " Next."]),
            tts, () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { });
        talk.AssistantResponseChanged += (text, complete) => { if (!complete) partials.Add(text); };
        talk.ActionsRequested += _ => raised++;

        await talk.SubmitTextAsync("copy it", false);

        Assert.Equal(["Done.", "Next."], tts.Spoken);
        Assert.All(partials, text => Assert.DoesNotContain("COPY", text));
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task StreamingReply_SpeaksCompletedSentencesInOrderWithoutCommandTags()
    {
        var tts = new RecordingTts();
        var partials = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new StreamingTalk(["First sentence.", "First sentence. Second", "First sentence. Second sentence. [POINT:", "First sentence. Second sentence. [POINT:none]"]),
            tts, () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { });
        talk.AssistantResponseChanged += (text, complete) => { if (!complete) partials.Add(text); };

        await talk.SubmitTextAsync("help", false);

        Assert.Equal(["First sentence.", "Second sentence."], tts.Spoken);
        Assert.All(partials, text => Assert.DoesNotContain("POINT", text));
        Assert.Equal(1, tts.MaximumConcurrent);
    }

    [Fact]
    public async Task StreamingReply_WithholdsPartialArtifactPayloadFromSpeechAndChat()
    {
        var tts = new RecordingTts();
        var partials = new List<string>();
        var updates = new[]
        {
            "Here is the chart.",
            "Here is the chart.\n```art",
            "Here is the chart.\n```artifact\n{\"kind\":\"chart\",\"id\":\"one\",",
            "Here is the chart.\n```artifact\n{\"kind\":\"chart\",\"id\":\"one\",\"title\":\"Plan\",\"series\":[{\"label\":\"Build\",\"value\":2}]}\n```\n[POINT:none]",
        };
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new StreamingTalk(updates), tts,
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { });
        talk.AssistantResponseChanged += (text, complete) => { if (!complete) partials.Add(text); };

        await talk.SubmitTextAsync("show a chart", false);

        Assert.Equal(["Here is the chart."], tts.Spoken);
        Assert.All(partials, text =>
        {
            Assert.DoesNotContain("artifact", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("kind", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("POINT", text, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task StreamingReply_WindowSwitchWithholdsLaterPartialsAndQueuedSpeech()
    {
        var title = "Original app";
        var partials = new List<string>();
        var messages = new List<string>();
        var tts = new GatedRecordingTts();
        var stream = new ControlledWindowSwitchStream();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => stream,
            tts, () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { },
            windowTitle: () => Volatile.Read(ref title));
        talk.AssistantResponseChanged += (text, complete) => { if (!complete) partials.Add(text); };
        talk.MessageReceived += (role, text) => { if (role == "OpenClicky") messages.Add(text); };

        var turn = talk.SubmitTextAsync("help", false);
        await stream.TwoUpdatesSent.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Volatile.Write(ref title, "Different app");
        tts.ReleaseFirst();
        stream.Continue.TrySetResult();
        await turn;

        Assert.Equal(2, partials.Count);
        Assert.DoesNotContain(partials, text => text.Contains("Stale", StringComparison.Ordinal));
        Assert.Equal(["First sentence."], tts.Spoken);
        Assert.Contains(messages, text => text.Contains("window changed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(messages, text => text.Contains("Stale", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StreamingReply_SpeechFailureDoesNotFailSuccessfulTextTurn()
    {
        var messages = new List<string>();
        var logs = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new StreamingTalk(["Valid answer."]), new ThrowingTts(),
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: logs.Add);
        talk.MessageReceived += (role, text) => { if (role == "OpenClicky") messages.Add(text); };

        await talk.SubmitTextAsync("help", false);

        Assert.Single(messages, "Valid answer.");
        Assert.DoesNotContain(messages, text => text.Contains("Couldn't finish", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(logs, text => text == "speech_error=InvalidOperationException");
    }

    [Fact]
    public async Task ExcludedWindow_StopsBeforeAnyCaptureOrProviderCall()
    {
        var capture = new Capture { Region = new([], 100, 50, 0, 0, 100, 50, true, "must not capture") };
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
            () => new Talk((_, _) => throw new Exception("Excluded window must not call provider")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowTitle: () => "1Password", windowBounds: () => (0, 0, 100, 50));
        talk.MessageReceived += (_, text) => messages.Add(text);

        await talk.SubmitTextAsync("what is here", false);

        Assert.Equal(0, capture.Calls);
        Assert.Equal(0, capture.RegionCalls);
        Assert.Contains(messages, text => text.Contains("excluded", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingWindowBounds_SendsNoImageAndStillReturnsTextAnswer()
    {
        var capture = new Capture();
        IReadOnlyList<ScreenCaptureResult>? sent = null;
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
            () => new Talk((_, _) => Task.FromResult("Text-only answer. [POINT:none]"), inspectScreens: screens => sent = screens),
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);

        await talk.SubmitTextAsync("explain generally", false);

        Assert.NotNull(sent);
        Assert.Empty(sent!);
        Assert.Equal(0, capture.Calls);
        Assert.Equal(0, capture.RegionCalls);
        Assert.Contains("Text-only answer.", messages);
    }

    [Fact]
    public async Task CancelCurrentTurn_StopsAnActiveStreamAndItsSpeech()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tts = new BlockingTts();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new BlockingStreamingTalk(entered), tts,
            () => new AppSettings { PersistConversationHistory = false, SpeakReplies = true }, log: _ => { });
        var completedReplies = new List<string>();
        talk.MessageReceived += (speaker, text) => { if (speaker == "OpenClicky") completedReplies.Add(text); };

        var turn = talk.SubmitTextAsync("help", false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        talk.CancelCurrentTurn();
        await turn.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.True(tts.Stopped);
        Assert.Empty(completedReplies);
    }

    [Fact]
    public async Task CancelledProviderTurnDoesNotPersistDisplayedRequestOrPartialReply()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-cancelled-turn-{Guid.NewGuid():N}.json");
        var memoryPath = Path.Combine(Path.GetTempPath(), $"openclicky-cancelled-memory-{Guid.NewGuid():N}.json");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
                () => new BlockingStreamingTalk(entered), new Tts(),
                () => new AppSettings { PersistConversationHistory = true, SpeakReplies = false }, log: _ => { },
                historyPath: historyPath, memoryPath: memoryPath);
            var turn = talk.SubmitTextAsync("cancel this request");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            talk.CancelCurrentTurn();
            await turn.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Empty(ConversationHistoryStore.Load(historyPath, 100));
            Assert.Empty(CompanionMemoryStore.Load(memoryPath).Memory?.Facts ?? []);
        }
        finally
        {
            ConversationHistoryStore.Clear(historyPath, memoryPath);
            CompanionMemoryStore.Clear(memoryPath);
        }
    }

    [Fact]
    public async Task RichReply_RendersDocumentButKeepsFenceOutOfChat()
    {
        const string reply = "Here is the plan. [POINT:none]\n```ag-ui\n{\"type\":\"CUSTOM\",\"name\":\"openclicky.ui.document\",\"value\":{\"version\":1,\"title\":\"Plan\",\"summary\":\"Two steps\",\"nodes\":[],\"actions\":[]}}\n```";
        var messages = new List<string>();
        IntelligentUiDocument? document = null;
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => new Talk((_, _) => Task.FromResult(reply)), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);
        talk.IntelligentUiChanged += value => document = value;

        await talk.SubmitTextAsync("show a plan", false);

        Assert.Equal("Plan", document?.Title);
        Assert.Contains("Here is the plan.", messages);
        Assert.DoesNotContain(messages, text => text.Contains("ag-ui", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NewRequest_CancelsOldTurnWithoutPublishingItsReply()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var messages = new List<string>();
        var client = new Talk(async (text, token) =>
        {
            if (text == "old") { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return "new reply [POINT:none]";
        });
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => client, new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);
        var old = talk.SubmitTextAsync("old", false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await talk.SubmitTextAsync("new", false);
        await old;
        Assert.Single(messages, m => m == "new reply");
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
    }

    [Fact]
    public async Task MissingProvider_GivesAnActionableMessageWithoutCapturingScreen()
    {
        var capture = new Capture();
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null, () => null,
            new Tts(), () => new AppSettings(), log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);
        await talk.SubmitTextAsync("help", false);
        Assert.Contains(messages, m => m.Contains("Continue with ChatGPT"));
        Assert.Equal(0, capture.Calls);
    }

    [Fact]
    public async Task CalculatorArtifactRequestEndsSystemInstructionsWithRendererAuthorityDespiteStaleRefusalHistory()
    {
        var historyPath = Path.Combine(Path.GetTempPath(), $"openclicky-artifact-prompt-{Guid.NewGuid():N}.json");
        try
        {
            ConversationHistoryStore.Append("make a calculator", "I can't create artifacts from this session.", historyPath);
            string? capturedSystem = null;
            IReadOnlyList<(string role, string text)>? capturedHistory = null;
            var client = new Talk((_, _) => Task.FromResult("done [POINT:none]"),
                history => capturedHistory = history, observeSystem: system => capturedSystem = system);
            using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
                () => client, new Tts(), () => new AppSettings { PersistConversationHistory = true }, log: _ => { },
                windowTitle: () => "OpenClicky Sandbox guided-check window", historyPath: historyPath);
            talk.Start();

            await talk.SubmitTextAsync("Create an interactive calculator named DEMO-20261012 effort calculator with editable Research 3 Build 5 Test 2 and show the sum in our visual workspace", false);

            Assert.NotNull(capturedSystem);
            Assert.Empty(capturedHistory!);
            Assert.Contains("quoted untrusted JSON data", capturedSystem, StringComparison.Ordinal);
            var quotedStart = capturedSystem!.IndexOf('[', capturedSystem.IndexOf("quoted untrusted JSON data", StringComparison.Ordinal));
            var quotedEnd = capturedSystem.LastIndexOf('\n', capturedSystem.LastIndexOf(VisualArtifactReply.Instructions, StringComparison.Ordinal) - 1);
            using var quotedHistory = JsonDocument.Parse(capturedSystem[quotedStart..quotedEnd]);
            Assert.Contains(quotedHistory.RootElement.EnumerateArray(), turn =>
                turn.GetProperty("role").GetString() == "assistant"
                && turn.GetProperty("text").GetString() == "I can't create artifacts from this session.");
            Assert.Contains("Emitting that block is the creation mechanism", capturedSystem, StringComparison.Ordinal);
            Assert.True(capturedSystem!.LastIndexOf(VisualArtifactReply.Instructions, StringComparison.Ordinal)
                > capturedSystem.IndexOf("OpenClicky Sandbox", StringComparison.Ordinal));
            Assert.EndsWith(VisualArtifactReply.Instructions, capturedSystem, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(historyPath);
            File.Delete(Path.ChangeExtension(historyPath, ".db"));
        }
    }

    [Fact]
    public async Task SelfContainedVisualUsesRendererRouteWithoutWindowCaptureOrPointActuation()
    {
        var windowReads = 0;
        string? capturedSystem = null;
        var capture = new Capture();
        var overlay = new Overlay();
        TalkOrchestrator? talk = null;
        bool? requiredScreenDuringProviderCall = null;
        var client = new Talk((_, _) =>
        {
            requiredScreenDuringProviderCall = talk!.CurrentTurnRequiresScreen;
            return Task.FromResult("Created.\n```artifact\n{\"kind\":\"calculator\",\"id\":\"effort\",\"title\":\"Effort\",\"fields\":[{\"id\":\"research\",\"label\":\"Research\",\"value\":3,\"min\":0,\"max\":100,\"step\":1}]}\n```\n[POINT:20,20:ignore]\n[MARK:rect:1,1,10,10:ignore:screen1]");
        },
            observeSystem: system => capturedSystem = system);
        JsonElement? artifact = null;
        using (talk = new TalkOrchestrator(new Hotkey(), overlay, capture, new Mic(), () => null,
            () => client, new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowTitle: () => { windowReads++; throw new InvalidOperationException(); },
            windowBounds: () => { windowReads++; throw new InvalidOperationException(); },
            windowHandle: () => { windowReads++; throw new InvalidOperationException(); }))
        {
        talk.VisualArtifactChanged += value => artifact = value;

        await talk.SubmitTextAsync("Create an interactive calculator with Research 3", false);

        Assert.False(requiredScreenDuringProviderCall);
        Assert.True(talk.CurrentTurnRequiresScreen);
        Assert.Equal(0, windowReads);
        Assert.Equal(0, capture.Calls + capture.RegionCalls);
        Assert.Contains("creating a supported interactive visual", capturedSystem, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("element pointing", capturedSystem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("calculator", artifact!.Value.GetProperty("kind").GetString());
        Assert.Null(overlay.LastPoint);
        Assert.Empty(overlay.Marks);
        }
    }

    [Fact]
    public async Task ArtifactOnlyReplyUsesTruthfulNeutralSpeechAndNoRoleHistory()
    {
        IReadOnlyList<(string role, string text)>? providerHistory = null;
        string? providerSystem = null;
        var client = new Talk((_, _) => Task.FromResult("```artifact\n{\"kind\":\"scene3d\",\"id\":\"cube\",\"title\":\"Cube\",\"shape\":\"cube\",\"color\":\"#25664c\",\"rotation\":24}\n```"),
            history => providerHistory = history, observeSystem: system => providerSystem = system);
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => client, new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.ReportLocalReply("older request", "I cannot create visuals here.");
        talk.MessageReceived += (role, text) => { if (role == "OpenClicky") messages.Add(text); };

        await talk.SubmitTextAsync("Create a rotatable 3D cube named Demo", false);

        Assert.Empty(providerHistory!);
        Assert.Contains("I cannot create visuals here.", providerSystem, StringComparison.Ordinal);
        Assert.Contains("I prepared the interactive visual.", messages);
        Assert.DoesNotContain(IntelligentUiParser.SafeFallbackText, messages);
    }

    [Fact]
    public async Task ScreenReferencedVisualStillRequiresCapture()
    {
        var capture = new Capture();
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
            () => new Talk((_, _) => Task.FromResult("unreachable")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowTitle: () => "Fixture", windowBounds: () => (0, 0, 100, 50), windowHandle: () => 42);
        talk.MessageReceived += (_, text) => messages.Add(text);

        await talk.SubmitTextAsync("Create a chart from this screenshot", false);

        Assert.Equal(1, capture.RegionCalls);
        Assert.Contains(messages, text => text.Contains("could not be captured", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SelectedRegionKeepsExplicitVisualOnCapturePath()
    {
        var capture = new Capture();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
            () => new Talk((_, _) => Task.FromResult("unreachable")), new Tts(),
            () => new AppSettings { PersistConversationHistory = false }, log: _ => { });

        await talk.SubmitRegionAsync("Create an interactive calculator with Research 3", (0, 0, 100, 50));

        Assert.Equal(1, capture.RegionCalls);
    }

    [Fact]
    public async Task ExcludedOwnForegroundRefusesBeforeBoundsCaptureOrProvider()
    {
        var boundsReads = 0;
        var providerCalls = 0;
        var capture = new Capture();
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
            () => { providerCalls++; return new Talk((_, _) => Task.FromResult("unreachable")); },
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowTitle: () => "", windowBounds: () => { boundsReads++; return (0, 0, 100, 50); },
            windowHandle: () => 42, foregroundCaptureEligible: () => false);
        talk.MessageReceived += (role, text) => { if (role == "OpenClicky") messages.Add(text); };

        await talk.SubmitTextAsync("Explain what is on this screen", false);

        Assert.Equal(0, boundsReads);
        Assert.Equal(0, capture.Calls + capture.RegionCalls);
        Assert.Equal(0, providerCalls);
        Assert.Contains(messages, text => text.Contains("Bring the app you want help with to the front", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EligibleUntitledExternalForegroundStillUsesScreenPipeline()
    {
        var providerCalls = 0;
        var capture = new Capture { Region = new([0xff, 0xd8], 100, 50, 0, 0, 100, 50, true, "external") };
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), capture, new Mic(), () => null,
            () => { providerCalls++; return new Talk((_, _) => Task.FromResult("done [POINT:none]")); },
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowTitle: () => "", windowBounds: () => (0, 0, 100, 50), windowHandle: () => 42,
            foregroundCaptureEligible: () => true);

        await talk.SubmitTextAsync("Explain what is on this screen", false);

        Assert.Equal(1, capture.RegionCalls);
        Assert.Equal(1, providerCalls);
    }

    [Fact]
    public async Task SelfContainedVisualBypassesForegroundCaptureEligibility()
    {
        var eligibilityReads = 0;
        var boundsReads = 0;
        var providerCalls = 0;
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => { providerCalls++; return new Talk((_, _) => Task.FromResult("```artifact\n{\"kind\":\"scene3d\",\"id\":\"cube\",\"title\":\"Cube\",\"shape\":\"cube\",\"color\":\"#25664c\",\"rotation\":24}\n```")); },
            new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { },
            windowTitle: () => "", windowBounds: () => { boundsReads++; return (0, 0, 100, 50); },
            windowHandle: () => 42, foregroundCaptureEligible: () => { eligibilityReads++; return false; });

        await talk.SubmitTextAsync("Create a rotatable 3D cube", false);

        Assert.Equal(0, eligibilityReads);
        Assert.Equal(0, boundsReads);
        Assert.Equal(1, providerCalls);
    }

    [Fact]
    public async Task QueuedCancelledVisualCannotChangeActiveScreenTurnGuard()
    {
        var client = new NonCancellableFirstTalk();
        using var talk = new TalkOrchestrator(new Hotkey(), new Overlay(), new Capture(), new Mic(), () => null,
            () => client, new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });

        var active = talk.SubmitTextAsync("Explain what is on this screen", false);
        await client.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(talk.CurrentTurnRequiresScreen);

        var queued = talk.SubmitTextAsync("Create an interactive calculator with Research 3", false);
        Assert.True(talk.CurrentTurnRequiresScreen);
        talk.CancelCurrentTurn();
        await queued;
        Assert.True(talk.CurrentTurnRequiresScreen);

        client.ReleaseFirst.TrySetResult();
        await active;
        Assert.True(talk.CurrentTurnRequiresScreen);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Agent_DenialOrExecutionFailureStopsTheLoop(bool allow, bool fail)
    {
        var planner = new Planner(new("type_text", new Dictionary<string,string> { ["text"] = "hello" }, "test", false));
        var confirm = new Confirm(() => allow);
        var executions = 0;
        var replies = new List<string>();
        var agent = new AgentOrchestrator(new Capture(), planner, (request, approved, token) =>
        {
            executions++;
            return Task.FromResult(new ActionResult(!fail, "input rejected", false));
        }, confirm, new Overlay(), new Tts(), _ => { }, onReply: replies.Add);
        await agent.RunAsync("test", "test");
        Assert.Equal(allow ? 1 : 0, executions);
        Assert.Equal(1, planner.Calls);
        Assert.Single(replies);
    }

    [Fact]
    public async Task Agent_RechecksProtectedWindowAfterApproval()
    {
        var title = "Test sandbox";
        var executions = 0;
        var agent = new AgentOrchestrator(new Capture(), new Planner(new("type_text", new Dictionary<string,string> { ["text"] = "hello" }, "test", false)),
            (r, c, t) => { executions++; return Task.FromResult(new ActionResult(true, "ok", false)); },
            new Confirm(() => { title = "1Password"; return true; }), new Overlay(), new Tts(), _ => { }, foregroundTitle: () => title);
        await agent.RunAsync("test", "test");
        Assert.Equal(0, executions);
    }

    [Theory]
    [InlineData("NaN", "screenshot")]
    [InlineData("5000", "screenshot")]
    [InlineData("5000", "virtual")]
    [InlineData("5", "unknown")]
    public async Task Agent_InvalidCoordinatesNeverAskOrExecute(string x, string space)
    {
        var confirm = new Confirm(() => throw new Exception("Invalid input must not reach approval"));
        var agent = new AgentOrchestrator(new Capture(), new Planner(new("click", new Dictionary<string,string> { ["x"] = x, ["y"] = "5", ["space"] = space }, "test", false)),
            (r,c,t) => throw new Exception("Invalid input must not execute"), confirm, new Overlay(), new Tts(), _ => { });
        await Assert.ThrowsAsync<FormatException>(() => agent.RunAsync("test", "test"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("http://user:pass@localhost")]
    [InlineData("http://localhost?secret=x")]
    public void LocalProxy_RejectsNonLocalOrAmbiguousDestinations(string url) =>
        Assert.Throws<InvalidOperationException>(() => ProviderFactory.ResolveBaseUrl(new AppSettings { UseLocalProxy = true, LocalProxyBaseUrl = url }));

    [Fact]
    public void LocalProxy_NormalizesLoopback() => Assert.Equal("http://127.0.0.1:8787/v1", ProviderFactory.ResolveBaseUrl(new AppSettings { UseLocalProxy = true }));

    [Fact]
    public void Hotkey_UsesObservedModifierKeyEvents()
    {
        Assert.True(HotkeyChord.Default.MatchesPressedKeys(new HashSet<int> { 0x5B, 0xA4, 0x20 }));
        Assert.False(HotkeyChord.Default.MatchesPressedKeys(new HashSet<int> { 0x5B, 0x20 }));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tool\":\"shell\"}")]
    [InlineData("{\"tool\":42}")]
    public void AgentParser_RejectsMissingOrUnsupportedTools(string json) => Assert.Throws<FormatException>(() => OpenRouterAgentPlanner.ParseStep(json));

    private sealed class Capture : IScreenCaptureService
    {
        public int Calls;
        public int RegionCalls;
        public ScreenCaptureResult? Region;
        public Task<ScreenCaptureResult?> CaptureRegionJpegAsync((int X, int Y, int Width, int Height) bounds, CancellationToken token = default)
        { RegionCalls++; return Task.FromResult(Region); }
        public Task<IReadOnlyList<ScreenCaptureResult>> CaptureAllMonitorsJpegAsync(CancellationToken token = default)
        { Calls++; return Task.FromResult<IReadOnlyList<ScreenCaptureResult>>([new([0xff,0xd8], 100,50,0,0,200,100,true,"test")]); }
    }
    private sealed class Mic : IMicrophoneRecorder
    {
        public int Starts;
        public bool IsRecording { get; private set; }
        public void Start() { Starts++; IsRecording = true; }
        public byte[] StopToWav() { IsRecording = false; return []; }
        public void Dispose() { }
    }
    [Fact]
    public void CancelledTalkReleaseDropsTheTurnSilently()
    {
        var hotkey = new RaisingHotkey();
        var mic = new Mic();
        var talkCalls = 0;
        var messages = new List<string>();
        using var talk = new TalkOrchestrator(hotkey, new Overlay(), new Capture(), mic, () => null,
            () => { talkCalls++; return null; }, new Tts(), () => new AppSettings { PersistConversationHistory = false }, log: _ => { });
        talk.MessageReceived += (_, text) => messages.Add(text);
        talk.Start();

        hotkey.Press();
        Assert.Equal(CompanionVoiceState.Listening, talk.State); // the listening state may show
        hotkey.Release(cancelled: true);
        Assert.Equal(CompanionVoiceState.Idle, talk.State);
        Assert.False(mic.IsRecording);
        Assert.Empty(messages);
        Assert.Equal(0, talkCalls);

        hotkey.Press();
        hotkey.Release(cancelled: false); // control: a real release does run the turn
        Assert.True(messages.Count + talkCalls > 0);
    }

    private sealed class RaisingHotkey : IPushToTalkMonitor
    {
        public HotkeyChord Chord { get; set; } = HotkeyChord.Default;
        public event EventHandler? Pressed;
        public event EventHandler? Released;
        public void Press() => Pressed?.Invoke(this, EventArgs.Empty);
        public void Release(bool cancelled) => Released?.Invoke(this, new ShortcutEndedEventArgs(cancelled));
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class Hotkey : IPushToTalkMonitor
    {
        public HotkeyChord Chord { get; set; } = HotkeyChord.Default;
        public event EventHandler? Pressed { add { } remove { } }
        public event EventHandler? Released { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Overlay : IOverlayController
    {
        public (int,int)? LastPoint;
        public IReadOnlyList<VirtualAnnotation> Marks = [];
        public void ShowAnnotations(IReadOnlyList<VirtualAnnotation> marks) => Marks = marks;
        public void ShowListening() { }
        public void ShowProcessing() { }
        public void ShowResponding(string text) { }
        public void PointAt(int x, int y, string? label, string? text = null) => LastPoint = (x,y);
        public void Hide() { }
        public void Dispose() { }
    }
    private sealed class Tts : ITtsClient
    { public Task SpeakAsync(string text, CancellationToken token = default) => Task.CompletedTask; public void Stop() { } }
    private sealed class RecordingTts : ITtsClient
    {
        private int _active;
        public List<string> Spoken { get; } = [];
        public int MaximumConcurrent { get; private set; }
        public async Task SpeakAsync(string text, CancellationToken token = default)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrent = Math.Max(MaximumConcurrent, active);
            try { Spoken.Add(text); await Task.Delay(5, token); }
            finally { Interlocked.Decrement(ref _active); }
        }
        public void Stop() { }
    }
    private sealed class BlockingTts : ITtsClient
    {
        public bool Stopped { get; private set; }
        public Task SpeakAsync(string text, CancellationToken token = default) => Task.Delay(Timeout.Infinite, token);
        public void Stop() => Stopped = true;
    }
    private sealed class InterruptibleTts : ITtsClient
    {
        private int _calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Stopped { get; private set; }
        public async Task SpeakAsync(string text, CancellationToken token = default)
        {
            if (Interlocked.Increment(ref _calls) > 1) return;
            Stopped = false;
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        }
        public void Stop() => Stopped = true;
    }
    private sealed class GatedRecordingTts : ITtsClient
    {
        private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Spoken { get; } = [];
        public async Task SpeakAsync(string text, CancellationToken token = default)
        {
            Spoken.Add(text);
            if (Spoken.Count == 1) await _first.Task.WaitAsync(token);
        }
        public void ReleaseFirst() => _first.TrySetResult();
        public void Stop() { }
    }
    private sealed class ThrowingTts : ITtsClient
    {
        public Task SpeakAsync(string text, CancellationToken token = default) =>
            Task.FromException(new InvalidOperationException("No speech output"));
        public void Stop() { }
    }
    private sealed class Talk(Func<string,CancellationToken,Task<string>> reply, Action<IReadOnlyList<(string role,string text)>>? observeHistory = null, Action<IReadOnlyList<ScreenCaptureResult>>? inspectScreens = null, Action<string>? observeSystem = null) : ITalkClient
    {
        public Task<string> CompleteVisionTalkAsync(string system, IReadOnlyList<(string role,string text)> history, string text, IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken token = default)
        { observeSystem?.Invoke(system); observeHistory?.Invoke(history); inspectScreens?.Invoke(screens); return reply(text,token); }
    }
    private sealed class StreamingTalk(IReadOnlyList<string> updates, Action<int>? afterUpdate = null) : IStreamingTalkClient
    {
        public Task<string> CompleteVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken token = default) => Task.FromResult(updates[^1]);
        public Task<string> StreamVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, Action<string> responseChanged, CancellationToken token = default)
        {
            for (var i = 0; i < updates.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                responseChanged(updates[i]);
                afterUpdate?.Invoke(i);
            }
            return Task.FromResult(updates[^1]);
        }
    }
    private sealed class BlockingStreamingTalk(TaskCompletionSource entered) : IStreamingTalkClient
    {
        public Task<string> CompleteVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken token = default) => throw new NotSupportedException();
        public async Task<string> StreamVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, Action<string> responseChanged, CancellationToken token = default)
        {
            responseChanged("Speaking now.");
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return "unreachable";
        }
    }
    private sealed class NonCancellableFirstTalk : IStreamingTalkClient
    {
        private int _calls;
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> CompleteVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken token = default) => throw new NotSupportedException();
        public async Task<string> StreamVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, Action<string> responseChanged, CancellationToken token = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstEntered.TrySetResult();
                await ReleaseFirst.Task.ConfigureAwait(false);
            }
            return "done [POINT:none]";
        }
    }
    private sealed class ControlledWindowSwitchStream : IStreamingTalkClient
    {
        public TaskCompletionSource TwoUpdatesSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> CompleteVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, CancellationToken token = default) => throw new NotSupportedException();
        public async Task<string> StreamVisionTalkAsync(string system, IReadOnlyList<(string role, string text)> history, string text,
            IReadOnlyList<ScreenCaptureResult> screens, string model, Action<string> responseChanged, CancellationToken token = default)
        {
            responseChanged("First sentence.");
            responseChanged("First sentence. Second sentence.");
            TwoUpdatesSent.TrySetResult();
            await Continue.Task.WaitAsync(token);
            const string final = "First sentence. Second sentence. Stale sentence.";
            responseChanged(final);
            return final;
        }
    }
    private sealed class Planner(AgentPlanStep step) : IAgentPlanner
    {
        public int Calls;
        public Task<AgentPlanStep> NextStepAsync(string task, IReadOnlyList<ScreenCaptureResult> screens, IReadOnlyList<string> prior, string model, CancellationToken token = default) { Calls++; return Task.FromResult(step); }
    }
    private sealed class Confirm(Func<bool> allow) : IAgentConfirm
    { public Task<bool> ConfirmAsync(string summary, CancellationToken token = default) => Task.FromResult(allow()); }
}
