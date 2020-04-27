using MongoDB.Bson.Serialization.Attributes;

namespace Neo.Plugins
{
    [BsonIgnoreExtraElements]
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
    }
    public enum InvokeType
    {
        Call = 1,
        Create = 2,
        Update = 3,
        Destroy = 4
    }
}
