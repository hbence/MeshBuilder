using Unity.Entities;
using Unity.Rendering;

namespace MeshBuilder.SmallObject
{
    [System.Serializable]
    public struct SmallObjectLOD : ISharedComponentData
    {
        public MeshInstanceRenderer renderer;
        public float appearDistance;
        public float disappearDistance;
        public int numberPerCell;
    }

    public class SmallObjectLODComponent : SharedComponentDataWrapper<SmallObjectLOD> { }
}
