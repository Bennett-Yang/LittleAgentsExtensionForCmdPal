# Little Agents MVP Decision Map

This file maps the plan decision IDs to shipped code or user-facing docs so the MVP audit can verify each decision is represented outside the work plan.

| ID | Decision | Implementation / docs path |
| --- | --- | --- |
| D-CANCEL | Only one active run should own streaming output; superseded or stopped streams cancel promptly. | `LittleAgentsExtension/Pages/Run/RunSessionCoordinator.cs`, `LittleAgentsExtension/Pages/Run/ChatRunPage.cs`, `LittleAgentsExtension.Tests/ChatRunPageStreamingTests.cs` |
| D-RUN-SINGLE | Starting a second run, including the input-gated path, cancels the previous active run. | `LittleAgentsExtension/Pages/Run/RunSessionCoordinator.cs`, `LittleAgentsExtension/Pages/Agents/AgentsListPage.cs`, `LittleAgentsExtension.Tests/AgentsListPageInvocationTests.cs` |
| D-HISTORY | Chat history is per `ChatRunPage`; re-running from the agent list creates a fresh page, while Reply appends to the current page. | `LittleAgentsExtension/Pages/Run/ChatRunPage.cs`, `LittleAgentsExtension/Pages/Run/ChatRunPage.Commands.cs`, `LittleAgentsExtension.Tests/ChatRunPageReplyTests.cs` |
| D-RENDER | Mutating `MarkdownContent.Body` plus `RaiseItemsChanged(0)` is the validated streaming render path. | `LittleAgentsExtension/Pages/Run/ChatRunPage.cs`, `.omo/evidence/spike-markdown-rerender.md` |
| D-AOT | Release builds may tolerate documented trim/analyzer warnings but must have zero publish/build errors. | `LittleAgentsExtension/LittleAgentsExtension.csproj`, `README.md`, `.omo/evidence/F2-code-quality.txt` |
| D-EXP | Provider errors are mapped to short toasts and scrubbed markdown without leaking API keys. | `LittleAgentsExtension/Pages/Run/ChatRunPage.cs`, `LittleAgentsExtension.Tests/ChatRunPageStreamingTests.cs`, `LittleAgentsExtension.Tests/OpenAiChatClientTests.cs` |
| D-CLIP | Clipboard reads use allocation-sized User32 access, bound managed text before rendering, and handle text, image-only, and empty payloads. | `LittleAgentsExtension/Llm/ClipboardReader.cs`, `LittleAgentsExtension/NativeMethods.txt`, `LittleAgentsExtension.Tests/ClipboardReaderTests.cs` |
| D-VAULT | API keys are stored outside provider JSON with Windows PasswordVault preferred and DPAPI fallback acceptable. | `LittleAgentsExtension/Storage/WindowsPasswordVaultSecretStore.cs`, `LittleAgentsExtension/Storage/DpapiSecretStore.cs`, `LittleAgentsExtension/Storage/SecretStoreFactory.cs`, `.omo/evidence/spike-vault-mta.md` |
| D-FORM-REFRESH | Store changes refresh list pages so newly saved providers and agents are immediately visible/runnable. | `LittleAgentsExtension/Pages/Agents/AgentsListPage.cs`, `LittleAgentsExtension/Pages/Providers/ProvidersListPage.cs`, `LittleAgentsExtension.Tests/AgentsListPageInvocationTests.cs` |
| D-SEL-CAP | Clipboard selection substitution is capped before rendering prompts. | `LittleAgentsExtension/Llm/TemplateRenderer.cs`, `LittleAgentsExtension.Tests/TemplateRendererTests.cs`, `README.md` |
| D-ORPHAN | Providers referenced by agents cannot be deleted; the user gets a toast listing referencing agents. | `LittleAgentsExtension/Pages/Providers/ProvidersListPage.cs`, `LittleAgentsExtension.Tests/ProviderDeleteOrphanTests.cs` |
| D-FIRSTRUN | Empty and first-run states guide users to create a provider before creating agents. | `LittleAgentsExtension/Pages/Agents/AgentsListPage.cs`, `LittleAgentsExtension.Tests/AgentsListPageEmptyContentTests.cs` |
| D-ERR | Network, TLS, status-code, and generic provider errors remove the exact configured credential before display. | `LittleAgentsExtension/Pages/Run/ChatRunPage.cs`, `LittleAgentsExtension.Tests/ChatRunPageStreamingTests.cs` |
| D-BASEURL | Provider base URLs must use HTTPS except for loopback HTTP, include the API root, and are documented with examples. | `LittleAgentsExtension/Pages/Providers/ProviderEditFormPage.cs`, `LittleAgentsExtension.Tests/ProviderEditFormTests.cs`, `README.md` |
