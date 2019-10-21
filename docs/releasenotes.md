# Release Notes

## 2.0.??

- add `BufferWriterTextWriter`, a `TextWriter` over an `IBufferWriter<byte>`

## 2.0.25

- add new `BufferWriter<T>` API; implements `IBufferWriter<T>`, but with a different model to `Pipe`
- [change how `PipeWriter.OnReaderCompleted` works](https://github.com/dotnet/corefx/issues/38362)

## 2.0.22

- add APIs to work with multi-cast delegates without allocating

## 2.0.20

- performance: improve high-congestion performance of Wait and improve low-congestion performance of WaitAsync (both paths now try spin-wait on first competitor **only**)

## 2.0.17

- fix: avoid stall condition on `MutexSlim`

## 2.0.11

- add ability to control the listen-backlog size on `SocketServer` (via [su21](https://github.com/sillyousu))

## 2.0.10

- add API to query length of a `Pipe`, and to query the state of a `SocketConnection`
- add API to query whether a `MutexSlim` is available without changing the state or requiring disposal

## 2.0.5

- fix issue #26 - `SocketConnection.DoSend` causing intermittent problems with invalid buffer re-use

## 2.0.1

- arenas (`Sequence<T>`): make better use of `ref return` features, `ref foreach` enumerators, and `in` operators; this is not binary compatible, hence 2.0

## 1.1.*

Two big feature additions in 1.1

- `MutexSlim` - works a lot like `new SemaphoreSlim(1,1)`, but optimized for mutex usage, and [fixes the sync+async problem in `SemaphoreSlim`](https://blog.marcgravell.com/2019/02/fun-with-spiral-of-death.html)
- arena allocation, including `Arena<T>`, `Arena`, `Sequence<T>` and `Reference<T>`; [discussed in more detail here](https://mgravell.github.io/Pipelines.Sockets.Unofficial/docs/arenas)


## 1.0.*

All the types you're likely to need for raw pipelines work over sockets, in particular `SocketConnection` and `StreamConnection`.

For typical usage of most features, [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) is a good place to look.