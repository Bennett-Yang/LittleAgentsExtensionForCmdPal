using LittleAgentsExtension.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LittleAgentsExtension.Storage;

internal sealed class AgentStore
{
    private const int SchemaVersion = 1;

    private readonly object _gate = new();
    private readonly string _storePath;

    public AgentStore()
        : this(Path.Combine(PathHelper.LocalStateDir, "agents.json"))
    {
    }

    internal AgentStore(string storePath)
    {
        _storePath = storePath;
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
    }

    public event EventHandler? Changed;

    public AgentDef[] Load()
    {
        if (!File.Exists(_storePath))
        {
            return Array.Empty<AgentDef>();
        }

        lock (_gate)
        {
            try
            {
                using FileStream stream = File.OpenRead(_storePath);
                AgentsFile? file = JsonSerializer.Deserialize(stream, LittleAgentsJsonContext.Default.AgentsFile);
                return file?.Agents ?? Array.Empty<AgentDef>();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Invalid agent store at '{_storePath}'.", exception);
            }
        }
    }

    public void Save(AgentDef[] agents)
    {
        lock (_gate)
        {
            string tempPath = Path.Combine(Path.GetDirectoryName(_storePath)!, Path.GetRandomFileName());

            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new AgentsFile(SchemaVersion, agents), LittleAgentsJsonContext.Default.AgentsFile);
            }

            File.Move(tempPath, _storePath, overwrite: true);
        }
    }

    public void Upsert(AgentDef agent)
    {
        lock (_gate)
        {
            List<AgentDef> agents = Load().ToList();
            int existingIndex = agents.FindIndex(existing => existing.Id == agent.Id);
            if (existingIndex >= 0)
            {
                agents[existingIndex] = agent;
            }
            else
            {
                agents.Add(agent);
            }

            Save(agents.ToArray());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            List<AgentDef> agents = Load().ToList();
            if (agents.RemoveAll(agent => agent.Id == id) > 0)
            {
                Save(agents.ToArray());
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
