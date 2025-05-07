@ECHO OFF
dotnet run --project .\tests\Benchmark\ -c Release -f net6.0 --runtimes net472 net6.0 -m %*