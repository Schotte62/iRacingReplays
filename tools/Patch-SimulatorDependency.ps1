param([Parameter(Mandatory = $true)][string]$DllPath)

$ErrorActionPreference = 'Stop'
$cecilDll = Join-Path $PSScriptRoot '../packages/Mono.Cecil.0.11.6/lib/netstandard2.0/Mono.Cecil.dll'
if (!(Test-Path $cecilDll)) { throw "Mono.Cecil not restored: $cecilDll" }
Add-Type -Path (Resolve-Path $cecilDll)

$originalPath = (Resolve-Path $DllPath).Path
$patchedPath = "$originalPath.patched"
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($originalPath)
try {
    $type = $assembly.MainModule.Types | Where-Object FullName -eq 'iRacingSimulator.TrackConditions'
    $method = $type.Methods | Where-Object Name -eq 'TrackUsageFromString'
    if ($null -eq $method -or $method.Parameters.Count -ne 1) {
        throw 'Unexpected simulator dependency: TrackUsageFromString(string) is missing.'
    }

    # The upstream method calls usage.ToLower() without a null check. In some
    # iRacing replay sessions SessionTrackRubberState is absent. Return the
    # existing Unknown enum value (-1) rather than crashing on every seek.
    $il = $method.Body.GetILProcessor()
    $first = $method.Body.Instructions[0]
    $il.InsertBefore($first, $il.Create([Mono.Cecil.Cil.OpCodes]::Ldarg_0))
    $il.InsertBefore($first, $il.Create([Mono.Cecil.Cil.OpCodes]::Brtrue_S, $first))
    $il.InsertBefore($first, $il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_M1))
    $il.InsertBefore($first, $il.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    $assembly.Write($patchedPath)
} finally {
    $assembly.Dispose()
}
Move-Item -Force $patchedPath $originalPath
