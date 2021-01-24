using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

namespace MeshBuilder
{
    public class CalculateNormals : Modifier
    {
        public void Init()
        {
            Inited();
        }

        protected override JobHandle StartGeneration(MeshData meshData, JobHandle dependOn)
            => Schedule(meshData, dependOn);

        protected override void EndGeneration()
        {

        }

        static public JobHandle ScheduleDeferred(NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, JobHandle dependOn = default)
        {
            dependOn = GenerateNormalsDeferred.Schedule(vertices, triangles, normals, dependOn);

            return dependOn;
        }

        static public JobHandle Schedule(NativeArray<float3> vertices, NativeArray<int> triangles, NativeArray<float3> normals, JobHandle dependOn = default)
        {
            NativeArray<NormalInfo> info = new NativeArray<NormalInfo>(triangles.Length, Allocator.TempJob);

            dependOn = CalculateNormalInfoJob.Schedule(vertices, triangles, info, dependOn);
            dependOn = GenerateNormals.Schedule(info, normals, dependOn);

            dependOn = info.Dispose(dependOn);

            return dependOn;
        }

        static public JobHandle Schedule(MeshData meshData, JobHandle dependOn = default)
        {
            if (!meshData.HasVertices || !meshData.HasNormals || !meshData.HasTriangles)
            {
                Debug.Log("CalculateNormals need vertices, normals and triangles in the meshdata, something is missing!");
                return dependOn;
            }

            return Schedule(meshData.Vertices, meshData.Triangles, meshData.Normals, dependOn);
        }

        static public void Calculate(MeshData meshData)
        {
            if (!meshData.HasVertices || !meshData.HasNormals || !meshData.HasTriangles)
            {
                Debug.Log("CalculateNormals.CalculateImmediately need vertices, normals and triangles in the meshdata, something is missing!");
                return;
            }

            var info = new NativeArray<NormalInfo>(meshData.Triangles.Length, Allocator.Temp);

            var infoJob = new CalculateNormalInfoJob { vertices = meshData.Vertices, triangles = meshData.Triangles, info = info };
            for (int i = 0; i < meshData.Triangles.Length / 3; ++i)
            {
                infoJob.Execute(i);
            }

            var normalsJob = new GenerateNormals { normalInfo = info, normals = meshData.Normals };
            normalsJob.Execute();

            info.Dispose();
        }

        private struct NormalInfo
        {
            public int vertexIndex;
            public float3 normal;
        }

        [BurstCompile]
        private struct CalculateNormalInfoJob : IJobParallelFor
        {
            private const int BatchCount = 512;

            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<int> triangles;
            
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<NormalInfo> info;

            public void Execute(int index)
            {
                int start = index * 3;
                int aIndex = triangles[start];
                int bIndex = triangles[start + 1];
                int cIndex = triangles[start + 2];
                float3 a = vertices[aIndex];
                float3 b = vertices[bIndex];
                float3 c = vertices[cIndex];
                float3 normal = CalcNormal(a, b, c);
                info[start] = new NormalInfo { vertexIndex = aIndex, normal = normal };
                info[start + 1] = new NormalInfo { vertexIndex = bIndex, normal = normal };
                info[start + 2] = new NormalInfo { vertexIndex = cIndex, normal = normal };
            }

            static private float3 CalcNormal(float3 a, float3 b, float3 c)
            {
                float3 ba = b - a;
                float3 ca = c - a;
                float3 normal = math.cross(ba, ca);
                return normal;
            }

            static public JobHandle Schedule(NativeArray<float3> vertices, NativeArray<int> tris, NativeArray<NormalInfo> info, JobHandle dependOn = default)
            {
                var job = new CalculateNormalInfoJob { vertices = vertices, triangles = tris, info = info };
                return job.Schedule(tris.Length / 3, BatchCount, dependOn);
            }
        }

        [BurstCompile]
        private struct GenerateNormals : IJob
        {
            [ReadOnly] public NativeArray<NormalInfo> normalInfo;
            public NativeArray<float3> normals;

            public void Execute()
            {
                for (int i = 0; i < normals.Length; ++i)
                {
                    normals[i] = float3.zero;
                }

                for (int i = 0; i < normalInfo.Length; ++i)
                {
                    var info = normalInfo[i];
                    normals[info.vertexIndex] += info.normal;
                }

                for (int i = 0; i < normals.Length; ++i)
                {
                    normals[i] = math.normalize(normals[i]);
                }
            }

            static public JobHandle Schedule(NativeArray<NormalInfo> normalInfo, NativeArray<float3> normals, JobHandle dependOn = default)
            {
                var job = new GenerateNormals { normalInfo = normalInfo, normals = normals };
                return job.Schedule(dependOn);
            }
        }

        [BurstCompile]
        private struct GenerateNormalsDeferred : IJob
        {
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<int> triangles;

            public NativeArray<float3> normals;

            public void Execute()
            {
                var info = new NativeArray<NormalInfo>(triangles.Length, Allocator.Temp);

                var infoJob = new CalculateNormalInfoJob { vertices = vertices, triangles = triangles, info = info };
                for (int i = 0; i < triangles.Length / 3; ++i)
                {
                    infoJob.Execute(i);
                }

                var normalsJob = new GenerateNormals { normalInfo = info, normals = normals };
                normalsJob.Execute();

                info.Dispose();
            }

            static public JobHandle Schedule(NativeList<float3> vertices, NativeList<int> triangles, NativeList<float3> normals, JobHandle dependOn = default)
            {
                var job = new GenerateNormalsDeferred { vertices = vertices.AsDeferredJobArray(), triangles = triangles.AsDeferredJobArray(), normals = normals.AsDeferredJobArray() };
                return job.Schedule(dependOn);
            }
        }
    }
}
