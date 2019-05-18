
using UnityEngine;

public interface IMeshBuilder : System.IDisposable
{
    Mesh Mesh { get; }
}
