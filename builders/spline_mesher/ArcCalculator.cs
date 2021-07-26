using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using System;

namespace MeshBuilder
{
    public enum ArcType
    {
        Linear,
        CatmullRom
    }

    public interface IArcCalculator : IEquatable<IArcCalculator>, ICloneable
    {
        ArcType Type { get; }
        float3 Calculate(float3[] controlPoints, int aIndex, int bIndex, float t, bool isClosed);
        void Calculate(NativeArray<float3> outPositions, float3[] controlPoints, int aIndex, int bIndex, float tStart, float tStep, bool isClosed);
    }

    public struct LinearArcCalculator : IArcCalculator
    {
        public ArcType Type => ArcType.Linear;

        public float3 Calculate(float3[] controlPoints, int aIndex, int bIndex, float t, bool isClosed)
            => math.lerp(controlPoints[aIndex], controlPoints[bIndex], t);

        public void Calculate(NativeArray<float3> outPositions, float3[] controlPoints, int aIndex, int bIndex, float tStart, float tStep, bool isClosed)
        {
            float3 a = controlPoints[aIndex];
            float3 b = controlPoints[bIndex];
            for (int i = 0; i < outPositions.Length; ++i)
            {
                outPositions[i] = math.lerp(a, b, tStart + tStep * i);
            }
        }

        public object Clone() => new LinearArcCalculator();

        public bool Equals(IArcCalculator other) => other is LinearArcCalculator;
    }

    /// <summary>
    /// Catmull-Rom curve between control points.
    /// </summary>
    [Serializable]
    public struct CatmullRomArcCalculator : IArcCalculator
    {
        public const float DefaultAlpha = 0.5f;

        public ArcType Type => ArcType.CatmullRom;

        [SerializeField] private float alpha;
        public float Alpha { get => alpha; set => alpha = value; }

        public CatmullRomArcCalculator(float alpha = DefaultAlpha)
        {
            this.alpha = alpha;
        }

        public float3 Calculate(float3[] controlPoints, int aIndex, int bIndex, float t, bool isClosed)
        {
            ControlData p0, p1, p2, p3;
            FillControlData(aIndex, bIndex, controlPoints, out p0, out p1, out p2, out p3, isClosed);

            return CalcAt(math.lerp(p1.t, p2.t, t), p0, p1, p2, p3);
        }

        public void Calculate(NativeArray<float3> outPositions, float3[] controlPoints, int aIndex, int bIndex, float tStart, float tStep, bool isClosed)
        {
            tStart = Mathf.Clamp01(tStart);
            tStep = Mathf.Abs(tStep);

            ControlData p0, p1, p2, p3;
            FillControlData(aIndex, bIndex, controlPoints, out p0, out p1, out p2, out p3, isClosed);
            float t = math.lerp(p1.t, p2.t, tStart);
            float step = (p2.t - p1.t) * tStep;
            for (int i = 0; i < outPositions.Length; ++i)
            {
                outPositions[i] = CalcAt(t, p0, p1, p2, p3);
                t += step;
            }
        }

        public object Clone() => new CatmullRomArcCalculator(alpha);

        public bool Equals(IArcCalculator other)
        {
            if (other is CatmullRomArcCalculator)
            {
                CatmullRomArcCalculator otherArc = (CatmullRomArcCalculator)other;
                return otherArc.alpha == alpha;
            }
            return false;
        }

        private void FillControlData(int a, int b, float3[] controlPoints, out ControlData p0, out ControlData p1, out ControlData p2, out ControlData p3, bool isClosed)
        {
            int length = controlPoints.Length;

            float3 v0 = controlPoints[math.max(0, a - 1)];
            float3 v1 = controlPoints[a];
            float3 v2 = controlPoints[b];
            float3 v3 = controlPoints[math.min(controlPoints.Length - 1, b + 1)];

            if (isClosed)
            {
                v0 = controlPoints[(a + length - 1) % length];
                v3 = controlPoints[(b + 1) % length];
            }

            p0 = new ControlData(v0, 0);
            p1 = new ControlData(v1, GetT(p0.t, v0, v1));
            p2 = new ControlData(v2, GetT(p1.t, v1, v2));
            p3 = new ControlData(v3, GetT(p2.t, v2, v3));
        }

        private struct ControlData
        {
            public Vector3 v;
            public float t;
            public ControlData(Vector3 v, float t) { this.v = v; this.t = t; }
        }

        static private float3 CalcAt(float t, ControlData p0, ControlData p1, ControlData p2, ControlData p3)
        {
            float a12 = RatioA(t, p1.t, p2.t);
            float b12 = RatioB(t, p1.t, p2.t);

            float3 A1 = (p0.t != p1.t) ? RatioA(t, p0.t, p1.t) * p0.v + RatioB(t, p0.t, p1.t) * p1.v : p0.v;
            float3 A2 = a12 * p1.v + b12 * p2.v;
            float3 A3 = (p2.t != p3.t) ? RatioA(t, p2.t, p3.t) * p2.v + RatioB(t, p2.t, p3.t) * p3.v : p2.v;

            float3 B1 = RatioA(t, p0.t, p2.t) * A1 + RatioB(t, p0.t, p2.t) * A2;
            float3 B2 = RatioA(t, p1.t, p3.t) * A2 + RatioB(t, p1.t, p3.t) * A3;

            return a12 * B1 + b12 * B2;
        }

        static private float RatioA(float t, float t0, float t1)
            =>  (t1 - t) / (t1 - t0);

        static private float RatioB(float t, float t0, float t1)
            => (t - t0) / (t1 - t0);

        private float GetT(float t, Vector3 p0, Vector3 p1)
        {
            float a = math.pow(p1.x - p0.x, 2.0f) + math.pow(p1.y - p0.y, 2.0f) + math.pow(p1.z - p0.z, 2.0f);
            float b = math.pow(a, 0.5f);
            float c = math.pow(b, alpha);

            return (c + t);
        }
    }
}
