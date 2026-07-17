using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace LittleAgentsExtension.Llm;

internal sealed class ClipboardReader
{
    private readonly IClipboardSource _clipboardSource;

    public ClipboardReader()
        : this(new User32ClipboardSource(new User32ClipboardReader()))
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

internal sealed class User32ClipboardSource : IClipboardSource
{
    private readonly User32ClipboardReader _reader;

    public User32ClipboardSource(User32ClipboardReader reader)
    {
        _reader = reader;
    }

    public async ValueTask<IClipboardContent?> GetContentAsync()
    {
        string? text = await _reader.TryGetTextAsync().ConfigureAwait(false);
        return text is null ? null : new BufferedClipboardContent(text);
    }

    public ValueTask<string?> TryGetFallbackTextAsync()
    {
        return _reader.TryGetTextAsync();
    }
}

internal sealed class BufferedClipboardContent : IClipboardContent
{
    private readonly string _text;

    public BufferedClipboardContent(string text)
    {
        _text = text;
    }

    public bool ContainsText => true;

    public ValueTask<string?> GetTextAsync()
    {
        return ValueTask.FromResult<string?>(_text);
    }
}

internal sealed class User32ClipboardReader
{
    private const uint CF_UNICODETEXT = 13;
    private const int MaximumDecodedCharacters = TemplateRenderer.SelectionCharacterLimit + 1;

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
                nuint allocationSize = PInvoke.GlobalSize(globalHandle);
                return DecodeUnicodeText(textPointer, allocationSize);
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

    internal static unsafe string? DecodeUnicodeText(nint textPointer, nuint allocationSize)
    {
        if (textPointer == 0 || allocationSize == 0 || allocationSize % sizeof(char) != 0)
        {
            return null;
        }

        nuint characterCapacity = allocationSize / sizeof(char);
        int inspectedCharacterCount = (int)Math.Min(characterCapacity, (nuint)MaximumDecodedCharacters);
        ReadOnlySpan<char> characters = new((void*)textPointer, inspectedCharacterCount);
        int terminatorIndex = characters.IndexOf('\0');
        if (terminatorIndex >= 0)
        {
            return Marshal.PtrToStringUni(textPointer, terminatorIndex);
        }

        return characterCapacity > (nuint)MaximumDecodedCharacters
            ? Marshal.PtrToStringUni(textPointer, MaximumDecodedCharacters)
            : null;
    }
}
