using Unity.Entities;
using UnityEngine;

namespace MeshBuilder.SmallObject
{
    /// <summary>
    /// This component is added to objects which have a mesh where they want the small objects to be placed
    /// mesh - this will be used to generate the points
    /// filters - filters to filter triangles of the mesh for point generation
    /// smallObjects - these small objects will be placed
    /// 
    /// NOTE: for convenience there is a SmallObjectTarget Monobehaviour class which sets up target objects properly
    /// (adds a TargetObject component, Transform component, and a component which initializes or updates the transform)
    /// </summary>
    [System.Serializable]
    public struct TargetObject : ISharedComponentData
    {
        public Mesh mesh;
        public bool dynamic;
        public ISmallObjectPlacementFilter[] filters;
        public SmallObject[] smallObjects;
    }

    public class TargetObjectComponent : SharedComponentDataWrapper<TargetObject> { }

    // needs to be looked at again
    public struct TargetObjectMeshChanged : IComponentData { }

    // if a target object is too big, it is possible to process it partially,
    // it will get this component which stores the bounds of the processed part
    public struct TargetObjectPartiallyProcessed : IComponentData
    {
        public Bounds processedBounds;
    }

    // the target object has been processed completely, no need to check for updates
    public struct TargetObjectCompletelyProcessed : IComponentData { }
}