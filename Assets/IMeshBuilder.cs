
using UnityEngine;

public interface IMeshBuilder : System.IDisposable
{
    Mesh Mesh { get; }

    void StartGeneration();
    void EndGeneration();

    bool IsGenerating { get; }
}
