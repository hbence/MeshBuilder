using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;
using Unity.Collections;

namespace MeshBuilder
{
    public abstract class Modifier : System.IDisposable
    {
        protected enum ModifierStart { Uninitialized, Initialized, Generating }
        protected ModifierStart State { get; private set; }

        protected JobHandle lastHandle;
        private List<System.IDisposable> temps = new List<System.IDisposable>();

        protected void Inited()
        {
            State = ModifierStart.Initialized;
        }

        public JobHandle Start(MeshData meshData, JobHandle dependOn = default)
        {
            if (State == ModifierStart.Uninitialized)
            {
                Debug.LogError("modifier was not inited!");
                DisposeTemps();
                return dependOn;
            }

            if (State == ModifierStart.Generating)
            {
                Debug.LogError("modifier is already generating!");
                DisposeTemps();
                return dependOn;
            }

            lastHandle = StartGeneration(meshData, dependOn);
            State = ModifierStart.Generating;

            JobHandle.ScheduleBatchedJobs();

            return lastHandle;
        }

        public void Complete()
        {
            if (State == ModifierStart.Generating)
            {
                lastHandle.Complete();
                EndGeneration();

                State = ModifierStart.Initialized;
            }
            else
            {
                Debug.LogError("modifier can't complete, it wasn't generating");
            }

            DisposeTemps();
        }

        abstract protected JobHandle StartGeneration(MeshData meshData, JobHandle dependOn);

        abstract protected void EndGeneration();

        public bool IsInitialized { get { return State != ModifierStart.Uninitialized; } }
        public bool IsGenerating { get { return State == ModifierStart.Generating; } }

        virtual public void Dispose()
        {
            DisposeTemps();
        }

        protected void AddTemp(System.IDisposable temp)
        {
            temps.Add(temp);
        }

        protected void DisposeTemps()
        {
            foreach (var elem in temps)
            {
                elem.Dispose();
            }
            temps.Clear();
        }
    }
}
