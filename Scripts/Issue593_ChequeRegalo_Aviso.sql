/*
    NestoAPI#593 (aviso): columnas para el correo al cliente con su cheque regalo.

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene permiso). Idempotente: se puede
    lanzar dos veces. Requiere Issue593_ChequeRegalo_Tablas.sql. Se puede lanzar ANTES o DESPUÉS de publicar la API:
    sin estas columnas la API no manda el correo ni pone la nota al pie (lo demás sigue igual).

    No choca con Issue593_ChequeRegalo_c4.sql (canje): este script solo añade ImagenCorreo a la campaña y
    FechaAvisoCorreo/CorreoAviso a los cheques. NO toca CanjeHasta, ni las exclusiones, ni Activa: la campaña
    sigue INACTIVA.

      ChequesRegaloCampanas.ImagenCorreo   la imagen del cheque que va en el correo: nombre del recurso incrustado
                                           en la API (NestoAPI\Resources\ChequesRegalo). La de octubre dice «50 €»;
                                           febrero llevará la suya (nuevo recurso + UPDATE). NULL = correo sin imagen.
      ChequesRegalo.FechaAvisoCorreo       cuándo se le mandó el correo al cliente. NULL = sin mandar: la
                                           reconciliación de la noche lo vuelve a intentar mientras se pueda canjear.
      ChequesRegalo.CorreoAviso            a qué correos se mandó, o el motivo de que no saliera
                                           («(sin correo)», «(falló el envío)»).
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.ChequesRegaloCampanas', 'ImagenCorreo') IS NULL
    ALTER TABLE dbo.ChequesRegaloCampanas ADD ImagenCorreo varchar(100) NULL;
GO

IF COL_LENGTH('dbo.ChequesRegalo', 'FechaAvisoCorreo') IS NULL
    ALTER TABLE dbo.ChequesRegalo ADD FechaAvisoCorreo datetime NULL;
GO

IF COL_LENGTH('dbo.ChequesRegalo', 'CorreoAviso') IS NULL
    ALTER TABLE dbo.ChequesRegalo ADD CorreoAviso varchar(500) NULL;
GO

-- La imagen de la campaña de octubre (solo si aún no tiene ninguna)
UPDATE dbo.ChequesRegaloCampanas
SET ImagenCorreo = 'cheque50.jpg', FechaModificacion = GETDATE()
WHERE Codigo = 'CHEQUE50_OCT_2026' AND ImagenCorreo IS NULL;
GO

-- Los cheques que ya existieran (pruebas) se quedan sin avisar: los mandaría la reconciliación si la campaña
-- estuviera activa. Comprobación:
SELECT Codigo, ImporteBase, MinimoCanje, CanjeHasta, ImagenCorreo, Activa FROM dbo.ChequesRegaloCampanas;
SELECT COUNT(*) AS Cheques, SUM(CASE WHEN FechaAvisoCorreo IS NULL THEN 1 ELSE 0 END) AS SinAvisar FROM dbo.ChequesRegalo;
