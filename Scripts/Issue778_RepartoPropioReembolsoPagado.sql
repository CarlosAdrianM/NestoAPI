/*
    NestoAPI#778 (03/11/26): los envíos del reparto propio (agencias 14 y 16) con reembolso quedaban «pendientes de
    cobrar por la agencia» (FechaPagoReembolso NULL) y descuadraban el correo de cuadre de agencias con la 55500092.
    Sus cobros van por caja (efectivo) o liquidan la factura (tarjeta, #773), así que se dan por pagados.
    Lanzar como sa en NV. @Aplicar = 0 solo enseña.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @Aplicar bit = 0;

SELECT Numero, Empresa, Agencia, Pedido, RTRIM(Cliente) Cliente, Estado, Fecha, FechaEntrega, Reembolso
FROM dbo.EnviosAgencia
WHERE Agencia IN (14, 16) AND Reembolso <> 0 AND FechaPagoReembolso IS NULL
ORDER BY Numero;

IF @Aplicar = 0
BEGIN
    PRINT 'Solo lectura (@Aplicar = 0). Nada modificado.';
    RETURN;
END

UPDATE dbo.EnviosAgencia
SET FechaPagoReembolso = CAST(ISNULL(FechaEntrega, Fecha) AS date)
WHERE Agencia IN (14, 16) AND Reembolso <> 0 AND FechaPagoReembolso IS NULL;
PRINT CONCAT('Envíos del reparto propio dados por pagados: ', @@ROWCOUNT);
