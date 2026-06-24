using LittleAgentsExtension.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LittleAgentsExtension.Storage;

internal sealed class ProviderStore
{
    private const int SchemaVersion = 1;

    private readonly object _gate = new();
    private readonly string _storePath;

    public ProviderStore()
        : this(Path.Combine(PathHelper.LocalStateDir, "providers.json"))
    {
    }

    internal ProviderStore(string storePath)
    {
        _storePath = storePath;
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
    }

    public event EventHandler? Changed;

    public ProviderDef[] Load()
    {
        if (!File.Exists(_storePath))
        {
            return Array.Empty<ProviderDef>();
        }

        lock (_gate)
        {
            try
            {
                using FileStream stream = File.OpenRead(_storePath);
                ProvidersFile? file = JsonSerializer.Deserialize(stream, LittleAgentsJsonContext.Default.ProvidersFile);
                return file?.Providers ?? Array.Empty<ProviderDef>();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Invalid provider store at '{_storePath}'.", exception);
            }
        }
    }

    public void Save(ProviderDef[] providers)
    {
        lock (_gate)
        {
            string tempPath = Path.Combine(Path.GetDirectoryName(_storePath)!, Path.GetRandomFileName());

            using (FileStream stream = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new ProvidersFile(SchemaVersion, providers), LittleAgentsJsonContext.Default.ProvidersFile);
            }

            File.Move(tempPath, _storePath, overwrite: true);
        }
    }

    public void Upsert(ProviderDef provider)
    {
        lock (_gate)
        {
            List<ProviderDef> providers = Load().ToList();
            int existingIndex = providers.FindIndex(existing => existing.Id == provider.Id);
            if (existingIndex >= 0)
            {
                providers[existingIndex] = provider;
            }
            else
            {
                providers.Add(provider);
            }

            Save(providers.ToArray());
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            List<ProviderDef> providers = Load().ToList();
            if (providers.RemoveAll(provider => provider.Id == id) > 0)
            {
                Save(providers.ToArray());
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
