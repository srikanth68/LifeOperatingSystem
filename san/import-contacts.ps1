$csv = Import-Csv "C:\Users\srila\Downloads\contacts.csv"
$api = "http://localhost:5300/api/people"
$imported = 0
$skipped = 0
$skippedNames = @()

function SafeStr($val) { if ($null -eq $val) { return "" } else { return $val.Trim() } }

$junkNames = @("Dropped Pin", "Me", "Fd", "halifax delete", "madhuri canada", "Puppi")
$junkPhonePatterns = @("^\*\d+", "^100$", "^101$", "^102$", "^411$", "^611$", "^275$")

foreach ($row in $csv) {
    $first = SafeStr $row.'First Name'
    $middle = SafeStr $row.'Middle Name'
    $last = SafeStr $row.'Last Name'

    if ($last -match "^AT&T:") {
        $skipped++
        $skippedNames += "AT&T: $last"
        continue
    }

    $parts = @($first, $middle, $last) | Where-Object { $_ -ne "" }
    $name = ($parts -join " ").Trim()

    $email = SafeStr $row.'E-mail Address'
    if (-not $email) { $email = SafeStr $row.'E-mail 2 Address' }

    if (-not $name -and $email) {
        $name = ($email -split "@")[0]
    }

    if (-not $name) {
        $skipped++
        $skippedNames += "(empty row)"
        continue
    }

    if ($junkNames -contains $name) {
        $skipped++
        $skippedNames += $name
        continue
    }

    $phone = ""
    $phoneCandidates = @(
        (SafeStr $row.'Mobile Phone'),
        (SafeStr $row.'Home Phone'),
        (SafeStr $row.'Business Phone'),
        (SafeStr $row.'Car Phone'),
        (SafeStr $row.'Other Phone')
    )
    foreach ($p in $phoneCandidates) {
        if ($p) {
            $p = $p -replace '[^\x20-\x7E+()]', ''
            $phone = $p.Trim()
            break
        }
    }

    $cleanPhone = $phone -replace '[\s\-\(\)\+]', ''
    $isJunkPhone = $false
    foreach ($pat in $junkPhonePatterns) {
        if ($cleanPhone -match $pat) {
            $isJunkPhone = $true
            break
        }
    }

    if (-not $phone -and -not $email) {
        $skipped++
        $skippedNames += "$name (no contact info)"
        continue
    }

    if ($isJunkPhone -and -not $email) {
        $skipped++
        $skippedNames += "$name (junk phone: $phone)"
        continue
    }

    if ($isJunkPhone) { $phone = "" }

    $birthday = SafeStr $row.'Birthday'
    if ($birthday -and $birthday -notmatch '^\d{4}-\d{2}-\d{2}$') {
        $birthday = ""
    }

    $company = SafeStr $row.'Company'
    $tags = $company
    $notes = SafeStr $row.'Notes'

    $bodyObj = @{ name = $name; relationship = "other" }
    if ($phone) { $bodyObj.phone = $phone }
    if ($email) { $bodyObj.email = $email }
    if ($birthday) { $bodyObj.birthday = $birthday }
    if ($notes) { $bodyObj.notes = $notes }
    if ($tags) { $bodyObj.tags = $tags }

    $body = $bodyObj | ConvertTo-Json

    try {
        $resp = Invoke-RestMethod -Uri $api -Method Post -Body $body -ContentType "application/json; charset=utf-8" -ErrorAction Stop
        $imported++
        if ($imported % 50 -eq 0) { Write-Host "  ... imported $imported so far" }
    } catch {
        Write-Host "FAILED: $name - $($_.Exception.Message)"
        $skipped++
        $skippedNames += "$name (API error)"
    }
}

Write-Host ""
Write-Host "=== Import Complete ==="
Write-Host "Imported: $imported"
Write-Host "Skipped:  $skipped"
Write-Host ""
Write-Host "Skipped entries:"
$skippedNames | ForEach-Object { Write-Host "  - $_" }
