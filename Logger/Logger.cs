using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace ALogger
{
    public class Logger
    {
        protected ConcurrentQueue<LogMessage> Queue = new ConcurrentQueue<LogMessage>();
        protected CancellationTokenSource Source;

        public Logger(CancellationTokenSource source)
        {
            Source = source;
            Queue.Enqueue(new LogMessage(Levels.Log, $"{DateTime.Now}; Logger started"));
            EmptyLog(Queue, Source);
        }

        public void Stop()
        {
            EmptyLog(Queue, Source);
            Source.Cancel();
        }

        public void AddMessage(LogMessage msg)
        {
            Queue.Enqueue(msg);
        }

        public void Flush()
        {
            EmptyLog(Queue, Source);
        }

        private static void EmptyLog(ConcurrentQueue<LogMessage> queue, CancellationTokenSource source) =>
            Task.Factory.StartNew(() =>
            {
                do
                {
                    bool filled;
                    do
                    {
                        filled = queue.TryDequeue(out var msg);
                        if (filled)
                            WriteLog(msg);
                    } while (filled);
                    Thread.Sleep(250);
                }
                while (!source.IsCancellationRequested);
            }, source.Token);

        private static void WriteLog(LogMessage msg)
        {
            try
            {
                switch (msg.Level)
                {
                    case Levels.Error:
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        break;
                    case Levels.Success:
                        Console.ForegroundColor = ConsoleColor.DarkGreen;
                        break;
                    case Levels.Log:
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.White;
                        break;
                }
                Console.Out.WriteLine(msg.Message.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
            finally
            {
                Console.ResetColor();
            }
        }
    }
}
