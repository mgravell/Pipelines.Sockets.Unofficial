@ECHO OFF
dotnet run -p .\tests\Benchmarks\ -c Release -f net472 --runtimes net472 netcoreapp2.0 netcoreapp2.1 -m