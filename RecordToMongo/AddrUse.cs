using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Neo.Plugins
{
    [BsonIgnoreExtraElements]
    public class AddrUse
    {
        public AddrUse()
        {
            txid = string.Empty;
            blockindex = 0;
            blocktime = new DateTime();
        }

        public string txid { get; set; }
        public uint blockindex { get; set; }
        public DateTime blocktime { get; set; }
    }
}
