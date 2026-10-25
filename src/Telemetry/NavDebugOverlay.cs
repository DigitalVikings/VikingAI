using System;
using System.Collections.Generic;
using UnityEngine;

namespace VikingAI.Telemetry
{
    [Flags]
    public enum DebugDrawMode
    {
        Off         = 0,
        Grid        = 1 << 0,
        Flow        = 1 << 1,
        Steering    = 1 << 2,
        Tokens      = 1 << 3,
        Affordances = 1 << 4,
        Senses      = 1 << 5,
        All         = Grid | Flow | Steering | Tokens | Affordances | Senses
    }

    public struct DebugLineSegment
    {
        public Vector3 Start;
        public Vector3 End;
        public Color Color;
    }

    public struct DebugWorldLabel
    {
        public Vector3 WorldPos;
        public string Text;
        public Color Color;
    }

    /// <summary>
    /// Diagnostic GL and GUI overlay renderer toggled via `vikingai debug [grid|flow|steering|tokens|affordances|senses|off]`.
    /// Zero overhead when ActiveMode == DebugDrawMode.Off.
    /// </summary>
    public sealed class NavDebugOverlay : MonoBehaviour
    {
        public static DebugDrawMode ActiveMode { get; set; } = DebugDrawMode.Off;

        private static readonly List<DebugLineSegment> _frameLines = new List<DebugLineSegment>(512);
        private static readonly List<DebugWorldLabel> _frameLabels = new List<DebugWorldLabel>(128);
        private static Material? _lineMaterial;
        private static int _lastClearedFrame = -1;

        public static bool IsModeActive(DebugDrawMode mode)
        {
            return (ActiveMode & mode) != 0;
        }

        public static void AddLine(Vector3 start, Vector3 end, Color color)
        {
            if (ActiveMode == DebugDrawMode.Off) return;
            EnsureFrameCleared();
            if (_frameLines.Count < 4096)
            {
                _frameLines.Add(new DebugLineSegment { Start = start, End = end, Color = color });
            }
        }

        public static void AddLabel(Vector3 worldPos, string text, Color color)
        {
            if (ActiveMode == DebugDrawMode.Off) return;
            EnsureFrameCleared();
            if (_frameLabels.Count < 256)
            {
                _frameLabels.Add(new DebugWorldLabel { WorldPos = worldPos, Text = text, Color = color });
            }
        }

        private static void EnsureFrameCleared()
        {
            int frame = Time.frameCount;
            if (_lastClearedFrame != frame)
            {
                _lastClearedFrame = frame;
                _frameLines.Clear();
                _frameLabels.Clear();
            }
        }

        private static void EnsureMaterial()
        {
            if (_lineMaterial != null) return;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader != null)
            {
                _lineMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                _lineMaterial.SetInt("_ZWrite", 0);
            }
        }

        private void OnRenderObject()
        {
            if (ActiveMode == DebugDrawMode.Off || _frameLines.Count == 0) return;

            EnsureMaterial();
            if (_lineMaterial == null) return;

            _lineMaterial.SetPass(0);
            GL.PushMatrix();
            GL.Begin(GL.LINES);

            for (int i = 0; i < _frameLines.Count; i++)
            {
                DebugLineSegment seg = _frameLines[i];
                GL.Color(seg.Color);
                GL.Vertex3(seg.Start.x, seg.Start.y, seg.Start.z);
                GL.Vertex3(seg.End.x, seg.End.y, seg.End.z);
            }

            GL.End();
            GL.PopMatrix();
        }

        private void OnGUI()
        {
            if (ActiveMode == DebugDrawMode.Off) return;

            Camera cam = Utils.GetMainCamera();
            if (cam == null) return;

            GUI.color = Color.cyan;
            GUI.Label(new Rect(16f, 16f, 480f, 26f), $"[VikingAI Telemetry] Active Modes: {ActiveMode}");

            for (int i = 0; i < _frameLabels.Count; i++)
            {
                DebugWorldLabel lbl = _frameLabels[i];
                Vector3 screen = cam.WorldToScreenPoint(lbl.WorldPos);
                if (screen.z <= 0f) continue;

                float guiY = Screen.height - screen.y;
                GUI.color = Color.black;
                GUI.Label(new Rect(screen.x + 1f, guiY + 1f, 260f, 44f), lbl.Text);
                GUI.color = lbl.Color;
                GUI.Label(new Rect(screen.x, guiY, 260f, 44f), lbl.Text);
            }
        }
    }
}
