using System.Runtime.InteropServices;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;

using static MeshBuilder.Utils;

namespace MeshBuilder
{
    using Offset = Utils.Offset;

    /// <summary>
    /// Utility class to handle generated mesh creation and update.
    /// Note: this can hold any kind of incomplete mesh data, so if you use it to update a mesh
    /// it can fail if an operation is not allowed by the mesh api, for example setting triangle indices without setting vertices
    /// </summary>
    public struct MeshData : System.IDisposable
    {
        public const uint DefaultFlags = (uint)Buffer.Vertex | (uint)Buffer.Triangle | (uint)Buffer.Normal | (uint)Buffer.UV;
        public const uint AllFlags = int.MaxValue;

        // NOTE: Add more buffer types later when needed
        public enum Buffer
        {
            None = 0,
            Vertex = 1 << 0,
            Triangle = 1 << 1,
            Normal   = 1 << 2,
            Color    = 1 << 3,
            Tangent  = 1 << 4,
            UV       = 1 << 5,
            UV2      = 1 << 6,
            UV3      = 1 << 7,
            UV4      = 1 << 8,
        }

        private NativeArray<float3> vertices;
        public NativeArray<float3> Vertices { get => vertices; }
        private NativeArray<float3> normals;
        public NativeArray<float3> Normals { get => normals; }
        private NativeArray<Color> colors;
        public NativeArray<Color> Colors { get => colors; }
        private NativeArray<float4> tangents;
        public NativeArray<float4> Tangents { get => tangents; }
        private NativeArray<float2> uvs;
        public NativeArray<float2> UVs { get => uvs; }
        private NativeArray<float2> uvs2;
        public NativeArray<float2> UVs2 { get => uvs2; }
        private NativeArray<float2> uvs3;
        public NativeArray<float2> UVs3 { get => uvs3; }
        private NativeArray<float2> uvs4;
        public NativeArray<float2> UVs4 { get => uvs4; }

        private NativeArray<int> triangles;
        public NativeArray<int> Triangles { get => triangles; }
        private NativeArray<Offset> submeshTriangleOffsets;

        public int VerticesLength { get; private set; }

        public MeshData(int verticesLength, int trianglesLength, Allocator allocator, uint bufferFlags = DefaultFlags)
            : this(verticesLength, trianglesLength, new Offset[] { new Offset { index = 0, length = trianglesLength } } , allocator, bufferFlags)
        {

        }

        public MeshData(int verticesLength, int trianglesLength, Offset[] submeshOffsets, Allocator allocator, uint bufferFlags = DefaultFlags)
        {
            VerticesLength = verticesLength;

            vertices = Initialize<float3>(bufferFlags, Buffer.Vertex, verticesLength, allocator);
            normals = Initialize<float3>(bufferFlags, Buffer.Normal, verticesLength, allocator);
            colors = Initialize<Color>(bufferFlags, Buffer.Color, verticesLength, allocator);
            tangents = Initialize<float4>(bufferFlags, Buffer.Tangent, verticesLength, allocator);
            uvs = Initialize<float2>(bufferFlags, Buffer.UV, verticesLength, allocator);
            uvs2 = Initialize<float2>(bufferFlags, Buffer.UV2, verticesLength, allocator);
            uvs3 = Initialize<float2>(bufferFlags, Buffer.UV3, verticesLength, allocator);
            uvs4 = Initialize<float2>(bufferFlags, Buffer.UV4, verticesLength, allocator);

            triangles = Initialize<int>(bufferFlags, Buffer.Triangle, trianglesLength, allocator);
            submeshTriangleOffsets = Initialize(bufferFlags, Buffer.Triangle, submeshOffsets, allocator);
        }

        public MeshData(Mesh mesh, Allocator allocator, uint bufferFlags = AllFlags)
        {
            VerticesLength = mesh.vertexCount;

            vertices = Initialize(bufferFlags, Buffer.Vertex, ToFloat3Array(mesh.vertices), allocator);
            normals = Initialize(bufferFlags, Buffer.Normal, ToFloat3Array(mesh.normals), allocator);
            colors = Initialize(bufferFlags, Buffer.Color, mesh.colors, allocator);
            tangents = Initialize(bufferFlags, Buffer.Tangent, ToFloat4Array(mesh.tangents), allocator);
            uvs = Initialize(bufferFlags, Buffer.UV, ToFloat2Array(mesh.uv), allocator);
            uvs2 = Initialize(bufferFlags, Buffer.UV2, ToFloat2Array(mesh.uv2), allocator);
            uvs3 = Initialize(bufferFlags, Buffer.UV3, ToFloat2Array(mesh.uv3), allocator);
            uvs4 = Initialize(bufferFlags, Buffer.UV4, ToFloat2Array(mesh.uv4), allocator);

            if (HasFlag(bufferFlags, Buffer.Triangle))
            {
                triangles = Initialize(bufferFlags, Buffer.Triangle, mesh.triangles, allocator);

                int submeshCount = mesh.subMeshCount;
                submeshTriangleOffsets = new NativeArray<Offset>(submeshCount, allocator, NativeArrayOptions.UninitializedMemory);
                for (int i = 0; i < submeshCount; ++i)
                {
                    submeshTriangleOffsets[i] = new Offset
                    {
                        index = (int)mesh.GetIndexStart(i),
                        length = (int)mesh.GetIndexCount(i)
                    };
                }
            }
            else
            {
                triangles = default;
                submeshTriangleOffsets = default;
            }
        }

        static private NativeArray<T> Initialize<T>(uint bufferFlags, Buffer flag, int size, Allocator allocator) where T : struct
        {
            return HasFlag(bufferFlags, flag) ? new NativeArray<T>(size, allocator, NativeArrayOptions.UninitializedMemory) : default;
        }

        static private NativeArray<T> Initialize<T>(uint bufferFlags, Buffer flag, T[] array, Allocator allocator) where T : struct
        {
            return (HasFlag(bufferFlags, flag) && array != null && array.Length > 0) ? new NativeArray<T>(array, allocator) : default;
        }

        public void Dispose()
        {
            SafeDispose(ref vertices);
            SafeDispose(ref triangles);
            SafeDispose(ref submeshTriangleOffsets);
            SafeDispose(ref normals);
            SafeDispose(ref colors);
            SafeDispose(ref tangents);
            SafeDispose(ref uvs);
            SafeDispose(ref uvs2);
            SafeDispose(ref uvs3);
            SafeDispose(ref uvs4);
        }

        public enum UpdateMode { Clear, DontClear }

        public void UpdateMesh(Mesh mesh, UpdateMode mode = UpdateMode.DontClear, uint bufferFlags = AllFlags)
        {
            if (mode == UpdateMode.Clear)
            {
                mesh.Clear();
            }

            bool setVertices = false;
            if (HasVertices && HasFlag(bufferFlags, Buffer.Vertex))
            {
                mesh.vertices = ToVector3Array(vertices);
                setVertices = true;
            }

            if (HasTriangles && HasFlag(bufferFlags, Buffer.Triangle))
            {
                if (VerticesLength > short.MaxValue)
                {
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }
 
                mesh.subMeshCount = submeshTriangleOffsets.Length;
                for (int submesh = 0; submesh < submeshTriangleOffsets.Length; ++submesh)
                {
                    int[] buffer = new int[GetSubmeshTriangleLength(submesh)];
                    CopySubmeshTriangles(submesh, buffer);
                    mesh.SetTriangles(buffer, submesh);
                }
            }

            bool setNormals = false;
            if (HasNormals && HasFlag(bufferFlags, Buffer.Normal))
            {
                mesh.normals = ToVector3Array(normals);
                setNormals = true;
            }

            if (HasColors && HasFlag(bufferFlags, Buffer.Color)) mesh.colors = colors.ToArray();
            if (HasTangents && HasFlag(bufferFlags, Buffer.Tangent)) mesh.tangents = ToVector4Array(tangents);
            if (HasUVs && HasFlag(bufferFlags, Buffer.UV)) mesh.uv = ToVector2Array(uvs);
            if (HasUVs2 && HasFlag(bufferFlags, Buffer.UV2)) mesh.uv2 = ToVector2Array(uvs2);
            if (HasUVs3 && HasFlag(bufferFlags, Buffer.UV3)) mesh.uv3 = ToVector2Array(uvs3);
            if (HasUVs4 && HasFlag(bufferFlags, Buffer.UV4)) mesh.uv4 = ToVector2Array(uvs4);

            if (setVertices)
            {
                mesh.RecalculateBounds();

                if (!setNormals)
                {
                    mesh.RecalculateNormals();
                }
            }
        }

        public Mesh ToMesh(bool markDynamic = false)
        {
            Mesh mesh = new Mesh();

            if (markDynamic)
            {
                mesh.MarkDynamic();
            }

            if (VerticesLength > short.MaxValue)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            if (HasVertices) mesh.vertices = ToVector3Array(vertices);

            if (HasTriangles)
            {
                for (int submesh = 0; submesh < submeshTriangleOffsets.Length; ++submesh)
                {
                    int[] buffer = new int[GetSubmeshTriangleLength(submesh)];
                    CopySubmeshTriangles(submesh, buffer);
                    mesh.SetTriangles(buffer, submesh);
                }
            }

            if (HasNormals) mesh.normals = ToVector3Array(normals);
            if (HasColors) mesh.colors = colors.ToArray();
            if (HasTangents) mesh.tangents = ToVector4Array(tangents);
            if (HasUVs) mesh.uv = ToVector2Array(uvs);
            if (HasUVs2) mesh.uv2 = ToVector2Array(uvs2);
            if (HasUVs3) mesh.uv3 = ToVector2Array(uvs3);
            if (HasUVs4) mesh.uv4 = ToVector2Array(uvs4);

            if (!HasNormals)
            {
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();

            return mesh;
        }

        public bool HasVertices { get => Has(vertices); }
        public bool HasTriangles { get => Has(triangles); }
        public bool HasNormals  { get => Has(normals); } 
        public bool HasColors   { get => Has(colors); }
        public bool HasTangents { get => Has(tangents); }
        public bool HasUVs  { get => Has(uvs); }
        public bool HasUVs2 { get => Has(uvs2); }
        public bool HasUVs3 { get => Has(uvs3); }
        public bool HasUVs4 { get => Has(uvs4); }

        public bool HasBuffer(Buffer flag)
        {
            switch (flag)
            {
                case Buffer.Vertex: return HasVertices;
                case Buffer.Triangle: return HasTriangles;
                case Buffer.Normal: return HasNormals;
                case Buffer.Color: return HasColors;
                case Buffer.Tangent: return HasTangents;
                case Buffer.UV: return HasUVs;
                case Buffer.UV2: return HasUVs2;
                case Buffer.UV3: return HasUVs3;
                case Buffer.UV4: return HasUVs4;
                default:
                    Debug.LogError("Case not handled!");
                    break;
            }
            return false;
        }

        public uint BufferFlags
        {
            get
            {
                uint res = 0;
                res |= HasVertices ? (uint)Buffer.Vertex : 0;
                res |= HasTriangles ? (uint)Buffer.Triangle : 0;
                res |= HasNormals ? (uint)Buffer.Normal : 0;
                res |= HasColors ? (uint)Buffer.Color : 0;
                res |= HasTangents ? (uint)Buffer.Tangent : 0;
                res |= HasUVs ? (uint)Buffer.UV : 0;
                res |= HasUVs2 ? (uint)Buffer.UV2 : 0;
                res |= HasUVs3 ? (uint)Buffer.UV3 : 0;
                res |= HasUVs4 ? (uint)Buffer.UV4 : 0;
                return res;
            }
        }

        public int SubmeshCount { get => submeshTriangleOffsets.IsCreated ? submeshTriangleOffsets.Length : 0; }

        private bool Has<T>(NativeArray<T> array) where T: struct { return array.IsCreated; }

        public int GetSubmeshTriangleOffset(int submesh)
        {
            if (IsInBounds(submesh, submeshTriangleOffsets))
            {
                return submeshTriangleOffsets[submesh].index;
            }
            return 0;
        }

        public int GetSubmeshTriangleLength(int submesh)
        {
            if (IsInBounds(submesh, submeshTriangleOffsets))
            {
                return submeshTriangleOffsets[submesh].length;
            }
            return 0;
        }

        public bool CopySubmeshTriangles(int submesh, NativeArray<int> dstBuffer)
        {
            if (IsInBounds(submesh, submeshTriangleOffsets))
            {
                int offset = submeshTriangleOffsets[submesh].index;
                int length = submeshTriangleOffsets[submesh].length;
                if (dstBuffer.Length >= length)
                {
                    NativeArray<int>.Copy(triangles, (int)offset, dstBuffer, 0, (int)length);
                }
                else
                {
                    Debug.LogError("buffer is too small for copy");
                }
            }
            else
            {
                Debug.LogError("submesh index is out of bounds");
            }

            return false;
        }

        public bool CopySubmeshTriangles(int submesh, int[] dstBuffer)
        {
            if (IsInBounds(submesh, submeshTriangleOffsets))
            {
                int offset = submeshTriangleOffsets[submesh].index;
                int length = submeshTriangleOffsets[submesh].length;
                if (dstBuffer.Length >= length)
                {
                    NativeArray<int>.Copy(triangles, offset, dstBuffer, 0, length);
                }
                else
                {
                    Debug.LogError("buffer is too small for copy");
                }
            }
            else
            {
                Debug.LogError("submesh index is out of bounds");
            }

            return false;
        }

        private static bool IsInBounds<T>(int index, NativeArray<T> buffer) where T : struct
        {
            return (buffer.IsCreated && index >= 0 && index < buffer.Length);
        }

        static public Vector2[] ToVector2Array(NativeArray<float2> array)
        {
            var data = new Vector2Converter { Float2Array = array.ToArray() };
            return data.Vector2Array;
        }

        static public Vector3[] ToVector3Array(NativeArray<float3> array)
        {
            var data = new Vector3Converter { Float3Array = array.ToArray() };
            return data.Vector3Array;
        }

        static public Vector4[] ToVector4Array(NativeArray<float4> array)
        {
            var data = new Vector4Converter { Float4Array = array.ToArray() };
            return data.Vector4Array;
        }

        static public float2[] ToFloat2Array(Vector2[] array)
        {
            var data = new Vector2Converter { Vector2Array = array };
            return data.Float2Array;
        }

        static public float3[] ToFloat3Array(Vector3[] array)
        {
            var data = new Vector3Converter { Vector3Array = array };
            return data.Float3Array;
        }

        static public float4[] ToFloat4Array(Vector4[] array)
        {
            var data = new Vector4Converter { Vector4Array = array };
            return data.Float4Array;
        }

        static public void UpdateMesh(Mesh mesh, NativeArray<float3> vertices, NativeArray<int> tris, NativeArray<float2> uvs)
        {
            mesh.Clear();

            var verticesData = new Vector3Converter { Float3Array = vertices.ToArray() };
            var triArray = tris.ToArray();
            var uvData = new Vector2Converter { Float2Array = uvs.ToArray() };

            mesh.vertices = verticesData.Vector3Array;
            mesh.triangles = triArray;
            mesh.uv = uvData.Vector2Array;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
        
        static public bool HasFlag(uint value, Buffer flag) { return ((value & (byte)flag) != 0); }
        static public bool HasFlag(uint value, uint flag) { return (value & flag) != 0; }

        static public uint CombineFlags(params Buffer[] flags)
        {
            uint res = 0;
            for (int i = 0; i < flags.Length; ++i)
            {
                res |= (uint)flags[i];
            }
            return res;
        }

        static public uint GetMeshFlags(Mesh mesh)
        {
            uint bufferFlags = 0;
            bufferFlags |= BufferFlag(mesh.vertices, Buffer.Vertex);
            bufferFlags |= BufferFlag(mesh.triangles, Buffer.Triangle);
            bufferFlags |= BufferFlag(mesh.colors, Buffer.Color);
            bufferFlags |= BufferFlag(mesh.normals, Buffer.Normal);
            bufferFlags |= BufferFlag(mesh.tangents, Buffer.Tangent);
            bufferFlags |= BufferFlag(mesh.uv, Buffer.UV);
            bufferFlags |= BufferFlag(mesh.uv2, Buffer.UV2);
            bufferFlags |= BufferFlag(mesh.uv3, Buffer.UV3);
            bufferFlags |= BufferFlag(mesh.uv4, Buffer.UV4);

            bool Has<T>(T[] a) { return a != null && a.Length > 0; }
            uint BufferFlag<T>(T[] a, Buffer flag) { return Has(a) ? (uint)flag : 0; }

            return bufferFlags;
        }

        // TODO: check back later, newer versions of Unity might allow direct use of math versions
        // of data types making these converter classes superflous
        [StructLayout(LayoutKind.Explicit)]
        public struct Vector4Converter
        {
            [FieldOffset(0)]
            public float4[] Float4Array;

            [FieldOffset(0)]
            public Vector4[] Vector4Array;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct Vector3Converter
        {
            [FieldOffset(0)]
            public float3[] Float3Array;

            [FieldOffset(0)]
            public Vector3[] Vector3Array;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct Vector2Converter
        {
            [FieldOffset(0)]
            public float2[] Float2Array;

            [FieldOffset(0)]
            public Vector2[] Vector2Array;
        }
    }
}
