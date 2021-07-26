using Unity.Mathematics;
using UnityEngine;

namespace MeshBuilder
{
    public class SplineComponent : MonoBehaviour
    {
        [SerializeField] private Spline spline = null;
        public Spline Spline => spline;

        public Vector3[] ControlPoints
        {
            get => spline.ControlPoints;
            set
            {
                spline.CopyControlPointsFrom(value);
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
        [SerializeField] private float cacheStepDistance = 0.1f;
        [Header("control points")]
        [SerializeField] private bool updateControlPointsFromChildren = false;
        [Header("debug")]
        [SerializeField] private bool drawGizmo = true;
        [SerializeField] private bool drawControlPoints = true;
        [SerializeField] private Color controlPointsColor = Color.blue;
        [SerializeField] private Color lineColor = Color.blue;

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
            if (updateControlPointsFromChildren)
            {
                UpdateControlPointsFromChildren();
            }

            if (cache == null)
            {
                cache = new CacheHandler(spline, cacheStepDistance, maxCachePositionCount, segmentLookupCount);
            }
            else
            {
                cache.Update(spline, cacheStepDistance);
            }
        }

        private void OnTransformChildrenChanged()
        {
            if (updateControlPointsFromChildren)
            {
                UpdateControlPointsFromChildren();
            }
        }

        private void UpdateControlPointsFromChildren()
        {
            if (spline.ControlPoints == null || spline.ControlPointsCount != transform.childCount)
            {
                Vector3[] controlPoints = new Vector3[transform.childCount];
                for(int i = 0; i < transform.childCount; ++i)
                {
                    controlPoints[i] = transform.GetChild(i).position;
                }
                spline.CopyControlPointsFrom(controlPoints);
            }
            else
            {
                for (int i = 0; i < transform.childCount; ++i)
                {
                    spline.SetControlPoint(i, transform.GetChild(i).position);
                }
            }

            UpdateCache();
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
            if (updateControlPointsFromChildren)
            {
                UpdateControlPointsFromChildren();
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

        private void OnDrawGizmos()
        {
            if (drawGizmo)
            {
                if (drawControlPoints)
                {
                    Gizmos.color = controlPointsColor;
                    if (spline != null && spline.ControlPoints != null)
                    {
                        for (int i = 0; i < spline.ControlPointsCount; ++i)
                        {
                            Gizmos.DrawSphere(spline.GetControlPoint(i), 0.5f);
                        }
                    }
                }

                if (cache != null)
                {
                    cache.DebugDraw(lineColor);
                }
                else
                {
                    DrawNonCachedGizmo();
                }
            }
        }

        private void DrawNonCachedGizmo()
        {
            Gizmos.color = lineColor;
            if (updateControlPointsFromChildren)
            {
                if (transform.childCount > 1)
                {
                    Vector3[] controlPoints = new Vector3[transform.childCount];
                    for (int i = 0; i < controlPoints.Length; ++i)
                    {
                        controlPoints[i] = transform.GetChild(i).position;
                    }
                    spline.CopyControlPointsFrom(controlPoints);
                }
            }

            if (spline.ControlPoints != null && spline.ControlPointsCount > 1)
            {
                var controlPoints = Utils.ToFloat3Array(spline.ControlPoints);
                int perSegment = 10;
                float step = 1f / perSegment;
                float3 prev = spline.ControlPoints[0];
                int segmentCount = spline.IsClosed ? spline.ControlPointsCount : spline.ControlPointsCount - 1;
                for (int i = 0; i < segmentCount; ++i)
                {
                    for (int j = 1; j <= perSegment; ++j)
                    {
                        float3 cur = ArcCalculator.Calculate(controlPoints, i, (i + 1) % spline.ControlPointsCount, step * j, spline.IsClosed);
                        Gizmos.DrawLine(prev, cur);
                        prev = cur;
                    }
                }
            }
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
