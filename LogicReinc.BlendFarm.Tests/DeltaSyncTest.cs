using LogicReinc.BlendFarm.Client;
using LogicReinc.BlendFarm.Server;
using LogicReinc.BlendFarm.Shared.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// End to end sync against a local render node: full transfer, delta transfer, and cache hit.
    /// Doesn't need Blender, only the sync protocol.
    /// </summary>
    [TestClass]
    public class DeltaSyncTest
    {
        private const int PORT = 18591;
        private const string NODE = "DeltaNode";

        private static RenderServer _server;
        private static string _dir;

        [ClassInitialize]
        public static void Init(TestContext context)
        {
            _dir = Path.Combine(Path.GetTempPath(), "bf_deltasync_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            if (Directory.Exists(BlendCache.Directory))
                Directory.Delete(BlendCache.Directory, true);
            _server = new RenderServer(PORT, -1, true);
            _server.Start();
            Thread.Sleep(1000);
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            _server.Stop();
            try { Directory.Delete(_dir, true); } catch { }
        }

        private static async Task<BlendFarmManager> Connect(string file)
        {
            BlendFarmManager manager = new BlendFarmManager(file, "none", null, Path.Combine(_dir, "local_" + Guid.NewGuid().ToString("N")));
            manager.AddNode(NODE, $"127.0.0.1:{PORT}");
            await manager.Connect(NODE);
            return manager;
        }

        private static byte[] NodeCopy(BlendFarmManager manager, string file)
        {
            string path = SessionData.GetFilePath(manager.GetOrCreateSession(file).SessionID);
            Assert.IsNotNull(path, "Node has no file for the session");
            return File.ReadAllBytes(path);
        }

        [TestMethod]
        public async Task FullThenDeltaThenCached()
        {
            string file = Path.Combine(_dir, "scene.blend");
            byte[] v1 = new byte[3 * 1024 * 1024];
            new Random(42).NextBytes(v1);
            File.WriteAllBytes(file, v1);

            //1. First sync, node has nothing: full transfer
            BlendFarmManager manager = await Connect(file);
            await manager.Sync(file, false);
            CollectionAssert.AreEqual(v1, NodeCopy(manager, file));
            string hash1 = ContentHash.ComputeFile(file);
            Assert.IsTrue(BlendCache.Has(hash1));

            //2. Edit in the middle and insert bytes: delta against v1
            byte[] v2 = v1.Take(1_000_000).Concat(new byte[5000]).Concat(v1.Skip(1_000_000)).ToArray();
            v2[2_500_000] ^= 0xFF;
            File.WriteAllBytes(file, v2);
            File.SetLastWriteTime(file, DateTime.Now.AddSeconds(5));
            await manager.Sync(file, true);
            CollectionAssert.AreEqual(v2, NodeCopy(manager, file));
            //Signatures of v1 were computed, so v1 served as delta base
            Assert.IsTrue(Directory.GetFiles(BlendCache.Directory, hash1 + ".*.sig").Length > 0, "Delta base was not used");
            int cachedFiles = Directory.GetFiles(BlendCache.Directory, "*.blend").Length;
            manager.DisconnectAll();

            //3. Restart the client (new session, same content): served from the cache, nothing new stored
            BlendFarmManager restarted = await Connect(file);
            await restarted.Sync(file, false);
            CollectionAssert.AreEqual(v2, NodeCopy(restarted, file));
            Assert.AreEqual(cachedFiles, Directory.GetFiles(BlendCache.Directory, "*.blend").Length);
            restarted.DisconnectAll();
        }
    }
}
