using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace LogicReinc.BlendFarm.Shared.Sync
{
    /// <summary>
    /// A delta instruction: copy blocks from the base file, or send bytes from the new file
    /// </summary>
    public struct DeltaOp
    {
        public bool IsCopy;
        /// <summary>Copy: first base block index. Literal: offset in the new file.</summary>
        public long Start;
        /// <summary>Copy: number of blocks. Literal: number of bytes.</summary>
        public long Length;

        public static DeltaOp Copy(long block, long count) => new DeltaOp() { IsCopy = true, Start = block, Length = count };
        public static DeltaOp Literal(long offset, long length) => new DeltaOp() { IsCopy = false, Start = offset, Length = length };
    }

    /// <summary>
    /// rsync-style block delta. The receiver signs fixed-size blocks of the file it already has (the base),
    /// the sender scans the new file with a rolling checksum to find those blocks at any offset,
    /// so only changed or inserted bytes need to be transferred.
    /// </summary>
    public static class BlockDelta
    {
        public const int DEFAULT_BLOCK_SIZE = 64 * 1024;
        /// <summary>Bytes per block in a serialized signature: weak (4) + strong (8)</summary>
        public const int SIGNATURE_ENTRY_SIZE = 12;

        private const int FILTER_BITS = 22;
        private const int FILTER_MASK = (1 << FILTER_BITS) - 1;

        /// <summary>
        /// Signs all full blocks of the base file, packed as [weak uint32][strong uint64] per block
        /// </summary>
        public static byte[] ComputeSignatures(Stream baseFile, int blockSize = DEFAULT_BLOCK_SIZE)
        {
            using (MemoryStream result = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(result))
            using (MD5 md5 = MD5.Create())
            {
                byte[] block = new byte[blockSize];
                while (ReadFully(baseFile, block, blockSize) == blockSize)
                {
                    writer.Write(WeakChecksum(block, 0, blockSize));
                    writer.Write(StrongChecksum(md5, block, 0, blockSize));
                }
                writer.Flush();
                return result.ToArray();
            }
        }

        /// <summary>
        /// Computes the operations that rebuild newFile from a base file with the given signatures
        /// </summary>
        public static List<DeltaOp> ComputeDelta(Stream newFile, byte[] signatures, int blockSize = DEFAULT_BLOCK_SIZE)
        {
            List<DeltaOp> ops = new List<DeltaOp>();
            long fileLength = newFile.Length;
            int blockCount = signatures.Length / SIGNATURE_ENTRY_SIZE;

            //Weak checksum -> base blocks, plus a bit filter to reject most positions without a dictionary lookup
            Dictionary<uint, List<int>> weakMap = new Dictionary<uint, List<int>>(blockCount);
            ulong[] strong = new ulong[blockCount];
            bool[] filter = new bool[1 << FILTER_BITS];
            for (int i = 0; i < blockCount; i++)
            {
                uint weak = BitConverter.ToUInt32(signatures, i * SIGNATURE_ENTRY_SIZE);
                strong[i] = BitConverter.ToUInt64(signatures, i * SIGNATURE_ENTRY_SIZE + 4);
                if (!weakMap.TryGetValue(weak, out List<int> list))
                    weakMap[weak] = list = new List<int>(1);
                list.Add(i);
                filter[weak & FILTER_MASK] = true;
            }

            long literalStart = 0;
            if (blockCount == 0 || fileLength < blockSize)
            {
                AddLiteral(ops, 0, fileLength);
                return ops;
            }

            using (MD5 md5 = MD5.Create())
            {
                //Sliding buffer over the new file, window [pos, pos + blockSize] plus one byte to roll in
                byte[] buffer = new byte[Math.Max(blockSize * 64, 16 * 1024 * 1024)];
                long bufferStart = 0;
                int bufferLength = 0;
                newFile.Seek(0, SeekOrigin.Begin);

                long pos = 0;
                uint a = 0, b = 0;
                bool rollValid = false;
                int expectedNext = -1;

                while (pos + blockSize <= fileLength)
                {
                    int needed = (int)Math.Min(blockSize + 1, fileLength - pos);
                    if (pos - bufferStart + needed > bufferLength)
                    {
                        //Refill starting at pos
                        newFile.Seek(pos, SeekOrigin.Begin);
                        bufferStart = pos;
                        bufferLength = ReadFully(newFile, buffer, buffer.Length);
                    }
                    int p = (int)(pos - bufferStart);

                    if (!rollValid)
                    {
                        uint w = WeakChecksum(buffer, p, blockSize);
                        a = w & 0xffff;
                        b = w >> 16;
                        rollValid = true;
                    }
                    uint weakNow = a | (b << 16);

                    if (filter[weakNow & FILTER_MASK] && weakMap.TryGetValue(weakNow, out List<int> candidates))
                    {
                        ulong s = StrongChecksum(md5, buffer, p, blockSize);
                        int match = -1;
                        //Prefer the block following the previous match, keeps copies contiguous
                        if (expectedNext >= 0 && expectedNext < blockCount && strong[expectedNext] == s && candidates.Contains(expectedNext))
                            match = expectedNext;
                        else
                            foreach (int c in candidates)
                                if (strong[c] == s) { match = c; break; }

                        if (match >= 0)
                        {
                            AddLiteral(ops, literalStart, pos - literalStart);
                            AddCopy(ops, match);
                            pos += blockSize;
                            literalStart = pos;
                            expectedNext = match + 1;
                            rollValid = false;
                            continue;
                        }
                    }

                    //Roll the window one byte forward
                    if (pos + blockSize < fileLength)
                    {
                        uint outByte = buffer[p];
                        uint inByte = buffer[p + blockSize];
                        a = (a - outByte + inByte) & 0xffff;
                        b = (b - (uint)blockSize * outByte + a) & 0xffff;
                    }
                    pos++;
                }
            }
            AddLiteral(ops, literalStart, fileLength - literalStart);
            return ops;
        }

        /// <summary>
        /// Rebuilds the new file: copies from baseFile, literal bytes come from readLiteral(length) in op order
        /// </summary>
        public static void ApplyDelta(Stream baseFile, IEnumerable<DeltaOp> ops, Func<long, byte[]> readLiteral, Stream output, int blockSize = DEFAULT_BLOCK_SIZE)
        {
            byte[] buffer = new byte[blockSize];
            foreach (DeltaOp op in ops)
            {
                if (op.IsCopy)
                {
                    baseFile.Seek(op.Start * blockSize, SeekOrigin.Begin);
                    for (long i = 0; i < op.Length; i++)
                    {
                        if (ReadFully(baseFile, buffer, blockSize) != blockSize)
                            throw new InvalidDataException("Base file is shorter than the delta expects");
                        output.Write(buffer, 0, blockSize);
                    }
                }
                else
                {
                    byte[] data = readLiteral(op.Length);
                    if (data.LongLength != op.Length)
                        throw new InvalidDataException("Literal data length mismatch");
                    output.Write(data, 0, data.Length);
                }
            }
        }

        /// <summary>
        /// Bytes that have to be transferred for these ops
        /// </summary>
        public static long LiteralBytes(IEnumerable<DeltaOp> ops)
        {
            long total = 0;
            foreach (DeltaOp op in ops)
                if (!op.IsCopy)
                    total += op.Length;
            return total;
        }

        /// <summary>
        /// rsync's rolling checksum: low 16 bits a = sum of bytes, high 16 bits b = weighted sum
        /// </summary>
        public static uint WeakChecksum(byte[] data, int offset, int length)
        {
            uint a = 0, b = 0;
            for (int i = 0; i < length; i++)
            {
                uint x = data[offset + i];
                a += x;
                b += (uint)(length - i) * x;
            }
            return (a & 0xffff) | ((b & 0xffff) << 16);
        }

        private static ulong StrongChecksum(MD5 md5, byte[] data, int offset, int length)
        {
            byte[] hash = md5.ComputeHash(data, offset, length);
            return BitConverter.ToUInt64(hash, 0);
        }

        private static void AddCopy(List<DeltaOp> ops, int block)
        {
            if (ops.Count > 0)
            {
                DeltaOp last = ops[ops.Count - 1];
                if (last.IsCopy && last.Start + last.Length == block)
                {
                    last.Length++;
                    ops[ops.Count - 1] = last;
                    return;
                }
            }
            ops.Add(DeltaOp.Copy(block, 1));
        }

        private static void AddLiteral(List<DeltaOp> ops, long offset, long length)
        {
            if (length <= 0)
                return;
            if (ops.Count > 0)
            {
                DeltaOp last = ops[ops.Count - 1];
                if (!last.IsCopy && last.Start + last.Length == offset)
                {
                    last.Length += length;
                    ops[ops.Count - 1] = last;
                    return;
                }
            }
            ops.Add(DeltaOp.Literal(offset, length));
        }

        private static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int done = 0;
            while (done < count)
            {
                int read = stream.Read(buffer, done, count - done);
                if (read <= 0)
                    break;
                done += read;
            }
            return done;
        }
    }
}
