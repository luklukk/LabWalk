param(
    [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\6000.0.66f2\Editor',
    [string]$BuildDirectory=(Join-Path $PSScriptRoot '..\NativeBuild')
)
$ErrorActionPreference='Stop'
# Close Unity before replacing its loaded Windows DLL. No Rhino installation is needed.
$build=[IO.Path]::GetFullPath($BuildDirectory)
New-Item -ItemType Directory -Force $build | Out-Null
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$android=Join-Path $UnityEditor 'Data\PlaybackEngines\AndroidPlayer'
$cmake=Join-Path $android 'SDK\cmake\3.22.1\bin\cmake.exe'
$ninja=Join-Path $android 'SDK\cmake\3.22.1\bin\ninja.exe'
$ndk=Join-Path $android 'NDK'
foreach($path in @($cmake,$ninja,(Join-Path $ndk 'build\cmake\android.toolchain.cmake'))){if(-not(Test-Path $path)){throw "Missing tool: $path"}}
function Check-Exit([string]$step){if($LASTEXITCODE -ne 0){throw "$step failed ($LASTEXITCODE)."}}
$source=Join-Path $build 'rhino3dm'
if(-not(Test-Path (Join-Path $source '.git'))){
    & git clone --depth 1 --branch 8.35.0 https://github.com/mcneel/rhino3dm.git $source
    Check-Exit 'Rhino source download'
}
$revision=(& git -C $source rev-parse HEAD).Trim()
if($revision -ne 'f44a7887955d471ad3e52327a5a410a96aad7957'){throw 'Unexpected Rhino source revision; use a clean build directory.'}
& git -C $source submodule update --init --depth 1 src/lib/opennurbs
Check-Exit 'OpenNURBS download'
$open=Join-Path $source 'src\lib\opennurbs'
if((& git -C $open rev-parse HEAD).Trim() -ne 'eb92af3ba1806b0a34a99aba0d3bda83e3d46083'){throw 'Unexpected OpenNURBS revision.'}
# Upstream enables FreeType on Android without providing the dependency in this target.
# The viewer imports meshes, not annotation glyphs. Explicitly disable that unused feature.
$header=Join-Path $open 'opennurbs_system.h'
$text=Get-Content -LiteralPath $header -Raw
$text=$text.Replace('#define OPENNURBS_FREETYPE_SUPPORT','// Lab Walk: font outline rendering is outside the mesh importer.')
Set-Content -LiteralPath $header -Value $text -NoNewline
$native=Join-Path $build 'android-arm64'
& $cmake -S (Join-Path $source 'src\librhino3dm_native') -B $native -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_TOOLCHAIN_FILE=$ndk/build/cmake/android.toolchain.cmake" -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-32 -DANDROID_STL=c++_static -DCMAKE_BUILD_TYPE=Release '-DCMAKE_SHARED_LINKER_FLAGS=-Wl,-z,max-page-size=16384' '-DCMAKE_CXX_STANDARD_LIBRARIES=-lc++_static -lc++abi -latomic -lm'
Check-Exit 'Native configure'
# Explicit libc++ linkage also handles the Windows short-path clang executable name.
& $cmake --build $native --parallel 6
Check-Exit 'Native compile'
$package=Join-Path $build 'rhino3dm.8.35.0.zip'
if(-not(Test-Path $package)){Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/rhino3dm/8.35.0/rhino3dm.8.35.0.nupkg' -OutFile $package}
if((Get-FileHash $package).Hash -ne '068A4099C28874DA85456B814BDFC919CA5813EA14BA485AF02C538ABC288213'){throw 'NuGet package hash mismatch.'}
$expanded=Join-Path $build 'package'
Expand-Archive -LiteralPath $package -DestinationPath $expanded -Force
$plugins=Join-Path $project 'Assets\Plugins\Rhino3dm'
New-Item -ItemType Directory -Force $plugins,(Join-Path $plugins 'x86_64'),(Join-Path $plugins 'Android\arm64-v8a') | Out-Null
Copy-Item (Join-Path $expanded 'lib\netstandard2.0\Rhino3dm.dll') (Join-Path $plugins 'Rhino3dm.dll') -Force
Copy-Item (Join-Path $expanded 'runtimes\win-x64\native\librhino3dm_native.dll') (Join-Path $plugins 'x86_64\librhino3dm_native.dll') -Force
$library=Join-Path $plugins 'Android\arm64-v8a\librhino3dm_native.so'
Copy-Item (Join-Path $native 'librhino3dm_native.so') $library -Force
& (Join-Path $ndk 'toolchains\llvm\prebuilt\windows-x86_64\bin\llvm-strip.exe') --strip-unneeded $library
Check-Exit 'Strip native debug symbols'
Get-FileHash (Join-Path $plugins 'Rhino3dm.dll'),(Join-Path $plugins 'x86_64\librhino3dm_native.dll'),$library
