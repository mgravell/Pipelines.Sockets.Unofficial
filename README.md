# Pipelines.Sockets.Unofficial

This is a managed sockets connector for the `System.IO.Pipelines` API, intended to act as a stop-gap while there is
no official such connector. It is largely borrowed from the "Kestrel" code which has a managed sockets provider, but
Kestrel's version is not intended as a public general purpose API - and in particular contains no client connect
functionality. It might sound cheeky making this available here, but it isn't as rude as it sounds - a large part of
the Kestrel version was originally written by me for back when "Pipelines" was at the inception stage, back when 
it was known as "Channels".

Hopefully some folks can make use of this for now; and maybe it'll encourage a proper client/server managed sockets
pipelines layer to be added to the official codebase. It will be my absolute pleasure to delete this repo when the
"real" version is viable.
