using System;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SoulstoneAP
{
    /// <summary>
    /// Screen-space burdens chosen from a DeathLink prompt. Most last until the
    /// current run ends; the blackout is deliberately short. Blur is produced by
    /// rendering the main camera into a low-resolution texture and drawing it
    /// back through a screen-space RawImage, so no custom shader is required.
    /// </summary>
    internal sealed class ScreenEffects : MonoBehaviour
    {
        private const float FullBlurScale = 0.55f;
        private const float CenterBlurScale = 0.22f;
        private const float CenterFraction = 0.30f;
        private const int BlurCanvasOrder = -1000;
        private const int OverlayCanvasOrder = 24000;

        private static ScreenEffects _instance;
        private static ManualLogSource Log => Plugin.Logger;

        private GameObject _blurCanvasObject;
        private RawImage _fullBlurImage;
        private RawImage _centerBlurImage;
        private GameObject _overlayCanvasObject;
        private Image _blindImage;
        private Image _leftBlackImage;
        private Image _rightBlackImage;

        private Camera _camera;
        private RenderTexture _fullTexture;
        private RenderTexture _lowTexture;
        private RenderTexture _originalTarget;
        private bool _targetCaptured;
        private int _textureWidth;
        private int _textureHeight;

        private bool _fullBlur;
        private bool _centerBlur;
        private bool _leftBlack;
        private bool _rightBlack;
        private float _blindUntil;

        private bool _deuteranopia;
        private bool _tritanopia;
        private bool _deepFried;
        private Volume _volume;
        private VolumeProfile _profile;
        private bool _postProcessingCaptured;
        private bool _postProcessingWasOff;

        private CommandBuffer _blitCommand;
        private Action<ScriptableRenderContext, Camera> _endCameraHandler;
        private bool _subscribed;
        private bool _seenPlayer;
        private float _runGoneAt;
        private float _nextRunCheck;
        private bool _warnedCamera;
        private bool _warnedVolume;

        private static readonly float[] Deuteranopia =
        {
            36.7322f, 86.0646f, -22.7968f,
            28.0085f, 67.2501f, 4.7413f,
            -1.1820f, 4.2940f, 96.8881f,
        };

        private static readonly float[] Tritanopia =
        {
            125.5528f, -7.6749f, -17.8779f,
            -7.8411f, 93.0809f, 14.7602f,
            0.4733f, 69.1367f, 30.3900f,
        };

        private void Awake()
        {
            _instance = this;
            _blitCommand = new CommandBuffer { name = "SoulstoneAP screen copy" };
        }

        private void OnEnable()
        {
            if (_subscribed) return;
            _endCameraHandler = (context, camera) => OnEndCameraRendering(context, camera);
            RenderPipelineManager.endCameraRendering += _endCameraHandler;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (_subscribed)
            {
                RenderPipelineManager.endCameraRendering -= _endCameraHandler;
                _subscribed = false;
                _endCameraHandler = null;
            }
        }

        private void OnDestroy()
        {
            ClearAll();
            if (_blurCanvasObject != null) Destroy(_blurCanvasObject);
            if (_overlayCanvasObject != null) Destroy(_overlayCanvasObject);
            if (_profile != null) Destroy(_profile);
            if (_blitCommand != null)
            {
                _blitCommand.Release();
                _blitCommand = null;
            }
            if (_instance == this) _instance = null;
        }

        private void LateUpdate()
        {
            if (_fullBlur || _centerBlur) ApplyBlurState();
            UpdateBlind();
            CheckRunState();
        }

        // ------------------------------------------------------------- public actions

        public static void EnableFullBlur()
        {
            if (_instance == null) return;
            _instance._seenPlayer = true;
            _instance._fullBlur = true;
            _instance.EnsureBlurCanvas();
            _instance.ApplyBlurState();
        }

        public static void EnableCenterBlur()
        {
            if (_instance == null) return;
            _instance._seenPlayer = true;
            _instance._centerBlur = true;
            _instance.EnsureBlurCanvas();
            _instance.ApplyBlurState();
        }

        public static void EnableDeuteranopia()
        {
            if (_instance == null) return;
            _instance._seenPlayer = true;
            _instance._deuteranopia = true;
            _instance._tritanopia = false;
            _instance.RebuildVolume();
        }

        public static void EnableTritanopia()
        {
            if (_instance == null) return;
            _instance._seenPlayer = true;
            _instance._tritanopia = true;
            _instance._deuteranopia = false;
            _instance.RebuildVolume();
        }

        public static void EnableDeepFried()
        {
            if (_instance == null) return;
            _instance._seenPlayer = true;
            _instance._deepFried = true;
            _instance.RebuildVolume();
        }

        public static void Blackout(float seconds)
        {
            if (_instance == null) return;
            _instance._seenPlayer = true;
            _instance._blindUntil = Mathf.Max(_instance._blindUntil, Time.time + Mathf.Max(0.1f, seconds));
            _instance.EnsureOverlayCanvas();
            _instance.UpdateBlind();
        }

        public static void HalfBlack()
        {
            if (_instance == null) return;
            _instance._seenPlayer = true;
            _instance.EnsureOverlayCanvas();
            if (UnityEngine.Random.Range(0, 2) == 0) _instance._leftBlack = true;
            else _instance._rightBlack = true;
            _instance.UpdateBlackPanels();
        }

        // ---------------------------------------------------------------- blur

        private void EnsureBlurCanvas()
        {
            if (_blurCanvasObject != null) return;

            _blurCanvasObject = new GameObject("SoulstoneAP_ScreenBlurCanvas");
            DontDestroyOnLoad(_blurCanvasObject);

            var canvas = _blurCanvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Keep the world replacement below the game's own UI canvases.
            canvas.sortingOrder = BlurCanvasOrder;

            _fullBlurImage = MakeRawImage(canvas.transform, "FullScreenBlur",
                Vector2.zero, Vector2.one, new Rect(0f, 0f, 1f, 1f));
            _fullBlurImage.gameObject.SetActive(false);

            var inset = 0.5f - CenterFraction * 0.5f;
            _centerBlurImage = MakeRawImage(canvas.transform, "CenterBlur",
                new Vector2(inset, inset), new Vector2(1f - inset, 1f - inset),
                new Rect(inset, inset, CenterFraction, CenterFraction));
            _centerBlurImage.gameObject.SetActive(false);
        }

        private RawImage MakeRawImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Rect uv)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.uvRect = uv;
            image.enabled = true;
            return image;
        }

        private void ApplyBlurState()
        {
            if (!_fullBlur && !_centerBlur)
            {
                ReleaseBlur();
                return;
            }

            var camera = FindMainCamera();
            if (camera == null)
            {
                if (!_warnedCamera)
                {
                    _warnedCamera = true;
                    Log?.LogWarning("AP: screen blur has no main camera to render");
                }
                HideBlurImages();
                return;
            }

            if (_camera != null && _camera != camera) ReleaseBlur();
            _camera = camera;

            var width = Mathf.Max(1, Screen.width);
            var height = Mathf.Max(1, Screen.height);
            var scale = _fullBlur ? FullBlurScale : CenterBlurScale;
            var lowWidth = Mathf.Max(1, Mathf.RoundToInt(width * scale));
            var lowHeight = Mathf.Max(1, Mathf.RoundToInt(height * scale));

            if (width != _textureWidth || height != _textureHeight)
            {
                ReleaseTextures();
                _textureWidth = width;
                _textureHeight = height;
            }

            if (!_fullBlur && _fullTexture == null)
                _fullTexture = CreateTexture(width, height, "SoulstoneAP full screen");

            if (_lowTexture == null || _lowTexture.width != lowWidth || _lowTexture.height != lowHeight)
            {
                ReleaseTexture(ref _lowTexture);
                _lowTexture = CreateTexture(lowWidth, lowHeight, "SoulstoneAP low screen");
            }

            if (!_targetCaptured)
            {
                _originalTarget = _camera.targetTexture;
                _targetCaptured = true;
            }

            var target = _fullBlur ? _lowTexture : _fullTexture;
            if (_camera.targetTexture != target) _camera.targetTexture = target;

            _fullBlurImage.texture = _fullBlur ? _lowTexture : _fullTexture;
            _fullBlurImage.gameObject.SetActive(true);

            _centerBlurImage.texture = _lowTexture;
            _centerBlurImage.gameObject.SetActive(_centerBlur && !_fullBlur);
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _camera || !_centerBlur || _fullBlur) return;
            if (_fullTexture == null || _lowTexture == null || _blitCommand == null) return;

            _blitCommand.Clear();
            _blitCommand.Blit(new RenderTargetIdentifier(_fullTexture), new RenderTargetIdentifier(_lowTexture));
            context.ExecuteCommandBuffer(_blitCommand);
        }

        private void ReleaseBlur()
        {
            if (_targetCaptured && _camera != null && _camera.targetTexture != _originalTarget)
                _camera.targetTexture = _originalTarget;

            _targetCaptured = false;
            _originalTarget = null;
            HideBlurImages();
            ReleaseTextures();
        }

        private void HideBlurImages()
        {
            if (_fullBlurImage != null) _fullBlurImage.gameObject.SetActive(false);
            if (_centerBlurImage != null) _centerBlurImage.gameObject.SetActive(false);
        }

        private void ReleaseTextures()
        {
            ReleaseTexture(ref _fullTexture);
            ReleaseTexture(ref _lowTexture);
            _textureWidth = 0;
            _textureHeight = 0;
        }

        private static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            Destroy(texture);
            texture = null;
        }

        private static RenderTexture CreateTexture(int width, int height, string name)
        {
            var texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            texture.Create();
            return texture;
        }

        private static Camera FindMainCamera()
        {
            try
            {
                if (Camera.main != null) return Camera.main;

                var best = (Camera)null;
                var cameras = Camera.allCameras;
                foreach (var camera in cameras)
                {
                    if (camera == null || !camera.isActiveAndEnabled || camera.cameraType != UnityEngine.CameraType.Game) continue;
                    if (best == null || camera.depth > best.depth) best = camera;
                }
                return best;
            }
            catch (Exception error)
            {
                Log?.LogWarning("AP: could not find a camera for screen effects: " + error.Message);
                return null;
            }
        }

        // ------------------------------------------------------------- overlays

        private void EnsureOverlayCanvas()
        {
            if (_overlayCanvasObject != null) return;

            _overlayCanvasObject = new GameObject("SoulstoneAP_ScreenOverlayCanvas");
            DontDestroyOnLoad(_overlayCanvasObject);

            var canvas = _overlayCanvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlayCanvasOrder;

            _blindImage = MakeImage(canvas.transform, "Blackout", Vector2.zero, Vector2.one);
            _leftBlackImage = MakeImage(canvas.transform, "LeftBlack", new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            _rightBlackImage = MakeImage(canvas.transform, "RightBlack", new Vector2(0.5f, 0f), new Vector2(1f, 1f));
            HideOverlay();
        }

        private static Image MakeImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            image.enabled = true;
            return image;
        }

        private void UpdateBlind()
        {
            if (_blindImage == null) return;

            var now = Time.time;
            var remaining = _blindUntil - now;
            if (remaining <= 0f)
            {
                if (_blindImage.gameObject.activeSelf) _blindImage.gameObject.SetActive(false);
                return;
            }

            var color = Color.black;
            color.a = Mathf.Clamp01(remaining / 0.35f);
            _blindImage.color = color;
            if (!_blindImage.gameObject.activeSelf) _blindImage.gameObject.SetActive(true);
        }

        private void UpdateBlackPanels()
        {
            if (_leftBlackImage != null) _leftBlackImage.gameObject.SetActive(_leftBlack);
            if (_rightBlackImage != null) _rightBlackImage.gameObject.SetActive(_rightBlack);
        }

        private void HideOverlay()
        {
            if (_blindImage != null) _blindImage.gameObject.SetActive(false);
            if (_leftBlackImage != null) _leftBlackImage.gameObject.SetActive(false);
            if (_rightBlackImage != null) _rightBlackImage.gameObject.SetActive(false);
        }

        // --------------------------------------------------------------- volume

        private void EnsureVolume()
        {
            if (_volume != null) return;
            try
            {
                _volume = gameObject.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 1000f;
                _volume.weight = 1f;
            }
            catch (Exception error)
            {
                if (!_warnedVolume)
                {
                    _warnedVolume = true;
                    Log?.LogWarning("AP: could not create the screen-effect volume: " + error);
                }
                _volume = null;
            }
        }

        private void RebuildVolume()
        {
            if (!_deuteranopia && !_tritanopia && !_deepFried)
            {
                if (_volume != null) _volume.weight = 0f;
                RestorePostProcessing();
                return;
            }

            EnsureVolume();
            if (_volume == null) return;

            _camera = _camera != null ? _camera : FindMainCamera();
            EnsurePostProcessing();

            _volume.weight = 1f;
            if (_profile != null) Destroy(_profile);
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.profile = _profile;

            if (_deuteranopia) AddChannelMixer(_profile, Deuteranopia);
            else if (_tritanopia) AddChannelMixer(_profile, Tritanopia);
            if (_deepFried) AddDeepFried(_profile);
        }

        private static void AddChannelMixer(VolumeProfile profile, float[] matrix)
        {
            var mixer = profile.Add<ChannelMixer>(true);
            mixer.redOutRedIn.Override(matrix[0]);
            mixer.redOutGreenIn.Override(matrix[1]);
            mixer.redOutBlueIn.Override(matrix[2]);
            mixer.greenOutRedIn.Override(matrix[3]);
            mixer.greenOutGreenIn.Override(matrix[4]);
            mixer.greenOutBlueIn.Override(matrix[5]);
            mixer.blueOutRedIn.Override(matrix[6]);
            mixer.blueOutGreenIn.Override(matrix[7]);
            mixer.blueOutBlueIn.Override(matrix[8]);
        }

        private static void AddDeepFried(VolumeProfile profile)
        {
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.35f);
            color.contrast.Override(35f);
            color.saturation.Override(70f);
            color.colorFilter.Override(new Color(1f, 0.88f, 0.65f));

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.7f);
            bloom.threshold.Override(0.55f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.85f, 0.6f));

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.55f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.4f);
            vignette.color.Override(Color.black);
        }

        private void EnsurePostProcessing()
        {
            if (_camera == null) return;
            try
            {
                var data = _camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) return;
                if (!_postProcessingCaptured)
                {
                    _postProcessingWasOff = !data.renderPostProcessing;
                    _postProcessingCaptured = true;
                }
                if (!data.renderPostProcessing) data.renderPostProcessing = true;
            }
            catch (Exception error)
            {
                Log?.LogWarning("AP: could not enable camera post-processing: " + error.Message);
            }
        }

        private void RestorePostProcessing()
        {
            if (!_postProcessingCaptured || _camera == null) return;
            try
            {
                var data = _camera.GetComponent<UniversalAdditionalCameraData>();
                if (data != null) data.renderPostProcessing = !_postProcessingWasOff;
            }
            catch
            {
                // The camera may have been destroyed with the run.
            }

            _postProcessingCaptured = false;
            _postProcessingWasOff = false;
        }

        // --------------------------------------------------------------- lifecycle

        private void CheckRunState()
        {
            if (Time.unscaledTime < _nextRunCheck) return;
            _nextRunCheck = Time.unscaledTime + 0.3f;

            var alive = PlayerRefs.TryGet(out var player) &&
                        player != null &&
                        player._health != null &&
                        !player._health.HasBeenKilled &&
                        player._health.CurrentHealth > 0f;

            if (alive)
            {
                _seenPlayer = true;
                _runGoneAt = 0f;
                return;
            }

            if (!_seenPlayer) return;
            if (_runGoneAt <= 0f)
            {
                _runGoneAt = Time.unscaledTime;
                return;
            }

            if (Time.unscaledTime - _runGoneAt > 1f) ClearAll();
        }

        private void ClearAll()
        {
            _fullBlur = false;
            _centerBlur = false;
            _leftBlack = false;
            _rightBlack = false;
            _blindUntil = 0f;
            _deuteranopia = false;
            _tritanopia = false;
            _deepFried = false;
            _seenPlayer = false;
            _runGoneAt = 0f;

            ReleaseBlur();
            HideOverlay();
            if (_volume != null) _volume.weight = 0f;
            RestorePostProcessing();
        }
    }
}
