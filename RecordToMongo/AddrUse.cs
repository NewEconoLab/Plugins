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

        public JObject ToJson()
        {
            JObject jo = new JObject();
            jo["txid"] = txid;
            jo["blockindex"] = blockindex;
            jo["blocktime"] = blocktime.ToString();
            return jo;
        }
    }
}
