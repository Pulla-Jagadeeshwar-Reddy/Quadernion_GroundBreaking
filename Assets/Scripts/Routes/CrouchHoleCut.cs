using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Left by the route builder on a floor-model object it changed to cut a crouch hole through a
/// wall: the mesh it replaced and the colliders it switched off, so the next build can put them
/// back first. No runtime behaviour.
/// </summary>
public class CrouchHoleCut : MonoBehaviour
{
    public Mesh originalMesh;
    public List<Collider> disabledColliders = new();
}
