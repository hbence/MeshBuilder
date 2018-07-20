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
    }

    public class SmallObjectComponent : SharedComponentDataWrapper<SmallObject> { }
}
