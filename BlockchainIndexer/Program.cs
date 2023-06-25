using BlockchainIndexer.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Transactions;

namespace BlockchainIndexer
{
    internal class Program
    {
        static ManualResetEvent mre = new ManualResetEvent(false);
        static HttpClient client = new HttpClient();

        static void Main(string[] args)
        {
            Console.WriteLine("Application Start.");
            Console.WriteLine("Getting Config...");
            GetConfig();
            Console.WriteLine("Config info:");
            Console.WriteLine($"\t Connection String: {Global.ConnectionString}");
            Console.WriteLine("Checking Database...");
            DBCheck();
            Console.WriteLine("Database check completed.");
            ReadLineLoop();

            Console.WriteLine("Ended");
            Console.ReadLine();
        }

        private static async void APICall(int blockNumber, int amountOfBlockNumber)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            int initialBlockNumber = blockNumber;
            for (int number = 0; number < amountOfBlockNumber; number++)
            {
                blockNumber = initialBlockNumber + number;

                Global.WriteToLogAndConsole($"Processing {blockNumber}...");
                sw.Reset();
                sw.Start();
                Block blockAndTransaction = new Block();

                JObject jsonBody = new JObject(
                    new JProperty("jsonrpc", "2.0"),
                    new JProperty("id", 0),
                    new JProperty("method", "eth_getBlockByNumber"),
                    new JProperty("params", new JArray($"0x{blockNumber.ToString("X")}", true)));

                HttpContent content = new StringContent(jsonBody.ToString());

                HttpResponseMessage response = await client.PostAsync(Global.MainNetAPI + Global.ApiKey, content);
                if (response.IsSuccessStatusCode)
                {
                    // Process the response
                    string responseContent = await response.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(responseContent);
                    if (json["result"].Type == JTokenType.Null)
                    {
                        Global.WriteToLogAndConsole($"No result for {blockNumber}");
                    }
                    else
                    {
                        JToken result = json["result"];
                        blockAndTransaction.BlockNumber = Convert.ToInt32(result["number"].ToString(), 16);
                        blockAndTransaction.Hash = result["hash"].ToString();
                        blockAndTransaction.ParentHash = result["parentHash"].ToString();
                        blockAndTransaction.Miner = result["miner"].ToString();
                        // block reward
                        blockAndTransaction.GasLimit = Convert.ToInt64(result["gasLimit"].ToString(), 16);
                        blockAndTransaction.GasUsed = Convert.ToInt64(result["gasUsed"].ToString(), 16);


                        // Get BlockTransactionCountByNumber
                        JObject getBlockTransactionCount = new JObject(
                            new JProperty("jsonrpc", "2.0"),
                            new JProperty("id", 0),
                            new JProperty("method", "eth_getBlockTransactionCountByNumber"),
                            new JProperty("params", new JArray(result["number"])));

                        HttpContent getBlockTransactionCountContent = new StringContent(getBlockTransactionCount.ToString());

                        HttpResponseMessage responseBlockTransactionCount = await client.PostAsync(Global.MainNetAPI + Global.ApiKey, getBlockTransactionCountContent);
                        if (responseBlockTransactionCount.IsSuccessStatusCode)
                        {
                            string blockTransactionCountResponseContent = await responseBlockTransactionCount.Content.ReadAsStringAsync();
                            JObject blockTransactionResponseContentJSON = JObject.Parse(blockTransactionCountResponseContent);
                            blockAndTransaction.Transaction = new BlockTransaction[Convert.ToInt32(blockTransactionResponseContentJSON["result"].ToString(), 16)];
                        }


                        // Get TransactionByBlockNumberAndIndex
                        for (int i = 0; i < blockAndTransaction.Transaction.Length; i++)
                        {
                            JObject getBlockTransaction = new JObject(
                                new JProperty("jsonrpc", "2.0"),
                                new JProperty("id", 0),
                                new JProperty("method", "eth_getTransactionByBlockNumberAndIndex"),
                                new JProperty("params", new JArray(result["number"], $"0x{i.ToString("X")}")));

                            HttpContent getBlockTransactionContent = new StringContent(getBlockTransaction.ToString());

                            HttpResponseMessage responseBlockTransaction = await client.PostAsync(Global.MainNetAPI + Global.ApiKey, getBlockTransactionContent);
                            if (responseBlockTransaction.IsSuccessStatusCode)
                            {
                                string blockTransactionResponseContent = await responseBlockTransaction.Content.ReadAsStringAsync();
                                JObject blockTransactionResponseContentJSON = JObject.Parse(blockTransactionResponseContent);
                                if (blockTransactionResponseContentJSON["result"].Type == JTokenType.Null)
                                {
                                    Console.WriteLine($"Block {result["number"]}, Transaction of index: {i} is null");
                                }
                                else
                                {
                                    JToken transactionResult = blockTransactionResponseContentJSON["result"];
                                    blockAndTransaction.Transaction[i] = new BlockTransaction();
                                    blockAndTransaction.Transaction[i].Hash = transactionResult["hash"].ToString();
                                    blockAndTransaction.Transaction[i].From = transactionResult["from"].ToString();
                                    blockAndTransaction.Transaction[i].To = transactionResult["to"].ToString();
                                    blockAndTransaction.Transaction[i].Value = (decimal)BigInteger.Parse(transactionResult["value"].ToString().Substring(2), System.Globalization.NumberStyles.HexNumber);
                                    blockAndTransaction.Transaction[i].Gas = Convert.ToInt64(transactionResult["gas"].ToString(), 16);
                                    blockAndTransaction.Transaction[i].GasPrice = Convert.ToInt64(transactionResult["gasPrice"].ToString(), 16);
                                    blockAndTransaction.Transaction[i].TransactionIndex = Convert.ToInt32(transactionResult["transactionIndex"].ToString(), 16);
                                }
                            }

                        }

                        using (TransactionScope scope = new TransactionScope())
                        {
                            using (BlockchainIndexerDBDataContext db = new BlockchainIndexerDBDataContext(Global.ConnectionString))
                            {
                                if ((from b in db.blocks where b.block_number == blockAndTransaction.BlockNumber select b).Any())
                                {
                                    block itemInDB = (from b in db.blocks where b.block_number == blockAndTransaction.BlockNumber select b).SingleOrDefault();
                                    itemInDB.hash = blockAndTransaction.Hash;
                                    itemInDB.parent_hash = blockAndTransaction.ParentHash;
                                    itemInDB.miner = blockAndTransaction.Miner;
                                    itemInDB.gas_limit = blockAndTransaction.GasLimit;
                                    itemInDB.gas_used = blockAndTransaction.GasUsed;

                                    foreach (BlockTransaction bt in blockAndTransaction.Transaction)
                                    {
                                        if (!(from t in db.transactions where t.hash == bt.Hash select t).Any())
                                        {
                                            db.transactions.InsertOnSubmit(new transaction
                                            {
                                                block_id = itemInDB.id,
                                                hash = bt.Hash,
                                                from = bt.From,
                                                to = bt.To,
                                                value = bt.Value,
                                                gas = bt.Gas,
                                                gas_price = bt.GasPrice,
                                                transaction_index = bt.TransactionIndex,
                                            });
                                        }
                                    }
                                }
                                else
                                {
                                    block newBlock = new block()
                                    {
                                        block_number = blockAndTransaction.BlockNumber,
                                        hash = blockAndTransaction.Hash,
                                        parent_hash = blockAndTransaction.ParentHash,
                                        miner = blockAndTransaction.Miner,
                                        block_reward = null,
                                        gas_limit = blockAndTransaction.GasLimit,
                                        gas_used = blockAndTransaction.GasUsed
                                    };
                                    db.blocks.InsertOnSubmit(newBlock);
                                    db.SubmitChanges();


                                    foreach (var item in blockAndTransaction.Transaction)
                                    {
                                        db.transactions.InsertOnSubmit(new transaction
                                        {
                                            block_id = newBlock.id,
                                            hash = item.Hash,
                                            from = item.From,
                                            to = item.To,
                                            value = item.Value,
                                            gas = item.Gas,
                                            gas_price = item.GasPrice,
                                            transaction_index = item.TransactionIndex,
                                        });
                                    }
                                }
                                db.SubmitChanges();
                            }
                            scope.Complete();
                        }
                    }
                }
                else
                {
                    Global.WriteToLogAndConsole($"Request failed with status code: {response.StatusCode}");
                }
                sw.Stop();
                Global.WriteToLogAndConsole($"{blockNumber} process completed.({sw.ElapsedMilliseconds}ms)");
            }

            // Release thread.
            mre.Set();
        }

        private static void ReadLineLoop()
        {
            Console.Write("Please enter the starting block number: ");
            string userInput = Console.ReadLine();
            bool invalidInput = false;

            // Check and accept only number.
            for (int i = 0; i < userInput.Length; i++)
            {
                if (!Regex.IsMatch(userInput[i].ToString(), @"[0-9]", RegexOptions.IgnoreCase))
                {
                    invalidInput = true;
                }
            }

            if (invalidInput)
            {
                Console.WriteLine($"Invalid block number. ({userInput})");
            }
            else
            {
                Console.Write($"Please enter the amount of block number: ");
                string userAmountOfBlockNumber = Console.ReadLine();
                bool invalidAmountOfBlockNumber = false;
                
                // Check and accept only number.
                for (int i = 0; i < userAmountOfBlockNumber.Length; i++)
                {
                    if (!Regex.IsMatch(userAmountOfBlockNumber[i].ToString(), @"[0-9]", RegexOptions.IgnoreCase))
                    {
                        invalidAmountOfBlockNumber = true;
                    }
                }

                if (invalidAmountOfBlockNumber)
                {
                    Console.WriteLine($"Invalid amount of block number. ({userAmountOfBlockNumber})");

                }
                else
                {
                    
                    APICall(int.Parse(userInput), int.Parse(userAmountOfBlockNumber));

                    // Wait for the APICall method to complete.
                    mre.WaitOne();

                    // Reset the thread and proceed.
                    mre.Reset();
                }
            }

            
            ConsoleKey response;
            do
            {
                Console.Write("Would you like to continue? [y/n]");
                response = Console.ReadKey(false).Key;
                if (response != ConsoleKey.Enter)
                {
                    Console.WriteLine();
                }
            } while (response != ConsoleKey.Y && response != ConsoleKey.N);

            if (response == ConsoleKey.Y)
            {
                ReadLineLoop();
            }
        }

        private static void GetConfig()
        {
            // Get info from App.Config.
            // TODO: ApiKey should be encrypted.
            Global.ConnectionString = ConfigurationManager.AppSettings["DB"].ToString();
            Global.MainNetAPI = ConfigurationManager.AppSettings["MainNetApi"].ToString();
            Global.ApiKey = ConfigurationManager.AppSettings["ApiKey"].ToString();

            // Check log file
            // TODO: periodically remove old log file.
            Global.LogFilePath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + @"\Log.txt";

        }

        private static void DBCheck()
        {
            try
            {
                using (BlockchainIndexerDBDataContext db = new BlockchainIndexerDBDataContext(Global.ConnectionString))
                {
                    if (!db.DatabaseExists())
                    {
                        Console.WriteLine("Creating database...");
                        db.CreateDatabase();
                        Console.WriteLine("Database created.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
