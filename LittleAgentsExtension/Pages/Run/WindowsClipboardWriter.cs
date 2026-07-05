using System;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace LittleAgentsExtension;

internal sealed class WindowsClipboardWriter : IClipboardWriter
{
    private const uint CF_UNICODETEXT = 13;

    public void SetText(string text)
    {
        WriteUnicodeText(text);
    }

    private static unsafe void WriteUnicodeText(string text)
    {
        if (!PInvoke.OpenClipboard(default))
        {
            throw new InvalidOperationException("Clipboard is busy.");
        }

        HGLOBAL textHandle = default;
        bool transferred = false;
        try
        {
            PInvoke.EmptyClipboard();
            nuint byteCount = checked((nuint)((text.Length + 1) * sizeof(char)));
            textHandle = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, byteCount);
            if (textHandle.IsNull)
            {
                throw new InvalidOperationException("Could not allocate clipboard memory.");
            }

            nint textPointer = (nint)PInvoke.GlobalLock(textHandle);
            if (textPointer == 0)
            {
                throw new InvalidOperationException("Could not lock clipboard memory.");
            }

            try
            {
                Span<char> destination = new(textPointer.ToPointer(), text.Length + 1);
                text.AsSpan().CopyTo(destination);
                destination[text.Length] = '\0';
            }
            finally
            {
                PInvoke.GlobalUnlock(textHandle);
            }

            if (PInvoke.SetClipboardData(CF_UNICODETEXT, (HANDLE)textHandle.Value).IsNull)
            {
                throw new InvalidOperationException("Could not set clipboard text.");
            }

            transferred = true;
        }
        finally
        {
            PInvoke.CloseClipboard();
            if (!transferred && !textHandle.IsNull)
            {
                PInvoke.GlobalFree(textHandle);
            }
        }
    }
}
