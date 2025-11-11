# Script PowerShell para probar el webhook localmente
# Simula lo que Google Pub/Sub enviaría

$endpoint = "http://localhost:53364/api/sync/webhook"

# Mensaje de prueba
$mensaje = @{
    tabla = "Clientes"
    accion = "actualizar"
    datos = @{
        parent = @{
            cliente_externo = "12345"
            contacto_externo = "001"
            name = "Test Cliente Local"
            mobile = "666123456"
            street = "Calle Test 123"
            city = "Madrid"
            zip = "28001"
        }
    }
} | ConvertTo-Json -Depth 5

Write-Host "Mensaje JSON:" -ForegroundColor Cyan
Write-Host $mensaje

# Codificar en base64 (como lo hace Google)
$mensajeBytes = [System.Text.Encoding]::UTF8.GetBytes($mensaje)
$mensajeBase64 = [Convert]::ToBase64String($mensajeBytes)

Write-Host "`nMensaje Base64:" -ForegroundColor Cyan
Write-Host $mensajeBase64

# Crear el request como lo envía Google Pub/Sub
$pubsubRequest = @{
    message = @{
        data = $mensajeBase64
        messageId = "test-local-$(Get-Date -Format 'yyyyMMddHHmmss')"
        publishTime = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    }
    subscription = "projects/test/subscriptions/test-local"
} | ConvertTo-Json -Depth 5

Write-Host "`nRequest completo (formato Google Pub/Sub):" -ForegroundColor Cyan
Write-Host $pubsubRequest

# Enviar al endpoint
Write-Host "`n========================================" -ForegroundColor Yellow
Write-Host "Enviando a: $endpoint" -ForegroundColor Yellow
Write-Host "========================================`n" -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Uri $endpoint -Method Post -Body $pubsubRequest -ContentType "application/json"

    Write-Host "✅ RESPUESTA EXITOSA:" -ForegroundColor Green
    Write-Host ($response | ConvertTo-Json -Depth 5)
}
catch {
    Write-Host "❌ ERROR:" -ForegroundColor Red
    Write-Host $_.Exception.Message

    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "`nDetalle del error:" -ForegroundColor Red
        Write-Host $responseBody
    }
}

Write-Host "`n========================================" -ForegroundColor Yellow
Write-Host "Verifica la consola de Visual Studio para ver los logs del servidor" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
