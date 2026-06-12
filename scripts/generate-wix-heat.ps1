# generate-wix-heat.ps1 — Generates a WiX v4 .wxs fragment from a directory listing.
# Replaces the WixToolset.Heat extension for CLI-based WiX builds.
#
# Usage:
#   pwsh scripts/generate-wix-heat.ps1 -SourceDir publish/CatalogBrowser -OutputFile heat-catalogbrowser.wxs -DirectoryRefId INSTALLDIR -ComponentGroupId PublishedFiles

param(
    [Parameter(Mandatory)][string]$SourceDir,
    [Parameter(Mandatory)][string]$OutputFile,
    [Parameter(Mandatory)][string]$DirectoryRefId,
    [Parameter(Mandatory)][string]$ComponentGroupId
)

$files = Get-ChildItem -Path $SourceDir -Recurse -File | Sort-Object FullName

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine("    <ComponentGroup Id=`"$ComponentGroupId`" Directory=`"$DirectoryRefId`">")

$id = 0
foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($SourceDir.Length + 1).Replace('\', '/')
    $componentId = "cmp_$($id.ToString("D4"))"
    $fileId = "fil_$($id.ToString("D4"))"
    $id++

    # Use Guid="*" for auto-generation in WiX v4
    [void]$sb.AppendLine("      <Component Id=`"$componentId`" Guid=`"*`">")
    [void]$sb.AppendLine("        <File Id=`"$fileId`" Source=`"$($file.FullName.Replace('\','/'))`" KeyPath=`"yes`" />")
    [void]$sb.AppendLine('      </Component>')
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$sb.ToString() | Out-File -FilePath $OutputFile -Encoding UTF8 -NoNewline
Write-Host "Generated $OutputFile with $id file components from $SourceDir"
