/*
    NestoAPI#593 (TNV): columnas para las push del cheque regalo a las clientas de la app de la tienda
    (TiendasNuevaVision).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene permiso). Idempotente: se puede
    lanzar dos veces. Requiere Issue593_ChequeRegalo_Tablas.sql. Se puede lanzar ANTES o DESPUÉS de publicar la API:
    sin estas columnas la API no manda push (el canje desde la app funciona igual; no las necesita).

    NO toca CanjeHasta, las exclusiones ni Activa: la campaña sigue INACTIVA.

      ChequesRegalo.FechaPushGenerado          cuándo se le mandó a la clienta la push «Tienes un cheque regalo…».
                                               NULL = sin mandar: si no tiene la app no se marca, y el job de la
                                               mañana (cheques-regalo-push, 10:00) se la manda si la instala después.
      ChequesRegalo.FechaPushRecordatorio      cuándo se le mandó el recordatorio «Te quedan 5 días…». Una sola vez.
      ChequesRegaloCampanas.DiasRecordatorioPush  cuántos días antes de CanjeHasta va el recordatorio (si el cheque
                                               sigue sin usar). NULL = 5; 0 = sin recordatorio.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.ChequesRegalo', 'FechaPushGenerado') IS NULL
    ALTER TABLE dbo.ChequesRegalo ADD FechaPushGenerado datetime NULL;
GO

IF COL_LENGTH('dbo.ChequesRegalo', 'FechaPushRecordatorio') IS NULL
    ALTER TABLE dbo.ChequesRegalo ADD FechaPushRecordatorio datetime NULL;
GO

IF COL_LENGTH('dbo.ChequesRegaloCampanas', 'DiasRecordatorioPush') IS NULL
    ALTER TABLE dbo.ChequesRegaloCampanas ADD DiasRecordatorioPush int NULL;
GO

-- La campaña de octubre: recordatorio 5 días antes del 7/11 (el 2/11), solo si aún no tiene valor
UPDATE dbo.ChequesRegaloCampanas
SET DiasRecordatorioPush = 5, FechaModificacion = GETDATE()
WHERE Codigo = 'CHEQUE50_OCT_2026' AND DiasRecordatorioPush IS NULL;
GO

-- Comprobación
SELECT Codigo, ImporteBase, MinimoCanje, CanjeHasta, DiasRecordatorioPush, Activa FROM dbo.ChequesRegaloCampanas;
SELECT COUNT(*) AS Cheques,
       SUM(CASE WHEN FechaPushGenerado IS NULL THEN 1 ELSE 0 END) AS SinPush,
       SUM(CASE WHEN FechaPushRecordatorio IS NULL THEN 1 ELSE 0 END) AS SinRecordatorio
FROM dbo.ChequesRegalo;
