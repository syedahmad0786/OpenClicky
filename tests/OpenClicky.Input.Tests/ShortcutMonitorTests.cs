using Xunit;
using OpenClicky.Core.Input;
using OpenClicky.Core.Settings;
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
}
