using System;
using UnityEngine;

namespace MeshBuilder
{
    [Serializable]
    public class Spline : IEquatable<Spline>, ICloneable
    {
        private static readonly IArcCalculator DefaultArcCalculator = new LinearArcCalculator();

        [SerializeField] 
        private IArcCalculator arcCalculator = DefaultArcCalculator;

        public IArcCalculator ArcCalculator
        {
            get => arcCalculator;
            set => arcCalculator = value;
        }

        [SerializeField]
        private bool isClosed = false;

        public bool IsClosed
        {
            get => isClosed;
            set => isClosed = value;
        }

        [SerializeField]
        private Vector3[] controlPoints = null;
        public Vector3[] ControlPoints
        {
            get => controlPoints;
        }

        public void CopyControlPointsFrom(Vector3[] points)
        {
            if (points == null)
            {
                controlPoints = null;
            }
            else
            {
                if (controlPoints == null || controlPoints.Length != points.Length)
                {
                    controlPoints = new Vector3[points.Length];
                }

                Array.Copy(points, controlPoints, points.Length);
            }
        }

        public int ControlPointsCount => controlPoints != null ? controlPoints.Length : 0;

        public Vector3 GetControlPoint(int index) => controlPoints[index];
        public void SetControlPoint(int index, Vector3 pos) => controlPoints[index] = pos;

        public void AddControlPoint(int index, Vector3 pos)
        {
            if (controlPoints == null)
            {
                controlPoints = new Vector3[1] { pos };
            }

            if (index < 0 || index > controlPoints.Length)
            {
                Debug.LogWarning($"can't add at invalid index {index}, added at end");
                index = controlPoints.Length;
            }

            var newControlPoints = new Vector3[controlPoints.Length + 1];
            int nextIndex = 0;
            for (int i = 0; i < newControlPoints.Length; ++i)
            {
                if (i == index)
                {
                    newControlPoints[i] = pos;
                }
                else
                {
                    newControlPoints[i] = controlPoints[nextIndex];
                    ++nextIndex;
                }
            }
            controlPoints = newControlPoints;
        }

        public void RemoveControlPoint(int index)
        {
            if (index < 0 || index >= controlPoints.Length)
            {
                Debug.LogError($"invalid index: {index}");
                return;
            }

            if (controlPoints.Length == 1)
            {
                controlPoints = null;
                return;
            }

            var newControlPoints = new Vector3[controlPoints.Length - 1];
            int nextIndex = 0;
            for (int i = 0; i < controlPoints.Length; ++i)
            {
                if (i == index)
                {
                    continue;
                }
                newControlPoints[nextIndex] = controlPoints[i];
                ++nextIndex;
            }
            controlPoints = newControlPoints;
        }

        public Spline()
        {
            arcCalculator = DefaultArcCalculator;
        }

        public Spline(Vector3[] controlPoints, bool isClosed, IArcCalculator arcCalculator)
        {
            ArcCalculator = (IArcCalculator) arcCalculator.Clone();
            IsClosed = isClosed;
            CopyControlPointsFrom(controlPoints);
        }

        static private bool Equals(Vector3[] a, Vector3[] b)
        {
            if (a == b) { return true; }

            if (a != null && b != null && a.Length == b.Length)
            {
                for(int i = 0; i < a.Length; ++i)
                {
                    if (a[i] != b[i]) 
                    {
                        return false; 
                    }
                }
                return true;
            }

            return false;
        }

        public object Clone()
            => new Spline(controlPoints, isClosed, arcCalculator);

        public void FromOther(Spline other)
        {
            IsClosed = other.isClosed;
            arcCalculator = (IArcCalculator) other.arcCalculator.Clone();
            CopyControlPointsFrom(other.controlPoints);
        }

        public bool Equals(Spline other)
            => other == null ? false :
                        (isClosed == other.isClosed &&
                        arcCalculator == other.arcCalculator && 
                        Equals(controlPoints, other.controlPoints));
    }
}
