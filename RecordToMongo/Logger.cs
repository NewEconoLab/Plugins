using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.VM;
using System;
using System.Linq;
using NEL.Simple.SDK.Helper;
using MongoDB.Bson;
using Neo.Wallets;

namespace Neo.Plugins
{
    public class Logger : Plugin,IRecordPlugin
    {
        public override void Configure()
        {
            Settings.Load(GetNELConfiguration());
        }

        void IRecordPlugin.Record(object message)
        {
            if (message is IO.Caching.WriteBatchTask wt)
            {
                if (wt.enumDataTpye == IO.Caching.EnumDataTpye.native && !string.IsNullOrEmpty(Settings.Default.Coll_Operation))
                {
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Operation, wt.writeBatchOperation);
                }
                else if (wt.enumDataTpye == IO.Caching.EnumDataTpye.nep5 && !string.IsNullOrEmpty(Settings.Default.Coll_Operation_Nep5))
                {
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Operation_Nep5, wt.writeBatchOperation);
                }
            }
            else if (message is Blockchain.ApplicationExecuted e)
            {
                if (string.IsNullOrEmpty(Settings.Default.Coll_Application))
                    return;
                JObject json = new JObject();
                json["txid"] = e.Transaction.Hash.ToString();
                json["blockindex"] = e.BlockIndex;
                json["executions"] = e.ExecutionResults.Select(p =>
                {
                    JObject execution = new JObject();
                    execution["trigger"] = p.Trigger;
                    execution["contract"] = p.ScriptHash.ToString();
                    execution["vmstate"] = p.VMState;
                    execution["gas_consumed"] = p.GasConsumed.ToString();
                    try
                    {
                        execution["stack"] = p.Stack.Select(q => q.ToParameter().ToJson()).ToArray();
                    }
                    catch (InvalidOperationException)
                    {
                        execution["stack"] = "error: recursive reference";
                    }
                    execution["notifications"] = p.Notifications.Select(q =>
                    {
                        JObject notification = new JObject();
                        notification["contract"] = q.ScriptHash.ToString();
                        try
                        {
                            notification["state"] = q.State.ToParameter().ToJson();
                        }
                        catch (InvalidOperationException)
                        {
                            notification["state"] = "error: recursive reference";
                        }
                        return notification;
                    }).ToArray();
                    return execution;
                }).ToArray();
                //增加applicationLog输入到数据库
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Application, BsonDocument.Parse(json.ToString()));
            }
            else if (message is Blockchain.DumpInfoExecuted d)
            {
                if (string.IsNullOrEmpty(Settings.Default.Coll_DumpInfo))
                    return;
                MyJson.JsonNode_Object data = new MyJson.JsonNode_Object();
                data["txid"] = new MyJson.JsonNode_ValueString(d.Hash.ToString());
                data["dimpInfo"] = new MyJson.JsonNode_ValueString(d.DumpInfoStr);
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_DumpInfo, BsonDocument.Parse(data.ToString()));
            }
            else if (message is Blockchain.PersistCompleted per)
            {
                if (string.IsNullOrEmpty(Settings.Default.Coll_Block))
                    return;
                var block = per.Block;
                //block 存入数据库
                NEL.Simple.SDK.Helper.MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Block, BsonDocument.Parse(block.ToJson().ToString()));
                //更新systemcounter
                var json = new JObject();
                json["counter"] = "block";
                string whereFliter = json.ToString();
                json["lastBlockindex"] = block.Index;
                string replaceFliter = json.ToString();
                NEL.Simple.SDK.Helper.MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_SystemCounter, whereFliter, BsonDocument.Parse(replaceFliter));

                json = new JObject();
                json["counter"] = "notify";
                whereFliter = json.ToString();
                json["lastBlockindex"] = block.Index;
                replaceFliter = json.ToString();
                MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_SystemCounter, whereFliter, BsonDocument.Parse(replaceFliter));
            }
            else if (message is Nep5State n)
            {
                if (string.IsNullOrEmpty(Settings.Default.Coll_Nep5State))
                    return;
                //获取这个资产的精度
                 var data = new { Address = n.Address.ToAddress(), AssetHash = n.AssetHash.ToString(), LastUpdatedBlock = n.LastUpdatedBlock, Balance =n.Balance.ToString() };
                 MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, data);
            }
        }
    }
}
