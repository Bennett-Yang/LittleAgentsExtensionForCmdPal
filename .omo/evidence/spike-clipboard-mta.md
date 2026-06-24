# T44 Spike Clipboard from MTA thread + binary-data handling

## Baseline
- `dotnet test .\LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj --filter "FullyQualifiedName~ClipboardReaderTests" 2>&1; "EXITCODE=$LASTEXITCODE"`
- Before implementation, the filter matched no clipboard tests.

## Implemented
- Added `LittleAgentsExtension/Llm/ClipboardReader.cs` with a WinRT-first `TryGetTextAsync()` path.
- The WinRT path checks `content.Contains(StandardDataFormats.Text)` before `GetTextAsync()`.
- Binary-only or empty clipboard content returns `null`.
- `COMException` and `InvalidOperationException` fall through to a User32 fallback that uses CsWin32-generated `OpenClipboard`, `GetClipboardData`, `GlobalLock`, `GlobalUnlock`, and `CloseClipboard`.
- Removed `LittleAgentsExtension/Llm/.gitkeep` after adding real code.
- Added `LittleAgentsExtension.Tests/ClipboardReaderTests.cs` with a test-only `IClipboardSource` shim.

## Verification
1. `dotnet test .\LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj --filter "FullyQualifiedName~ClipboardReaderTests" 2>&1; "EXITCODE=$LASTEXITCODE"`
   - Result: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3`
   - Exit code: `EXITCODE=0`
2. `Select-String -Path .\LittleAgentsExtension\Llm\ClipboardReader.cs -Pattern "Contains\(StandardDataFormats.Text\)|GetTextAsync|OpenClipboard|CF_UNICODETEXT"`
   - Matched the WinRT text check and User32 fallback calls.

## Manual desktop smoke
- Status: BLOCKED in this headless worker.
- Required steps when running on desktop:
  1. Start the extension in its normal MTA host / F5 path.
  2. Copy plain text in a Windows app and confirm the reader returns that text.
  3. Copy an image-only clipboard payload and confirm the reader returns `null` without throwing.
  4. Repeat with an empty clipboard and confirm `null`.
- Evidence placeholder: record the host used, clipboard payload type, and observed result in this file once smoke is run.

## Cleanup receipt
- No temporary probe files were kept.
- Clipboard handles are closed in the fallback path via `CloseClipboard()` in `finally`.
