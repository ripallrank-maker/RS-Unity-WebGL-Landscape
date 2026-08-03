using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Singleton that handles portrait-to-landscape orientation for Unity WebGL and Editor.
///
/// Orientation is driven by screen-size polling (Update) so it reacts automatically
/// to any resolution change — game-view resize in Editor, device rotation on mobile,
/// or browser resize on WebGL.
///
/// Portrait mode applies:
///   • WorldRoot rotation (optional, assign in Inspector).
///   • Camera rotation -90 deg Z + orthographicSize correction.
///   • A "__PortraitRotationRoot__" RectTransform pivot in every Screen-Space
///     canvas — both Overlay AND Camera render mode — (landscape-sized, rotated
///     -90 deg) that holds all original canvas children. Screen-Space Camera
///     canvases are NOT assumed to auto-follow their Render Camera's rotation;
///     they get the exact same manual pivot as Overlay.
///
/// The JS bridge is used only for browser orientation detection;
/// it no longer manipulates the HTML canvas element.
/// </summary>
public class WebGLOrientationAdapter : MonoBehaviour
{
    public static WebGLOrientationAdapter Instance { get; private set; }

    [Tooltip("Rotation applied to cameras and canvas pivot in portrait mode. -90 matches CSS rotate(-90deg).")]
    [SerializeField] private float portraitRotationDeg = -90f;

    [Tooltip("Optional: scene root Transform containing all world GameObjects. " +
             "Rotated by portraitRotationDeg in portrait mode. Leave null to skip.")]
    [SerializeField] private Transform worldRoot;

    [Tooltip("Landscape design resolution (e.g. 1920 x 1080). Used to compute the design " +
             "aspect ratio for the portrait camera framing. Set this when the scene has no " +
             "Screen-Space overlay CanvasScaler to read the design resolution from " +
             "(e.g. a World-Space-only UI). Leave at 0,0 to auto-detect from a CanvasScaler.")]
    [SerializeField] private Vector2 landscapeReferenceResolution = new Vector2(1920f, 1080f);

    [Tooltip("Log the computed camera framing numbers to the Console on each apply. " +
             "Turn on to diagnose letterbox / scale issues, then paste the log.")]
    [SerializeField] private bool debugLog = false;

    // ── JSLib bridge — orientation detection only ─────────────────────────────
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void WebGLOriBridge_Init(string goName);
    [DllImport("__Internal")] static extern int  WebGLOriBridge_IsPortrait();
    [DllImport("__Internal")] static extern void WebGLOriBridge_Cleanup();
#endif

    static void BridgeInit(string n) { }
    static int BridgeIsPortrait()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return WebGLOriBridge_IsPortrait();
#else
        return Screen.height > Screen.width ? 1 : 0;
#endif
    }
    static void BridgeCleanup() { }

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _isPortrait;
    private bool _baselineCaptured;
    private Quaternion _worldRootBaseRot;

    /// <summary>
    /// True after the first orientation pass (CaptureBaseline + ApplyCurrent + ForceUpdateCanvases)
    /// has completed. Other MonoBehaviours can yield on this before doing UI work.
    /// </summary>
    public bool IsReady { get; private set; }

    // Screen-size polling
    private int _lastW, _lastH;
    private bool _pendingRefresh;

    private readonly List<CameraEntry> _cameras = new List<CameraEntry>();
    private readonly List<CanvasEntry> _canvases = new List<CanvasEntry>();

    struct CameraEntry
    {
        public Camera cam;
        public Quaternion baseRotation;
        public float baseOrthographicSize;
    }

    struct CanvasEntry
    {
        public Canvas canvas;
        public CanvasScaler scaler;
        public Vector2 baseRefRes;
        public CanvasScaler.ScaleMode scaleMode;
        public GameObject rotationRoot;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Awake()
    {
        // Scene-scoped by design: NO DontDestroyOnLoad. Only the scene that
        // actually contains this component is rotated; the instance is destroyed
        // together with its scene, so scenes without the adapter are never touched.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    IEnumerator Start()
    {
        yield return new WaitForEndOfFrame();
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLOriBridge_Init(gameObject.name);
#endif
        CaptureBaseline();
        _isPortrait = BridgeIsPortrait() == 1;
        _lastW = Screen.width;
        _lastH = Screen.height;
        ApplyCurrent();
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
        IsReady = true;
    }

    /// <summary>
    /// Poll screen size every frame. Any change (resize, rotation, SetResolution)
    /// triggers a deferred reapply so layouts stay correct.
    /// </summary>
    void Update()
    {
        if (!_baselineCaptured || _pendingRefresh) return;
        if (Screen.width == _lastW && Screen.height == _lastH) return;

        _pendingRefresh = true;
        StartCoroutine(OnScreenSizeChanged());
    }

    IEnumerator OnScreenSizeChanged()
    {
        yield return new WaitForEndOfFrame(); // let the new resolution settle

        int newW = Screen.width;
        int newH = Screen.height;
        bool nowPortrait = newH > newW;

        if (nowPortrait != _isPortrait)
        {
            // Orientation flipped — full reset then apply
            ResetToBaseline();
            _isPortrait = nowPortrait;
            ApplyCurrent();
        }
        // (portrait with same size — no camera adjustments needed)

        _lastW = newW;
        _lastH = newH;
        _pendingRefresh = false;

        Canvas.ForceUpdateCanvases();
    }

    void OnDestroy()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLOriBridge_Cleanup();
#endif
        // Trả canvas về baseline TRƯỚC KHI adapter này bị hủy (vd khi đổi scene).
        // Adapter này scene-scoped (không DontDestroyOnLoad), nhưng canvas như
        // Popup canvas thì DontDestroyOnLoad và tồn tại xuyên suốt các scene.
        // Scene kế tiếp sẽ tạo một adapter MỚI và CaptureBaseline() của nó đọc lại
        // CanvasScaler.referenceResolution hiện tại của MỌI canvas — nếu còn đang
        // ở trạng thái đã swap sang portrait, scene mới sẽ tưởng đó là baseline
        // gốc rồi swap NGƯỢC LẠI một lần nữa, khiến referenceResolution sai lệch
        // hẳn so với màn hình thật dù vẫn đang ở portrait (UI co lại thành khối
        // nhỏ giữa canvas thay vì phủ kín).
        if (_baselineCaptured && _isPortrait)
            ResetToBaseline();

#if UNITY_EDITOR
        // Screen.SetResolution does NOT revert on Play Mode exit — reset manually.
        if (_baselineCaptured)
        {
            Vector2 ls = GetLandscapeRefRes();
            Screen.SetResolution((int)ls.x, (int)ls.y, false);
        }
#endif
        if (Instance == this) Instance = null;
    }

    // ── JSLib → Unity callback (WebGL browser events) ────────────────────────
    /// <summary>
    /// Called by the JS bridge via SendMessage when the browser orientation changes.
    /// On WebGL the screen size also changes, so the Update loop will catch it too —
    /// but this callback fires first and is more reliable on some browsers.
    /// </summary>
    public void OnBrowserOrientationChanged(int isPortrait)
    {
        StartCoroutine(ApplyNextFrame(isPortrait == 1));
    }

    IEnumerator ApplyNextFrame(bool portrait)
    {
        yield return new WaitForEndOfFrame();
        if (portrait == _isPortrait) yield break; // already correct, Update will sync
        _isPortrait = portrait;
        ApplyCurrent();
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    /// <summary>
    /// Editor Orientation Simulator: applies orientation immediately, then changes
    /// the game-view resolution so the Update polling stays in sync.
    /// Direct apply is needed because Screen.SetResolution in Editor may not
    /// update Screen.width/height fast enough for the polling loop to catch.
    /// </summary>
    public void SimulatePortrait(bool portrait)
    {
        if (!_baselineCaptured) CaptureBaseline();
        if (portrait == _isPortrait) return; // already correct

        // ── 1. Apply orientation immediately ─────────────────────────────────
        ResetToBaseline();
        _isPortrait = portrait;
        ApplyCurrent();

        // ── 2. Change game-view resolution to match ───────────────────────────
        Vector2 ls = GetLandscapeRefRes();
        int newW = portrait ? (int)ls.y : (int)ls.x;
        int newH = portrait ? (int)ls.x : (int)ls.y;
        Screen.SetResolution(newW, newH, false);

        // Keep _lastW/_lastH at the CURRENT actual screen size.
        // Screen.SetResolution takes effect next frame (async); if we set _lastW/_lastH
        // to the target resolution instead, Update() detects a mismatch immediately and
        // OnScreenSizeChanged re-reads the still-portrait screen → re-applies portrait,
        // undoing the landscape reset. Setting current dimensions avoids this race.
        _lastW = Screen.width;
        _lastH = Screen.height;

        // ── 3. Force canvas layout rebuild next frame ─────────────────────────
        StartCoroutine(ForceCanvasUpdateNextFrame());
    }

    IEnumerator ForceCanvasUpdateNextFrame()
    {
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
    }

    public bool IsPortrait => _isPortrait;

    /// <summary>
    /// Converts a screen-space delta vector to the logical (landscape) coordinate space.
    /// In portrait mode the canvas root is rotated by <c>portraitRotationDeg</c>, so raw
    /// screen deltas have their axes swapped relative to the game content.
    /// Use this before any axis-based direction check (swipe left/right/up/down).
    /// In landscape mode the vector is returned unchanged.
    /// </summary>
    public Vector2 ScreenDeltaToLogical(Vector2 screenDelta)
    {
        if (!_isPortrait) return screenDelta;
        // Apply the inverse of portraitRotationDeg to map screen space → canvas-local space.
        float rad = -portraitRotationDeg * Mathf.Deg2Rad; // e.g. -(-90°) = +90°
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(
            cos * screenDelta.x - sin * screenDelta.y,
            sin * screenDelta.x + cos * screenDelta.y);
    }

    /// <summary>
    /// Converts a screen-space position to the logical (landscape) coordinate space.
    /// Rotation is applied around the screen centre.
    /// </summary>
    public Vector2 ScreenPointToLogical(Vector2 screenPoint)
    {
        if (!_isPortrait) return screenPoint;
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        return ScreenDeltaToLogical(screenPoint - center) + center;
    }

    // ── Internal ──────────────────────────────────────────────────────────────
    void ApplyCurrent() { if (_isPortrait) ApplyPortrait(); else ResetToBaseline(); }

    void CaptureBaseline()
    {
        _cameras.Clear();
        _canvases.Clear();
        AppendNewObjects();
        if (worldRoot != null)
            _worldRootBaseRot = worldRoot.rotation;
        _baselineCaptured = true;
    }

    void AppendNewObjects()
    {
        foreach (var cam in Camera.allCameras)
        {
            if (cam == null) continue;
            if (_cameras.Exists(e => e.cam == cam)) continue;
            _cameras.Add(new CameraEntry
            {
                cam = cam,
                baseRotation = cam.transform.rotation,
                baseOrthographicSize = cam.orthographicSize
            });
        }

        foreach (var cv in Object.FindObjectsOfType<Canvas>(true))
        {
            if (cv == null) continue;

            // World-Space canvases are rendered by the camera, so the camera
            // rotation already tilts them together with the world — no extra
            // handling needed here.
            if (cv.renderMode == RenderMode.WorldSpace) continue;

            // Screen-Space Overlay AND Screen-Space Camera both get the manual
            // "__PortraitRotationRoot__" pivot below. Unity does NOT reliably
            // re-orient a Screen-Space Camera canvas to follow its Render
            // Camera's own rotation (roll included) the way this adapter needs,
            // so relying on that was wrong — both render modes are handled the
            // same explicit way here.
            if (_canvases.Exists(e => e.canvas == cv)) continue;

            var sc = cv.GetComponent<CanvasScaler>();
            var mode = sc != null ? sc.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
            var res = sc != null ? sc.referenceResolution : Vector2.zero;

            _canvases.Add(new CanvasEntry
            {
                canvas = cv,
                scaler = sc,
                scaleMode = mode,
                baseRefRes = res,
                rotationRoot = null
            });
        }
    }

    Vector2 GetLandscapeRefRes()
    {
        // Ưu tiên 1: design resolution khai báo trực tiếp trong Inspector.
        // Cần thiết khi scene chỉ có World-Space canvas (không có overlay
        // CanvasScaler để suy ra design aspect).
        if (landscapeReferenceResolution.x > 0f && landscapeReferenceResolution.y > 0f)
        {
            float w = Mathf.Max(landscapeReferenceResolution.x, landscapeReferenceResolution.y);
            float h = Mathf.Min(landscapeReferenceResolution.x, landscapeReferenceResolution.y);
            return new Vector2(w, h);
        }

        // Ưu tiên 2: lấy design resolution từ CanvasScaler.
        for (int i = 0; i < _canvases.Count; i++)
        {
            var e = _canvases[i];

            if (e.baseRefRes != Vector2.zero)
            {
                float w = Mathf.Max(e.baseRefRes.x, e.baseRefRes.y);
                float h = Mathf.Min(e.baseRefRes.x, e.baseRefRes.y);
                return new Vector2(w, h);
            }
        }

        // Fallback: dùng screen hiện tại và ép về landscape.
        float sw = Screen.width;
        float sh = Screen.height;

        return sw >= sh
            ? new Vector2(sw, sh)
            : new Vector2(sh, sw);
    }

    void ApplyPortrait()
    {
        Vector2 landscapeRef = GetLandscapeRefRes();

        float designAspect = landscapeRef.y > 0f
            ? landscapeRef.x / landscapeRef.y
            : 16f / 9f;

        // ── Cameras ───────────────────────────────────────────────────────────
        for (int i = _cameras.Count - 1; i >= 0; i--)
        {
            var e = _cameras[i];

            if (e.cam == null)
            {
                _cameras.RemoveAt(i);
                continue;
            }

            // Camera xoay ngược chiều canvas/root để giả lập landscape.
            e.cam.transform.rotation =
                e.baseRotation * Quaternion.Euler(0f, 0f, -portraitRotationDeg);

            // Xóa mọi aspect bị ép cứng bằng tay (vd game gọi
            // Camera.main.aspect = 800/480f) — nếu không, camera render theo
            // tỉ lệ landscape cố định và để lại thanh đen/letterbox trên màn
            // portrait. ResetAspect() trả camera về tỉ lệ thật của viewport.
            e.cam.ResetAspect();

            if (e.cam.orthographic)
            {
                // Canvas Screen-Space Overlay luôn ép matchWidthOrHeight = 0 ở portrait
                // (khớp theo trục width vật lý — xem ApplyPortrait canvas loop bên dưới).
                // Để world-space (bao gồm mọi collider/UI giả lập bằng world object,
                // vd tutorial highlight) tiếp tục khớp pixel-perfect với Canvas đó,
                // orthographicSize PHẢI theo đúng công thức tương đương: giữ cố định
                // nửa-chiều-rộng thế giới (baseOrthographicSize * designAspect) và suy
                // ra nửa-chiều-cao từ aspect thực tế — tức orthoSize = base / cam.aspect.
                // (cam.aspect ở đây đã được ResetAspect() ở trên = Screen.width/Screen.height.)
                //
                // Lưu ý: dùng công thức "fill/fit" min/max cũ sẽ làm world-space bị lệch
                // tỉ lệ so với Canvas → các collider tag "isTutorial" (StepTutorial.
                // IsRightInputPos) đè sai vị trí lên nút UI khi ở portrait.
                e.cam.orthographicSize = e.baseOrthographicSize / e.cam.aspect;

                if (debugLog)
                    Debug.Log($"[RSWebGLLandscape] baseOrtho={e.baseOrthographicSize:F3} " +
                              $"designAspect={designAspect:F3} camAspect={e.cam.aspect:F3} " +
                              $"({Screen.width}x{Screen.height}) " +
                              $"→ ortho={e.cam.orthographicSize:F3}");
            }
            else if (debugLog)
            {
                Debug.LogWarning($"[RSWebGLLandscape] Camera '{e.cam.name}' is PERSPECTIVE — " +
                                 "orthographicSize scaling skipped; letterbox is expected. " +
                                 "A perspective camera needs FOV/distance handling instead.");
            }
        }

        // ── Canvases ──────────────────────────────────────────────────────────
        for (int i = _canvases.Count - 1; i >= 0; i--)
        {
            var e = _canvases[i];

            if (e.canvas == null)
            {
                _canvases.RemoveAt(i);
                continue;
            }

            if (e.scaler != null &&
                e.scaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                e.scaler.referenceResolution =
                    new Vector2(e.baseRefRes.y, e.baseRefRes.x);

                e.scaler.matchWidthOrHeight = 0f;
            }

            // Both Screen-Space Overlay and Screen-Space Camera get the manual
            // pivot: Unity does re-orient a Screen-Space Camera canvas's own
            // RectTransform to align with its Render Camera (shown as "driven by
            // Canvas" in the Inspector), but the rendered result still comes out
            // screen-locked/unrotated on the final frame either way — same as
            // Overlay — so it needs the exact same manual compensation.
            if (e.rotationRoot != null)
                continue;

            Vector2 landscapeSize =
                e.baseRefRes != Vector2.zero
                    ? new Vector2(
                        Mathf.Max(e.baseRefRes.x, e.baseRefRes.y),
                        Mathf.Min(e.baseRefRes.x, e.baseRefRes.y))
                    : landscapeRef;

            var rootGO = new GameObject("__PortraitRotationRoot__");
            var rt = rootGO.AddComponent<RectTransform>();

            rt.SetParent(e.canvas.transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = landscapeSize;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.Euler(0f, 0f, portraitRotationDeg);

            var children = new List<Transform>();

            foreach (Transform child in e.canvas.transform)
            {
                if (child != rt)
                    children.Add(child);
            }

            foreach (var child in children)
                child.SetParent(rt, false);

            e.rotationRoot = rootGO;
            _canvases[i] = e;
        }

        Canvas.ForceUpdateCanvases();
    }

    static void UnparentRotationRoot(Canvas canvas, GameObject rotationRoot)
    {
        var children = new List<Transform>();
        foreach (Transform child in rotationRoot.transform)
            children.Add(child);
        foreach (var child in children)
            child.SetParent(canvas.transform, false);

        Object.Destroy(rotationRoot);
    }

    void ResetToBaseline()
    {
        // ── WorldRoot ─────────────────────────────────────────────────────────
        if (worldRoot != null)
            worldRoot.rotation = _worldRootBaseRot;

        // ── Cameras ───────────────────────────────────────────────────────────
        for (int i = _cameras.Count - 1; i >= 0; i--)
        {
            var e = _cameras[i];
            if (e.cam == null) { _cameras.RemoveAt(i); continue; }
            e.cam.transform.rotation = e.baseRotation;
            e.cam.ResetAspect();
            if (e.cam.orthographic)
                e.cam.orthographicSize = e.baseOrthographicSize;
        }

        // ── Canvases ──────────────────────────────────────────────────────────
        for (int i = _canvases.Count - 1; i >= 0; i--)
        {
            var e = _canvases[i];
            if (e.canvas == null) { _canvases.RemoveAt(i); continue; }

            if (e.rotationRoot != null)
            {
                UnparentRotationRoot(e.canvas, e.rotationRoot);
                e.rotationRoot = null;
                _canvases[i] = e;
            }

            if (e.scaler != null && e.scaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                e.scaler.referenceResolution = e.baseRefRes;
                e.scaler.matchWidthOrHeight = 1f; // landscape → fit height
            }
        }

        Canvas.ForceUpdateCanvases();
    }
}
