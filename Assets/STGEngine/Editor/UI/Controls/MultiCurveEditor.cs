using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using STGEngine.Core.Serialization;

namespace STGEngine.Editor.UI.Controls
{
    /// <summary>
    /// 多通道曲线编辑器。在同一图表中叠加显示多条 SerializableCurve，
    /// 支持关键帧拖拽、缩放/平移、添加/删除关键帧。
    /// </summary>
    public class MultiCurveEditor : VisualElement
    {
        private const float DefaultHeight = 200f;
        private const float ToolbarHeight = 28f;
        private const float LeftAxisWidth = 56f;
        private const float BottomAxisHeight = 24f;
        private const float KeyframeSize = 6f;
        private const float MinTimeRange = 0.1f;
        private const float MinValueRange = 0.1f;

        private readonly VisualElement _toolbar;
        private readonly VisualElement _graph;
        private readonly List<CurveEntry> _curves = new();
        private readonly List<Toggle> _curveToggles = new();
        private readonly List<Label> _overlayLabels = new();

        private Rect _viewRect = new(0f, -1f, 5f, 2f);
        private bool _isPanning;
        private bool _isDraggingKeyframe;
        private Vector2 _lastMousePosition;
        private SelectedKeyframe _selectedKeyframe;
        private Vector2 _contextMouseLocal;
        private int _contextCurveIndex = -1;
        private int _contextKeyframeIndex = -1;

        /// <summary>当用户编辑任意曲线后触发。</summary>
        public event Action OnCurvesChanged;

        /// <summary>
        /// 创建多曲线编辑器。
        /// </summary>
        public MultiCurveEditor()
        {
            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1;
            style.height = DefaultHeight;
            style.minHeight = DefaultHeight;
            style.backgroundColor = new Color(0.12f, 0.12f, 0.12f);

            _toolbar = new VisualElement();
            _toolbar.style.flexDirection = FlexDirection.Row;
            _toolbar.style.flexWrap = Wrap.Wrap;
            _toolbar.style.minHeight = ToolbarHeight;
            _toolbar.style.paddingLeft = 6;
            _toolbar.style.paddingRight = 6;
            _toolbar.style.paddingTop = 4;
            _toolbar.style.paddingBottom = 4;
            _toolbar.style.backgroundColor = new Color(0.14f, 0.14f, 0.14f);
            _toolbar.style.borderBottomWidth = 1;
            _toolbar.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
            hierarchy.Add(_toolbar);

            _graph = new VisualElement();
            _graph.style.flexGrow = 1;
            _graph.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
            _graph.focusable = true;
            _graph.generateVisualContent += OnGenerateVisualContent;
            hierarchy.Add(_graph);

            _graph.RegisterCallback<WheelEvent>(OnWheel);
            _graph.RegisterCallback<MouseDownEvent>(OnMouseDown);
            _graph.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            _graph.RegisterCallback<MouseUpEvent>(OnMouseUp);
            _graph.RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
            _graph.RegisterCallback<ContextualMenuPopulateEvent>(OnContextMenuPopulate);
        }

        /// <summary>
        /// 设置要显示和编辑的曲线集合。
        /// </summary>
        public void SetCurves(IReadOnlyList<(string name, SerializableCurve curve, Color color)> curves)
        {
            _curves.Clear();
            if (curves != null)
            {
                foreach (var (name, curve, color) in curves)
                {
                    _curves.Add(new CurveEntry
                    {
                        Name = name,
                        Curve = curve,
                        Color = color,
                        Visible = true
                    });
                }
            }

            RebuildToolbar();
            AutoFitView();
            _selectedKeyframe = default;
            MarkDirtyRepaint();
        }

        /// <summary>
        /// 将数据坐标转换为图表区域内的本地像素坐标。
        /// </summary>
        public Vector2 DataToLocal(float time, float value)
        {
            Rect plotRect = GetPlotRect();
            if (plotRect.width <= 0f || plotRect.height <= 0f)
                return Vector2.zero;

            float x = plotRect.xMin + (time - _viewRect.xMin) / Mathf.Max(_viewRect.width, MinTimeRange) * plotRect.width;
            float y = plotRect.yMax - (value - _viewRect.yMin) / Mathf.Max(_viewRect.height, MinValueRange) * plotRect.height;
            return new Vector2(x, y);
        }

        /// <summary>
        /// 将图表区域内的本地像素坐标转换为数据坐标。
        /// </summary>
        public (float time, float value) LocalToData(Vector2 localPos)
        {
            Rect plotRect = GetPlotRect();
            if (plotRect.width <= 0f || plotRect.height <= 0f)
                return (0f, 0f);

            float tx = Mathf.InverseLerp(plotRect.xMin, plotRect.xMax, localPos.x);
            float ty = Mathf.InverseLerp(plotRect.yMax, plotRect.yMin, localPos.y);

            float time = Mathf.Lerp(_viewRect.xMin, _viewRect.xMax, tx);
            float value = Mathf.Lerp(_viewRect.yMin, _viewRect.yMax, ty);
            return (time, value);
        }

        /// <summary>
        /// 命中测试最近的关键帧。
        /// 返回对应曲线中的关键帧索引，未命中则返回 -1。
        /// </summary>
        public int HitTestKeyframe(Vector2 localPos, float radius)
        {
            var hit = HitTestKeyframeDetailed(localPos, radius, visibleOnly: true);
            return hit.keyframeIndex;
        }

        private void RebuildToolbar()
        {
            _toolbar.Clear();
            _curveToggles.Clear();

            for (int i = 0; i < _curves.Count; i++)
            {
                int curveIndex = i;
                CurveEntry entry = _curves[curveIndex];

                var toggle = new Toggle(entry.Name)
                {
                    value = entry.Visible
                };

                toggle.style.marginRight = 8;
                toggle.style.marginBottom = 4;
                toggle.style.unityFontStyleAndWeight = FontStyle.Bold;
                toggle.style.color = entry.Color;

                toggle.RegisterValueChangedCallback(evt =>
                {
                    _curves[curveIndex].Visible = evt.newValue;
                    if (evt.newValue)
                        AutoFitView();
                    MarkDirtyRepaint();
                });

                _curveToggles.Add(toggle);
                _toolbar.Add(toggle);
            }
        }

        private void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            ClearOverlayLabels();

            var painter = ctx.painter2D;
            Rect plotRect = GetPlotRect();
            if (plotRect.width <= 1f || plotRect.height <= 1f)
                return;

            DrawBackground(painter, _graph.contentRect);
            DrawGrid(painter, plotRect);
            DrawAxesAndLabels(painter, plotRect);
            DrawCurves(painter, plotRect);
            DrawKeyframes(painter);
        }

        private void DrawBackground(Painter2D painter, Rect rect)
        {
            painter.fillColor = new Color(0.1f, 0.1f, 0.1f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(new Vector2(rect.xMax, rect.yMax));
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.Fill();
        }

        private void DrawGrid(Painter2D painter, Rect plotRect)
        {
            float timeStep = GetNiceStep(_viewRect.width / 8f);
            float valueStep = GetNiceStep(_viewRect.height / 6f);

            painter.lineWidth = 1f;
            painter.strokeColor = new Color(0.24f, 0.24f, 0.24f, 0.8f);

            float timeStart = Mathf.Floor(_viewRect.xMin / timeStep) * timeStep;
            for (float t = timeStart; t <= _viewRect.xMax + timeStep * 0.5f; t += timeStep)
            {
                Vector2 p0 = DataToLocal(t, _viewRect.yMin);
                Vector2 p1 = DataToLocal(t, _viewRect.yMax);
                painter.BeginPath();
                painter.MoveTo(new Vector2(p0.x, plotRect.yMin));
                painter.LineTo(new Vector2(p1.x, plotRect.yMax));
                painter.Stroke();
            }

            float valueStart = Mathf.Floor(_viewRect.yMin / valueStep) * valueStep;
            for (float v = valueStart; v <= _viewRect.yMax + valueStep * 0.5f; v += valueStep)
            {
                Vector2 p0 = DataToLocal(_viewRect.xMin, v);
                Vector2 p1 = DataToLocal(_viewRect.xMax, v);
                painter.BeginPath();
                painter.MoveTo(new Vector2(plotRect.xMin, p0.y));
                painter.LineTo(new Vector2(plotRect.xMax, p1.y));
                painter.Stroke();
            }
        }

        private void DrawAxesAndLabels(Painter2D painter, Rect plotRect)
        {
            painter.lineWidth = 1.5f;
            painter.strokeColor = new Color(0.42f, 0.42f, 0.42f);

            painter.BeginPath();
            painter.MoveTo(new Vector2(plotRect.xMin, plotRect.yMin));
            painter.LineTo(new Vector2(plotRect.xMin, plotRect.yMax));
            painter.LineTo(new Vector2(plotRect.xMax, plotRect.yMax));
            painter.Stroke();

            float timeStep = GetNiceStep(_viewRect.width / 8f);
            float valueStep = GetNiceStep(_viewRect.height / 6f);
            float timeStart = Mathf.Floor(_viewRect.xMin / timeStep) * timeStep;
            float valueStart = Mathf.Floor(_viewRect.yMin / valueStep) * valueStep;

            for (float t = timeStart; t <= _viewRect.xMax + timeStep * 0.5f; t += timeStep)
            {
                Vector2 p = DataToLocal(t, _viewRect.yMin);
                DrawText($"{t:0.##}", new Vector2(p.x - 14f, plotRect.yMax + 4f), 10, new Color(0.72f, 0.72f, 0.72f));
            }

            for (float v = valueStart; v <= _viewRect.yMax + valueStep * 0.5f; v += valueStep)
            {
                Vector2 p = DataToLocal(_viewRect.xMin, v);
                DrawText($"{v:0.##}", new Vector2(4f, p.y - 8f), 10, new Color(0.72f, 0.72f, 0.72f));
            }
        }

        private void DrawCurves(Painter2D painter, Rect plotRect)
        {
            float sampleStep = Mathf.Max(1f, 2f / Mathf.Max(plotRect.width, 1f) * _viewRect.width);

            foreach (CurveEntry entry in _curves)
            {
                if (!entry.Visible || entry.Curve == null || entry.Curve.Keyframes.Count == 0)
                    continue;

                painter.lineWidth = 2f;
                painter.strokeColor = entry.Color;
                painter.BeginPath();

                bool started = false;
                for (float t = _viewRect.xMin; t <= _viewRect.xMax; t += sampleStep)
                {
                    float value = entry.Curve.Evaluate(t);
                    Vector2 p = DataToLocal(t, value);
                    if (!started)
                    {
                        painter.MoveTo(p);
                        started = true;
                    }
                    else
                    {
                        painter.LineTo(p);
                    }
                }

                float finalValue = entry.Curve.Evaluate(_viewRect.xMax);
                painter.LineTo(DataToLocal(_viewRect.xMax, finalValue));
                painter.Stroke();
            }
        }

        private void DrawKeyframes(Painter2D painter)
        {
            for (int curveIndex = 0; curveIndex < _curves.Count; curveIndex++)
            {
                CurveEntry entry = _curves[curveIndex];
                if (!entry.Visible || entry.Curve == null)
                    continue;

                for (int keyIndex = 0; keyIndex < entry.Curve.Keyframes.Count; keyIndex++)
                {
                    CurveKeyframe keyframe = entry.Curve.Keyframes[keyIndex];
                    Vector2 p = DataToLocal(keyframe.Time, keyframe.Value);

                    float size = KeyframeSize;
                    bool selected = _selectedKeyframe.IsValid &&
                                    _selectedKeyframe.CurveIndex == curveIndex &&
                                    _selectedKeyframe.KeyframeIndex == keyIndex;
                    if (selected)
                        size = KeyframeSize + 2f;

                    Color fillColor = selected ? Color.white : entry.Color;
                    Color strokeColor = selected ? entry.Color : new Color(0f, 0f, 0f, 0.8f);
                    DrawDiamond(painter, p, size, fillColor, strokeColor);
                }
            }
        }

        private void DrawDiamond(Painter2D painter, Vector2 center, float size, Color fillColor, Color strokeColor)
        {
            Vector2 top = new(center.x, center.y - size);
            Vector2 right = new(center.x + size, center.y);
            Vector2 bottom = new(center.x, center.y + size);
            Vector2 left = new(center.x - size, center.y);

            painter.fillColor = fillColor;
            painter.BeginPath();
            painter.MoveTo(top);
            painter.LineTo(right);
            painter.LineTo(bottom);
            painter.LineTo(left);
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = strokeColor;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(top);
            painter.LineTo(right);
            painter.LineTo(bottom);
            painter.LineTo(left);
            painter.ClosePath();
            painter.Stroke();
        }

        private void DrawText(string text, Vector2 position, int fontSize, Color color)
        {
            var label = new Label(text);
            label.style.position = Position.Absolute;
            label.style.left = position.x;
            label.style.top = position.y;
            label.style.fontSize = fontSize;
            label.style.color = color;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.backgroundColor = Color.clear;
            label.pickingMode = PickingMode.Ignore;
            _overlayLabels.Add(label);
            _graph.Add(label);
            label.BringToFront();
        }

        private void ClearOverlayLabels()
        {
            for (int i = 0; i < _overlayLabels.Count; i++)
            {
                if (_overlayLabels[i].parent != null)
                    _overlayLabels[i].RemoveFromHierarchy();
            }

            _overlayLabels.Clear();
        }

        private void OnWheel(WheelEvent evt)
        {
            Rect plotRect = GetPlotRect();
            if (!plotRect.Contains(evt.localMousePosition))
                return;

            var (cursorTime, cursorValue) = LocalToData(evt.localMousePosition);
            float zoomFactor = evt.delta.y > 0f ? 1.1f : 0.9f;

            float newWidth = Mathf.Max(MinTimeRange, _viewRect.width * zoomFactor);
            float newHeight = Mathf.Max(MinValueRange, _viewRect.height * zoomFactor);

            float tx = Mathf.InverseLerp(plotRect.xMin, plotRect.xMax, evt.localMousePosition.x);
            float ty = Mathf.InverseLerp(plotRect.yMax, plotRect.yMin, evt.localMousePosition.y);

            _viewRect.xMin = cursorTime - tx * newWidth;
            _viewRect.width = newWidth;
            _viewRect.yMin = cursorValue - ty * newHeight;
            _viewRect.height = newHeight;

            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            _graph.Focus();
            _lastMousePosition = evt.localMousePosition;

            if (evt.button == 2)
            {
                _isPanning = true;
                _graph.CaptureMouse();
                evt.StopPropagation();
                return;
            }

            if (evt.button == 0)
            {
                var hit = HitTestKeyframeDetailed(evt.localMousePosition, 8f, visibleOnly: true);
                if (hit.curveIndex >= 0)
                {
                    _selectedKeyframe = new SelectedKeyframe(hit.curveIndex, hit.keyframeIndex);
                    _isDraggingKeyframe = true;
                    _graph.CaptureMouse();
                    MarkDirtyRepaint();
                    evt.StopPropagation();
                    return;
                }

                _selectedKeyframe = default;
                MarkDirtyRepaint();
            }
        }

        private void OnMouseMove(MouseMoveEvent evt)
        {
            Vector2 delta = evt.localMousePosition - _lastMousePosition;
            _lastMousePosition = evt.localMousePosition;

            if (_isPanning)
            {
                Rect plotRect = GetPlotRect();
                if (plotRect.width > 0f && plotRect.height > 0f)
                {
                    float dt = -delta.x / plotRect.width * _viewRect.width;
                    float dv = delta.y / plotRect.height * _viewRect.height;
                    _viewRect.x += dt;
                    _viewRect.y += dv;
                    MarkDirtyRepaint();
                }
                evt.StopPropagation();
                return;
            }

            if (_isDraggingKeyframe && _selectedKeyframe.IsValid)
            {
                CurveEntry entry = _curves[_selectedKeyframe.CurveIndex];
                if (entry.Curve == null || _selectedKeyframe.KeyframeIndex < 0 || _selectedKeyframe.KeyframeIndex >= entry.Curve.Keyframes.Count)
                    return;

                var (time, value) = LocalToData(evt.localMousePosition);
                CurveKeyframe keyframe = entry.Curve.Keyframes[_selectedKeyframe.KeyframeIndex];

                float minTime = _selectedKeyframe.KeyframeIndex > 0
                    ? entry.Curve.Keyframes[_selectedKeyframe.KeyframeIndex - 1].Time + 0.0001f
                    : float.NegativeInfinity;
                float maxTime = _selectedKeyframe.KeyframeIndex < entry.Curve.Keyframes.Count - 1
                    ? entry.Curve.Keyframes[_selectedKeyframe.KeyframeIndex + 1].Time - 0.0001f
                    : float.PositiveInfinity;

                keyframe.Time = Mathf.Clamp(time, minTime, maxTime);
                keyframe.Value = value;
                entry.Curve.Keyframes[_selectedKeyframe.KeyframeIndex] = keyframe;
                entry.Curve.AutoComputeTangents();
                FireCurvesChanged();
                MarkDirtyRepaint();
                evt.StopPropagation();
            }
        }

        private void OnMouseUp(MouseUpEvent evt)
        {
            if (evt.button == 2 && _isPanning)
            {
                _isPanning = false;
                if (_graph.HasMouseCapture()) _graph.ReleaseMouse();
                evt.StopPropagation();
                return;
            }

            if (evt.button == 0 && _isDraggingKeyframe)
            {
                _isDraggingKeyframe = false;
                if (_graph.HasMouseCapture()) _graph.ReleaseMouse();
                evt.StopPropagation();
            }
        }

        private void OnMouseLeave(MouseLeaveEvent _)
        {
            _isPanning = false;
            _isDraggingKeyframe = false;
            if (_graph.HasMouseCapture()) _graph.ReleaseMouse();
        }

        private void OnContextMenuPopulate(ContextualMenuPopulateEvent evt)
        {
            _contextMouseLocal = evt.localMousePosition;
            var hit = HitTestKeyframeDetailed(_contextMouseLocal, 8f, visibleOnly: true);
            _contextCurveIndex = hit.curveIndex;
            _contextKeyframeIndex = hit.keyframeIndex;

            if (_contextCurveIndex < 0)
                _contextCurveIndex = GetNearestVisibleCurveIndex(_contextMouseLocal, 10f);

            if (_contextCurveIndex >= 0)
            {
                evt.menu.AppendAction("Add Keyframe", _ => AddKeyframeAtContext());
                if (_contextKeyframeIndex >= 0)
                    evt.menu.AppendAction("Delete Keyframe", _ => DeleteKeyframeAtContext());
                else
                    evt.menu.AppendAction("Delete Keyframe", _ => { }, DropdownMenuAction.Status.Disabled);
            }
            else
            {
                evt.menu.AppendAction("Add Keyframe", _ => { }, DropdownMenuAction.Status.Disabled);
                evt.menu.AppendAction("Delete Keyframe", _ => { }, DropdownMenuAction.Status.Disabled);
            }
        }

        private void AddKeyframeAtContext()
        {
            if (_contextCurveIndex < 0 || _contextCurveIndex >= _curves.Count)
                return;

            CurveEntry entry = _curves[_contextCurveIndex];
            if (entry.Curve == null)
                return;

            var (time, _) = LocalToData(_contextMouseLocal);
            float value = entry.Curve.Evaluate(time);
            int insertIndex = 0;
            while (insertIndex < entry.Curve.Keyframes.Count && entry.Curve.Keyframes[insertIndex].Time < time)
                insertIndex++;

            if (insertIndex > 0 && Mathf.Abs(entry.Curve.Keyframes[insertIndex - 1].Time - time) < 0.0001f)
            {
                var existing = entry.Curve.Keyframes[insertIndex - 1];
                existing.Value = value;
                entry.Curve.Keyframes[insertIndex - 1] = existing;
                _selectedKeyframe = new SelectedKeyframe(_contextCurveIndex, insertIndex - 1);
            }
            else
            {
                entry.Curve.Keyframes.Insert(insertIndex, new CurveKeyframe
                {
                    Time = time,
                    Value = value
                });
                _selectedKeyframe = new SelectedKeyframe(_contextCurveIndex, insertIndex);
            }

            entry.Curve.AutoComputeTangents();
            AutoFitView(expandOnly: true);
            FireCurvesChanged();
            MarkDirtyRepaint();
        }

        private void DeleteKeyframeAtContext()
        {
            if (_contextCurveIndex < 0 || _contextCurveIndex >= _curves.Count)
                return;

            CurveEntry entry = _curves[_contextCurveIndex];
            if (entry.Curve == null || _contextKeyframeIndex < 0 || _contextKeyframeIndex >= entry.Curve.Keyframes.Count)
                return;

            entry.Curve.Keyframes.RemoveAt(_contextKeyframeIndex);
            entry.Curve.AutoComputeTangents();
            _selectedKeyframe = default;
            AutoFitView();
            FireCurvesChanged();
            MarkDirtyRepaint();
        }

        private (int curveIndex, int keyframeIndex) HitTestKeyframeDetailed(Vector2 localPos, float radius, bool visibleOnly)
        {
            float bestDistSq = radius * radius;
            int bestCurve = -1;
            int bestKey = -1;

            for (int curveIndex = 0; curveIndex < _curves.Count; curveIndex++)
            {
                CurveEntry entry = _curves[curveIndex];
                if (entry.Curve == null || (visibleOnly && !entry.Visible))
                    continue;

                for (int keyIndex = 0; keyIndex < entry.Curve.Keyframes.Count; keyIndex++)
                {
                    CurveKeyframe keyframe = entry.Curve.Keyframes[keyIndex];
                    Vector2 p = DataToLocal(keyframe.Time, keyframe.Value);
                    float distSq = (p - localPos).sqrMagnitude;
                    if (distSq <= bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestCurve = curveIndex;
                        bestKey = keyIndex;
                    }
                }
            }

            return (bestCurve, bestKey);
        }

        private int GetNearestVisibleCurveIndex(Vector2 localPos, float thresholdPixels)
        {
            Rect plotRect = GetPlotRect();
            if (!plotRect.Contains(localPos))
                return -1;

            var (time, value) = LocalToData(localPos);
            float bestDistance = thresholdPixels;
            int bestIndex = -1;

            for (int curveIndex = 0; curveIndex < _curves.Count; curveIndex++)
            {
                CurveEntry entry = _curves[curveIndex];
                if (!entry.Visible || entry.Curve == null || entry.Curve.Keyframes.Count == 0)
                    continue;

                float curveValue = entry.Curve.Evaluate(time);
                Vector2 curvePos = DataToLocal(time, curveValue);
                float distance = Mathf.Abs(curvePos.y - localPos.y);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = curveIndex;
                }
            }

            return bestIndex;
        }

        private void AutoFitView(bool expandOnly = false)
        {
            bool hasData = false;
            float minTime = float.PositiveInfinity;
            float maxTime = float.NegativeInfinity;
            float minValue = float.PositiveInfinity;
            float maxValue = float.NegativeInfinity;

            foreach (CurveEntry entry in _curves)
            {
                if (!entry.Visible || entry.Curve == null)
                    continue;

                foreach (CurveKeyframe kf in entry.Curve.Keyframes)
                {
                    hasData = true;
                    minTime = Mathf.Min(minTime, kf.Time);
                    maxTime = Mathf.Max(maxTime, kf.Time);
                    minValue = Mathf.Min(minValue, kf.Value);
                    maxValue = Mathf.Max(maxValue, kf.Value);
                }
            }

            if (!hasData)
            {
                if (!expandOnly)
                    _viewRect = new Rect(0f, -1f, 5f, 2f);
                return;
            }

            if (Mathf.Approximately(minTime, maxTime))
                maxTime = minTime + 1f;
            else
                maxTime += 1f;

            float valueRange = maxValue - minValue;
            if (valueRange < 0.001f)
            {
                minValue -= 1f;
                maxValue += 1f;
            }
            else
            {
                float padding = valueRange * 0.1f;
                minValue -= padding;
                maxValue += padding;
            }

            Rect fitted = Rect.MinMaxRect(minTime, minValue, maxTime, maxValue);
            if (expandOnly)
            {
                float xMin = Mathf.Min(_viewRect.xMin, fitted.xMin);
                float yMin = Mathf.Min(_viewRect.yMin, fitted.yMin);
                float xMax = Mathf.Max(_viewRect.xMax, fitted.xMax);
                float yMax = Mathf.Max(_viewRect.yMax, fitted.yMax);
                _viewRect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
            else
            {
                _viewRect = fitted;
            }
        }

        private Rect GetPlotRect()
        {
            Rect rect = _graph.contentRect;
            return new Rect(
                LeftAxisWidth,
                4f,
                Mathf.Max(0f, rect.width - LeftAxisWidth - 4f),
                Mathf.Max(0f, rect.height - BottomAxisHeight - 8f));
        }

        private float GetNiceStep(float rawStep)
        {
            rawStep = Mathf.Max(rawStep, 0.0001f);
            float exponent = Mathf.Floor(Mathf.Log10(rawStep));
            float baseValue = Mathf.Pow(10f, exponent);
            float fraction = rawStep / baseValue;

            if (fraction <= 1f) return baseValue;
            if (fraction <= 2f) return 2f * baseValue;
            if (fraction <= 5f) return 5f * baseValue;
            return 10f * baseValue;
        }

        private void FireCurvesChanged()
        {
            OnCurvesChanged?.Invoke();
        }

        private class CurveEntry
        {
            public string Name;
            public SerializableCurve Curve;
            public Color Color;
            public bool Visible;
        }

        private readonly struct SelectedKeyframe
        {
            public readonly int CurveIndex;
            public readonly int KeyframeIndex;
            public bool IsValid => CurveIndex >= 0 && KeyframeIndex >= 0;

            public SelectedKeyframe(int curveIndex, int keyframeIndex)
            {
                CurveIndex = curveIndex;
                KeyframeIndex = keyframeIndex;
            }
        }
    }
}
