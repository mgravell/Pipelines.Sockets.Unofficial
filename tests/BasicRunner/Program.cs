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
            Console.WriteLine($"Starting {label}...");
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
            Console.WriteLine($"Completed {label}");
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
                //SocketConnection.SetLog(log);
                //Utils.DebugLog = Console.Out;
                //SocketConnection.SetLog(Console.Out);
#endif
                //await MemoryMappedDecode();
                await SocketPingPong(Console.Out);
            }

        }
        static async Task MemoryMappedDecode()
        {
            const string path = "t8.shakespeare.txt";
            Console.WriteLine();
            var fi = new FileInfo(path);
            if(!fi.Exists)
            {
                Console.WriteLine($"Input file not found: {path}");
                return;
            }
            const int REPEAT = 10;
            var enc = Encoding.ASCII;
            Console.WriteLine($"Reading: {fi.Name}, {fi.Length} bytes, encoding: {enc.EncodingName}");
            Console.WriteLine();
            Console.WriteLine("Using PipeTextReader/MemoryMappedPipeReader (no buffer)");
            for (int i = 0; i < REPEAT; i++)
            {
                var mmap = MemoryMappedPipeReader.Create(path);
                using (mmap as IDisposable)
                using (var reader = PipeTextReader.Create(mmap, enc, bufferSize: 0))
                {
                    var watch = Stopwatch.StartNew();
                    var s = await TestReaderTests.MeasureAndTime(reader);
                    watch.Stop();
                    Console.WriteLine($"{s}; time taken: {watch.ElapsedMilliseconds}ms");
                }
            }
            Console.WriteLine();
            Console.WriteLine("Using PipeTextReader/MemoryMappedPipeReader (leased buffer)");
            for (int i = 0; i < REPEAT; i++)
            {
                var mmap = MemoryMappedPipeReader.Create(path);
                using (mmap as IDisposable)
                using (var reader = PipeTextReader.Create(mmap, enc))
                {
                    var watch = Stopwatch.StartNew();
                    var s = await TestReaderTests.MeasureAndTime(reader);
                    watch.Stop();
                    Console.WriteLine($"{s}; time taken: {watch.ElapsedMilliseconds}ms");
                }
            }
            Console.WriteLine();
            Console.WriteLine("Using StreamReader/FileStream");
            for (int i = 0; i < REPEAT; i++)
            {
                using (var reader = new StreamReader(path, enc))
                {
                    var watch = Stopwatch.StartNew();
                    var s = await TestReaderTests.MeasureAndTime(reader);
                    watch.Stop();
                    Console.WriteLine($"{s}; time taken: {watch.ElapsedMilliseconds}ms");
                }
            }
        }
        static async Task SocketPingPong(TextWriter log)
        {

            var parent = PingPongTests.Create(log);

            await RunTest(parent.Basic_Pipelines_PingPong, "Socket=>Pipelines=>PingPong");
            await RunTest(parent.Basic_Pipelines_Text_PingPong, "Socket=>Pipelines=>TRW=>PingPong");

            await RunTest(parent.Basic_NetworkStream_PingPong, "Socket=>NetworkStream=>PingPong");
            await RunTest(parent.Basic_NetworkStream_Text_PingPong, "Socket=>NetworkStream=>TRW=>PingPong");


            await RunTest(parent.Basic_NetworkStream_Pipelines_PingPong, "Socket=>NetworkStream=>Pipelines=>PingPong");

            if (PingPongTests.RunTLS)
            {
                //await RunTest(parent.ServerClientDoubleInverted_SslStream_PingPong, "Socket=>Pipelines=>Inverter=>SslStream=>Inverter=>PingPong");
                //await RunTest(parent.ServerClient_SslStream_PingPong, "Socket=>NetworkStream=>SslStream=>PingPong");
                //await RunTest(parent.ServerClient_SslStream_Inverter_PingPong, "Socket=>NetworkStream=>SslStream=>Inverter=>PingPong");
            }
        }
    }
}

