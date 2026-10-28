using OpenClicky.Core.Input;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using K = OpenClicky.Core.Input.ShortcutCommandKind;

namespace OpenClicky.Core.Tests;

public class ShortcutStateMachineTests
{
    private const int LCtrl = 0xA2, LAlt = 0xA4, RAlt = 0xA5, LWin = 0x5B, LShift = 0xA0;
    private const int A = 0x41, C = 0x43, E = 0x45, L = 0x4C, V = 0x56, Del = 0x2E, Esc = 0x1B, Space = 0x20;

    /// <summary>Feeds key events with a fake clock; every call returns the commands that event produced.</summary>
    private sealed class Driver(ShortcutBindings? bindings = null)
    {
        public ShortcutStateMachine Machine { get; } = new(bindings ?? ShortcutBindings.Default);
        public long Now { get; private set; } = 10_000;
        public List<ShortcutCommand> All { get; } = [];

        public IReadOnlyList<ShortcutCommand> Down(int vk, long after = 0, bool injected = false) => Key(vk, true, after, injected);
        public IReadOnlyList<ShortcutCommand> Up(int vk, long after = 0, bool injected = false) => Key(vk, false, after, injected);

        public IReadOnlyList<ShortcutCommand> Wait(long ms)
        {
            Now += ms;
            return Log(Machine.Tick(Now));
        }

        /// <summary>Taps the keys together: all down, hold, all up in reverse order.</summary>
        public void Tap(long hold, params int[] vks)
        {
            foreach (var vk in vks) Down(vk);
            Wait(hold);
            foreach (var vk in vks.Reverse()) Up(vk);
        }

        private IReadOnlyList<ShortcutCommand> Key(int vk, bool down, long after, bool injected)
        {
            Now += after;
            return Log(Machine.OnKey(vk, down, injected, Now));
        }

        private IReadOnlyList<ShortcutCommand> Log(IReadOnlyList<ShortcutCommand> commands)
        {
            All.AddRange(commands);
            return commands;
        }
    }

    private static ShortcutCommand Cmd(K kind, bool cancelled = false) => new(kind, cancelled);

    [Fact]
    public void ModifierOnlyChordsIgnoreOrdinaryShortcuts()
    {
        var d = new Driver();

        // Ctrl+C then Ctrl+V straight after (copy/paste), Ctrl held a while first.
        d.Down(LCtrl); d.Wait(300); d.Down(C); d.Up(C, 30); d.Up(LCtrl, 30);
        d.Down(LCtrl, 100); d.Down(V, 40); d.Up(V, 30); d.Up(LCtrl, 30);
        // Ctrl+Alt+Del typed normally, then held.
        d.Wait(1000);
        d.Down(LCtrl); d.Down(LAlt, 20); d.Down(Del, 30); d.Wait(1000); d.Up(Del); d.Wait(500); d.Up(LAlt); d.Up(LCtrl);
        // AltGr+e arrives as LCtrl+RAlt then the letter; AltGr stays held after the letter.
        d.Wait(1000);
        d.Down(LCtrl); d.Down(RAlt); d.Down(E, 40); d.Up(E, 60); d.Wait(1000); d.Up(RAlt); d.Up(LCtrl);
        // Win+L.
        d.Wait(1000);
        d.Down(LWin); d.Down(L, 50); d.Wait(500); d.Up(L); d.Up(LWin);
        // Ctrl+Win+E (a different shortcut on the dictate keys) typed quickly.
        d.Wait(1000);
        d.Down(LCtrl); d.Down(LWin, 20); d.Down(E, 40); d.Up(E, 50); d.Up(LWin, 30); d.Up(LCtrl, 30);

        Assert.DoesNotContain(d.All, c => c.Kind != K.MaskStartMenu);
    }

    [Fact]
    public void HeldAltGrThenLetterCancelsTalkAndStaysBlockedUntilRelease()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(RAlt);
        Assert.Equal([Cmd(K.TalkStart)], d.Wait(200));
        Assert.Equal([Cmd(K.TalkEnd, cancelled: true)], d.Down(E));
        d.Up(E, 50);
        d.Wait(1000); // Ctrl+Alt is exactly down again, but the chord is poisoned until everything is released.
        d.Up(RAlt); d.Up(LCtrl);
        Assert.Equal([Cmd(K.TalkStart), Cmd(K.TalkEnd, true)], d.All);
    }

    [Fact]
    public void InjectedInputIsIgnored()
    {
        var d = new Driver();
        d.Down(LCtrl, injected: true); d.Down(LAlt, injected: true); d.Wait(1000);
        d.Up(LAlt, injected: true); d.Up(LCtrl, injected: true);
        for (var i = 0; i < 2; i++) { d.Down(LCtrl, 50, injected: true); d.Up(LCtrl, 50, injected: true); }
        d.Down(LCtrl, 100, injected: true); d.Down(LWin, injected: true); d.Wait(1000); d.Up(LWin, injected: true); d.Up(LCtrl, injected: true);
        Assert.Empty(d.All);
        Assert.False(d.Machine.SwallowLastKey);

        // The agent typing (or pressing Esc) while the user holds talk neither cancels nor poisons it.
        d.Down(LCtrl, 1000); d.Down(LAlt); d.Wait(200);
        d.Down(E, injected: true); d.Up(E, injected: true); d.Down(Esc, injected: true); d.Up(Esc, injected: true);
        d.Wait(800);
        d.Up(LAlt); d.Up(LCtrl);
        Assert.Equal([Cmd(K.TalkStart), Cmd(K.TalkEnd)], d.All);
    }

    [Fact]
    public void WinChordRequestsStartMenuMask()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LWin);
        Assert.Equal([Cmd(K.DictateStart)], d.Wait(600));
        Assert.Equal([Cmd(K.MaskStartMenu), Cmd(K.DictateEnd)], d.Up(LWin)); // mask first, before Win-up passes
        Assert.Empty(d.Up(LCtrl));

        // Ctrl released first: the dictation ends on Ctrl-up and the later Win-up is still masked.
        d.Down(LCtrl, 1000); d.Down(LWin); d.Wait(600);
        Assert.Equal([Cmd(K.DictateEnd)], d.Up(LCtrl));
        Assert.Equal([Cmd(K.MaskStartMenu)], d.Up(LWin));

        // A plain Win tap and Win+L are left alone so the Start menu still works.
        d.Wait(1000);
        Assert.Empty(d.Down(LWin)); Assert.Empty(d.Up(LWin, 100));
        d.Down(LWin, 1000); d.Down(L); d.Up(L);
        Assert.Empty(d.Up(LWin));
    }

    [Theory]
    [InlineData(149, false)]
    [InlineData(150, true)]
    public void ModifierOnlyHoldStartsAfter150Ms(long held, bool started)
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt, 30); // the 150 ms count starts once the exact set is down
        d.Wait(held);
        Assert.Equal(started, d.All.Contains(Cmd(K.TalkStart)));
        Assert.True(d.Machine.NeedsTick); // pending, then held (recovery checks run while held)
    }

    [Theory]
    [InlineData(400, true)]
    [InlineData(401, false)]
    public void HoldReleasedWithin400MsIsATapAndCancels(long held, bool cancelled)
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt);
        d.Wait(held);
        d.Up(LAlt);
        d.Up(LCtrl, 20);
        Assert.Equal([Cmd(K.TalkStart), Cmd(K.TalkEnd, cancelled)], d.All);
    }

    [Fact]
    public void HoldReleasedBefore150MsDoesNothing()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt); d.Wait(100); d.Up(LAlt); d.Up(LCtrl);
        Assert.Empty(d.All);
    }

    [Fact]
    public void ExtraModifierWhileTalkingCancelsIt()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt); d.Wait(1000);
        Assert.Equal([Cmd(K.TalkEnd, true)], d.Down(LShift));
        d.Up(LShift); d.Wait(500); d.Up(LAlt); d.Up(LCtrl);
        Assert.Equal([Cmd(K.TalkStart), Cmd(K.TalkEnd, true)], d.All);
    }

    [Theory]
    [InlineData(400, 500, 400, true)]
    [InlineData(401, 500, 100, false)]
    [InlineData(100, 501, 100, false)]
    [InlineData(100, 100, 401, false)]
    public void DoubleTapTimingBoundaries(long firstHold, long gap, long secondHold, bool fires)
    {
        var d = new Driver();
        d.Down(LCtrl); d.Up(LCtrl, firstHold);
        d.Down(LCtrl, gap);
        var up = d.Up(LCtrl, secondHold); // fires on the second key-up
        ShortcutCommand[] expected = fires ? [Cmd(K.TextMode)] : [];
        Assert.Equal(expected, up);
        Assert.Equal(expected, d.All);
    }

    [Fact]
    public void DoubleTapNeedsTwoCleanTapsInARow()
    {
        var d = new Driver();
        d.Tap(50, LCtrl); d.Wait(50); d.Tap(50, A); d.Wait(50); d.Tap(50, LCtrl); // another key between the taps
        Assert.Empty(d.All);

        d.Wait(1000);
        d.Tap(50, LCtrl); d.Wait(100); d.Tap(50, LCtrl); d.Wait(100); d.Tap(50, LCtrl); // a third tap starts over
        Assert.Equal([Cmd(K.TextMode)], d.All);
        d.Wait(100); d.Tap(50, LCtrl);
        Assert.Equal([Cmd(K.TextMode), Cmd(K.TextMode)], d.All);
    }

    [Fact]
    public void HoldTapOnSharedKeysCountsTowardDoubleTap()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LWin); d.Wait(300); d.Up(LWin); d.Up(LCtrl); // dictate starts at 150, tap cancels it
        d.Down(LCtrl, 200); d.Down(LWin); d.Wait(100); d.Up(LWin);
        var second = d.Up(LCtrl);
        Assert.Equal([Cmd(K.HandsFreeToggle)], second);
        Assert.Equal(
            [Cmd(K.DictateStart), Cmd(K.MaskStartMenu), Cmd(K.DictateEnd, true), Cmd(K.MaskStartMenu), Cmd(K.HandsFreeToggle)],
            d.All);
    }

    [Fact]
    public void EscCancelsOnlyAnActiveAction()
    {
        var d = new Driver();
        Assert.Empty(d.Down(Esc)); Assert.Empty(d.Up(Esc));

        d.Down(LCtrl, 1000); d.Down(LAlt); d.Wait(500);
        Assert.Equal([Cmd(K.TalkEnd, true), Cmd(K.Cancel)], d.Down(Esc));
        d.Up(Esc); d.Wait(500); d.Up(LAlt); d.Up(LCtrl);
        Assert.Equal([Cmd(K.TalkStart), Cmd(K.TalkEnd, true), Cmd(K.Cancel)], d.All);
    }

    [Fact]
    public void BindingWithAKeyStartsImmediatelyAndSwallowsOnlyThatKey()
    {
        var d = new Driver(ShortcutBindings.Default with { Talk = new HotkeyBinding(["Win", "Alt", "Space"], HotkeyTrigger.Hold) });
        d.Down(LWin); Assert.False(d.Machine.SwallowLastKey);
        d.Down(LAlt); Assert.False(d.Machine.SwallowLastKey);
        Assert.Equal([Cmd(K.TalkStart)], d.Down(Space));
        Assert.True(d.Machine.SwallowLastKey);
        d.Down(Space, 30); Assert.True(d.Machine.SwallowLastKey); // auto-repeat
        Assert.Equal([Cmd(K.TalkEnd)], d.Up(Space, 1000));
        Assert.True(d.Machine.SwallowLastKey);
        d.Up(LAlt); Assert.False(d.Machine.SwallowLastKey);
        Assert.Equal([Cmd(K.MaskStartMenu)], d.Up(LWin));
        Assert.False(d.Machine.SwallowLastKey);
    }

    [Fact]
    public void RecorderCapturesTheLargestChordWithTheActionsTrigger()
    {
        var d = new Driver();
        d.Machine.BeginRecording(ShortcutAction.Dictate, d.Now);
        d.Down(LCtrl); d.Down(LAlt); d.Down(LShift, 30);
        d.Wait(1000); // no talk while recording
        d.Up(LShift); d.Up(LAlt);
        var done = d.Up(LCtrl);
        var binding = Assert.Single(done, c => c.Kind == K.RecordingFinished).Binding;
        Assert.NotNull(binding);
        Assert.True(binding.SameChord(new HotkeyBinding(["Ctrl", "Alt", "Shift"], HotkeyTrigger.Hold)));
        Assert.Equal(binding, d.Machine.Recorded);
        Assert.False(d.Machine.IsRecording);
        Assert.False(d.Machine.RecordingTimedOut);
        Assert.Equal([K.RecordingFinished], d.All.Select(c => c.Kind));

        // Double-tap actions record the first tap's set; Win-up is masked; normal shortcuts work again afterwards.
        d.Machine.BeginRecording(ShortcutAction.HandsFree, d.Now);
        d.Down(LShift, 100); d.Down(LWin);
        Assert.Equal([K.MaskStartMenu], d.Up(LWin).Select(c => c.Kind));
        var tap = Assert.Single(d.Up(LShift)).Binding;
        Assert.True(tap!.SameChord(new HotkeyBinding(["Shift", "Win"], HotkeyTrigger.DoubleTap)));

        d.Tap(50, LCtrl); d.Wait(100); d.Tap(50, LCtrl);
        Assert.Equal(K.TextMode, d.All[^1].Kind);
    }

    [Fact]
    public void RecorderSwallowsKeysAndKeepsListeningAfterAnUnusableChord()
    {
        var d = new Driver();
        d.Machine.BeginRecording(ShortcutAction.Talk, d.Now);
        d.Down(A); Assert.True(d.Machine.SwallowLastKey); // a bare letter is not a valid shortcut
        Assert.Empty(d.Up(A)); Assert.True(d.Machine.SwallowLastKey);
        Assert.True(d.Machine.IsRecording);
        d.Down(LCtrl); d.Down(A);
        d.Up(A);
        var binding = Assert.Single(d.Up(LCtrl)).Binding;
        Assert.True(binding!.SameChord(new HotkeyBinding(["Ctrl", "A"], HotkeyTrigger.Hold)));
    }

    [Fact]
    public void RecorderTimesOutAfter7SecondsAndEscAborts()
    {
        var d = new Driver();
        d.Machine.BeginRecording(ShortcutAction.Talk, d.Now);
        Assert.Empty(d.Wait(6999));
        Assert.True(d.Machine.NeedsTick);
        Assert.Equal([new ShortcutCommand(K.RecordingFinished)], d.Wait(1));
        Assert.True(d.Machine.RecordingTimedOut);
        Assert.Null(d.Machine.Recorded);
        Assert.False(d.Machine.NeedsTick);

        d.Machine.BeginRecording(ShortcutAction.Text, d.Now);
        d.Down(LCtrl);
        Assert.Equal([new ShortcutCommand(K.RecordingFinished)], d.Down(Esc));
        Assert.True(d.Machine.SwallowLastKey);
        Assert.False(d.Machine.RecordingTimedOut);
        Assert.False(d.Machine.IsRecording);
        d.Up(Esc); Assert.True(d.Machine.SwallowLastKey);
        d.Up(LCtrl);
        Assert.Equal([K.RecordingFinished, K.RecordingFinished], d.All.Select(c => c.Kind));
    }

    [Fact]
    public void ResetEndsActiveActionsAsCancelled()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt); d.Wait(500);
        Assert.Equal([Cmd(K.TalkEnd, true)], d.Machine.Reset());
        Assert.Empty(d.Machine.PressedPassedKeys);
        Assert.Empty(d.Up(LAlt)); Assert.Empty(d.Up(LCtrl)); // unseen key-ups are ignored
    }

    [Fact]
    public void MissedKeyUpDuringAHoldEndsItCancelled()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt); d.Wait(2000);
        Assert.True(d.Machine.NeedsTick); // the host keeps checking for lost key-ups while a hold runs
        Assert.Equal([Cmd(K.TalkEnd, true)], d.Machine.ReleaseMissed(LAlt, d.Now)); // e.g. Alt-up lost to a UAC prompt
        Assert.Empty(d.Machine.ReleaseMissed(LCtrl, d.Now));

        // Win lost on the secure desktop: no Start-menu mask (the key-up already went), dictation is discarded.
        d.Down(LCtrl, 1000); d.Down(LWin); d.Wait(2000);
        Assert.Equal([Cmd(K.DictateEnd, true)], d.Machine.ReleaseMissed(LWin, d.Now));
        d.Up(LCtrl);
        Assert.Equal([Cmd(K.TalkStart), Cmd(K.DictateStart)], d.All); // nothing else, e.g. no mask on the real Ctrl-up

        // Recovered releases never complete a double-tap.
        d.Tap(50, LCtrl); d.Down(LCtrl, 100);
        Assert.Empty(d.Machine.ReleaseMissed(LCtrl, d.Now + 50));
    }

    [Fact]
    public void SwallowedKeyThatStopsRepeatingIsReleasedCancelled()
    {
        var d = new Driver(ShortcutBindings.Default with { Talk = new HotkeyBinding(["Ctrl", "Space"], HotkeyTrigger.Hold) });
        d.Down(LCtrl); d.Down(Space);
        d.Down(Space, 1000); // first auto-repeat after the keyboard delay
        Assert.Empty(d.Wait(ShortcutStateMachine.SwallowedStaleMs));
        Assert.Equal([Cmd(K.TalkEnd, true)], d.Wait(1)); // repeats went quiet: its key-up was missed
        Assert.Empty(d.Up(Space)); // a late real key-up is ignored...
        Assert.True(d.Machine.SwallowLastKey); // ...but still swallowed, so the app never sees an unpaired up
        d.Up(LCtrl);
        Assert.False(d.Machine.NeedsTick);

        // Recovery that happens inside OnKey (no tick in between) does not swallow the unrelated key-up that follows.
        d.Down(LCtrl, 1000); d.Down(Space);
        Assert.Equal([Cmd(K.TalkEnd, true)], d.Up(LCtrl, ShortcutStateMachine.SwallowedStaleMs + 1));
        Assert.False(d.Machine.SwallowLastKey);
        Assert.True(d.Machine.OnKey(Space, down: false, injected: false, d.Now + 10).Count == 0 && d.Machine.SwallowLastKey);
        d.Down(Space, 100); d.Up(Space); // a fresh press after recovery is an ordinary key again
        Assert.False(d.Machine.SwallowLastKey);
    }

    [Fact]
    public void CtrlClickIsNotACtrlTap()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Machine.OnMouseButton(d.Now + 30); d.Up(LCtrl, 60);
        d.Tap(50, LCtrl);
        Assert.Empty(d.All);

        // A click between two clean taps also breaks the double-tap; a click stops a pending hold too.
        d.Wait(1000);
        d.Tap(50, LCtrl); d.Machine.OnMouseButton(d.Now + 50); d.Wait(100); d.Tap(50, LCtrl);
        d.Wait(1000);
        d.Down(LCtrl); d.Down(LAlt); d.Machine.OnMouseButton(d.Now + 20); d.Wait(500); d.Up(LAlt); d.Up(LCtrl);
        Assert.Empty(d.All);
    }

    [Fact]
    public void ClicksDuringAnActiveHoldKeepTalking()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt);
        Assert.Equal([Cmd(K.TalkStart)], d.Wait(300));
        Assert.Empty(d.Machine.OnMouseButton(d.Now + 10)); // drawing on screen while holding talk
        Assert.Empty(d.Machine.OnMouseButton(d.Now + 200));
        d.Wait(700);
        Assert.Equal([Cmd(K.TalkEnd)], d.Up(LAlt)); // a normal end: the turn is sent
        d.Up(LCtrl);

        // The click still poisons the rest of that session: Ctrl+Alt down again after the hold ended does not restart.
        d.Down(LCtrl, 1000); d.Down(LAlt); d.Wait(300);
        d.Machine.OnMouseButton(d.Now);
        d.Up(LAlt); d.Wait(50); d.Down(LAlt); d.Wait(500); d.Up(LAlt); d.Up(LCtrl);
        Assert.Equal([Cmd(K.TalkStart), Cmd(K.TalkEnd), Cmd(K.TalkStart), Cmd(K.TalkEnd, true)], d.All);
    }

    [Fact]
    public void ChangingBindingsKeepsUnchangedShortcutsAndCancelsChangedOnes()
    {
        var d = new Driver();
        d.Down(LCtrl); d.Down(LAlt); d.Wait(1000);
        d.Machine.Bindings = d.Machine.Bindings with
        {
            Talk = new HotkeyBinding(["Alt", "Ctrl"], HotkeyTrigger.Hold), // same chord, new instance
            Text = new HotkeyBinding(["Shift"], HotkeyTrigger.DoubleTap),
        };
        Assert.Empty(d.Wait(20));
        Assert.Equal([Cmd(K.TalkEnd)], d.Up(LAlt));
        d.Up(LCtrl);

        // A double-tap toggle that is on when its chord changes ends cancelled on the next tick.
        d.Machine.Bindings = d.Machine.Bindings with { Dictate = new HotkeyBinding(["Ctrl", "Win"], HotkeyTrigger.DoubleTap) };
        d.Wait(1000);
        d.Tap(50, LCtrl, LWin); d.Wait(100); d.Tap(50, LCtrl, LWin);
        Assert.Equal(K.DictateStart, d.All.Single(c => c.Kind == K.DictateStart).Kind);
        d.Machine.Bindings = d.Machine.Bindings with { Dictate = HotkeyBinding.DefaultDictate };
        Assert.True(d.Machine.NeedsTick);
        Assert.Equal([Cmd(K.DictateEnd, true)], d.Wait(15));
        Assert.False(d.Machine.NeedsTick);
    }

    [Fact]
    public void EscDoesNotCancelAnActionWithoutAnEnd()
    {
        var d = new Driver(ShortcutBindings.Default with { Text = new HotkeyBinding(["Ctrl", "Shift"], HotkeyTrigger.Hold) });
        d.Down(LCtrl); d.Down(LShift); d.Wait(200);
        Assert.Equal([Cmd(K.TextMode)], d.All);
        Assert.Empty(d.Down(Esc));
    }

    [Fact]
    public void DoubleTapKeyIsSwallowedInPairs()
    {
        const int F9 = 0x78;
        var d = new Driver(ShortcutBindings.Default with { Text = new HotkeyBinding(["F9"], HotkeyTrigger.DoubleTap) });
        d.Down(F9); Assert.True(d.Machine.SwallowLastKey);
        d.Up(F9, 50); Assert.True(d.Machine.SwallowLastKey);
        d.Down(F9, 100); Assert.True(d.Machine.SwallowLastKey);
        Assert.Equal([Cmd(K.TextMode)], d.Up(F9, 50));
        Assert.True(d.Machine.SwallowLastKey);

        d.Down(LShift, 1000); d.Down(F9); Assert.False(d.Machine.SwallowLastKey); // Shift+F9 is not the binding
        d.Up(F9); Assert.False(d.Machine.SwallowLastKey);
    }

    [Fact]
    public void PressedPassedKeysExcludesSwallowedKeys()
    {
        var d = new Driver(ShortcutBindings.Default with { Talk = new HotkeyBinding(["Ctrl", "Space"], HotkeyTrigger.Hold) });
        d.Down(LCtrl); d.Down(Space);
        Assert.Equal([LCtrl], d.Machine.PressedPassedKeys);
    }
}
