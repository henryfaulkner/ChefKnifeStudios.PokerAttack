<#
.SYNOPSIS
Run reviewed PokerAttack SELECT/catalog queries through existing trusted VM SSH.
#>
[CmdletBinding(DefaultParameterSetName = 'Inline')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Inline')]
    [string]$Sql,
    [Parameter(Mandatory, ParameterSetName = 'Files')]
    [string[]]$SqlFile,
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9.-]*$')]
    [string]$Server = '170.187.142.115',
    [ValidatePattern('^[a-z_][a-z0-9_-]*$')]
    [string]$SshUser = 'root',
    [string]$IdentityFile = (Join-Path $env:USERPROFILE '.ssh/id_ed25519_linode_us_southeast'),
    [ValidatePattern('^[a-z][a-z0-9_]{0,62}$')]
    [string]$Database = 'expensetrackerdb',
    [ValidateSet('table', 'csv')]
    [string]$Format = 'table',
    [ValidateRange(1, 300)]
    [int]$StatementTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ssh = Get-Command ssh -CommandType Application -ErrorAction Stop
if (-not (Test-Path -LiteralPath $IdentityFile -PathType Leaf)) { throw 'The selected SSH identity file is missing.' }
$queries = if ($PSCmdlet.ParameterSetName -eq 'Files') {
    @($SqlFile | ForEach-Object { Get-Content -LiteralPath $_ -Raw -Encoding UTF8 })
} else { @($Sql) }
foreach ($query in $queries) {
    if ([string]::IsNullOrWhiteSpace($query)) { throw 'SQL must not be empty.' }
    if ($query -match '(?m)^\s*\\') { throw 'Supply reviewed SQL, not psql backslash commands.' }
}

# All interpolated shell values are validated identifiers/numbers; SQL is stdin only.
$remote = "sudo -n -u postgres psql -X -w -q -v ON_ERROR_STOP=1 -v expected_db=$Database -P pager=off -P null=NULL --dbname=$Database"
if ($Format -eq 'csv') { $remote += ' --csv' }
$guard = @'
SELECT current_database() = :'expected_db' AS target_matches \gset
\if :target_matches
'@
$finish = @'
\else
\echo Selected database does not match expected target; query refused.
\quit 3
\endif
ROLLBACK;
'@
$body = "BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;`n" +
    "SET LOCAL TIME ZONE 'UTC';`nSET LOCAL statement_timeout = '$($StatementTimeoutSeconds)s';`n" +
    $guard + "`n" + (($queries | ForEach-Object { $_ + "`n;" }) -join "`n") + "`n" + $finish
$previousEncoding = $OutputEncoding
try {
    $OutputEncoding = [Text.UTF8Encoding]::new($false)
    $body | & $ssh.Source -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=yes -o ConnectTimeout=10 -i $IdentityFile "$SshUser@$Server" $remote
    if ($LASTEXITCODE -ne 0) { throw "SSH/psql failed with exit code $LASTEXITCODE; partial output is not a complete report." }
} finally {
    $OutputEncoding = $previousEncoding
}
