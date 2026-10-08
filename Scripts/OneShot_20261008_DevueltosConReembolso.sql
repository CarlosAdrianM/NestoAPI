-- Sugerencia 544 (08/10/26): envíos YA DEVUELTOS (Estado 4) con reembolso > 0 sin pagar por la agencia.
-- SOLO LECTURA. Desde ahora el seguimiento quita el reembolso al pasar a DEVUELTO; estos son los de antes.
-- Columna SPago*: el apunte «S/Pago pedido N a AGENCIA c/CLIENTE» que se contabilizó al tramitar
-- (lo que deshace la vía manual: desliquidar si liquidaba algo + «Deshago» en _Reembolso).
SELECT e.Numero AS Envio,
       e.Pedido,
       RTRIM(e.Cliente) AS Cliente,
       RTRIM(e.Contacto) AS Contacto,
       RTRIM(c.Nombre) AS NombreCliente,
       RTRIM(a.Nombre) AS Agencia,
       e.Fecha,
       e.Reembolso,
       e.FechaRetornoRecibido,
       RTRIM(e.DetalleEstado) AS DetalleEstado,
       sp.[Nº Orden] AS SPagoNOrden,
       sp.Importe AS SPagoImporte,
       sp.ImportePdte AS SPagoPdte
FROM EnviosAgencia e WITH (NOLOCK)
JOIN AgenciasTransporte a WITH (NOLOCK) ON a.Numero = e.Agencia
LEFT JOIN Clientes c WITH (NOLOCK) ON c.Empresa = e.Empresa AND c.[Nº Cliente] = e.Cliente AND c.Contacto = e.Contacto
OUTER APPLY (SELECT TOP 1 x.[Nº Orden], x.Importe, x.ImportePdte
             FROM ExtractoCliente x WITH (NOLOCK)
             WHERE x.Empresa = e.Empresa AND x.[Número] = e.Cliente AND x.Contacto = e.Contacto
               AND x.Fecha = e.Fecha AND x.TipoApunte = '3' AND x.Importe = -e.Reembolso
               AND x.Concepto LIKE 'S/Pago pedido ' + CAST(e.Pedido AS varchar(10)) + ' a %'
             ORDER BY x.[Nº Orden] DESC) sp
WHERE e.Estado = 4
  AND e.Reembolso > 0
  AND e.FechaPagoReembolso IS NULL
ORDER BY e.Fecha, e.Numero;
