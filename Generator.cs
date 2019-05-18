using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;

namespace MeshBuilder
{
    public abstract class Generator : System.IDisposable
    {
        protected enum GeneratorState { Uninitialized, Initialized, Generating }
        protected GeneratorState State { get; private set; }

        protected JobHandle lastHandle;
        private List<System.IDisposable> temps = new List<System.IDisposable>();

        protected void Inited()
        {
            State = GeneratorState.Initialized;
        }

        public void Complete()
        {
            if (State == GeneratorState.Generating)
            {
                lastHandle.Complete();
                EndGeneration();
                State = GeneratorState.Initialized;
            }
            else
            {
                Debug.LogError("modifier can't complete, it wasn't generating");
            }

            DisposeTemps();
        }

        abstract protected JobHandle StartGeneration(JobHandle dependOn);

        abstract protected void EndGeneration();

        public bool IsInitialized { get { return State != GeneratorState.Uninitialized; } }
        public bool IsGenerating { get { return State == GeneratorState.Generating; } }

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
