using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LogicReinc.BlendFarm.Server
{
    public class ServerSettings
    {
        private const string SETTINGS_PATH = "ServerSettings";

        /// <summary>
        /// Port to use for communication (May be blocked by firewall)
        /// </summary>
        public int Port { get; set; } = 15000;
        /// <summary>
        /// Port to use for broadcasting (May be blocked by firewall),
        /// -1 generally implies no broadcasting
        /// </summary>
        public int BroadcastPort { get; set; } = 16342;

        /// <summary>
        /// Used for storing different versions of Blender
        /// </summary>
        public string BlenderData = "BlenderData";
        /// <summary>
        /// Used for storing temporary render data
        /// </summary>
        public string RenderData = "RenderData";
        /// <summary>
        /// Used for storing temporary .blend files (sessions)
        /// </summary>
        public string BlenderFiles = "BlenderFiles";

        /// <summary>
        /// Synced .blend files kept by content, survives restarts so unchanged files never upload again
        /// </summary>
        public string BlendCache { get; set; } = "BlendCache";
        /// <summary>
        /// Size limit of the blend cache in GB, least recently used files are removed first. 0 = no limit
        /// </summary>
        public double BlendCacheGB { get; set; } = 30;

        /// <summary>
        /// Prevents software from replacing the .py if it changed
        /// </summary>
        public bool BypassScriptUpdate { get; set; } = false;

        /// <summary>
        /// Kill Blender if it prints nothing for this many minutes while rendering (hung process),
        /// so the client can retry the frame elsewhere. 0 disables the watchdog.
        /// Keep it generous, heavy scenes can be silent for a while during loading/BVH/shader compiles.
        /// </summary>
        public int WatchdogMinutes { get; set; } = 30;


        /// <summary>
        /// Very basic security password. NOT actually very secure, but by request.
        /// This is not stored safely, and only prevent basic external access..
        /// </summary>
        public string BasicSecurityPassword { get; set; } = null;

        #region Boilerplate
        private static ServerSettings _instance = null;
        public static ServerSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Load();
                return _instance;
            }
        }

        private static readonly object _saveLock = new object();
        public void Save()
        {
            string path = SystemInfo.RelativeToApplicationDirectory(SETTINGS_PATH);
            string json = JsonSerializer.Serialize(this);
            //Write to a temp file first so an interrupted save can't leave a corrupt settings file
            lock (_saveLock)
            {
                File.WriteAllText(path + ".tmp", json);
                File.Move(path + ".tmp", path, true);
            }
        }
        public static ServerSettings Load()
        {
            string path = SystemInfo.RelativeToApplicationDirectory(SETTINGS_PATH);
            if (!File.Exists(path))
                return new ServerSettings();
            try
            {
                return JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(path)) ?? new ServerSettings();
            }
            catch (Exception ex)
            {
                //Keep the broken file for recovery and start with defaults instead of failing to launch
                Console.WriteLine($"Failed to load {SETTINGS_PATH}, using defaults: {ex.Message}");
                try { File.Copy(path, path + ".corrupt", true); } catch { }
                return new ServerSettings();
            }
        }
        #endregion
    }
}
