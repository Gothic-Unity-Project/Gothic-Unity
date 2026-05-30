using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GUZ.Core.Logging;
using UnityEditor;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace GUZ.Core.Editor
{
    /// <summary>
    ///
    /// Roger Cabo 2020 V1.0. Transform component extension.
    /// Display real world transform values for child objects automatically.
    /// Reminds fold/unfold option
    ///
    /// found at: https://discussions.unity.com/t/editor-display-global-position-of-a-child-object-in-a-prefab/852116/3
    /// </summary>
    [CustomEditor(typeof(Transform)), CanEditMultipleObjects]
    public class WorldTransform : UIBaseAlignmentInspector {
        bool unfold = false;
        SerializedProperty p;
        SerializedProperty r;
        SerializedProperty s;
        Transform t;

        public WorldTransform() : base("TransformInspector") { }

        void OnEnable() {
            p = serializedObject.FindProperty("m_LocalPosition");
            r = serializedObject.FindProperty("m_LocalRotation");
            s = serializedObject.FindProperty("m_LocalScale");
            t = target as Transform;
            if (EditorPrefs.HasKey("CustomWordTransformUnfold"))
                unfold = EditorPrefs.GetString("CustomWordTransformUnfold") == "True";
        }

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();

            if (p.vector3Value != t.position  || s.vector3Value != t.transform.lossyScale) {
                // || r.vector3Value != t.transform.rotation.eulerAngles
                string originLabel = "Word Transform";
                unfold = EditorGUILayout.Foldout(unfold, originLabel);
                EditorPrefs.SetString("CustomWordTransformUnfold", unfold.ToString());
                if (unfold) {
                    EditorGUILayout.Vector3Field("Position", RoundTo5th(t.position));
                    EditorGUILayout.Vector3Field("Rotation", t.rotation.eulerAngles);
                    EditorGUILayout.Vector3Field("Scale", RoundTo5th(t.lossyScale));
                }
            }
        }

        Vector3 RoundTo5th(Vector3 v3) {
            return new Vector3((float)Math.Round(v3.x, 5), (float)Math.Round(v3.y, 5), (float)Math.Round(v3.z, 5));
        }
    }

    public abstract class UIBaseAlignmentInspector : UnityEditor.Editor {
        // Reflection Array
        private static readonly object[] EMPTY_ARRAY = new object[0];

        #region Editor Fields

        /// <summary>
        /// Type object for the internally used UIBaseAlignmentInspector editor.
        /// </summary>
        private Type AlignmentInspectorType;

        /// <summary>
        /// Type object for the object that is edited by this editor.
        /// </summary>
        private Type editedObjectType;

        private UnityEditor.Editor editorInstance;

        #endregion

        private static Dictionary<string, MethodInfo> decoratedMethods = new Dictionary<string, MethodInfo>();

        private static Assembly editorAssembly = Assembly.GetAssembly(typeof(UnityEditor.Editor));

        protected UnityEditor.Editor EditorInstance {
            get {
                if (editorInstance == null && targets != null && targets.Length > 0) {
                    editorInstance = CreateEditor(targets, AlignmentInspectorType);
                }
                if (editorInstance == null) {
                    Logger.LogErrorEditor("Could not create editor !", LogCat.Debug);
                }
                return editorInstance;
            }
        }

        public UIBaseAlignmentInspector(string editorTypeName) {
            this.AlignmentInspectorType = editorAssembly.GetTypes().Where(t => t.Name == editorTypeName).FirstOrDefault();
            Init();
            var originalEditedType = GetCustomEditorType(AlignmentInspectorType);
            if (originalEditedType != editedObjectType) {
                throw new ArgumentException(
                     string.Format("Type {0} does not match the editor {1} type {2}",
                                  editedObjectType, editorTypeName, originalEditedType));
            }
        }

        private Type GetCustomEditorType(Type type) {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var attributes = type.GetCustomAttributes(typeof(CustomEditor), true) as CustomEditor[];
            var field = attributes.Select(editor => editor.GetType().GetField("m_InspectedType", flags)).First();
            return field.GetValue(attributes[0]) as Type;
        }

        private void Init() {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var attributes = this.GetType().GetCustomAttributes(typeof(CustomEditor), true) as CustomEditor[];
            var field = attributes.Select(editor => editor.GetType().GetField("m_InspectedType", flags)).First();
            editedObjectType = field.GetValue(attributes[0]) as Type;
        }

        void OnDisable() {
            if (editorInstance != null) {
                DestroyImmediate(editorInstance);
            }
        }

        /// <summary>
        /// Delegate by UIBaseAlignmentInspector instance
        /// </summary>
        protected void CallInspectorMethod(string methodName) {
            MethodInfo method = null;
            if (!decoratedMethods.ContainsKey(methodName)) {
                var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
                method = AlignmentInspectorType.GetMethod(methodName, flags);
                if (method != null) {
                    decoratedMethods[methodName] = method;
                } else {
                    Logger.LogErrorEditor(string.Format("Could not find method {0}", methodName), LogCat.Debug);
                }
            } else {
                method = decoratedMethods[methodName];
            }
            if (method != null) {
                method.Invoke(EditorInstance, EMPTY_ARRAY);
            }
        }

        protected override void OnHeaderGUI() {
            CallInspectorMethod("OnHeaderGUI");
        }

        public override void OnInspectorGUI() {
            EditorInstance.OnInspectorGUI();
        }

        public override void DrawPreview(Rect previewArea) {
            EditorInstance.DrawPreview(previewArea);
        }

        public override string GetInfoString() {
            return EditorInstance.GetInfoString();
        }

        public override GUIContent GetPreviewTitle() {
            return EditorInstance.GetPreviewTitle();
        }

        public override bool HasPreviewGUI() {
            return EditorInstance.HasPreviewGUI();
        }

        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background) {
            EditorInstance.OnInteractivePreviewGUI(r, background);
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background) {
            EditorInstance.OnPreviewGUI(r, background);
        }

        public override void OnPreviewSettings() {
            EditorInstance.OnPreviewSettings();
        }

        public override void ReloadPreviewInstances() {
            EditorInstance.ReloadPreviewInstances();
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height) {
            return EditorInstance.RenderStaticPreview(assetPath, subAssets, width, height);
        }

        public override bool RequiresConstantRepaint() {
            return EditorInstance.RequiresConstantRepaint();
        }

        public override bool UseDefaultMargins() {
            return EditorInstance.UseDefaultMargins();
        }
    }
}
