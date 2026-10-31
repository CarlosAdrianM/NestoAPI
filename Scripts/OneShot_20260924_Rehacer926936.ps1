# NestoAPI#524: rehace el pedido 926936 con los descuentos del cliente 15191 (POST api/PedidosVenta como empleado).
# -Regalo: el regalo de Ganavisiones que eligió en TNV y que se perdió (TNV no manda los regalos al pedido).
param([ValidateSet("", "43406", "39796", "39798", "43408")][string]$Regalo = "")
$regalos = @{
  "43406" = @{ texto = "PASTA PARA DISEÑO DE CEJAS"; pvp = 12.20 }
  "39796" = @{ texto = "TINTE DE PESTAÑAS Y CEJAS NEGRO Nº1"; pvp = 6.60 }
  "39798" = @{ texto = "TINTE DE PESTAÑAS Y CEJAS NEGRO AZULADO Nº2"; pvp = 6.60 }
  "43408" = @{ texto = "EXFOLIANTE DE CEJAS"; pvp = 13.30 }
}
$pedido = Get-Content "$PSScriptRoot\OneShot_20260924_Rehacer926936.json" -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
if ($Regalo) {
  # Igual que los regalos que guarda Nesto: precio de tarifa, 100 % de descuento de línea, sin Aplicar Dto
  $linea = $pedido.Lineas[0].Clone()
  # Mismo texto que NestoApp: nombre a 40 caracteres + " (BONIF)"
  $nombre = $regalos[$Regalo].texto; if ($nombre.Length -gt 40) { $nombre = $nombre.Substring(0, 40) }
  $linea.Producto = $Regalo; $linea.texto = "$nombre (BONIF)"; $linea.Cantidad = 1
  $linea.PrecioUnitario = $regalos[$Regalo].pvp; $linea.DescuentoProducto = 0; $linea.DescuentoLinea = 1
  $linea.AplicarDescuento = $false; $linea.GrupoProducto = "COS"; $linea.SubgrupoProducto = $null; $linea.iva = "G21"
  $pedido.Lineas += $linea
}
$r = Invoke-RestMethod -Uri "http://api.nuevavision.es/api/auth/windows-token" -Method Post -UseDefaultCredentials -AllowUnencryptedAuthentication
$body = [Text.Encoding]::UTF8.GetBytes(($pedido | ConvertTo-Json -Depth 10))
$n = Invoke-RestMethod -Uri "http://api.nuevavision.es/api/PedidosVenta" -Method Post -Headers @{ Authorization = "Bearer " + $r.token } -ContentType "application/json; charset=utf-8" -Body $body
"Pedido nuevo: $($n.numero)  base: $($n.BaseImponible)  total: $($n.Total)  lineas: $($n.Lineas.Count)"