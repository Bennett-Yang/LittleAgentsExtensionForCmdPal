namespace LittleAgentsExtension.Llm;

internal sealed record ChatRequest(string Model, ChatMessage[] Messages, double? Temperature);
internal sealed record ChatMessage(ChatRole Role, string Content);
internal enum ChatRole { System, User, Assistant }
