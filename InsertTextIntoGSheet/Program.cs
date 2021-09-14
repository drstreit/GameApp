using ALogger;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InsertTextIntoGSheet
{
    class Program
    {
        static void Main()
        {
            var stringQueue = new ConcurrentQueue<string>();
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
            _googleSheet.GetPrices();

            bool action(object obj)
            {
                //File path 
                FileInfo fileInfo = new((string)obj);
                bool result = false;
                // min 5 sec old to avoid conflicts
                if (new TimeSpan(DateTime.Now.Ticks - fileInfo.CreationTime.Ticks).TotalSeconds > 5)
                    result = ReadFiles(fileInfo.FullName, source, log, stringQueue);

                return result;
            }

            var tasks = new List<Task<bool>>();

            while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
            {
                foreach (string file in Directory.GetFiles(path, "*.txt"))
                {
                    tasks.Add(Task<bool>.Factory.StartNew(action, file));
                }
                // wait for finish
                try
                {
                    // Wait for all the tasks to finish.
                    Task.WaitAll(tasks.ToArray());
                }
                catch (AggregateException e)
                {
                    for (int j = 0; j < e.InnerExceptions.Count; j++)
                    {
                        log.AddMessage(new LogMessage(Levels.Error, e.InnerExceptions[j].ToString()));
                    }
                }
                // extract text
                if (stringQueue.IsEmpty)
                    continue;
                ExtractTextToKVP(stringQueue, kvpQueue);
                log.AddMessage(new LogMessage(Levels.Log, $"Extracted {kvpQueue.Count} trade items with prices from files"));
                stringQueue.Clear();
                //var x = (from c in kvpQueue orderby c.Key ascending select c).ToList();
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


        private static void ExtractTextToKVP(ConcurrentQueue<string> stringQueue, ConcurrentQueue<KeyValuePair<string, double>> kvpQueue)
        {
            if (!stringQueue.IsEmpty)
            {
                foreach (var item in stringQueue)
                {
                    // split on last space
                    var indexOfLastEmpty = item.Trim().LastIndexOf(" ");
                    if (indexOfLastEmpty == -1)
                        continue;
                    // split to last space => trade item
                    string tradeItem = item.Substring(0, indexOfLastEmpty).Trim();
                    if (string.IsNullOrWhiteSpace(tradeItem))
                        continue;
                    // split from last to end => number
                    var priceText = item[indexOfLastEmpty..].Trim();
                    if (!double.TryParse(priceText, out double price))
                        continue;
                    // add kvp to queue
                    kvpQueue.Enqueue(new KeyValuePair<string, double>(tradeItem, price));
                }
            }
        }

        private static bool ReadFiles(string filePath, CancellationTokenSource source, Logger log, ConcurrentQueue<string> stringQueue)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var start = DateTime.Now;
                    // load text file
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    using var sr = new StreamReader(fs, Encoding.UTF8);
                    string content = sr.ReadToEnd();
                    foreach (string item in content.Split(Environment.NewLine,
                            StringSplitOptions.RemoveEmptyEntries))
                    {
                        stringQueue.Enqueue(item);
                    }
                    sr.Close();
                    sr.Dispose();
                    fs.Close();
                    fs.Dispose();
                    // rename file
                    new FileInfo(filePath).MoveTo(filePath + ".bak");
                    // report
                    var elapsed = DateTime.Now.Ticks - start.Ticks;
                    log.AddMessage(new LogMessage(Levels.Log, $"Processed '{filePath}' in {new TimeSpan(DateTime.Now.Ticks - start.Ticks).TotalSeconds} sec"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            return true;
        }
    }
}
