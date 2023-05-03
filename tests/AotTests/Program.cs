
using Pipelines.Sockets.Unofficial;
using Pipelines.Sockets.Unofficial.Arenas;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

Console.WriteLine("Validating AOT build/features");
TestDelegates();
TestArenas();

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

static void TestArenas()
{
    // here we ask for an int and a chunk of floats, and find that they have in fact
    // both come from the same chunk
    var arena = new Arena(new ArenaOptions(ArenaFlags.BlittablePaddedSharing));
    var a = arena.Allocate<int>();
    var payload = "hello, world!"u8;
    var b = MemoryMarshal.Cast<float, byte>(arena.Allocate<float>(5).FirstSpan).Slice(0, payload.Length);
    a.Value = 42;
    payload.CopyTo(b);
    Console.WriteLine($"42 vs {a.Value}");
    Console.WriteLine(Encoding.UTF8.GetString(b));
    Console.WriteLine($"byte offset (expect 4): {Unsafe.ByteOffset(ref Unsafe.As<int, byte>(ref a.Value), ref MemoryMarshal.GetReference(b))}");
    arena.Reset();
}