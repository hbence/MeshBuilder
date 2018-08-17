using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[System.Serializable]
public struct Scale : IComponentData
{
    public float3 Value;
}
public class ScaleComponent : ComponentDataWrapper<Scale> { }

[UpdateAfter(typeof(TransformSystem))]
public class ScaleSystem : ComponentSystem
{
    public struct ScaleGroup
    {
        public readonly int Length;
        [ReadOnly] public ComponentDataArray<Scale> Scale;
        public ComponentDataArray<TransformMatrix> TransformMatrix;
        public SubtractiveComponent<VoidSystem<TransformSystem>> VoidSystemTransformSystem;
        public SubtractiveComponent<VoidSystem<ScaleSystem>> VoidSystemScaleSystem;
    }
    [Inject] public ScaleGroup Scale;

    protected override void OnUpdate()
    {
        for (int i = 0; i < Scale.Length; i++)
        {
            var scale = Scale.Scale[i].Value;
            Scale.TransformMatrix[i] = new TransformMatrix { Value = math.mul(Scale.TransformMatrix[i].Value, new float4x4(scale.x, 0, 0, 0, 0, scale.y, 0, 0, 0, 0, scale.z, 0, 0, 0, 0, 1)) };
        }
    }
}
