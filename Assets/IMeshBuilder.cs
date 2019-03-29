
using UnityEngine;

public interface IMeshBuilder
{
    Mesh Mesh { get; }

    void StartGeneration();
    void EndGeneration();

    bool IsGenerating { get; }
}
