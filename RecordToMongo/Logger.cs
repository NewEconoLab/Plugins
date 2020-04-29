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
using System.IO;
using System.Text;
using Neo.Cryptography;
using System.Security.Cryptography;
using System.Diagnostics.Contracts;

namespace Neo.Plugins
{
    public class Logger : Plugin, IPersistencePlugin
    {
        public override string Name => "RecordToMongo";

        protected override void Configure()
        {
            Settings.Load(GetConfiguration());
        }

        void IPersistencePlugin.OnPersist(StoreView snapshot, IReadOnlyList<Blockchain.ApplicationExecuted> applicationExecutedList)
        {
            //存applicationlog
            foreach (var appExec in applicationExecutedList)
            {
                RecordApplication(snapshot,appExec, snapshot.PersistingBlock.Index,snapshot.PersistingBlock.Timestamp);
            }
        }

        void IPersistencePlugin.OnCommit(StoreView snapshot)
        {
            try
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
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
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
            var addr_tx = new BsonDocument();
            addr_tx["addr"] = addr;
            addr_tx["txid"] = txid;
            addr_tx["blockindex"] = index;
            addr_tx["blocktime"] = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Local).AddSeconds((UInt64)(blocktime / 1000));
            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr_Tx, addr_tx);
        }

        public void RecordAddress(string _addr,string _txid,uint _index,ulong _blocktime)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_Addr)|| string.IsNullOrEmpty(_addr))
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
                data[0].lastuse = new AddrUse() { txid = _txid, blockindex = _index, blocktime = TimeZoneInfo.ConvertTime(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Local).AddSeconds((UInt32)(_blocktime / 1000)) };
                data[0].txcount= (int)data[0].txcount + 1;
                MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Addr, findStr.ToString(), data[0]);
            }
        }
        public void UpdateSystemCounter(string counterName, uint index)
        {
            if (string.IsNullOrEmpty(Settings.Default.Coll_SystemCounter))
                return;
            //更新systemcounter
            var json = new BsonDocument();
            json["counter"] = counterName;
            string whereFliter = json.ToString();
            json["lastBlockindex"] = index;
            MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_SystemCounter, whereFliter, json);
        }
        public void RecordApplication(StoreView snapshot,Blockchain.ApplicationExecuted appExec,uint _blockIndex,ulong _blockTimestamp)
        {
            if (appExec.Transaction == null)
                return;
            JObject json = new JObject();
            json["blockIndex"] = _blockIndex;
            json["txid"] = appExec.Transaction.Hash.ToString();
            json["trigger"] = appExec.Trigger;
            json["vmstate"] = appExec.VMState;
            if (appExec.DumpInfo != null)
            {
                json["dumpinfo"] = appExec.DumpInfo;
                RecordExecDetail(appExec.Transaction.Sender,appExec.Transaction.Hash.ToString(), _blockIndex, _blockTimestamp, appExec.DumpInfo);
            }
            else
            {
                json["dumpinfo"] = "";
            }
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
                //try
                {
                    notification["state"] = q.State.ToParameter().ToJson();
                    var array_value = (JArray)notification["state"]["value"];
                    if (array_value[0]["value"].AsString() == "VHJhbnNmZXI=") //5472616e73666572  
                    {
                        var info = new AssetInfo();
                        RecordAssetInfo(snapshot, q.ScriptHash, out info);

                        var bytes_from = array_value[1]["value"];
                        var uint160_from = bytes_from == null ? null: (new UInt160(Convert.FromBase64String(bytes_from.AsString().Replace("\u002B","+"))));
                        var bytes_to = array_value[2]["value"];
                        var uint160_to = bytes_to == null ? null : (new UInt160(Convert.FromBase64String(bytes_to.AsString().Replace("\u002B", "+"))));
                        var _value = array_value[3]["value"].AsString();
                        JObject transfer = new JObject();
                        transfer["blockindex"] = _blockIndex;
                        transfer["n"] = n;
                        transfer["txid"] = appExec.Transaction?.Hash.ToString();
                        transfer["asset"] = q.ScriptHash.ToString();
                        transfer["from"] = uint160_from == null ? "" : uint160_from.ToAddress().ToString();
                        transfer["to"] = uint160_to == null ? "" : uint160_to.ToAddress().ToString();
                        transfer["value"] = _value;
                        transfer["decimals"] = info?.decimals;
                        MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5Transfer, BsonDocument.Parse(transfer.ToString()));
                        RecordAddress(uint160_from == null ? "" : uint160_from.ToAddress().ToString(), appExec.Transaction?.Hash.ToString(), _blockIndex, _blockTimestamp);
                        RecordAddress(uint160_to == null ? "" : uint160_to.ToAddress().ToString(), appExec.Transaction?.Hash.ToString(), _blockIndex, _blockTimestamp);
                        RecordNep5State(snapshot, q.ScriptHash, _blockIndex, uint160_from, uint160_to, BigInteger.Parse(_value), info?.decimals.ToString(), info?.symbol.ToString());
                    }
                }
                return notification;
            }).ToArray();
            //增加applicationLog输入到数据库
            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Application, BsonDocument.Parse(json.ToString()));
        }

        public void RecordNep5State(StoreView snapshot,UInt160 _assetHash,uint _updatedBlock, UInt160 _from, UInt160 _to, BigInteger amount ,string decimals,string symbol)
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
                var data = new BsonDocument();
                data["Address"] = _from.ToAddress().ToString();
                data["AssetDecimals"] = decimals;
                data["AssetSymbol"] = symbol;
                data["AssetHash"] = _assetHash.ToString();
                data["LastUpdatedBlock"] = _updatedBlock;
                data["Balance"] = BsonDecimal128.Create(balance_from.ToString());
                //这个高度有可能已经记录过一次了
                var findStr = new JObject();
                findStr["Address"] = _from.ToAddress().ToString();
                findStr["AssetHash"] = _assetHash.ToString();
                var ja = MongoDBHelper.Get(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, findStr.ToString());
                if (ja.Count > 0)
                {
                    MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, findStr.ToString(), data);
                }
                else
                {
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, data);
                }
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
                var data = new BsonDocument();
                data["Address"] = _to.ToAddress().ToString();
                data["AssetDecimals"] = decimals;
                data["AssetSymbol"] = symbol;
                data["AssetHash"] = _assetHash.ToString();
                data["LastUpdatedBlock"] = _updatedBlock;
                data["Balance"] = BsonDecimal128.Create(balance_to.ToString());
                var findStr = new BsonDocument();
                findStr["Address"] = _to.ToAddress().ToString();
                findStr["AssetHash"] = _assetHash.ToString();
                var ja = MongoDBHelper.Get(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, findStr.ToString());
                if (ja.Count > 0)
                {
                    MongoDBHelper.ReplaceData(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, findStr.ToString(), data);
                }
                else
                {
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5State, data);
                }
            }
        }

        public void RecordAssetInfo(StoreView snapshot, UInt160 assetHash ,out AssetInfo info)
        {
            info = null;
            if (string.IsNullOrEmpty(Settings.Default.Coll_Nep5Asset))
                return;
            //先检查这个资产有没有存过
            var findStr = new BsonDocument();
            findStr["assetid"] = assetHash.ToString();
            var ja = MongoDBHelper.Get<AssetInfo>(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5Asset, findStr.ToString());
            if (ja.Count > 0)
            {
                info = ja[0];
                return;
            }
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
            info = new AssetInfo(){ assetid = assetHash.ToString(), totalsupply = _totalSupply.ToString(), name = _name, symbol = _symbol, decimals = _decimals };
            MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Nep5Asset, info);
        }

        public void RecordExecDetail(UInt160 sender,string txid,uint blockIndex, ulong blockTimestamp, string dumpinfo)
        {
            byte[] bts = dumpinfo.HexToBytes();
            using (MemoryStream ms = new MemoryStream(bts))
            {
                var outms = llvm.QuickFile.FromFile(ms);
                var text = Encoding.UTF8.GetString(outms.ToArray());
                var json = JObject.Parse(text);
                if (json["VMState"] == null || json["script"]== null)
                    return;
                List<string> froms = new List<string>();
                froms.Add(sender.ToString());
                uint index = 0;
                uint level = 0;
                execOps(json["script"]["ops"] as JArray, txid, blockIndex, blockTimestamp, froms, ref index, ref level, sender.ToString());
            }
        }

        void execOps(JArray ops, string txid, uint blockIndex, ulong blockTimestamp, List<string> froms, ref uint index, ref uint level, string sender)
        {
            for (var n = 0; n < ops.Count; n++)
            {
                var op = ops[n];
                if (op["op"].AsString() == "SYSCALL"
                    && op["param"] != null 
                    && (InteropService.Contract.Call.Hash == BitConverter.ToUInt32(op["param"].AsString().HexToBytes()) || InteropService.Contract.CallEx.Hash == BitConverter.ToUInt32(op["param"].AsString().HexToBytes())))
                {
                    if (op["subscript"] != null)
                    {
                        var to = op["subscript"]["hash"].AsString();
                        var l = (int)level > froms.Count ? froms.Count - 1 : (int)level;
                        InvokeInfo info = new InvokeInfo() { from = froms[l], txid = txid, to = to, type = InvokeType.Call, index = index, level = level, blockIndex = blockIndex, blockTimestamp = blockTimestamp };
                        MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Contract_Exec_Detail, info);
                        index++;
                        level++;
                        froms.Add(to);
                        execOps(op["subscript"]["ops"] as JArray, txid, blockIndex, blockTimestamp, froms, ref index, ref level, sender);
                    }
                    else
                    {
                        index++;
                        level++;
                    }
                }
                else if (op["op"].AsString() == "CALL" || op["op"].AsString() == "CALL_L" || op["op"].AsString() == "CALLA")
                {
                    froms.Add(froms[(int)level]);
                    level++;
                }
                else if (op["op"].AsString() == "RET")
                {
                    if (level == 0)
                        return;
                    froms.RemoveAt((int)level);
                    level--;
                }
                else if (op["op"].AsString() == "SYSCALL"
                    && op["param"] != null
                    && InteropService.Contract.Create.Hash == BitConverter.ToUInt32(op["param"].AsString().HexToBytes()))
                {
                    UInt160 scriptHash = UInt160.Zero;
                    try
                    {
                        string data = JObject.Parse(ops[n - 1]["result"].AsString())["ByteString"].AsString();
                        var bytes_data = data.HexToBytes();
                        scriptHash = new UInt160(bytes_data.Sha256().RIPEMD160());
                    }
                    catch
                    {
                        
                    }
                    var l = (int)level > froms.Count ? froms.Count - 1 : (int)level;
                    InvokeInfo info = new InvokeInfo() { from = froms[l], txid = txid, to = scriptHash.ToString(), type = InvokeType.Create, index = index, level = level, blockIndex = blockIndex, blockTimestamp = blockTimestamp };
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Contract_Exec_Detail, info);
                    index++;
                }
                else if (op["op"].AsString() == "SYSCALL"
                    && op["param"] != null
                    && InteropService.Contract.Update.Hash == BitConverter.ToUInt32(op["param"].AsString().HexToBytes()))
                {
                    var data = JObject.Parse(ops[n - 1]["result"].AsString())["ByteString"].AsString();
                    var bytes_data = data.HexToBytes();
                    var l = (int)level > froms.Count ? froms.Count - 1 : (int)level;
                    UInt160 scriptHash =new UInt160(bytes_data.Sha256().RIPEMD160());
                    InvokeInfo info = new InvokeInfo() { from = froms[l], txid = txid, to = scriptHash.ToString(), type = InvokeType.Update, index = index, level = level, blockIndex = blockIndex, blockTimestamp = blockTimestamp };
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Contract_Exec_Detail, info);
                    index++;
                }
                else if (op["op"].AsString() == "SYSCALL"
                    && op["param"] != null
                    && InteropService.Contract.Destroy.Hash == BitConverter.ToUInt32(op["param"].AsString().HexToBytes()))
                {
                    var l = (int)level > froms.Count ? froms.Count - 1 : (int)level;
                    InvokeInfo info = new InvokeInfo() { from = froms[l], txid = txid, to = "", type = InvokeType.Destroy, index = index, level = level, blockIndex = blockIndex, blockTimestamp = blockTimestamp };
                    MongoDBHelper.InsertOne(Settings.Default.Conn, Settings.Default.DataBase, Settings.Default.Coll_Contract_Exec_Detail, info);
                    index++;
                }
            }
        }

    }
}
