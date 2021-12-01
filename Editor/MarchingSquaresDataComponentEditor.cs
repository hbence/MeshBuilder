using UnityEngine;
using UnityEditor;

using static MeshBuilder.MarchingSquaresDataComponent;

namespace MeshBuilder
{
    [CustomEditor(typeof(MarchingSquaresDataComponent))]
    public class MarchingSquaresDataComponentEditor : Editor
    {
        private class PropName
        {
            public const string CreationInfo = "creationInfo";
            public const string InitPolicy = "initPolicy";
            public const string SavePolicy = "savePolicy";
            public const string SerializationPolicy = "serializationPolicy";
            public const string DataAsset = "dataAsset";
            public const string BinaryDataPath = "binaryDataPath";
        }

        private class Props
        {
            public SerializedProperty creationInfo;
            public SerializedProperty initPolicy;
            public SerializedProperty savePolicy;
            public SerializedProperty serializationPolicy;
            public SerializedProperty dataAsset;
            public SerializedProperty binaryDataPath;

            public void FindProps(SerializedObject serializedObject)
            {
                creationInfo = serializedObject.FindProperty(PropName.CreationInfo);
                initPolicy = serializedObject.FindProperty(PropName.InitPolicy);
                savePolicy = serializedObject.FindProperty(PropName.SavePolicy);
                serializationPolicy = serializedObject.FindProperty(PropName.SerializationPolicy);
                dataAsset = serializedObject.FindProperty(PropName.DataAsset);
                binaryDataPath = serializedObject.FindProperty(PropName.BinaryDataPath);
            }
        }

        private MarchingSquaresDataComponent marchingSquaresData;
        private Props props;
        
        private void OnEnable()
        {
            marchingSquaresData = (MarchingSquaresDataComponent)target;
            props = new Props();
            props.FindProps(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(props.creationInfo);
            EditorGUILayout.PropertyField(props.initPolicy);
            EditorGUILayout.PropertyField(props.savePolicy);
            EditorGUILayout.PropertyField(props.serializationPolicy);
            
            SerializationPolicy serialization = (SerializationPolicy)props.serializationPolicy.enumValueIndex;
            if (serialization == SerializationPolicy.Binary)
            {
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(props.binaryDataPath);
            }
            else if (serialization == SerializationPolicy.DataAsset)
            {
                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(props.dataAsset);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
