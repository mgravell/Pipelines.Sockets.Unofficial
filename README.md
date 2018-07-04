# Pipelines.Sockets.Unofficial

This is a managed sockets connector for the `System.IO.Pipelines` API, intended to act as a stop-gap while there is
no official such connector. Pipelines are pretty useless if you can't actually *connect* them to anything...

It draws inspiration from:

- [`Channels.Networking.Sockets`](https://github.com/davidfowl/Channels/tree/master/src/Channels.Networking.Sockets) - the original "managed sockets" provider I wrote back when Pipelines were Channels
- [`System.IO.Pipelines`](https://github.com/dotnet/corefx/tree/master/src/System.IO.Pipelines) - the "corefx" version of the above
- [`Kestrel.Transport.Sockets`](https://github.com/aspnet/KestrelHttpServer/tree/dev/src/Kestrel.Transport.Sockets) - purely server-side connector used for ASP.NET Core, using pieces of the above

and aims to provide a high-performance implementation of the `IDuplexPipe` interface, providing both client and server APIs. At the moment the API is *very* preliminary.

Key APIs:

- `SocketConnection` - interacting with a `Socket` as a pipe
- `StreamConnection` - interacting with a `Stream` as a pipe, or a pipe as a `Stream`

It is provided under the MIT license.
