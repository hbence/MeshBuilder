using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Rendering;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using Unity.Transforms;
using Unity.Burst;

namespace MeshBuilder.SmallObject
{
    public class SOUpdateLODLevelSystem : JobComponentSystem
    {
        [Inject] SOUpdateLODBarrier barrier;

        private ComponentGroup group;
        private List<SmallObject> uniqueSmallObjects;

        private JobHandle lastHandle;

        protected override void OnCreateManager(int capacity)
        {
            uniqueSmallObjects = new List<SmallObject>();

            group = GetComponentGroup(typeof(SmallObjectParent),
                                            typeof(SmallObject),
                                            typeof(Position),
                                            typeof(CurrentLODLevel),
                                            typeof(MaxLODLevel));
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var cam = Camera.main.transform;
            float3 lodCenter = cam.position;

            var commandBuffer = barrier.CreateCommandBuffer();

            EntityManager.GetAllUniqueSharedComponentDatas(uniqueSmallObjects);

            lastHandle = inputDeps;

            for (int objIndex = 0; objIndex < uniqueSmallObjects.Count; ++objIndex)
            {
                var smallObj = uniqueSmallObjects[objIndex];
                group.SetFilter(smallObj);

                int length = group.CalculateLength();

                if (length > 0)
                {
                    var updateJob = new UpdateLODLevelJob2
                    {
                        camPosition = lodCenter,
                        lod0 = LODRange.Create(smallObj, 0),
                        lod1 = LODRange.Create(smallObj, 1),
                        lod2 = LODRange.Create(smallObj, 2),

                        positionArray = group.GetComponentDataArray<Position>(),
                        curLodArray = group.GetComponentDataArray<CurrentLODLevel>(),
                        entityArray = group.GetEntityArray(),
                        mgr = EntityManager
                    };

                    updateJob.Execute();
                  //  var handle = updateJob.Schedule(inputDeps);
                  //  var handle = updateJob.Schedule(length, 128, inputDeps);
                 //   lastHandle = JobHandle.CombineDependencies(lastHandle, handle);
                    //lastHandle.Complete();
                }
            }

            uniqueSmallObjects.Clear();

            return lastHandle;
        }

        [BurstCompile]
        struct UpdateLODLevelJob : IJobParallelFor
        {
            public float3 camPosition;
            public LODRange lod0;
            public LODRange lod1;
            public LODRange lod2;

            [ReadOnly] public ComponentDataArray<Position> positionArray;
            [ReadOnly] public ComponentDataArray<CurrentLODLevel> curLodArray;
            [ReadOnly] public EntityArray entityArray;

            public EntityCommandBuffer.Concurrent commandBuffer;

            public void Execute(int index)
            {
                int curLod = curLodArray[index].Value;
                float distSq = math.lengthSquared(camPosition - positionArray[index].Value);

                byte selectedLod = 3;
                if (lod0.IsInRange(distSq))
                {
                    selectedLod = 0;
                }
                else if (lod1.IsInRange(distSq))
                {
                    selectedLod = 1;
                }
                else if (lod2.IsInRange(distSq))
                {
                    selectedLod = 2;
                }

                if (curLod != selectedLod)
                {
                    commandBuffer.SetComponent(entityArray[index],
                        new CurrentLODLevel
                        {
                            Value = selectedLod,
                            Changed = 1
                        });
                }
            }
        }

        [BurstCompile]
        struct UpdateLODLevelJob2 : IJob
        {
            public float3 camPosition;
            public LODRange lod0;
            public LODRange lod1;
            public LODRange lod2;

            [ReadOnly] public ComponentDataArray<Position> positionArray;
            [ReadOnly] public ComponentDataArray<CurrentLODLevel> curLodArray;
            [ReadOnly] public EntityArray entityArray;

            public EntityManager mgr;

            public void Execute()
            {
                for (int index = 0; index < entityArray.Length; ++index)
                {
                    int curLod = curLodArray[index].Value;
                    float distSq = math.lengthSquared(camPosition - positionArray[index].Value);

                    byte selectedLod = 3;
                    if (lod0.IsInRange(distSq))
                    {
                        selectedLod = 0;
                    }
                    else if (lod1.IsInRange(distSq))
                    {
                        selectedLod = 1;
                    }
                    else if (lod2.IsInRange(distSq))
                    {
                        selectedLod = 2;
                    }

                    if (curLod != selectedLod)
                    {
                        mgr.SetComponentData(entityArray[index], new CurrentLODLevel { Value = selectedLod, Changed = 1 });
                    }
                }
            }
        }

        private struct LODRange
        {
            public float minSq;
            public float maxSq;

            public bool IsInRange(float distanceSq) { return distanceSq >= minSq && distanceSq <= maxSq; }

            static public LODRange Create(SmallObject obj, int level)
            {
                float min = 0;
                float max = 0;
                switch (level)
                {
                    case 0:
                        {
                            min = obj.lod0.appearDistance;
                            max = obj.lod0.disappearDistance;
                            break;
                        }
                    case 1:
                        {
                            min = obj.lod1.appearDistance;
                            max = obj.lod1.disappearDistance;
                            break;
                        }
                    case 2:
                        {
                            min = obj.lod2.appearDistance;
                            max = obj.lod2.disappearDistance;
                            break;
                        }
                    default:
                        {
                            Debug.LogError("invalid LOD level");
                            break;
                        }
                }

                return new LODRange { minSq = min * min, maxSq = max * max };
            }
        }
    }

    [UpdateAfter(typeof(SOUpdateLODLevelSystem))]
    public class SOUpdateLODBarrier : BarrierSystem { }

    [UpdateAfter(typeof(SOUpdateLODBarrier))]
    public class SOUpdateNotRenderedSystem : ComponentSystem
    {
        private struct Group
        {
            public readonly int Length;
            [ReadOnly] public EntityArray entity;
            [ReadOnly] public SharedComponentDataArray<SmallObject> smallObject;
            [ReadOnly] public ComponentDataArray<CurrentLODLevel> curLod;
            [ReadOnly] public ComponentDataArray<MaxLODLevel> maxLod;
            [ReadOnly] public ComponentDataArray<Position> position;

            public SubtractiveComponent<MeshInstanceRenderer> renderer;
        }

        [Inject] Group group;
        [Inject] private SOPostRendererBarrier postBarrier;

        protected override void OnUpdate()
        {
            var changeBuffer = postBarrier.CreateCommandBuffer();
            
            for (int i = 0; i < group.Length; ++i)
            {
                if (!group.curLod[i].HasChanged)
                {
                    continue;
                }

                byte curLod = group.curLod[i].Value;
                SmallObject smallObj = group.smallObject[i];
                Entity entity = group.entity[i];

                if (curLod <= group.maxLod[i].Value)
                {
                    switch (curLod)
                    {
                        case 0: AddRenderer(entity, smallObj.lod0.renderer, changeBuffer); break;
                        case 1: AddRenderer(entity, smallObj.lod1.renderer, changeBuffer); break;
                        case 2: AddRenderer(entity, smallObj.lod2.renderer, changeBuffer); break;
                        default:
                            {
                                // this shouldn't happen, ever, but just in case
                                Debug.Log("invalid lod level");
                                break;
                            }
                    }
                }

                PostUpdateCommands.SetComponent(entity, new CurrentLODLevel { Value = curLod, Changed = 0 });
            }
        }

        private void AddRenderer(Entity entity, MeshInstanceRenderer next, EntityCommandBuffer commandBuffer)
        {
            if (next.mesh != null)
            {
               PostUpdateCommands.AddSharedComponent(entity, next);
            }
        }
    }

    [UpdateAfter(typeof(SOUpdateNotRenderedSystem))]
    public class SOUpdateRenderedSystem : JobComponentSystem
    {
        struct Group
        {
            public readonly int Length;
            public EntityArray entity;
            [ReadOnly] public SharedComponentDataArray<SmallObject> smallObject;
            [ReadOnly] public ComponentDataArray<CurrentLODLevel> curLod;
            [ReadOnly] public ComponentDataArray<MaxLODLevel> maxLod;
            [ReadOnly] public ComponentDataArray<Position> position;
            [ReadOnly] public SharedComponentDataArray<MeshInstanceRenderer> renderer;
        }

        [Inject] Group group;
        [Inject] private SOPostRendererBarrier postBarrier;

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var postBuffer = postBarrier.CreateCommandBuffer();
            
            for (int i = 0; i < group.Length; ++i)
            {
                if (!group.curLod[i].HasChanged)
                {
                    continue;
                }

                byte curLod = group.curLod[i].Value;
                SmallObject smallObj = group.smallObject[i];
                Entity entity = group.entity[i];

                if (curLod <= group.maxLod[i].Value)
                {
                    switch (curLod)
                    {
                        case 0: UpdateRenderer(entity, smallObj.lod0.renderer, postBuffer, postBuffer); break;
                        case 1: UpdateRenderer(entity, smallObj.lod1.renderer, postBuffer, postBuffer); break;
                        case 2: UpdateRenderer(entity, smallObj.lod2.renderer, postBuffer, postBuffer); break;
                        default:
                            {
                                Debug.Log("invalid lod level");
                                break;
                            }
                    }
                }
                else
                {
                    postBuffer.RemoveComponent<MeshInstanceRenderer>(entity);
                }

                postBuffer.SetComponent(entity, new CurrentLODLevel { Value = curLod, Changed = 0 });
            }

            return inputDeps;
        }

        private void UpdateRenderer(Entity entity, MeshInstanceRenderer next, EntityCommandBuffer postBuffer, EntityCommandBuffer addRemoveBuffer)
        {
            if (next.mesh != null)
            {
                postBuffer.SetSharedComponent(entity, next);
            }
            else
            {
                postBuffer.RemoveComponent<MeshInstanceRenderer>(entity);
            }
        }
    }

    [UpdateAfter(typeof(SOUpdateRenderedSystem))]
    public class SOPostRendererBarrier: BarrierSystem { }
}
