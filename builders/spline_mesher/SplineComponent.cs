using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    [ExecuteInEditMode]
    public class SplineComponent : MonoBehaviour
    {
        public enum ArcType
        {
            Linear,
            CatmullRom,
            Bezier
        }

        [SerializeField] private bool autoRedraw = false;
        public bool AutoRedraw { get => autoRedraw; set => autoRedraw = value; }

        [Header("mesh")]
        [SerializeField] private MeshFilter meshFilter;

        [Header("spline")]
        [SerializeField] private bool isClosed;
        public bool IsClosed { get; private set; }

        [SerializeField] private ArcType arcType = ArcType.CatmullRom;
        public ArcType Type { get => arcType; set { arcType = value; SetDirty(); } }

        [SerializeField] private Spline.CatmullRom catmullRomArc = null;
        public Spline.CatmullRom CatmullRomArc { get => catmullRomArc; set { catmullRomArc = value; SetDirty(); } }

        [SerializeField] private Spline.Bezier bezierArc = null;
        public Spline.Bezier BezerArc { get => BezerArc; set { bezierArc = value; SetDirty(); } }

        private Vector3[] controlPoints;
        private SplineMeshBuilder.CrossSectionData crossSection;
        private Spline spline;

        public bool IsDirty { get; private set; }

        void Awake()
        {
            spline = new Spline();
            spline.AutoRecalculate = false;
        }

        private void SetDirty() 
        { 
            IsDirty = true; 
            if (autoRedraw)
            {
                Redraw();
            }
        }

        public void Redraw()
        {
            if (controlPoints != null)
            {
                spline.ControlPoints = controlPoints;
                spline.Arc = SelectArc();

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
            SetDirty();
        }

        public void SetControlPoint(int index, Vector3 pos)
        {
            controlPoints[index] = pos;
            SetDirty();
        }
        
        public void SetCrossSection(Vector3[] points, float[] uCoords = null)
        {
            var array = Utils.ToFloat3Array(points);
            crossSection = new SplineMeshBuilder.CrossSectionData(array, uCoords);
            SetDirty();
        }
    }
}

