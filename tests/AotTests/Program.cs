
using Pipelines.Sockets.Unofficial;
using System;

Console.WriteLine("Validating AOT build/features");
TestDelegates();

static void TestDelegates()
{
    Console.WriteLine($"Delegates.IsSupported: {Delegates.IsSupported}");

    TestDelegate(null);
    TestDelegate(() => { Console.WriteLine("Single delegate"); });

    Action multi = () => { Console.WriteLine("Multi delegate A"); };
    multi += () => { Console.WriteLine("Multi delegate B"); };
    TestDelegate(multi);

    static void TestDelegate(Action? action)
    {
        foreach (var handler in Delegates.AsEnumerable(action))
        {
            handler!();
        }
    }

}