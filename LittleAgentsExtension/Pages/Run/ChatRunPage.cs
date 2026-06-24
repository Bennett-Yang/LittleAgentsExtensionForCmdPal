using System;
using System.Globalization;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
namespace LittleAgentsExtension;
internal sealed partial class ChatRunPage : ContentPage
{
    private readonly AgentDef _agent;
    private readonly ProviderDef _provider;
    private readonly string _apiKey;
    private readonly ILlmChatClient _llm;
    private readonly Func<RuntimeSettings> _settings;
    private readonly Func<Task<string?>> _readClipboardAsync;
    private readonly RunSessionCoordinator _session;
    private readonly IClipboardWriter _clipboard;
    private readonly MarkdownContent _output;
    private RunInputForm? _inputForm;
    private bool _showingInput;
    private bool _initialized;
    private Task? _initTask;
    private readonly List<ChatMessage> _history;
    private string _lastAssistantText;
    private string? _lastToastText;
    private string _initialUserMsg;
    private CancellationTokenSource? _cts;
    private Task? _streamTask;
    private bool _pageActivated;
    private const string SecretPattern = @"(?i)(bearer\s+)?sk-[A-Za-z0-9_-]{4,}";
    public ChatRunPage(AgentDef agent, ProviderDef provider, string apiKey, ILlmChatClient llm, RunSessionCoordinator session, IClipboardWriter clipboard, Func<RuntimeSettings>? settings = null, Func<Task<string?>>? readClipboardAsync = null)
    {
        _agent = agent;
        _provider = provider;
        _apiKey = apiKey;
        _llm = llm;
        _settings = settings ?? (() => RuntimeSettings.Default);
        _readClipboardAsync = readClipboardAsync ?? (() => new ClipboardReader().TryGetTextAsync());
        _session = session;
        _clipboard = clipboard;
        _output = new MarkdownContent() { Body = string.Empty };
        _history = new List<ChatMessage>();
        _lastAssistantText = string.Empty;
        _initialUserMsg = string.Empty;

        Title = _agent.Name;
        Icon = _agent.Icon is not null ? new IconInfo(_agent.Icon) : new IconInfo("\uE945");
        InitializeCommands();
    }
    public override IContent[] GetContent()
    {
        ActivatePageOnce();
        if (!_initialized)
        {
            _initialized = true;
            _output.Body = "_Preparing..._";
            _initTask = Task.Run(InitializeFirstRunAsync);
            return [_output];
        }

        return _showingInput ? [_inputForm!] : [_output];
    }
    private async Task InitializeFirstRunAsync()
    {
        string? selection = await _readClipboardAsync().ConfigureAwait(false);
        if (RequiresInput(_agent.UserTemplate))
        {
            _inputForm = new RunInputForm("Your input", text =>
            {
                _initialUserMsg = TemplateRenderer.Render(_agent.UserTemplate, input: text, selection: selection);
                _showingInput = false;
                RaiseItemsChanged(0);
                StartStream(_initialUserMsg);
            });
            _showingInput = true;
            RaiseItemsChanged(0);
            return;
        }

        _initialUserMsg = TemplateRenderer.Render(_agent.UserTemplate, input: null, selection: selection);
        _showingInput = false;
        RaiseItemsChanged(0);
        StartStream(_initialUserMsg);
    }
    private static bool RequiresInput(string template)
    {
        for (int index = 0; index < template.Length; index++)
        {
            if (template[index] != '{') { continue; }

            if (index + 1 < template.Length && template[index + 1] == '{')
            {
                index++;
                continue;
            }

            int end = template.IndexOf('}', index + 1);
            if (end < 0) { continue; }
            if (template[(index + 1)..end] == "input") { return true; }
            index = end;
        }
        return false;
    }
    private void StartStream(string renderedUserMsg)
    {
        ActivatePageOnce();
        _cts?.Cancel();
        CancellationTokenSource thisCts = new();
        _cts = thisCts;
        CancellationToken ct = thisCts.Token;
        _history.Add(new ChatMessage(ChatRole.User, renderedUserMsg));
        ChatRequest request = BuildRequest();
        _streamTask = Task.Run(() => RunStreamAsync(renderedUserMsg, request, thisCts, ct), CancellationToken.None);
    }
    private ChatRequest BuildRequest()
    {
        RuntimeSettings settings = _settings();
        ChatMessage[] messages = new ChatMessage[_history.Count + 1];
        messages[0] = new ChatMessage(ChatRole.System, settings.SystemPrefix + _agent.SystemPrompt);
        _history.CopyTo(messages, 1);
        return new ChatRequest(_agent.Model, messages, settings.Temperature);
    }
    private async Task RunStreamAsync(string renderedUserMsg, ChatRequest request, CancellationTokenSource thisCts, CancellationToken ct)
    {
        StringBuilder transcript = new(_output.Body);
        transcript.AppendLine().Append("**You:** ").AppendLine(renderedUserMsg).AppendLine().Append("**Assistant:** ");
        UpdateOutput(transcript.ToString(), thisCts);
        StringBuilder assistant = new();
        try
        {
            await foreach (string chunk in _llm.StreamAsync(request, _provider, _apiKey, ct).WithCancellation(ct).ConfigureAwait(false))
            {
                assistant.Append(chunk);
                UpdateOutput(transcript.ToString() + assistant, thisCts);
            }
            if (ReferenceEquals(_cts, thisCts))
            {
                _lastAssistantText = assistant.ToString();
                _history.Add(new ChatMessage(ChatRole.Assistant, _lastAssistantText));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            UpdateOutput(transcript.ToString() + assistant + "\n_(stopped)_", thisCts);
        }
        catch (Exception exception)
        {
            UpdateOutput(transcript.ToString() + assistant + "\n\n" + MapErrorToMarkdown(exception), thisCts);
            ShowToast(MapErrorToToast(exception));
        }
        finally
        {
            thisCts.Dispose();
            if (ReferenceEquals(_cts, thisCts)) { _cts = null; }
        }
    }
    private void UpdateOutput(string body, CancellationTokenSource owner)
    {
        if (!ReferenceEquals(_cts, owner)) { return; }
        _output.Body = body;
    }
    private static string MapErrorToMarkdown(Exception exception)
    {
        if (HasTlsFailure(exception)) { return "> **Provider TLS certificate rejected.** Use a trusted certificate or http://localhost for local servers."; }

        int? status = TryGetStatus(exception);
        string message = TrimForMarkdown(ScrubSecrets(exception.Message));
        if (status is not null) { return $"> **Error {status}:** {message}"; }
        if (exception is HttpRequestException) { return $"> **Network error:** {message}"; }
        return $"> **Error:** {message}";
    }
    private static string MapErrorToToast(Exception exception)
    {
        if (HasTlsFailure(exception)) { return "TLS rejected"; }
        int? status = TryGetStatus(exception);
        if (status is not null) { return $"Error {status}"; }
        return exception is HttpRequestException ? "Network error" : "Error";
    }
    private static bool HasTlsFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException || current is CryptographicException || current.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
    private static int? TryGetStatus(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            int? status = TryGetStatusProperty(current, "Status") ?? TryGetStatusProperty(current, "StatusCode");
            if (status is not null) { return status; }
        }
        return null;
    }
    private static int? TryGetStatusProperty(Exception exception, string name)
    {
        object? value = exception.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(exception);
        try { return value is null ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException) { return null; }
    }
    private static string ScrubSecrets(string message) => Regex.Replace(message, SecretPattern, "***");
    private static string TrimForMarkdown(string message) => message.Length <= 400 ? message : message[..400];
    private CommandResult ShowToast(string message) { _lastToastText = message; return CommandResult.ShowToast(new ToastArgs() { Message = message, Result = CommandResult.KeepOpen(), }); }
    internal void ActivatePageOnce()
    {
        if (_pageActivated) { return; }
        _pageActivated = true;
        _session.Activate(this);
    }
    internal void CancelActiveStreamForSupersededRun() => _cts?.Cancel();
}
internal readonly record struct RuntimeSettings(string SystemPrefix, double? Temperature) { public static RuntimeSettings Default { get; } = new(string.Empty, null); }
internal sealed partial class RunInputForm : FormContent
{
    private readonly Action<string> _onSubmit;
    public RunInputForm(string label, Action<string> onSubmit)
    {
        _onSubmit = onSubmit;
        TemplateJson = $$"""{"$schema":"http://adaptivecards.io/schemas/adaptive-card.json","type":"AdaptiveCard","version":"1.6","body":[{"type":"Input.Text","id":"Input","label":{{Quote(label)}},"isMultiline":true}],"actions":[{"type":"Action.Submit","title":"Run"}]}""";
    }
    public override CommandResult SubmitForm(string payload)
    {
        JsonObject? input = JsonNode.Parse(payload)?.AsObject();
        _onSubmit(input?["Input"]?.ToString() ?? string.Empty);
        return CommandResult.KeepOpen();
    }
    private static string Quote(string value) => $"\"{JsonEncodedText.Encode(value)}\"";
}
