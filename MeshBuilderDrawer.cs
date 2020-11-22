using UnityEngine;
using Unity.Jobs;

namespace MeshBuilder
{
    [System.Serializable]
    public class MeshBuilderDrawer : MeshDrawer, System.IDisposable
    {
        public Builder MeshBuilder { get; private set; }
        public T Get<T>() where T : Builder { return MeshBuilder as T; }

        public MeshBuilderDrawer(RenderInfo info, Builder builder)
            : base(info)
        {
            MeshBuilder = builder;
            Mesh = new Mesh();
        }

        public void StartBuilder(JobHandle dependOn = default)
        {
            if (MeshBuilder.IsInitialized)
            {
                MeshBuilder.Start(dependOn);
            }
            else
            {
                Debug.LogError("MeshBuilder was not initialized!");
            }
        }

        public void CompleteBuilder()
        {
            if (MeshBuilder.IsGenerating)
            {
                MeshBuilder.Complete(Mesh);
            }
            else
            {
                Debug.LogWarning("MeshBuilder was not generating!");
            }
        }

        public bool IsBuilderGenerating { get => MeshBuilder.IsGenerating; }

        public void Dispose()
        {
            MeshBuilder.Dispose();
        }

        static public MeshBuilderDrawer Create<T>(RenderInfo info) where T : Builder, new()
        {
            return new MeshBuilderDrawer(info, new T());
        }
    }
}
