# Little Agents Developer Handoff

This guide is for the next developer working on Little Agents. It is source backed by the checked in code, README, publishing notes, decision map, and evidence files current at this handoff.

## Purpose And Scope

Little Agents is a PowerToys Command Palette extension for saving named prompt templates and running them against OpenAI compatible `/v1/chat/completions` services.

Each agent stores a name, system prompt, user template, provider id, model, optional icon, and tags. Each provider stores a name, base URL, and optional default model. API keys are stored outside provider JSON.

Current scope is chat completions only. The project does not implement images, audio, tools, RAG, document upload, cost tracking, provider model discovery, embeddings, vector stores, or `/v1/models` calls. The README and F4 evidence both treat those as out of scope today.

## Prerequisites

Use Windows build 19041 or newer with PowerToys Command Palette v0.100 or newer. Development uses Visual Studio 2022 17.13 or newer and .NET 9 for Windows.

The main project targets `net9.0-windows10.0.26100.0`, supports x64 and arm64 runtime identifiers, enables nullable reference types, allows unsafe blocks for clipboard interop, and uses MSIX tooling. Release builds are self-contained and trimmed, keep AOT compatibility checks enabled, and explicitly disable single-file publishing so the MSIX contains the application DLL and runtime configuration required for packaged COM activation. Known package dependencies include `Microsoft.CommandPalette.Extensions`, CsWin32, CsWinRT, `Shmuelie.WinRTServer`, `OpenAI`, `Microsoft.Extensions.AI.OpenAI`, and DPAPI protected data.

## Build, Deploy, And Reload

From the repository root, these are the developer commands recorded by the project docs and evidence:

```powershell
dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64
dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Debug -p:Platform=x64
dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64
dotnet publish "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -r win-x64 -p:Platform=x64
```

For local Command Palette use, open `LittleAgentsExtension.sln` in Visual Studio, press `F5` to build and deploy the MSIX package, then run `Reload Command Palette Extension` from Command Palette.

For Store bundle creation, GitHub Actions packaging, and Partner Center submission,
use the dedicated [Microsoft Store publishing guide](PUBLISHING.md).

## Use The Extension

The extension exposes one top level command provider, `LittleAgentsExtensionCommandsProvider`, with display name `Little Agents`. It creates one `AgentsListPage` and wires shared stores, the secret store, the OpenAI chat client, the run session coordinator, and Command Palette settings.

On first run, `AgentsListPage` loads agents and providers from local JSON. If there are no providers, empty content and the `+ New Agent` pinned item route the operator to provider creation first. If providers exist but no agents exist, empty content routes the operator to create an agent.

Provider setup asks for name, base URL, API key, and optional default model. The base URL must use HTTPS unless it identifies a loopback provider, for which HTTP is accepted. Include the provider API root, such as `https://api.openai.com/v1`, `https://openrouter.ai/api/v1`, or `http://localhost:11434/v1`. The request layer appends `/chat/completions` after that base URL.

Agent setup asks for name, system prompt, user template, provider, model, icon, and comma separated tags. If model is empty, the selected provider default model is used. If neither the agent model nor provider default model is present, save is rejected.

Running an agent opens a fresh `ChatRunPage` when the provider exists and the API key is present. Missing provider or missing key returns a toast instead of starting a run.

## Code Architecture

`LittleAgentsExtension\LittleAgentsExtension.cs` is the extension entry point. It returns the command provider for `ProviderType.Commands`.

`LittleAgentsExtension\LittleAgentsExtensionCommandsProvider.cs` builds shared runtime services and exposes the top level `Little Agents` command.

`LittleAgentsExtension\Pages\Agents\AgentsListPage.cs` lists agents, searches by name, provider id, and tag, pins `+ New Agent` and `Manage Providers`, and turns runnable agents into `ChatRunPage` commands.

`LittleAgentsExtension\Pages\Agents\AgentEditFormPage.cs` owns the agent Adaptive Card form. `ConfirmDeleteAgentPage.cs` owns agent delete confirmation.

`LittleAgentsExtension\Pages\Providers\ProvidersListPage.cs` lists providers, searches by name and base URL, opens edit pages, and blocks provider deletion while agents still reference the provider.

`LittleAgentsExtension\Pages\Providers\ProviderEditFormPage.cs` owns provider creation and editing, base URL validation, API key handling, and provider JSON writes.

`LittleAgentsExtension\Pages\Run\ChatRunPage.cs` owns first run initialization, template rendering, request construction, streaming output, error mapping, markdown refresh, history, and cancellation. `ChatRunPage.Commands.cs` owns Copy result, Copy transcript, Stop, Re-run, and Reply. `RunSessionCoordinator.cs` ensures the active page cancels older streams.

`LittleAgentsExtension\Llm\OpenAiChatClient.cs` adapts `Microsoft.Extensions.AI` over the OpenAI SDK and streams text updates from chat completions. `TemplateRenderer.cs` renders `{input}` and `{selection}` for the first user turn. `ClipboardReader.cs` uses sized User32 `CF_UNICODETEXT` access so clipboard text is bounded before managed allocation.

`LittleAgentsExtension\Storage\AgentStore.cs` and `ProviderStore.cs` persist JSON with schema version 1. `Models.cs` defines `AgentDef`, `ProviderDef`, and file envelopes. `WindowsPasswordVaultSecretStore.cs`, `DpapiSecretStore.cs`, and `SecretStoreFactory.cs` keep API keys outside provider JSON.

`LittleAgentsExtension\Pages\SettingsPage.cs` wires Command Palette settings for a global system prompt prefix and default temperature.

`LittleAgentsExtension.Tests\` contains xUnit tests for stores, forms, template rendering, clipboard reads, OpenAI request behavior, run page streaming, reply, copy commands, provider delete safety, settings, and first run flows.

## Runtime Flow

1. Command Palette asks `LittleAgentsExtension` for a commands provider.
2. `LittleAgentsExtensionCommandsProvider` returns one `AgentsListPage` command.
3. `AgentsListPage` reads `agents.json` and `providers.json`, refreshes when either store changes, and builds rows plus pinned actions.
4. Selecting a runnable agent creates a new `ChatRunPage` with the resolved provider, API key, LLM client, clipboard writer, settings reader, and shared `RunSessionCoordinator`.
5. `ChatRunPage.GetContent()` activates the page once, cancels any older active page through the coordinator, shows `_Preparing..._`, then reads clipboard text.
6. If the user template contains an unescaped `{input}`, `ChatRunPage` shows `RunInputForm` and waits for typed input. Otherwise it starts streaming immediately.
7. The first user message is rendered by `TemplateRenderer.Render`. Replies bypass template rendering and are sent verbatim.
8. `ChatRunPage.BuildRequest()` prepends the system message, global system prefix plus agent system prompt, then appends the page local history.
9. `OpenAiChatClient.StreamAsync()` creates an SDK chat client for the provider base URL and model, sends streaming chat completions, and yields non empty text chunks.
10. `ChatRunPage` retains at most 256,000 characters from an individual assistant response and throttles intermediate markdown renders. When the limit is reached, it stops consuming that response, retains the bounded assistant turn, and displays a truncation notice.
10. `ChatRunPage` mutates `MarkdownContent.Body` and calls `RaiseItemsChanged(0)` on each update. This is the validated markdown refresh path in `docs/decisions.md` and spike evidence.
11. Successful streams append one assistant message to page history and update `_lastAssistantText`. Canceled streams show `_(stopped)_` and don't append an incomplete assistant turn. Provider errors are shown as scrubbed markdown and short toasts.

## Persistence, Secrets, And Settings

Runtime state lives under `PathHelper.LocalStateDir`, which uses `ApplicationData.Current.LocalFolder.Path\LittleAgents` in the packaged app. If Windows app data isn't available, it falls back to `%TEMP%\LittleAgentsExtension\LittleAgents` for test and non packaged contexts.

`agents.json` contains schema version 1 plus the agent array. `providers.json` contains schema version 1 plus provider metadata. Stores write through a temp file and `File.Move(..., overwrite: true)`, then raise `Changed` so list pages refresh.

API keys are not written to `providers.json`. `SecretStoreFactory` first smoke tests Windows PasswordVault and uses `WindowsPasswordVaultSecretStore` when it works. Otherwise it uses `DpapiSecretStore`, which writes DPAPI protected bytes under `LocalStateDir\secrets`. Provider deletion removes the provider secret only when no agents reference that provider.

Settings are stored by `LittleAgentsSettingsManager` in `settings.json`. The current settings are `systemPrefix`, prepended to every agent system prompt, and `temperature`, parsed with invariant culture. Empty, invalid, or out of range temperature text resolves to `1.0`; valid values must be between `0.0` and `2.0`.

## Template Variables And Clipboard Behavior

Current template variables are text only:

`{input}` is text typed by the operator when the agent runs. If the first run template contains an unescaped `{input}`, the run waits for the input form.

`{selection}` is current Windows clipboard text, capped at 8,000 characters. The User32 reader inspects the clipboard allocation size and copies at most 8,001 characters into managed memory, allowing the renderer to detect overflow. Text longer than the cap is replaced with `[truncated to 8000 chars]` followed by the first 8,000 characters.

`{{` and `}}` escape literal braces. Unknown placeholders are preserved. Placeholder names are case sensitive, so `{Input}` is not `{input}`.

Clipboard image to LLM support is not implemented today. `ClipboardReaderTests` cover image only clipboard content returning `null`, and F4 verifies no production `image_url` support exists. Current `{selection}` is text only; images remain scoped out. A future image clipboard feature would need new request models, UI rules, provider support decisions, tests, and scope approval before it becomes current behavior.

## Streaming, Session, Reply, And Copy Behavior

Only one active run owns live streaming output. `RunSessionCoordinator.Activate` cancels the previous active page when another run page is activated, including the path where a second page is waiting for `{input}`.

Each `ChatRunPage` owns its own history. Starting an agent from the list creates a fresh page. Reply stays on the current page, appends a new user turn, sends the full page history, and sends reply text literally without replacing `{selection}` or `{input}`.

Re-run clears current page history and output, then starts the stored initial user message again. Stop cancels the active stream or shows `Nothing to stop` when there is no active stream.

Copy result writes only the latest completed assistant turn. If there is no completed assistant text, it shows `Nothing to copy yet`. Copy transcript writes the full markdown body. Copy uses User32 `CF_UNICODETEXT`, added after Command Palette manual QA showed WinRT clipboard writes did not update the real clipboard from the extension host.

## Error Handling Rules

Provider and request errors must not leak API keys. Error handling removes the exact configured credential and then applies heuristic `sk-` redaction. `ChatRunPage` maps TLS certificate failures to a certificate markdown message and `TLS rejected` toast. HTTP status exceptions map to `Error <status>`. Network failures map to `Network error`. Other errors map to `Error`. Error markdown is secret scrubbed and capped at 400 characters.

The client layer doesn't bypass TLS certificate validation. For loopback providers without a trusted certificate, use `http://localhost`; remote providers require HTTPS.

## Extending Safely

Use `docs/decisions.md` as the map from architectural decisions to code and tests. When changing a behavior covered by a `D-*` row, update the implementation and the tests named there, and keep docs accurate.

For provider changes, preserve the current base URL contract: operators enter the API root, including `/v1` when required, and the SDK request ends at `/chat/completions`. `OpenAiChatClientTests` verify the URI does not become `/v1/v1/chat/completions`.

For prompt rendering changes, update `TemplateRendererTests`, `ChatRunPageInitialRunTests`, and any README text that describes variables. Don't treat image clipboard behavior as present unless production request code actually sends images.

For run behavior changes, check `ChatRunPageStreamingTests`, `ChatRunPageReplyTests`, `ChatRunPageCommandsTests`, and `AgentsListPageInvocationTests`. These tests encode cancellation, page local history, error scrubbing, copy, reply, and runtime settings behavior.

For persistence changes, keep API keys outside JSON and keep provider deletion safe. `ProviderEditFormTests`, `ProviderDeleteOrphanTests`, `DpapiSecretStoreTests`, store tests, and secret store contract tests are the first places to look.

For UI discoverability changes, check pinned navigation and first run tests. F3 manual QA specifically caught missing agent row edit access and copy failures, so keep row `MoreCommands` and copy behavior easy to exercise.

## Developer Testing And Quality Gates

Use these focused test areas before wider runs:

```powershell
dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~TemplateRendererTests"
dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OpenAiChatClientTests"
dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ChatRunPageStreamingTests|FullyQualifiedName~ChatRunPageReplyTests|FullyQualifiedName~ChatRunPageCommandsTests"
dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ProviderEditFormTests|FullyQualifiedName~ProviderDeleteOrphanTests"
```

Then run the full suite and builds:

```powershell
dotnet test "LittleAgentsExtension.Tests\LittleAgentsExtension.Tests.csproj" -c Debug -p:Platform=x64
dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Debug -p:Platform=x64
dotnet build "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -p:Platform=x64
```

The latest checked in evidence records 107 Debug tests passed, Debug build passed with 0 warnings and 0 errors, and Release build passed with 0 errors and known warnings in manual QA evidence. Don't claim a new run passed unless you run it in the current session.

For publish smoke checking from the repository root, F3 used this error scan:

```powershell
dotnet publish "LittleAgentsExtension\LittleAgentsExtension.csproj" -c Release -r win-x64 -p:Platform=x64 2>&1 | Select-String "error"
```

## Manual QA And Evidence Workflow

Manual QA lives in `.omo\evidence\F3-manual-qa.md`. It records real Command Palette smoke testing for first run setup, provider and agent creation, runs, streaming, reply, copy result, copy transcript, edit, delete, reload persistence, and publish error scan. Final F3 status is pass.

Quality evidence lives in `.omo\evidence\F2-code-quality.txt`. It records static quality checks, no oversized non bin/obj C# files, no shipped TODOs, no `#pragma warning disable`, full tests, and builds at the time of that gate.

Scope evidence lives in `.omo\evidence\F4-scope-fidelity.txt`. It records that forbidden scope out patterns such as tools, function calls, images, audio, embeddings, vector stores, and `/v1/models` are absent from production files.

Plan compliance evidence lives in `.omo\evidence\F1-plan-compliance.md`. Current status is blocked, not failed by implementation behavior. All implementation, evidence, decision map, test, and build checks passed in that evidence. The only recorded blocker is the explicit F1 commit log clause, because the repository had only the initial commit and commits were not authorized.

Decision evidence lives in `docs\decisions.md`. Spike evidence called out by F1 includes `.omo\evidence\spike-clipboard-mta.md`, `.omo\evidence\spike-vault-mta.md`, `.omo\evidence\spike-markdown-rerender.md`, `.omo\evidence\task-44-little-agents-mvp.txt`, and `.omo\evidence\task-45-little-agents-mvp.txt`.

When you change behavior, update or add evidence only for what you actually ran. Preserve the distinction between source backed current behavior and future ideas.

## Current Constraints And Non Goals

Images, audio, tools, RAG, document upload, cost tracking, provider model auto discovery, embeddings, vector stores, and model listing are non goals today.

Clipboard selection is text only. Image only clipboard content is treated as no selection.

Provider base URLs must use HTTPS, except that loopback providers may use HTTP. The app does not ignore TLS certificate errors.

Release AOT and trim support is best effort. The project is configured to tolerate documented trim warnings for `OpenAI` and `Microsoft.Extensions.AI.OpenAI` in Release while keeping publish and build errors at zero.

Secrets must not be stored in provider JSON, docs, manifests, evidence, or logs. Use placeholders in publishing docs and examples.

Git history requirements are tracked by evidence, but this handoff does not authorize or prescribe commits.
