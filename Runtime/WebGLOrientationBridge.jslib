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

            isPortrait: function() {
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
    },

    // Synchronous portrait query — safe to call on the very first frame.
    WebGLOriBridge_IsPortrait: function() {
        return window.innerHeight > window.innerWidth ? 1 : 0;
    },

    WebGLOriBridge_Cleanup: function() {
        var bridge = Module['WebGLOriBridge'];
        if (!bridge || !bridge.initialized) return;

        window.removeEventListener('resize', bridge.notify);
        window.removeEventListener('orientationchange', bridge.notify);
        if (bridge.debounceTimer) clearTimeout(bridge.debounceTimer);
        bridge.initialized = false;
    }
});
