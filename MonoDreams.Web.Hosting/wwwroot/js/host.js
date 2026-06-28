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

window.initRenderJS = (instance) => {
    window.theInstance = instance;

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
