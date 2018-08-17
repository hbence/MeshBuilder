using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Unity.Entities;
using Unity.Collections;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Burst;

using Random = UnityEngine.Random;
using Unity.Rendering;

namespace MeshBuilder.SmallObject
{
    public struct SmallObjectParent : ISharedComponentData
    {
        public TargetObject parent;
    }

    public struct MaxLODLevel : IComponentData
    {
        public byte Value;
    }

    public struct CurrentLODLevel : IComponentData
    {
        public byte Value;
    }

    public struct LODLevelChanged : IComponentData { }

    public class RootSpawnBarrier : BarrierSystem { }

    [UpdateBefore(typeof(RootSpawnBarrier))]
    public class SmallObjectPlacementSystem : JobComponentSystem
    {
        // these need to be inited
        struct UnprocessedTargetData
        {
            public readonly int Length;
            public EntityArray entity;
            [ReadOnly] public SharedComponentDataArray<TargetObject> target;
            [ReadOnly] public ComponentDataArray<Position> pos;
            [ReadOnly] public ComponentDataArray<Rotation> rot;

            public SubtractiveComponent<TargetObjectCompletelyProcessed> completelyProcessed;
            public SubtractiveComponent<TargetObjectPartiallyProcessed> partiallyProcessed;
        }

        // these need to be checked if active bounds change
        struct PartiallyProcessedTargetData
        {
            public readonly int Length;
            [ReadOnly] public EntityArray entity;
            [ReadOnly] public ComponentDataArray<TargetObjectPartiallyProcessed> processed;
            [ReadOnly] public SharedComponentDataArray<TargetObject> data;
            [ReadOnly] public ComponentDataArray<TransformMatrix> transform;

            public SubtractiveComponent<TargetObjectCompletelyProcessed> completelyProcessed;
        }

        [Inject] private RootSpawnBarrier spawnBarrier;
        [Inject] private UnprocessedTargetData unprocessedTargets;

        private JobHandle lastHandle;
        private List<IDisposable> tempArrays;
        private EntityArchetype smallObjRootArcheTypeLocal;
        private EntityArchetype smallObjRootArcheTypeGlobal;

        private NativeArray<float> randomArray;

        protected override void OnCreateManager(int capacity)
        {
            base.OnCreateManager(capacity);

            randomArray = new NativeArray<float>(4000, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < randomArray.Length; ++i)
            {
                randomArray[i] = Random.value;
            }

            tempArrays = new List<IDisposable>();

            smallObjRootArcheTypeLocal = EntityManager.CreateArchetype(
                typeof(MaxLODLevel),
                typeof(CurrentLODLevel),
                typeof(LODLevelChanged),
                typeof(TransformMatrix),
                typeof(TransformParent),
                typeof(LocalPosition),
                typeof(LocalRotation),
                typeof(Position),
                typeof(Rotation),
                typeof(Scale)
            );

            smallObjRootArcheTypeGlobal = EntityManager.CreateArchetype(
                typeof(MaxLODLevel),
                typeof(CurrentLODLevel),
                typeof(LODLevelChanged),
                typeof(TransformMatrix),
                typeof(Position),
                typeof(Rotation),
                typeof(Scale)
            );
        }

        protected override void OnDestroyManager()
        {
            lastHandle.Complete();
            DisposeTempArrays();
            randomArray.Dispose();

            base.OnDestroyManager();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            lastHandle = inputDeps;
            if (unprocessedTargets.Length > 0)
            {
                for (int i = 0; i < unprocessedTargets.Length; ++i)
                {
                    if (unprocessedTargets.target[i].mesh != null && unprocessedTargets.target[i].mesh.vertexCount > 0)
                    {
                        Entity entity = unprocessedTargets.entity[i];
                        TargetObject target = unprocessedTargets.target[i];

                        float3 pos = unprocessedTargets.pos[i].Value;
                        quaternion rot = unprocessedTargets.rot[i].Value;
                        float4x4 transform = new float4x4(rot, pos);

                        var handle = ProcessTargetObject(entity, target, transform, inputDeps);
                        lastHandle = JobHandle.CombineDependencies(handle, lastHandle);

                        EntityManager.AddComponent(unprocessedTargets.entity[i], typeof(TargetObjectCompletelyProcessed));
                    }
                }
            }

            DisposeTempArrays();

            return lastHandle;
        }

        private void DisposeTempArrays()
        {
            if (tempArrays.Count > 0)
            {
                for (int i = 0; i < tempArrays.Count; ++i)
                {
                    tempArrays[i].Dispose();
                }
                tempArrays.Clear();
            }
        }

        private JobHandle ProcessTargetObject(Entity parentEntity, TargetObject target, float4x4 transform, JobHandle inputDeps)
        {
            if (target.smallObjects == null || target.smallObjects.Length == 0)
            {
                return inputDeps;
            }

            SmallObject smallObj = target.smallObjects[0];
            int objCount = smallObj.lod0.numberPerCell;

            Mesh mesh = target.mesh;
            Bounds bounds = target.mesh.bounds;

            NativeArray<float2> rayPositions = new NativeArray<float2>(objCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            tempArrays.Add(rayPositions);
            
            // Step 1 A
            // generate the positions where we will check for collisions
            var posHandle = Jobs.ScheduleRandomPositionsGen(rayPositions, bounds, randomArray, inputDeps);

            NativeArray<byte> lodClasses = new NativeArray<byte>(objCount, Allocator.Temp, NativeArrayOptions.ClearMemory);
            tempArrays.Add(lodClasses);

            NativeArray<int> meshTriangles = new NativeArray<int>(mesh.triangles, Allocator.Temp);
            tempArrays.Add(meshTriangles);
            int triangleCount = mesh.triangles.Length / 3;

            MeshData.VectorConverter convert = new MeshData.VectorConverter();
            convert.Vector3Array = mesh.vertices;
            NativeArray<float3> meshVertices = new NativeArray<float3>(convert.Float3Array, Allocator.Temp);
            tempArrays.Add(meshVertices);

            NativeArray<Bounds> triangleBounds = new NativeArray<Bounds>(triangleCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            tempArrays.Add(triangleBounds);

            // Step 1 B
            // optimization, go through the mesh and generate a bounding box for every triangle
            // checking the bounding box will be a first step for ray - triangle intersection
            var boundHandle = Jobs.ScheduleTriangleBoundGen(triangleBounds, meshVertices, meshTriangles, inputDeps);
            inputDeps = JobHandle.CombineDependencies(posHandle, boundHandle);

            NativeArray<float3> resultPoints = new NativeArray<float3>(objCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            tempArrays.Add(resultPoints);

            // Step 2
            // check for ray - triangle intersection points
            inputDeps = Jobs.ScheduleTopDownIntersection(resultPoints, rayPositions, bounds.max.y + 1, meshVertices, meshTriangles, triangleBounds, inputDeps);

            // Step 2.5 
            // transform the positions to global space
            if (!target.localObjects)
            {
                inputDeps = Jobs.ScheduleTransformToGlobal(resultPoints, transform, inputDeps);
            }
            
            inputDeps.Complete();

            // Step 3
            // use the intersection points to generate the object roots
            var parent = new SmallObjectParent { parent = target };
            var commandBuffer = spawnBarrier.CreateCommandBuffer();

            if (target.localObjects)
            {
                for (int index = 0; index < resultPoints.Length; ++index)
                {
                    var pos = resultPoints[index];
                    if (!Equals(pos, Zero))
                    {
                        SpawnLocalEntity(commandBuffer, pos, parent, smallObj, parentEntity);
                    }
                }
            }
            else
            {
                for (int index = 0; index < resultPoints.Length; ++index)
                {
                    var pos = resultPoints[index];
                    if (!Equals(pos, Zero))
                    {
                        SpawnGlobalEntity(commandBuffer, pos, parent, smallObj, parentEntity);
                    }
                }
            }
            
            return inputDeps;
        }

        static private readonly float3 Zero = new float3(0, 0, 0);

        private void SpawnLocalEntity(EntityCommandBuffer commandBuffer, float3 pos, SmallObjectParent target, SmallObject smallObject, Entity parentEntity)
        {
            commandBuffer.CreateEntity(smallObjRootArcheTypeLocal);
            commandBuffer.SetComponent(new LocalPosition { Value = pos });
            commandBuffer.SetComponent(new MaxLODLevel { Value = 2 });
            commandBuffer.SetComponent(new CurrentLODLevel { Value = 0 });
            commandBuffer.SetComponent(new TransformParent { Value = parentEntity });
            commandBuffer.SetComponent(new Scale { Value = CalcObjScale(smallObject) });
            commandBuffer.AddSharedComponent(target);
            commandBuffer.AddSharedComponent(smallObject);
        }

        private void SpawnGlobalEntity(EntityCommandBuffer commandBuffer, float3 pos, SmallObjectParent target, SmallObject smallObject, Entity parentEntity)
        {
            commandBuffer.CreateEntity(smallObjRootArcheTypeGlobal);
            commandBuffer.SetComponent(new Position { Value = pos });
            commandBuffer.SetComponent(new MaxLODLevel { Value = 2 });
            commandBuffer.SetComponent(new CurrentLODLevel { Value = 0 });
            commandBuffer.SetComponent(new Scale { Value = CalcObjScale(smallObject) });
            commandBuffer.AddSharedComponent(target);
            commandBuffer.AddSharedComponent(smallObject);
        }

        static private Vector3 CalcObjScale(SmallObject obj)
        {
            return Vector3.Lerp(obj.minScale, obj.maxScale, Random.value);
        }

        static private Vector3 CalcObjRotation(SmallObject obj)
        {
            return Vector3.Lerp(obj.minRotation, obj.maxRotation, Random.value);
        }

        static private Vector3 CalcObjOffset(SmallObject obj)
        {
            return Vector3.Lerp(obj.minOffset, obj.maxOffset, Random.value);
        }

        /// <summary>
        /// every small object root is available for lod0
        /// when the camera gets further and small objects change lod levels, 
        /// the roots which have a smaller max lod level than the current required will disappear
        /// 
        /// NOTE: 
        /// - this job classifies every root with a lod level, for now it just simply changes X random
        /// lod levels, so there is a possibility of earlier values getting overwritten
        /// - also, when the ray generation gets more sophisticated, perhaps it will make sense to choose
        /// lod levels based on a system (so high lod levels dispersed through the whole area and not
        /// clumped together)
        /// </summary>
        [BurstCompile]
        private struct LODClassification : IJob
        {
            public int lod1Count;
            public int lod2Count;

            [WriteOnly] public NativeArray<byte> lodLevels;

            public void Execute()
            {
                lod1Count = math.max(0, lod1Count - lod2Count);
                for (int i = 0; i < lod2Count; ++i)
                {
                    int index = Random.Range(0, lodLevels.Length);
                    lodLevels[index] = 2;
                }
                for (int i = 0; i < lod1Count; ++i)
                {
                    int index = Random.Range(0, lodLevels.Length);
                    lodLevels[index] = 1;
                }
            }
        }
    }

    internal class Jobs
    {
        [BurstCompile]
        internal struct GenerateRandomPositionsXZ : IJobParallelFor
        {
            public Bounds bounds;
            public int randomOffset;
            [ReadOnly] public NativeArray<float> randomArray;
            [WriteOnly] public NativeArray<float2> results;

            public void Execute(int index)
            {
                int r = (randomOffset + index) % (randomArray.Length - 1);
                results[index] = new float2
                {
                    x = math.lerp(bounds.min.x, bounds.max.x, randomArray[r]),
                    y = math.lerp(bounds.min.z, bounds.max.z, randomArray[r + 1])
                };
            }
        }

        internal static JobHandle ScheduleRandomPositionsGen(NativeArray<float2> results, Bounds bounds, NativeArray<float> randomArray, JobHandle inputDeps)
        {
            var generateRayPositionsJob = new GenerateRandomPositionsXZ
            {
                bounds = bounds,
                randomArray = randomArray,
                randomOffset = Random.Range(0, randomArray.Length),
                results = results
            };

            return generateRayPositionsJob.Schedule(results.Length, 128, inputDeps);
        }

        [BurstCompile]
        internal struct GenerateTriangleBounds : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<float3> vertices;

            [WriteOnly] public NativeArray<Bounds> triangleBounds;

            public void Execute(int index)
            {
                int tri = index * 3;
                int i0 = triangles[tri];
                int i1 = triangles[tri + 1];
                int i2 = triangles[tri + 2];
                float3 min = math.min(vertices[i0], vertices[i1]);
                min = math.min(min, vertices[i2]);
                float3 max = math.max(vertices[i0], vertices[i1]);
                max = math.max(max, vertices[i2]);

                var bounds = new Bounds { };
                bounds.SetMinMax(min, max);
                triangleBounds[index] = bounds;
            }
        }

        internal static JobHandle ScheduleTriangleBoundGen(NativeArray<Bounds> results, NativeArray<float3> vertices, NativeArray<int> triangles, JobHandle inputDeps)
        {
            var generateTriBound = new GenerateTriangleBounds
            {
                triangles = triangles,
                vertices = vertices,
                triangleBounds = results
            };

            return generateTriBound.Schedule(results.Length, 64, inputDeps);
        }

        static private readonly float3 Zero = new float3(0, 0, 0);
        static private readonly float3 Down = new float3(0, -1, 0);

        [BurstCompile]
        internal struct TopDownIntersection : IJobParallelFor
        {
            public float rayOriginY;

            [ReadOnly] public NativeArray<float2> positions;

            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<Bounds> triangleBounds;

            [WriteOnly] public NativeArray<float3> results;

            public void Execute(int index)
            {
                float3 res = Zero;
                float3 pos = new float3(positions[index].x, rayOriginY, positions[index].y);

                for (int i = 0; i < triangleBounds.Length; ++i)
                {
                    if (IsOverBounds(pos.x, pos.z, triangleBounds[i]))
                    {
                        int triInd = i * 3;
                        int tri0 = triangles[triInd];
                        int tri1 = triangles[triInd + 1];
                        int tri2 = triangles[triInd + 2];
                        if (DoesIntersect(pos, Down, vertices[tri0], vertices[tri1], vertices[tri2], out res))
                        {
                            break;
                        }
                    }
                }

                results[index] = res;
            }
        }

        static private bool Equals(float3 a, float3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }

        // ray / bounds intersection check for top down ray
        static private bool IsOverBounds(float x, float z, Bounds bounds)
        {
            return x >= bounds.min.x && x <= bounds.max.x && z >= bounds.min.z && z <= bounds.max.z;
        }

        // Möller–Trumbore intersection
        static private bool DoesIntersect(float3 origin, float3 dir, float3 v0, float3 v1, float3 v2, out float3 intersection)
        {
            intersection = Zero;

            float3 edge1 = v1 - v0;
            float3 edge2 = v2 - v0;
            float3 h = math.cross(dir, edge2);
            float a = math.dot(h, edge1);
            if (a > -math.epsilon_normal && a < math.epsilon_normal)
            {
                return false;
            }

            float f = 1f / a;
            float3 s = origin - v0;
            float u = f * math.dot(s, h);
            if (u < 0f || u > 1f)
            {
                return false;
            }

            float3 q = math.cross(s, edge1);
            float v = f * math.dot(dir, q);
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            float t = f * math.dot(edge2, q);
            if (t > math.epsilon_normal)
            {
                intersection = origin + dir * t;
                return true;
            }

            return false;
        }

        static internal JobHandle ScheduleTopDownIntersection(NativeArray<float3> results, NativeArray<float2> rayOriginPositions, float rayOriginY, NativeArray<float3> meshVertices, NativeArray<int> meshTriangles, NativeArray<Bounds> triBounds, JobHandle inputDeps)
        {
            var topdownIntersections = new TopDownIntersection
            {
                rayOriginY = rayOriginY,
                positions = rayOriginPositions,

                triangles = meshTriangles,
                vertices = meshVertices,
                triangleBounds = triBounds,

                results = results
            };

            return topdownIntersections.Schedule(results.Length, 32, inputDeps);
        }

        internal struct TransformToGlobal : IJobParallelFor
        {
            public float4x4 rootTransform;
            public NativeArray<float3> positions;

            public void Execute(int index)
            {
                float4 p = new float4(positions[index], 1);
                p = math.mul(rootTransform, p);
                positions[index] = new float3 { x = p.x, y = p.y, z = p.z };
            }
        }

        internal static JobHandle ScheduleTransformToGlobal(NativeArray<float3> results, float4x4 transform, JobHandle inputDeps)
        {
            var toGlobal = new TransformToGlobal
            {
                rootTransform = transform,
                positions = results
            };
            return toGlobal.Schedule(results.Length, 64, inputDeps);
        }
    }
}
