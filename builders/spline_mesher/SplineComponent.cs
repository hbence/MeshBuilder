using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
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
            if (ControlPoints != null && index < ControlPointCount)
            {
                spline.SetControlPoint(index, pos);
                UpdateCache();
            }
        }

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

        [Header("cache")]
        [SerializeField] private int maxCachePositionCount = SplineCache.DefaultMaxPositionCount;
        [SerializeField] private int segmentLookupCount = 20;
        public int SegmentLookupCount => segmentLookupCount;
        [SerializeField] private float cacheStepDistance = 0.1f;

        private CacheHandler cache = null;
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
                cache = new CacheHandler(spline, cacheStepDistance, maxCachePositionCount, segmentLookupCount);
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

        public int CalculateDebugSplinePoints(Vector3[] outPoints)
        {
            if (cache != null)
            {
                return FromCacheDebugSplinePoints(outPoints);
            }
            else
            {
                return CalculateSplinePoints(outPoints);
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

        public int CalculateSplinePoints(Vector3[] outPoints)
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
                int perSegment = 10;
                float step = 1f / perSegment;
                outPoints[0] = spline.ControlPoints[0];
                ++resCount;
                for (int i = 0; i < SegmentCount; ++i)
                {
                    for (int j = 1; j <= perSegment; ++j)
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

            public CacheHandler(Spline spline, float cacheStepDistance, int maxPositionCount, int segmentLookupCount)
            {
                this.cacheStepDistance = cacheStepDistance;

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
