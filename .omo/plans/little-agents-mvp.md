# little-agents-mvp - Work Plan

## TL;DR (For humans)

**What you'll get:** A working Command Palette plug-in where you save prompt templates as named agents, point them at any OpenAI-format chat service (yours, OpenRouter, DeepSeek, Ollama, or anything in between), and call them in two keystrokes from the palette. The reply streams in live as markdown — you can copy it, stop it mid-flight, re-run it, or send a follow-up. Clipboard text auto-fills the prompt when you write `{selection}`.

**Why this approach:** Microsoft's own OpenAI .NET client supports a custom endpoint and streaming out of the box, and Microsoft's `IChatClient` adapter makes the whole call a single `await foreach`. We keep API keys in Windows' encrypted credential vault (never on disk) and the rest of your settings as plain JSON files you can back up. Two behaviours that the public docs gloss over — does the page get a teardown signal, does the markdown actually live-update — are nailed down by two short spike tasks **before** we wire the streaming UI, with documented fallbacks for either outcome.

**What it will NOT do:**
- No images, audio, tool/function calling, RAG, document upload.
- No chat history persisted across Command Palette restarts. Each new invocation of an agent starts a fresh conversation and uses a `RunSessionCoordinator` to cancel any previous active run before the new run streams; an old page may remain in the back-stack, but it must be inactive.
- No auto-typing into your foreground app, no automatic model discovery, no cost tracking.
- No TLS certificate bypass — if your local LLM uses a self-signed HTTPS cert, use `http://localhost` instead, or install the cert into Windows' trusted store.

**Effort:** Medium — 46 work tasks plus 4 verification gates plus 3 spike tasks, roughly 2-3 focused days for one developer who already has Visual Studio and PowerToys installed.

**Risk:** Medium — driven by two unverified SDK behaviours (page lifecycle + markdown re-render) that the plan addresses with explicit early spikes. Everything else is well-trodden ground.

**Decisions to sanity-check:**
- We test the LLM client, the JSON store, the template renderer, and the secret-store contract — but **not** the visual pages, because Command Palette has no automated UI test harness. UI is verified by a 15-step manual checklist.
- Ahead-of-Time / trim publishing is best-effort, not a release gate — Microsoft's own AI SDK isn't AOT-tested upstream, so we suppress trim **warnings** for those two assemblies but still require a clean Release build.
- Chat history lives only in the run-page instance. Re-invoking the agent from the list starts a brand-new ChatRunPage (and a fresh history) and cancels the prior active run via `RunSessionCoordinator`; an old page may remain in the back-stack, but it must not keep streaming.

Your next move: say **"start work"** (or `$start-work`) to begin implementation in a fresh worker session. The dual high-accuracy plan review has already been run and applied. Full execution detail follows below.

---

> TL;DR (machine): Effort=Medium · Risk=Medium · 6 components · 46 todos + 3 spikes + 4 final gates · CmdPal extension on net9.0-windows + OpenAI 2.11.0 + Extensions.AI.OpenAI 10.7.0 + PasswordVault + xUnit. Streaming markdown ContentPage; ephemeral multi-turn; AOT best-effort.

## Scope

### Must have
- A working PowerToys Command Palette extension named **Little Agents** that loads after MSIX deploy + Reload, replacing the current placeholder `LittleAgentsExtensionPage`.
- CRUD for **agents**: name, system prompt, user template, providerId, model, optional icon, optional tags. Persisted as JSON under the app-owned `LocalState\LittleAgents` subtree.
- CRUD for **providers**: name, baseUrl, apiKey, optional default model. Provider metadata in JSON; **API key encrypted at rest, never plaintext** — preferred backend is `Windows.Security.Credentials.PasswordVault`, with `DataProtectionApi` (DPAPI, `CurrentUser` scope) as the documented fallback when the Wave-1 vault spike (T45) reports failure.
- Top-level entry as a **`DynamicListPage`** that filters agents by case-insensitive substring on Name + Tags + ProviderId, with pinned items: "+ New Agent", "Manage Providers", "Settings".
- **Streaming chat run page** built on `ContentPage` + a single mutable `MarkdownContent` block; assistant chunks appended via `Body` reassignment.
- **Multi-turn dialogue**: ephemeral `messages[]` history scoped to one `ChatRunPage` instance. "Reply" appends a turn to that instance's history; re-invoking the agent from the list creates a NEW `ChatRunPage` with empty history and cancels the prior active run via `RunSessionCoordinator` (the old instance may persist in the back-stack, but must be inactive — documented, not a bug).
- **Template variables**: `{input}` (user-typed at run time) and `{selection}` (current Windows clipboard text). `{{`/`}}` escape; unknown placeholders pass through verbatim.
- Run-page commands: **Copy result**, **Stop** (cancels in-flight HTTP), **Re-run**, **Reply**.
- LLM client wrapping `OpenAI 2.11.0` + `Microsoft.Extensions.AI.OpenAI 10.7.0` via `chatClient.AsIChatClient()`, custom endpoint via `OpenAIClientOptions { Endpoint = ... }`. Streaming via `IAsyncEnumerable<ChatResponseUpdate>`.
- **Tests-after** xUnit suite (added in the same wave as each component): LLM client (fake `HttpMessageHandler`), JSON store round-trips, `TemplateRenderer`, `ISecretStore` mock contract.
- Visible empty-state, error toasts on LLM 4xx/5xx, friendly orphan-provider blocking, and a "no providers yet" first-launch flow.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- ❌ Image / audio / video input or output.
- ❌ Function calling / tool calling / OpenAI `tools` parameter.
- ❌ RAG / file search / web search / vector stores / embeddings.
- ❌ **Conversation persistence across CmdPal restarts** (multi-turn is ephemeral by design).
- ❌ Agent / provider import-export or sharing.
- ❌ Token usage or cost-tracking display.
- ❌ Provider model auto-discovery (no calls to `/v1/models`).
- ❌ Theme / accent / custom styling beyond Adaptive Cards defaults.
- ❌ SendInput / foreground-app text injection.
- ❌ Auto-copy streaming result to clipboard mid-stream.
- ❌ Multiple simultaneous run pages (one active `ChatRunPage` at a time).
- ❌ Microsoft Store submission (documented in `docs/PUBLISHING.md`, not executed).
- ❌ Self-signed dev cert generation (documented only).
- ❌ Localization beyond English in the Adaptive Card form labels.
- ❌ Telemetry / OpenTelemetry hookup (capable via Extensions.AI; we do NOT wire it).
- ❌ Retry policy on failed requests (one attempt, then user-driven Re-run).
- ❌ Streaming cancellation via `Dispose` on the page (verified-false: CmdPal does NOT call `Dispose` on `ContentPage` — see Decision D-CANCEL below).
- ❌ TLS certificate-validation bypass for provider endpoints. We do NOT install a custom `ServerCertificateCustomValidationCallback`. Self-signed or expired provider certs surface as a clear error (per D-ERR). Users with local providers should use `http://localhost` or install a trusted certificate.

## Verification strategy
> Zero human intervention - all verification is agent-executed unless explicitly named "manual".

- **Test decision:** tests-after, focused. Framework: xUnit + hand-written test doubles (no Moq dependency) — keeps the test project AOT-friendly and trim-safe in case it ever ships.
- **Static gates:** `dotnet build LittleAgentsExtension/LittleAgentsExtension.csproj -c Debug -p:Platform=x64` ⇒ zero errors. `dotnet build ... -c Release -p:Platform=x64` ⇒ zero errors. `dotnet publish ... -c Release -r win-x64 -p:Platform=x64 2>&1 | Select-String "error IL"` ⇒ zero matches (warnings tolerated, see D-AOT).
- **Test gate:** `dotnet test LittleAgentsExtension.Tests/LittleAgentsExtension.Tests.csproj` ⇒ all green; minimum 25 passing tests across the four covered seams.
- **Secret hygiene gate:** after adding a provider with canary API key `sk-test-METIS-CANARY-12345`, run `Get-ChildItem "$env:LOCALAPPDATA\Packages\*\LocalState\LittleAgents" -Recurse -File | Select-String -Pattern "sk-test-METIS-CANARY"`; it must return zero matches across **all Little Agents files**, not just JSON. All app writes are required to live under `LocalState\LittleAgents` via `PathHelper.LocalStateDir`.
- **Manual gates** (named explicitly): F5 deploy + Reload ⇒ extension visible as "Little Agents"; full T43 manual QA checklist.
- **Evidence:** `.omo/evidence/task-<N>-little-agents-mvp.<ext>` per todo.

## Execution strategy

### Parallel execution waves
> Target 5-8 todos per wave. Tests written in the SAME wave as the component they cover (Metis: do not batch all tests into Wave 6).

- **Wave 0 — Hygiene & dependencies (5 todos, all parallel).** Central package versions, csproj refs, .gitignore correction, folder scaffold, test project skeleton.
- **Wave 1 — Storage + spikes (8 todos, mostly parallel).** Domain DTOs + JSON source-gen, AgentStore, ProviderStore, PasswordVault adapter, **two threading spikes** (Clipboard from MTA, PasswordVault from MTA), tests for each store + secret-store contract.
- **Wave 2 — LLM client (5 todos, parallel).** ChatRequest DTOs + ILlmChatClient interface, OpenAiChatClient impl with custom endpoint + streaming, TemplateRenderer for `{input}`/`{selection}` (with 8 000-char cap on `{selection}`), tests for client + renderer.
- **Wave 3 — Top-level page + render-spike (4 todos, mostly serial).** AgentsListPage : DynamicListPage, store.Changed subscription, **MarkdownContent re-render spike + fallback decision** (D-RENDER), CommandsProvider rewiring.
- **Wave 4 — Editors (8 todos, parallel where independent).** AgentEditFormPage + Adaptive Card + SubmitForm, agent delete-with-confirm, ProviderEditFormPage + Adaptive Card (apiKey style=password) + SubmitForm with secret split, provider delete + orphan block, ProvidersListPage, pinned-item navigation wiring.
- **Wave 5 — Chat run page (6 todos, mostly serial).** ChatRunPage scaffold, initial-run flow with input form, streaming loop with cancellation, page commands, multi-turn Reply, AgentsListPage → ChatRunPage wiring.
- **Wave 6 — Polish + docs (6 todos, mostly parallel).** SettingsPage with toolkit Settings and final settings entry wiring, icons + tags polish, EmptyContent for first-launch, README, PUBLISHING.md, manual QA checklist evidence file.

### Decisions (referenced from todos)

- **D-CANCEL — Cancellation does NOT depend on `Dispose`.** Verified by Metis: `ContentPage` and its base classes do not implement `IDisposable`; CmdPal does not call any unload hook. Cancellation triggers are: (a) explicit "Stop" command on the run page; (b) when `StartStream` is called again (Re-run / Reply); (c) when the parent provider's apiKey changes mid-stream (rare). The `CancellationTokenSource` is owned by `ChatRunPage` and renewed on each stream start. **Corollary D-HISTORY**: history clearing is per-instance, not per-navigation. Re-invoking the agent from the list constructs a NEW `ChatRunPage` with empty history. The previous instance may persist in CmdPal's page back-stack until CmdPal is reloaded — this is documented behavior, not a leak.
- **D-RUN-SINGLE — one active run at a time.** Because CmdPal may keep old pages in the navigation back-stack, `LittleAgentsExtensionCommandsProvider` owns a singleton `RunSessionCoordinator`. `AgentsListPage` passes it into every `ChatRunPage`; the page calls `_session.Activate(this)` on first page activation / first synchronous `GetContent()` **before** async clipboard initialization, so even `{input}` agents that show an input form but have not started streaming still cancel the previous active run. `StartStream` may also call `_session.Activate(this)` defensively, but same-page activation is a no-op. `Activate` cancels the previously active page's stream (if any) and marks this page active. Old pages may remain navigable, but their `_cts` is cancelled and they must not keep streaming. This enforces the Must-NOT-have "Multiple simultaneous run pages" without relying on `Dispose`.
- **D-RENDER — `MarkdownContent.Body` mutation may not re-render in 0.5.250829002.** Mitigation: Wave 3 includes a **render spike** (T46) — deploy a tiny test page that mutates `Body` 5×/sec and observe whether CmdPal updates. If it does NOT update, the fallback (folded into T34 by an `if` flag) is to replace the whole `MarkdownContent` instance in `IContent[]` and call `ContentPage.RaiseItemsChanged(0)` per chunk. We commit to whichever path the spike validates.
- **D-AOT — AOT/trim is best-effort, not a hard gate.** Verified by Metis: `Microsoft.Extensions.AI.OpenAI` is explicitly excluded from dotnet/extensions' AOT-compat test app (https://github.com/dotnet/extensions/commit/1073446964db2b0c2bc8a0fcdf7aa6624d91ec30). Strategy: keep `IsAotCompatible=true` for our DTOs (via source-gen `JsonSerializerContext`) and keep `PublishTrimmed=true` in Release. The csproj already sets `<ILLinkTreatWarningsAsErrors>false</ILLinkTreatWarningsAsErrors>` for non-Debug configs (line 92 of the existing file) — that is what actually demotes IL2026/IL3050 from errors to warnings during trimming. The `<TrimmerSingleWarn Include="..."/>` items are a **noise-reduction** mechanism (consolidate per-assembly warnings into one), NOT a severity-demotion mechanism. So Release publish flow is: `ILLinkTreatWarningsAsErrors=false` (already set) → IL warnings allowed; `<TrimmerSingleWarn>` for `OpenAI` and `Microsoft.Extensions.AI.OpenAI` → cleaner build log. Release **errors** still must be zero; IL warnings are tolerated. Document the trade-off in README.
- **D-EXP — `OPENAI001` Experimental warnings.** Suppress at csproj level: `<NoWarn>$(NoWarn);OPENAI001</NoWarn>`. Justified because we explicitly opt in to the surface.
- **D-CLIP — Clipboard from MTA.** Wave 1 spike (T44) verifies whether `Clipboard.GetContent().GetTextAsync()` works from the extension's `[MTAThread]`. If it throws `RPC_E_WRONG_THREAD` or similar, the fallback is to marshal via `DispatcherQueue.GetForCurrentThread()` or fall back to `User32` `OpenClipboard` P/Invoke. **`{selection}` is in scope** but its implementation depends on the spike result.
- **D-VAULT — Secret store backend is Vault-or-DPAPI, both pass.** Wave 1 spike (T45) calls `new PasswordVault()` from the extension's MTA startup. If it works → `WindowsPasswordVaultSecretStore` is the production `ISecretStore`. If it fails (any `COMException` or `RPC_E_*`) → `DpapiSecretStore` is the production `ISecretStore` (`ProtectedData.Protect(bytes, optionalEntropy: providerId-utf8, DataProtectionScope.CurrentUser)`, written to `Path.Combine(PathHelper.LocalStateDir, "secrets", "{providerId}.bin")`, i.e. under `LocalState\LittleAgents\secrets`). Both backends satisfy the success criterion "API key encrypted at rest, never plaintext" — the canary-grep gate (line 69) is the actual contract. The plan does NOT stop-and-replan on vault failure; the worker chooses the spike-validated backend in T28's wiring.
- **D-FORM-REFRESH — Form-to-list refresh.** The Agent/Provider edit forms do NOT hold a parent reference. Instead, each list page subscribes to `agentStore.Changed` / `providerStore.Changed` and calls `RaiseItemsChanged(-1)` on the event. The form's `SubmitForm` only calls `store.Upsert(...)` — the store fires `Changed` — the list refreshes. This is the canonical Toolkit pattern and avoids cross-page references.
- **D-SEL-CAP — `{selection}` size cap.** 8 000 characters. If clipboard text exceeds, truncate and prepend `[truncated to 8000 chars]\n` to the substituted value, plus a `ShowToast` warning to the user.
- **D-ORPHAN — Orphaned providers.** Deleting a provider with referencing agents is **blocked** with a `ShowToast` listing affected agent names. User must reassign or delete those agents first.
- **D-FIRSTRUN — Zero providers.** When `providerStore.Load().Length == 0`, AgentsListPage's `EmptyContent` directs the user to "Manage Providers" and the "+ New Agent" pinned item, when invoked, shows a `ShowToast` "Add a provider first" and navigates to ProviderEditFormPage instead.
- **D-ERR — LLM 4xx/5xx surface.** No retry. Map exceptions in this priority: (1) TLS chain validation failure found anywhere in the inner-exception chain (`AuthenticationException`, `CryptographicException`, or certificate-text match) → append `> **Provider TLS certificate rejected.** Use a trusted certificate or `http://localhost` for local servers.`; (2) `Microsoft.Extensions.AI` / OpenAI client SDK exceptions that expose an HTTP status → append `> **Error <status>:** <body excerpt ≤ 400 chars>` (extract `status` and `body` via SDK-specific properties; if the body is unavailable, use the exception's localized `Message` truncated to 400 chars and elide any `Bearer` / `sk-` substring); (3) `HttpRequestException` with no TLS inner found (network down, DNS failure) → append `> **Network error:** <Message ≤ 400 chars>`; (4) any other `Exception` → append `> **Error:** <Message ≤ 400 chars>`. For all four cases also call `ShowToast` with the short status. **Never** emit the apiKey value into any branch's text; the canary log-grep test in T18 enforces this.
- **D-BASEURL — Provider `BaseUrl` semantics (concrete examples).** `ProviderDef.BaseUrl` is the **OpenAI-compatible API root, including `/v1`** (or whatever versioned path the provider uses). Concrete worked examples — given the user enters BaseUrl `https://api.openai.com/v1`, the SDK MUST issue `POST https://api.openai.com/v1/chat/completions` (i.e. the SDK/request layer appends exactly `/chat/completions` after the configured API root and must not append a second `/v1`). Other valid BaseUrl inputs: `https://openrouter.ai/api/v1` → `POST https://openrouter.ai/api/v1/chat/completions`; `http://localhost:11434/v1` → `POST http://localhost:11434/v1/chat/completions`. **Anti-pattern to test against**: a worker who manually concatenates `/v1` produces `https://api.openai.com/v1/v1/chat/completions` — T18 includes a negative assertion that the request URI does NOT match `^.*/v1/v1/chat/completions$`. T16 passes `provider.BaseUrl` verbatim to `OpenAIClientOptions.Endpoint`. T28's UI placeholder is `https://api.openai.com/v1` with hint text instructing the user to include the `/v1`.

### Dependency matrix
> Showing only edges that span waves; intra-wave parallelism is in each todo's `Parallelization:` line.

| Wave | Blocks | Blocked by |
|------|--------|-----------|
| Wave 0 | Wave 1, Wave 2 | — |
| Wave 1 | Wave 3 (T20), Wave 4 (T25, T28, T29), Wave 5 (T32) | Wave 0 |
| Wave 2 | Wave 5 (T34) | Wave 0 |
| Wave 3 | Wave 4 (T30), Wave 5 (T37) | Wave 1 |
| Wave 4 | Wave 5 (T37 wiring) | Wave 1, Wave 3 |
| Wave 5 | Wave 6 (T43 QA) | Wave 2, Wave 3, Wave 4 |
| Wave 6 | F1, F3 | Wave 5 |

## Todos
> Implementation + Test = ONE todo where reasonable. Each todo is 1-3 tool-call atomic. Files ≤ 250 LoC.

### Wave 0 — Hygiene & dependencies

- [x] 1. `Directory.Packages.props`: add OpenAI 2.11.0 + Microsoft.Extensions.AI.OpenAI 10.7.0
  What to do / Must NOT do: append `<PackageVersion Include="OpenAI" Version="2.11.0" />` and `<PackageVersion Include="Microsoft.Extensions.AI.OpenAI" Version="10.7.0" />` to the existing `<ItemGroup>` (lines 5-17). Must NOT bump existing pins.
  Parallelization: Wave 0 | Blocked by: — | Blocks: T2, T5 | Parallel with: T3, T4
  References: `Directory.Packages.props:5-17`; https://www.nuget.org/packages/OpenAI/2.11.0; https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/10.7.0
  Acceptance criteria (agent-executable): `dotnet restore LittleAgentsExtension/LittleAgentsExtension.csproj 2>&1` exits 0; `Select-String -Path Directory.Packages.props -Pattern '"OpenAI"'` returns ≥ 1 match.
  QA: happy = restore green; failure = pin a non-existent version like `999.0.0`, expect NU1102. Evidence `.omo/evidence/task-1-little-agents-mvp.txt`.
  Commit: Y | `chore(deps): add OpenAI 2.11.0 and Microsoft.Extensions.AI.OpenAI 10.7.0 to central package management`

- [x] 2. csproj: reference both packages + suppress OPENAI001 + best-effort trim warnings
  What to do / Must NOT do: in `LittleAgentsExtension/LittleAgentsExtension.csproj` ItemGroup at line 40: add `<PackageReference Include="OpenAI" />` and `<PackageReference Include="Microsoft.Extensions.AI.OpenAI" />`. Add `<NoWarn>$(NoWarn);OPENAI001</NoWarn>` to first PropertyGroup. Add `<TrimmerSingleWarn Include="OpenAI" />` and `<TrimmerSingleWarn Include="Microsoft.Extensions.AI.OpenAI" />` in a new ItemGroup, gated by `Condition="'$(Configuration)'=='Release'"`. Must NOT remove `IsAotCompatible=true` or `PublishTrimmed=true`.
  Parallelization: Wave 0 | Blocked by: T1 | Blocks: T6, T14, T15
  References: `LittleAgentsExtension/LittleAgentsExtension.csproj:40-50, 61-93`; D-EXP, D-AOT decisions above; https://github.com/dotnet/extensions/commit/1073446964db2b0c2bc8a0fcdf7aa6624d91ec30
  Acceptance: `dotnet build LittleAgentsExtension/LittleAgentsExtension.csproj -c Debug -p:Platform=x64 2>&1 | Select-String "error"` returns zero matches.
  QA: happy = Debug build green; failure = remove central PackageVersion entry, expect NU1604. Evidence `.omo/evidence/task-2-little-agents-mvp.txt`.
  Commit: Y | `feat(deps): reference OpenAI + Extensions.AI.OpenAI; suppress OPENAI001 and trim warnings`

- [x] 3. `.gitignore`: stop ignoring `launchSettings.json` and `*.pubxml`
  What to do / Must NOT do: search repo root for `.gitignore`. If found, remove lines `**/Properties/launchSettings.json` and `*.pubxml` (per official cmdpal guidance). If absent, skip silently. Must NOT add a `.gitignore` if none exists.
  Parallelization: Wave 0 | Blocked by: — | Blocks: — | Parallel with: T1, T4
  References: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/creating-an-extension (section "Without it, anyone who clones your repo won't be able to deploy your extension")
  Acceptance: if `.gitignore` exists, `Select-String -Path .gitignore -Pattern "launchSettings\.json"` returns 0 matches; `Test-Path LittleAgentsExtension/Properties/launchSettings.json` returns True.
  QA: Evidence `.omo/evidence/task-3-little-agents-mvp.txt`.
  Commit: Y if changed | `chore: stop ignoring launchSettings.json and pubxml so Visual Studio Deploy works`

- [x] 4. Folder scaffold under `LittleAgentsExtension/`
  What to do / Must NOT do: create folders `Llm/`, `Storage/`, `Pages/Agents/`, `Pages/Providers/`, `Pages/Run/`. Add a `.gitkeep` to each so git tracks them. Must NOT touch the existing `Pages/LittleAgentsExtensionPage.cs` yet (T20 will replace it).
  Parallelization: Wave 0 | Blocked by: — | Blocks: T6, T14, T20, T24, T27, T32
  References: programming-skill solution-layout convention; topology lock C1–C6.
  Acceptance: all five `Test-Path` checks return True.
  QA: Evidence `.omo/evidence/task-4-little-agents-mvp.txt`.
  Commit: Y | `chore(structure): scaffold Llm/Storage/Pages subfolders`

- [x] 5. Add `LittleAgentsExtension.Tests` xUnit project to solution
  What to do / Must NOT do: at solution root create `LittleAgentsExtension.Tests/LittleAgentsExtension.Tests.csproj` targeting `net9.0-windows10.0.26100.0` so it can reference the production `LittleAgentsExtension.csproj` target (`net9.0-windows10.0.26100.0`) without TFM incompatibility. Reference `LittleAgentsExtension.csproj`. Add `<PackageReference Include="xunit" />`, `<PackageReference Include="xunit.runner.visualstudio" />`, `<PackageReference Include="Microsoft.NET.Test.Sdk" />`. Add corresponding `<PackageVersion>` entries to `Directory.Packages.props` (xunit 2.9.x, runner 2.8.x, Test.Sdk 17.11.x). Add the new project to `LittleAgentsExtension.sln`. Must NOT add Moq (we hand-roll test doubles).
  Parallelization: Wave 0 | Blocked by: T1 | Blocks: T11, T12, T13, T18, T19
  References: `LittleAgentsExtension.sln:1-43`; `Directory.Packages.props:5-17`.
  Acceptance: `dotnet test LittleAgentsExtension.Tests/LittleAgentsExtension.Tests.csproj 2>&1 | Select-String "Passed!"` (with no tests yet, output is "Passed!  - Failed: 0, Passed: 0").
  QA: happy = test runner discovers 0 tests cleanly; failure = remove xunit ref, expect CS0246. Evidence `.omo/evidence/task-5-little-agents-mvp.txt`.
  Commit: Y | `chore(tests): add LittleAgentsExtension.Tests xUnit project`

### Wave 1 — Storage + threading spikes

- [x] 6. Domain DTOs in `Storage/Models.cs`
  What to do / Must NOT do: add `internal sealed record AgentDef(string Id, string Name, string SystemPrompt, string UserTemplate, string ProviderId, string Model, string? Icon, string[] Tags)`, `internal sealed record ProviderDef(string Id, string Name, string BaseUrl, string? DefaultModel)`, `internal sealed record StoredChatMessage(string Role, string Content)`, `internal sealed record AgentsFile(int SchemaVersion, AgentDef[] Agents)`, `internal sealed record ProvidersFile(int SchemaVersion, ProviderDef[] Providers)`. Must NOT include any apiKey-shaped field on `ProviderDef` — secrets live exclusively in `ISecretStore` (per D-VAULT, the runtime backend is either `WindowsPasswordVaultSecretStore` or `DpapiSecretStore`).
  Parallelization: Wave 1 | Blocked by: T2, T4 | Blocks: T7, T8, T9, T16, T17, T25, T28
  References: programming skill (records, branded ids); user request "保存多个 prompts 为 agent".
  Acceptance: `dotnet build` Debug green; file ≤ 80 LoC.
  QA: Evidence `.omo/evidence/task-6-little-agents-mvp.txt`. Commit: Y | `feat(storage): AgentDef / ProviderDef / StoredChatMessage records`

- [x] 7. AOT JSON source-gen: `Storage/LittleAgentsJsonContext.cs`
  What to do / Must NOT do: `[JsonSerializable(typeof(AgentsFile))] [JsonSerializable(typeof(ProvidersFile))] [JsonSerializable(typeof(StoredChatMessage))] [JsonSourceGenerationOptions(WriteIndented = true)] internal partial class LittleAgentsJsonContext : JsonSerializerContext { }`. Must NOT use reflection-based `JsonSerializer.Serialize<T>(obj)`; always pass the source-generated context.
  Parallelization: Wave 1 | Blocked by: T6 | Blocks: T8, T9
  References: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation; csproj `IsAotCompatible=true`; D-AOT.
  Acceptance: Release build (`dotnet build -c Release -p:Platform=x64`) emits zero IL2026/IL3050 errors mentioning these types.
  QA: happy = Release green; failure = remove `[JsonSerializable]`, expect IL2026 on `JsonSerializer.Serialize<AgentsFile>`. Evidence `.omo/evidence/task-7-little-agents-mvp.txt`. Commit: Y | `feat(storage): AOT-safe JsonSerializerContext for all persisted DTOs`

- [x] 8. `Storage/AgentStore.cs` — atomic JSON store
  What to do / Must NOT do: `internal sealed class AgentStore`. Methods: `AgentDef[] Load()`, `void Save(AgentDef[])` (atomic via tmp + `File.Move(tmp, target, overwrite: true)`), `void Upsert(AgentDef)`, `void Delete(string id)`, `event EventHandler? Changed`. Add/consume `LittleAgentsExtension.Common.PathHelper.LocalStateDir`, defined as `Path.Combine(ApplicationData.Current.LocalFolder.Path, "LittleAgents")` with a temp-folder fallback for unpackaged dev/test runs; the helper creates the directory before returning it. Path = `Path.Combine(PathHelper.LocalStateDir, "agents.json")`. SchemaVersion=1. Use `LittleAgentsJsonContext.Default.AgentsFile`. Concurrent writes guarded by a private `lock`. Must NOT swallow IO exceptions silently. Must NOT write outside the `LocalState\LittleAgents` subtree.
  Parallelization: Wave 1 | Blocked by: T7 | Blocks: T11, T20, T25, T26, T29
  References: https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ExtensionTemplate/TemplateCmdPalExtension/SettingsManager.cs (path convention); D-FORM-REFRESH.
  Acceptance: T11 tests pass; file ≤ 200 LoC.
  QA: see T11. Evidence `.omo/evidence/task-8-little-agents-mvp.txt`. Commit: Y | `feat(storage): AgentStore with atomic writes and Changed event`

- [x] 9. `Storage/ProviderStore.cs` — mirror of AgentStore for ProviderDef
  What to do / Must NOT do: identical shape and contract as T8 but for `ProviderDef[]` and `providers.json`. Methods: `ProviderDef[] Load()`, `void Save(ProviderDef[])`, `void Upsert(ProviderDef)`, `void Delete(string id)`, `event EventHandler? Changed`. Same atomic-write pattern (tmp + Move), same `lock`-guarded mutation, same `LittleAgentsJsonContext.Default.ProvidersFile` source-gen usage. Path = `Path.Combine(PathHelper.LocalStateDir, "providers.json")`. Must NOT swallow IO exceptions silently. Must NOT include any apiKey field — apiKey lives in `ISecretStore` only. Must NOT write outside the `LocalState\LittleAgents` subtree.
  Parallelization: Wave 1 | Blocked by: T7 | Blocks: T12, T20, T28, T29, T31
  References: T8 (the symmetric AgentStore implementation); D-FORM-REFRESH; D-VAULT (apiKey is OUT of scope here).
  Acceptance criteria (agent-executable): T12 passes (≥ 6 tests); file ≤ 200 LoC (`(Get-Content LittleAgentsExtension/Storage/ProviderStore.cs).Count -le 200`).
  QA: see T12. Evidence `.omo/evidence/task-9-little-agents-mvp.txt`.
  Commit: Y | `feat(storage): ProviderStore with atomic writes and Changed event`

- [x] 10. `Storage/ISecretStore.cs` + `Storage/WindowsPasswordVaultSecretStore.cs`
  What to do / Must NOT do: interface methods `void Set(string providerId, string apiKey)`, `string? TryGet(string providerId)`, `void Delete(string providerId)`. Resource = `$"LittleAgents.Provider.{providerId}"`, username = `"apikey"`. `WindowsPasswordVaultSecretStore` wraps `Windows.Security.Credentials.PasswordVault`. `TryGet` returns `null` when `Element not found` (`COMException` HResult `0x80070490`). Must NOT log the apiKey value in any exception path. The DPAPI implementation lives in T45 (`DpapiSecretStore`); `SecretStoreFactory.Create()` (also in T45) selects the runtime backend per the spike result.
  Parallelization: Wave 1 | Blocked by: T6 | Blocks: T13, T16, T28, T29
  References: https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker; D-VAULT.
  Acceptance criteria (agent-executable): T13 passes (≥ 4 contract tests on `InMemorySecretStore`); the Vault implementation compiles and the smoke run from T45 produces an `.omo/evidence/spike-vault-mta.md` verdict — **either Vault succeeds (then `WindowsPasswordVaultSecretStore` is the production backend) OR Vault fails (then `DpapiSecretStore` is the production backend)**. Both outcomes are accepted per D-VAULT; the canary-grep gate (line 69) is the actual contract.
  QA: happy = T13 green AND a spike verdict file exists; failure = if `SecretStoreFactory.Create()` returns null instead of one of the two impls, expect a unit-level smoke to fail. Evidence `.omo/evidence/task-10-little-agents-mvp.txt`.
  Commit: Y | `feat(storage): ISecretStore + PasswordVault adapter (DPAPI fallback in T45)`

- [x] 11. xUnit: `LittleAgentsExtension.Tests/AgentStoreTests.cs`
  What to do / Must NOT do: tests = empty-load returns []; save+load round-trip; upsert replaces by Id; delete removes; corrupt JSON throws `InvalidDataException`; concurrent saves don't corrupt the file (use `Parallel.For` with `lock`-protected store). Each test gets a fresh temp directory via `Path.GetTempFileName()` style. Must NOT depend on real `LocalFolder`.
  Parallelization: Wave 1 | Blocked by: T5, T8 | Blocks: F2
  References: T8; T12 (sister ProviderStore test); xUnit fact/theory pattern.
  Acceptance criteria (agent-executable): `dotnet test --filter "FullyQualifiedName~AgentStoreTests"` reports ≥ 6 passed, 0 failed.
  QA: happy = all 6 pass; failure = corrupt the JSON in the temp file before load, expect `InvalidDataException`. Evidence `.omo/evidence/task-11-little-agents-mvp.txt`.
  Commit: Y | `test(storage): AgentStore round-trip and edge-case tests`

- [x] 12. xUnit: `LittleAgentsExtension.Tests/ProviderStoreTests.cs` (mirror of T11)
  What to do / Must NOT do: tests = empty-load returns []; save+load round-trip; upsert replaces by Id; delete removes; corrupt JSON throws `InvalidDataException`; concurrent saves don't corrupt the file. Each test gets a fresh temp directory. Must NOT depend on real `LocalFolder` and Must NOT include any apiKey-shaped string in test fixtures.
  Parallelization: Wave 1 | Blocked by: T5, T9 | Blocks: F2
  References: T9; T11 (sister test); xunit fact/theory pattern.
  Acceptance criteria (agent-executable): `dotnet test --filter "FullyQualifiedName~ProviderStoreTests"` reports ≥ 6 passed, 0 failed.
  QA: happy = all 6 pass; failure = corrupt the JSON in the temp file before load, expect `InvalidDataException`. Evidence `.omo/evidence/task-12-little-agents-mvp.txt`.
  Commit: Y | `test(storage): ProviderStore round-trip and edge-case tests`

- [x] 13. xUnit: `LittleAgentsExtension.Tests/SecretStoreContractTests.cs` + in-memory impl
  What to do / Must NOT do: define `internal sealed class InMemorySecretStore : ISecretStore` for tests. Tests = set/get round-trip; delete then TryGet returns null; multiple providers don't collide; setting twice for the same providerId overwrites. Must NOT call the real `PasswordVault` from xUnit (non-deterministic across CI environments). Must NOT log apiKey values from the test name into stdout.
  Parallelization: Wave 1 | Blocked by: T5, T10 | Blocks: F2
  References: T10; D-VAULT.
  Acceptance criteria (agent-executable): `dotnet test --filter "FullyQualifiedName~SecretStoreContractTests"` reports ≥ 4 passed, 0 failed.
  QA: happy = all 4 pass; failure = make `Set` not overwrite, expect "set twice" test to fail. Evidence `.omo/evidence/task-13-little-agents-mvp.txt`.
  Commit: Y | `test(storage): ISecretStore contract tests via in-memory impl`

- [ ] 44. **Spike: Clipboard from MTA thread + binary-data handling** (D-CLIP)
  What to do / Must NOT do: add `LittleAgentsExtension/Llm/ClipboardReader.cs` with `internal static async Task<string?> TryGetTextAsync()`. Implementation:
  ```csharp
  // First try the WinRT path
  try {
    var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
    if (!content.Contains(StandardDataFormats.Text)) return null;  // binary-only clipboard → null
    return await content.GetTextAsync();
  } catch (COMException) { /* fall through to P/Invoke */ }
  catch (InvalidOperationException) { /* fall through */ }

  // Fallback: User32 OpenClipboard / GetClipboardData(CF_UNICODETEXT)
  // Use CsWin32-generated PInvoke (csproj already has Microsoft.Windows.CsWin32).
  // Returns null if format CF_UNICODETEXT is absent (binary-only clipboard).
  ```
  **Binary-data invariant**: the method MUST return `null` when the clipboard contains only non-text formats (image bitmaps, file drops). Never throws on binary content. **Document the result in `.omo/evidence/spike-clipboard-mta.md`**: which path worked from the extension's MTA thread? Pick that path and remove the other. Must NOT swallow the spike result silently. Must NOT use `GetTextAsync()` without first checking `Contains(StandardDataFormats.Text)`.
  Parallelization: Wave 1 | Blocked by: T4 | Blocks: T17 (TemplateRenderer's selection path)
  References: https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboard; https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.standarddataformats; D-CLIP.
  Acceptance criteria (agent-executable): xUnit tests in `ClipboardReaderTests.cs` (using a test-only `IClipboardSource` shim) verify (a) text content returns the text; (b) image-only clipboard (mock `Contains(Text) == false`) returns null without throwing; (c) empty clipboard returns null. PLUS manual smoke per D-CLIP: F5 deploy, copy "hello" → debug agent calls `ClipboardReader.TryGetTextAsync()` → returns "hello".
  QA: happy = all unit tests + smoke pass; failure = remove the `Contains(Text)` guard, expect `GetTextAsync` to throw on image clipboard. Evidence `.omo/evidence/spike-clipboard-mta.md` plus `.omo/evidence/task-44-little-agents-mvp.txt`.
  Commit: Y | `feat(llm): ClipboardReader with binary-safe fallback (spike + tests)`

- [x] 45. **Spike: PasswordVault from MTA thread + always-deliver DpapiSecretStore** (D-VAULT)
  What to do / Must NOT do: two parts:

  **(a) Smoke test.** Add `LittleAgentsExtension/Storage/SecretStoreSmokeTest.cs` (internal, callable from `Program.Main` with a hidden `--smoke-vault` arg). It does `var v = new PasswordVault(); v.Add(new("LittleAgents.Smoke", "u", "p")); var c = v.Retrieve("LittleAgents.Smoke", "u"); v.Remove(c);` and writes success/failure + the sanitized exception (if any) to `Path.Combine(PathHelper.LocalStateDir, "spike-vault-mta.log")` (`LocalState\LittleAgents\spike-vault-mta.log`). **Document result in `.omo/evidence/spike-vault-mta.md`**.

  **(b) Always implement BOTH backends.** Per D-VAULT, the plan does not stop-and-replan on vault failure — both stores are coded up front. Add `LittleAgentsExtension/Storage/DpapiSecretStore.cs` implementing `ISecretStore` via `System.Security.Cryptography.ProtectedData.Protect(plaintext, optionalEntropy: Encoding.UTF8.GetBytes(providerId), DataProtectionScope.CurrentUser)`, written atomically (tmp + Move) to `Path.Combine(PathHelper.LocalStateDir, "secrets", "{providerId}.bin")` (`LocalState\LittleAgents\secrets\...`). `TryGet` calls `ProtectedData.Unprotect`; on `CryptographicException` return null. `Delete` deletes the file. Then add `Storage/SecretStoreFactory.cs` with `internal static ISecretStore Create()` that calls the smoke logic ONCE on first call and caches the choice: vault works → return `WindowsPasswordVaultSecretStore`; vault fails → return `DpapiSecretStore`. T28's wiring uses `SecretStoreFactory.Create()`.

  Must NOT: log the smoke probe's plaintext "p" if the vault leaks the entire credential to the log; sanitize before writing.
  Parallelization: Wave 1 | Blocked by: T10 | Blocks: T28 (provider create wires the spike-validated backend via SecretStoreFactory).
  References: https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker; https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata; D-VAULT.
  Acceptance criteria (agent-executable): unit tests in `DpapiSecretStoreTests.cs` (skipped on non-Windows CI via `[OSPlatform("windows")]` xUnit attribute) cover: round-trip set/get/delete; tampered file → `TryGet` returns null; `Delete` then `TryGet` returns null. PLUS manual smoke: run `LittleAgentsExtension.exe --smoke-vault`, inspect log, then `Get-Content "$env:LOCALAPPDATA\Packages\*\LocalState\LittleAgents\spike-vault-mta.log"` shows either `OK` or a captured sanitized exception. Evidence `.omo/evidence/spike-vault-mta.md` plus `.omo/evidence/task-45-little-agents-mvp.txt`.
  QA: happy = both backends tested green + smoke logs OK or captured failure; failure = corrupt a `.bin` file's first byte → `TryGet` returns null (negative test). Evidence as above.
  Commit: Y | `feat(storage): PasswordVault smoke + DpapiSecretStore fallback + SecretStoreFactory (D-VAULT)`

### Wave 2 — LLM client

- [x] 14. `Llm/ChatRequest.cs` — request DTOs
  What to do / Must NOT do: `internal sealed record ChatRequest(string Model, ChatMessage[] Messages, double? Temperature)`, `internal sealed record ChatMessage(ChatRole Role, string Content)`, `internal enum ChatRole { System, User, Assistant }`. Keep separate from persistence DTOs (`StoredChatMessage`) to avoid coupling LLM-protocol shape to file format. Must NOT add `tools`, `functions`, `images`, `audio`, or `response_format` fields — those are explicitly Scope-OUT.
  Parallelization: Wave 2 | Blocked by: T2 | Blocks: T15, T16
  References: T6 (Storage/Models.cs — for distinction); programming-skill records.
  Acceptance criteria (agent-executable): `dotnet build` Debug green; `(Get-Content LittleAgentsExtension/Llm/ChatRequest.cs).Count -le 30`; grep `Select-String -Path LittleAgentsExtension/Llm/ChatRequest.cs -Pattern "tools|function|image|audio|response_format"` returns 0 matches.
  QA: happy = all three checks green; failure = add a `Tools` field, expect grep to fail. Evidence `.omo/evidence/task-14-little-agents-mvp.txt`.
  Commit: Y | `feat(llm): ChatRequest and ChatMessage DTOs (separated from persistence)`

- [x] 15. `Llm/ILlmChatClient.cs`
  What to do / Must NOT do: `internal interface ILlmChatClient { IAsyncEnumerable<string> StreamAsync(ChatRequest request, ProviderDef provider, string apiKey, [EnumeratorCancellation] CancellationToken ct); }`. Must NOT leak SDK types (`OpenAI.Chat.ChatClient`, `Microsoft.Extensions.AI.IChatClient`, `OpenAIClientOptions`) across this boundary — anything that returns `using OpenAI.*` or `using Microsoft.Extensions.AI.*` from the public signature is a violation.
  Parallelization: Wave 2 | Blocked by: T14 | Blocks: T16, T34
  References: T14; programming-skill hexagonal seam.
  Acceptance criteria (agent-executable): `(Get-Content LittleAgentsExtension/Llm/ILlmChatClient.cs).Count -le 25`; `Select-String -Path LittleAgentsExtension/Llm/ILlmChatClient.cs -Pattern "OpenAI\.|Microsoft\.Extensions\.AI"` returns 0 matches outside of comments.
  QA: happy = both green; failure = leak `IChatClient` from the signature, expect grep to fail. Evidence `.omo/evidence/task-15-little-agents-mvp.txt`.
  Commit: Y | `feat(llm): ILlmChatClient streaming interface (no SDK leakage)`

- [x] 16. `Llm/OpenAiChatClient.cs` — implementation
  What to do / Must NOT do: implement `ILlmChatClient` by constructing `new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(provider.BaseUrl) }).AsIChatClient()`. **Per D-BASEURL: pass `provider.BaseUrl` verbatim to `Endpoint`. Do NOT append `/v1` and do NOT strip trailing slashes; the SDK handles path resolution and appends `/chat/completions`.** Convert our `ChatMessage[]` → `Microsoft.Extensions.AI.ChatMessage[]` (System → `ChatRole.System`, User → `ChatRole.User`, Assistant → `ChatRole.Assistant`). Stream: `await foreach (var update in client.GetStreamingResponseAsync(messages, new ChatOptions { Temperature = (float?)request.Temperature }, ct)) { var text = update.Text; if (!string.IsNullOrEmpty(text)) yield return text; }`. Wrap construction errors (invalid URL, etc.) → `InvalidOperationException("Provider misconfigured: ...")`. Must NOT log the apiKey in any exception or trace; on any caught exception, scrub the message via `Regex.Replace(msg, @"(?i)(bearer\s+)?sk-[A-Za-z0-9_-]{4,}", "***")` before re-throwing.
  Parallelization: Wave 2 | Blocked by: T15, T10 | Blocks: T18, T34
  References: https://github.com/dotnet/extensions/blob/main/src/Libraries/Microsoft.Extensions.AI.OpenAI/README.md (chat-streaming snippet); https://github.com/openai/openai-dotnet/blob/main/README.md (custom base URL — `Endpoint = new Uri("BASE_URL")`); D-EXP, D-BASEURL.
  Acceptance criteria (agent-executable): T18 passes (≥ 7 tests); file ≤ 150 LoC (verify with `(Get-Content LittleAgentsExtension/Llm/OpenAiChatClient.cs).Count -le 150`).
  QA: happy = T18's mock-handler tests pass and verify request URL exactly equals the expected API-root-plus-chat path (for example, `https://api.example.com/v1/chat/completions`) and does NOT contain a doubled `/v1/v1/`; failure = pass `BaseUrl="not-a-url"`, expect `InvalidOperationException` thrown at construction. Evidence `.omo/evidence/task-16-little-agents-mvp.txt`.
  Commit: Y | `feat(llm): OpenAiChatClient with custom endpoint and streaming (D-BASEURL)`

- [x] 17. `Llm/TemplateRenderer.cs`
  What to do / Must NOT do: `internal static class TemplateRenderer { public static string Render(string template, string? input, string? selection) { ... } }`. Inputs are taken as STRING parameters (no clipboard call inside the renderer — that lives in T44; the caller fetches `selection` via `ClipboardReader.TryGetTextAsync()` and passes it in). Replace `{input}` with `input ?? ""`; `{selection}` with `selection ?? ""` truncated per D-SEL-CAP (8 000 chars; if truncation happens, prepend `[truncated to 8000 chars]\n`). Honor `{{` → `{` and `}}` → `}` literal escapes. Unknown `{xxx}` placeholders pass through verbatim. Reference impl shape: scan the template once, switch on the brace token. Must NOT call `ClipboardReader` from inside `TemplateRenderer` (keeps the renderer pure for testing).
  Parallelization: Wave 2 | Blocked by: T2, T44 | Blocks: T19, T33
  References: D-SEL-CAP; T44 (clipboard reader, called by the caller).
  Acceptance criteria (agent-executable): T19 passes (≥ 9 tests); `(Get-Content LittleAgentsExtension/Llm/TemplateRenderer.cs).Count -le 100`; `Select-String -Path LittleAgentsExtension/Llm/TemplateRenderer.cs -Pattern "ClipboardReader|GetTextAsync"` returns 0 matches (renderer must not import the clipboard).
  QA: happy = both checks green and T19 green; failure = inline `ClipboardReader.TryGetTextAsync().Result` in the body, expect the grep assertion to fail. Evidence `.omo/evidence/task-17-little-agents-mvp.txt`.
  Commit: Y | `feat(llm): TemplateRenderer for {input}/{selection} with cap and escapes (pure function)`

- [x] 18. xUnit: `OpenAiChatClientTests.cs` with fake HTTP handler
  What to do / Must NOT do: write `internal sealed class FakeHttpHandler : HttpMessageHandler` that records the last request and emits SSE chunks (`data: {"choices":[{"delta":{"content":"foo"}}]}\n\n` × N + `data: [DONE]\n\n`). Inject via `OpenAIClientOptions { Transport = ... }` if the SDK exposes a transport hook, OR via `HttpClient` factory if the SDK accepts one, OR (fallback) via a thin `IHttpHandler`-style abstraction inserted between `OpenAiChatClient` and the SDK; pick the first that the SDK actually allows and document the choice in the test file's class summary.     Tests required: (a) **request URI exactly equals `https://api.example.com/v1/chat/completions`** when `BaseUrl="https://api.example.com/v1"` (assert via `Assert.Equal(new Uri("https://api.example.com/v1/chat/completions"), recordedRequest.RequestUri)`); (a-neg) **negative assertion**: with the same BaseUrl, the recorded URI does NOT match the regex `^.+/v1/v1/chat/completions$` (catches the worker who manually appends `/v1`); (b) Authorization header is `Bearer <key>`; (c) request body has `stream: true` and the messages we passed; (d) SSE response of 3 chunks is yielded as 3 strings in order; (e) cancellation mid-stream stops yielding within 200 ms AND sets `CancellationToken.IsCancellationRequested == true` on the handler's recorded request; (f) HTTP 401 → exception thrown, `ex.Message` does NOT contain the canary `sk-test-LOG-CANARY-99999` (assert via `Assert.DoesNotContain`); (g) HTTP 429 → exception with status code accessible (either via `RequestFailedException.Status` or by inspecting the inner SDK exception); (h) `HttpRequestException` (network down — handler throws) → bubbles up as the same exception type for T34 to catch; (i) **TLS chain failure** — handler throws `new HttpRequestException("TLS", new AuthenticationException("Cert chain"))` → exception bubbles up with the inner `AuthenticationException` reachable via `InnerException` chain (T34's MapErrorToMarkdown depends on this); (j) **generic `InvalidOperationException`** thrown from inside the SDK conversion path bubbles up unchanged (so T34's generic-fallback branch is exercised). Must NOT call real OpenAI.
  Parallelization: Wave 2 | Blocked by: T5, T16 | Blocks: F2
  References: T16; D-BASEURL; https://github.com/openai/openai-dotnet/blob/main/README.md
  Acceptance criteria (agent-executable): `dotnet test --filter "FullyQualifiedName~OpenAiChatClientTests"` reports ≥ 10 passed, 0 failed (covers tests a, a-neg, b, c, d, e, f, g, h, i, j — count is 10 because some letters are paired).
  QA: happy = all 10 tests pass; failure = make `OpenAiChatClient` append `/v1` manually, expect (a) to fail AND (a-neg) to fail (regex matches the doubled path). Evidence `.omo/evidence/task-18-little-agents-mvp.txt`.
  Commit: Y | `test(llm): OpenAiChatClient tests with fake HTTP handler (URL/SSE/cancel/error)`

- [x] 19. xUnit: `TemplateRendererTests.cs`
  What to do / Must NOT do: tests = (a) `{input}` only; (b) `{selection}` only; (c) both; (d) neither (template with no placeholders returns verbatim); (e) null `input` substitutes to `""`; (f) null `selection` substitutes to `""`; (g) literal `{{` → `{` and `}}` → `}`; (h) unknown placeholder `{foo}` passes through unchanged; (i) `{selection}` over 8 000 chars truncated with `[truncated to 8000 chars]\n` prefix; (j) case-sensitivity: `{Input}` (capitalized) is NOT substituted, passes through; (k) `{{input}}` (escaped braces around the keyword) renders as `{input}` literal. Must NOT call real `Clipboard` — inject the selection value as a string parameter.
  Parallelization: Wave 2 | Blocked by: T5, T17 | Blocks: F2
  References: T17; D-SEL-CAP.
  Acceptance criteria (agent-executable): `dotnet test --filter "FullyQualifiedName~TemplateRendererTests"` reports ≥ 11 passed, 0 failed.
  QA: happy = all 11 sub-tests pass; failure = make case-insensitive substitution, expect (j) to fail. Evidence `.omo/evidence/task-19-little-agents-mvp.txt`.
  Commit: Y | `test(llm): TemplateRenderer placeholder substitution (11 tests)`

### Wave 3 — Top-level page + render-spike

- [x] 20. `Pages/Agents/AgentsListPage.cs` : `DynamicListPage`
  What to do / Must NOT do: first add `Pages/Run/RunSessionCoordinator.cs` (`internal sealed class RunSessionCoordinator`) so the type exists before `AgentsListPage` and `CommandsProvider` compile. Minimum API for this todo is an empty compile stub (`internal sealed class RunSessionCoordinator { }`) — do **not** reference `ChatRunPage` yet because T32 creates that type. T32/T34 replace the stub with the real `Activate(ChatRunPage page)` / `IsActive(ChatRunPage page)` behavior. Then implement `AgentsListPage`; ctor takes `(AgentStore agents, ProviderStore providers, ISecretStore secrets, ILlmChatClient llm, RunSessionCoordinator sessions)`. `Title = "Little Agents"`. `Icon = new IconInfo("\uE945")`. Override `UpdateSearchText(old, new) => RaiseItemsChanged(-1)`. `GetItems()` filters `_cachedAgents` (populated by T21) by case-insensitive substring match on `Name`/`Tags`/`ProviderId`, returns matching `ListItem(...)` entries (Title=Name, Subtitle=`{providerName} · {model}`, Icon=`agent.Icon ?? "\uE945"`, Tags from `agent.Tags`). Pinned items appended last regardless of search filter: `+ New Agent`, `Manage Providers`, `Settings`. Initial implementation in this todo: pinned items use `NoOpCommand` placeholders + `ShowToast` (T22 holds this state, T30/T40 replace with real behavior). Must NOT block `GetItems` on `agents.Load()` directly — caching is paired with T21.
  Parallelization: Wave 3 | Blocked by: T8, T9 | Blocks: T21, T22, T23, T30, T37
  References: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/microsoft-commandpalette-extensions-toolkit/dynamiclistpage; https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.Indexer/Pages/IndexerPage.cs (UpdateSearchText pattern); D-FORM-REFRESH; D-RUN-SINGLE.
  Acceptance criteria (agent-executable): `dotnet build LittleAgentsExtension/LittleAgentsExtension.csproj -c Debug -p:Platform=x64` succeeds; `Test-Path LittleAgentsExtension/Pages/Run/RunSessionCoordinator.cs` returns True; `(Get-Content LittleAgentsExtension/Pages/Agents/AgentsListPage.cs).Count -le 250`; manual smoke (F5 + Reload) shows the page with the 3 pinned items at the bottom and the Segoe Fluent agent glyph.
  QA: happy = build green and manual smoke shows pinned items; failure = forget to append pinned items in `GetItems`, expect manual smoke to show only saved agents (or empty list). Evidence `.omo/evidence/task-20-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): AgentsListPage as searchable DynamicListPage with pinned items`

- [ ] 46. **Spike: MarkdownContent re-render in 0.5.250829002** (D-RENDER)
  What to do / Must NOT do: add a hidden debug page `Pages/_Spikes/MarkdownTickerPage.cs` that mutates a `MarkdownContent.Body` 5 ×/sec for 5 seconds. Wire it as a 4th pinned item in AgentsListPage **only when** `#if DEBUG` is set. Document observed behavior in `.omo/evidence/spike-markdown-rerender.md`: does the UI live-update? If YES, T34 uses Body-mutation. If NO, T34 uses the fallback (replace whole `MarkdownContent` instance + `RaiseItemsChanged(0)`). Must NOT ship the debug page in Release (gate with `#if DEBUG`).
  Parallelization: Wave 3 | Blocked by: T20 | Blocks: T34
  References: PowerToys issue https://github.com/microsoft/PowerToys/issues/39216; PR https://github.com/microsoft/PowerToys/pull/39263; D-RENDER.
  Acceptance: spike file written with verdict; T34 reads the verdict and chooses path.
  QA: manual — F5 deploy, open ticker, observe. Evidence `.omo/evidence/spike-markdown-rerender.md`.
  Commit: Y | `chore(spike): markdown re-render verification (debug-only ticker page)`

- [x] 21. AgentsListPage subscribes to `agents.Changed` and `providers.Changed`
  What to do / Must NOT do: in ctor, `agents.Changed += (_, _) => { _cachedAgents = agents.Load(); RaiseItemsChanged(-1); }; providers.Changed += (_, _) => RaiseItemsChanged(-1);`. **Cache** the latest `agents.Load()` result in `_cachedAgents` so `GetItems` doesn't hit disk on every keystroke. Per D-CANCEL there is no Dispose hook on `ContentPage` — but `AgentsListPage` is a long-lived top-level page that lives for the whole CmdPal session, so unsubscribing isn't required for MVP. Must NOT call `agents.Load()` from inside `GetItems()` directly.
  Parallelization: Wave 3 | Blocked by: T20 | Blocks: T25 (form-to-list refresh path)
  References: T8 / T9 (Changed event); D-FORM-REFRESH; D-CANCEL.
  Acceptance criteria (agent-executable): manual smoke — open the AgentsListPage, F5 rebuild a 2nd time and add an agent via T25's edit form, return to AgentsListPage and verify the new agent appears within 1 s without a manual reload (this is the D-FORM-REFRESH pathway).
  QA: happy = D-FORM-REFRESH flow demonstrated; failure = comment out the `_cachedAgents = agents.Load()` re-load inside the Changed handler, expect the new agent to NOT appear. Evidence `.omo/evidence/task-21-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): AgentsListPage caches agents.Load and refreshes on Changed`

- [ ] 22. Pinned items use NoOp + ShowToast placeholders (later replaced by T30)
  What to do / Must NOT do: in T20's `GetItems`, the three pinned items (`+ New Agent`, `Manage Providers`, `Settings`) are wired to `new InvokableCommand(() => { /* show toast */ })` returning `CommandResult.ShowToast("Wired in T30")`. This intermediate state lets T20 compile and lets manual smoke verify the page renders, without depending on Wave 4's editor pages. Must NOT ship this state — T30 replaces these with real navigation; this todo is purely a build-order convenience.
  Parallelization: Wave 3 | Blocked by: T20 | Blocks: T30 (replaces this code)
  References: T20.
  Acceptance criteria (agent-executable): manual smoke after T20 + T22: F5 deploy + Reload, click each of the 3 pinned items, see a toast each.
  QA: Evidence `.omo/evidence/task-22-little-agents-mvp.txt`.
  Commit: N (T30 replaces this code in the same branch; commit only if a separate WIP push is needed for review)

- [x] 23. Rewrite `LittleAgentsExtensionCommandsProvider.cs`
  What to do / Must NOT do: in the constructor, build `_agentStore = new AgentStore()`, `_providerStore = new ProviderStore()`, `_secretStore = SecretStoreFactory.Create()` (T45 — chooses Vault or DPAPI per spike), `_llmClient = new OpenAiChatClient()`, `_runSessions = new RunSessionCoordinator()`, all as private readonly fields. Replace `new CommandItem(new LittleAgentsExtensionPage())` with `new CommandItem(new AgentsListPage(_agentStore, _providerStore, _secretStore, _llmClient, _runSessions))`. Expose toolkit `Settings` after T40's SettingsPage is built — for now, reserve the field with a comment `// settings wired by T40 SettingsPage`. Must NOT keep the old `LittleAgentsExtensionPage.cs` — delete it in this todo.
  Parallelization: Wave 3 | Blocked by: T20, T8, T9, T10, T16, T45 | Blocks: T37, T40
  References: `LittleAgentsExtension/LittleAgentsExtensionCommandsProvider.cs:10-27`; `LittleAgentsExtension/Pages/LittleAgentsExtensionPage.cs:10-25`; T45 SecretStoreFactory.
  Acceptance criteria (agent-executable): `dotnet build LittleAgentsExtension/LittleAgentsExtension.csproj -c Debug -p:Platform=x64` succeeds; `Test-Path LittleAgentsExtension/Pages/LittleAgentsExtensionPage.cs` returns False (file removed); manual smoke — F5 + Reload, "Little Agents" visible in CmdPal top-level with the AgentsListPage as primary action.
  QA: happy = all three checks pass; failure = leave the placeholder `LittleAgentsExtensionPage.cs` in place, expect `Test-Path` to return True. Evidence `.omo/evidence/task-23-little-agents-mvp.txt`.
  Commit: Y | `refactor(provider): wire stores + LLM client into AgentsListPage; remove placeholder ListPage`

### Wave 4 — Editors

- [x] 24. `Pages/Agents/AgentEditFormPage.cs` scaffold
  What to do / Must NOT do: `internal sealed partial class AgentEditFormPage : ContentPage`. Ctor: `(AgentStore agents, ProviderStore providers, AgentDef? existing)`. `Title = existing == null ? "New agent" : "Edit agent"`. `Icon = new IconInfo("\uE710")`. Override `IContent[] GetContent() => [_form];` where `_form = new AgentEditForm(agents, providers, existing)`. Must NOT add commands at this stage — T25 wires submit/cancel via Adaptive Card actions.
  Parallelization: Wave 4 | Blocked by: T8, T9 | Blocks: T25
  References: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/microsoft-commandpalette-extensions-toolkit/contentpage; T25.
  Acceptance criteria (agent-executable): Debug build green after T24+T25; `(Get-Content LittleAgentsExtension/Pages/Agents/AgentEditFormPage.cs).Count -le 50`.
  QA: see T25 (this and T25 are tested together). Evidence `.omo/evidence/task-24-little-agents-mvp.txt`.
  Commit: N (paired with T25 — single commit at T25's commit line)

- [x] 25. `AgentEditForm : FormContent` Adaptive Card + SubmitForm
  What to do / Must NOT do: `TemplateJson` = Adaptive Card 1.6 with: `Input.Text id=Name (isRequired=true)`, `Input.Text id=SystemPrompt (isMultiline=true)`, `Input.Text id=UserTemplate (isMultiline=true)` with hint "Use {input} for user input, {selection} for clipboard text", `Input.ChoiceSet id=ProviderId` populated from `providers.Load()` (`{ title: p.Name, value: p.Id }`, isRequired=true), `Input.Text id=Model` with placeholder text `"Leave empty to use provider default"`, `Input.Text id=Icon` (optional), `Input.Text id=Tags` (comma-separated, optional). `Action.Submit` with title "Save". `DataJson` prefilled when `existing != null`. `SubmitForm(payload)` flow: `JsonNode.Parse(payload)`. (1) Name empty → `CommandResult.ShowToast("Name is required") + KeepOpen`. (2) ProviderId empty → `CommandResult.ShowToast("Provider is required") + KeepOpen`. (3) **Model resolution**: if user-typed Model is non-empty, use it as-is. If empty, look up `providers.Load().First(p => p.Id == ProviderId).DefaultModel`; if THAT is also null/empty → `CommandResult.ShowToast("Model required (no default on this provider)") + KeepOpen`. Otherwise upsert `agents.Upsert(new AgentDef(Id: existing?.Id ?? Guid.NewGuid().ToString(), ...))`, return `CommandResult.GoBack()`. Must NOT serialize back into the form on success — the parent list page picks up via `Changed` (D-FORM-REFRESH). Must NOT save with empty Model.
  Parallelization: Wave 4 | Blocked by: T24 | Blocks: T30
  References: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/using-form-pages; https://adaptivecards.io/explorer/Input.Text.html; https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/SamplePagesExtension/Pages/SampleContentPage.cs (FormContent example).
  Acceptance: manual — open form, fill required Name, submit, verify list shows new agent without manual reload (D-FORM-REFRESH path).
  QA: happy = save with explicit Model succeeds; save with blank Model and provider DefaultModel succeeds using the default; failure = blank Model with no provider default returns `ShowToast("Model required (no default on this provider)") + KeepOpen` and does not save. Evidence `.omo/evidence/task-25-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): AgentEditFormPage with Adaptive Card create/edit flow`

- [x] 26. Agent delete via context command + confirm
  What to do / Must NOT do: in `AgentsListPage.GetItems` per agent ListItem add `Commands = [new CommandContextItem(new ConfirmDeleteAgentPage(agents, agent)) { Title = "Delete" }]`. The `ConfirmDeleteAgentPage : ContentPage` returns a `FormContent` with text "Delete agent '<name>'? This cannot be undone." and two `Action.Submit` actions (`{ "title": "Delete", "data": { "confirmed": true } }` and `{ "title": "Cancel", "data": { "confirmed": false } }`). `SubmitForm(payload)` reads `confirmed`; if true → `agents.Delete(agent.Id)` + `CommandResult.GoBack`; if false → `CommandResult.GoBack` only. Must NOT delete without the explicit confirmed=true payload.
  Parallelization: Wave 4 | Blocked by: T20, T8 | Blocks: F3
  References: T20; T25 (FormContent submit pattern); D-FORM-REFRESH (list refreshes via store.Changed event).
  Acceptance criteria (agent-executable): manual smoke — create an agent, click "Delete" context command, click Cancel → agent still present; click Delete → agent disappears from list within 1s of confirmation (D-FORM-REFRESH).
  QA: happy = both paths work; failure = bypass confirmation by directly invoking `agents.Delete` from the context command, expect a regression test asserting the confirmation form was rendered to fail. Evidence `.omo/evidence/task-26-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): delete agent via context command with confirmation form`

- [x] 27. `Pages/Providers/ProviderEditFormPage.cs` scaffold (mirror T24)
  What to do / Must NOT do: identical shape to T24 but for providers. `internal sealed partial class ProviderEditFormPage : ContentPage`. Ctor: `(ProviderStore providers, ISecretStore secrets, ProviderDef? existing)`. `Title = existing == null ? "New provider" : "Edit provider"`. `Icon = new IconInfo("\uE968")`. Returns `IContent[] { _form }` where `_form = new ProviderEditForm(providers, secrets, existing)`. Must NOT add commands — T28 wires submit via Adaptive Card actions.
  Parallelization: Wave 4 | Blocked by: T9, T10 | Blocks: T28
  References: T24 (sister scaffold); T28; D-FORM-REFRESH.
  Acceptance criteria (agent-executable): Debug build green after T27+T28; `(Get-Content LittleAgentsExtension/Pages/Providers/ProviderEditFormPage.cs).Count -le 50`.
  QA: see T28. Evidence `.omo/evidence/task-27-little-agents-mvp.txt`.
  Commit: N (paired with T28 — single commit at T28's commit line)

- [ ] 28. `ProviderEditForm` Adaptive Card with apiKey style=password + secret split
  What to do / Must NOT do: Adaptive Card 1.6 fields: `Input.Text id=Name (isRequired=true)`, `Input.Text id=BaseUrl (isRequired=true, style=url, placeholder="https://api.openai.com/v1")` with hint text **"Include the `/v1` (or your provider's API root path). Examples: https://api.openai.com/v1 · https://openrouter.ai/api/v1 · http://localhost:11434/v1"** (per D-BASEURL); `Input.Text id=ApiKey (style=password)`, `Input.Text id=DefaultModel` (optional but recommended). **Edit-mode:** ApiKey field empty by default; submitting empty ApiKey on edit KEEPs the existing key (don't blank the vault); submitting empty ApiKey on create → `CommandResult.ShowToast("API key is required") + KeepOpen`. **TLS posture:** do NOT add any cert-bypass code. If the user enters an `https://` URL whose cert is self-signed or expired, the request will fail at run time with a TLS error mapped by D-ERR. For local providers, recommend `http://localhost` (also surface this in T39 PUBLISHING.md). Submit flow: parse payload, validate Name + BaseUrl + (ApiKey on create), split — `ProviderDef(Id, Name, BaseUrl, DefaultModel)` → `providers.Upsert(...)`; `apiKey` → `secrets.Set(providerId, apiKey)` only if non-empty. **Backend selection (D-VAULT)**: `secrets` is the spike-validated singleton — either `WindowsPasswordVaultSecretStore` or `DpapiSecretStore`; the form does not care which. Must NOT serialize the apiKey into providers.json. Add a post-submit debug-only assertion: `Debug.Assert(!File.ReadAllText(providers.json).Contains(apiKey))`.
  Parallelization: Wave 4 | Blocked by: T27, T10, T45 | Blocks: T29, T30, T31
  References: https://adaptivecards.io/explorer/Input.Text.html (style=password, style=url); D-FORM-REFRESH; D-VAULT; D-BASEURL.
  Acceptance criteria (agent-executable): manual smoke — create with canary key `sk-test-METIS-CANARY-12345`, then `Get-ChildItem "$env:LOCALAPPDATA\Packages\*\LocalState\LittleAgents" -Recurse -File | Select-String -Pattern "sk-test-METIS-CANARY"` returns 0 matches across all files (JSON, DPAPI `.bin`, logs, temp files).
  QA: happy = create + edit + edit-without-changing-key flows all succeed; failure = inject the canary into BaseUrl text by mistake → grep finds it (negative control, expected). Evidence `.omo/evidence/task-28-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): ProviderEditFormPage with secret split (D-VAULT, D-BASEURL)`

- [ ] 29. Provider delete + orphan-block (D-ORPHAN)
  What to do / Must NOT do: in `ProvidersListPage`'s per-provider context "Delete" command, wrap a `ConfirmDeleteProviderPage : ContentPage` whose `FormContent.SubmitForm` runs: `var orphans = agents.Load().Where(a => a.ProviderId == providerId).Select(a => a.Name).ToArray();`. If `orphans.Length > 0`: `return CommandResult.ShowToast($"Cannot delete '{name}': {orphans.Length} agent(s) reference it — {string.Join(", ", orphans.Take(3))}{(orphans.Length > 3 ? ", ..." : "")} — reassign or delete them first") + KeepOpen`. Otherwise: `providers.Delete(providerId); secrets.Delete(providerId); return CommandResult.GoBack()`. Must NOT silently orphan agents. Must NOT skip the `secrets.Delete(providerId)` cleanup — leftover credential entries are a leak.
  Parallelization: Wave 4 | Blocked by: T9, T10, T28, T31 | Blocks: F3
  References: T31 (ProvidersListPage); T28 (paired secret store); D-ORPHAN.
  Acceptance criteria (agent-executable): unit test in `LittleAgentsExtension.Tests/ProviderDeleteOrphanTests.cs` using `InMemorySecretStore` + temp-file `AgentStore`/`ProviderStore`: (a) delete provider with 0 referencing agents → provider removed, secret removed, returns `GoBack`; (b) delete provider with 2 referencing agents → provider NOT removed, secret NOT removed, returns toast that names both agents; (c) delete provider with 5 referencing agents → toast lists first 3 with `, ...` suffix.
  QA: happy = 3 sub-tests pass; failure = remove the `secrets.Delete(providerId)` line, expect a follow-up assertion "secret store has 0 entries after delete" to fail. Evidence `.omo/evidence/task-29-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): provider delete blocks on orphan agents and cleans up secret (D-ORPHAN)`

- [ ] 30. Replace AgentsListPage pinned NoOps with real navigation for agents/providers (D-FIRSTRUN)
  What to do / Must NOT do: "+ New Agent" navigates to `new AgentEditFormPage(agents, providers, existing: null)` UNLESS `providers.Load().Length == 0` — in that case show `ShowToast("Add a provider first")` and navigate to `new ProviderEditFormPage(providers, secrets, existing: null)` instead (D-FIRSTRUN). "Manage Providers" → `new ProvidersListPage(...)`. "Settings" remains a temporary `ShowToast("Settings are available from the Command Palette gear after T40")` placeholder until T40 wires the toolkit Settings gear and, if a list-level Settings item remains, replaces it there. Confirm the exact CmdPal navigation API by inspecting https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/SamplePagesExtension/Pages/SamplesListPage.cs — pattern is to set the ListItem's primary `Command` to either an `IPage` directly (toolkit auto-pushes) or wrap with an `InvokableCommand` returning `CommandResult.GoToPage(...)` if that API exists in 0.5.x. Pick the pattern that the sample uses and document it in a code comment. Must NOT pre-construct subpages on every `GetItems` call — lazy-construct each time the user actually invokes the pinned item (memory cost + correctness with refresh).
  Parallelization: Wave 4 | Blocked by: T22, T25, T28, T31 | Blocks: T37
  References: D-FIRSTRUN; CommandResult navigation enum; https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/SamplePagesExtension/Pages/SamplesListPage.cs (canonical navigation pattern).
  Acceptance criteria (agent-executable): manual smoke — (a) with 0 providers, "+ New Agent" shows the "Add a provider first" toast AND opens `ProviderEditFormPage`; (b) with ≥1 provider, "+ New Agent" opens `AgentEditFormPage` directly; (c) "Manage Providers" opens `ProvidersListPage`; (d) "Settings" shows the temporary T40-directed toast rather than a NoOp placeholder.
  QA: happy = all 4 paths work; failure = remove the `providers.Length == 0` check, expect (a) to silently allow agent creation with no provider. Evidence `.omo/evidence/task-30-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): wire pinned items to real pages with D-FIRSTRUN guard`

- [x] 31. `Pages/Providers/ProvidersListPage.cs` : `DynamicListPage`
  What to do / Must NOT do: ctor takes `(ProviderStore providers, ISecretStore secrets, AgentStore agents)` (the agents reference is needed for D-ORPHAN delete-blocking in T29). Override `UpdateSearchText` and `GetItems` symmetrically with T20: filter providers by case-insensitive substring on Name / BaseUrl. Per-provider primary command navigates to `new ProviderEditFormPage(providers, secrets, existing: p)`. Per-provider context command "Delete" wraps a `ConfirmDeleteProviderPage` that runs T29's orphan-block logic before `providers.Delete + secrets.Delete`. Pinned items: `+ New Provider` (navigates to `new ProviderEditFormPage(providers, secrets, existing: null)`). Subscribe to `providers.Changed`. Must NOT block the `GetItems` hot path on `providers.Load()` — cache and refresh via Changed event.
  Parallelization: Wave 4 | Blocked by: T9, T10, T27 | Blocks: T30
  References: T20 (sister DynamicListPage pattern); T27 (edit form); T29 (orphan-block); D-FORM-REFRESH; D-ORPHAN.
  Acceptance criteria (agent-executable): Debug build green; manual smoke — page lists existing providers, search filters live, primary click navigates to edit form, "+ New Provider" opens an empty form, "Delete" with referencing agents shows the D-ORPHAN toast.
  QA: happy = manual smoke covers all four paths; failure = remove the `providers.Changed` subscription, expect new provider to not appear without manual reload. Evidence `.omo/evidence/task-31-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): ProvidersListPage with create/edit/delete and orphan-block delete`

### Wave 5 — Chat run page

- [x] 32. `Pages/Run/ChatRunPage.cs` scaffold
  What to do / Must NOT do: upgrade the T20-created `Pages/Run/RunSessionCoordinator.cs` from compile stub to full implementation, add `internal interface IClipboardWriter` in `Pages/Run/IClipboardWriter.cs`, then add `internal sealed class ChatRunPage : ContentPage`. `RunSessionCoordinator` has `void Activate(ChatRunPage page)` that cancels the previously active page via `CancelActiveStreamForSupersededRun()` before recording the new one; it may also have `bool IsActive(ChatRunPage page)` for tests. Fields:
  - `_agent` (AgentDef), `_provider` (ProviderDef), `_apiKey` (string)
  - `_llm` (ILlmChatClient), `_settings` (Func<RuntimeSettings>) — late binding to T40's settings
  - `_session` (`RunSessionCoordinator` — singleton owned by CommandsProvider; enforces D-RUN-SINGLE)
  - `_clipboard` (`IClipboardWriter` — T35 adds the production writer + commands)
  - `_output` (MarkdownContent — initial Body=`""`)
  - `_inputForm` (RunInputForm — created lazily by T33)
  - `_showingInput` (`bool`), `_initialized` (`bool`), `_initTask` (`Task?`)
  - `_history` (`List<ChatMessage>` — empty at construction)
  - `_lastAssistantText` (string — `""` initially; T35's "Copy result" reads it)
  - `_initialUserMsg` (string — set by T33 after first render; T35's "Re-run" reuses it)
  - `_cts` (`CancellationTokenSource?` — nullable; recreated on each `StartStream`)
  - `_streamTask` (`Task?` — last spawned stream task)

  `Title = _agent.Name`. `Icon = _agent.Icon != null ? new IconInfo(_agent.Icon) : new IconInfo("\uE945")`. Ctor takes `(AgentDef agent, ProviderDef provider, string apiKey, ILlmChatClient llm, RunSessionCoordinator session, IClipboardWriter clipboard, Func<RuntimeSettings>? settings = null)`. Add `ActivatePageOnce()` helper: if `_pageActivated` is false, set it true and call `_session.Activate(this)` before any clipboard/input/stream setup. Must NOT persist `_history` to disk under any circumstance. Must NOT reuse a `_cts` after it has been cancelled.
  Parallelization: Wave 5 | Blocked by: T8, T9, T10, T16 | Blocks: T33-T36, T37
  References: T20 (RunSessionCoordinator compile stub); T34 (history invariant + StartStream flow); T35 (commands that read fields); D-CANCEL; D-RUN-SINGLE.
  Acceptance criteria (agent-executable): Debug build green after T32+T33+T34; `(Get-Content LittleAgentsExtension/Pages/Run/ChatRunPage.cs).Count -le 250`.
  QA: see T34 (covers full streaming behavior including this scaffold's contracts). Evidence `.omo/evidence/task-32-little-agents-mvp.txt`.
  Commit: N (paired with T33+T34 — single commit at T34's commit line)

- [x] 33. Initial-run flow: detect `{input}`, optionally show input form
  What to do / Must NOT do: gate the initial flow on the FIRST synchronous `GetContent()` call (use a `bool _initialized` flag set after first dispatch; CmdPal does NOT provide an `OnFirstActivated` hook per Metis). **Important:** `ContentPage.GetContent()` is synchronous; do not `await` inside it and do not block on `.Result`/`.GetAwaiter().GetResult()`. Instead, `GetContent()` starts one async initialization task and immediately returns a placeholder content array.
  ```
  public override IContent[] GetContent() {
    ActivatePageOnce();                    // D-RUN-SINGLE: cancels prior active page even before {input} submit
    if (!_initialized) {
      _initialized = true;
      _output.Body = "_Preparing..._";
      _initTask = Task.Run(InitializeFirstRunAsync);
      return new IContent[] { _output };
    }
    return _showingInput ? new IContent[] { _inputForm! } : new IContent[] { _output };
  }

  private async Task InitializeFirstRunAsync() {
    var selection = await ClipboardReader.TryGetTextAsync();  // null on binary clipboard
    if (_agent.UserTemplate.Contains("{input}")) {
      _inputForm = new RunInputForm(label: "Your input", onSubmit: text => {
        _initialUserMsg = TemplateRenderer.Render(_agent.UserTemplate, input: text, selection: selection);
        _showingInput = false;
        SwapToOutput();
        StartStream(_initialUserMsg);
      });
      _showingInput = true;
      RaiseItemsChanged(0);
      return;
    }
    _showingInput = false;
    _initialUserMsg = TemplateRenderer.Render(_agent.UserTemplate, input: null, selection: selection);
    RaiseItemsChanged(0);
    StartStream(_initialUserMsg);
  }
  ```
  Must NOT call `ClipboardReader.TryGetTextAsync()` more than once per page instance (it's eagerly read at first activation; replies do NOT re-read clipboard per T36). Must NOT wait until `StartStream()` to activate the page; input-gated agents must cancel the prior run as soon as their page first renders.
  Parallelization: Wave 5 | Blocked by: T32, T17 | Blocks: T34, T36
  References: T17 (TemplateRenderer); T44 (ClipboardReader); T36 ({selection} first-turn-only invariant).
  Acceptance criteria (agent-executable): integration test asserts (a) first `GetContent()` returns immediately with `_output` placeholder and no blocking wait; (b) first `GetContent()` calls `_session.Activate(this)` exactly once before `_initTask` starts; (c) after `_initTask` completes, agent with `{input}` → `_inputForm` shown and no stream started yet; (d) after `_initTask` completes, agent without `{input}` → stream started and output visible; (e) `ClipboardReader.TryGetTextAsync` invoked exactly once per page instance; (f) with page A streaming and page B using a `{input}` template, page B's first `GetContent()` cancels A within 1 s even though B has not submitted input.
  QA: happy = all 6 sub-tests pass; failure = use `await` directly in `GetContent()` or block on `.Result`, expect a compile failure or a timeout test failure; failure-2 = move activation into `StartStream()` only, expect (f) to fail. Evidence `.omo/evidence/task-33-little-agents-mvp.txt`.
  Commit: N (paired with T34 — single commit at T34's commit line)

- [x] 34. Streaming loop with cancellation, history invariant, and D-ERR mapping
  What to do / Must NOT do: implement `BuildRequest()` and `StartStream(string renderedUserMsg)`.

  **History invariant** (single source of truth): `_history` contains ONLY user + assistant turns from this `ChatRunPage` instance. `BuildRequest()` constructs `new ChatRequest(_agent.Model, [System(_settings.SystemPrefix + _agent.SystemPrompt), .._history], _settings.Temperature)` — that is, the system prompt is prepended on every send, never stored in `_history`. T35's Re-run discards `_history` and calls `StartStream(initialRendered)`. T36's Reply appends a User message and calls `StartStream(replyText)`. `StartStream` itself appends the User turn before iteration, then a single Assistant turn AFTER successful completion (NOT on cancellation, NOT on error — those branches do not pollute `_history`).

  **Streaming logic** (pseudocode, real impl ≤ 80 LoC) — note the **CTS lifecycle invariant**: a CTS is owned by exactly one stream task; only that task disposes it in its `finally`. `StartStream` cancels the previous CTS but does NOT dispose it (the previous task's own `finally` does that after it unwinds):
  ```csharp
  private string _lastAssistantText = "";
  private void StartStream(string renderedUserMsg) {
    ActivatePageOnce();                    // defensive no-op if first GetContent already activated this page
    _cts?.Cancel();                       // signal the previous task; do NOT dispose here
    var thisCts = new CancellationTokenSource();
    _cts = thisCts;                       // _cts now points at the new one
    var ct = thisCts.Token;
    _history.Add(new(ChatRole.User, renderedUserMsg));
    IsLoading = true;
    _streamTask = Task.Run(async () => {
      var sb = new StringBuilder(_output.Body);
      sb.AppendLine().Append("**You:** ").AppendLine(renderedUserMsg).AppendLine().Append("**Assistant:** ");
      UpdateOutput(sb.ToString());
      var assistant = new StringBuilder();
      try {
        await foreach (var chunk in _llm.StreamAsync(BuildRequest(), _provider, _apiKey, ct).WithCancellation(ct)) {
          assistant.Append(chunk);
          UpdateOutput(sb.ToString() + assistant.ToString());
        }
        _lastAssistantText = assistant.ToString();
        _history.Add(new(ChatRole.Assistant, _lastAssistantText));
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested) {
        UpdateOutput(sb.ToString() + assistant.ToString() + "\n_(stopped)_");
        // Note: _history is NOT updated; assistant turn was incomplete.
      }
      catch (Exception ex) {
        var errorBlock = MapErrorToMarkdown(ex);  // see D-ERR mapping below
        UpdateOutput(sb.ToString() + assistant.ToString() + "\n\n" + errorBlock);
        ShowToast(MapErrorToToast(ex));
      }
      finally {
        // Each task disposes its OWN CTS unconditionally (it owns thisCts).
        thisCts.Dispose();
        // Only clear _cts and IsLoading if a NEWER task hasn't already replaced us.
        if (ReferenceEquals(_cts, thisCts)) { _cts = null; IsLoading = false; }
      }
    }, ct);
  }
  ```

  **`MapErrorToMarkdown(Exception ex)`** implements D-ERR. **Mapping order matters**: TLS errors arrive in .NET as `HttpRequestException` wrapping `AuthenticationException` (inner) wrapping a chain failure. We MUST walk the inner-exception chain looking for TLS first, BEFORE matching the outer `HttpRequestException` as a generic network error. Order:
  1. **TLS chain check**: walk `ex` via `for (var e = ex; e != null; e = e.InnerException)` — if any `e is AuthenticationException || e is CryptographicException` OR `e.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase)`, return `> **Provider TLS certificate rejected.** Use a trusted certificate or http://localhost for local servers.`
  2. **SDK status-bearing exception** (`Microsoft.Extensions.AI.ChatClientException` with HTTP status; OpenAI SDK's `ClientResultException` with `Status` property reachable; `Azure.RequestFailedException`): return `> **Error <status>:** <body or scrubbed message ≤ 400 chars>`.
  3. **`HttpRequestException`** (no TLS inner found, no SDK status — DNS / network down): return `> **Network error:** <Message ≤ 400 chars>`.
  4. **Anything else**: return `> **Error:** <Message ≤ 400 chars>`.
  All branches first run the apiKey-scrubbing regex from T16 on the message before truncating to 400 chars. The toast variant uses the same priority order with shorter text (e.g. "TLS rejected", "Error 429", "Network down", "Error").

  **`UpdateOutput(string)`** is the D-RENDER indirection: if `T46` spike says Body-mutation works, body is `_output.Body = s;`. If not, body replaces the whole `MarkdownContent` instance in the `IContent[]` and calls `RaiseItemsChanged(0)`.

  `CancelActiveStreamForSupersededRun()` is a small helper called only by `RunSessionCoordinator.Activate(otherPage)`: it calls `_cts?.Cancel()` and returns immediately; it does not dispose `_cts` and does not mutate `_history`.

  Must NOT: log apiKey in any catch path; update `_history` on cancellation/error; reuse a disposed `_cts`; start a stream without first calling `ActivatePageOnce()` (which is a same-page no-op after first `GetContent()`).
  Parallelization: Wave 5 | Blocked by: T33, T16, T46 | Blocks: T35, T36
  References: https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.Indexer/Pages/IndexerPage.cs (CTS pattern); D-RENDER; D-CANCEL; D-ERR; D-BASEURL.
  Acceptance criteria (agent-executable): integration test in `LittleAgentsExtension.Tests/ChatRunPageStreamingTests.cs` using a fake `ILlmChatClient` that emits 3 chunks: (1) verify `_output.Body` after completion contains all 3 chunks AND `_lastAssistantText` equals their concatenation AND `_history` ends with `(User, _, Assistant, all-three)`; (2) cancel via `_cts.Cancel()` after chunk 2 — body ends with `_(stopped)_`, `_history` length = before-call + 1 (user only, no assistant); (3) fake client throws `HttpRequestException("DNS down")` → body contains `> **Network error:** DNS down`; (4) fake client throws synthetic SDK exception with status 429 → body contains `> **Error 429:**`; (5) fake client emits a chunk containing `sk-test-LOG-CANARY-99999` then throws — `_output.Body` may contain it (the model output IS the user's responsibility), but `Toast` and any error block must NOT; (6) **TLS classification**: fake client throws `new HttpRequestException("network", new AuthenticationException("cert chain"))` → body contains `> **Provider TLS certificate rejected.**`, **NOT** `> **Network error:**` (verifies mapping-order fix); (7) **CTS lifecycle**: kick off a stream, immediately call `StartStream` again — old task's `thisCts.Dispose()` fires in its own `finally`; new task runs to completion; `_cts` is set to null only by the second (still-active-at-finally) task; no `ObjectDisposedException` thrown anywhere; (8) **D-RUN-SINGLE streaming path**: two ChatRunPages share one `RunSessionCoordinator`; when page B starts streaming, page A's fake client observes cancellation within 1 s and page B completes; (9) **D-RUN-SINGLE input-gated path**: with page A streaming, page B has `{input}` and calls `GetContent()` but does not submit input — page A still observes cancellation within 1 s.
  QA: happy = all 9 sub-tests pass; failure-1 = remove the apiKey-scrubbing branch in T16's regex, expect (5) to fail; failure-2 = swap mapping order (HttpRequestException before TLS), expect (6) to fail; failure-3 = remove `ActivatePageOnce()` from first `GetContent()`, expect (9) to fail. Evidence `.omo/evidence/task-34-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): ChatRunPage streaming loop — cancellation lifecycle, history invariant, D-ERR mapping order`

- [x] 35. Run-page commands: Copy result / Copy transcript / Stop / Re-run / Reply
  What to do / Must NOT do: implement the production `WindowsClipboardWriter : IClipboardWriter` (calls `Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(...)`) for the seam introduced in T32. `ChatRunPage` already takes an `IClipboardWriter` via ctor. Tests use a `FakeClipboardWriter` that records the last set text. This avoids both ambiguity and the toolkit's `CopyTextCommand` (which would force a string at construction time, defeating the read-at-Invoke requirement).

  Define five commands in `ChatRunPage.Commands`:
  - **CopyResultCommand** (`Title = "Copy result"`): on Invoke reads `page._lastAssistantText` and calls `_clipboard.SetText(...)`. If `_lastAssistantText == ""`, `ShowToast("Nothing to copy yet")` and skip the SetText call.
  - **CopyTranscriptCommand** (`Title = "Copy transcript"`): on Invoke calls `_clipboard.SetText(page._output.Body)`.
  - **StopCommand** (`Title = "Stop"`): on Invoke calls `page._cts?.Cancel()`. If `page._cts == null`, `ShowToast("Nothing to stop")`.
  - **RerunCommand** (`Title = "Re-run"`): on Invoke sets `_history = new()`, `_output.Body = ""`, `_lastAssistantText = ""`, then calls `page.StartStream(page._initialUserMsg)`.
  - **ReplyCommand** (`Title = "Reply"`): wired by T36.

  All commands read fields at Invoke time (do NOT capture transient values at construction). Must NOT close over `_output.Body` or `_lastAssistantText` at command-construction time. Must NOT call `Clipboard.SetContent` outside `IClipboardWriter` — that's what the seam exists for.
  Parallelization: Wave 5 | Blocked by: T34 | Blocks: T36
  References: T34 (history invariant); D-CANCEL; https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.clipboard.setcontent
  Acceptance criteria (agent-executable): xUnit integration tests verify (a) `CopyResultCommand.Invoke` after completion calls `FakeClipboardWriter.SetText` exactly once with `_lastAssistantText`; (b) `CopyTranscriptCommand.Invoke` calls `FakeClipboardWriter.SetText` once with the full `_output.Body` (which contains both "**You:**" and "**Assistant:**" headers); (c) `StopCommand.Invoke` with `_cts == null` does not throw and shows the "Nothing to stop" toast; (d) `RerunCommand.Invoke` resets `_history.Count` to 0 and `_lastAssistantText` to `""` BEFORE the second stream begins; (e) modifying `_lastAssistantText` between command construction and Invoke causes Invoke to copy the LATER value (not the construction-time value).
  QA: happy = all 5 sub-tests pass; failure = inline `Clipboard.SetText(_lastAssistantText)` directly in command ctor (capturing at construction), expect (e) to fail. Evidence `.omo/evidence/task-35-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): Copy result / Copy transcript / Stop / Re-run via IClipboardWriter seam`

- [ ] 36. Reply: append a new turn (multi-turn) — preserves history invariant
  What to do / Must NOT do: `ReplyCommand` swaps page content from `_output` to a fresh `RunInputForm` (empty by default — does NOT prefill clipboard, since `{selection}` is a first-turn-only substitution). On submit: read `replyText` from the form, then call `StartStream(replyText)`. Per T34's history invariant, `StartStream` appends a User turn before iteration and a single Assistant turn on success — no extra plumbing needed. The visible separator between turns in `_output.Body` is one blank line (already produced by the `sb.AppendLine()` calls in T34). After streaming completes, swap `_inputForm` back out and re-display `_output`.

  **{selection} semantics**: `{selection}` is evaluated ONCE, at first-turn rendering, before history exists. Replies do NOT re-evaluate `{selection}` — replies are taken verbatim from the form. Document this in T17's TemplateRenderer XML comments and in T38 README.

  Must NOT: re-run the system prompt rendering (system is computed once and stored in `_agent.SystemPrompt`); re-evaluate `{selection}` on a reply.
  Parallelization: Wave 5 | Blocked by: T34, T35 | Blocks: F3
  References: T34 (history invariant); D-CANCEL; T17 (TemplateRenderer).
  Acceptance criteria (agent-executable): integration test using fake LLM client emitting 2 distinct chunks per call: (1) initial run → `_history` = `[User1, Assistant1]`; (2) Reply with text "follow-up" → `_history` = `[User1, Assistant1, User2(="follow-up"), Assistant2]`; (3) `_output.Body` contains both turns separated visibly; (4) cancellation during a Reply leaves history at `[User1, Assistant1, User2]` with no incomplete Assistant turn.
  QA: happy = all 4 sub-tests pass; failure = re-evaluate `{selection}` on reply by calling `TemplateRenderer.Render(replyText, ...)` instead of using verbatim — expect a test asserting raw substring "{selection}" in user2 to fail when clipboard differs. Evidence `.omo/evidence/task-36-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): multi-turn Reply preserves history invariant; {selection} is first-turn only`

- [ ] 37. Wire AgentsListPage agent invocation → ChatRunPage
  What to do / Must NOT do: in `AgentsListPage.GetItems` per agent, the primary `Command` of the ListItem is an `InvokableCommand` whose Invoke only validates prerequisites and creates the page. The created `ChatRunPage` activates the shared `RunSessionCoordinator` on its first `GetContent()` (T33), not in this invoke lambda, so navigation to an input-gated page still cancels the previous active run as soon as the page renders. Invoke does:
  ```csharp
  var p = providers.Load().FirstOrDefault(x => x.Id == agent.ProviderId);
  if (p == null) return CommandResult.ShowToast($"Provider '{agent.ProviderId}' missing — edit the agent to reassign");
  var key = secrets.TryGet(agent.ProviderId);
  if (string.IsNullOrEmpty(key)) return CommandResult.ShowToast("API key missing — edit the provider to re-enter it");
  return CommandResult.GoToPage(new ChatRunPage(agent, p, key, llm, sessions, new WindowsClipboardWriter()));
  ```
  Must NOT silently fail (no log-and-continue). Must NOT cache `key` across invocations — read fresh from `ISecretStore` on each invoke (so a re-entered key is picked up immediately). Must NOT create a fresh `RunSessionCoordinator` per invoke — use the singleton passed into `AgentsListPage` by T23.
  Parallelization: Wave 5 | Blocked by: T20, T32, T35 | Blocks: F3
  References: T20; T23 (`RunSessionCoordinator` singleton ownership); T32 (ChatRunPage ctor); T35 (IClipboardWriter); D-ORPHAN; D-RUN-SINGLE.
  Acceptance criteria (agent-executable): unit test using `InMemorySecretStore` + temp-file stores: (a) agent with valid provider+key → invocation returns a `GoToPage` result whose target is a `ChatRunPage`; (b) agent whose ProviderId is missing → invocation returns a `ShowToast` whose Message contains "Provider"; (c) agent whose secret store has no key for ProviderId → invocation returns `ShowToast` whose Message contains "API key"; (d) invoke agent A, start stream, then invoke agent B with the same `RunSessionCoordinator` and a `{input}` template; call B page's first `GetContent()` but do not submit input → A's fake client observes cancellation and B becomes active.
  QA: happy = all 4 sub-tests pass; failure = allocate `new RunSessionCoordinator()` inside the invoke lambda, expect (d) to fail because A keeps streaming. Evidence `.omo/evidence/task-37-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): AgentsListPage → ChatRunPage navigation with missing-provider/key handling`

### Wave 6 — Polish + docs

- [ ] 38. `README.md` at solution root
  What to do / Must NOT do: cover the following sections in this order:
  1. **English description** matching the user's request — "save prompt templates as agents and invoke any OpenAI-compatible LLM from Command Palette".
  2. **中文摘要** (Chinese summary) — same content, ≤ 8 lines, since the user wrote in Chinese.
  3. **Prerequisites**: Windows 11 build 19041+; PowerToys with Command Palette v0.100+ installed; Visual Studio 2022 17.13+ for development.
  4. **Build / deploy / reload**: open the .sln, F5 to deploy MSIX, run "Reload Command Palette Extension" in CmdPal.
  5. **Supported providers**: any service exposing OpenAI-compatible `/v1/chat/completions` (DeepSeek, OpenRouter, Together, Groq, Ollama via `http://localhost:11434/v1`, llama.cpp's server, etc.). Include 3 worked configuration examples.
  6. **Template variables**: `{input}` (user types each invoke), `{selection}` (current Windows clipboard text up to 8 000 chars). Worked examples: "Translate to English: {selection}", "Summarize this: {input}".
  7. **Limitations**: no images/audio/tools/RAG/cost-tracking/auto-discovery; AOT/trim is **best-effort** with documented warnings for the OpenAI assemblies (D-AOT); no TLS bypass — use `http://localhost` for local providers without a trusted cert.
  8. **License** section that links to the repository's existing license if present, or states "No license file present yet" without inventing one.
  9. **Screenshots** placeholder (1-2 PNG slots; do NOT commit binaries in this todo, just the markdown stubs).
  Must NOT include marketing copy, "AI-powered" buzzwords, claims about supported models we haven't verified.
  Parallelization: Wave 6 | Blocked by: T37 | Blocks: F1
  References: D-AOT; D-BASEURL; D-SEL-CAP; D-VAULT; D-ERR; the user's Chinese request stored in the durable draft.
  Acceptance criteria (agent-executable): `Test-Path README.md` returns True; the file contains both an `## English` (or `## Description`) and a `## 中文` section header (verify with `Select-String -Path README.md -Pattern "^##\s+(中文|Chinese|English|Description)" -AllMatches | Measure-Object | Select -ExpandProperty Count` ≥ 2); the file mentions all of `{input}`, `{selection}`, `OpenAI`, `OpenRouter` or `DeepSeek` or `Ollama`.
  QA: happy = all three asserts hold; failure = ship a single-language README, expect the bilingual-section assert to fail. Evidence `.omo/evidence/task-38-little-agents-mvp.txt`.
  Commit: Y | `docs: README with build/deploy/usage/limitations (bilingual)`

- [x] 39. `docs/PUBLISHING.md`
  What to do / Must NOT do: cover three sections:
  1. **Self-signed dev cert path**: `New-SelfSignedCertificate -Type Custom -Subject "CN=<your-name>" -KeyUsage DigitalSignature -FriendlyName "LittleAgents Dev" -CertStoreLocation "Cert:\CurrentUser\My" -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3","2.5.29.19={text}")`. Then update `Package.appxmanifest` `<Identity Publisher="..."/>` to match the cert's CN exactly. Use `signtool sign /fd SHA256 /sha1 <thumbprint> <msix>` to sign.
  2. **Microsoft Store path** per https://learn.microsoft.com/en-us/windows/powertoys/command-palette/publish-extension-store — Partner Center → reserved name → upload signed MSIX → fill required metadata.
  3. **Reference link**: https://github.com/microsoft/PowerToys/blob/main/src/PackageIdentity/BuildSparsePackage.ps1 (advanced sparse-cert handling pattern, useful for CI signing).
  Must NOT include any real cert thumbprint, real publisher subject from the maintainer's identity, or secret. Use `<your-name>`, `<thumbprint>`, etc. as placeholders only.
  Parallelization: Wave 6 | Blocked by: — | Blocks: F1
  References: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/publish-extension-store; https://learn.microsoft.com/en-us/powershell/module/pki/new-selfsignedcertificate; https://github.com/microsoft/PowerToys/blob/main/src/PackageIdentity/BuildSparsePackage.ps1
  Acceptance criteria (agent-executable): `Test-Path docs/PUBLISHING.md` returns True; `Select-String -Path docs/PUBLISHING.md -Pattern "New-SelfSignedCertificate|signtool|Partner Center"` returns ≥ 3 matches; `Select-String -Path docs/PUBLISHING.md -Pattern "[A-F0-9]{40}"` returns 0 matches (no leaked thumbprint).
  QA: happy = all 3 asserts hold; failure = paste a real 40-char hex thumbprint, expect the 3rd assert to fail. Evidence `.omo/evidence/task-39-little-agents-mvp.txt`.
  Commit: Y | `docs(publishing): self-signed cert, manifest publisher swap, Store path`

- [ ] 40. `Pages/SettingsPage.cs` with toolkit Settings + JsonSettingsManager
  What to do / Must NOT do: create `internal sealed class SettingsManager : Microsoft.CommandPalette.Extensions.Toolkit.JsonSettingsManager`. Register two settings via the toolkit:
  - `NumberSetting("temperature", label: "Default temperature", description: "0.0 - 2.0", defaultValue: 1.0, min: 0.0, max: 2.0)` — `ChatRunPage` reads this once per `BuildRequest()` call as the default for `ChatRequest.Temperature`.
  - `TextSetting("systemPrefix", label: "System-prompt prefix", description: "Optional text prepended to every agent's system prompt", defaultValue: "")` — `ChatRunPage.BuildRequest()` does `new ChatMessage(System, settings.SystemPrefix + agent.SystemPrompt)`.

  Path = `Path.Combine(PathHelper.LocalStateDir, "settings.json")` (under `LocalState\LittleAgents`). `LittleAgentsExtensionCommandsProvider.Settings = _settingsManager.Settings;` so the gear icon appears in CmdPal. Must NOT store any apiKey field in this settings file. Must NOT auto-save on every keystroke — the toolkit handles save-on-change. Must NOT write outside the `LocalState\LittleAgents` subtree.
  Also replace T30's temporary list-level "Settings" toast item if it still exists: either remove it in favor of the official CmdPal gear (preferred) or make it invoke the same settings surface if the Toolkit exposes a page navigation API. Must NOT leave the T30 "Settings are available... after T40" placeholder after this todo.
  Parallelization: Wave 6 | Blocked by: T23, T30 | Blocks: F1
  References: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/adding-extension-settings; https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ExtensionTemplate/TemplateCmdPalExtension/SettingsManager.cs (canonical pattern); T23 (CommandsProvider wiring); T30 (temporary settings placeholder).
  Acceptance criteria (agent-executable): manual smoke — open CmdPal, navigate to "Little Agents", click the gear icon, verify temperature slider and system-prefix text input appear; change temperature to 0.5 and re-run an agent → request body shows `"temperature": 0.5`; `Test-Path "$env:LOCALAPPDATA\Packages\*\LocalState\LittleAgents\settings.json"` returns True after first save; `Select-String -Path LittleAgentsExtension\Pages\Agents\AgentsListPage.cs -Pattern "Settings are available.*after T40|Wired in T30"` returns 0 matches.
  QA: happy = settings UI works AND value flows into request body AND file persists AND temporary placeholder text is gone; failure = remove `Settings = _settingsManager.Settings` wiring, expect the gear icon to not appear. Evidence `.omo/evidence/task-40-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): SettingsPage with default temperature and system-prompt prefix`

- [ ] 41. Icons + tags + subtitles polish
  What to do / Must NOT do: introduce a static `LittleAgentsExtension.Icons` class holding all glyph constants:
  - `AgentDefault = new IconInfo("\uE945")` (LightningBolt)
  - `New = new IconInfo("\uE710")` (Add)
  - `Settings = new IconInfo("\uE713")` (Settings)
  - `Provider = new IconInfo("\uE968")` (Cloud)
  - `Run = new IconInfo("\uE7C5")` (Play)
  - `Delete = new IconInfo("\uE74D")` (Delete)

  Update T20's AgentsListPage to use `Icons.AgentDefault` instead of inline `new IconInfo("\uE945")`. ListItem subtitles: agents = `$"{providerName} · {model}"`; providers = `provider.BaseUrl`. Tags from `agent.Tags` rendered as `new Tag(t)` pills. Must NOT use emoji icons (programming-skill taste mandate); Segoe Fluent glyphs only. Must NOT hard-code glyphs at call sites — all glyph references go through `Icons.*`.
  Parallelization: Wave 6 | Blocked by: T20, T31 | Blocks: F1
  References: https://learn.microsoft.com/en-us/windows/powertoys/command-palette/samples (icon examples and Segoe Fluent codepoints); https://docs.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font
  Acceptance criteria (agent-executable): `Test-Path LittleAgentsExtension/Icons.cs` returns True; `Select-String -Path "LittleAgentsExtension\Pages\**\*.cs" -Pattern '"\\u[E-F][0-9A-F]{3}"' -AllMatches | Where-Object { $_.Path -notlike "*Icons.cs" } | Measure-Object | Select -ExpandProperty Count` returns 0 (no inline glyphs outside `Icons.cs`).
  QA: happy = both checks pass; failure = inline `new IconInfo("\uE945")` in any Pages/*.cs file, expect grep to find it. Evidence `.omo/evidence/task-41-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): centralize Segoe Fluent glyphs in Icons.cs; polish subtitles and tags`

- [ ] 42. EmptyContent on AgentsListPage when zero agents
  What to do / Must NOT do: in `AgentsListPage`, override `EmptyContent` (a `ListPage` property — verify against the Toolkit class). When `_cachedAgents.Length == 0` AND `_cachedProviders.Length == 0`: `EmptyContent = new ListItem(new InvokableCommand(() => CommandResult.GoToPage(new ProviderEditFormPage(...)))) { Title = "Add a provider first", Subtitle = "You'll need an OpenAI-compatible endpoint and API key" }`. When `_cachedAgents.Length == 0` AND `_cachedProviders.Length > 0`: `EmptyContent = new ListItem(new InvokableCommand(() => CommandResult.GoToPage(new AgentEditFormPage(...)))) { Title = "Create your first agent", Subtitle = "Define a system prompt and pick a provider" }`. Must NOT show EmptyContent when the user has filtered the list to empty via search — only when the underlying store is empty.
  Parallelization: Wave 6 | Blocked by: T20, T21, T30 | Blocks: F1
  References: T20; T30; D-FIRSTRUN.
  Acceptance criteria (agent-executable): manual smoke — fresh install (delete `LocalState\LittleAgents\*.json`) → AgentsListPage shows EmptyContent "Add a provider first"; add a provider → EmptyContent changes to "Create your first agent"; type in search box with both providers and agents present → EmptyContent does NOT show even when filter returns nothing.
  QA: happy = all 3 states show correctly; failure = trigger EmptyContent on filtered-empty (not store-empty), expect a regression test asserting that searching for `"zzz"` with non-empty store does NOT show EmptyContent to fail. Evidence `.omo/evidence/task-42-little-agents-mvp.txt`.
  Commit: Y | `feat(pages): EmptyContent guides first-launch users (D-FIRSTRUN)`

- [ ] 43. `.omo/evidence/task-43-little-agents-mvp.md` — manual QA checklist
  What to do / Must NOT do: write a **16-step** numbered checklist that an operator can execute step-by-step: (1) F5 Deploy from Visual Studio, (2) Reload via "Reload Command Palette Extension" command, (3) "Little Agents" appears in CmdPal top-level, (4) add provider with valid baseUrl + key (e.g. `https://api.openai.com/v1`), (5) verify `Get-ChildItem "$env:LOCALAPPDATA\Packages\*\LocalState\LittleAgents" -Recurse -File | Select-String -Pattern "<canary key>"` returns 0 matches across all files (secret hygiene), (6) add agent referencing the provider, (7) run agent with no `{input}` in template — first chunk visible within 1 s of click, (8) run agent with `{input}` — input form shows, submit, stream visible, (9) cancel mid-stream — body ends with `_(stopped)_` within 1 s of Stop click, (10) Reply works (multi-turn within the same ChatRunPage instance), (11) while agent A is streaming, invoke agent B whose template contains `{input}`; as soon as B's input form appears and before submitting it, A stops within 1 s (D-RUN-SINGLE input-gated path), then submit B and verify B streams, (12) Copy result puts the most recent assistant turn on clipboard; Copy transcript puts the full body, (13) edit agent persists across CmdPal reload, (14) delete provider blocks if agents reference it (D-ORPHAN toast lists names), (15) close+reopen CmdPal → agents and providers persist, ephemeral history does NOT, (16) `dotnet publish LittleAgentsExtension/LittleAgentsExtension.csproj -c Release -r win-x64 -p:Platform=x64 2>&1 | Select-String "error"` returns 0 matches (the test ignores warnings per D-AOT).
  Parallelization: Wave 6 | Blocked by: T37, T38, T39 | Blocks: F3
  References: D-FORM-REFRESH; D-ORPHAN; D-FIRSTRUN; D-AOT; D-CANCEL; D-RUN-SINGLE; D-VAULT.
  Acceptance criteria (agent-executable): `Test-Path .omo/evidence/task-43-little-agents-mvp.md` returns True; the file contains exactly 16 numbered steps (`(Get-Content task-43....md | Select-String -Pattern "^\d+\." -AllMatches).Count == 16`).
  QA: happy = file exists with 16 steps; failure = drop D-RUN-SINGLE step 11, expect step count assert and manual QA to fail. Evidence `.omo/evidence/task-43-little-agents-mvp.md` (this todo's deliverable IS its evidence).
  Commit: Y | `docs(qa): 15-step manual QA checklist`

## Final verification wave
> Runs in parallel after ALL todos. ALL must APPROVE. Surface results and wait for the user's explicit okay.

- [ ] F1. Plan compliance audit. Every todo has its acceptance criteria met; commit log shows the listed commit messages in some order; D-CANCEL, D-RUN-SINGLE, D-HISTORY, D-RENDER, D-AOT, D-EXP, D-CLIP, D-VAULT, D-FORM-REFRESH, D-SEL-CAP, D-ORPHAN, D-FIRSTRUN, D-ERR, D-BASEURL all referenced from at least one real code or docs path; the four spike evidence files exist and match the spike-validated implementation choice (vault vs DPAPI, body-mutation vs whole-replace, WinRT vs P/Invoke clipboard).
- [ ] F2. Code quality review. `dotnet build -c Release -p:Platform=x64` zero errors. `dotnet test` ≥ 25 passing. All new files ≤ 250 LoC (verify with `Get-ChildItem -Recurse *.cs | Where-Object { (Get-Content $_).Count -gt 250 }`). No `#pragma warning disable` outside the documented OPENAI001 exception. No `// TODO` left in shipped code.
- [ ] F3. Real manual QA. Execute every step of T43's checklist; record outcomes in `.omo/evidence/F3-manual-qa.md`. F3 fails if any of the 16 steps fails.
- [ ] F4. Scope fidelity. Confirm every "Must NOT have" item from `## Scope` is absent: grep for forbidden patterns (`tools:`, `function_call`, `image_url`, `audio`, `embedding`, `vector_store`, `/v1/models`). All matches must be in test fixtures only, not production code.

## Commit strategy

- One commit per todo as listed in each todo's `Commit:` line. Commits are ordered by wave; within a wave, parallel todos can land in any order.
- Conventional Commits: `<type>(<scope>): <summary>`. Types: `chore`, `feat`, `refactor`, `docs`, `test`, `fix`. Scopes seen here: `deps`, `tests`, `structure`, `storage`, `llm`, `pages`, `provider`, `publishing`, `qa`, `spike`.
- Spike artifacts in `.omo/evidence/` are committed alongside their owning todo.
- Branch strategy: a single feature branch `little-agents-mvp` based on the current default. No force-pushes. Final merge after F1-F4 all approve. **The plan does NOT instruct the worker to create commits, push, or open PRs unless the user explicitly asks** — per OpenCode git rules.

## Success criteria

- [ ] Sideloaded MSIX, visible in Command Palette as **Little Agents** with the configured icon.
- [ ] User can add, edit, and delete providers, with API key **encrypted at rest** — vault preferred, DPAPI fallback acceptable per D-VAULT — verified by canary grep returning zero plaintext matches in any file under `LocalState\LittleAgents`.
- [ ] User can add, edit, and delete agents bound to a provider+model.
- [ ] Selecting an agent in CmdPal:
  - if template has `{input}` ⇒ input form ⇒ submit ⇒ streaming markdown response (first chunk in UI within 1 s of network arrival).
  - if template has no `{input}` ⇒ immediate streaming.
  - `{selection}` substitutes with current Windows clipboard text (capped at 8 000 chars).
- [ ] Multi-turn Reply works within a single ChatRunPage instance. Re-invoking the agent from the list yields a fresh ChatRunPage with empty history and cancels the previous active run through `RunSessionCoordinator` (back-stack retention is acceptable only if the retained page is inactive).
- [ ] Stop command cancels the in-flight HTTP request within 1 s; verified in T18-style integration test that asserts the fake handler observed `CancellationToken.IsCancellationRequested == true`.
- [ ] **Copy result** puts the **most recent assistant turn** on the clipboard (tracked as `_lastAssistantText`); a separate **Copy transcript** command puts the full markdown body.
- [ ] xUnit suite ≥ 25 passing tests covering OpenAiChatClient (fake handler), AgentStore, ProviderStore, ISecretStore contract, TemplateRenderer.
- [ ] Release build succeeds with zero IL2026/IL3050 **errors** (warnings tolerated for OpenAI/Microsoft.Extensions.AI.OpenAI assemblies per D-AOT).
- [ ] No plaintext API key on disk in any file under the package's LocalState (verified by recursive all-file canary grep: `Get-ChildItem "$env:LOCALAPPDATA\Packages\*\LocalState\LittleAgents" -Recurse -File | Select-String -Pattern "sk-test"` returns zero matches).
- [ ] Every Scope-OUT item is absent from production code (verified by grep in F4).
- [ ] All four spike documents exist and inform the implementation: `spike-clipboard-mta.md`, `spike-vault-mta.md`, `spike-markdown-rerender.md`, plus the `task-N-*` evidence files.
