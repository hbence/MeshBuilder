using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using System.Collections.Generic;
using UnityEngine;


namespace MeshBuilder
{
    abstract public class TileMesherBase<TileVariant> : IMeshBuilder where TileVariant : struct
    {
        protected enum State { Uninitialized, Initialized, Generating }
        protected enum GenerationType { FromDataUncached, FromDataCachedTiles, FromTiles }

        protected string name;
        protected State state = State.Uninitialized;
        protected GenerationType generationType = GenerationType.FromDataUncached;

        // GENERATED DATA
        protected Volume<TileVariant> tiles;
        protected JobHandle lastHandle;

        public Mesh Mesh { get; protected set; }

        public void StartGeneration()
        {
            if (!IsInitialized)
            {
                Error("not initialized!");
                return;
            }

            if (IsGenerating)
            {
                Error("is already generating!");
                return;
            }

            state = State.Generating;

            lastHandle.Complete();
            DisposeTemp();

            ScheduleGenerationJobs();
        }

        abstract protected void ScheduleGenerationJobs();

        public void EndGeneration()
        {
            if (!IsGenerating)
            {
                Warning("is not generating! nothing to stop");
                return;
            }

            lastHandle.Complete();
            state = State.Initialized;

            AfterGenerationJobsComplete();

            if (generationType == GenerationType.FromDataUncached)
            {
                if (HasTilesData)
                {
                    tiles.Dispose();
                    tiles = null;
                }
            }

            DisposeTemp();
        }

        abstract protected void AfterGenerationJobsComplete();

        virtual public void Dispose()
        {
            state = State.Uninitialized;

            lastHandle.Complete();
            DisposeTemp();

            if (tiles != null)
            {
                tiles.Dispose();
                tiles = null;
            }
        }

        virtual protected void DisposeTemp()
        {

        }

        protected void Warning(string msg, params object[] args)
        {
            Debug.LogWarningFormat(name + " - " + msg, args);
        }

        protected void Error(string msg, params object[] args)
        {
            Debug.LogErrorFormat(name + " - " + msg, args);
        }

        public bool IsInitialized { get { return state != State.Uninitialized; } }
        public bool IsGenerating { get { return state == State.Generating; } }

        protected bool HasTilesData { get { return tiles != null && !tiles.IsDisposed; } }
    }
}
