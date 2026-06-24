using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace LittleAgentsExtension.Llm;

internal sealed class ClipboardReader
{
    private readonly IClipboardSource _clipboardSource;

    public ClipboardReader()
        : this(new WinRtClipboardSource(new User32ClipboardReader()))
    {
    }

    internal ClipboardReader(IClipboardSource clipboardSource)
    {
        _clipboardSource = clipboardSource;
    }

    public async Task<string?> TryGetTextAsync()
    {
        try
        {
            IClipboardContent? content = await _clipboardSource.GetContentAsync().ConfigureAwait(false);
            if (content is null || !content.ContainsText)
            {
                return null;
            }

            return await content.GetTextAsync().ConfigureAwait(false);
        }
        catch (COMException)
        {
            return await _clipboardSource.TryGetFallbackTextAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return await _clipboardSource.TryGetFallbackTextAsync().ConfigureAwait(false);
        }
    }
}

internal interface IClipboardSource
{
    ValueTask<IClipboardContent?> GetContentAsync();

    ValueTask<string?> TryGetFallbackTextAsync();
}

internal interface IClipboardContent
{
    bool ContainsText { get; }

    ValueTask<string?> GetTextAsync();
}

internal sealed class WinRtClipboardSource : IClipboardSource
{
    private readonly User32ClipboardReader _fallbackReader;

    public WinRtClipboardSource(User32ClipboardReader fallbackReader)
    {
        _fallbackReader = fallbackReader;
    }

    public ValueTask<IClipboardContent?> GetContentAsync()
    {
        return ValueTask.FromResult<IClipboardContent?>(new WinRtClipboardContent(Clipboard.GetContent()));
    }

    public ValueTask<string?> TryGetFallbackTextAsync()
    {
        return _fallbackReader.TryGetTextAsync();
    }
}

internal sealed class WinRtClipboardContent : IClipboardContent
{
    private readonly DataPackageView _content;

    public WinRtClipboardContent(DataPackageView content)
    {
        _content = content;
    }

    public bool ContainsText => _content.Contains(StandardDataFormats.Text);

    public async ValueTask<string?> GetTextAsync()
    {
        return await _content.GetTextAsync();
    }
}

internal sealed class User32ClipboardReader
{
    private const uint CF_UNICODETEXT = 13;

    public ValueTask<string?> TryGetTextAsync()
    {
        return ValueTask.FromResult(ReadTextFromClipboard());
    }

    private static unsafe string? ReadTextFromClipboard()
    {
        if (!PInvoke.OpenClipboard(default))
        {
            return null;
        }

        try
        {
            HANDLE handle = PInvoke.GetClipboardData(CF_UNICODETEXT);
            if (handle.Value is null)
            {
                return null;
            }

            HGLOBAL globalHandle = (HGLOBAL)handle.Value;
            nint textPointer = (nint)PInvoke.GlobalLock(globalHandle);
            if (textPointer == 0)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(textPointer);
            }
            finally
            {
                PInvoke.GlobalUnlock(globalHandle);
            }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }
}
