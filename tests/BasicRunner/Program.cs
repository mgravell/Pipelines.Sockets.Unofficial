using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Tests;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BasicRunner
{
    static class Program
    {
        static string Rate(Stopwatch watch)
        {
            var seconds = ((decimal)watch.ElapsedMilliseconds) / 1000;
            return seconds <= 0 ? "inf" : $"{(PingPongTests.LoopCount / seconds):0}/s";

        }

        static async Task RunTest(Func<Task> method, string label)
        {
            Console.WriteLine();
            Console.WriteLine();
            for (int i = 0; i < 10; i++)
            {
#if DEBUG
                if (i == 1) DebugCounters.Reset();
#endif
                var watch = Stopwatch.StartNew();
                await method();
                watch.Stop();
                Console.WriteLine($"{watch.ElapsedMilliseconds}ms\t{Rate(watch)}\t{label}");

#if DEBUG
                if (i == 1)
                {
                    var summary = DebugCounters.GetSummary();
                    Console.WriteLine(summary);
                }
#endif
            }
        }

        static async Task Main()
        {
            Thread.CurrentThread.Name = nameof(Main);
            Console.WriteLine($"Loop count: {PingPongTests.LoopCount}");
            Console.WriteLine($"Scheduler: {PingPongTests.Scheduler}");
            Console.WriteLine($"MemoryMappedPipeReader: {(MemoryMappedPipeReader.IsAvailable ? "available" : "unavailable")}");
            Console.WriteLine($"Detailed trace to 'log.txt'");

            using (var log = new StreamWriter("log.txt", false))
            {
#if DEBUG
                SocketConnection.SetLog(log);
                //SocketConnection.SetLog(Console.Out);
#endif
                await MemoryMappedDecode();
                //await SocketPingPong(log);
            }

        }
        static async Task MemoryMappedDecode()
        {
            char[] buffer = new char[2048];
            async ValueTask MeasureAndTime(TextReader reader)
            {

                int lineCount = 0, nonEmptyLineCount = 0;
                long charCount = 0;

                var watch = Stopwatch.StartNew();

                //int charsRead;
                //do
                //{
                //    charsRead = await reader.ReadAsync(buffer, 0, 2048);
                //    len += charsRead;
                //} while (charsRead != 0);

                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    charCount += line.Length;
                    if (!string.IsNullOrWhiteSpace(line)) nonEmptyLineCount++;
                    lineCount++;
                }
                watch.Stop();
                Console.WriteLine($"Lines: {lineCount} ({nonEmptyLineCount} non-empty, {charCount} characters); Time: {watch.ElapsedMilliseconds}ms");
                //Console.WriteLine($"Total chars {len}; Time: {watch.ElapsedMilliseconds}ms");
            }

            const string path = "t8.shakespeare.txt";
            Console.WriteLine();
            var fi = new FileInfo(path);
            if(!fi.Exists)
            {
                Console.WriteLine($"Input file not found: {path}");
                return;
            }
            Console.WriteLine($"Reading: {fi.Name}, {fi.Length} bytes");
            Console.WriteLine();
            Console.WriteLine("Using PipeTextReader/MemoryMappedPipeReader");
            for (int i = 0; i < 10; i++)
            {
                var mmap = MemoryMappedPipeReader.Create(path);
                using (mmap as IDisposable)
                using (var reader = new PipeTextReader(mmap, Encoding.UTF8))
                {
                    await MeasureAndTime(reader);
                    mmap.Complete();
                }
            }
            Console.WriteLine();
            Console.WriteLine("Using StreamReader/FileStream");
            for (int i = 0; i < 10; i++)
            {
                using (var reader = new StreamReader(path, Encoding.UTF8))
                {
                    await MeasureAndTime(reader);
                }
            }
        }
        static async Task SocketPingPong(TextWriter log)
        {

            var parent = new PingPongTests(log);

            await RunTest(parent.Basic_Pipelines_PingPong, "Socket=>Pipelines=>PingPong");
            await RunTest(parent.Basic_Pipelines_Text_PingPong, "Socket=>Pipelines=>TRW=>PingPong");

            await RunTest(parent.Basic_NetworkStream_PingPong, "Socket=>NetworkStream=>PingPong");
            await RunTest(parent.Basic_NetworkStream_Text_PingPong, "Socket=>NetworkStream=>TRW=>PingPong");
            

            //await RunTest(parent.Basic_NetworkStream_Pipelines_PingPong, "Socket=>NetworkStream=>Pipelines=>PingPong");

            if (PingPongTests.RunTLS)
            {
                //await RunTest(parent.ServerClientDoubleInverted_SslStream_PingPong, "Socket=>Pipelines=>Inverter=>SslStream=>Inverter=>PingPong");
                //await RunTest(parent.ServerClient_SslStream_PingPong, "Socket=>NetworkStream=>SslStream=>PingPong");
                //await RunTest(parent.ServerClient_SslStream_Inverter_PingPong, "Socket=>NetworkStream=>SslStream=>Inverter=>PingPong");
            }
        }
    }
}

