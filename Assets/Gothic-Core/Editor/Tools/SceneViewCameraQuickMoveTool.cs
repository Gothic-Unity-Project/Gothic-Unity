using System;
using GUZ.Core.Logging;
using UnityEditor;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Editor.Tools
{
    public class SceneViewCameraQuickMoveTool : EditorWindow
    {
        private static readonly CameraPosition[] cameraPositions = new[]
        {
            new CameraPosition("World - Diego Start", new Vector3(54.88f, 54.62f, 364.34f), new Vector3(5.78f, 191.21f, 0.00f)),

            new CameraPosition("Lab - NpcDialogHandler", new Vector3(-6.16f, 1.43f, 2.75f), new Vector3(6.61f, 355.26f, 0.00f)),
        };


        private int _selectedPositionIndex;


        [MenuItem("UnZENity/Debug/SceneView/Move to Position", priority = 200)]
        private static void SetSceneCameraPosition()
        {
            var window = CreateInstance<SceneViewCameraQuickMoveTool>();
            window.position = new Rect(0, 0, 250, 100);
            window.ShowPopup();
        }

        [MenuItem("UnZENity/Debug/SceneView/Capture Camera Position", priority = 201)]
        private static void CaptureSceneCameraPosition()
        {
            var sceneView = SceneView.lastActiveSceneView;
            Logger.LogEditor("Current Scene Camera Position+Rotation:", LogCat.Debug);
            Logger.LogEditor(sceneView.camera.transform.position.ToString(), LogCat.Debug);
            Logger.LogEditor(sceneView.camera.transform.rotation.eulerAngles.ToString(), LogCat.Debug);
        }


        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Select Camera Position", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // Create dropdown
            _selectedPositionIndex = EditorGUILayout.Popup("Position", _selectedPositionIndex,
                Array.ConvertAll(cameraPositions, pos => pos.Name));

            EditorGUILayout.Space(10);

            // Add Apply and Close buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                ApplySelectedPosition();
            }
            if (GUILayout.Button("Close"))
            {
                this.Close();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ApplySelectedPosition()
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                return;
            }

            var selectedPosition = cameraPositions[_selectedPositionIndex];
            sceneView.pivot = selectedPosition.Position;
            sceneView.rotation = Quaternion.Euler(selectedPosition.Rotation);
            sceneView.Repaint();
        }

        // Helper class to store camera position presets
        private class CameraPosition
        {
            public string Name { get; }
            public Vector3 Position { get; }
            public Vector3 Rotation { get; }

            public CameraPosition(string name, Vector3 position, Vector3 rotation)
            {
                Name = name;
                Position = position;
                Rotation = rotation;
            }
        }
    }
}
