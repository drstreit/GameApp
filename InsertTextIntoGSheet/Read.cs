using ALogger;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace InsertTextIntoGSheet
{
    class Read
    {
        static void Main()
        {

        }

        private static void ReadText()
        {
            var path = ConfigurationManager.AppSettings["StoragePath"];
            var source = new CancellationTokenSource();

            var log = new Logger(source);
            log.AddMessage(new LogMessage(Levels.Log, "Press 'ESC' to stop program"));

            while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
            {
                foreach (string file in Directory.GetFiles(path, "*.bmp"))
                {
                    FileInfo fileInfo = new(file);
                    // min 5 sec old to avoid conflicts
                    if (new TimeSpan(DateTime.Now.Ticks - fileInfo.CreationTime.Ticks).TotalSeconds > 5)
                        Processing(fileInfo.FullName, source, log);
                }
                // full round - wait 5 sec for next 
                Thread.Sleep(5000);
            }

            // clean up
            source.Cancel();
            log.Flush();
        }

        private static void Processing(string fullName, CancellationTokenSource source, Logger log)
        {
            throw new NotImplementedException();
        }
    }
}
