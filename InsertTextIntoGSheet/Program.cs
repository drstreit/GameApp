using ALogger;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
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
            var source = new CancellationTokenSource();
            // initialize logger
            var log = new Logger(source);
            log.AddMessage(new LogMessage(Levels.Log, "Press 'ESC' to stop program"));

            // check GSheet credentials and availability
            GoogleSheet _googleSheet = new(log);
            if (!_googleSheet.CheckSheet())
                throw new Exception($"Please create Google sheet '{ConfigurationManager.AppSettings["TradeManSheet"]}' first");
            // Sheet exists - now get all values
            //_googleSheet.GetPrices();

            bool action(object obj)
            {
                //File path 
                FileInfo fileInfo = new((string)obj);
                bool result = false;
                // min 5 sec old to avoid conflicts
                if (new TimeSpan(DateTime.Now.Ticks - fileInfo.CreationTime.Ticks).TotalSeconds > 5)
                    ReadFiles(fileInfo.FullName, log, kvpQueue);

                return result;
            }

            var tasks = new List<Task<bool>>();

            while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
            {
                foreach (string file in Directory.GetFiles(path, "*.txt"))
                    tasks.Add(Task<bool>.Factory.StartNew(action, file));
                // wait for finish
                try
                {
                    // Wait for all the tasks to finish.
                    Task.WaitAll(tasks.ToArray());
                }
                catch (AggregateException e)
                {
                    for (int j = 0; j < e.InnerExceptions.Count; j++)
                        log.AddMessage(new LogMessage(Levels.Error, e.InnerExceptions[j].ToString()));
                }
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

            private async static void ReadFiles(string filePath, Logger log, ConcurrentQueue<KeyValuePair<string, double>> kvpQueue)
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
                    if (tmpQueue.Count != 0)
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
