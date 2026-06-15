using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Animations;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Domain.Npc.Actions;
using UnityEditor;
using UnityEngine;
using AnimationState = Gothic.Core.Models.Animations.AnimationState;

namespace Gothic.Core.Editor.Tools
{
    public class AnimationSystemWindowTool : EditorWindow
    {
        private Dictionary<string, AnimationSystem> _animationSystems = new();
        private Vector2 _scrollPosition;

        private int _selectedAnimationSystemIndex;
        private AnimationSystem _targetAnimationSystem;

        private float _timeScale = 1f;
        private bool _isTimeScaleFoldedOut;

        private AiHandler _targetAiHandler;
        private bool _isAiHandlerFoldedOut;

        private GUILayoutOption _ultraSmall = GUILayout.Width(20);
        private GUILayoutOption _small = GUILayout.Width(50);
        private GUILayoutOption _medium = GUILayout.Width(75);
        private GUILayoutOption _wide = GUILayout.Width(100);
        private GUILayoutOption _ultraWide = GUILayout.Width(200);



        [MenuItem("Gothic/Debug/Animation System Window", priority = 100)]
        public static void ShowWindow()
        {
            var titleContent = new GUIContent("Animation System", Constants.TextureGothicUnityLogoInverseTransparent);

            var window = GetWindow<AnimationSystemWindowTool>();
            window.titleContent = titleContent;
        }

        private void OnGUI()
        {
            DrawTimeScale();
            DrawNpcSelection();

            if (_targetAnimationSystem == null)
            {
                EditorGUILayout.HelpBox("No AnimationSystem selected in the scene!", MessageType.Warning);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawAiActionInfo();
            DrawBreakpointInfo();
            DrawAnimationInfo();

            EditorGUILayout.EndScrollView();

            // Repaint the window every frame to update the animation state
            Repaint();
        }

        private void DrawNpcSelection()
        {
            EditorGUILayout.LabelField("Select NPC", EditorStyles.boldLabel);

            // NPC dropdown
            _selectedAnimationSystemIndex =
                EditorGUILayout.Popup("NPC", _selectedAnimationSystemIndex, _animationSystems.Keys.ToArray(), GUILayout.Width(400));

            if (_selectedAnimationSystemIndex >= _animationSystems.Count)
            {
                _targetAnimationSystem = null;
            }
            else
            {
                var oldSelectedAnimationSystem = _targetAnimationSystem;
                _targetAnimationSystem = _animationSystems.Values.ElementAt(_selectedAnimationSystemIndex);
                _targetAiHandler = _targetAnimationSystem != null ? _targetAnimationSystem.GetComponent<AiHandler>() : null;

                if (oldSelectedAnimationSystem != _targetAnimationSystem && oldSelectedAnimationSystem != null)
                {
                    // Reset! Otherwise, we will never find again from which NPC the game is pausing.
                    oldSelectedAnimationSystem.DebugPauseAtPlayAnimation = false;
                    oldSelectedAnimationSystem.DebugPauseAtStopAnimation = false;
                }
            }

            var origBack = GUI.backgroundColor;
            // Green == already collected once at least
            GUI.backgroundColor = _animationSystems.Any() ? Color.green : Color.grey;

            EditorGUILayout.BeginHorizontal();
            // Re-Collect AnimationSystems
            if (GUILayout.Button("(Re)collect Animation Systems", _ultraWide))
            {
                var emptyElement = new[] { new { name = "<<Choose NPC>>", animComp = (AnimationSystem)null } };

                var no = 0;
                // Add additional empty element to the Dictionary
                _animationSystems = emptyElement
                    .Concat(FindObjectsByType<AnimationSystem>(FindObjectsSortMode.None)
                        .Select(animComp => new { animComp.GetComponentInParent<NpcLoader>().name, animComp }))
                    .ToDictionary(i => $"#{no++} - {i.name}", i => i.animComp); // We need to have a unique key for the Dict as e.g. Meatbug will be there multiple times.
            }
            GUI.backgroundColor = origBack;

            if (GUILayout.Button("Select in Inspector", _ultraWide))
            {
                if (_targetAnimationSystem != null)
                {
                    Selection.activeObject = _targetAnimationSystem.GetComponentInParent<NpcLoader>();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTimeScale()
        {
            // TimeScale controls in a foldout
            _isTimeScaleFoldedOut = EditorGUILayout.Foldout(_isTimeScaleFoldedOut, "Time Scale Controls", true);
            if (_isTimeScaleFoldedOut)
            {
                // Center the buttons by using flexible space before and after
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace(); // This pushes content to the center

                if (GUILayout.Button("|<", _small))
                {
                    _timeScale = 0f;
                }
                if (GUILayout.Button("<<", _small))
                {
                    _timeScale -= 0.1f;
                }
                if (GUILayout.Button("<", _small))
                {
                    _timeScale -= 0.01f;
                }

                var origBack = GUI.backgroundColor;

                // Green == We have the normal 1f timeScale active (Helps finding issues if it's not reset to 1 after use)
                GUI.backgroundColor = !Mathf.Approximately(Time.timeScale, 1f) ? Color.grey : Color.green;
                if (GUILayout.Button("1", _small))
                {
                    _timeScale = 1f;
                }
                GUI.backgroundColor = origBack;

                if (GUILayout.Button(">", _small))
                {
                    _timeScale += 0.01f;
                }
                if (GUILayout.Button(">>", _small))
                {
                    _timeScale += 0.1f;
                }
                if (GUILayout.Button(">|", _small))
                {
                    _timeScale = 2f;
                }

                GUILayout.FlexibleSpace(); // This pushes content to the center
                EditorGUILayout.EndHorizontal();

                // Show slider inside the foldout, also centered
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Time Scale:", GUILayout.Width(70));
                _timeScale = EditorGUILayout.Slider(_timeScale, 0, 2, GUILayout.Width(200));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                // Show current timescale value with a label
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"Current Value: {_timeScale:F2}", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                DrawDivider();
            }

            // Apply the timescale value (outside the foldout so it's always applied)
            Time.timeScale = _timeScale;
        }


        private void DrawAiActionInfo()
        {
            // TimeScale controls in a foldout
            _isAiHandlerFoldedOut = EditorGUILayout.Foldout(_isAiHandlerFoldedOut, "AiHandler History", true);
            if (!_isAiHandlerFoldedOut)
                return;
                
            // _targetAiHandler.AiActionHistory
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("#", _ultraSmall);
            EditorGUILayout.LabelField("AiAction", _wide);
            EditorGUILayout.LabelField("Int0", _small);
            EditorGUILayout.LabelField("Int1", _small);
            EditorGUILayout.LabelField("Bool", _small);
            EditorGUILayout.LabelField("String", _ultraWide);
            EditorGUILayout.EndHorizontal();

            for (var i = _targetAiHandler.AiActionHistory.Count-1; i >= 0; i--)
            {
                (string name, AnimationAction action) entry = _targetAiHandler.AiActionHistory[i];

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(i.ToString(), _ultraSmall);
                EditorGUILayout.LabelField(entry.name, _wide);
                EditorGUILayout.LabelField(entry.action.Int0.ToString(), _small);
                EditorGUILayout.LabelField(entry.action.Int1.ToString(), _small);
                EditorGUILayout.LabelField(entry.action.Bool0.ToString(), _small);
                EditorGUILayout.LabelField(entry.action.String0, _ultraWide);
                EditorGUILayout.EndHorizontal();
            }

            DrawDivider();
        }
        
        private void DrawBreakpointInfo()
        {
            var origBackgroundColor = GUI.backgroundColor;

            DrawDivider();
            EditorGUILayout.LabelField("Breakpoints", EditorStyles.boldLabel);

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = _targetAnimationSystem.DebugPauseAtPlayAnimation ? Color.green : origBackgroundColor;
            if (GUILayout.Button("PlayAnimation", _wide))
            {
                _targetAnimationSystem.DebugPauseAtPlayAnimation = !_targetAnimationSystem.DebugPauseAtPlayAnimation;
            }

            GUI.backgroundColor = _targetAnimationSystem.DebugPauseAtStopAnimation ? Color.green : origBackgroundColor;
            if (GUILayout.Button("StopAnimation", _wide))
            {
                _targetAnimationSystem.DebugPauseAtStopAnimation = !_targetAnimationSystem.DebugPauseAtStopAnimation;
            }
            GUILayout.EndHorizontal();

            GUI.backgroundColor = origBackgroundColor;
        }

        private void DrawAnimationInfo()
        {
            DrawDivider();

            var originalBackgroundColor = GUI.backgroundColor;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Layer - Animation", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Time x/y - State", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Blend Weight", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // Access the currently playing animations from selected AnimationSystem.
            // Per-bone weights are gone: bone subsets are handled per track inside AnimationPoseJob.
            foreach (var trackInstance in _targetAnimationSystem.DebugTrackInstances)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{trackInstance.Track.Layer:D2} - {trackInstance.Track.AliasName ?? trackInstance.Track.Name}");
                EditorGUILayout.LabelField(
                    $"{trackInstance.CurrentTime:F2} / {trackInstance.Track.Duration:F2} - {trackInstance.State}");

                switch (trackInstance.State)
                {
                    case AnimationState.None:
                    case AnimationState.BlendIn:
                    case AnimationState.Play:
                        GUI.backgroundColor = Color.Lerp(Color.red, Color.green, trackInstance.Weight);
                        break;
                    case AnimationState.BlendOut:
                    case AnimationState.Stop:
                        GUI.backgroundColor = Color.Lerp(Color.grey, Color.green, trackInstance.Weight);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                var progressRect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                EditorGUI.ProgressBar(progressRect, trackInstance.Weight, $"{trackInstance.State}({trackInstance.Weight:F2})");
                GUI.backgroundColor = originalBackgroundColor;

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawDivider()
        {
            EditorGUILayout.Space(5);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, Color.grey);
            EditorGUILayout.Space(5);
        }
    }
}
