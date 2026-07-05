# T46 MarkdownContent Re-render Spike

Date: 2026-06-28

## Implementation

- Added DEBUG-only `LittleAgentsExtension/Pages/_Spikes/MarkdownTickerPage.cs`.
- Wired a DEBUG-only pinned item named `Markdown ticker spike` from `AgentsListPage`.
- The page mutates one `MarkdownContent.Body` every 200 ms for 25 ticks and calls `RaiseItemsChanged(0)` after each mutation.
- Added `LittleAgentsExtension.csproj` Release exclusion for `Pages\_Spikes\**\*.cs` so the spike source is not compiled into Release builds.

## Verification

- `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~AgentsListPagePinnedNavigationTests"`
  - Passed: 4, Failed: 0, Skipped: 0.
- `dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --no-build`
  - Passed: 98, Failed: 0, Skipped: 0.
- `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Debug -p:Platform=x64`
  - Build succeeded with 0 warnings, 0 errors.
- `dotnet clean "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64`
  - Clean succeeded with 0 warnings, 0 errors.
- `dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64`
  - Build succeeded with 0 errors. Warnings are trim/SDK/package warnings allowed by D-AOT/F2 wording.
- Release output string scan for `Markdown ticker spike`, `MarkdownTickerPage`, and `little-agents.spike.markdown-ticker`
  - Result: `COUNT=0`.

## Manual Observation Needed

T46 is not complete yet because the actual D-RENDER verdict requires observing Command Palette rendering behavior:

1. Build/deploy Debug via F5.
2. Reload Command Palette Extension.
3. Open Little Agents.
4. Invoke `Markdown ticker spike`.
5. Watch whether the displayed markdown tick number updates live for 5 seconds.

Record one of these verdicts here after desktop smoke:

- `VERDICT: Body mutation re-renders live` -> T34's current Body reassignment path is validated.
- `VERDICT: Body mutation does not re-render live` -> switch T34 to replace the whole `MarkdownContent` instance and call `RaiseItemsChanged(0)` per chunk.

## Manual Observation

- Host: Visual Studio F5 + Command Palette reload.
- Result: PASS.
- Observed: the `Markdown ticker spike` page's Tick number live-updated in Command Palette.

VERDICT: Body mutation re-renders live.

Decision:
- T34's current `_output.Body = ...` mutation path plus `RaiseItemsChanged(0)` is validated.
- No fallback to replacing the whole `MarkdownContent` instance is needed for `Microsoft.CommandPalette.Extensions` 0.5.250829002.
