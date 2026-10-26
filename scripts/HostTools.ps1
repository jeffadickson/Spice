# Shell helpers configure only the current process, not global profiles or PATH.
function Import-SpiceTools {
    param([Parameter(Mandatory)][string]$Root)
    $localDotnet=Join-Path $Root '.tools/dotnet'
    $localRust=Join-Path $Root '.tools/rust'
    if (Test-Path (Join-Path $localDotnet $(if($IsWindows){'dotnet.exe'}else{'dotnet'}))) { $env:DOTNET_ROOT=$localDotnet; $env:PATH=$localDotnet+[IO.Path]::PathSeparator+$env:PATH }
    if (Test-Path (Join-Path $localRust 'cargo/bin')) { $env:CARGO_HOME=Join-Path $localRust 'cargo'; $env:RUSTUP_HOME=Join-Path $localRust 'rustup'; $env:PATH=(Join-Path $env:CARGO_HOME 'bin')+[IO.Path]::PathSeparator+$env:PATH }
    $profileRoot=[Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $userCargo=Join-Path $profileRoot '.cargo/bin'
    if (-not (Get-Command cargo -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $userCargo $(if($IsWindows){'cargo.exe'}else{'cargo'})))) { $env:PATH=$userCargo+[IO.Path]::PathSeparator+$env:PATH }
    $userDotnet=Join-Path $profileRoot '.dotnet'
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $userDotnet $(if($IsWindows){'dotnet.exe'}else{'dotnet'})))) { $env:DOTNET_ROOT=$userDotnet; $env:PATH=$userDotnet+[IO.Path]::PathSeparator+$env:PATH }
    # Preserve compatibility with the existing development host's portable runtime.
    if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
        $existing=Join-Path $Root '../../work/rust-runtime'
        if (Test-Path (Join-Path $existing 'cargo/bin/cargo.exe')) { $env:CARGO_HOME=(Resolve-Path (Join-Path $existing 'cargo')).Path; $env:RUSTUP_HOME=(Resolve-Path (Join-Path $existing 'rustup')).Path; $env:RUSTUP_TOOLCHAIN='stable-x86_64-pc-windows-gnu'; $env:PATH="$env:CARGO_HOME/bin;C:/dev/tools/msys64/ucrt64/bin;"+$env:PATH }
    }
}
function Import-SpiceMsvc {
    # link.exe needs SDK libraries as well as PATH; use Microsoft's developer shell.
    if (-not $IsWindows) { return }
    if (Get-Command link.exe -ErrorAction SilentlyContinue) { return }
    $where=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (Test-Path $where) {
        $installation=& $where -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
        if ($installation) {
            $module=Join-Path $installation 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll'
            if (Test-Path $module) { Import-Module $module; Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null }
        }
    }
}
