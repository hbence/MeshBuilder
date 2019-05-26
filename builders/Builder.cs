using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;

namespace MeshBuilder
{
    public abstract class Builder : IMeshBuilder
    {
        protected enum BuilderState { Uninitialized, Initialized, Generating }
        protected BuilderState State { get; private set; }

        private JobHandle lastHandle;
        private List<System.IDisposable> temps = new List<System.IDisposable>();

    //    public Mesh Mesh { get; private set; }
        public Mesh Mesh { get; set; }

        public Builder()
        {
            Mesh = new Mesh();
        }

        protected void Inited()
        {
            State = BuilderState.Initialized;
        }

        public JobHandle Start(JobHandle dependOn = default)
        {
            if (State == BuilderState.Uninitialized)
            {
                Debug.LogError("modifier was not inited!");
                return dependOn;
            }

            if (State == BuilderState.Generating)
            {
                Debug.LogError("modifier is already generating!");
                return dependOn;
            }

            lastHandle = StartGeneration(dependOn);
            State = BuilderState.Generating;

            JobHandle.ScheduleBatchedJobs();

            return lastHandle;
        }

        public void Complete()
        {
            if (State == BuilderState.Generating)
            {
                lastHandle.Complete();
                EndGeneration(Mesh);
                State = BuilderState.Initialized;
            }
            else
            {
                Debug.LogError("modifier can't complete, it wasn't generating");
            }

            DisposeTemps();
        }

        abstract protected JobHandle StartGeneration(JobHandle dependOn);

        abstract protected void EndGeneration(Mesh mesh);

        public bool IsInitialized { get { return State != BuilderState.Uninitialized; } }
        public bool IsGenerating { get { return State == BuilderState.Generating; } }

        virtual public void Dispose()
        {
            DisposeTemps();
        }

        protected void AddTemp(System.IDisposable temp)
        {
            temps.Add(temp);
        }

        virtual protected void DisposeTemps()
        {
            foreach (var elem in temps)
            {
                elem.Dispose();
            }
            temps.Clear();
        }
    }
}
