using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlockchainIndexer.Models
{
    public class Block
    {
        //block
        //save blocknumber = number, hex to int
        // save hash = hash, varchar 66
        //parent hash = xxx, varchar 66
        //mine = miner, varchar 42
        // block reward = xxxx, decimal
        // gas limit = xxxx, decimal
        //gas used = xxx, decimal

        //transaction
        // hash = hash,
        // from = xxxx, varchar 42
        // to = xxx, varchar 42
        // value = xxxx, decimal
        // gas = xxxx, decimal
        // gas price = xxxx, decimal
        // transaction index = xxxx, int

        public int BlockNumber { get; set; }
        public string Hash { get; set; }
        public string ParentHash { get; set; }
        public string Miner { get; set; }
        public decimal GasLimit { get; set; }
        public decimal GasUsed { get; set; }

        // block reward
        public BlockTransaction[] Transaction { get; set; }
    }
}
