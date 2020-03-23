using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Text;

namespace Neo.Plugins
{
    public class InvokeInfo
    {
        public InvokeType type;
        public string txid;
        public string from;
        public string to;
        public uint level;
        public uint index;
        public uint blockIndex;
        public ulong blockTimestamp;

        public BsonDocument ToBson()
        {
            BsonDocument jo = new BsonDocument();
            jo["type"] = (int)type;
            jo["txid"] = txid;
            jo["from"] = from;
            jo["to"] = to;
            jo["level"] = level;
            jo["index"] = index;
            jo["blockIndex"] = blockIndex;
            jo["blockTimestamp"] = (long)blockTimestamp;
            return jo;
        }
    }
    public enum InvokeType
    {
        Call = 1,
        Create = 2,
        Update = 3,
        Destroy = 4
    }
}
