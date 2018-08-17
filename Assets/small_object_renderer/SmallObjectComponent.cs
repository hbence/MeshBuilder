using UnityEngine;
using Unity.Entities;
using Unity.Rendering;

namespace MeshBuilder.SmallObject
{
    [System.Serializable]
    public struct SmallObject : ISharedComponentData
    {
        public string name;
        public SmallObjectLOD lod0;
        public SmallObjectLOD lod1;
        public SmallObjectLOD lod2;

        public Vector3 minScale;
        public Vector3 maxScale;
        public Vector3 minRotation;
        public Vector3 maxRotation;
        public Vector3 minOffset;
        public Vector3 maxOffset;
    }

    public class SmallObjectComponent : SharedComponentDataWrapper<SmallObject> { }
}
