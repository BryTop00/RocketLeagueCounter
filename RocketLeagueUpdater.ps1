
param(
    [Parameter(Mandatory = $true)]
    [string]$Url,

    [Parameter(Mandatory = $true)]
    [string]$Hash,

    [Parameter(Mandatory = $true)]
    [string]$TargetExe,

    [Parameter(Mandatory = $true)]
    [int]$ProcessId
)

$ErrorActionPreference = "Stop"

$work = Join-Path $env:TEMP (
    "RocketLeagueUpdate-" + [guid]::NewGuid().ToString("N")
)

$targetDir = Split-Path -Parent $TargetExe
$targetScript = Join-Path $targetDir "RocketLeagueUpdater.ps1"
$backupExe = Join-Path $work "RocketLeagueCounter.exe.backup"
$backupScript = Join-Path $work "RocketLeagueUpdater.ps1.backup"

$exeUpdated = $false
$scriptUpdated = $false
$success = $false

try {
    # Verifica i parametri ricevuti.
    if ($Hash -notmatch '^[a-fA-F0-9]{64}$') {
        throw "Hash SHA-256 non valido."
    }

    if (-not [Uri]::TryCreate(
        $Url,
        [UriKind]::Absolute,
        [ref]$null
    )) {
        throw "URL di aggiornamento non valido."
    }

    $uri = [Uri]$Url

    if ($uri.Scheme -ne "https") {
        throw "Il download deve utilizzare HTTPS."
    }

    if ((Split-Path -Leaf $TargetExe) -ne "RocketLeagueCounter.exe") {
        throw "Percorso dell'eseguibile non valido."
    }

    # Aspetta la chiusura completa del contatore.
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue

    if ($null -ne $process) {
        Wait-Process -Id $ProcessId
    }

    New-Item -ItemType Directory -Path $work -Force | Out-Null

    $zip = Join-Path $work "update.zip"
    $extract = Join-Path $work "extracted"

    # Scarica il pacchetto.
    Invoke-WebRequest `
        -Uri $Url `
        -OutFile $zip `
        -UseBasicParsing

    # Verifica l'integrità prima di estrarre.
    $actualHash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash

    if ($actualHash -ine $Hash) {
        throw "La verifica SHA-256 non è riuscita. Il programma non è stato aggiornato."
    }

    # Estrae il pacchetto.
    Expand-Archive -Path $zip -DestinationPath $extract -Force

    $newExe = Join-Path $extract "RocketLeagueCounter.exe"
    $newScript = Join-Path $extract "RocketLeagueUpdater.ps1"

    if (-not (Test-Path -LiteralPath $newExe -PathType Leaf)) {
        throw "Il pacchetto non contiene RocketLeagueCounter.exe."
    }

    if (-not (Test-Path -LiteralPath $newScript -PathType Leaf)) {
        throw "Il pacchetto non contiene RocketLeagueUpdater.ps1."
    }

    # Conserva copie di sicurezza prima di sostituire i file.
    Copy-Item -LiteralPath $TargetExe -Destination $backupExe

    if (Test-Path -LiteralPath $targetScript) {
        Copy-Item -LiteralPath $targetScript -Destination $backupScript
    }

    # Sostituisce l'eseguibile e lo script.
    Copy-Item -LiteralPath $newExe -Destination $TargetExe -Force
    $exeUpdated = $true

    Copy-Item -LiteralPath $newScript -Destination $targetScript -Force
    $scriptUpdated = $true

    # Riavvia il programma aggiornato.
    Start-Process `
        -FilePath $TargetExe `
        -WorkingDirectory $targetDir

    $success = $true
}
catch {
    $errorMessage = $_.Exception.Message

    # Se la sostituzione fallisce, tenta il ripristino.
    try {
        if ($exeUpdated -and (Test-Path -LiteralPath $backupExe)) {
            Copy-Item -LiteralPath $backupExe -Destination $TargetExe -Force
        }

        if ($scriptUpdated -and (Test-Path -LiteralPath $backupScript)) {
            Copy-Item -LiteralPath $backupScript -Destination $targetScript -Force
        }
    }
    catch {
        $errorMessage += "`nAttenzione: anche il ripristino automatico ha incontrato un errore: $($_.Exception.Message)"
    }

    Add-Type -AssemblyName PresentationFramework

    [System.Windows.MessageBox]::Show(
        "Aggiornamento non riuscito.`n`n$errorMessage`n`nPuoi provare a riaprire il programma.",
        "RocketLeagueCounter - Aggiornamento",
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error
    ) | Out-Null
}
finally {
    # Le copie di sicurezza vengono mantenute fino al riavvio.
    if ($success) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}