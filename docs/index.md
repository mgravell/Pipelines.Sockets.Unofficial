# Pipelines.Sockets.Unofficial

This utility library intended to provide useful additions for working with "pipelines". This includes additional features not *directly* tied to "pipelines",
but likely to be useful in sceneraios where you would be interested in looking at "pipelines".

# Release Notes

## 1.1.*

Two big feature additions in 1.1

- `MutexSlim` - works a lot like `new SemaphoreSlim(1,1)`, but optimized for mutex usage, and [fixes the sync+async problem in `SemaphoreSlim`](https://blog.marcgravell.com/2019/02/fun-with-spiral-of-death.html)
- arena allocation, including `Arena<T>`, `Arena`, `Sequence<T>` and `Reference<T>`; [discussed in more detail here](/docs/arenas.md)


## 1.0.*

All the types you're likely to need for raw pipelines work over sockets, in particular `SocketConnection` and `StreamConnection`.

For typical usage of most features, [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) is a good place to look.