using ALogger;
using ImageMagick;
using IronOcr;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TextExtract
{
    class Program
    {
        static void Main()
        {
            var stringQueue = new ConcurrentQueue<string>();
            var path = ConfigurationManager.AppSettings["StoragePath"];
            var source = new CancellationTokenSource();

            var log = new Logger(source);
            log.AddMessage(new LogMessage(Levels.Log, "Press 'ESC' to stop program"));

            bool action(object obj)
            {
                //File path 
                FileInfo fileInfo = new((string)obj);
                bool result = false;
                // min 5 sec old to avoid conflicts
                if (new TimeSpan(DateTime.Now.Ticks - fileInfo.CreationTime.Ticks).TotalSeconds > 5)
                    result = Processing(fileInfo.FullName, source, log, stringQueue);

                return result;
            }

            var tasks = new List<Task<bool>>();

            while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
            { 
                foreach (string file in Directory.GetFiles(path, "*.bmp"))
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
                // save text
                if (!stringQueue.IsEmpty)
                {
                    var sb = new StringBuilder();
                    while (stringQueue.TryDequeue(out string s)) 
                        if (!string.IsNullOrEmpty(s)) _ = sb.AppendLine(s);
                    if (sb.Length > 0)
                        File.WriteAllTextAsync($"{path}{DateTime.Now.Ticks}.txt", sb.ToString());
                }
                // full round - wait 5 sec for next
                log.AddMessage(new LogMessage(Levels.Success, $"Waiting 5 sec to look for new BMP files..."));
                Thread.Sleep(5000);
            }
            // clean up
            source.Cancel();
            log.Flush();
        }

        private static bool Processing(string filePath, CancellationTokenSource source, Logger log, ConcurrentQueue<string> stringQueue)
        {
            var Ocr = new IronTesseract();
            Ocr.Configuration.WhiteListCharacters = ".1234567890abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            Ocr.Configuration.PageSegmentationMode = TesseractPageSegmentationMode.SingleColumn;
            Ocr.Configuration.TesseractVersion = TesseractVersion.Tesseract5;
            Ocr.Configuration.EngineMode = TesseractEngineMode.TesseractOnly;
            Ocr.Language = OcrLanguage.English;
            
            var start = DateTime.Now;
            try
            {
                if (File.Exists(filePath))
                {
                    Bitmap b = new(filePath);

                    #region Process Image
                    Percentage brightness = new(30);
                    Percentage contrast = new(100);

                    Console.WriteLine($"Processing '{filePath}' - adjusting brightness and contrast...");
                    using MagickImage image = new(new MagickFactory().Image.Create(b));
                    // Dispose asap to allow file renaming
                    b.Dispose();
                    // rename file to remove it from round
                    new FileInfo(filePath).MoveTo(filePath + ".bak");
                    // time intensive tasks
                    image.BrightnessContrast(brightness, contrast);
                    image.ColorSpace = ColorSpace.Gray;
                    using var Input = new OcrInput(image.ToBitmap());
                    var Result = Ocr.Read(Input);
                    // result saving
                    if (!string.IsNullOrWhiteSpace(Result.Text))
                        stringQueue.Enqueue(Result.Text.Replace("\r\n\r\n", "\r\n"));
                    #endregion
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