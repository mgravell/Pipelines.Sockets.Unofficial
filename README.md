# Pipelines.Sockets.Unofficial

This is a managed sockets connector for the `System.IO.Pipelines` API, intended to act as a stop-gap while there is
no official such connector. It draws from:

- [`Channels.Networking.Sockets`](https://github.com/davidfowl/Channels/tree/master/src/Channels.Networking.Sockets) - the original "managed sockets" provider I wrote back when Pipelines were Channels
- [`System.IO.Pipelines.Networking.Sockets`](https://github.com/dotnet/corefxlab/tree/master/src/System.IO.Pipelines.Networking.Sockets) - the "corefxlab" version of the above
- [`Kestrel.Transport.Sockets`](https://github.com/aspnet/KestrelHttpServer/tree/dev/src/Kestrel.Transport.Sockets) - purely server-side connector used for ASP.NET Core

and aims to provide a high-performance implementation of the `IDuplexPipe` interface, providing both client and server APIs. At the moment the API is *very* preliminary.

It is completely unofficial and should not be interpreted as anything other than a bridge.


Complete:

- basic client connect

Planned:

- server API
- socket option configuration
- deferred read/write loop spawn
- optional "optimize for large volumes of idle connections" (aka "zero-length reads")
- your suggestions here...
