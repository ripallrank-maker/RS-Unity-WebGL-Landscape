using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;
#if RS_LANDSCAPE_CINEMACHINE
using Cinemachine;
#endif

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

    [Header("Safe Area")]
    [Tooltip("Co mọi Canvas Screen-Space (Overlay/Camera) vào Screen.safeArea bằng một " +
             "\"__SafeAreaRoot__\" wrapper, tránh notch/status-bar/home-indicator che UI. " +
             "Áp dụng độc lập với portrait/landscape.")]
    [SerializeField] private bool applySafeAreaToCanvas = false;

    [Tooltip("Cho phép GetSafeAreaWorldInsets() trả về giá trị khác 0, để các script điều " +
             "khiển camera world-space (vd CameraFollow2D/2DPvP) tự co bound clamp theo " +
             "Screen.safeArea. Không tự động làm gì nếu không có script nào gọi hàm này.")]
    [SerializeField] private bool applySafeAreaToWorld = false;

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
#if RS_LANDSCAPE_CINEMACHINE
    private readonly List<VirtualCameraEntry> _vcams = new List<VirtualCameraEntry>();
#endif

    struct CameraEntry
    {
        public Camera cam;
        public Quaternion baseRotation;
        public float baseOrthographicSize;
        // True when a CinemachineBrain drives this camera's transform/lens every
        // LateUpdate from a CinemachineVirtualCamera — direct writes here would be
        // overwritten the same frame, so rotation/orthoSize go to the vcam instead
        // (see VirtualCameraEntry / the _vcams loop in ApplyPortrait/ResetToBaseline).
        public bool driveByCinemachine;
    }

#if RS_LANDSCAPE_CINEMACHINE
    struct VirtualCameraEntry
    {
        public CinemachineVirtualCamera vcam;
        public Quaternion baseRotation;
        public float baseOrthographicSize;
    }
#endif

    struct CanvasEntry
    {
        public Canvas canvas;
        public CanvasScaler scaler;
        public Vector2 baseRefRes;
        public CanvasScaler.ScaleMode scaleMode;
        public GameObject rotationRoot;
        // Outermost wrapper (direct child of canvas) inset to Screen.safeArea via
        // offsetMin/offsetMax. rotationRoot (or, in landscape, the original
        // children) is parented INSIDE this instead of directly under canvas, so
        // the safe area applies regardless of portrait/landscape state.
        public GameObject safeAreaRoot;
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

#if UNITY_WEBGL && !UNITY_EDITOR
        // Some Android WebView engines briefly report a stale/incorrect
        // window.innerWidth/innerHeight right after a Unity scene transition
        // (their own layout/rotation-lock hasn't settled yet), which this
        // adapter would otherwise bake in as the wrong baseline for the whole
        // scene's lifetime — a plain resize event never follows because the
        // WebView's viewport doesn't actually change size again afterward.
        // Re-check shortly after and reapply if the first read turned out wrong.
        yield return RecheckOrientationAfterSettle();
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    IEnumerator RecheckOrientationAfterSettle()
    {
        yield return new WaitForSeconds(0.3f);
        if (!_baselineCaptured) yield break;

        bool actuallyPortrait = BridgeIsPortrait() == 1;
        if (actuallyPortrait == _isPortrait) yield break;

        if (debugLog)
            Debug.LogWarning($"[RSWebGLLandscape] Settle-recheck disagreed with the initial read " +
                              $"(initial isPortrait={_isPortrait}, now={actuallyPortrait}) — reapplying.");

        ResetToBaseline();
        _isPortrait = actuallyPortrait;
        ApplyCurrent();
        _lastW = Screen.width;
        _lastH = Screen.height;
        yield return new WaitForEndOfFrame();
        Canvas.ForceUpdateCanvases();
    }
#endif

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
        else if (applySafeAreaToCanvas)
        {
            // Same portrait/landscape state, but Screen.safeArea can still
            // change independently (e.g. rotating between landscape-left and
            // landscape-right swaps which side the notch is on) — refresh the
            // safe-area insets without a full rotation reset/apply.
            for (int i = 0; i < _canvases.Count; i++)
                UpdateSafeAreaRoot(i);
        }

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
    /// Registers a Canvas that was created/enabled after the initial baseline
    /// capture (e.g. a popup instantiated at runtime by a popup manager) and,
    /// if the app is currently in portrait, immediately applies the same
    /// rotation-root/scaler treatment used for canvases captured at Start().
    /// Safe to call multiple times for the same canvas (no-op once tracked).
    /// World-Space canvases are ignored, matching AppendNewObjects().
    /// </summary>
    public void NotifyCanvasShown(Canvas canvas)
    {
        if (canvas == null) return;
        if (canvas.renderMode == RenderMode.WorldSpace) return;
        if (!_baselineCaptured) CaptureBaseline();

        int index = _canvases.FindIndex(e => e.canvas == canvas);
        if (index < 0)
        {
            var sc = canvas.GetComponent<CanvasScaler>();
            var mode = sc != null ? sc.uiScaleMode : CanvasScaler.ScaleMode.ConstantPixelSize;
            var res = sc != null ? sc.referenceResolution : Vector2.zero;

            _canvases.Add(new CanvasEntry
            {
                canvas = canvas,
                scaler = sc,
                scaleMode = mode,
                baseRefRes = res,
                rotationRoot = null
            });
            index = _canvases.Count - 1;
        }

        UpdateSafeAreaRoot(index);

        if (_isPortrait)
            ApplyPortraitToCanvas(index);
    }

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
#if RS_LANDSCAPE_CINEMACHINE
        _vcams.Clear();
#endif
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
                baseOrthographicSize = cam.orthographicSize,
#if RS_LANDSCAPE_CINEMACHINE
                driveByCinemachine = cam.GetComponent<CinemachineBrain>() != null
#else
                driveByCinemachine = false
#endif
            });
        }

#if RS_LANDSCAPE_CINEMACHINE
        foreach (var vcam in Object.FindObjectsOfType<CinemachineVirtualCamera>(true))
        {
            if (vcam == null) continue;
            if (_vcams.Exists(e => e.vcam == vcam)) continue;
            _vcams.Add(new VirtualCameraEntry
            {
                vcam = vcam,
                baseRotation = vcam.transform.localRotation,
                baseOrthographicSize = vcam.m_Lens.OrthographicSize
            });
        }
#endif

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

            // Xóa mọi aspect bị ép cứng bằng tay (vd game gọi
            // Camera.main.aspect = 800/480f) — nếu không, camera render theo
            // tỉ lệ landscape cố định và để lại thanh đen/letterbox trên màn
            // portrait. ResetAspect() trả camera về tỉ lệ thật của viewport.
            // Aspect luôn reset ở đây kể cả khi CinemachineBrain điều khiển camera —
            // Brain không ghi đè aspect, chỉ ghi đè rotation/lens từ vcam mỗi LateUpdate.
            e.cam.ResetAspect();

            if (e.driveByCinemachine)
            {
                // CinemachineBrain sẽ ghi đè transform.rotation + orthographicSize của
                // camera này từ CinemachineVirtualCamera ngay LateUpdate kế tiếp, nên set
                // trực tiếp ở đây vô nghĩa — rotation/orthoSize được áp lên vcam bên dưới
                // (xem vòng lặp _vcams) để Brain tự copy sang camera thật.
                continue;
            }

            // Camera xoay ngược chiều canvas/root để giả lập landscape.
            e.cam.transform.rotation =
                e.baseRotation * Quaternion.Euler(0f, 0f, -portraitRotationDeg);

            if (e.cam.orthographic)
            {
                // Canvas Screen-Space Overlay luôn ép matchWidthOrHeight = 0 ở portrait
                // (khớp theo trục width vật lý — xem ApplyPortrait canvas loop bên dưới).
                // Để world-space (bao gồm mọi collider/UI giả lập bằng world object,
                // vd tutorial highlight) tiếp tục khớp pixel-perfect với Canvas đó,
                // orthoSize suy ra trực tiếp từ aspect thực tế (KHÔNG nhân designAspect
                // — đã verify bằng số đo thật: base=4 → ~7 ở aspect thiết kế (0.5625),
                // ~8 ở màn dài nhất 2:1 (aspect=0.5); nhân thêm designAspect sẽ overshoot
                // lên ~15, sai). cam.aspect ở đây đã được ResetAspect() ở trên =
                // Screen.width/Screen.height.
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

#if RS_LANDSCAPE_CINEMACHINE
        // ── Cinemachine virtual cameras ───────────────────────────────────────
        // Rotate/rescale the vcam instead of the Brain-driven Camera above: the
        // Brain copies the vcam's transform + m_Lens into the real Camera every
        // LateUpdate, so this is the only write that survives.
        float vcamAspect = _cameras.Exists(c => c.driveByCinemachine)
            ? _cameras.Find(c => c.driveByCinemachine).cam.aspect
            : (float)Screen.width / Screen.height;

        for (int i = _vcams.Count - 1; i >= 0; i--)
        {
            var v = _vcams[i];
            if (v.vcam == null)
            {
                _vcams.RemoveAt(i);
                continue;
            }

            v.vcam.transform.localRotation =
                v.baseRotation * Quaternion.Euler(0f, 0f, -portraitRotationDeg);

            var lens = v.vcam.m_Lens;
            lens.OrthographicSize = v.baseOrthographicSize / vcamAspect;
            v.vcam.m_Lens = lens;

            if (debugLog)
                Debug.Log($"[RSWebGLLandscape] vcam '{v.vcam.name}' baseOrtho={v.baseOrthographicSize:F3} " +
                          $"aspect={vcamAspect:F3} → ortho={lens.OrthographicSize:F3}");
        }
#endif

        // ── Canvases ──────────────────────────────────────────────────────────
        for (int i = _canvases.Count - 1; i >= 0; i--)
        {
            if (_canvases[i].canvas == null)
            {
                _canvases.RemoveAt(i);
                continue;
            }

            UpdateSafeAreaRoot(i);
            ApplyPortraitToCanvas(i);
        }

        Canvas.ForceUpdateCanvases();
    }

    /// <summary>
    /// Creates (once) or refreshes the "__SafeAreaRoot__" wrapper for a tracked
    /// canvas: a full-stretch RectTransform inset to Screen.safeArea via
    /// offsetMin/offsetMax (converted from screen pixels to canvas-local units
    /// through canvas.scaleFactor, so it's correct under any CanvasScaler mode).
    /// No-op if applySafeAreaToCanvas is off. Safe to call every frame the
    /// screen size / safe area may have changed (e.g. rotation, notch cutout
    /// toggling) — cheap RectTransform field writes only.
    /// </summary>
    void UpdateSafeAreaRoot(int i)
    {
        if (!applySafeAreaToCanvas) return;

        var e = _canvases[i];
        if (e.canvas == null) return;

        RectTransform rt;
        if (e.safeAreaRoot == null)
        {
            var go = new GameObject("__SafeAreaRoot__");
            rt = go.AddComponent<RectTransform>();
            rt.SetParent(e.canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;

            // Move whatever is currently a direct child of the canvas (original
            // UI content, or an already-existing rotationRoot) inside the wrapper
            // so the safe-area inset applies to everything uniformly.
            var existing = new List<Transform>();
            foreach (Transform child in e.canvas.transform)
                if (child != rt) existing.Add(child);
            foreach (var child in existing)
                child.SetParent(rt, false);

            e.safeAreaRoot = go;
            _canvases[i] = e;
        }
        else
        {
            rt = e.safeAreaRoot.GetComponent<RectTransform>();
        }

        Rect safe = Screen.safeArea;
        float scale = e.canvas.scaleFactor > 0f ? e.canvas.scaleFactor : 1f;

        rt.offsetMin = new Vector2(safe.xMin / scale, safe.yMin / scale);
        rt.offsetMax = new Vector2((safe.xMax - Screen.width) / scale, (safe.yMax - Screen.height) / scale);
    }

    /// <summary>
    /// Converts Screen.safeArea into world-space inset amounts to shrink a
    /// world-space camera-bound clamp rect (minX/maxX/minY/maxY) by, so
    /// non-Canvas objects (world sprites, gameplay bound clamps in
    /// CameraFollow2D/2DPvP) also respect the safe area. Returns all zeros
    /// (no-op) when applySafeAreaToWorld is off.
    ///
    /// <paramref name="camVerticalSize"/> is the world half-height the caller
    /// is ALREADY using for its own bound-clamp extent (orthographicSize, the
    /// Cinemachine vcam's m_Lens.OrthographicSize, or the perspective
    /// heightAtDistance/2 projection) — reuse it here instead of reading
    /// cam.orthographicSize directly, since that can be stale (Cinemachine
    /// Brain hasn't copied the vcam's lens into the real Camera yet this
    /// frame) or simply wrong for a perspective camera.
    ///
    /// Portrait handling: this adapter rotates the camera -90°/+90° about Z to
    /// simulate landscape, which swaps which physical screen axis maps to
    /// which world axis (same swap already applied to camHorizontalSize/
    /// camVerticalSize in CameraFollow2D/2DPvP's bound-clamp code) — physical
    /// top/bottom (Screen.height axis) maps to world X, physical left/right
    /// (Screen.width axis) maps to world Y.
    /// </summary>
    public void GetSafeAreaWorldInsets(float camVerticalSize, out float insetMinX, out float insetMaxX, out float insetMinY, out float insetMaxY)
    {
        insetMinX = insetMaxX = insetMinY = insetMaxY = 0f;
        if (!applySafeAreaToWorld) return;
        if (Screen.height <= 0) return;

        Rect safe = Screen.safeArea;
        float insetLeftPx = safe.xMin;
        float insetRightPx = Screen.width - safe.xMax;
        float insetBottomPx = safe.yMin;
        float insetTopPx = Screen.height - safe.yMax;

        // World units per physical screen pixel — identical along both screen
        // axes (no stretch), so one scalar covers both.
        float worldPerPx = (2f * camVerticalSize) / Screen.height;

        if (_isPortrait)
        {
            insetMinX = insetTopPx * worldPerPx;
            insetMaxX = insetBottomPx * worldPerPx;
            insetMinY = insetLeftPx * worldPerPx;
            insetMaxY = insetRightPx * worldPerPx;
        }
        else
        {
            insetMinX = insetLeftPx * worldPerPx;
            insetMaxX = insetRightPx * worldPerPx;
            insetMinY = insetBottomPx * worldPerPx;
            insetMaxY = insetTopPx * worldPerPx;
        }
    }

    /// <summary>
    /// Applies the portrait scaler swap + rotation-root pivot to a single
    /// tracked canvas by index. Extracted from ApplyPortrait() so newly
    /// registered canvases (see NotifyCanvasShown) can be brought in line
    /// with an already-portrait app without re-processing every canvas.
    /// Idempotent: safe to call again on a canvas that already has its
    /// rotation root (e.g. re-shown popup).
    /// </summary>
    void ApplyPortraitToCanvas(int i)
    {
        var e = _canvases[i];
        if (e.canvas == null) return;

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
        {
            _canvases[i] = e;
            return;
        }

        Vector2 landscapeRef = GetLandscapeRefRes();
        Vector2 landscapeSize =
            e.baseRefRes != Vector2.zero
                ? new Vector2(
                    Mathf.Max(e.baseRefRes.x, e.baseRefRes.y),
                    Mathf.Min(e.baseRefRes.x, e.baseRefRes.y))
                : landscapeRef;

        // When applySafeAreaToCanvas is on, the rotation root is parented
        // INSIDE the safe-area wrapper (not directly under the canvas) so its
        // center pivot sits at the safe-area's center rather than the full
        // screen's center.
        Transform container = e.safeAreaRoot != null ? e.safeAreaRoot.transform : e.canvas.transform;

        var rootGO = new GameObject("__PortraitRotationRoot__");
        var rt = rootGO.AddComponent<RectTransform>();

        rt.SetParent(container, false);
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = landscapeSize;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.Euler(0f, 0f, portraitRotationDeg);

        var children = new List<Transform>();

        foreach (Transform child in container)
        {
            if (child != rt)
                children.Add(child);
        }

        foreach (var child in children)
            child.SetParent(rt, false);

        e.rotationRoot = rootGO;
        _canvases[i] = e;
    }

    static void UnparentRotationRoot(Transform container, GameObject rotationRoot)
    {
        var children = new List<Transform>();
        foreach (Transform child in rotationRoot.transform)
            children.Add(child);
        foreach (var child in children)
            child.SetParent(container, false);

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
            e.cam.ResetAspect();

            if (e.driveByCinemachine) continue; // handled via the vcam loop below

            e.cam.transform.rotation = e.baseRotation;
            if (e.cam.orthographic)
                e.cam.orthographicSize = e.baseOrthographicSize;
        }

#if RS_LANDSCAPE_CINEMACHINE
        for (int i = _vcams.Count - 1; i >= 0; i--)
        {
            var v = _vcams[i];
            if (v.vcam == null) { _vcams.RemoveAt(i); continue; }

            v.vcam.transform.localRotation = v.baseRotation;
            var lens = v.vcam.m_Lens;
            lens.OrthographicSize = v.baseOrthographicSize;
            v.vcam.m_Lens = lens;
        }
#endif

        // ── Canvases ──────────────────────────────────────────────────────────
        for (int i = _canvases.Count - 1; i >= 0; i--)
        {
            var e = _canvases[i];
            if (e.canvas == null) { _canvases.RemoveAt(i); continue; }

            UpdateSafeAreaRoot(i);
            e = _canvases[i];

            if (e.rotationRoot != null)
            {
                Transform container = e.safeAreaRoot != null ? e.safeAreaRoot.transform : e.canvas.transform;
                UnparentRotationRoot(container, e.rotationRoot);
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
