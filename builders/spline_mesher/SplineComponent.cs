using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    [ExecuteInEditMode]
    [Serializable]
    public class SplineComponent : ScriptableObject
    {
        public enum ArcType
        {
            Linear,
            CatmullRom,
            Bezier
        }

        [Header("mesh")]
        [SerializeField] private MeshFilter meshFilter;

        [Header("spline")]
        [SerializeField] private bool isClosed;
        public bool IsClosed { get => isClosed; private set { isClosed = value; IsDirty = true; } }

        [SerializeField] private ArcType arcType = ArcType.CatmullRom;
        public ArcType Type { get => arcType; set { arcType = value; IsDirty = true; } }

        [SerializeField] private Spline.CatmullRom catmullRomArc = null;
        public Spline.CatmullRom CatmullRomArc { get => catmullRomArc; set { catmullRomArc = value; IsDirty = true; } }

        [SerializeField] private Spline.Bezier bezierArc = null;
        public Spline.Bezier BezerArc { get => BezerArc; set { bezierArc = value; IsDirty = true; } }

        [HideInInspector, SerializeField] private Vector3[] controlPoints;
        [HideInInspector, SerializeField] private Spline spline;

        [SerializeField] private bool autoRecalculate = false;
        public bool AutoRecalculate { get => autoRecalculate; set => autoRecalculate = value; }

        private bool isDirty = false;
        public bool IsDirty
        {
            get => isDirty;
            private set
            {
                isDirty = value;
                if (isDirty && autoRecalculate)
                {
                    Recalculate();
                }
            }
        }

        void Awake()
        {
            if (spline == null)
            {
                spline = new Spline();
                spline.AutoRecalculate = false;
            }
        }

        public void Recalculate()
        {
            if (controlPoints != null)
            {
                spline.ControlPoints = controlPoints;
                spline.Arc = SelectArc();
                spline.Recalculate();
            }
            IsDirty = false;
        }

        private Spline.ArcCalculator SelectArc()
        {
            switch(arcType)
            {
                case ArcType.Bezier: return bezierArc;
                case ArcType.CatmullRom: return catmullRomArc;
            }
            return Spline.Linear.Instance;
        }

        public void SetControlPointsCount(int size)
        {
            if (size < 0)
            {
                size = 0;
                Debug.LogWarning("negative controlpoints size");
            }

            Vector3[] newControlPoints = new Vector3[size];
            Array.Copy(controlPoints, newControlPoints, Mathf.Min(controlPoints.Length, size));
            for (int i = controlPoints.Length; i < size; ++i)
            {
                newControlPoints[i] = controlPoints[controlPoints.Length];
            }
            controlPoints = newControlPoints;
            IsDirty = true;
        }

        public void SetControlPoint(int index, Vector3 pos)
        {
            controlPoints[index] = pos;
            IsDirty = true;
        }
    }
}

