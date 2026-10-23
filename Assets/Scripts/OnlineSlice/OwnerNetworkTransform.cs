using Unity.Netcode.Components;

/// <summary>
/// Prototype transform synchronization: each player sends their own movement.
/// The host still validates gameplay interactions on the server.
/// </summary>
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
