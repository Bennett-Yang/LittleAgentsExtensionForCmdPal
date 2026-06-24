using System.Text;

namespace LittleAgentsExtension.Llm;

internal static class TemplateRenderer
{
    /// <summary>
    /// Renders the first ChatRunPage user turn; reply text bypasses template rendering and is sent verbatim.
    /// </summary>
    public static string Render(string template, string? input, string? selection)
    {
        string resolvedInput = input ?? string.Empty;
        string resolvedSelection = selection is { Length: > 8000 }
            ? "[truncated to 8000 chars]\n" + selection[..8000]
            : selection ?? string.Empty;

        StringBuilder builder = new(template.Length + 32);
        for (int index = 0; index < template.Length; index++)
        {
            char current = template[index];
            if (current == '{')
            {
                if (index + 1 < template.Length && template[index + 1] == '{')
                {
                    builder.Append('{');
                    index++;
                    continue;
                }

                int end = template.IndexOf('}', index + 1);
                if (end < 0)
                {
                    builder.Append(current);
                    continue;
                }

                string token = template[(index + 1)..end];
                builder.Append(token switch
                {
                    "input" => resolvedInput,
                    "selection" => resolvedSelection,
                    _ => "{" + token + "}",
                });
                index = end;
                continue;
            }

            if (current == '}' && index + 1 < template.Length && template[index + 1] == '}')
            {
                builder.Append('}');
                index++;
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }
}
