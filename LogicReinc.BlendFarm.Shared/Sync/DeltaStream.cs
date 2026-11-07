using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace LogicReinc.BlendFarm.Shared.Sync
{
    /// <summary>
    /// Wire format for delta ops. Each chunk holds whole ops:
    /// copy = [1][int64 block][int64 count], literal = [2][int32 length][bytes]
    /// </summary>
    public static class DeltaStream
    {
        private const byte OP_COPY = 1;
        private const byte OP_LITERAL = 2;

        /// <summary>
        /// Encodes ops into chunks of roughly maxChunk bytes, reading literal bytes from newFile.
        /// Large literals are split so a chunk never exceeds maxChunk (plus a header).
        /// </summary>
        public static IEnumerable<byte[]> EncodeChunks(Stream newFile, IEnumerable<DeltaOp> ops, int maxChunk, bool gzip)
        {
            MemoryStream chunk = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(chunk);
            byte[] buffer = new byte[maxChunk];

            foreach (DeltaOp op in ops)
            {
                if (op.IsCopy)
                {
                    writer.Write(OP_COPY);
                    writer.Write(op.Start);
                    writer.Write(op.Length);
                }
                else
                {
                    long offset = op.Start;
                    long left = op.Length;
                    while (left > 0)
                    {
                        int space = maxChunk - (int)chunk.Length;
                        if (space < 64 * 1024)
                        {
                            writer.Flush();
                            yield return Finish(chunk, gzip);
                            chunk = new MemoryStream();
                            writer = new BinaryWriter(chunk);
                            space = maxChunk;
                        }
                        int part = (int)Math.Min(left, space);
                        newFile.Seek(offset, SeekOrigin.Begin);
                        int read = ReadFully(newFile, buffer, part);
                        if (read != part)
                            throw new IOException("File changed while syncing (unexpected end of file)");
                        writer.Write(OP_LITERAL);
                        writer.Write(part);
                        writer.Write(buffer, 0, part);
                        offset += part;
                        left -= part;
                    }
                }
                if (chunk.Length >= maxChunk)
                {
                    writer.Flush();
                    yield return Finish(chunk, gzip);
                    chunk = new MemoryStream();
                    writer = new BinaryWriter(chunk);
                }
            }
            writer.Flush();
            if (chunk.Length > 0)
                yield return Finish(chunk, gzip);
        }

        private static byte[] Finish(MemoryStream chunk, bool gzip)
        {
            if (!gzip)
                return chunk.ToArray();
            using (MemoryStream compressed = new MemoryStream())
            {
                using (GZipStream zip = new GZipStream(compressed, CompressionLevel.Fastest, true))
                    zip.Write(chunk.GetBuffer(), 0, (int)chunk.Length);
                return compressed.ToArray();
            }
        }

        internal static int ReadFully(Stream stream, byte[] buffer, int count)
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

    /// <summary>
    /// Receiving side: rebuilds the new file from a base file and incoming chunks, hashing the output as it is written
    /// </summary>
    public class DeltaApplier : IDisposable
    {
        private readonly Stream _base;
        private readonly Stream _output;
        private readonly int _blockSize;
        private readonly long _baseBlocks;
        private readonly bool _gzip;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _blockBuffer;

        public long BytesWritten { get; private set; }

        public DeltaApplier(Stream baseFile, Stream output, int blockSize, bool gzip)
        {
            _base = baseFile;
            _output = output;
            _blockSize = blockSize;
            _gzip = gzip;
            _baseBlocks = baseFile != null ? baseFile.Length / blockSize : 0;
            _blockBuffer = new byte[blockSize];
        }

        public void ApplyChunk(byte[] data)
        {
            Stream input = new MemoryStream(data);
            if (_gzip)
                input = new GZipStream(input, CompressionMode.Decompress);
            using (input)
            using (BinaryReader reader = new BinaryReader(input))
            {
                while (true)
                {
                    int kind = input.ReadByte();
                    if (kind < 0)
                        break;
                    if (kind == 1)
                    {
                        long block = reader.ReadInt64();
                        long count = reader.ReadInt64();
                        if (_base == null || block < 0 || count < 0 || block + count > _baseBlocks)
                            throw new InvalidDataException("Delta copies blocks outside of the base file");
                        _base.Seek(block * _blockSize, SeekOrigin.Begin);
                        for (long i = 0; i < count; i++)
                        {
                            if (DeltaStream.ReadFully(_base, _blockBuffer, _blockSize) != _blockSize)
                                throw new InvalidDataException("Base file is shorter than expected");
                            Write(_blockBuffer, _blockSize);
                        }
                    }
                    else if (kind == 2)
                    {
                        int length = reader.ReadInt32();
                        if (length < 0)
                            throw new InvalidDataException("Invalid literal length");
                        byte[] bytes = reader.ReadBytes(length);
                        if (bytes.Length != length)
                            throw new InvalidDataException("Truncated literal in delta chunk");
                        Write(bytes, length);
                    }
                    else
                        throw new InvalidDataException($"Unknown delta op {kind}");
                }
            }
        }

        private void Write(byte[] data, int length)
        {
            _output.Write(data, 0, length);
            _hash.AppendData(data, 0, length);
            BytesWritten += length;
        }

        /// <summary>
        /// SHA-256 (lowercase hex) of everything written
        /// </summary>
        public string FinishHash()
        {
            _output.Flush();
            return ContentHash.ToHex(_hash.GetHashAndReset());
        }

        public void Dispose()
        {
            _hash.Dispose();
        }
    }

    public static class ContentHash
    {
        /// <summary>
        /// SHA-256 of a file as lowercase hex
        /// </summary>
        public static string ComputeFile(string path)
        {
            using (FileStream str = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024))
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(str));
        }

        public static string ComputeString(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value ?? "")));
        }

        /// <summary>
        /// A 64 character lowercase hex SHA-256, safe to use as a file name
        /// </summary>
        public static bool IsValid(string hash)
        {
            if (hash == null || hash.Length != 64)
                return false;
            foreach (char c in hash)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return false;
            return true;
        }

        public static string ToHex(byte[] bytes)
        {
            char[] chars = new char[bytes.Length * 2];
            const string hex = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[bytes[i] >> 4];
                chars[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(chars);
        }
    }
}
