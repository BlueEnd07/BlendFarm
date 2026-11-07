using LogicReinc.BlendFarm.Shared.Sync;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Tests
{
    /// <summary>
    /// Delta sync must always rebuild the exact new file, and should only send what changed.
    /// </summary>
    [TestClass]
    public class BlockDeltaTest
    {
        private const int BLOCK = 1024;

        private static byte[] RandomBytes(int length, int seed)
        {
            byte[] data = new byte[length];
            new Random(seed).NextBytes(data);
            return data;
        }

        /// <summary>Delta from baseData to newData, rebuild, assert identical, return literal bytes sent</summary>
        private static long RoundTrip(byte[] baseData, byte[] newData)
        {
            byte[] sigs = BlockDelta.ComputeSignatures(new MemoryStream(baseData), BLOCK);
            MemoryStream newStream = new MemoryStream(newData);
            List<DeltaOp> ops = BlockDelta.ComputeDelta(newStream, sigs, BLOCK);

            IEnumerator<DeltaOp> literals = ops.Where(x => !x.IsCopy).GetEnumerator();
            MemoryStream output = new MemoryStream();
            BlockDelta.ApplyDelta(new MemoryStream(baseData), ops, len =>
            {
                literals.MoveNext();
                return newData.Skip((int)literals.Current.Start).Take((int)len).ToArray();
            }, output, BLOCK);

            CollectionAssert.AreEqual(newData, output.ToArray());
            return BlockDelta.LiteralBytes(ops);
        }

        [TestMethod]
        public void IdenticalFileSendsNothing()
        {
            byte[] data = RandomBytes(BLOCK * 50, 1);
            Assert.AreEqual(0, RoundTrip(data, data));
        }

        [TestMethod]
        public void InPlaceEditSendsOneBlock()
        {
            byte[] baseData = RandomBytes(BLOCK * 50, 2);
            byte[] newData = (byte[])baseData.Clone();
            newData[BLOCK * 20 + 7] ^= 0xFF;
            Assert.AreEqual(BLOCK, RoundTrip(baseData, newData));
        }

        [TestMethod]
        public void InsertionShiftsButStillMatches()
        {
            byte[] baseData = RandomBytes(BLOCK * 50, 3);
            byte[] inserted = RandomBytes(333, 4);
            byte[] newData = baseData.Take(BLOCK * 10 + 5).Concat(inserted).Concat(baseData.Skip(BLOCK * 10 + 5)).ToArray();
            long sent = RoundTrip(baseData, newData);
            //Inserted bytes plus the one block they landed in, everything after the shift is still found
            Assert.IsTrue(sent <= inserted.Length + BLOCK * 2, $"Sent {sent} bytes");
        }

        [TestMethod]
        public void DeletionAndAppend()
        {
            byte[] baseData = RandomBytes(BLOCK * 50, 5);
            byte[] newData = baseData.Take(BLOCK * 5).Concat(baseData.Skip(BLOCK * 8)).Concat(RandomBytes(500, 6)).ToArray();
            long sent = RoundTrip(baseData, newData);
            Assert.IsTrue(sent <= 500 + BLOCK, $"Sent {sent} bytes");
        }

        [TestMethod]
        public void EdgeCases()
        {
            //Empty base, everything is literal
            byte[] data = RandomBytes(BLOCK * 3 + 17, 7);
            Assert.AreEqual(data.Length, RoundTrip(new byte[0], data));
            //New file smaller than a block
            Assert.AreEqual(10, RoundTrip(data, data.Take(10).ToArray()));
            //Empty new file
            Assert.AreEqual(0, RoundTrip(data, new byte[0]));
            //Unaligned tail that matches nothing is sent, the full blocks are copied
            Assert.AreEqual(17, RoundTrip(data, data));
        }

        [TestMethod]
        public void RepetitiveDataRebuildsCorrectly()
        {
            //Many identical blocks (e.g. zeroed buffers in a .blend), copies may point at any equal block
            byte[] baseData = new byte[BLOCK * 20];
            byte[] newData = new byte[BLOCK * 25 + 3];
            newData[BLOCK * 12] = 1;
            RoundTrip(baseData, newData);
        }
    }
}
