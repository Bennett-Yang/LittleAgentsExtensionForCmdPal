---
slug: little-agents-mvp
status: plan-approved-final-review-passed
intent: clear
pending-action: do not implement from this draft; `.omo/plans/little-agents-mvp.md` is the sole source of truth. Final two-lane review passed; deliver summary and wait for explicit $start-work.
approach: SUPERSEDED SUMMARY ONLY. The executable approach lives in `.omo/plans/little-agents-mvp.md`: JSON stores + Vault-or-DPAPI secret backend, OpenAI-compatible streaming client, DynamicListPage entry, Adaptive-Card editors, one-active-run ChatRunPage via RunSessionCoordinator, async first-run initialization, {input}/{selection} template substitution, tests-after xUnit suite, and best-effort AOT/trim.
---

# Draft: little-agents-mvp

> **SUPERSEDED DRAFT — DO NOT IMPLEMENT FROM THIS FILE.** This draft preserves historical planning context only. `.omo/plans/little-agents-mvp.md` is the single executable source of truth. If any line below conflicts with the plan, the plan wins.

## Components (topology ledger)
| id | outcome (one line) | status | evidence path |
| --- | --- | --- | --- |
| C1 | Persistence: agents.json + providers.json under LocalState; API keys in the plan-selected Vault-or-DPAPI encrypted backend | active | `JsonSettingsManager` toolkit class; `Windows.Security.Credentials.PasswordVault`; plan D-VAULT |
| C2 | LLM client: stream chat completions from any OpenAI-compatible endpoint by `(baseUrl, apiKey, model)` | active | `OpenAI 2.11.0` (https://github.com/openai/openai-dotnet) + `Microsoft.Extensions.AI.OpenAI 10.7.0` (https://github.com/dotnet/extensions/tree/main/src/Libraries/Microsoft.Extensions.AI.OpenAI) |
| C3 | Top-level `DynamicListPage` listing all agents + pinned "New agent / Manage providers / Settings" | active | rewrites `LittleAgentsExtension/Pages/LittleAgentsExtensionPage.cs:10` |
| C4 | Agent editor: `ContentPage` + `FormContent` Adaptive Card 1.6 (name, system prompt, user template, providerId, model, icon) | active | `SamplePagesExtension/Pages/SampleContentPage.cs` |
| C5 | Provider editor: `ContentPage` + `FormContent` (name, baseUrl, apiKey [password style], defaultModel) | active | same as C4 |
| C6 | Chat run page: `ContentPage` streaming a single mutable `MarkdownContent.Body`; multi-turn via "Reply" command appending to ephemeral `messages[]` | active | `MarkdownContent` + `BaseObservable` INPC; `IndexerPage.cs` cancellation pattern |

## Open assumptions (announced defaults — user to veto if wrong)
| assumption | adopted default | rationale | reversible? |
| --- | --- | --- | --- |
| LLM SDK | `OpenAI 2.11.0` + `Microsoft.Extensions.AI.OpenAI 10.7.0` via `chatClient.AsIChatClient()` | Official Microsoft-authored combo; custom-endpoint + streaming; AOT/trim is best-effort per final plan D-AOT | yes (could swap to raw `HttpClient` SSE) |
| API key store | Vault preferred, DPAPI fallback accepted, selected by final plan T45 | Encrypted-at-rest contract is enforced by recursive all-file canary grep; backend choice is not user-visible | yes |
| Agent + Provider metadata | JSON files under `Utilities.BaseSettingsPath("LittleAgents")` (custom store, not `JsonSettingsManager`) | `JsonSettingsManager` is shaped for typed singletons not record collections | yes |
| Streaming | `IAsyncEnumerable<ChatResponseUpdate>`, mutate `MarkdownContent.Body` per chunk | Best UX; SDK has no append API | yes (could fall back to non-streaming) |
| AOT/trim | Add `LittleAgentsJsonContext : JsonSerializerContext` for all DTOs | Required by csproj `IsAotCompatible=true` | no - mandatory |
| Provider↔Agent binding | Each agent stores `providerId + model`; same prompt across providers = separate agents | Simplest mental model | yes |
| Top-level search | `DynamicListPage.UpdateSearchText` filters by case-insensitive substring on agent.Name + Tags | Minimal viable filter | yes |
| Publisher identity | Leave manifest publisher placeholder; document in `docs/PUBLISHING.md` how to swap to self-signed cert | Not blocking; out of MVP | yes |
| Cancellation | Each run page owns one `CancellationTokenSource` per stream; cancels on explicit Stop or a superseding Re-run/Reply, never on `Dispose` because CmdPal does not call a page unload hook | Verified in final plan D-CANCEL / T34 CTS lifecycle | no - mandatory |
| Solution layout | New folders `Llm/`, `Storage/`, `Pages/Agents/`, `Pages/Providers/`; ≤ 250 LoC per file | programming-skill mandate | no - mandatory |
| Conversation history scope | Ephemeral, per run-page session; lost on Reload | "Quickly invoke" implies low-friction; persistence is scope creep | yes |

## Findings (cited - path:lines)

### Existing scaffold
- `LittleAgentsExtension/LittleAgentsExtension.cs:12-34` — `[Guid("36fde8e8-87f6-4677-a559-bc8ac65d97c4")] IExtension`. Will keep CLSID and class skeleton; only `_provider = new LittleAgentsExtensionCommandsProvider()` line stays.
- `LittleAgentsExtension/LittleAgentsExtensionCommandsProvider.cs:10-27` — replace single placeholder `CommandItem` with `new CommandItem(new AgentsListPage()) { ... }`; expose `Settings` via `_settingsManager.Settings` (toolkit pattern).
- `LittleAgentsExtension/Pages/LittleAgentsExtensionPage.cs:10-25` — DELETE (renamed/rewritten as `Pages/Agents/AgentsListPage.cs : DynamicListPage`).
- `LittleAgentsExtension/Program.cs:14-43` — KEEP as-is; the WinRTServer COM pattern is correct.
- `LittleAgentsExtension/Package.appxmanifest:11-79` — KEEP capabilities (`internetClient`, `runFullTrust`); KEEP CLSID; defer publisher swap.
- `LittleAgentsExtension/LittleAgentsExtension.csproj:7-94` — ADD `<PackageReference Include="OpenAI" />` and `<PackageReference Include="Microsoft.Extensions.AI.OpenAI" />`. Keep `IsAotCompatible=true`. Suppress `OPENAI001` warnings as needed.
- `Directory.Packages.props:6-16` — ADD `<PackageVersion Include="OpenAI" Version="2.11.0" />` and `<PackageVersion Include="Microsoft.Extensions.AI.OpenAI" Version="10.7.0" />`.

### Microsoft Command Palette extension SDK (verified)
- Toolkit base classes: `ListPage`, `DynamicListPage`, `ContentPage` from `Microsoft.CommandPalette.Extensions.Toolkit`. https://learn.microsoft.com/en-us/windows/powertoys/command-palette/microsoft-commandpalette-extensions-toolkit/
- `DynamicListPage` exposes `UpdateSearchText(old,new)`, `RaiseItemsChanged(int)`, `IsLoading`, `EmptyContent`. https://learn.microsoft.com/en-us/windows/powertoys/command-palette/microsoft-commandpalette-extensions-toolkit/dynamiclistpage
- `ContentPage` returns `IContent[] GetContent()` mixing `MarkdownContent`, `FormContent`. `MarkdownContent.Body` is INPC-observable via `BaseObservable.SetProperty`.
- `FormContent.TemplateJson` = Adaptive Card 1.6 JSON; `SubmitForm(string payload)` → `CommandResult.GoBack/GoHome/KeepOpen/Dismiss/ShowToast`.
- Live samples to cite: https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/SamplePagesExtension/Pages/SampleContentPage.cs, https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/SamplePagesExtension/Pages/SampleDynamicListPage.cs, https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ext/Microsoft.CmdPal.Ext.Indexer/Pages/IndexerPage.cs (production-grade DynamicListPage with cancellation), https://github.com/microsoft/PowerToys/blob/main/src/modules/cmdpal/ExtensionTemplate/TemplateCmdPalExtension/SettingsManager.cs.
- Reload after deploy: run "Reload Command Palette Extension".

### OpenAI .NET SDK (verified)
- `OpenAI 2.11.0` (https://www.nuget.org/packages/OpenAI/) — custom endpoint via `OpenAIClientOptions { Endpoint = new Uri(baseUrl) }`. Streaming via `CompleteChatStreamingAsync` → `AsyncCollectionResult<StreamingChatCompletionUpdate>` (await foreach + cancellation).
- `Microsoft.Extensions.AI.OpenAI 10.7.0` (https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/) — wraps `OpenAI 2.11.0`. `chatClient.AsIChatClient()` → `IChatClient.GetStreamingResponseAsync` returns `IAsyncEnumerable<ChatResponseUpdate>`. Targets net9.0. Some surfaces marked `[Experimental("OPENAI001")]`; suppress in csproj if needed.
- Custom endpoint behavior: https://github.com/openai/openai-dotnet/issues/416 — endpoint replaces URL prefix; with `BaseUrl` set to an API root such as `https://api.openai.com/v1`, the SDK/request layer must issue `.../v1/chat/completions` and must not double-append `/v1`.

### Secret storage (verified)
- `Windows.Security.Credentials.PasswordVault` — encrypted, per-user, per-package-identity. https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker — usable in `runFullTrust` MSIX.

### Async / cancellation patterns (verified)
- `Microsoft.CmdPal.Ext.Indexer/Pages/IndexerPage.cs` — `_cancellationTokenSource?.Cancel(); _cts = new(); ... Task.Run(async () => { ct.ThrowIfCancellationRequested(); ... RaiseItemsChanged(); }, ct);`. Mirror this exactly in C6.

## Decisions (with rationale)

### User-approved (3 forks)
1. **Invoke semantics: Multi-turn dialogue.** Run page maintains ephemeral `messages: List<ChatMessage>`. If template has `{input}`, first action = ask user for input via inline form (or prefilled with `{selection}`); subsequent "Reply" command re-opens the input form and appends to history. If no `{input}`, system-prompt-only one-shot is auto-run. History resets when a new run page is created or CmdPal is reloaded; cancellation does not rely on `Dispose`.
2. **Template variables: `{input}` + `{selection}`.** Custom string substitution: `{input}` from form, `{selection}` from the final plan's binary-safe `ClipboardReader.TryGetTextAsync()` (WinRT or P/Invoke path chosen by T44). Unknown `{...}` placeholders pass through verbatim with a warning at edit time.
3. **Test strategy: Tests-after, focused.** Add `LittleAgentsExtension.Tests` (xUnit + `Moq` or hand-mocked). Cover: (a) `OpenAiChatClient.StreamAsync` with fake `HttpMessageHandler` (assert request body, parse SSE, verify cancellation), (b) `AgentStore`/`ProviderStore` JSON round-trips, (c) `TemplateRenderer` for both placeholders + unknown placeholder + escape, (d) `ISecretStore` interface mock for vault adapter. UI-level pages = manual smoke test only.

### Architecture decisions
4. **Solution structure** — three new internal subnamespaces: `LittleAgentsExtension.Llm`, `LittleAgentsExtension.Storage`, plus `Pages/Agents/`, `Pages/Providers/`, `Pages/Run/` folders. ≤ 250 pure LoC per file.
5. **Build robustness** — pin OpenAI to 2.11.0 and Extensions.AI.OpenAI to 10.7.0 in `Directory.Packages.props`; add `[JsonSerializable]` source-gen for AOT.
6. **Error UX** — show LLM 4xx/5xx as a markdown error block with status, body excerpt, and a "Retry" command. Cancellation = silent (markdown ends with "_(stopped)_").
7. **First-launch UX** — when zero providers exist, "New agent" command is greyed out / shows toast "Please add a provider first"; the Manage Providers entry is highlighted.
8. **Form-to-list refresh** — child `FormContent.SubmitForm` returns `CommandResult.GoBack()` and the parent `DynamicListPage` re-invokes `GetItems()` on next render via `RaiseItemsChanged()` triggered by the agent store's `Changed` event.

## Scope IN
- Local-only extension. Sideload via Visual Studio Deploy + CmdPal "Reload Command Palette Extension".
- Custom OpenAI-compatible endpoint per provider.
- Ephemeral multi-turn chat per run-page session.
- `{input}` and `{selection}` template variables.
- Streaming markdown response with Copy / Stop / Reply / Re-run commands.
- AOT-compatible Release build.
- Tests-after xUnit suite for the four core seams.

## Scope OUT (Must NOT have)
- ❌ Image/audio/video input or output.
- ❌ Function calling / tool calling.
- ❌ RAG / file search / web search tools.
- ❌ Conversation persistence across Command Palette restarts.
- ❌ Agent import/export / sharing.
- ❌ Token usage / cost tracking display.
- ❌ Provider model auto-discovery (calling `/v1/models`).
- ❌ Theme / custom styling beyond Adaptive Cards defaults.
- ❌ SendInput automation into the foreground app.
- ❌ Microsoft Store submission (documented but not executed).
- ❌ Self-signed dev cert generation (documented in `docs/PUBLISHING.md` only).
- ❌ Localization beyond English in MVP (Adaptive Card fields are English-only).
- ❌ Telemetry / OpenTelemetry hookup (Extensions.AI supports it; we won't wire it).

## Open questions

(none — three forks resolved by the user's three approvals; defaults adopted otherwise)

## Approval gate
status: approved; plan file written; final two-lane review resumed after compaction
approved-at: 2026-06-19 (turn count 4-5 of this session)
approved-decisions:
  - Q1: Multi-turn dialogue (recommended option A)
  - Q2: `{input}` + `{selection}` (recommended option A)
  - Q3: Tests-after focused (recommended option A)

## Notes for resume after compaction
If this session is compacted: read this draft only to locate the plan and review ledger. Do **not** implement from this draft; `.omo/plans/little-agents-mvp.md` is the sole source of truth. The plan file is already written. Resume the final two-lane review (Momus + Oracle) if no approved result is recorded; patch only `.omo/` artifacts for review blockers. Do NOT re-explore. Do NOT begin execution — execution belongs to the worker, started only via `$start-work`.

## Review resume ledger
- 2026-06-19 continuation: old background IDs from the previous session were not retrievable, so final review was restarted.
- Native Momus final review: `bg_adb25b7f` / child session `ses_11fc7f947ffelllop8l6oHtb0m`.
- Oracle adversarial final review: `bg_e2f45a8f` / child session `ses_11fc7f8d8ffe0XB9PRyuHvtSOQ`.
- While those were running, self-check fixed stale draft/plan contradictions: D-CANCEL no-Dispose, D-ERR TLS-before-network ordering, D-BASEURL wording, T16 QA wording, T44/T46 decision references, and missing T11/T25 References/QA fields.
- Final review returned REVISE from both lanes. Applied fixes in `.omo/plans/little-agents-mvp.md`: T5 test TFM now matches production Windows TFM; T33 no longer awaits in synchronous `GetContent()` and uses async init task; D-RUN-SINGLE + `RunSessionCoordinator` added to enforce one active run; recursive all-file LocalState secret scans replaced JSON-only scans; T30/T40 and T32/T35 dependency cycles removed; T43 manual checklist expanded to 16 steps; this draft marked superseded so the plan is sole implementation source.
- Revision review: Momus approved; Oracle requested two more fixes. Applied them in `.omo/plans/little-agents-mvp.md`: T20 now creates `RunSessionCoordinator` compile stub before T20/T23 use it, and T32 upgrades it to full ChatRunPage-aware behavior; all app writes are explicitly under `LocalState\LittleAgents`, so recursive canary scans match the storage/settings/secrets path contract.
- Final recheck returned REVISE from both lanes. Applied latest fixes in `.omo/plans/little-agents-mvp.md`: D-RUN-SINGLE activation moved to first synchronous `GetContent()` via `ActivatePageOnce()` so `{input}` pages cancel prior runs before submit; T45 smoke log moved under `PathHelper.LocalStateDir` (`LocalState\LittleAgents\spike-vault-mta.log`).
- Latest final recheck passed: Oracle `bg_b2481d0a` APPROVED and Momus `bg_f3c829cf` APPROVED. Plan is ready for worker execution when the user explicitly says `$start-work` / "start work".
