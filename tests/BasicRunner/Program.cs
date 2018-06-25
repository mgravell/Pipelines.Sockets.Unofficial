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
            Console.WriteLine($"Detailed trace to 'log.txt'");

            // Console.WriteLine(typeof(System.Buffers.ReadOnlySequence<byte>).Assembly.Location);


            using (var logFile = new StreamWriter("log.txt", false))
            {
#if DEBUG
                SocketConnection.SetLog(logFile);
                //SocketConnection.SetLog(Console.Out);
#endif

                char[] buffer = new char[2048];
                async ValueTask MeasureAndTime(TextReader reader)
                {

                    //int count = 0;
                    long len = 0;
                    
                    var watch = Stopwatch.StartNew();

                    int charsRead;
                    do
                    {
                        charsRead = await reader.ReadAsync(buffer, 0, 2048);
                        len += charsRead;
                    } while (charsRead != 0);

                    //string line;
                    //while ((line = await reader.ReadLineAsync()) != null)
                    //{
                    //    len += line.Length;
                    //    count++;
                    //}
                    watch.Stop();
                    //Console.WriteLine($"Lines: {count}; Length: {len}; Time: {watch.ElapsedMilliseconds}ms");
                    Console.WriteLine($"Total chars {len}; Time: {watch.ElapsedMilliseconds}ms");
                }

                const string path = "logcopy.txt";
                Console.WriteLine();
                Console.WriteLine($"File size: {(new FileInfo(path).Length)} bytes");
                Console.WriteLine();
                Console.WriteLine("Using PipeTextReader/MemoryMappedPipeReader");
                for (int i = 0; i < 5; i++)
                {
                    using (var mmap = MemoryMappedPipeReader.Create(path))
                    using (var reader = new PipeTextReader(mmap, Encoding.UTF8))
                    {
                        await MeasureAndTime(reader);
                    }
                }
                Console.WriteLine();
                Console.WriteLine("Using StreamReader/FileStream");
                for (int i = 0; i < 5; i++)
                {
                    using (var reader = new StreamReader(path, Encoding.UTF8))
                    {
                        await MeasureAndTime(reader);
                    }
                }

            }
            //using (var logFile = new StreamWriter("log.txt", false))
            //{
            //    var parent = new PingPongTests(logFile);

            //    //await RunTest(parent.Basic_Pipelines_PingPong, "Socket=>Pipelines=>PingPong");
            //    //await RunTest(parent.Basic_NetworkStream_PingPong, "Socket=>NetworkStream=>PingPong");

            //    await RunTest(parent.Basic_NetworkStream_Text_PingPong, "Socket=>NetworkStream=>TRW=>PingPong");
            //    await RunTest(parent.Basic_Pipelines_Text_PingPong, "Socket=>Pipelines=>TRW=>PingPong");

            //    //await RunTest(parent.Basic_NetworkStream_Pipelines_PingPong, "Socket=>NetworkStream=>Pipelines=>PingPong");

            //    if (PingPongTests.RunTLS)
            //    {
            //        //await RunTest(parent.ServerClientDoubleInverted_SslStream_PingPong, "Socket=>Pipelines=>Inverter=>SslStream=>Inverter=>PingPong");
            //        //await RunTest(parent.ServerClient_SslStream_PingPong, "Socket=>NetworkStream=>SslStream=>PingPong");
            //        //await RunTest(parent.ServerClient_SslStream_Inverter_PingPong, "Socket=>NetworkStream=>SslStream=>Inverter=>PingPong");
            //    }
            //}
        }
    }
}

