using MongoDB.Bson;
using Neo.IO.Json;
using System;

namespace Neo.Plugins
{
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

        public BsonDocument ToBson()
        {
            BsonDocument jo = new BsonDocument();
            jo["txid"] = txid;
            jo["blockindex"] = blockindex;
            jo["blocktime"] = blocktime.ToString();
            return jo;
        }
    }
}
