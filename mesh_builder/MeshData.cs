using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

namespace MeshBuilder
{
    /// <summary>
    /// Utility class to handle generated mesh creation and update.
    /// </summary>
    [System.Serializable]
    public class MeshData
    {
        public const bool HasUVs = true;
        public const bool NoUVs = false;

        public const bool MarkDynamic = true;
        public const bool DontMarkDynamic = false;

        // NOTE: Add more buffer types later when needed
        public enum Buffer
        {
            None = 0,
            Vertex   = 1 << 0,
            Triangle = 1 << 1,
            Normal   = 1 << 2,
            Color    = 1 << 3,
            Tangent  = 1 << 4,
            UV       = 1 << 5,
            UV2      = 1 << 6,
            UV3      = 1 << 7,
            UV4      = 1 << 8,
        }

        [SerializeField]
        [HideInInspector]
        private Vector3[] vertices;
        public Vector3[] Vertices { get { return vertices; } }

        [SerializeField]
        [HideInInspector]
        private int[] triangles;
        public int[] Triangles { get { return triangles; } }

        [SerializeField]
        [HideInInspector]
        private Vector2[] uvs;
        public Vector2[] UVs { get { return uvs; } }

        [SerializeField]
        [HideInInspector]
        private Mesh mesh;
        public Mesh Mesh { get; private set; }

        [SerializeField]
        [HideInInspector]
        private Buffer backedBuffers;

        /// <summary>
        /// Creates a new mesh, vertex and triangle buffers
        /// </summary>
        /// <param name="verticesBufferSize">vertex buffer size</param>
        /// <param name="triangleBufferSize">triangle buffer size</param>
        /// <param name="hasUvs">should it create a uv buffer also?</param>
        /// <param name="markDynamic">should it mark the mesh as dynamic? (this can also be done manually on the mesh object)</param>
        public MeshData(int verticesBufferSize, int triangleBufferSize, bool hasUvs = HasUVs, bool markDynamic = DontMarkDynamic)
        {
            mesh = new Mesh();

            if (markDynamic)
            {
                mesh.MarkDynamic();
            }

            backedBuffers = Buffer.Vertex | Buffer.Triangle;
            vertices = new Vector3[verticesBufferSize];
            triangles = new int[triangleBufferSize];

            if (hasUvs)
            {
                backedBuffers |= Buffer.UV;
                uvs = new Vector2[verticesBufferSize];
            }
        }

        public MeshData(Mesh sourceMesh, Buffer buffersToCopy, Buffer buffersToKeep, bool markDynamic = DontMarkDynamic)
        {
            mesh = new Mesh();
        }
        /*
        public void UpdateData()
        {
            Mesh.vertices = Vertices;
            Mesh.triangles = Triangles;
            Mesh.uv = UVs;
        }
        
        public void UpdateVertices(NativeArray<float3> data)
        {
            VectorConverter converter;
            if (HasFlag(Buffer.Vertex))
            {
                converter = new VectorConverter { Vector3Array = Vertices };
            }
            else
            {
                Vector3[] tempBuffer = new Vector3[data.Length];
                converter = new VectorConverter { Vector3Array = tempBuffer };
            }

            data.CopyTo(converter.Float3Array);
            Mesh.vertices = converter.Vector3Array;
            Mesh.RecalculateNormals();
        }
        */
        private bool HasFlag(Buffer flag)
        {
            return (backedBuffers & flag) != 0; 
        }
        
        static public void UpdateMesh(Mesh mesh, NativeArray<float3> vertices, NativeArray<int> tris, NativeArray<float2> uvs)
        {
            mesh.Clear();

            var verticesData = new VectorConverter { Float3Array = vertices.ToArray() };
            var triArray = tris.ToArray();
            var uvData = new UVConverter { Float2Array = uvs.ToArray() };

            mesh.vertices = verticesData.Vector3Array;
            mesh.triangles = triArray;
            mesh.uv = uvData.Vector2Array;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // TODO: check back later, newer versions of Unity might allow direct use of math versions
        // of data types making these converter classes depricated
        [StructLayout(LayoutKind.Explicit)]
        public struct VectorConverter
        {
            [FieldOffset(0)]
            public float3[] Float3Array;

            [FieldOffset(0)]
            public Vector3[] Vector3Array;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct UVConverter
        {
            [FieldOffset(0)]
            public float2[] Float2Array;

            [FieldOffset(0)]
            public Vector2[] Vector2Array;
        }
    }
}
