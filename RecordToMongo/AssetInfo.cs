using MongoDB.Bson;
using Neo.IO.Json;

namespace Neo.Plugins
{
    public class AssetInfo
    {
        public string assetid { get; set; }
        public string totalsupply { get; set; }
        public string name { get; set; }
        public string symbol { get; set; }
        public uint decimals { get; set; }

        public BsonDocument ToBson()
        {
            BsonDocument jo = new BsonDocument();
            jo["assetid"] = assetid;
            jo["totalsupply"] = totalsupply;
            jo["name"] = name;
            jo["symbol"] = symbol;
            jo["decimals"] = decimals;
            return jo;
        }
    }
}
