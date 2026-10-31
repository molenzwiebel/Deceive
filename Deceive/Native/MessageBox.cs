namespace Deceive;

// Drop-in replacements for the subset of the WinForms MessageBox API Deceive uses, backed by MessageBoxW.
// MessageBoxW is safe to call from any thread, which matters because Deceive shows prompts from background tasks.

internal enum MessageBoxButtons : uint
{
    OK = 0x0,
    OKCancel = 0x1,
    RetryCancel = 0x5
}

internal enum MessageBoxIcon : uint
{
    None = 0x00,
    Error = 0x10,
    Question = 0x20,
    Information = 0x40
}

internal enum MessageBoxDefaultButton : uint
{
    Button1 = 0x000,
    Button2 = 0x100,
    Button3 = 0x200
}

internal enum DialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7
}

internal static class MessageBox
{
    private const uint MB_SETFOREGROUND = 0x10000;

    internal static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1) =>
        (DialogResult)Native.Win32.MessageBoxW(0, text, caption, (uint)buttons | (uint)icon | (uint)defaultButton | MB_SETFOREGROUND);
}
