using ALogger;
using ImageMagick;
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
using Windows.Media.Ocr;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using System.Drawing.Imaging;
using Windows.Foundation;
using System.Text.Json;

namespace TextExtract
{
    class Program
    {
        static void Main()
        {
            var finalPriceList = new ConcurrentQueue<KeyValuePair<string, double>>();
            var path = ConfigurationManager.AppSettings["StoragePath"];
            CancellationTokenSource source = new();
            CancellationToken token = source.Token;
            ParallelOptions parallelOptions = new();
            parallelOptions.MaxDegreeOfParallelism = Environment.ProcessorCount;
            parallelOptions.CancellationToken = token;

            var log = new Logger(source);
            log.AddMessage(new LogMessage(Levels.Log, "Press 'ESC' to stop program"));

            while (!(Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Escape))
            {
                Parallel.ForEachAsync(Directory.GetFiles(path, "*.bmp"), parallelOptions, async (file, token) =>
                {
                    //File path 
                    FileInfo fileInfo = new(file);
                    // min 5 sec old to avoid conflicts
                    if (new TimeSpan(DateTime.Now.Ticks - fileInfo.CreationTime.Ticks).TotalSeconds > 5)
                        await ProcessWithWindowsOcr(fileInfo.FullName, source, log, finalPriceList);
                });

                // save text
                if (!finalPriceList.IsEmpty)
                {
                    string txtFile = $"{path}{DateTime.Now.Ticks}.txt";
                    File.WriteAllText(txtFile
                        , JsonSerializer.Serialize(finalPriceList));
                    log.AddMessage(new LogMessage(Levels.Success, $"Wrote textfile {txtFile}, containing {finalPriceList.Count} trade items"));
                    finalPriceList.Clear();
                }
                // full round - wait 5 sec for next
                log.AddMessage(new LogMessage(Levels.Log, $"Waiting 5 sec to look for new BMP files..."));
                Thread.Sleep(5000);
            }
            // clean up
            source.Cancel();
            log.Flush();
        }
        private async static Task ProcessWithWindowsOcr(string filePath, CancellationTokenSource source, Logger log, ConcurrentQueue<KeyValuePair<string, double>> finalPriceList)
        {
            var ocrLanguage = new Language("en");
            OcrEngine ocrEngine = OcrEngine.TryCreateFromLanguage(ocrLanguage);

            if (ocrEngine == null)
            {
                log.AddMessage(new LogMessage(Levels.Error, $"Language {ocrLanguage} is not supported as Ocr language on this machine - install via Windows Language/...add Language"));
                return;
            }
            try
            {
                if (File.Exists(filePath))
                {
                    List<string> queue = new();
                    Bitmap b = new(filePath);

                    #region Process Image
                    Percentage brightness = new(30);
                    Percentage contrast = new(100);
                    Stopwatch watch = new();
                    watch.Start();
                    using MagickImage image = new(new MagickFactory().Image.Create(b));
                    // Dispose asap to allow file renaming
                    b.Dispose();
                    // rename file to remove it from round
                    new FileInfo(filePath).MoveTo(filePath + ".bak");
                    // time intensive tasks
                    image.BrightnessContrast(brightness, contrast);
                    image.ColorSpace = ColorSpace.Gray;
                    watch.Stop();
                    log.AddMessage(new LogMessage(Levels.Success, $"Found '{filePath}' - image processed within {watch.Elapsed.TotalSeconds} sec"));
                    #endregion

                    #region Ocr
                    // Ocr
                    watch.Restart();
                    SoftwareBitmap bitmap = await BitmapToSoftwareBitmap(image.ToBitmap());
                    var ocrResult = await ocrEngine.RecognizeAsync(bitmap);
                    // result saving
                    foreach (var line in ocrResult.Lines)
                        queue.Add(line.Text);

                    for (var i = 0; i < queue.Count / 2; i++)
                    {
                        string name = queue[i].ToString();
                        if (double.TryParse(queue[i + (queue.Count / 2)], out double price))
                            finalPriceList.Enqueue(new KeyValuePair<string, double>(queue[i].Trim(), price));
                    }
                    log.AddMessage(new LogMessage(Levels.Success, $"Ocr found {queue.Count / 2} trade items withinin {watch.Elapsed.TotalSeconds} sec"));
                    queue.Clear();
                    #endregion
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private async static Task<SoftwareBitmap> BitmapToSoftwareBitmap(Bitmap bitmap)
        {
            SoftwareBitmap softwareBitmap;
            using (Windows.Storage.Streams.InMemoryRandomAccessStream stream = new())
            {
                bitmap.Save(stream.AsStream(), ImageFormat.Jpeg);//choose the specific image format by your own bitmap source
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            }
            return softwareBitmap;
        }
    }
}