using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    [ExecuteInEditMode]
    public class SplineConnectChildren : MonoBehaviour
    {
        [SerializeField] private bool autoRefresh = true;
        public bool AutoRefresh { get => autoRefresh; set { autoRefresh = value; } }

        [SerializeField] private SplineComponent splineComponent = null;

        private List<TransformCache> childTransforms;

        void Awake()
        {
            childTransforms = new List<TransformCache>();
            CheckChildren();
        }

        void LateUpdate()
        {
            if (autoRefresh)
            {
                Refresh();
            }
        }

        public void Refresh()
        {
            CheckChildren();
        }

        private void CheckChildren()
        {
            bool update = false;

            if (childTransforms.Count != transform.childCount)
            {
                SetChildTrasnformsCount(transform.childCount);
                for (int i = 0;  i < transform.childCount; ++i)
                {
                    childTransforms[i].SetFrom(transform.GetChild(i));
                }
            }
            else
            {
                for (int i = 0; i < transform.childCount; ++i)
                {
                    if (update)
                    {
                        childTransforms[i].SetFrom(transform.GetChild(i));
                    }
                    else
                    {
                        if (childTransforms[i].DoesMatch(transform.GetChild(i)))
                        {
                            update = true;
                            childTransforms[i].SetFrom(transform.GetChild(i));
                        }
                    }
                }
            }

            if (update)
            {
                UpdateSpline();
            }
        }

        private void UpdateSpline()
        {

        }

        private void SetChildTrasnformsCount(int count)
        {
            while (childTransforms.Count < count)
            {
                childTransforms.Add(new TransformCache());
            }
            while (childTransforms.Count > count)
            {
                childTransforms.RemoveAt(0);
            }
        }

        private class TransformCache
        {
            public Vector3 Position { get; private set; }
            public Vector3 Scale { get; private set; }
            public Quaternion Rotation { get; private set; }

            public TransformCache()
            {

            }

            public TransformCache(Transform t)
            {
                SetFrom(t);
            }

            public bool DoesMatch(Transform t)
            {
                if (Position != t.position) { return false; }
                if (Scale != t.localScale) { return false; }
                if (Rotation != t.localRotation) { return false; }

                return true;
            }

            public void SetFrom(Transform t)
            {
                Position = t.position;
                Scale = t.localScale;
                Rotation = t.localRotation;
            }
        }
    }
}
