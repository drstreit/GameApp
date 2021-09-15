using ALogger;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace InsertTextIntoGSheet
{
    class Program
    {
        static void Main()
        {
            var kvpQueue = new ConcurrentQueue<KeyValuePair<string, double>>();
            var path = ConfigurationManager.AppSettings["StoragePath"];
            CancellationTokenSource source = new();
            CancellationToken token = source.Token;
            ParallelOptions parallelOptions = new();
            parallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount;
            parallelOptions.CancellationToken = token;

            // initialize logger
            var log = new Logger(source);
            log.AddMessage(new LogMessage(Levels.Log, "Press 'ESC' to stop program"));

            // check GSheet credentials and availability
            GoogleSheet _googleSheet = new(log);
            if (!_googleSheet.CheckSheet())
                throw new Exception($"Please create Google sheet '{ConfigurationManager.AppSettings["TradeManSheet"]}' first");
            // Sheet exists - now get all values
            //_googleSheet.GetPrices();

            while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
            {
                Parallel.ForEachAsync(Directory.GetFiles(path, "*.txt"), parallelOptions, async (file, token) =>
                {
                    //File path 
                    FileInfo fileInfo = new(file);
                    // min 5 sec old to avoid conflicts
                    if (new TimeSpan(DateTime.Now.Ticks - fileInfo.CreationTime.Ticks).TotalSeconds > 5)
                        await ReadFiles(fileInfo.FullName, log, kvpQueue);
                });

                // write to GSheet
                if (!kvpQueue.IsEmpty && _googleSheet.AppendValues(kvpQueue))
                {
                    log.AddMessage(new LogMessage(Levels.Log, $"Wrote {kvpQueue.Count} new values to GSheet"));
                    kvpQueue.Clear();
                }
                // full round - wait 5 sec for next
                log.AddMessage(new LogMessage(Levels.Success, $"Waiting 10 sec to look for new text files..."));
                Thread.Sleep(10000);
            }
            // clean up
            source.Cancel();
            log.Flush();
        }

        private async static Task ReadFiles(string filePath, Logger log, ConcurrentQueue<KeyValuePair<string, double>> kvpQueue)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    Stopwatch watch = new();
                    watch.Start();
                    var start = DateTime.Now;
                    // load text file
                    string content = await File.ReadAllTextAsync(filePath);
                    // rename file
                    new FileInfo(filePath).MoveTo(filePath + ".bak");
                    // fill queue
                    var tmpQueue = (ConcurrentQueue<KeyValuePair<string, double>>)JsonSerializer.Deserialize(
                        content,
                        typeof(ConcurrentQueue<KeyValuePair<string, double>>)
                    );
                    while (tmpQueue.TryDequeue(out KeyValuePair<string, double> tmp))
                        kvpQueue.Enqueue(tmp);
                    watch.Stop();
                    if (!tmpQueue.IsEmpty)
                        log.AddMessage(new LogMessage(Levels.Log, $"Added {tmpQueue.Count} items to '{filePath}' in {watch.ElapsedMilliseconds} ms"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }
    }
}
