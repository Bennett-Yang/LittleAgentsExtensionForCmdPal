# Privacy Policy for Little Agents Extension for Command Palette

**Effective date:** July 15, 2026  
**Last updated:** July 15, 2026

Little Agents Extension for Command Palette ("Little Agents") is developed and
published by BennettYang. This policy explains how Little Agents accesses,
uses, stores, and transmits information.

## Summary

Little Agents does not use advertising, analytics, tracking technologies, or
developer-operated cloud services. BennettYang does not receive telemetry,
prompts, API keys, clipboard contents, or AI responses through the extension.

Little Agents stores its configuration on the user's Windows device. When the
user invokes an agent, the extension sends a request directly from that device
to the OpenAI-compatible service configured by the user. The selected service
is an independent third party and processes the request under its own terms and
privacy policy.

## Information Little Agents accesses or processes

### Agent, provider, and application configuration

Little Agents stores the following information locally:

- Agent names, system prompts, user-prompt templates, model names, icons, and
  tags.
- Provider names, provider base URLs, and default model names.
- Application settings, including the optional system-prompt prefix and default
  temperature.

This information may contain personal information if the user chooses to enter
personal information into these fields.

### API credentials

The user may provide an API key for each configured AI provider. API keys are
stored separately from the provider configuration. Little Agents uses Windows
Password Vault when available. If Password Vault is unavailable, it uses
Windows Data Protection API (DPAPI) protection scoped to the current Windows
user and stores the protected value in the application's local package data.

An API key is sent only to the provider endpoint selected by the user as part of
authenticating an AI request. BennettYang does not receive or have access to the
key.

### Prompt and conversation content

When the user runs an agent, Little Agents may process:

- The agent's system prompt and user-prompt template.
- Text entered by the user.
- The selected model and temperature.
- Earlier user and assistant messages from the current run.
- Responses returned by the configured provider.

Conversation history is kept in memory for the active Command Palette run so
that replies can include prior context. Little Agents does not write conversation
history or AI responses to persistent storage. A user may explicitly copy a
response or transcript to the Windows clipboard.

### Windows clipboard

When an agent is invoked, Little Agents reads the current Windows clipboard text
transiently so it can resolve the `{selection}` template variable. Clipboard
text is included in the request sent to the configured provider only when the
agent's template contains `{selection}`. Clipboard text is limited to 8,000
characters before substitution. Clipboard images and other non-text formats are
not sent to AI providers.

Little Agents writes to the clipboard only when the user selects a command such
as **Copy result** or **Copy transcript**.

### Technical network information

The configured AI provider may receive the user's IP address, request headers,
model selection, and other standard network metadata when a request is made.
The provider, not BennettYang, determines how that information and the request
content are retained and processed.

## How information is used

Little Agents uses the information described above only to:

- Save and display the user's agents, providers, and settings.
- Authenticate requests to the user-selected AI provider.
- Render prompt templates and, when requested by the template, insert clipboard
  text.
- Send chat-completion requests and display streaming responses.
- Maintain conversation context during the current run.
- Copy content to the clipboard when the user requests it.

Little Agents does not use information for advertising, profiling, behavioral
tracking, sale, or data-broker purposes.

## Disclosure to AI providers

Running an agent instructs Little Agents to transmit the rendered prompt and
related request data directly to the provider endpoint configured by the user.
Depending on the template and conversation, transmitted data may include system
prompts, user-entered text, clipboard text, and previous conversation messages.

The user controls which provider endpoint is configured and whether to invoke
an agent. Users should review the selected provider's privacy policy, security
practices, data-retention rules, and location of processing before sending
sensitive information. Users should not submit personal information belonging
to another person unless they have permission and a lawful basis to do so.

Little Agents requires HTTPS for remote provider URLs so requests and API
credentials are protected by transport encryption. Plain HTTP is accepted only
for loopback services running on the same device, such as a provider configured
at `http://localhost`.

## Local storage and retention

Agent definitions, provider definitions, and settings remain in the application's
Windows package data until the user deletes them or removes the application and
its data. Provider deletion removes the associated credential when the provider
is no longer referenced by an agent. Windows controls the underlying package
storage, Password Vault, and DPAPI facilities.

BennettYang does not operate a server that stores this information and therefore
cannot retrieve, correct, export, or delete information stored on a user's
device or held by a user-selected AI provider.

## User choices and controls

Users can control processing by:

- Choosing the AI provider and endpoint.
- Reviewing prompts and templates before invoking an agent.
- Omitting `{selection}` from templates when clipboard text should not be sent.
- Deleting agents and providers, including saved provider credentials.
- Choosing not to run an agent or stopping an active response.
- Uninstalling Little Agents and removing its Windows application data.
- Contacting the selected AI provider regarding data processed by that provider.

## Security

Little Agents uses Windows Password Vault or current-user DPAPI protection for
stored API credentials. It does not intentionally write API keys into agent or
provider JSON files, and it does not disable TLS certificate validation.

No method of storage or transmission is completely secure. Users are
responsible for protecting their Windows account, selecting trustworthy
providers, using HTTPS for remote endpoints, and avoiding unnecessary sensitive
information in prompts or clipboard contents.

## Microsoft PowerToys, Microsoft Store, and GitHub

Little Agents runs as an extension of Microsoft PowerToys Command Palette and is
distributed through Microsoft Store. Microsoft may independently process
diagnostic, account, Store, or device information under Microsoft's own privacy
terms. That processing is not controlled by BennettYang and is outside this
policy.

If a user submits information through the project's GitHub repository or issue
tracker, GitHub processes that information under GitHub's privacy terms. GitHub
issues are public; users must not post API keys, private prompts, clipboard
contents, or other sensitive information there.

## Children's privacy

Little Agents is not directed to children under 13 and BennettYang does not
knowingly collect personal information from children. AI providers selected by
the user may impose their own age requirements.

## Changes to this policy

This policy may be updated when Little Agents' features or data practices
change. The updated policy will be published in the project repository with a
new "Last updated" date. Material changes will be disclosed through the project
or Store listing when appropriate.

## Contact

For privacy questions, open an issue at:

<https://github.com/Bennett-Yang/LittleAgentsExtensionForCmdPal/issues/new>

Do not include API keys, prompts, clipboard contents, or other sensitive
information in a public GitHub issue.
