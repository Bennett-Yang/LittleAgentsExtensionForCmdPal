using System;
using System.IO;
using Windows.Storage;

namespace LittleAgentsExtension.Common;

internal static class PathHelper
{
    private const string LittleAgentsFolderName = "LittleAgents";

    internal static string LocalStateDir
    {
        get
        {
            string basePath;

            try
            {
                basePath = ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception)
            {
                basePath = Path.Combine(Path.GetTempPath(), "LittleAgentsExtension");
            }

            string localStateDir = Path.Combine(basePath, LittleAgentsFolderName);
            Directory.CreateDirectory(localStateDir);
            return localStateDir;
        }
    }
}
