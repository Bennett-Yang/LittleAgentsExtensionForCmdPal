# Little Agents

## Description

Little Agents is a PowerToys Command Palette extension for saving prompt templates as named agents and invoking any OpenAI-compatible LLM from Command Palette.

Each agent stores a system prompt, a user template, a provider, and a model name. When you run an agent from Command Palette, Little Agents renders the template, sends it to the configured chat completions endpoint, and shows the reply in the run page.

## 中文摘要

Little Agents 是一个 PowerToys Command Palette 扩展。
它可以把 prompt 模板保存为命名 agent。
你可以从 Command Palette 调用任何 OpenAI-compatible LLM。
每个 agent 绑定 provider、model、system prompt 和 user template。
运行时会渲染模板并显示回复。

## Prerequisites

Windows 11 build 19041+.

PowerToys with Command Palette v0.100+ installed.

Visual Studio 2022 17.13+ for development.

## Build / Deploy / Reload

Open `LittleAgentsExtension.sln` in Visual Studio.

Press `F5` to build and deploy the MSIX package.

Open Command Palette and run `Reload Command Palette Extension` so CmdPal reloads Little Agents.

## Supported Providers

Little Agents can call any service that exposes an OpenAI-compatible `/v1/chat/completions` endpoint. The provider base URL should include the API root, including `/v1` when the service expects it. The request layer appends `/chat/completions` after that base URL.

Worked configuration examples:

| Provider | Base URL | Model example | Notes |
| --- | --- | --- | --- |
| OpenAI | `https://api.openai.com/v1` | `gpt-4.1-mini` | Uses `POST https://api.openai.com/v1/chat/completions`. |
| OpenRouter | `https://openrouter.ai/api/v1` | `openai/gpt-4.1-mini` | Uses `POST https://openrouter.ai/api/v1/chat/completions`. |
| Ollama | `http://localhost:11434/v1` | `llama3.1` | Use local HTTP unless you have a trusted certificate. |

Other OpenAI-compatible providers, such as DeepSeek, Together, Groq, or llama.cpp's server, can be configured the same way if they expose `/v1/chat/completions`.

## Template Variables

Little Agents supports these template variables:

| Variable | Meaning |
| --- | --- |
| `{input}` | Text you type each time you invoke the agent. |
| `{selection}` | Current Windows clipboard text, capped at 8,000 characters. |

Examples:

```text
Translate to English: {selection}
Summarize this: {input}
```

## Limitations

Little Agents does not support images, audio, tool calls, RAG, document upload, cost tracking, or provider model auto discovery.

AOT and trim support are best effort. Release builds may show documented trim warnings for the `OpenAI` and `Microsoft.Extensions.AI.OpenAI` assemblies.

Little Agents does not bypass TLS certificate validation. For local providers without a trusted certificate, use `http://localhost` or install a trusted certificate in Windows.

## License

No license file present yet.

## Screenshots

![Agents list placeholder](docs/screenshots/agents-list.png)

![Run page placeholder](docs/screenshots/run-page.png)
