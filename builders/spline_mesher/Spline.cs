using System;
using UnityEngine;

namespace MeshBuilder
{
    /// <summary>
    /// Spline class to get points on a curve between controlpoints. A certain number of points (LookupPerSegment)
    /// are calculated and cached in the LookupTable, later the spline position values are interpolated from the lookup table.
    /// </summary>
    [Serializable]
    public class Spline
    {
        /// <summary>
        /// The class to calculate points between controlpoints.
        /// </summary>
        [Serializable]
        public abstract class ArcCalculator
        {
            abstract public Vector3 Calculate(float t, int aIndex, int bIndex, Vector3[] controlPoints);
            abstract public void Calculate(Vector3[] outPositions, int aIndex, int bIndex, Vector3[] controlPoints, float tStart, float tStep);
        }

        [SerializeField] private Vector3[] controlPoints;
        public Vector3[] ControlPoints { get => controlPoints; set { controlPoints = value; Recalculate(); } }

        [SerializeField] private bool closed = false;
        public bool IsClosed { get => closed; set { if (closed != value) { closed = value; Recalculate(); } } }

        [SerializeField] private int lookupPerSegment = 10;
        public int LookupPerSegment { get => lookupPerSegment; }

        [SerializeField] private ArcCalculator arcCalculator;
        public ArcCalculator Calculator { get => arcCalculator; }

        [HideInInspector, SerializeField] private PositionLookupTable lookupTable;
        public PositionLookupTable LookupTable { get => lookupTable; }

        public Spline(ArcCalculator arcCalculator, Vector3[] controlPoints = null, bool closed = false)
        {
            this.controlPoints = controlPoints;
            this.arcCalculator = arcCalculator;
            this.closed = closed;

            lookupTable = new PositionLookupTable();

            Recalculate();
        }

        public void Recalculate()
        {
            lookupTable.UpdatePoints(this);
        }

        public Vector3 CalcAtDistance(float distance)
        {
            return lookupTable.CalcAtDistance(distance);
        }

        public int ControlPointsLength { get => controlPoints != null ? controlPoints.Length : 0; }

        public void SetControlPoint(int index, Vector3 pos)
        {
            controlPoints[index] = pos;
            Recalculate();
        }

        public float Length { get => lookupTable.MaxDistance; }

        static public Spline CreateCatmullRomSpline(Vector3[] controlPoints = null, float alpha = CatmullRom.DefaultAlpha)
        {
            return new Spline(new CatmullRom(alpha), controlPoints);
        }

        /// <summary>
        /// Class to cache positions on the spline.
        /// </summary>
        [Serializable]
        public class PositionLookupTable : DistanceLookupTable<Vector3>
        {
            public void UpdatePoints(Spline spline)
            {
                var controlPoints = spline.controlPoints;
                var arc = spline.arcCalculator;

                if (arc == null || controlPoints == null || controlPoints.Length < 2)
                {
                    Clear();
                    return;
                }
                else
                {
                    int segmentCount = controlPoints.Length - 1;
                    int pointCount = spline.lookupPerSegment * segmentCount + 1;
                    if (spline.closed)
                    {
                        segmentCount += 1;
                        pointCount = spline.lookupPerSegment * segmentCount + 1;
                    }

                    Resize(pointCount);

                    int lookupCount = spline.lookupPerSegment;
                    Vector3[] segmentPositions = new Vector3[lookupCount];
                    float tStep = 1f / lookupCount;
                    for (int i = 0; i < segmentCount; ++i)
                    {
                        arc.Calculate(segmentPositions, i, (i + 1) % controlPoints.Length, controlPoints, 0f, tStep);
                        int offset = i * lookupCount;
                        for (int j = 0; j < lookupCount; ++j)
                        {
                            SetElemData(offset + j, segmentPositions[j]);
                        }
                    }

                    Vector3 last;
                    if (spline.IsClosed)
                    {
                        last = arc.Calculate(1f, spline.controlPoints.Length - 1, 0, spline.controlPoints);
                    }
                    else
                    {
                        last = arc.Calculate(1f, spline.controlPoints.Length - 2, spline.controlPoints.Length - 1, spline.controlPoints);
                    }
                    SetElemData(pointCount - 1, last);

                    Vector3 prev = GetElemData(0);
                    float sum = 0;
                    for (int i = 1; i < Elems.Length; ++i)
                    {
                        Vector3 cur = GetElemData(i);
                        sum += Vector3.Distance(prev, cur);
                        prev = cur;
                        SetElemDistance(i, sum);
                    }
                }
            }

            override protected Vector3 Lerp(Vector3 a, Vector3 b, float t)
            {
                return Vector3.Lerp(a, b, t);
            }
        }

        /// <summary>
        /// Simple linearly interpolated values between control points.
        /// </summary>
        [Serializable]
        public class Linear : ArcCalculator
        {
            public override Vector3 Calculate(float t, int aIndex, int bIndex, Vector3[] controlPoints)
            {
                return Vector3.Lerp(controlPoints[aIndex], controlPoints[bIndex], t);
            }

            public override void Calculate(Vector3[] outPositions, int aIndex, int bIndex, Vector3[] controlPoints, float tStart, float tStep)
            {
                Vector3 a = controlPoints[aIndex];
                Vector3 b = controlPoints[bIndex];
                for (int i = 0; i < outPositions.Length; ++i)
                {
                    outPositions[i] = Vector3.Lerp(a, b, tStart + tStep * i);
                }
            }
        }

        /// <summary>
        /// Catmull-Rom curve between control points.
        /// </summary>
        [Serializable]
        public class CatmullRom : ArcCalculator
        {
            public const float DefaultAlpha = 0.5f;

            [SerializeField] private float alpha = 0.5f;
            public float Alpha { get => alpha; set => alpha = value; }

            public CatmullRom(float alpha = DefaultAlpha)
            {
                this.alpha = alpha;
            }

            public override Vector3 Calculate(float t, int aIndex, int bIndex, Vector3[] controlPoints)
            {
                ControlData p0, p1, p2, p3;
                FillControlData(aIndex, bIndex, controlPoints, out p0, out p1, out p2, out p3);

                return CalcAt(Mathf.Lerp(p1.t, p2.t, t), p0, p1, p2, p3);
            }

            public override void Calculate(Vector3[] outPositions, int aIndex, int bIndex, Vector3[] controlPoints, float tStart, float tStep)
            {
                tStart = Mathf.Clamp01(tStart);
                tStep = Mathf.Abs(tStep);

                ControlData p0, p1, p2, p3;
                FillControlData(aIndex, bIndex, controlPoints, out p0, out p1, out p2, out p3);
                float t = Mathf.Lerp(p1.t, p2.t, tStart);
                float step = (p2.t - p1.t) * tStep;
                for (int i = 0; i < outPositions.Length; ++i)
                {
                    outPositions[i] = CalcAt(t, p0, p1, p2, p3);
                    t += step;
                }
            }

            private void FillControlData(int a, int b, Vector3[] controlPoints, out ControlData p0, out ControlData p1, out ControlData p2, out ControlData p3)
            {
                Vector3 v0 = a > 0 ? controlPoints[a - 1] : controlPoints[a];
                Vector3 v1 = controlPoints[a];
                Vector3 v2 = controlPoints[b];
                Vector3 v3 = b < controlPoints.Length - 1 ? controlPoints[b + 1] : controlPoints[b];

                p0 = new ControlData(v0, 0);
                p1 = new ControlData(v1, GetT(p0.t, v0, v1));
                p2 = new ControlData(v2, GetT(p1.t, v1, v2));
                p3 = new ControlData(v3, GetT(p2.t, v2, v3));
            }

            private class ControlData
            {
                public Vector3 v;
                public float t;
                public ControlData(Vector3 v, float t) { this.v = v; this.t = t; }
            }

            static private Vector3 CalcAt(float t, ControlData p0, ControlData p1, ControlData p2, ControlData p3)
            {
                float a12 = RatioA(t, p1.t, p2.t);
                float b12 = RatioB(t, p1.t, p2.t);

                Vector3 A1 = (p0.t != p1.t) ? RatioA(t, p0.t, p1.t) * p0.v + RatioB(t, p0.t, p1.t) * p1.v : p0.v;
                Vector3 A2 = a12 * p1.v + b12 * p2.v;
                Vector3 A3 = (p2.t != p3.t) ? RatioA(t, p2.t, p3.t) * p2.v + RatioB(t, p2.t, p3.t) * p3.v : p2.v;

                Vector3 B1 = RatioA(t, p0.t, p2.t) * A1 + RatioB(t, p0.t, p2.t) * A2;
                Vector3 B2 = RatioA(t, p1.t, p3.t) * A2 + RatioB(t, p1.t, p3.t) * A3;

                return a12 * B1 + b12 * B2;
            }

            static private float RatioA(float t, float t0, float t1)
            {
                return (t1 - t) / (t1 - t0);
            }

            static private float RatioB(float t, float t0, float t1)
            {
                return (t - t0) / (t1 - t0);
            }

            float GetT(float t, Vector3 p0, Vector3 p1)
            {
                float a = Mathf.Pow((p1.x - p0.x), 2.0f) + Mathf.Pow((p1.y - p0.y), 2.0f) + Mathf.Pow((p1.z - p0.z), 2.0f);
                float b = Mathf.Pow(a, 0.5f);
                float c = Mathf.Pow(b, alpha);

                return (c + t);
            }
        }

        /// <summary>
        /// Bezier curve between control points. Uses a Cubic function, two temporary control points are generated between
        /// given control points. The generation can be automatic (trying to fit between the control points based on the
        /// previous/next points), manually scaled (the points are selected similarily to automatic mode but scaled with
        /// a value) and manual (which uses offset values to get the in-between control points).
        /// </summary>
        [Serializable]
        public class Bezier : ArcCalculator
        {
            private static readonly ManualScale DefaultManualScale = new ManualScale(1, 1);
            private static readonly ManualOffset DefaultManualOffset = new ManualOffset(Vector3.zero, Vector3.zero);

            public enum ControlPointMode
            {
                Automatic,
                ManuallyScaled,
                Manual
            }

            [SerializeField] private ControlPointMode controlPointMode = ControlPointMode.Automatic;
            public ControlPointMode ControlMode { get => controlPointMode; set => controlPointMode = value; }
            [SerializeField] private float autoScale = 1f;
            public float AutoScale { get => autoScale; set => autoScale = value; }
            [SerializeField] private ManualScale[] manualScales;
            public ManualScale[] ManualScales { get => manualScales; set => manualScales = value; }
            [SerializeField] private ManualOffset[] manualOffsets;
            public ManualOffset[] ManualOffsets { get => manualOffsets; set => manualOffsets = value; }

            private Vector3 CalculateAutomatic(float t, int aIndex, int bIndex, Vector3[] controlPoints)
            {
                Vector3 prev = controlPoints[(controlPoints.Length + aIndex - 1) % controlPoints.Length];
                Vector3 next = controlPoints[(bIndex + 1) % controlPoints.Length];
                Vector3 p0 = controlPoints[aIndex];
                Vector3 p3 = controlPoints[bIndex];
                float dist = Vector3.Distance(p0, p3);
                float scale = dist * 0.25f * autoScale;
                Vector3 p1 = p0 + (p3 - prev).normalized * scale;
                Vector3 p2 = p3 + (p0 - next).normalized * scale;

                return CalcBezier(t, p0, p1, p2, p3);
            }

            private Vector3 CalculateManuallyScaled(float t, int aIndex, int bIndex, Vector3[] controlPoints)
            {
                Vector3 prev = controlPoints[(controlPoints.Length + aIndex - 1) % controlPoints.Length];
                Vector3 next = controlPoints[(bIndex + 1) % controlPoints.Length];
                Vector3 p0 = controlPoints[aIndex];
                Vector3 p3 = controlPoints[bIndex];
                float dist = Vector3.Distance(p0, p3);
                ManualScale scales = (manualScales != null && aIndex < manualScales.Length) ? manualScales[aIndex] : DefaultManualScale;
                float scale1 = dist * 0.25f * scales.BeginScale;
                float scale2 = dist * 0.25f * scales.EndScale;
                Vector3 p1 = p0 + (p3 - prev).normalized * scale1;
                Vector3 p2 = p3 + (p0 - next).normalized * scale2;

                return CalcBezier(t, p0, p1, p2, p3);
            }

            private Vector3 CalculateManual(float t, int aIndex, int bIndex, Vector3[] controlPoints)
            {
                Vector3 prev = controlPoints[(controlPoints.Length + aIndex - 1) % controlPoints.Length];
                Vector3 next = controlPoints[(bIndex + 1) % controlPoints.Length];
                Vector3 p0 = controlPoints[aIndex];
                Vector3 p3 = controlPoints[bIndex];
                float dist = Vector3.Distance(p0, p3);
                ManualOffset offset = (manualOffsets != null && aIndex < manualOffsets.Length) ? manualOffsets[aIndex] : DefaultManualOffset;
                Vector3 p1 = p0 + offset.BeginOffset;
                Vector3 p2 = p3 + offset.EndOffset;

                return CalcBezier(t, p0, p1, p2, p3);
            }

            public override Vector3 Calculate(float t, int aIndex, int bIndex, Vector3[] controlPoints)
            {
                if (controlPointMode == ControlPointMode.Manual && manualOffsets != null)
                {
                    return CalculateManuallyScaled(t, aIndex, bIndex, controlPoints);
                }
                else if (controlPointMode == ControlPointMode.ManuallyScaled && manualScales != null)
                {
                    return CalculateManual(t, aIndex, bIndex, controlPoints);
                }

                return CalculateAutomatic(t, aIndex, bIndex, controlPoints);
            }

            public override void Calculate(Vector3[] outPositions, int aIndex, int bIndex, Vector3[] controlPoints, float tStart, float tStep)
            {
                Func<float, int, int, Vector3[], Vector3> calc = CalculateAutomatic;

                if (controlPointMode == ControlPointMode.Manual && manualOffsets != null)
                {
                    calc = CalculateManual;
                }
                else if (controlPointMode == ControlPointMode.ManuallyScaled && manualScales != null)
                {
                    calc = CalculateManuallyScaled;
                }

                for (int i = 0; i < outPositions.Length; ++i)
                {
                    outPositions[i] = calc(tStart + tStep * i, aIndex, bIndex, controlPoints);
                }
            }

            private static Vector3 CalcBezier(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                float tc = 1f - t;
                return CB(tc) * p0 + (3 * SQ(tc) * t) * p1 + (3 * tc * SQ(t)) * p2 + CB(t) * p3;
            }

            private static float SQ(float v) { return v * v; }
            private static float CB(float v) { return v * v * v; }

            [Serializable]
            public class ManualScale
            {
                [SerializeField] private float beginScale = 1f;
                public float BeginScale { get => beginScale; set => beginScale = value; }
                [SerializeField] private float endScale = 1f;
                public float EndScale { get => endScale; set => endScale = value; }

                public ManualScale(float begin = 1f, float end = 1f) { beginScale = begin; endScale = end; }
            }

            [Serializable]
            public class ManualOffset
            {
                [SerializeField] private Vector3 beginOffset;
                public Vector3 BeginOffset { get => beginOffset; set => beginOffset = value; }
                [SerializeField] private Vector3 endOffset;
                public Vector3 EndOffset { get => endOffset; set => endOffset = value; }

                public ManualOffset(Vector3 begin, Vector3 end) { beginOffset = begin; endOffset = end; }
            }

        }
    }
}
