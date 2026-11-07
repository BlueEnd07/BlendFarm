using System;
using System.Collections.Generic;
using System.Text;

namespace LogicReinc.BlendFarm.Shared.Communication.RenderNode
{
    //Packets for syncing

    public enum Compression
    {
        Raw = 0,
        GZip = 1
    }

    [BlendFarmHeader("sync")]
    public class SyncRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long FileID { get; set; }
        public Compression Compression { get; set; }
    }

    [BlendFarmHeader("syncNetwork")]
    public class SyncNetworkRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long FileID { get; set; }
        public string WindowsPath { get; set; }
        public string LinuxPath { get; set; }
        public string MacOSPath { get; set; }
    }

    [BlendFarmHeader("syncResp")]
    public class SyncResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public bool SameFile { get; set; }
        public string Message { get; set; }
        public string UploadID { get; set; }
    }

    [BlendFarmHeader("syncUpload")]
    public class SyncUploadRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
        public byte[] Data { get; set; }
        public string Hash { get; set; }
    }
    [BlendFarmHeader("syncUploadResp")]
    public class SyncUploadResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    //Content-addressed delta sync (protocol 8)
    //1. syncHash: node either has the content already (Cached) or returns block signatures of a base version
    //2. syncDelta: start rebuilding the file, syncDeltaData: op stream chunks, syncDeltaDone: verify hash and store

    [BlendFarmHeader("syncHash")]
    public class SyncHashRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long FileID { get; set; }
        /// <summary>SHA-256 (hex) of the file content</summary>
        public string Hash { get; set; }
        /// <summary>Stable id of the project (hash of its path on the client), used to find a previous version as delta base</summary>
        public string ProjectKey { get; set; }
        public long FileSize { get; set; }
    }
    [BlendFarmHeader("syncHashResp")]
    public class SyncHashResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        /// <summary>Node already has this content, nothing to transfer</summary>
        public bool Cached { get; set; }
        /// <summary>Content hash of the base version the signatures belong to, null when there is no base</summary>
        public string BaseHash { get; set; }
        public int BlockSize { get; set; }
        public byte[] Signatures { get; set; }
    }

    [BlendFarmHeader("syncDelta")]
    public class SyncDeltaRequest : BlendFarmMessage
    {
        public string SessionID { get; set; }
        public long FileID { get; set; }
        public string Hash { get; set; }
        public string ProjectKey { get; set; }
        /// <summary>Base to copy blocks from, null for a full transfer</summary>
        public string BaseHash { get; set; }
        public int BlockSize { get; set; }
        public Compression Compression { get; set; }
    }
    [BlendFarmHeader("syncDeltaData")]
    public class SyncDeltaDataRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
        /// <summary>Serialized whole delta ops (see DeltaStream), optionally GZip compressed</summary>
        public byte[] Data { get; set; }
    }
    [BlendFarmHeader("syncDeltaDone")]
    public class SyncDeltaDoneRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
    }

    [BlendFarmHeader("syncComplete")]
    public class SyncCompleteRequest : BlendFarmMessage
    {
        public string UploadID { get; set; }
    }
    [BlendFarmHeader("syncCompleteResp")]
    public class SyncCompleteResponse : BlendFarmMessage
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }
}
