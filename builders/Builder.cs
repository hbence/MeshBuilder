﻿using System.Collections.Generic;
using UnityEngine;
using Unity.Jobs;

namespace MeshBuilder
{
    public abstract class Builder : System.IDisposable
    {
        protected enum BuilderState { Uninitialized, Initialized, Generating }
        protected BuilderState State { get; private set; }

        protected JobHandle lastHandle;
        protected List<System.IDisposable> Temps { get; private set; } = new List<System.IDisposable>();

        protected void Inited()
        {
            State = BuilderState.Initialized;
        }

        public JobHandle Start(JobHandle dependOn = default)
        {
            if (State == BuilderState.Uninitialized)
            {
                Debug.LogError("builder was not inited!");
                return dependOn;
            }

            if (State == BuilderState.Generating)
            {
                Debug.LogError("builder is already generating!");
                return dependOn;
            }

            lastHandle = StartGeneration(dependOn);
            State = BuilderState.Generating;

            JobHandle.ScheduleBatchedJobs();

            return lastHandle;
        }

        public void Complete(Mesh mesh)
        {
            if (State == BuilderState.Generating)
            {
                lastHandle.Complete();
                EndGeneration(mesh);
                State = BuilderState.Initialized;
            }
            else
            {
                Debug.LogError("builder can't complete, it wasn't generating");
            }

            DisposeTemps();
        }

        abstract protected JobHandle StartGeneration(JobHandle dependOn);

        abstract protected void EndGeneration(Mesh mesh);

        public bool IsInitialized { get { return State != BuilderState.Uninitialized; } }
        public bool IsGenerating { get { return State == BuilderState.Generating; } }

        virtual public void Dispose()
        {
            State = BuilderState.Uninitialized;
            DisposeTemps();
        }

        protected void AddTemp(System.IDisposable temp)
        {
            Temps.Add(temp);
        }

        virtual protected void DisposeTemps()
        {
            foreach (var elem in Temps)
            {
                elem.Dispose();
            }
            Temps.Clear();
        }
    }
}
