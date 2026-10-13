/*
    NestoAPI#593 (estadística por vendedor): cuántos días antes del final del canje los clientes con el cheque sin usar
    suben en «clientes para contactar» (prioridad Máxima, los primeros, con «Tiene un cheque de 50 € sin usar hasta el
    07/11»).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene permiso). Idempotente. OPCIONAL: sin
    la columna (o a NULL) la API usa 10 días (ReglasEstadisticaChequesRegalo.DIAS_IMPULSO_LLAMADAS_POR_DEFECTO). Se
    puede lanzar antes o después de publicar la API.

      ChequesRegaloCampanas.DiasImpulsoLlamadas   los N últimos días del canje, CanjeHasta incluido (10 → del 29/10 al
                                                  07/11). NULL = 10; 0 = sin impulso.

    NO toca nada más de la campaña.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.ChequesRegaloCampanas', 'DiasImpulsoLlamadas') IS NULL
    ALTER TABLE dbo.ChequesRegaloCampanas ADD DiasImpulsoLlamadas int NULL;
GO

-- La campaña de octubre: los 10 últimos días (del 29/10 al 07/11), solo si aún no tiene valor
UPDATE dbo.ChequesRegaloCampanas
SET DiasImpulsoLlamadas = 10, FechaModificacion = GETDATE()
WHERE Codigo = 'CHEQUE50_OCT_2026' AND DiasImpulsoLlamadas IS NULL;
GO

SELECT Codigo, CanjeHasta, DiasImpulsoLlamadas, DATEADD(day, 1 - ISNULL(DiasImpulsoLlamadas, 10), CanjeHasta) AS DesdeElDia
FROM dbo.ChequesRegaloCampanas;
GO
