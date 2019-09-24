using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.VM;
using System;
using System.Linq;
using NEL.Simple.SDK.Helper;
using MongoDB.Bson;
using Neo.Wallets;
using System.Text;

namespace Neo.Plugins
{
    public class Logger : Plugin,IRecordPlugin
    {
        public override string Name => "RecordToMongo";

        public override void Configure()
        {
            Settings.Load(GetNELConfiguration());
        }

        public void Record(object message)
        { 
            if (message is Network.P2P.Payloads.Block block)
            {
                if (string.IsNullOrEmpty(Settings.Default.Coll_Block))
                    return;
                if (string.IsNullOrEmpty(Settings.Default.Coll_Tx))
                    return;

                //存入tx
                foreach (Network.P2P.Payloads.Transaction tx in block.Transactions)
                {
                    var jo_tx = tx.ToJson();
                    jo_tx["blockindex"] = block.Index;
                    jo_tx["txid"] = jo_tx["hash"];
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Tx, BsonDocument.Parse(jo_tx.ToString()));

                    //存入address_tx
                    for (var i = 0; i < tx.Cosigners.Length; i++)
                    {
                        var addr = tx.Cosigners[i].Account.ToAddress().ToString();
                        var blocktime = block.Timestamp;
                        var addr_tx = new JObject();
                        addr_tx["addr"] = addr;
                        addr_tx["txid"] = jo_tx["hash"];
                        addr_tx["blockindex"] = block.Index;
                        addr_tx["blocktime"] = blocktime;
                        MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr_Tx, BsonDocument.Parse(addr_tx.ToString()));

                        //记录所有的address数量
                        var addr_json = new JObject();
                        addr_json["addr"] = addr;
                        MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr,BsonDocument.Parse(addr_json.ToString()));
                    }
                }

                //存入block
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Block, BsonDocument.Parse(block.ToJson().ToString()));
                //更新systemcounter
                var json = new JObject();
                json["counter"] = "block";
                string whereFliter = json.ToString();
                json["lastBlockindex"] = block.Index;
                string replaceFliter = json.ToString();
                MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_SystemCounter, whereFliter, BsonDocument.Parse(replaceFliter));
            }
            else if (message is Blockchain.ApplicationExecuted appExec)
            {
                if (appExec.Transaction == null)
                    return;
                JObject json = new JObject();
                json["blockIndex"] = appExec.BlockIndex;
                json["txid"] = appExec.Transaction.Hash.ToString();
                json["trigger"] = appExec.Trigger;
                json["vmstate"] = appExec.VMState;
                json["gas_consumed"] = appExec.GasConsumed.ToString();
                try
                {
                    json["stack"] = appExec.Stack.Select(q => q.ToParameter().ToJson()).ToArray();
                }
                catch (InvalidOperationException)
                {
                    json["stack"] = "error: recursive reference";
                }
                json["notifications"] = appExec.Notifications.Select((q,n) =>
                {
                    JObject notification = new JObject();
                    notification["contract"] = q.ScriptHash.ToString();
                    try
                    {
                        notification["state"] = q.State.ToParameter().ToJson();
                        var array_value = (JArray)notification["state"]["value"];
                        if (array_value[0]["value"].AsString() == "5472616e73666572")
                        {
                            var bytes_from = array_value[1]["value"].AsString();
                            var _from = bytes_from == "" ? "" : (new UInt160(Helper.HexToBytes(bytes_from))).ToAddress().ToString();
                            var bytes_to = array_value[2]["value"].AsString();
                            var _to = bytes_to == "" ? "" : (new UInt160(Helper.HexToBytes(bytes_to))).ToAddress().ToString();
                            var _value = array_value[3]["value"].AsString();
                            JObject transfer = new JObject();
                            transfer["blockindex"] = appExec.BlockIndex;
                            transfer["n"] = n;
                            transfer["txid"] = appExec.Transaction?.Hash.ToString();
                            transfer["asset"] = q.ScriptHash.ToString();
                            transfer["from"] = _from;
                            transfer["to"] = _to;
                            transfer["value"] = _value;
                            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_NEP5transfer, BsonDocument.Parse(transfer.ToString()));
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        notification["state"] = "error: recursive reference";
                    }
                    return notification;
                }).ToArray();
                //增加applicationLog输入到数据库
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Application, BsonDocument.Parse(json.ToString()));
            }
            else if (message is Blockchain.Nep5State nep5State)
            {
                if (string.IsNullOrEmpty(Settings.Default.Coll_Nep5State))
                    return;
                //获取这个资产的精度
                var data = new { Address = nep5State.Address.ToAddress(), AssetHash = nep5State.AssetHash.ToString(), LastUpdatedBlock = nep5State.LastUpdatedBlock, Balance = nep5State.Balance.ToString() };
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, data);
            }
        }
    }
}
