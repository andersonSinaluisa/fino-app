$ErrorActionPreference = "Stop"

$api = if ([string]::IsNullOrWhiteSpace($env:NEXO_API_URL)) {
  "http://localhost:5080"
} else {
  $env:NEXO_API_URL.TrimEnd("/")
}

$email = if ([string]::IsNullOrWhiteSpace($env:NEXO_EMAIL)) {
  Read-Host "Email"
} else {
  $env:NEXO_EMAIL
}

if ([string]::IsNullOrWhiteSpace($env:NEXO_PASSWORD)) {
  $securePassword = Read-Host "Password" -AsSecureString
  $passwordPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
  try {
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPtr)
  } finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPtr)
  }
} else {
  $password = $env:NEXO_PASSWORD
}

try {
  $login = Invoke-RestMethod -Method Post -Uri "$api/api/v1/auth/login" -ContentType "application/json" -Body (@{
    email    = $email
    password = $password
  } | ConvertTo-Json)

  $result = Invoke-RestMethod -Method Post -Uri "$api/api/v1/pulses/evaluate" `
    -Headers @{ Authorization = "Bearer $($login.accessToken)" }

  $result | ConvertTo-Json -Depth 10
} catch {
  Write-Error "No se pudo ejecutar la notificacion contra $api. $($_.Exception.Message)"
  throw
}
