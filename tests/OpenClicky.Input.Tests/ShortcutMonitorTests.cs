using Xunit;
using OpenClicky.Core;
using OpenClicky.Core.Input;
using OpenClicky.Core.Settings;
using OpenClicky.Core.Shell;
using OpenClicky.Input;
using K = OpenClicky.Core.Input.ShortcutCommandKind;

namespace OpenClicky.Input.Tests;

public class ShortcutMonitorTests
{
    [Fact]
    public void CommandsRaiseTheMatchingEvents()
    {
        using var monitor = new ShortcutMonitor();
        var log = new List<string>();
        monitor.Pressed += (_, _) => log.Add("pressed");
        monitor.Released += (_, e) => log.Add($"released:{((ShortcutEndedEventArgs)e).Cancelled}");
        monitor.Cancelled += (_, _) => log.Add("cancelled");
        monitor.TextMode += (_, _) => log.Add("text");
        monitor.DictateStarted += (_, _) => log.Add("dictate");
        monitor.DictateEnded += (_, e) => log.Add($"dictateEnded:{e.Cancelled}");
        monitor.HandsFreeToggled += (_, _) => log.Add("handsFree");

        foreach (var command in new ShortcutCommand[]
        {
            new(K.TalkStart), new(K.TalkEnd, true), new(K.Cancel), new(K.TextMode),
            new(K.DictateStart), new(K.DictateEnd), new(K.HandsFreeToggle),
        })
            monitor.Raise(command);

        Assert.Equal(["pressed", "released:True", "cancelled", "text", "dictate", "dictateEnded:False", "handsFree"], log);
    }

    [Fact]
    public void StopEndsARecordingWithNull()
    {
        using var monitor = new ShortcutMonitor();
        var calls = new List<HotkeyBinding?>();
        monitor.Record(ShortcutAction.Talk, calls.Add);
        monitor.Record(ShortcutAction.Text, calls.Add); // the newer request replaces the first
        monitor.Stop();
        Assert.Equal([null, null], calls);
    }

    [Fact]
    public void NewBindingsWaitUntilTheHeldShortcutEnds()
    {
        using var monitor = new ShortcutMonitor { IsKeyDown = _ => true };
        var log = new List<string>();
        monitor.Pressed += (_, _) => log.Add("pressed");
        monitor.Released += (_, e) => log.Add($"released:{((ShortcutEndedEventArgs)e).Cancelled}");
        monitor.HandleKey(0xA2, true, false, 0);   // LCtrl
        monitor.HandleKey(0xA4, true, false, 10);  // LAlt
        monitor.HandleKey(0xA4, true, false, 200); // auto-repeat: the 150 ms hold has started
        var moved = ShortcutBindings.Default with { Talk = new HotkeyBinding(["Ctrl", "Shift"], HotkeyTrigger.Hold) };
        monitor.Bindings = moved;
        Assert.Same(moved, monitor.Bindings);
        monitor.HandleKey(0xA4, false, false, 1000);
        monitor.HandleKey(0xA2, false, false, 1010);
        Assert.Equal(["pressed", "released:False"], log); // not cut short by the change
        monitor.HandleKey(0xA2, true, false, 2000);
        monitor.HandleKey(0xA4, true, false, 2010);
        monitor.HandleKey(0xA4, true, false, 2300);
        Assert.Equal(2, log.Count); // Ctrl+Alt no longer talks
    }

    [Fact]
    public void EveryPhysicalEscPressIsReportedAndPassedOn()
    {
        using var monitor = new ShortcutMonitor { IsKeyDown = _ => true };
        var escapes = 0;
        monitor.EscapePressed += (_, _) => escapes++;
        Assert.False(monitor.HandleKey(0x1B, true, false, 0));
        Assert.False(monitor.HandleKey(0x1B, true, false, 30)); // auto-repeat
        Assert.False(monitor.HandleKey(0x1B, false, false, 60));
        monitor.HandleKey(0x1B, true, true, 90); // injected
        monitor.HandleKey(0x1B, false, true, 95);
        monitor.HandleKey(0x1B, true, false, 100);
        Assert.Equal(2, escapes);
    }
}
