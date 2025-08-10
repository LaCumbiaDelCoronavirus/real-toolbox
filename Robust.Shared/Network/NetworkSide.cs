using System;

namespace Robust.Shared.Network;

/// <summary>
///     Flag that specifies the server, client, or both.
/// </summary>
[Flags]
public enum NetworkSide : byte
{
    Client = 1 << 0,
    Server = 1 << 1,
    Shared = Client | Server
}
