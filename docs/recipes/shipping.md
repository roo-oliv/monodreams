# Shipping: the six failures that only exist in production

A MonoDreams game is verified by `dotnet build`, `dotnet test`, and the headless
Demos host. None of them can see the failure class this page is about, because
its signature is:

> **the build succeeds, and the defect only appears after upload** — on itch.io's
> CDN, inside a macOS `.app` bundle, or on someone else's clone.

Every one of these passes `dotnet build MonoDreams.sln`, passes
`dotnet test MonoDreams.Tests/`, and runs perfectly from `bin/`. What breaks is
the *layout* the artifact lands in, the *toolchain* that gets carried along with
it, or the *files a clone does not have* — none of which the compiler models.
You cannot unit-test your way to them; they have to be **known**, and then
**asserted by a script that runs before the push**.

The reference game **Witch v Necromancer**
([`roo-oliv/gmtk-2026gj`](https://github.com/roo-oliv/gmtk-2026gj), public) paid
for all six the expensive way — a black-screen web upload, an app macOS called
damaged, a 116 MB Mac build for a pixel-art platformer, and a public repo nobody
could clone-and-build. This page turns that tuition into recipes.

**Nothing here lands in the engine.** Shipping is per-game: the artifact layout,
the bundle identifier, the itch target, the licensed assets. MonoDreams stays a
framework (`CORE_TENETS.md` §1) and the game repo owns its `tools/`. So each
recipe points at a **reference implementation to adapt**, living in a *game*
repo, never in `MonoDreams/`.

> **Read alongside:** [`docs/web-targeting.md`](../web-targeting.md) — the
> `.Core` + per-platform-heads model, `-p:MonoDreamsPlatform=web`, the KNI
> content build and its macOS/Linux native-lib shim. Recipes 1, 2, 3 and 5 are
> all about *packaging what that document builds*.

Reference implementations, all on `main`:

| File | Recipe |
|---|---|
| [`tools/publish-itch.sh`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/tools/publish-itch.sh) | 1, 4, 5, 6 (the publish gate) |
| [`tools/serve-web-build.py`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/tools/serve-web-build.py) | 2 |
| [`Gmtk2026.Desktop/Gmtk2026.Desktop.csproj`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/Gmtk2026.Desktop/Gmtk2026.Desktop.csproj) | 3 |
| [`Gmtk2026.Core/Content/MusicPlaceholders.targets`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/Gmtk2026.Core/Content/MusicPlaceholders.targets) | 6 |
| [`tools/build-placeholder-track.py`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/tools/build-placeholder-track.py) | 6 |
| [`docs/publishing-itch.md`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/docs/publishing-itch.md) | the game-side writeup of all of it |

---

## 1. The publish script asserts every upload precondition

**Signature.** The upload is accepted, the build page loads, and the game does
not run: a black canvas with `_framework/*` 404s in the console; or it boots and
throws on its first `Load<Texture2D>`; or macOS says *"the application is
damaged and can't be opened"*.

**Why it passes locally.** Each of these is a property of the **artifact**, not
of the code. `dotnet build` type-checks C#; it does not check that
`<base href>` is relative, that `index.html` is at the upload root, that the
content build actually emitted anything, that the JS interop shim versions in
`index.html` still match the restored packages, or that a Mach-O's architecture
matches the RID it was published for. Nothing in the toolchain owns those
invariants, so nothing fails when they are violated.

**Countermeasure.** Write a publish script that **stages, asserts, and only then
pushes** — and make `--push` opt-in, so packaging is always safe to run. The
reference `publish-itch.sh` gates every channel behind assertions that map 1:1
to a failure it has actually seen:

```bash
[[ -f "$stage/index.html" ]] || die "index.html is not at the upload root"

# One awk pass, not grep|head|sed — see the pipefail trap in recipe 4.
base_href=$(awk 'match($0, /<base href="[^"]*"/) {
    s = substr($0, RSTART, RLENGTH); sub(/.*href="/, "", s); sub(/"$/, "", s); print s; exit }' \
  "$stage/index.html")
[[ -n "$base_href" ]] || die "no <base href> — Blazor cannot resolve _framework/"
[[ "$base_href" != /* ]] || die "<base href=\"$base_href\"> is ABSOLUTE — every _framework/ fetch will 404"

content_files=$(find "$stage/Content" -type f 2>/dev/null | wc -l | tr -d ' ')
[[ "$content_files" -gt 10 ]] || die "Content/ has only $content_files file(s) — the content build did not run"
```

The `Content/` assertion is the one people skip, and it is the sharpest.
**The content build is a separate MSBuild path from the C# compile.** On web
that path is KNI's builder plus the macOS/Linux native-lib shim documented in
[`web-targeting.md`](../web-targeting.md#macos--linux-native-lib-shim-required)
— a chain of copied native libs, staged pipeline DLLs, and a nested
`dotnet build -p:MonoDreamsPlatform=web`. When a link in it no-ops, MGCB
produces nothing and **the C# build still reports success**. You get a green
build and a game that dies on its first asset load. Count the files.

The interop-shim check closes the trap `web-targeting.md` names explicitly: a
head's `index.html` references `nkast.Wasm.*` JS by **version**
(`_content/nkast.Wasm.Canvas/js/Canvas.8.0.11.js`), hand-maintained against
whatever `nkast.Kni.Platform.Blazor.GL` transitively pulls. Bump the KNI package
without editing every `<script src>` and you get a perfectly green build with a
runtime 404 on every shim. Assert it from the file itself, so the check cannot
drift from the markup:

```bash
while read -r shim; do
  [[ -f "$stage/$shim" ]] || { note "MISSING interop shim: $shim"; missing=$((missing + 1)); }
done < <(grep -o '_content/[^"]*\.js' "$stage/index.html")
```

For the desktop/mac channel the equivalents are: the executable exists at
`Contents/MacOS/<CFBundleExecutable>` **with the exec bit**, its architecture
matches the RID, its signature survived the copy (recipe 4), and the bundle
holds real `.xnb`:

```bash
arch_line=$(file -b "$app/Contents/MacOS/$EXECUTABLE")
case "$rid:$arch_line" in
  osx-arm64:*arm64*) ;;
  osx-x64:*x86_64*)  ;;
  *) die "$EXECUTABLE is '$arch_line', which does not match $rid" ;;
esac
```

Two more things worth stealing from the reference script:

- **Stamp the build with the commit.** `version_label()` emits
  `0.1.0+<short-sha>` and appends `-dirty` when the tree is not clean. "It
  worked on the build page" is worth nothing if you cannot tell which tree
  produced it.
- **Run the cheapest gate first.** The music preflight (recipe 6) runs *before
  any build*, so a non-shippable tree costs seconds instead of a full
  three-channel package.

---

## 2. Test the web build at a subpath, never at a root

**Signature.** The web upload shows a black canvas. The devtools console is a
wall of 404s on `/_framework/blazor.boot.json`, `/_framework/dotnet.native.wasm`
— absolute paths that resolve against the CDN root instead of the build.

**Why it passes locally.** itch.io does not serve an HTML5 game from a domain
root. It serves it from a **per-build subpath**:

```
https://html-classic.itch.zone/html/<build-id>/index.html
```

Every asset Blazor fetches is resolved against `<base href>`. The two obvious
local tests — `python3 -m http.server` in the stage dir, and
`dotnet run --project MyGame.Web -p:MonoDreamsPlatform=web` (the Blazor dev
server) — both serve **from a root**. That is the one layout itch never uses, so
an absolute `<base href="/">` passes both and breaks in production. The test
does not exercise the failure mode.

**Countermeasure.** Two halves, and you want both.

1. **Set `<base href="./">`** in the head's `wwwroot/index.html` — relative, so
   the same bundle works at a root *and* under `/html/<id>/`. Assert it in the
   publish script (recipe 1).
2. **Serve the staged build under a fake itch subpath** and make every 404
   scream. `serve-web-build.py` mounts at a hardcoded
   `MOUNT = "/html/2846731/"` (the id is arbitrary — what matters is that the
   game is *not* at `/`), strips the prefix in `translate_path`, refuses to
   escape the build dir, and lets anything **outside** the mount 404 on purpose:

   ```python
   # A request for /_framework/... means <base href> is absolute.
   # Let it 404 and be logged — that is the bug this server exists to catch.
   def send_error(self, code, message=None, explain=None):
       if code == 404:
           Handler.misses.append(self.path)
           sys.stderr.write(f"\033[1;31m  404  {self.path}\033[0m\n")
       super().send_error(code, message, explain)
   ```

   Successful requests are not logged at all — only failures are interesting.
   On Ctrl-C it prints the 404 count, which is the number you actually want.

**MIME types are part of the test.** The server sets them explicitly rather than
trusting the host database, because `.wasm` **must** be `application/wasm` or
`WebAssembly.instantiateStreaming` refuses the module, and Python's `mimetypes`
does not know `.wasm` on every version. The table also covers the MonoDreams
content types so a local pass means what a remote pass means:

```python
TYPES = {
    ".wasm": "application/wasm",           # load-bearing: instantiateStreaming validates this
    ".xnb":  "application/octet-stream",   # built MonoGame/KNI content
    ".mdscene": "application/octet-stream",# native levels, MGCB /copy:-bundled
    ".dat":  "application/octet-stream",   # ICU data
}
```

That `.mdscene` line is MonoDreams-specific and load-bearing. A shipped game
boots **native scenes only** (`CORE_TENETS.md` §6): `LoadLevelRequest` probes
`Content/Levels/<id>.mdscene` and **fails loud** when it is absent — there is no
format fallback to degrade into. So a level missing from, or mis-served by, the
upload is a hard boot failure, and this server is where you find out.

Wire it into the publish script so the smoke test is one flag:
`publish-itch.sh --only web --serve` stages, asserts, then `exec`s the server.

---

## 3. `ExcludeAssets="all"` on every content-pipeline `PackageReference`

**Signature.** A shipped macOS build of a pixel-art game weighs **116 MB**, and
`Contents/MacOS/` contains `libassimp` (10 MB), `libfreeimage` (12 MB),
`ffmpeg`, `ffprobe`, `basisu`, `crunch`, and SharpDX's **D3D shader
compiler** — on a platform with no DirectX. Nobody notices until someone reads
the bundle, because a fat build is not an error.

**Why it passes locally.** A head must reference the content-pipeline packages
so restore populates them, and by default a `PackageReference`'s assets **flow
into the project's output**. Those packages are the *authoring* toolchain: model
importers, texture compressors, audio transcoders — tools for **building**
content. A shipped game only ever **reads** `.xnb`. The excess is functionally
invisible: nothing crashes, nothing warns, the game runs fine. It is just a
third of your download.

**Countermeasure.** Mark the pipeline packages **build-time only**. Per the
dependency-parity table in
[`web-targeting.md`](../web-targeting.md#dependency-parity-what-changes-per-backend),
that is two packages per head:

```xml
<!-- Desktop head -->
<PackageReference Include="MonoGame.Extended.Content.Pipeline" Version="4.1.0" ExcludeAssets="all" />
<PackageReference Include="MonoGame.Framework.Content.Pipeline" Version="3.8.4" ExcludeAssets="all" />

<!-- Web head — GeneratePathProperty because the head Imports the builder's .targets
     and passes /reference: paths straight out of the package -->
<PackageReference Include="nkast.Xna.Framework.Content.Pipeline.Builder" Version="4.2.9001"
                  ExcludeAssets="all" GeneratePathProperty="true" />
<PackageReference Include="KNI.Extended.Content.Pipeline" Version="6.0.0"
                  ExcludeAssets="all" GeneratePathProperty="true" />
```

**Why the content build is unaffected — the mechanism that makes this safe.**
MGCB runs **out-of-process**. `MonoGame.Content.Builder.Task` invokes the
`dotnet-mgcb` tool, which carries its own pipeline; custom importers are handed
to it as `/reference:` paths resolved from the **NuGet cache**
(`$(NuGetPackageRoot)monogame.extended.content.pipeline/4.1.0/tools/...`), and
`restore` fills that cache whether or not assets flow into the project. The web
head does the same with `$(PkgKNI_Extended_Content_Pipeline)`-style path
properties — which is exactly why it needs `GeneratePathProperty="true"`
alongside the exclusion. Nothing in either content build reads the head's
output directory, so removing the assets from it costs nothing.

**Verify it, don't assume it.** The failure mode of getting this wrong is a
content build that breaks *only from clean*, because a stale `Content/bin` hides
it. Clear the intermediate + output content dirs, rebuild, and confirm the
`.xnb` count is unchanged:

```bash
rm -rf MyGame.Core/Content/bin MyGame.Core/Content/obj
dotnet build MyGame.Desktop -c Release
find MyGame.Desktop/bin/Release/net8.0/Content -name '*.xnb' | wc -l
```

Measured on the reference game's `osx-arm64` publish: **116 MB → 91 MB**, with
both bitmap fonts still building. Note the residue — 91 MB is the untrimmed
self-contained .NET runtime, a different problem with a different fix.

---

## 4. Codesign the plain executable, before bundling it

**Signature.** On Apple Silicon the `.app` does not launch at all — macOS
reports it as damaged, or it dies instantly with no window. Or the signing step
itself fails with one of:

```
bundle format unrecognized, invalid, or unsuitable
code object is not signed at all ... System.IO.dll
```

**Why it passes locally.** Your own `dotnet run` never goes through a bundle,
and the machine that built the binary trusts it. **Apple Silicon refuses to
execute an unsigned arm64 binary outright** — this is a hard gate, not
cosmetics — but you only meet it on a *copied* artifact. And the signing failure
above is an *ordering* fault that looks like a signing fault: `codesign`
inspects the path it is given, and a target inside `.app/Contents/MacOS` makes
it adopt **the whole bundle** as the signing subject and walk everything sitting
beside the executable. In a MonoDreams game, two things live there and both are
fatal to it:

- **The raw art drop folder.** Assets referenced by `file:` AssetKeys are copied
  raw beside the executable (the game resolves from `AppDomain.BaseDirectory`; a
  bundle launches with `CWD=/`, so nothing may resolve relative to the working
  directory). An animation authored as a *directory with an extension*
  (`Chest Open.anim`) reads to `codesign` as a malformed nested bundle.
- **The managed assemblies.** `System.IO.dll` and friends are not Mach-O, so
  they are "code objects not signed at all".

This is also why `--deep` is unusable here.

**Countermeasure.** Sign **in the plain publish directory**, where the binary is
a lone Mach-O with nothing around it to misread, then let `cp -a` carry the
signature into the bundle — a signature is embedded in the Mach-O, so copying
preserves it:

```bash
dotnet publish "$DESKTOP_PROJECT" -c Release -r "$rid" --self-contained true -o "$publish"

codesign --force --sign - "$publish/$EXECUTABLE" \
  || die "codesign failed — an arm64 build will not launch at all"

mkdir -p "$app/Contents/MacOS"
cp -a "$publish/." "$app/Contents/MacOS/"
```

Then verify **pre-copy**, on the lone Mach-O — asking `codesign` about the copy
inside `.app/Contents/MacOS` reintroduces the same bundle ambiguity (it reports
`Format=app bundle` and walks the art tree), and that check has failed
spuriously on a correctly signed build. Confirm the copy separately with an
exact byte comparison, which cannot be confused by the `.app` around it:

```bash
cmp -s "$publish/$EXECUTABLE" "$app/Contents/MacOS/$EXECUTABLE" \
  || die "the bundled executable differs from the signed one — cp did not carry the signature in"
```

Ad-hoc signing does **not** avoid the Gatekeeper prompt on a downloaded build —
that needs a Developer ID and notarisation. It makes the app launchable once the
user allows it, which on arm64 is the difference between a game and a dead icon.

While writing the `Info.plist`, set **`NSHighResolutionCapable`**. Without it
macOS runs the app at 1× and upscales the whole window, softening every pixel in
a game whose entire look is exact integer pixel scaling — a MonoDreams game
rendering at a virtual resolution is precisely that game.

### The pipefail trap: a passing check that reads as failing

This one costs a full debugging session and belongs in the same recipe, because
it is what a verification script does to *itself*.

Under `set -o pipefail` (implied by the near-universal `set -euo pipefail`), a
**consumer that exits early** — `grep -q`, `head -n1`, `awk … exit` — closes the
pipe, the **producer dies of SIGPIPE (exit 141)**, and the pipeline reports
failure. So a *passing* check reads as a *failing* one. The reference script's
signature check did exactly this: it aborted a correctly signed build with "not
signed", and the same command run by hand (no `pipefail`) passed — which sent
the diagnosis chasing `codesign`'s bundle behaviour rather than the shell.

**Capture output into a variable and match it**, or use a single process that
reads the file itself:

```bash
# WRONG: grep -q closes the pipe, codesign gets SIGPIPE, pipefail reports failure.
codesign -dv "$publish/$EXECUTABLE" 2>&1 | grep -q "Signature=adhoc"

# RIGHT: no pipe to break.
signature=$(codesign -dv "$publish/$EXECUTABLE" 2>&1) || true
[[ "$signature" == *"Signature=adhoc"* ]] || die "codesign reported success but the binary is not signed"
```

The same reasoning is why recipe 1's `<base href>` extraction is a single `awk`
pass instead of `grep | head -1 | sed`. Any verification pipeline that ends in
an early-exiting consumer is suspect: audit for `| grep -q`, `| head`, and
`| awk '…; exit'`.

---

## 5. Prune Blazor's `*.br` / `*.gz` before uploading to itch

**Signature.** Nothing breaks. The web upload is simply a third larger than it
needs to be, and every player waits for bytes no browser will ever request.

**Why it passes locally.** `dotnet publish` emits **pre-compressed copies** of
the WASM bundle for hosts that negotiate `Content-Encoding` — the Blazor dev
server and most static hosts do, so locally the compressed variants are the ones
actually served and everything looks efficient. **itch does not negotiate
`Content-Encoding`**: the loader fetches the plain files, and the `.br`/`.gz`
siblings are dead weight sitting in the upload. It is a correctness-free
failure, which is why it survives every test you have.

**Countermeasure.** Delete them at stage time, along with `web.config` (IIS
hosting config, meaningless on itch):

```bash
cp -a "$WEB_PUBLISH/." "$stage/"          # trailing /. so index.html lands at the stage ROOT
pruned=$(find "$stage" -type f \( -name '*.br' -o -name '*.gz' \) | wc -l | tr -d ' ')
find "$stage" -type f \( -name '*.br' -o -name '*.gz' \) -delete
rm -f "$stage/web.config"
note "pruned $pruned pre-compressed file(s) + web.config"
```

Measured on the reference game: **9.2 MB of a 28 MB upload → 19 MB**, by
deletion alone. Note the `cp -a "$src/." "$dst/"` idiom — the trailing `/.` is
what puts `index.html` at the stage **root** rather than nesting a directory,
which is recipe 1's first assertion.

If you also zip for a manual dashboard upload, zip **from inside** the stage dir
(`cd "$stage" && zip -qr … .`) for the same reason: itch opens
`<root>/index.html` and nothing else.

---

## 6. Ship a committed placeholder for gitignored licensed content

**Signature.** Two distinct failures from one cause, and the second is worse
than the first.

1. **A public repo nobody can build.** A fresh clone fails the content build
   with `error : The source file '…/Content/Music/ambient-calm.ogg' does not
   exist!` — five of them in the reference game. Not "builds without music":
   **could not build at all**. Every contributor, every CI job, every agent that
   clones the repo hits a wall.
2. **A mute release.** Once you *do* paper over the missing inputs, the build
   succeeds, the upload works, and the shipped game is silent — with nothing
   downstream saying so.

**Why it passes locally.** The owner's machine has the licensed masters, so both
failures are invisible there by construction. And **MGCB hard-fails on a missing
input** while having no conditionals and no globbing — the `.mgcb` is a literal
list of files that must exist. A gitignored asset plus a `#begin` block for it is
a build that only its author can run.

**Countermeasure — three rules, and each one is load-bearing.**

**(a) Substitute a committed placeholder for any absent input; never overwrite a
real file.** An MSBuild target hooked `BeforeTargets="RunContentBuilder"` (the
target name is the same for `MonoGame.Content.Builder.Task` and the KNI builder,
so **one file serves both heads**) copies a committed stand-in over anything
missing:

```xml
<ItemGroup>
  <_GmtkMissingMusic Include="@(_GmtkMusicFile)" Condition="!Exists('%(_GmtkMusicFile.Identity)')" />
</ItemGroup>
<!-- SourceFiles is the placeholder repeated once per destination — "this one file, to all of these". -->
<Copy SourceFiles="@(_GmtkMissingMusic->'$(GmtkPlaceholderTrack)')"
      DestinationFiles="@(_GmtkMissingMusic)" Condition="'@(_GmtkMissingMusic)' != ''" />
```

The `Condition="!Exists(...)"` is the whole safety story: a real track — the
case on the author's machine and in every release — is untouched, and the build
output stays byte-identical to what it always was.

Derive the list from the **`.mgcb` itself** (`sed`/`ReadLinesFromFile` over the
`#begin Music/` blocks), never from a second list in the target. The manifest
*is* the set of inputs the build needs; a copy of it is one more thing to forget
when an asset is added.

Substituting an input beats the seemingly cleaner alternative of splitting the
optional blocks into their own `.mgcb`: for BlazorGL the KNI builder puts every
content reference in **one output dir and one intermediate dir**, and MGCB
cleans output whose source is absent from the response file it was handed — so
two `.mgcb` files sharing an intermediate dir invite each build to delete the
other's work. Substitution needs nothing from either head's content plumbing,
keeps one `.mgcb` as the manifest, and is per-asset rather than all-or-nothing.

**(b) Warn on EVERY build, by hashing — never with a marker file.** The warning
must be as reliable on the tenth incremental build as on the clone, because the
build that ships is rarely the build that wrote the placeholder. Compare each
input's SHA256 against the placeholder's:

```xml
<GetFileHash Files="$(GmtkPlaceholderTrack)" Algorithm="SHA256">
  <Output TaskParameter="Hash" PropertyName="_GmtkPlaceholderHash" />
</GetFileHash>
<GetFileHash Files="@(_GmtkMusicFile)" Algorithm="SHA256" Condition="'$(_GmtkPlaceholderHash)' != ''">
  <Output TaskParameter="Items" ItemName="_GmtkHashedMusic" />
</GetFileHash>
<Warning Condition="'@(_GmtkPlaceholderMusic)' != ''" Code="GMTK0001"
         Text="SILENT PLACEHOLDER music: @(_GmtkPlaceholderMusic->'%(Filename)', ', '). DO NOT SHIP THIS BUILD." />
```

A marker file beside each placeholder is the obvious alternative and is
**strictly worse: it is state that can go stale, and going stale means a silent
release nobody was warned about.** The hash is derived from the artifacts
themselves every build, so it cannot desync. This is also why the placeholder is
**committed** rather than encoded during the build: a committed file has a
stable hash (and encoding it at build time would need the very `ffmpeg` that
arrives *with* the content build — a chicken-and-egg the first build on a fresh
machine should not have to win). See `build-placeholder-track.py`, which bakes
2 s of silence *once*, writing the samples with Python's `wave` module and
letting MGCB's bundled ffmpeg do only the transcode it certainly supports.

Make the placeholder **silence**, not a tone. It is what a missing asset should
sound like; a placeholder beep is worse than the absence it stands for, and it
would survive into a build somebody ships.

**(c) Gate the publish on it.** A warning scrolls past in a wall of MSBuild
output; a push does not. The publish script re-derives the same list from the
same `.mgcb` and **refuses to package** when anything is missing or hashes to
the placeholder — before any build runs, so it costs seconds:

```bash
while read -r track; do
  path="Gmtk2026.Core/Content/$track"
  if [[ ! -f "$path" ]]; then missing+=("$track")
  elif [[ "$(shasum -a 256 "$path" | cut -d' ' -f1)" == "$placeholder_hash" ]]; then silent+=("$track")
  fi
done < <(sed -n 's|^#begin \(Music/.*\)$|\1|p' "$manifest")
```

The failure message must name the **command that fixes it** and the file that
records how to re-derive each asset (the reference uses an `ATTRIBUTION.md` with
the master and the exact clip arguments per track). Same for the MSBuild
`<Error>` path when even the placeholder is gone: name
`python3 tools/build-placeholder-track.py`, don't make the reader deduce it.

Nothing here is music-specific. Any gitignored licensed input a `.mgcb` lists —
music, voice, purchased art — takes the same three rules.

---

## The pre-push checklist

For an agent packaging a MonoDreams game, in order:

1. **Preflight, before any build** — every `.mgcb`-listed licensed asset is
   present and none hashes to the placeholder (recipe 6c).
2. **Build** — `dotnet publish MyGame.Web -c Release -p:MonoDreamsPlatform=web`
   (global `-p:`, so it flows through restore to `.Core`) and
   `dotnet publish MyGame.Desktop -c Release -r <rid> --self-contained true`.
   Build the desktop head at least once first: the web content build borrows
   native FreeImage/freetype from the cached `dotnet-mgcb` tool
   ([`web-targeting.md`](../web-targeting.md#macos--linux-native-lib-shim-required)).
3. **Stage web** — `cp -a "$src/." "$stage/"`, then delete `*.br`, `*.gz`,
   `web.config` (recipe 5).
4. **Assert web** — `index.html` at the root; `<base href>` present and
   **relative**; `_framework/blazor.boot.json` + `dotnet.native.wasm` present;
   `Content/` file count > 10; every `_content/**/*.js` shim referenced by
   `index.html` exists on disk (recipes 1, 2).
5. **Smoke-test web** — serve the *staged* dir at `/html/<id>/` and confirm
   **zero 404s** (recipe 2).
6. **Sign then bundle mac** — `codesign --force --sign -` on the **plain publish
   executable**, verify there via a captured variable (not a pipe), `cp -a` into
   `Contents/MacOS/`, then `cmp -s` the two (recipe 4).
7. **Assert mac** — `Info.plist` present with `NSHighResolutionCapable`;
   `Contents/MacOS/<CFBundleExecutable>` exists and is executable; `file -b`
   architecture matches the RID; `.xnb` count > 10 (recipes 1, 4).
8. **Push** — only behind an explicit `--push`, with a commit-stamped
   user version.

Also confirm your `ExcludeAssets="all"` is in place (recipe 3) whenever you
touch a head's `PackageReference` list — it is the one recipe that is a *code*
change rather than a script step, and the only one whose regression is silent
even in production.

## See also

- [`docs/web-targeting.md`](../web-targeting.md) — the `.Core` + heads model,
  `$(MonoDreamsPlatform)`, the KNI content build, the `nkast.Wasm.*` shim
  version coupling, and the Reach render limits. Everything recipes 1–3 and 5
  package.
- [`docs/CORE_TENETS.md`](../CORE_TENETS.md) — engine-wide invariants; §6
  (native-only level load, fail-loud) and §9 (a scene is ship-ready exactly when
  it has zero `file:` AssetKeys) both have upload consequences.
- [`CONTRIBUTING.md`](../../CONTRIBUTING.md) — build/test workflow and OS setup.
- [`roo-oliv/gmtk-2026gj`](https://github.com/roo-oliv/gmtk-2026gj) — the
  reference game these recipes were extracted from. Its
  [`docs/publishing-itch.md`](https://github.com/roo-oliv/gmtk-2026gj/blob/main/docs/publishing-itch.md)
  is the game-side companion: channel table, itch page settings `butler` cannot
  set, and the measured sizes quoted above.
