using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BlockchainIndexer
{
    public static class Global
    {
        public static string ConnectionString { get; set; }
        public static string MainNetAPI { get; set; }
        public static string ApiKey { get; set; }
        public static string LogFilePath { get; set; }
        
        public static void WriteToLogAndConsole(string msg, bool isError = false)
        {
            try
            {
                string message = $"[{DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss tt")}] {(isError ? "[Error]" : "[Info]")} {msg} {Environment.NewLine}";
                Console.Write(message);
                File.AppendAllText(LogFilePath, message);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
