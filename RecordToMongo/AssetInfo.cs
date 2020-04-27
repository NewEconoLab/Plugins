﻿using MongoDB.Bson.Serialization.Attributes;

namespace Neo.Plugins
{
    [BsonIgnoreExtraElements]
    public class AssetInfo
    {
        public string assetid { get; set; }
        public string totalsupply { get; set; }
        public string name { get; set; }
        public string symbol { get; set; }
        public uint decimals { get; set; }
    }
}
