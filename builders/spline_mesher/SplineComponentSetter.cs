using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MeshBuilder
{
    public class SplineComponentSetter : MonoBehaviour
    {
        public enum SettingType
        {
            Nothing,
            FromVectorArray,
            FromTransformArray,
            FromChildren
        }

        [SerializeField] private SplineComponent spline = null;

        [Header("cross section")]
        [SerializeField] private SettingType crossSectionSetting = SettingType.Nothing;
        [SerializeField] private Vector3[] crossSectionPoints = null;
        [SerializeField] private Transform[] crossSectionTransforms = null;
        [SerializeField] private Transform crossSectionParent = null;

        [Header("spline path")]
        [SerializeField] private SettingType splinePathSetting = SettingType.Nothing;
        [SerializeField] private Vector3[] splinePathPoints = null;
        [SerializeField] private Transform[] splinePathTransforms = null;
        [SerializeField] private Transform splinePathParent = null;


    }
}
