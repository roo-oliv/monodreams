// Shared BlazorGL host loop for every MonoDreams web head. Loaded by each head's index.html as
//   <script src="_content/MonoDreams.Web.Hosting/js/host.js"></script>
// after the nkast.Wasm.* interop scripts. GameCanvas (the .NET root component) calls initRenderJS
// once the canvas exists; from there requestAnimationFrame drives TickDotNet every frame.

function tickJS() {
    window.theInstance.invokeMethod('TickDotNet');
    window.requestAnimationFrame(tickJS);
}

// The canvas FILLS the window and its DRAWING BUFFER equals its on-screen (CSS) size — 1:1,
// full-window. This keeps the cursor locked to the mouse: KNI maps a pointer event to
// backbuffer space by the canvas buffer-to-display ratio; with buffer == display that ratio
// is always 1, so the mouse, the back buffer, and the engine's ScreenWidth all share ONE
// coordinate space — nothing can go stale and there is no scale error (the earlier drift came
// from a fixed 16:9 buffer scaled to fit, whose cached ratio was stale until a resize).
//
// Letterboxing is done by the ENGINE, not the canvas: the viewport manager fits the 16:9
// virtual resolution inside the (window-shaped) back buffer and FinalDrawSystem paints the
// margins black. So a single, correct letterbox on every aspect ratio — no CSS centering and
// no double letterbox when KNI sizes its back buffer to the window on resize.
function resizeCanvas() {
    var canvas = document.getElementById('theCanvas');
    var holder = document.getElementById('canvasHolder');
    var w = Math.max(1, holder.clientWidth), h = Math.max(1, holder.clientHeight);
    if (canvas.width !== w) canvas.width = w;     // drawing buffer == display size (1:1)
    if (canvas.height !== h) canvas.height = h;
}

// Browser autoplay policy: an AudioContext created before the first user gesture starts
// "suspended" and stays silent until resume() is called from a gesture handler. KNI's stack
// does NOT do this itself (verified against 4.2.9001: ConcreteAudioService.Suspend/Resume are
// empty bodies, nothing calls AudioContext.ResumeAsync, and the nkast.Wasm.Audio JS shim is a
// bare 1:1 WebAudio interop with no gesture listener). So the host page owns the unlock:
// wrap the shim's AudioContext factories to track live contexts, and resume any suspended one
// on the first pointerdown/keydown. Sources started while suspended (e.g. an ambient loop on
// screen load) begin sounding on resume — nothing is lost, only delayed until the gesture.
// Listeners stay attached (they're cheap and state-guarded) to cover contexts that suspend
// again, e.g. after an iframe re-embed. Load order guarantees nkAudioContext exists here:
// index.html loads the nkast.Wasm.* shims before host.js.
function hookAudioContextAutoResume() {
    if (!window.nkAudioContext || window.nkAudioContext.__mdAutoResumeHooked) return;
    window.nkAudioContext.__mdAutoResumeHooked = true;

    var liveContexts = [];
    ['Create', 'Create1'].forEach(function (name) {
        var original = window.nkAudioContext[name];
        window.nkAudioContext[name] = function () {
            var uid = original.apply(this, arguments);
            liveContexts.push(nkJSObject.GetObject(uid));
            return uid;
        };
    });

    function resumeSuspendedContexts() {
        liveContexts.forEach(function (ac) {
            if (ac.state === 'suspended') ac.resume();
        });
    }
    window.addEventListener('pointerdown', resumeSuspendedContexts, true);
    window.addEventListener('keydown', resumeSuspendedContexts, true);
}
hookAudioContextAutoResume();

window.initRenderJS = (instance) => {
    window.theInstance = instance;

    // Fallback for heads that load host.js before the nkast.Wasm.Audio shim: by the time
    // Blazor boots and calls initRenderJS, every classic <script> has executed (idempotent).
    hookAudioContextAutoResume();

    var canvas = document.getElementById('theCanvas');
    resizeCanvas();

    // keep canvas focusable so it receives keyboard input
    canvas.setAttribute('tabindex', '0');
    canvas.focus();
    canvas.addEventListener('pointerdown', () => canvas.focus());

    // disable context menu on right click
    canvas.addEventListener("contextmenu", e => e.preventDefault());

    // re-sync the drawing buffer (kept == display) whenever the window changes size
    window.addEventListener('resize', resizeCanvas);

    // begin game loop
    window.requestAnimationFrame(tickJS);
};

// Prevent Arrow keys / Spacebar from scrolling the outer page (e.g. iframe embeds).
window.addEventListener("keydown", function (event) {
    if ([32, 37, 38, 39, 40].indexOf(event.keyCode) > -1)
        event.preventDefault();
});
window.addEventListener("wheel", function (event) {
    event.preventDefault();
}, { passive: false });
