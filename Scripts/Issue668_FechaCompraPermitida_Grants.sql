-- NestoAPI#668: Conciliación, si las compras están cerradas, ofrecer la primera fecha permitida.
-- ------------------------------------------------------------------------------------------------
-- POST api/PedidosCompra/CrearAlbaranYFactura comprueba ANTES de crear nada si la fecha de la factura
-- está abierta para compras (prdFechaEsVálida tipo 3) y, si no, lee FechaInicialCmp/FechaFinalCmp del
-- usuario en FechasPermitidas para proponer la primera fecha permitida (409 FechaCompraNoPermitida).
--
-- Hasta ahora la API solo llegaba a prdFechaEsVálida desde dentro de prdCrearFacturaCmp (encadenamiento
-- de propietarios) y nunca leía FechasPermitidas: los dos verbos son NUEVOS para la cuenta de la API.
-- Sin estos permisos NO se rompe nada: la comprobación falla, se registra en ELMAH («NestoAPI#668: no se
-- ha podido comprobar la fecha de compras...») y la factura sigue como siempre (el SP decide), pero no se
-- ofrece la fecha propuesta.
--
-- BD: NestoConnection (NV). Ejecutar con la cuenta privilegiada; da igual antes o después de publicar la API.

USE NV;
GO

GRANT EXECUTE ON dbo.[prdFechaEsVálida] TO [NUEVAVISION\RDS2016$];
GRANT SELECT ON dbo.FechasPermitidas TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación: fechas de compras de quien concilia (ajustar el usuario)
SELECT Empresa, Usuario, FechaInicialCmp, FechaFinalCmp, FechaInicialCnt, FechaFinalCnt, FechaInicialBanco, FechaFinalBanco
FROM dbo.FechasPermitidas
WHERE Usuario = N'NUEVAVISION\Carlos';
GO
