using MongoDB.Bson.Serialization.Attributes;

namespace Neo.Plugins
{
    [BsonIgnoreExtraElements]
    public class Address
    {
        public Address()
        {
            addr = string.Empty;
            firstuse = new AddrUse();
            lastuse = new AddrUse();
            txcount = 0;
        }

        public string addr { get; set; }
        public AddrUse firstuse { get; set; }
        public AddrUse lastuse { get; set; }
        public int txcount { get; set; }
    }
}
