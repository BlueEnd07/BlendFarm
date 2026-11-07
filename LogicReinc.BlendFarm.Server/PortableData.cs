using System;
using System.IO;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Keeps all data (settings, Blender versions, caches, render output) in one portable folder,
    /// so BlendFarm runs from any folder without an installer or admin rights.
    /// The folder is the application directory when it is writable, otherwise %LOCALAPPDATA%\BlendFarm
    /// (~/.local/share/BlendFarm on Linux). MacOS keeps using Application Support.
    /// </summary>
    public static class PortableData
    {
        private static string _root = null;
        private static readonly object _lock = new object();

        private static bool IsMac => SystemInfo.IsOS(SystemInfo.OS_MACOS) || SystemInfo.IsOS(SystemInfo.OS_MACOSARM64);

        /// <summary>
        /// Folder all relative data paths resolve against
        /// </summary>
        public static string Root
        {
            get
            {
                lock (_lock)
                {
                    if (_root == null)
                        _root = FindRoot();
                    return _root;
                }
            }
        }

        private static string FindRoot()
        {
            string baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir) && IsWritable(baseDir))
                return baseDir;

            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BlendFarm");
            Directory.CreateDirectory(fallback);
            Console.WriteLine($"Application folder is not writable, storing data in {fallback}");
            return fallback;
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, $".write-test-{Guid.NewGuid():N}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resolves a relative data path against the portable data folder
        /// </summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
                return path;
            return Path.Combine(Root, path);
        }

        /// <summary>
        /// Makes the data folder the working directory, so every relative path used by the server
        /// (BlenderData, RenderData, BlenderFiles, BlendCache, VersionCache, ...) lands in it,
        /// whatever folder or shortcut the program was started from.
        /// </summary>
        public static void UseAsWorkingDirectory()
        {
            if (IsMac)
                return;
            try
            {
                string root = Root;
                if (!string.Equals(Path.GetFullPath(Directory.GetCurrentDirectory()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                   Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                   StringComparison.OrdinalIgnoreCase))
                    Directory.SetCurrentDirectory(root);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to switch to data folder: {ex.Message}");
            }
        }
    }
}
