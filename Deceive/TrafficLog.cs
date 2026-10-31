using System.Diagnostics;

namespace Deceive;

/// Logging for the contents of chat and client config traffic. This includes private messages, friend lists
/// and account details, so it is opt-in (tray menu) and only meant to be enabled while reproducing a bug.
/// Connection events and errors are always logged through Trace as usual.
internal static class TrafficLog
{
    private static volatile bool _enabled = Persistence.GetLogTraffic();

    internal static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            Persistence.SetLogTraffic(value);
            Trace.WriteLine(value ? "Chat traffic logging enabled." : "Chat traffic logging disabled.");
        }
    }

    internal static void Write(string label, string content)
    {
        if (_enabled)
            Trace.WriteLine(label + content);
    }
}
