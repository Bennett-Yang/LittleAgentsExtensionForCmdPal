using System.Collections;
using System.Reflection;
using LittleAgentsExtension.Llm;
using LittleAgentsExtension.Storage;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed partial class AgentsListPageInvocationTests
{
    private static AgentDef CreateAgent(string id, string providerId, string userTemplate)
    {
        return new AgentDef(id, id == "agent-a" ? "Agent A" : "Agent B", "You are concise.", userTemplate, providerId, "model-a", "\uE700", ["test"]);
    }

    private static ProviderDef CreateProvider(string id)
    {
        return new ProviderDef(id, "Provider A", "https://provider.example.test/v1", "model-a");
    }

    private static ICommandResult InvokePrimaryCommand(IListItem item)
    {
        ICommand command = GetProperty<ICommand>(item, "Command");
        InvokableCommand invokable = command as InvokableCommand ?? throw new InvalidOperationException("Command is not invokable.");
        return invokable.Invoke();
    }

    private static ChatRunPage AssertDirectChatRunPage(IListItem item)
    {
        ICommand command = GetProperty<ICommand>(item, "Command");
        return Assert.IsType<ChatRunPage>(command);
    }

    private static T GetProperty<T>(object target, string propertyName)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing property {propertyName}.");
        return (T)property.GetValue(target)!;
    }

    private static T GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing field {fieldName}.");
        return (T)field.GetValue(target)!;
    }

    private static void StartStream(ChatRunPage page, string message)
    {
        MethodInfo method = typeof(ChatRunPage).GetMethod("StartStream", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing StartStream.");
        method.Invoke(page, [message]);
    }

    private static Task GetStreamTask(ChatRunPage page) => GetField<Task?>(page, "_streamTask") ?? throw new InvalidOperationException("Missing stream task.");

    private static void AssertContainsString(object target, string expected)
    {
        Assert.True(ContainsString(target, expected), $"Expected {target.GetType().FullName} to contain '{expected}'.");
    }

    private static T? FindObject<T>(object target) where T : class
    {
        return FindObject<T>(target, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
    }

    private static T? FindObject<T>(object? target, HashSet<object> visited, int depth) where T : class
    {
        if (target is null || depth > 6)
        {
            return null;
        }

        if (target is T match)
        {
            return match;
        }

        if (!ShouldDescend(target, visited))
        {
            return null;
        }

        foreach (object? child in GetChildren(target))
        {
            T? childMatch = FindObject<T>(child, visited, depth + 1);
            if (childMatch is not null)
            {
                return childMatch;
            }
        }

        return null;
    }

    private static bool ContainsString(object target, string expected)
    {
        return ContainsString(target, expected, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
    }

    private static bool ContainsString(object? target, string expected, HashSet<object> visited, int depth)
    {
        if (target is null || depth > 6)
        {
            return false;
        }

        if (target is string text)
        {
            return text.Contains(expected, StringComparison.Ordinal);
        }

        if (!ShouldDescend(target, visited))
        {
            return false;
        }

        return GetChildren(target).Any(child => ContainsString(child, expected, visited, depth + 1));
    }

    private static bool ShouldDescend(object target, HashSet<object> visited)
    {
        Type targetType = target.GetType();
        return !targetType.IsPrimitive && !targetType.IsEnum && targetType != typeof(decimal) && visited.Add(target);
    }

    private static IEnumerable<object?> GetChildren(object target)
    {
        if (target is IEnumerable enumerable and not string)
        {
            foreach (object? item in enumerable)
            {
                yield return item;
            }
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type targetType = target.GetType();
        foreach (PropertyInfo property in targetType.GetProperties(flags).Where(property => property.GetIndexParameters().Length == 0))
        {
            object? value;
            try { value = property.GetValue(target); }
            catch (Exception exception) when (exception is TargetInvocationException or InvalidOperationException or NotSupportedException) { continue; }
            yield return value;
        }

        foreach (FieldInfo field in targetType.GetFields(flags))
        {
            yield return field.GetValue(target);
        }
    }

    private static async IAsyncEnumerable<string> EmptyStream()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<string> WaitForCancellation(StreamCall call, TaskCompletionSource firstStarted, TaskCompletionSource firstCancelled)
    {
        firstStarted.SetResult();
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, call.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            firstCancelled.SetResult();
            throw;
        }

        yield break;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ScriptedLlmChatClient(Func<StreamCall, IAsyncEnumerable<string>> stream) : ILlmChatClient
    {
        public List<ChatRequest> Requests { get; } = new();

        public IAsyncEnumerable<string> StreamAsync(ChatRequest request, ProviderDef provider, string apiKey, CancellationToken ct)
        {
            Requests.Add(request);
            return stream(new StreamCall(ct));
        }
    }

    private sealed class StreamCall(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;
    }

    private sealed class InMemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.Ordinal);

        public void Set(string providerId, string apiKey) => _secrets[providerId] = apiKey;

        public string? TryGet(string providerId) => _secrets.TryGetValue(providerId, out string? apiKey) ? apiKey : null;

        public void Delete(string providerId) => _secrets.Remove(providerId);
    }

    private sealed class TempStoreDirectory : IDisposable
    {
        private readonly string _rootPath;

        internal TempStoreDirectory()
        {
            _rootPath = Path.Combine(Path.GetTempPath(), $"little-agents-invocation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootPath);
        }

        internal AgentStore CreateAgentStore() => new(Path.Combine(_rootPath, "agents.json"));

        internal ProviderStore CreateProviderStore() => new(Path.Combine(_rootPath, "providers.json"));

        public void Dispose()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
    }
}
