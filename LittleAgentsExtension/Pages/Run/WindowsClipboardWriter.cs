using Windows.ApplicationModel.DataTransfer;

namespace LittleAgentsExtension;

internal sealed class WindowsClipboardWriter : IClipboardWriter
{
    public void SetText(string text)
    {
        DataPackage package = new();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
