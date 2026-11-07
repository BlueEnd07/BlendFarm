using LogicReinc.BlendFarm.Shared.Sync;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Client
{
    /// <summary>
    /// Computes the delta of one file against each base version a node offers, once per base,
    /// so nodes holding the same previous version share the work
    /// </summary>
    public class DeltaPlanner
    {
        private readonly string _file;
        private readonly ConcurrentDictionary<string, Lazy<Task<List<DeltaOp>>>> _plans = new ConcurrentDictionary<string, Lazy<Task<List<DeltaOp>>>>();

        public string File => _file;

        public DeltaPlanner(string file)
        {
            _file = file;
        }

        public Task<List<DeltaOp>> GetOps(string baseHash, byte[] signatures, int blockSize)
        {
            return _plans.GetOrAdd(baseHash + ":" + blockSize, key => new Lazy<Task<List<DeltaOp>>>(() => Task.Run(() =>
            {
                using (FileStream str = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024))
                    return BlockDelta.ComputeDelta(str, signatures, blockSize);
            }))).Value;
        }

        /// <summary>
        /// Transfer everything, when the node has no previous version
        /// </summary>
        public List<DeltaOp> GetFullOps()
        {
            return new List<DeltaOp>() { DeltaOp.Literal(0, new FileInfo(_file).Length) };
        }
    }
}
