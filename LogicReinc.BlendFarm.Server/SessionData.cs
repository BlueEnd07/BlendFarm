using LogicReinc.BlendFarm.Shared;
using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Contains information about sessions (Blend files and such)
    /// Keeps track of them by SessionID
    /// </summary>
    public class SessionData
    {
        /// <summary>
        /// Keeps track of ongoing sessions by SessionID
        /// </summary>
        public static Dictionary<string, SessionData> Sessions { get; private set; } = new Dictionary<string, SessionData>();

        /// <summary>
        /// Identifier for session
        /// </summary>
        public string SessionID { get; set; }

        /// <summary>
        /// FileID of session (Blendfile version)
        /// </summary>
        public long FileID { get; set; }
        /// <summary>
        /// Last Sync datetime
        /// </summary>
        public DateTime Updated { get; set; }

        /// <summary>
        /// If blendfile is a network file
        /// </summary>
        public bool IsNetworked { get; set; }
        /// <summary>
        /// Network Path
        /// </summary>
        public string NetworkedPath { get; set; }

        /// <summary>
        /// Keeps track if currently in use
        /// </summary>
        public bool InUse { get; set; } = false;

        /// <summary>
        /// Content hash of the blend cache file this session renders, null for the legacy per-session copy
        /// </summary>
        public string CacheHash { get; set; }

        /// <summary>
        /// Point this session at a file in the blend cache
        /// </summary>
        public void UseCachedFile(string hash, long fileId)
        {
            string legacy = GetLegacyBlendFilePath();
            CacheHash = hash;
            IsNetworked = false;
            UpdatedFile(fileId);
            BlendCache.Touch(hash);
            try { if (File.Exists(legacy)) File.Delete(legacy); } catch { }
        }

        /// <summary>
        /// Cache hashes in use by any session, these are never evicted
        /// </summary>
        public static List<string> GetCacheHashesInUse()
        {
            lock (Sessions)
                return Sessions.Values.Select(x => x.CacheHash).Where(x => x != null).Distinct().ToList();
        }

        /// <summary>
        /// Delete a session and associated blend file
        /// </summary>
        public void Delete()
        {
            lock (Sessions)
            {
                if (Sessions.ContainsKey(SessionID))
                    Sessions.Remove(SessionID);
            }
            //Cached files are shared and kept, only the legacy per-session copy is removed
            string blendFile = GetLegacyBlendFilePath();
            if (File.Exists(blendFile))
                File.Delete(blendFile);
        }

        /// <summary>
        /// Get or create a session with given SessionID 
        /// </summary>
        public static SessionData GetOrCreate(string sessionID)
        {
            if (!SessionUtil.IsSafeId(sessionID))
                throw new ArgumentException("Invalid SessionID");
            lock (Sessions)
            {
                if (!Sessions.ContainsKey(sessionID))
                {
                    Sessions.Add(sessionID, new SessionData()
                    {
                        SessionID = sessionID
                    });
                }

                return Sessions[sessionID];
            }
        }
        /// <summary>
        /// Get a session with given SessionID if it exists 
        /// </summary>
        public static SessionData Get(string sessionID)
        {
            lock (Sessions)
            {
                if (!Sessions.ContainsKey(sessionID))
                    return null;
                return Sessions[sessionID];
            }
        }

        /// <summary>
        /// Deletes all sessions with provided IDs and their associated files
        /// </summary>
        public static void CleanUp(params string[] args)
        {
            List<SessionData> sessions = null;
            lock (Sessions)
                sessions = args.Distinct().Where(x => Sessions.ContainsKey(x)).Select(x => Sessions[x]).ToList();
            foreach (SessionData ses in sessions)
                try
                {
                    ses.Delete();
                }
                catch (Exception ex) { }
        }
        public static async Task CleanUpDelayed(int ms, params string[] args)
        {
            List<SessionData> sessions = null;
            lock(Sessions)
                sessions = args.Distinct().Where(x => Sessions.ContainsKey(x)).Select(x => Sessions[x]).ToList();

            foreach (SessionData ses in sessions)
                ses.InUse = false;

            await Task.Delay(ms);
            foreach (SessionData ses in sessions)
                try
                {
                    if (!ses.InUse)
                        ses.Delete();
                }
                catch (Exception ex) { }
        }

        /// <summary>
        /// Called when updating file
        /// </summary>
        public void UpdatingFile()
        {
            FileID = -1;
        }
        /// <summary>
        /// Sets a new FileID (indicating updated blend file)
        /// </summary>
        public void UpdatedFile(long fileId)
        {
            FileID = fileId;
            Updated = DateTime.Now;
        }

        /// <summary>
        /// Get formatted path to blend file for this session
        /// </summary>
        /// <returns></returns>
        public string GetBlendFilePath()
        {
            if (CacheHash != null)
                return BlendCache.GetPath(CacheHash);
            return GetLegacyBlendFilePath();
        }
        private string GetLegacyBlendFilePath()
        {
            return Path.Combine(SystemInfo.RelativeToApplicationDirectory(ServerSettings.Instance.BlenderFiles), SessionID) + ".blend";
        }

        /// <summary>
        /// Get formatted path to Blend file for a specific session
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public static string GetFilePath(string session)
        {
            SessionData sess = null;
            lock (Sessions)
                if (Sessions.ContainsKey(session))
                    sess = Sessions[session];

            if (sess == null)
                return null;
            if (sess.FileID == 0 || sess.Updated == DateTime.MinValue)
                return null;

            string path = null;

            if (!sess.IsNetworked)
                path = sess.GetBlendFilePath();
            else
                path = sess.NetworkedPath;

            if (!File.Exists(path))
                return null;

            return path;
        }
    }
}
