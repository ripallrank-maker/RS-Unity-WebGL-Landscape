mergeInto(LibraryManager.library, {

    // Called once at startup.
    // Installs resize/orientationchange listeners that forward orientation changes
    // to the Unity GameObject via SendMessage.
    // No CSS or input manipulation — Unity handles rotation entirely on its own side.
    WebGLOriBridge_Init: function(gameObjectNamePtr) {
        var goName = UTF8ToString(gameObjectNamePtr);

        // Each scene creates its own adapter GameObject/instance (scene-scoped,
        // no DontDestroyOnLoad) and calls Init() again on scene load — if a prior
        // scene's bridge is still marked initialized (e.g. its OnDestroy/Cleanup
        // hasn't run yet when this fires), only refresh which GameObject future
        // resize/orientationchange events target instead of skipping entirely,
        // otherwise SendMessage keeps firing at the previous scene's (possibly
        // gone) GameObject name and the new scene never hears about later
        // rotations.
        if (Module['WebGLOriBridge'] && Module['WebGLOriBridge'].initialized) {
            Module['WebGLOriBridge'].goName = goName;
            return;
        }

        Module['WebGLOriBridge'] = {
            initialized: true,
            goName: goName,
            debounceTimer: null,

            // Prefer visualViewport: window.innerWidth/innerHeight can include
            // area covered by the URL bar / on-screen keyboard and briefly
            // report stale values mid-rotation, which flips this comparison
            // and disagrees with the CSS canvas shape computed the same way in
            // index.html (applyCanvasFit/getViewportSize) — keep both reads
            // consistent so the canvas box and Unity's rotation state agree.
            isPortrait: function() {
                if (window.visualViewport) {
                    return window.visualViewport.height > window.visualViewport.width;
                }
                return window.innerHeight > window.innerWidth;
            },

            notify: function() {
                var bridge = Module['WebGLOriBridge'];
                if (bridge.debounceTimer) clearTimeout(bridge.debounceTimer);
                bridge.debounceTimer = setTimeout(function() {
                    bridge.debounceTimer = null;
                    var portrait = bridge.isPortrait() ? 1 : 0;
                    SendMessage(bridge.goName, 'OnBrowserOrientationChanged', portrait);
                }, 150);
            }
        };

        window.addEventListener('resize', Module['WebGLOriBridge'].notify);
        window.addEventListener('orientationchange', function() {
            setTimeout(Module['WebGLOriBridge'].notify, 300);
        });
        if (window.visualViewport) {
            window.visualViewport.addEventListener('resize', Module['WebGLOriBridge'].notify);
        }
    },

    // Synchronous portrait query — safe to call on the very first frame.
    WebGLOriBridge_IsPortrait: function() {
        if (window.visualViewport) {
            return window.visualViewport.height > window.visualViewport.width ? 1 : 0;
        }
        return window.innerHeight > window.innerWidth ? 1 : 0;
    },

    WebGLOriBridge_Cleanup: function() {
        var bridge = Module['WebGLOriBridge'];
        if (!bridge || !bridge.initialized) return;

        window.removeEventListener('resize', bridge.notify);
        window.removeEventListener('orientationchange', bridge.notify);
        if (window.visualViewport) {
            window.visualViewport.removeEventListener('resize', bridge.notify);
        }
        if (bridge.debounceTimer) clearTimeout(bridge.debounceTimer);
        bridge.initialized = false;
    }
});
