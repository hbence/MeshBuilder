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
    public class SmallObjectAddRemoveRendererBarrier : BarrierSystem { }

    [UpdateBefore(typeof(SmallObjectAddRemoveRendererBarrier))]
    public class SmallObjectChangeRendererBarrier : BarrierSystem { }

    public class SmallObjectRenderSystem : JobComponentSystem
    {
        struct NotShownGroup
        {
            public readonly int Length;
            [ReadOnly] public EntityArray entity;
            [ReadOnly] public SharedComponentDataArray<SmallObject> smallObject;
            [ReadOnly] public ComponentDataArray<CurrentLODLevel> curLod;
            [ReadOnly] public ComponentDataArray<MaxLODLevel> maxLod;
            [ReadOnly] public ComponentDataArray<Position> position;

            SubtractiveComponent<MeshInstanceRenderer> renderer;
        }

        struct ShownGroup
        {
            public readonly int Length;
            public EntityArray entity;
            [ReadOnly] public SharedComponentDataArray<SmallObject> smallObject;
            [ReadOnly] public ComponentDataArray<CurrentLODLevel> curLod;
            [ReadOnly] public ComponentDataArray<MaxLODLevel> maxLod;
            [ReadOnly] public ComponentDataArray<Position> position;
            [ReadOnly] public SharedComponentDataArray<MeshInstanceRenderer> renderer;
        }

        [Inject] private SmallObjectAddRemoveRendererBarrier addRemoveBarrier;
        [Inject] private SmallObjectChangeRendererBarrier changeBarrier;
        [Inject] private NotShownGroup notShownGroup;
        [Inject] private ShownGroup shownGroup;

        private ComponentGroup all;
        private List<SmallObject> uniqueSmallObjects;

        private JobHandle lastHandle;

        protected override void OnCreateManager(int capacity)
        {
            uniqueSmallObjects = new List<SmallObject>();

            all = GetComponentGroup(typeof(SmallObjectParent),
                                            typeof(SmallObject),
                                            typeof(Position),
                                            typeof(CurrentLODLevel),
                                            typeof(MaxLODLevel));
        }

        protected override void OnDestroyManager()
        {
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var cam = Camera.main.transform;
            float3 lodCenter = cam.position;

            var addRemoveBuffer = addRemoveBarrier.CreateCommandBuffer();
            var changeBuffer = changeBarrier.CreateCommandBuffer();

            EntityManager.GetAllUniqueSharedComponentDatas(uniqueSmallObjects);

            for (int objIndex = 0; objIndex < uniqueSmallObjects.Count; ++objIndex)
            {
                var smallObj = uniqueSmallObjects[objIndex];
                all.SetFilter(smallObj);

                var updateJob = new UpdateLODLevel
                {
                    camPosition = lodCenter,
                    lod0 = LODRange.Create(smallObj, 0),
                    lod1 = LODRange.Create(smallObj, 1),
                    lod2 = LODRange.Create(smallObj, 2),

                    positionArray = all.GetComponentDataArray<Position>(),
                    curLodArray = all.GetComponentDataArray<CurrentLODLevel>(),
                    entityArray = all.GetEntityArray(),
                    commandBuffer = changeBuffer
                };

                int length = all.CalculateLength();
                updateJob.Run(length);
                //    lastHandle = initJob.Schedule(length, 128, lastHandle);
                //    lastHandle.Complete();
            }

            UpdateRenderers(shownGroup.Length,
                shownGroup.entity,
                shownGroup.smallObject,
                shownGroup.renderer,
                shownGroup.curLod,
                shownGroup.maxLod,
                changeBuffer,
                addRemoveBuffer);

            AddRenderers(notShownGroup.Length,
                notShownGroup.entity,
                notShownGroup.smallObject,
                notShownGroup.curLod,
                notShownGroup.maxLod,
                addRemoveBuffer);

            return inputDeps;
        }

        /*
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var cam = Camera.main.transform;
            float3 lodCenter = cam.position;

            lastHandle.Complete();
            lastHandle = inputDeps;

            EntityManager.GetAllUniqueSharedComponentDatas(uniqueSmallObjects);

            for (int objIndex = 0; objIndex < uniqueSmallObjects.Count; ++objIndex)
            {
                var smallObj = uniqueSmallObjects[objIndex];
                all.SetFilter(smallObj);

                var initJob = new UpdateLODLevel
                {
                    camPosition = lodCenter,
                    lod0 = LODRange.Create(smallObj, 0),
                    lod1 = LODRange.Create(smallObj, 1),
                    lod2 = LODRange.Create(smallObj, 2),

                    positionArray = all.GetComponentDataArray<Position>(),
                    curLodArray = all.GetComponentDataArray<CurrentLODLevel>(),
                    entityArray = all.GetEntityArray(),
                    commandBuffer = changeBarrier.CreateCommandBuffer()
                };

                int length = all.CalculateLength();
                initJob.Run(length);
            //    lastHandle = initJob.Schedule(length, 128, lastHandle);
            //    lastHandle.Complete();
            }

            var addRemoveBuffer = addRemoveBarrier.CreateCommandBuffer();
            var changeBuffer = changeBarrier.CreateCommandBuffer();

            UpdateRenderers(shownGroup.Length,
                shownGroup.entity,
                shownGroup.smallObject,
                shownGroup.renderer,
                shownGroup.curLod,
                shownGroup.maxLod,
                changeBuffer,
                addRemoveBuffer);

            AddRenderers(notShownGroup.Length, 
                notShownGroup.entity, 
                notShownGroup.smallObject, 
                notShownGroup.curLod, 
                notShownGroup.maxLod, 
                addRemoveBuffer);

            JobHandle.ScheduleBatchedJobs();

            return lastHandle;
        }
        */

        private void UpdateRenderers(int length, EntityArray entity, SharedComponentDataArray<SmallObject> obj, SharedComponentDataArray<MeshInstanceRenderer> renderers, ComponentDataArray<CurrentLODLevel> currentLod, ComponentDataArray<MaxLODLevel> maxLod, EntityCommandBuffer changeBuffer, EntityCommandBuffer addRemoveBuffer)
        {
            for (int i = 0; i < length; ++i)
            {
                if (!currentLod[i].HasChanged)
                {
                    continue;
                }

                int curLod = currentLod[i].Value;
                SmallObject smallObj = obj[i];
                if (curLod <= maxLod[i].Value)
                {
                    switch (curLod)
                    {
                        case 0: UpdateRenderer(entity[i], renderers[i], smallObj.lod0.renderer, changeBuffer, addRemoveBuffer); break;
                        case 1: UpdateRenderer(entity[i], renderers[i], smallObj.lod1.renderer, changeBuffer, addRemoveBuffer); break;
                        case 2: UpdateRenderer(entity[i], renderers[i], smallObj.lod2.renderer, changeBuffer, addRemoveBuffer); break;
                        default:
                            {
                                Debug.Log("invalid lod level");
                                break;
                            }
                    }
                }
                else
                {
                 //   addRemoveBuffer.RemoveComponent<MeshInstanceRenderer>(entity[i]);
                }
            }
        }

        private void UpdateRenderer(Entity entity, MeshInstanceRenderer prev, MeshInstanceRenderer next, EntityCommandBuffer changeBuffer, EntityCommandBuffer addRemoveBuffer)
        {
            if (next.mesh != null)
            {
                changeBuffer.SetSharedComponent(next);
            }
            else
            {
          //      addRemoveBuffer.RemoveComponent<MeshInstanceRenderer>(entity);
            }
        }

        private void AddRenderers(int length, [ReadOnly] EntityArray entity, [ReadOnly] SharedComponentDataArray<SmallObject> obj, ComponentDataArray<CurrentLODLevel> currentLod, ComponentDataArray<MaxLODLevel> maxLod, EntityCommandBuffer commandBuffer)
        {
            for (int i = 0; i < length; ++i)
            {
                if (!currentLod[i].HasChanged)
                {
                    continue;
                }

                byte curLod = currentLod[i].Value;
                SmallObject smallObj = obj[i];
                if (curLod <= maxLod[i].Value)
                {
                    switch (curLod)
                    {
                        case 0: AddRenderer(entity[i], smallObj.lod0.renderer, commandBuffer); break;
                        case 1: AddRenderer(entity[i], smallObj.lod1.renderer, commandBuffer); break;
                        case 2: AddRenderer(entity[i], smallObj.lod2.renderer, commandBuffer); break;
                        default:
                        {
                            // this shouldn't happen, ever, but just in case
                            Debug.Log("invalid lod level");
                            break;
                        }
                    }
                }

                commandBuffer.SetComponent(entity[i], new CurrentLODLevel { Value = curLod, Changed = 0 } );
            }
        }

        private void AddRenderer(Entity entity, MeshInstanceRenderer next, EntityCommandBuffer commandBuffer)
        {
            if (next.mesh != null)
            {
                commandBuffer.AddSharedComponent(entity, next);
            }
        }

        [BurstCompile]
        struct UpdateLODLevel : IJobParallelFor
        {
            public float3 camPosition;
            public LODRange lod0;
            public LODRange lod1;
            public LODRange lod2;

            [ReadOnly] public ComponentDataArray<Position> positionArray;
            public ComponentDataArray<CurrentLODLevel> curLodArray;
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
                   // curLodArray[index] = new CurrentLODLevel { Value = selectedLod, Changed = 1 };
                    commandBuffer.SetComponent(entityArray[index],
                        new CurrentLODLevel
                        {
                            Value = selectedLod,
                            Changed = 1
                        });
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
}
