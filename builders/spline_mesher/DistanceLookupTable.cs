using System;
using UnityEngine;

namespace MeshBuilder
{
    /// <summary>
    /// Get interpolated data based on cached data at set distances.
    /// </summary>
    /// <typeparam name="T">The data you want to interpolate.</typeparam>
    [Serializable]
    public abstract class DistanceLookupTable<T>
    {
        private const int DefaultLinearLimit = 50;

        [HideInInspector, SerializeField]
        private Elem[] elems;
        protected Elem[] Elems { get => elems; set => elems = value; }

        public float MaxDistance { get => (elems != null) ? elems[elems.Length - 1].DistanceSum : 0; }

        public int ElemCount { get => Elems == null ? 0 : Elems.Length; }

        private delegate T DistanceLookup(float dist);
        private DistanceLookup lookupFn;

        public DistanceLookupTable()
        {
            lookupFn = LookupDefault;
        }

        public void Resize(int count)
        {
            if (count > 0)
            {
                if (elems == null || elems.Length != count)
                {
                    var newElems = new Elem[count];
                    if (elems != null)
                    {
                        Array.Copy(elems, newElems, Mathf.Min(elems.Length, newElems.Length));
                    }
                    elems = newElems;

                    SelectLookupFn();
                }
            }
            else if (count == 0)
            {
                Clear();
            }
        }

        public void SetElem(int index, T data, float distance)
        {
            float prevDist = 0;
            if (index > 0)
            {
                prevDist = distance - Elems[index - 1].DistanceSum;
            }
            Elems[index] = new Elem(data, prevDist, distance);
        }

        public void SetElemData(int index, T data)
        {
            SetElem(index, data, Elems[index].DistanceSum);
        }

        public T GetElemData(int index)
        {
            return Elems[index].Data;
        }

        public void SetElemDistance(int index, float distance)
        {
            SetElem(index, Elems[index].Data, distance);
        }

        public float GetElemDistance(int index)
        {
            return Elems[index].DistanceSum;
        }

        public void Clear()
        {
            elems = null;
            lookupFn = LookupDefault;
        }

        public T CalcAtDistance(float distance)
        {
            return lookupFn(distance);
        }

        public T CalcAtDistanceSequential(float distance, ref int fromIndex)
        {
            fromIndex = Mathf.Min(fromIndex, 1);

            for (; fromIndex < Elems.Length; ++fromIndex)
            {
                if (distance < Elems[fromIndex].DistanceSum)
                {
                    return Calc(fromIndex - 1, fromIndex, distance);
                }
            }

            return Elems[ElemCount - 1].Data;
        }

        private void SelectLookupFn()
        {
            if (Elems == null)
            {
                lookupFn = LookupDefault;
            }
            else
            {
                if (elems.Length < DefaultLinearLimit)
                {
                    lookupFn = CalcAtDistanceLinear;
                }
                else
                {
                    lookupFn = CalcAtDistanceBinary;
                }
            }
        }

        private T LookupDefault(float distance)
        {
            return default;
        }

        public T CalcAtDistanceLinear(float distance)
        {
            for (int i = 1; i < Elems.Length; ++i)
            {
                if (distance < Elems[i].DistanceSum)
                {
                    return Calc(i - 1, i, distance);
                }
            }

            return Elems[ElemCount - 1].Data;
        }

        public T CalcAtDistanceBinary(float distance)
        {
            int start = 0;
            int end = 0;

            BinaryBounds(distance, out start, out end);

            return Calc(start, end, distance);
        }

        public void BinaryBounds(float distance, out int start, out int end)
        {
            start = 0;
            end = Elems.Length - 1;

            while ((end - start) >= 2)
            {
                int pivot = (start + end) / 2;

                if (Elems[pivot].DistanceSum > distance)
                {
                    end = pivot;
                }
                else
                {
                    start = pivot;
                }
            }
        }

        private T Calc(int a, int b, float distance)
        {
            return Lerp(Elems[a].Data, Elems[b].Data, (distance - Elems[a].DistanceSum) / Elems[b].DistanceFromPrev);
        }

        protected abstract T Lerp(T a, T b, float t);

        [Serializable]
        protected struct Elem
        {
            [SerializeField] private float distanceSum;
            public float DistanceSum { get => distanceSum; }
            [SerializeField] private float distanceFromPrev;
            public float DistanceFromPrev { get => distanceFromPrev; }
            [SerializeField] private T data;
            public T Data { get => data; }

            public Elem(T data, float distanceFromPrev, float distanceSum)
            {
                this.data = data;
                this.distanceFromPrev = distanceFromPrev;
                this.distanceSum = distanceSum;
            }
        }
    }

}
