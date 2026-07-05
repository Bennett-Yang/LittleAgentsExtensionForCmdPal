# F3 Manual QA Evidence

Status: pass. All 16 T43 checklist steps passed in real Command Palette smoke testing after retries.

## Attempt 1

Operator report:

- Steps 1-9: pass.
- Step 10: fail. Reply follow-up submitted, but the assistant output did not visibly react.
- Step 11: pending. Operator needed more precise timing instructions for the D-RUN-SINGLE input-gated path.
- Step 12: fail. Copy result and Copy transcript did not change the Windows clipboard.
- Step 13: fail. Agent edit path was not discoverable from the agent row.
- Steps 14-15: pass.
- Step 16: fail. Publish command returned `MSBUILD : error MSB1009`, likely from running the relative project path outside the repository root.

## Fixes Applied After Attempt 1

- `LittleAgentsExtension/Pages/Run/ChatRunPage.cs`: `UpdateOutput` now raises `RaiseItemsChanged(0)` after mutating the markdown body so Command Palette is notified during Reply/streaming updates.
- `LittleAgentsExtension/Pages/Run/WindowsClipboardWriter.cs`: clipboard writes now call `Clipboard.Flush()` after `Clipboard.SetContent(...)` so Command Palette command invocation does not lose copied content when the data package owner exits.
- `LittleAgentsExtension/Pages/Agents/AgentsListPage.cs`: agent rows now expose an `Edit` more-command that opens `AgentEditFormPage` directly.
- `LittleAgentsExtension.Tests/ChatRunPageReplyTests.cs`: Reply flow coverage asserts submitting the follow-up keeps the same page and returns page content to the markdown view.
- `LittleAgentsExtension.Tests/AgentsListPagePinnedNavigationTests.cs`: coverage asserts the agent row `Edit` more-command opens `AgentEditFormPage`.

## Verification After Fixes

- Focused F3 regression tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AgentsListPagePinnedNavigationTests|FullyQualifiedName~ChatRunPageCommandsTests|FullyQualifiedName~ChatRunPageReplyTests"` -> 15 passed, 0 failed, 0 skipped.
- Full Debug tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64` -> 106 passed, 0 failed, 0 skipped.
- Debug build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Debug -p:Platform=x64` -> succeeded, 0 warnings, 0 errors.
- Release build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64` -> succeeded, 0 errors, 7 known warnings.

## Retry Required

Redeploy from Visual Studio F5, run `Reload Command Palette Extension`, then rerun the failed or pending T43 steps:

- Step 10: on a completed run, invoke Reply, submit a follow-up, and confirm the same run page appends the follow-up plus a new assistant response.
- Step 11: start agent A with a slow/streaming no-`{input}` template; while A is streaming, open agent B whose template contains `{input}`; as soon as B's input form appears and before submitting B, return to/observe A and confirm A stops within 1 second; then submit B and confirm B streams.
- Step 12: use Copy result, paste into a separate text field/editor, and confirm it contains only the latest assistant turn; then use Copy transcript and confirm the pasted clipboard contains the full markdown transcript.
- Step 13: on an agent row, open More commands, choose `Edit`, change a visible field, reload Command Palette, and confirm the edit persists.
- Step 16: from the repository root, run `dotnet publish "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -r win-x64 -p:Platform=x64 2>&1 | Select-String "error"` and confirm it returns 0 matches.

F3 remains unchecked until the retry passes all 16 steps.

## Addendum: Agent Delete Bug

Operator report after Attempt 1: agent delete had no visible effect.

Root cause: `ConfirmDeleteAgentForm.SubmitForm` only deleted when the submitted AdaptiveCard payload contained `confirmed: true`. Command Palette can submit the delete action with an empty form payload, so the code silently treated the real Delete click as unconfirmed. Provider delete did not have this problem because its confirmation form deletes on submit without relying on action data.

Fix applied:

- `LittleAgentsExtension/Pages/Agents/ConfirmDeleteAgentPage.cs`: delete confirmation now exposes only one `Delete` submit action and deletes on submit, matching the provider delete confirmation pattern. There is no `Cancel` submit action; backing out of the page is the cancel path.
- `LittleAgentsExtension.Tests/AgentStoreTests.cs`: added regression coverage for empty submit payload deleting the agent and for the template exposing only a Delete submit.

Verification:

- Red test before fix: `ConfirmDeleteAgentForm_empty_submit_payload_deletes_agent` failed because the agent remained in the store.
- Focused tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AgentStoreTests"` -> 9 passed, 0 failed, 0 skipped.
- Full Debug tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64` -> 106 passed, 0 failed, 0 skipped.
- Debug build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Debug -p:Platform=x64` -> succeeded, 0 warnings, 0 errors.
- Release build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64` -> succeeded, 0 errors, 7 known warnings.

Additional retry step: after redeploy/reload, open an agent row's More commands, choose `Delete`, press the confirmation page's `Delete` button, and confirm the agent disappears and remains absent after `Reload Command Palette Extension`.

## Attempt 2

Operator report:

- Steps 1-11: pass.
- Step 10 note: Reply works, but the new reply rendered immediately after the previous assistant turn without a separating blank line.
- Step 12: fail. Copy result and Copy transcript did not change the Windows clipboard.
- Steps 13-16: pass.

Fixes applied after Attempt 2:

- `LittleAgentsExtension/Pages/Run/ChatRunPage.cs`: each new transcript turn now inserts a blank line before `**You:**`, so Reply begins as a separate markdown block instead of running into the previous assistant text.
- `LittleAgentsExtension/Pages/Run/WindowsClipboardWriter.cs`: copy now writes `CF_UNICODETEXT` through User32 clipboard APIs (`OpenClipboard`, `EmptyClipboard`, `GlobalAlloc`, `GlobalLock`, `SetClipboardData`, `CloseClipboard`) instead of relying on WinRT `Clipboard.SetContent`/`Flush`, which did not update the real clipboard from the Command Palette extension host.
- `LittleAgentsExtension/NativeMethods.txt`: added the User32/global-memory APIs required for clipboard writes.
- `LittleAgentsExtension.Tests/ChatRunPageReplyTests.cs`: added coverage that Reply output contains a blank line between the previous assistant turn and the next `**You:**` turn.

Verification after Attempt 2 fixes:

- C# LSP diagnostics were unavailable: MCP connection closed for changed files.
- Focused run-page tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ChatRunPageReplyTests|FullyQualifiedName~ChatRunPageCommandsTests"` -> 10 passed, 0 failed, 0 skipped.
- Full Debug tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64` -> 106 passed, 0 failed, 0 skipped.
- Debug build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Debug -p:Platform=x64` -> succeeded, 0 warnings, 0 errors.
- Release build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64` -> succeeded, 0 errors, 7 known warnings.

Retry requested: redeploy from Visual Studio F5, run `Reload Command Palette Extension`, then rerun Step 10 for Reply visual separation and Step 12 for Copy result / Copy transcript clipboard writes.

Final operator report after retry:

- Step 10: pass. Reply works and the new reply is visually separated from the previous assistant turn.
- Step 12: pass. Copy result updates the clipboard with the latest assistant turn, and Copy transcript updates the clipboard with the full markdown transcript.

Final verdict: F3 passed. Steps 1-16 are all pass.

## Post-F3 Cleanup: Markdown Ticker Spike UI

Operator noticed the `Markdown ticker spike` entry still appeared in the Debug-deployed Command Palette UI. This spike was only needed to validate T46 markdown body mutation re-rendering and should not remain visible in the MVP experience.

Cleanup applied:

- `LittleAgentsExtension/Pages/Agents/AgentsListPage.cs`: removed the Debug-only `MarkdownTicker` pinned item and enum branch.
- `LittleAgentsExtension/Pages/Agents/AgentsListPage.PinnedCommands.cs`: removed `CreateMarkdownTickerPage()`.
- `LittleAgentsExtension/Pages/_Spikes/MarkdownTickerPage.cs`: deleted the spike page source.
- `LittleAgentsExtension.Tests/AgentsListPagePinnedNavigationTests.cs`: added coverage that `Markdown ticker spike` is not exposed in the agent list.

Verification:

- Residual source scan: `Select-String -Path "LittleAgentsExtension\**\*.cs","LittleAgentsExtension.Tests\**\*.cs" -Pattern "MarkdownTicker|Markdown ticker|markdown-ticker"` -> no output.
- Focused pinned navigation tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AgentsListPagePinnedNavigationTests"` -> 6 passed, 0 failed, 0 skipped.
- Full Debug tests: `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64` -> 107 passed, 0 failed, 0 skipped.
- Debug build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Debug -p:Platform=x64` -> succeeded, 0 warnings, 0 errors.
- Release build: `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64` -> succeeded, 0 errors, 7 known warnings.
