using UnityEngine;
using STGEngine.Core.Timeline;

namespace STGEngine.Runtime.Preview
{
    /// <summary>
    /// Manages a scrolling background layer using a Quad mesh.
    /// Supports UV-scroll and transition effects (Cut, CrossFade, SlideUp, SlideDown).
    /// Created by PatternSandboxSetup, driven by ActionEventPreviewController.
    /// </summary>
    [AddComponentMenu("STGEngine/Background Layer")]
    public class BackgroundLayer : MonoBehaviour
    {
        private MeshRenderer _rendererA;
        private MeshRenderer _rendererB;
        private Material _matA;
        private Material _matB;
        private Vector2 _scrollSpeed;
        private Vector2 _uvOffset;

        // Transition state
        private bool _transitioning;
        private float _transitionProgress;
        private float _transitionDuration;
        private BgTransitionType _transitionType;

        /// <summary>
        /// Initialize the background layer with two overlapping quads.
        /// </summary>
        public void Initialize()
        {
            var cam = Camera.main;
            float z = cam != null ? cam.farClipPlane * 0.85f : 80f;

            // Quad A (current background)
            var goA = GameObject.CreatePrimitive(PrimitiveType.Quad);
            goA.name = "BG_QuadA";
            goA.transform.SetParent(transform);
            goA.transform.localPosition = new Vector3(0, 0, z);
            goA.transform.localScale = new Vector3(z * 2f, z * 1.5f, 1f);
            var col = goA.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            _rendererA = goA.GetComponent<MeshRenderer>();
            _matA = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture"));
            _matA.color = new Color(0.05f, 0.05f, 0.1f);
            _rendererA.material = _matA;

            // Quad B (next background, used during transitions)
            var goB = GameObject.CreatePrimitive(PrimitiveType.Quad);
            goB.name = "BG_QuadB";
            goB.transform.SetParent(transform);
            goB.transform.localPosition = new Vector3(0, 0, z - 0.1f);
            goB.transform.localScale = goA.transform.localScale;
            col = goB.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
            _rendererB = goB.GetComponent<MeshRenderer>();
            _matB = new Material(_matA);
            _rendererB.material = _matB;
            _rendererB.enabled = false;
        }

        /// <summary>
        /// Switch to a new background with the specified transition.
        /// </summary>
        public void SetBackground(string bgId, BgTransitionType transition, float duration, Vector2 scrollSpeed)
        {
            var tex = Resources.Load<Texture2D>($"STGData/Backgrounds/{bgId}");
            if (tex == null)
                tex = Resources.Load<Texture2D>(bgId);

            _scrollSpeed = scrollSpeed;

            if (transition == BgTransitionType.Cut || duration <= 0f)
            {
                // Instant switch
                if (tex != null)
                {
                    _matA.mainTexture = tex;
                    _matA.color = Color.white;
                }
                _transitioning = false;
                _rendererB.enabled = false;
            }
            else
            {
                // Start transition: B shows new texture, A keeps old
                if (tex != null)
                {
                    _matB.mainTexture = tex;
                    _matB.color = Color.white;
                }
                _rendererB.enabled = true;
                _transitionType = transition;
                _transitionDuration = duration;
                _transitionProgress = 0f;
                _transitioning = true;

                // For CrossFade, start B fully transparent
                if (transition == BgTransitionType.CrossFade)
                {
                    var c = _matB.color;
                    c.a = 0f;
                    _matB.color = c;
                }
            }
        }

        /// <summary>
        /// Advance UV scrolling and transition animation.
        /// </summary>
        public void Tick(float deltaTime)
        {
            // UV scroll
            _uvOffset += _scrollSpeed * deltaTime;
            _matA.mainTextureOffset = _uvOffset;
            if (_rendererB.enabled)
                _matB.mainTextureOffset = _uvOffset;

            // Transition
            if (!_transitioning) return;

            _transitionProgress += deltaTime / Mathf.Max(0.01f, _transitionDuration);
            float t = Mathf.Clamp01(_transitionProgress);

            switch (_transitionType)
            {
                case BgTransitionType.CrossFade:
                {
                    var c = _matB.color;
                    c.a = t;
                    _matB.color = c;
                    break;
                }
                case BgTransitionType.SlideUp:
                {
                    // Slide new background up from bottom
                    var posB = _rendererB.transform.localPosition;
                    float fullHeight = _rendererB.transform.localScale.y;
                    posB.y = Mathf.Lerp(-fullHeight, 0f, t);
                    _rendererB.transform.localPosition = posB;
                    break;
                }
                case BgTransitionType.SlideDown:
                {
                    var posB = _rendererB.transform.localPosition;
                    float fullHeight = _rendererB.transform.localScale.y;
                    posB.y = Mathf.Lerp(fullHeight, 0f, t);
                    _rendererB.transform.localPosition = posB;
                    break;
                }
            }

            if (t >= 1f)
            {
                // Transition complete: swap A ← B
                _matA.mainTexture = _matB.mainTexture;
                _matA.color = _matB.color;
                _matA.color = new Color(_matA.color.r, _matA.color.g, _matA.color.b, 1f);
                _rendererB.enabled = false;
                _transitioning = false;

                // Reset B position
                var pos = _rendererB.transform.localPosition;
                pos.y = _rendererA.transform.localPosition.y;
                _rendererB.transform.localPosition = pos;
            }
        }

        /// <summary>Reset background to default state.</summary>
        public void ResetBackground()
        {
            _transitioning = false;
            _uvOffset = Vector2.zero;
            _scrollSpeed = Vector2.zero;
            _matA.mainTextureOffset = Vector2.zero;
            _rendererB.enabled = false;
        }
    }
}
