# Rhino native dependency record — 2026-09-24

Included binaries:

| File under Assets/Plugins/Rhino3dm | SHA-256 |
|---|---|
| `Rhino3dm.dll` | `A1F9B3B31085FE57A0B37902467C76F7118ABD66C50DD1D401491B522F15F7E0` |
| `x86_64/librhino3dm_native.dll` | `BDFADDAA5CB2F084D08CBE7E9BE77F136669EA8C3673919DC7827EEB4D704BCF` |
| `Android/arm64-v8a/librhino3dm_native.so` | `6D6655118577B3A2D323A86486905B46919AC83C153A948D63247DD477D09EBF` |

Managed/Windows source: official NuGet `Rhino3dm` 8.35.0, package hash `068A4099C28874DA85456B814BDFC919CA5813EA14BA485AF02C538ABC288213`.

Android source: Rhino3dm tag `8.35.0`, commit `f44a7887955d471ad3e52327a5a410a96aad7957`; pinned OpenNURBS submodule `eb92af3ba1806b0a34a99aba0d3bda83e3d46083`.

Built with Unity 6000.0.66f2's Android NDK, SDK CMake 3.22.1/Ninja, Release, arm64-v8a, API 32, C++17, statically linked libc++. ELF alignment requested at 16 KB. Native debug symbols stripped; resulting library is 10,042,832 bytes. Dynamic dependencies are Android system `liblog`, `libm`, `libdl` and `libc`.

Two build accommodations:

1. Disabled OpenNURBS' Android FreeType define in `opennurbs_system.h`. Upstream's native target enabled font support without supplying its headers/library. Lab Walk does not render annotation glyphs; geometry reading remains available. This is a local source modification, not an upstream release binary.
2. Explicitly linked `c++_static` and `c++abi` because the Windows short-path compiler name otherwise omitted C++ runtime symbols at link time.

Rebuild with Unity closed: `pwsh -File Tools/Build-RhinoNative.ps1`. Requires Git, network access for the pinned downloads, and the pinned Unity Android tools. The script checks source revisions and the NuGet hash, applies the documented font change, builds, strips and copies the libraries. Keep the included plugin `.meta` files: Windows native code is Editor-only; Android native code is ARM64-only. macOS/Linux editor and Windows player builds are outside this milestone.

The native library compiled and linked for Android; physical Quest execution is not yet verified. Windows tests use the official Windows binary. Building and inspecting an ELF library does not validate Android marshaling or IL2CPP runtime behavior.

Dependency notices are included under `Docs/Licenses` and in the app's bundled third-party notices. Do not remove them when redistributing the libraries.
