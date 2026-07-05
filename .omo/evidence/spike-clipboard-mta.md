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

## Manual smoke attempt 2026-06-28
- Host: Visual Studio F5 + Command Palette reload.
- Text clipboard result: failed before clipboard behavior could be observed.
- Observation: running the `Clipboard smoke` agent produced no visible reaction.
- Follow-up root cause found in agent navigation, not clipboard reading: agent rows used an invokable command returning `CommandResult.GoToPage` with custom `ChatRunPageNavigationArgs`, but CmdPal's `GoToPageArgs` only carries `PageId`/`NavigationMode` and does not carry a target page object.
- Fix applied: runnable agent rows now use direct `ChatRunPage` commands (`new ListItem(new ChatRunPage(...))`), matching the toolkit's direct-page pattern. Missing-provider/key cases still use toast commands.
- Additional fix: `ProviderEditForm.SubmitForm` now writes a non-empty api key before `ProviderStore.Upsert(...)`, so the provider `Changed` refresh can build runnable direct `ChatRunPage` rows immediately after provider creation.
- Verification after fix: focused invocation tests passed 6/6; full suite passed 105/105; Debug build passed 0 warnings/errors; Release build passed 0 errors with existing trim/analyzer warnings.
- Status: T44 still pending. Retry the text/image/empty clipboard smoke after redeploy + Command Palette reload.

## Manual smoke pass 2026-06-28
- Host: Visual Studio F5 + Command Palette reload.
- Text clipboard: PASS. `Clipboard smoke` rendered the copied text in the `You` section, and the assistant returned the same text verbatim.
- Image-only clipboard: PASS. The `You` section was empty; no image/binary garbage appeared and no clipboard exception surfaced. The assistant section showed downstream provider error `Error 400: HTTP 400 (: 1213)未正常接收到prompt内容`, which is expected for an empty prompt and is not a clipboard-reader failure.
- Empty clipboard: PASS. The `You` section was empty; no clipboard exception surfaced. The assistant section showed downstream provider error `Error 400: HTTP 400 (: 1213)未正常接收到prompt内容`, which is expected for an empty prompt and is not a clipboard-reader failure.
- Verdict: PASS. Real desktop smoke confirms text clipboard returns text, image-only clipboard is treated as null/empty, and empty clipboard is treated as null/empty from the extension's normal MTA host path.
