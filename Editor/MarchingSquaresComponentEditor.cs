using UnityEngine;
using UnityEditor;

using static MeshBuilder.MarchingSquaresComponent;

namespace MeshBuilder
{
    [CustomEditor(typeof(MarchingSquaresComponent))]
    public class MarchingSquaresComponentEditor : Editor
    {
        private class PropName
        {
            public const string MeshFilter = "meshFilter";
            public const string InitializationPolicy = "initializationPolicy";
            public const string Initialization = "initialization";
            public const string DataCreationPolicy = "dataCreationPolicy";
            public const string DataCreationInfo = "dataCreationInfo";
        }

        private class Props
        {
            public SerializedProperty meshFilter;
            public SerializedProperty initializationPolicy;
            public SerializedProperty initialization;
            public SerializedProperty dataCreationPolicy;
            public SerializedProperty dataCreationInfo;

            public void FindProps(SerializedObject serializedObject)
            {
                meshFilter = serializedObject.FindProperty(PropName.MeshFilter);
                initializationPolicy = serializedObject.FindProperty(PropName.InitializationPolicy);
                initialization = serializedObject.FindProperty(PropName.Initialization);
                dataCreationPolicy = serializedObject.FindProperty(PropName.DataCreationPolicy);
                dataCreationInfo = serializedObject.FindProperty(PropName.DataCreationInfo);
            }
        }

        private MarchingSquaresComponent marchingSquares;
        private Props props;
        
        private void OnEnable()
        {
            marchingSquares = (MarchingSquaresComponent)target;
            props = new Props();
            props.FindProps(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(props.meshFilter);
            EditorGUILayout.PropertyField(props.initializationPolicy);
            EditorGUILayout.PropertyField(props.initialization);
            EditorGUILayout.PropertyField(props.dataCreationPolicy);

            DataManagementPolicy dataPolicy = (DataManagementPolicy)props.dataCreationPolicy.enumValueIndex;
            if (dataPolicy == DataManagementPolicy.CreateOwn)
            {
                EditorGUILayout.PropertyField(props.dataCreationInfo);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
