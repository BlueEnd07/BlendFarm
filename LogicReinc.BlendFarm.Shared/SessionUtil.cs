using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogicReinc.BlendFarm.Shared
{
    public static class SessionUtil
    {
        /// <summary>
        /// Session and task IDs are used in file names on render nodes,
        /// only allow plain identifiers (GUIDs) so they can't escape their directory
        /// </summary>
        public static bool IsSafeId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 128)
                return false;
            foreach (char c in id)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_'))
                    return false;
            return true;
        }

        public static string GetSessionNetworkPath(string path, string sessionId)
        {
            if (sessionId.Contains("-"))
                sessionId = sessionId.Substring(0, sessionId.IndexOf('-'));
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + $".{sessionId}.blend");
        }
    }
}
