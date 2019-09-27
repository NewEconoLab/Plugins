using MongoDB.Bson;

namespace Neo.Plugins
{
    public class AssetInfo
    {
        public ObjectId _id { get; set; }
        public string assetid { get; set; }
        public string totalsupply { get; set; }
        public string name { get; set; }
        public string symbol { get; set; }
        public uint decimals { get; set; }
    }
}
