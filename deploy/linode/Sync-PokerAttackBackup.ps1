[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$backupRoot = Join-Path $env:USERPROFILE 'PokerAttack-Backups/recurring'
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$backupRoot = (Resolve-Path -LiteralPath $backupRoot).Path
$backupOwner = [Security.Principal.WindowsIdentity]::GetCurrent().Name
& icacls $backupRoot /inheritance:r /grant:r "${backupOwner}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot protect backup directory' }
$sshOptions = @('-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','StrictHostKeyChecking=yes','-o','ConnectTimeout=15','-i',"$env:USERPROFILE/.ssh/id_ed25519_linode_us_southeast")
$stamp = (& ssh @sshOptions root@170.187.142.115 'cat /var/lib/pokerattack/latest-backup').Trim()
if ($LASTEXITCODE -ne 0 -or $stamp -notmatch '^\d{8}T\d{6}Z$') { throw 'Cannot identify a completed remote snapshot' }
$snapshotTime = [DateTime]::ParseExact($stamp,'yyyyMMddTHHmmssZ',[Globalization.CultureInfo]::InvariantCulture,
    ([Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal))
if ([DateTime]::UtcNow - $snapshotTime -gt [TimeSpan]::FromHours(24)) { throw 'Remote snapshot is older than the recovery target' }
$destination = Join-Path $backupRoot $stamp
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$complete = Join-Path $destination 'complete.json'
if (-not (Test-Path -LiteralPath $complete)) {
    & scp -q -r @sshOptions "root@170.187.142.115:/var/backups/pokerattack/$stamp/." $destination
    if ($LASTEXITCODE -ne 0) { throw 'Backup transfer failed' }
    $verified = 0
    foreach ($line in Get-Content -LiteralPath (Join-Path $destination 'SHA256SUMS')) {
        if ($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9_.-]+)$') { throw 'Invalid checksum manifest' }
        $expected = $Matches[1]
        $file = [IO.Path]::GetFullPath((Join-Path $destination $Matches[2]))
        if ([IO.Path]::GetDirectoryName($file) -ne [IO.Path]::GetFullPath($destination)) { throw 'Backup path escaped directory' }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Transfer checksum mismatch' }
        $plain = [IO.File]::ReadAllBytes($file)
        $cipher = [Security.Cryptography.ProtectedData]::Protect($plain,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        [IO.File]::WriteAllBytes("$file.dpapi",$cipher)
        $roundtrip = [Security.Cryptography.ProtectedData]::Unprotect($cipher,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $actual = ([BitConverter]::ToString($hasher.ComputeHash($roundtrip))).Replace('-','').ToLowerInvariant() }
        finally { $hasher.Dispose() }
        if ($actual -ne $expected) { throw 'Encrypted checksum mismatch' }
        $plain = $null; $cipher = $null; $roundtrip = $null
        Remove-Item -LiteralPath $file
        $verified++
    }
    if ($verified -ne 7) { throw 'Incomplete backup manifest' }
    @{ snapshot=$stamp; files=$verified; encrypted='CurrentUser DPAPI'; verifiedUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath $complete -Encoding UTF8
}
& ssh @sshOptions root@170.187.142.115 "umask 077; printf '%s\n' '$stamp' > /var/lib/pokerattack/offhost-latest"
if ($LASTEXITCODE -ne 0) { throw 'Off-host acknowledgement failed' }
# Remove only completed, encrypted snapshots under the verified private root.
foreach ($directory in Get-ChildItem -LiteralPath $backupRoot -Directory) {
    if ($directory.Name -notmatch '^\d{8}T\d{6}Z$' -or $directory.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) { continue }
    if (-not (Test-Path -LiteralPath (Join-Path $directory.FullName 'complete.json'))) { continue }
    $timestamp = [DateTime]::ParseExact($directory.Name,'yyyyMMddTHHmmssZ',[Globalization.CultureInfo]::InvariantCulture)
    if ($timestamp -gt [DateTime]::UtcNow.AddDays(-7)) { continue }
    $resolved = (Resolve-Path -LiteralPath $directory.FullName).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne $backupRoot) { throw 'Retention path escaped private backup root' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output "Encrypted off-host backup verified: $stamp"
