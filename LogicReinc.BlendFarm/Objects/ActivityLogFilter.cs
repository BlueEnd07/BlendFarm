using LogicReinc.BlendFarm.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LogicReinc.BlendFarm.Objects
{
    public enum ActivityLogLevelFilter
    {
        All,
        /// <summary>Informational entries (Info and Success).</summary>
        Info,
        Warnings,
        Errors
    }

    /// <summary>
    /// Pure filtering/formatting for the activity log: level, node and free-text search.
    /// Entries carry no node field; the node is recognised by the title prefix that every node message starts with
    /// ("Local connected", "Local / PC: rendering ..."). Titles map to a node key (the node's name), because
    /// a node's title changes once its computer name is known.
    /// </summary>
    public class ActivityLogFilter
    {
        public const int MaxEntries = 2000;

        public ActivityLogLevelFilter Level { get; set; } = ActivityLogLevelFilter.All;
        /// <summary>Only entries belonging to this node; null/empty means every entry.</summary>
        public string Node { get; set; }
        public string Search { get; set; }

        public bool IsActive => Level != ActivityLogLevelFilter.All || !string.IsNullOrEmpty(Node) || !string.IsNullOrWhiteSpace(Search);

        public bool Matches(RenderActivityLogEntry entry, IReadOnlyDictionary<string, string> nodeByTitle)
        {
            if (entry == null) return false;
            switch (Level)
            {
                case ActivityLogLevelFilter.Info:
                    if (entry.LogType != ActivityLogType.Info && entry.LogType != ActivityLogType.Success) return false;
                    break;
                case ActivityLogLevelFilter.Warnings:
                    if (entry.LogType != ActivityLogType.Warning) return false;
                    break;
                case ActivityLogLevelFilter.Errors:
                    if (entry.LogType != ActivityLogType.Error) return false;
                    break;
            }
            if (!string.IsNullOrEmpty(Node) &&
                !string.Equals(FindNode(entry.Message, nodeByTitle), Node, StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrWhiteSpace(Search) &&
                (entry.Message ?? "").IndexOf(Search.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            return true;
        }

        public List<RenderActivityLogEntry> Apply(IEnumerable<RenderActivityLogEntry> entries, IReadOnlyDictionary<string, string> nodeByTitle)
        {
            return entries.Where(e => Matches(e, nodeByTitle)).ToList();
        }

        /// <summary>The node key a message belongs to (longest matching title wins), or null.</summary>
        public static string FindNode(string message, IReadOnlyDictionary<string, string> nodeByTitle)
        {
            if (string.IsNullOrEmpty(message) || nodeByTitle == null) return null;
            string bestTitle = null;
            string bestNode = null;
            foreach (var pair in nodeByTitle)
            {
                string title = pair.Key;
                if (string.IsNullOrEmpty(title) || !message.StartsWith(title, StringComparison.Ordinal)) continue;
                bool boundary = message.Length == title.Length || message[title.Length] == ':' || message[title.Length] == ' ';
                if (boundary && (bestTitle == null || title.Length > bestTitle.Length))
                {
                    bestTitle = title;
                    bestNode = pair.Value;
                }
            }
            return bestNode;
        }

        public static string LevelLabel(ActivityLogType type) => type switch
        {
            ActivityLogType.Success => "SUCCESS",
            ActivityLogType.Warning => "WARN",
            ActivityLogType.Error => "ERROR",
            _ => "INFO"
        };

        public static string FormatEntry(RenderActivityLogEntry entry) =>
            $"{entry.Time} [{LevelLabel(entry.LogType)}] {entry.Message}";

        public static string FormatEntries(IEnumerable<RenderActivityLogEntry> entries)
        {
            var sb = new StringBuilder();
            foreach (var entry in entries)
                sb.AppendLine(FormatEntry(entry));
            return sb.ToString();
        }
    }
}
