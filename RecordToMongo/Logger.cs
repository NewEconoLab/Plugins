using Neo.IO;
using Neo.IO.Json;
using Neo.Ledger;
using Neo.VM;
using System;
using System.Linq;
using NEL.Simple.SDK.Helper;
using MongoDB.Bson;
using Neo.Wallets;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using System.Collections.Generic;
using System.Numerics;

namespace Neo.Plugins
{
    public class Logger : Plugin,IPersistencePlugin
    {
        public override string Name => "RecordToMongo";

        public override void Configure()
        {
            Settings.Load(GetConfiguration());
        }

        void IPersistencePlugin.OnPersist(Snapshot snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            //存applicationlog
            foreach (var appExec in applicationExecutedList)
            {
                RecordApplication(snapshot,appExec, snapshot.PersistingBlock.Index);
            }
        }

        void IPersistencePlugin.OnCommit(Snapshot snapshot)
        {
            var block = snapshot.PersistingBlock;

            RecordBlock(block);

            foreach (Network.P2P.Payloads.Transaction tx in block.Transactions)
            {
                //记录交易信息
                RecordTx(tx, block.Index, block.Timestamp);
                //存入address_tx
                for (var i = 0; i < tx.Cosigners.Length; i++)
                {
                    var addr = tx.Cosigners[i].Account.ToAddress().ToString();
                    var blocktime = block.Timestamp;
                    RecordAddressTx(addr, tx.Hash.ToString(), block.Index, block.Timestamp);
                    RecordAddress(addr, tx.Hash.ToString(), block.Index, block.Timestamp);
                }
            }

            UpdateSystemCounter("block", block.Index);
        }

        bool IPersistencePlugin.ShouldThrowExceptionFromCommit(Exception ex)
        {
            throw new NotImplementedException();
        }

        public void RecordBlock(Network.P2P.Payloads.Block block)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Block))
                return;
            //存入block
            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Block, BsonDocument.Parse(block.ToJson().ToString()));
            RecordBlockSysfee(block.Index);
        }

        public void RecordTx(Network.P2P.Payloads.Transaction tx,uint index,ulong timestamp)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Tx))
                return;
            var jo_tx = tx.ToJson();
            jo_tx["blockindex"] = index;
            jo_tx["blocktime"] = timestamp;
            jo_tx["txid"] = jo_tx["hash"];
            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Tx, BsonDocument.Parse(jo_tx.ToString()));
        }

        public void RecordAddressTx(string addr,string txid,uint index,ulong blocktime)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Addr_Tx))
                return;
            var addr_tx = new JObject();
            addr_tx["addr"] = addr;
            addr_tx["txid"] = txid;
            addr_tx["blockindex"] = index;
            addr_tx["blocktime"] = blocktime;
            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr_Tx, BsonDocument.Parse(addr_tx.ToString()));
        }

        public void RecordAddress(string _addr,string _txid,uint _index,ulong _blocktime)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Addr))
                return;
            //先获取这个地址的情况
            var findStr = new JObject();
            findStr["addr"] = _addr;
            var data = MongoDBHelper.Get<Address>(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr,findStr.ToString());
            if (data.Count == 0) //如果是第一次使用那么更新firstuse和lastuse
            {
                var addressInfo = new Address(){
                    addr = _addr,
                    firstuse = new AddrUse(){ txid = _txid,blockindex = _index,blocktime = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Local).AddSeconds((UInt32)(_blocktime / 1000)) },
                    lastuse = new AddrUse(){ txid = _txid, blockindex = _index, blocktime = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Local).AddSeconds((UInt32)(_blocktime/1000)) },
                    txcount = 1
                };
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr, addressInfo);
            }
            else //不是第一次使用就更新lastuse 并且txcount++
            {
                var addressInfo = data[0];
                data[0].lastuse = new AddrUse() { txid = _txid, blockindex = _index, blocktime = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Local).AddSeconds((UInt32)(_blocktime / 1000)) };
                data[0].txcount++;
                MongoDBHelper.ReplaceData<Address>(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr, findStr.ToString(), addressInfo);
            }
        }

        public void UpdateSystemCounter(string counterName,uint index)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_SystemCounter))
                return;
            //更新systemcounter
            var json = new JObject();
            json["counter"] = counterName;
            string whereFliter = json.ToString();
            json["lastBlockindex"] = index;
            string replaceFliter = json.ToString();
            MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_SystemCounter, whereFliter, BsonDocument.Parse(replaceFliter));
        }

        public void RecordApplication(Snapshot snapshot,Blockchain.ApplicationExecuted appExec,uint _blockIndex)
        {
            if (appExec.Transaction == null)
                return;
            JObject json = new JObject();
            json["blockIndex"] = _blockIndex;
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
            json["notifications"] = appExec.Notifications.Select((q, n) =>
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
                        var uint160_from = bytes_from == "" ? null : (new UInt160(Helper.HexToBytes(bytes_from)));
                        var bytes_to = array_value[2]["value"].AsString();
                        var uint160_to = bytes_to == "" ? null : (new UInt160(Helper.HexToBytes(bytes_to)));
                        var _value = array_value[3]["value"].AsString();
                        JObject transfer = new JObject();
                        transfer["blockindex"] = _blockIndex;
                        transfer["n"] = n;
                        transfer["txid"] = appExec.Transaction?.Hash.ToString();
                        transfer["asset"] = q.ScriptHash.ToString();
                        transfer["from"] = uint160_from == null ? "" : uint160_from.ToAddress().ToString();
                        transfer["to"] = uint160_to == null ? "" : uint160_to.ToAddress().ToString();
                        transfer["value"] = _value;
                        MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5Transfer, BsonDocument.Parse(transfer.ToString()));
                        RecordAssetInfo(snapshot,q.ScriptHash);
                        RecordNep5StateRecordNep5State(snapshot, q.ScriptHash, _blockIndex, uint160_from, uint160_to, BigInteger.Parse(_value));
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

        public void RecordNep5StateRecordNep5State(Snapshot snapshot,UInt160 _assetHash,uint _updatedBlock, UInt160 _from, UInt160 _to, BigInteger amount)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Nep5State))
                return;
            BigInteger balance_to = 0;
            BigInteger balance_from = 0;
            if (_from != null)
            {
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    sb.EmitAppCall(_assetHash, "balanceOf", _from);
                    using (ApplicationEngine engine = ApplicationEngine.Run(sb.ToArray(), snapshot, testMode: true))
                    {
                        if (engine.State.HasFlag(VMState.FAULT))
                            throw new InvalidOperationException($"Execution for {_assetHash.ToString()}.balanceOf('{_from.ToString()}' fault");
                        balance_from = engine.ResultStack.Pop().GetBigInteger();
                    }
                }
                var data = new { Address = _from.ToAddress().ToString(), AssetHash = _assetHash.ToString(), LastUpdatedBlock = _updatedBlock, Balance = balance_from.ToString() };
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, data);
            }
            if (_to != null)
            {
                using (ScriptBuilder sb = new ScriptBuilder())
                {
                    sb.EmitAppCall(_assetHash, "balanceOf", _to);
                    using (ApplicationEngine engine = ApplicationEngine.Run(sb.ToArray(), snapshot, testMode: true))
                    {
                        if (engine.State.HasFlag(VMState.FAULT))
                            throw new InvalidOperationException($"Execution for {_assetHash.ToString()}.balanceOf('{_to.ToString()}' fault");
                        balance_to = engine.ResultStack.Pop().GetBigInteger();
                    }
                }
                var data = new { Address = _to.ToAddress().ToString(), AssetHash = _assetHash.ToString(), LastUpdatedBlock = _updatedBlock, Balance = balance_to.ToString() };
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, data);
            }
        }

        public void RecordBlockSysfee(uint _index)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Block_SysFee))
                return;
            if (_index == 0)
                return;
            using (ApplicationEngine engine = NativeContract.GAS.TestCall("getSysFeeAmount", _index))
            {
                var fee = engine.ResultStack.Peek().GetBigInteger().ToString();
                if (string.IsNullOrEmpty(Settings.Default.Coll_Block_SysFee))
                    return;
                var data = new { index = _index,totalSysfee = fee};
                MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Block_SysFee, data);
            }
        }

        public void RecordAssetInfo(Snapshot snapshot, UInt160 assetHash)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Nep5Asset))
                return;
            //先检查这个资产有没有存过
            var findStr = new JObject();
            findStr["assetid"] = assetHash.ToString();
            var ja = MongoDBHelper.Get(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5Asset, findStr.ToString());
            if (ja.Count > 0)
                return;
            BigInteger _totalSupply = 0;
            string _name = "";
            string _symbol = "";
            uint _decimals = 0;
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitAppCall(assetHash, "name");
                using (ApplicationEngine engine = ApplicationEngine.Run(sb.ToArray(), snapshot, testMode: true))
                {
                    _name = engine.ResultStack.Pop().GetString();
                }
            }
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitAppCall(assetHash, "symbol");
                using (ApplicationEngine engine = ApplicationEngine.Run(sb.ToArray(), snapshot, testMode: true))
                {
                    _symbol = engine.ResultStack.Pop().GetString();
                }
            }
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitAppCall(assetHash, "totalSupply");
                using (ApplicationEngine engine = ApplicationEngine.Run(sb.ToArray(), snapshot, testMode: true))
                {
                    _totalSupply = engine.ResultStack.Pop().GetBigInteger();
                }
            }
            using (ScriptBuilder sb = new ScriptBuilder())
            {
                sb.EmitAppCall(assetHash, "decimals");
                using (ApplicationEngine engine = ApplicationEngine.Run(sb.ToArray(), snapshot, testMode: true))
                {
                    _decimals = (uint)engine.ResultStack.Pop().GetBigInteger();
                }
            }
            var data = new { assetid = assetHash.ToString(), totalsupply = _totalSupply.ToString(), name = _name, symbol = _symbol, decimals = _decimals };
            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5Asset, data);
        }
    }

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
    }

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
