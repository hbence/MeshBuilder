using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    using ScaleValue = SplineModifier.ValueAtDistance<float2>;
    using RotationValue = SplineModifier.ValueAtDistance<float>;

    public class SplineComponent : MonoBehaviour
    {
        [SerializeField] private Spline spline = null;
        public Spline Spline => spline;
        public int SegmentCount => spline == null ? 0 :
                                    spline.IsClosed ? spline.ControlPointsCount : 
                                                      spline.ControlPointsCount - 1;

        public Vector3[] ControlPoints
        {
            get => spline.ControlPoints;
            set
            {
                spline.CopyControlPointsFrom(value);
                UpdateCache();
            }
        }

        public int ControlPointCount => ControlPoints.Length;
        public void UpdateControlPoint(int index, Vector3 pos)
        {
            if (IsValidSplineControlPointIndex(index))
            {
                spline.SetControlPoint(index, pos);
                UpdateCache();
            }
        }

        public void AddControlPoint(int index, Vector3 pos)
        {
            if (spline != null)
            {
                if (spline.ControlPoints == null || (index >= 0 && index <= ControlPointCount))
                {
                    spline.AddControlPoint(index, pos);
                    UpdateCache();
                }
            }
        }

        public void RemoveControlPoint(int index)
        {
            if (IsValidSplineControlPointIndex(index))
            {
                spline.RemoveControlPoint(index);
                UpdateCache();
            }
        }

        private bool IsValidSplineControlPointIndex(int index)
            => spline != null && spline.ControlPoints != null & index >= 0 && index < ControlPointCount;

        [Header("spline")]
        [SerializeField] private ArcType arcType = ArcType.CatmullRom;
        public ArcType ArcType => arcType;
        [SerializeField] private LinearArcCalculator linearArc;
        [SerializeField] private CatmullRomArcCalculator catmullRomArc;
        public IArcCalculator ArcCalculator
        {
            get
            {
                switch(arcType)
                {
                    case ArcType.Linear: return linearArc;
                    case ArcType.CatmullRom: return catmullRomArc;
                }
                return linearArc;
            }
        }

        [SerializeField] private bool useCustomRotation = false;
        public bool UseCustomRotation => useCustomRotation;
        [SerializeField] private RotationValue[] rotationValues = null;
        public RotationValue[] RotationValues
        {
            get => rotationValues;
            set
            {
                rotationValues = value;
                if (rotationValues != null)
                {
                    System.Array.Sort(rotationValues, (RotationValue a, RotationValue b) => a.Distance.CompareTo(b.Distance));
                }
            }
        }

        [SerializeField] private bool useCustomScaling = false;
        public bool UseCustomScaling => useCustomScaling;
        [SerializeField] private ScaleValue[] scaleValues = null;
        public ScaleValue[] ScaleValues
        {
            get => scaleValues;
            set
            {
                scaleValues = value;
                if (scaleValues != null)
                {
                    System.Array.Sort(scaleValues, (ScaleValue a, ScaleValue b) => a.Distance.CompareTo(b.Distance));
                }
            }
        }

        [Header("cache")]
        [SerializeField] private int maxCachePositionCount = SplineCache.DefaultMaxPositionCount;
        [SerializeField] private int segmentLookupCount = 20;
        public int SegmentLookupCount => segmentLookupCount;
        [SerializeField] private float cacheStepDistance = 0.1f;

        private CacheHandler cache = null;
        public bool HasCache => cache != null;
        public SplineCache SplineCache => cache.Cache;

        private void Awake()
        {
            spline.ArcCalculator = ArcCalculator;
            CreateCache();
        }

        private void OnEnable()
        {
            if (cache == null)
            {
                CreateCache();
            }
        }

        private void CreateCache()
        {
            if (cache == null)
            {
                cache = new CacheHandler(transform, spline, cacheStepDistance, maxCachePositionCount, segmentLookupCount);
            }
            else
            {
                cache.Update(spline, cacheStepDistance);
            }
        }

        public void UpdateCache()
        {
            cache?.Update(spline, cacheStepDistance);
        }

        public void Update()
        {
            if (spline.ArcCalculator.Type != ArcCalculator.Type)
            {
                spline.ArcCalculator = ArcCalculator;
                UpdateCache();
            }
        }

        private void OnDestroy()
        {
            if (cache != null)
            {
                cache.Destroy();
                cache = null;
            }
        }

        private int FromCacheDebugSplinePoints(Vector3[] outPoints)
        {
            if (cache == null)
            {
                Debug.LogError("cache is not generated!");
                return 0;
            }

            int count = Mathf.Min(outPoints.Length, cache.Cache.CachedPositionCount);
            var convertedArray = Utils.ToFloat3Array(outPoints);
            Unity.Collections.NativeArray<float3>.Copy(cache.Cache.Positions, convertedArray, count);

            return count;
        }

        public int CalculateSplinePoints(Vector3[] outPoints, int pointsPerSegment = 10)
        {
            if (outPoints == null || outPoints.Length < 2)
            {
                Debug.LogError("outPoints needs to be larger!");
                return 0;
            }

            int resCount = 0;
            if (spline.ControlPoints != null && spline.ControlPointsCount > 1)
            {
                var controlPoints = Utils.ToFloat3Array(spline.ControlPoints);
                float step = 1f / pointsPerSegment;
                outPoints[0] = spline.ControlPoints[0];
                ++resCount;
                for (int i = 0; i < SegmentCount; ++i)
                {
                    for (int j = 1; j <= pointsPerSegment; ++j)
                    {
                        if (resCount >= outPoints.Length)
                        {
                            break;
                        }
                       
                        float3 cur = ArcCalculator.Calculate(controlPoints, i, (i + 1) % spline.ControlPointsCount, step * j, spline.IsClosed);
                        outPoints[resCount] = cur;
                        ++resCount;
                    }
                }
            }

            return resCount;
        }

        public int CalculateSplinePoints(Vector3[] outPoints, int startControlPoint, int endControlPoint, int pointsPerSegment = 10)
        {
            if (outPoints == null || outPoints.Length < 2)
            {
                Debug.LogError("outPoints needs to be larger!");
                return 0;
            }

            int resCount = 0;
            if (spline.ControlPoints != null && spline.ControlPointsCount > 1)
            {
                var controlPoints = Utils.ToFloat3Array(spline.ControlPoints);
                float step = 1f / pointsPerSegment;
                outPoints[0] = spline.ControlPoints[startControlPoint];
                ++resCount;
                for (int i = startControlPoint; i < endControlPoint; ++i)
                {
                    for (int j = 1; j <= pointsPerSegment; ++j)
                    {
                        if (resCount >= outPoints.Length)
                        {
                            break;
                        }

                        float3 cur = ArcCalculator.Calculate(controlPoints, i, (i + 1) % spline.ControlPointsCount, step * j, spline.IsClosed);
                        outPoints[resCount] = cur;
                        ++resCount;
                    }
                }
            }

            return resCount;
        }

        private class CacheHandler
        {
            public SplineCache Cache { get; set; }

            private Spline cachedSpline;
            private float cacheStepDistance;

            private Transform Root { get; }

            public CacheHandler(Transform root, Spline spline, float cacheStepDistance, int maxPositionCount, int segmentLookupCount)
            {
                this.cacheStepDistance = cacheStepDistance;
                Root = root;

                cachedSpline = new Spline();
                cachedSpline.FromOther(spline);

                Cache = new SplineCache(cachedSpline, cacheStepDistance, maxPositionCount, segmentLookupCount);
            }

            public void Update(Spline spline, float cacheStepDist)
            {
                if (!spline.Equals(cachedSpline) || cacheStepDistance != cacheStepDist)
                {
                    cacheStepDistance = cacheStepDist;
                    cachedSpline.FromOther(spline);
                
                    Cache.Recalculate(cachedSpline, cacheStepDistance);
                }
            }

            public void Destroy()
            {
                Cache.Dispose();
                Cache = default;
            }

            public float3 CaclulatePositionAtDistance(float distance)
                => Cache.CalculatePosition(distance);

            public void DebugDraw(Color color)
            {
                if (Cache.Distance > 0 && Cache.CachedPositionCount > 0)
                {
                    Gizmos.color = color;
                    for (int i = 0; i < Cache.CachedPositionCount - 1; ++i)
                    {
                        Gizmos.DrawLine(CP(i), CP(i + 1));
                        Gizmos.DrawSphere(CP(i), 0.05f);
                    }
                    Gizmos.DrawSphere(CP(Cache.CachedPositionCount - 1), 0.05f);
                }
            }

            private Vector3 CP(int index) => Cache.GetCachedPosition(index);
        }
    }
}
