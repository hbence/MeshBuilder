using UnityEngine;
using Unity.Mathematics;

namespace MeshBuilder.SmallObject
{
    public interface ISmallObjectPlacementFilter
    {
        bool Allow(Vector3 v0, Vector3 v1, Vector3 v2);
    }

    public struct BoundsFilter : ISmallObjectPlacementFilter
    {
        public Bounds bounds;

        public bool Allow(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            return bounds.Contains(v0) && bounds.Contains(v1) && bounds.Contains(v2);
        }
    }

    public struct NegativeBoundsFilter : ISmallObjectPlacementFilter
    {
        public Bounds bounds;

        public bool Allow(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            return !(bounds.Contains(v0) && bounds.Contains(v1) && bounds.Contains(v2));
        }
    }

    public struct HeightFilter
    {
        public float minHeight;
        public float maxHeight;

        public bool Allow(Vector3 v0, Vector3 v1, Vector3 v2)
        {
            return minHeight <= v0.y && v0.y <= maxHeight &&
                    minHeight <= v1.y && v1.y <= maxHeight &&
                    minHeight <= v2.y && v2.y <= maxHeight
                ;
        }
    }

}