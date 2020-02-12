using MongoDB.Bson;
using Neo.IO.Json;

namespace Neo.Plugins
{
    public class Address
    {
        public Address()
        {
            addr = string.Empty;
            firstuse = new AddrUse();
            lastuse = new AddrUse();
            txcount = 0;
        }

        public ObjectId _id { get; set; }
        public string addr { get; set; }
        public AddrUse firstuse { get; set; }
        public AddrUse lastuse { get; set; }
        public int txcount { get; set; }

        public JObject ToJson()
        {
            JObject jo = new JObject();
            jo["addr"] = addr;
            jo["firstuse"] = firstuse.ToJson();
            jo["lastuse"] = lastuse.ToJson();
            jo["txcount"] = txcount;
            return jo;
        }
    }
}
