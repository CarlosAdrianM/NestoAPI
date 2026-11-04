/*
    NestoAPI#814 (sugerencia 1020 de Paloma; decisión de Carlos 04/11/26): reposición puntual que lleva un compañero.

    Cuando un compañero pasa por la tienda (a una reunión, por ejemplo) y luego va a Algete, se le da la puntual. No tiene
    Ariadna: no lee ningún bulto. Al enviar la puntual en Nesto («¿Quién la lleva?» → «Un compañero») se escribe su nombre
    (texto libre) y la puntual queda recogida en el acto, sin movimientos de bultos ni furgoneta. Algete la recibe como siempre.

    Columna nueva en ReposicionesTraspasos (la cabecera de cada traspaso):
      - LaLleva varchar(50) NULL: el compañero que lleva la puntual («Manuel»). NULL: el repartidor.
    Con ella se rellenan también FechaRecogida (cuándo se le dio) y UsuarioRecogida (quién lo apuntó) de esa cabecera, solo de
    esa: el viaje de hoy sigue esperando al repartidor. Las puntuales con LaLleva no cuentan en «Recogidos N de M bultos».

    ORDEN: da igual antes o después del deploy de la API. La columna NO va en el EDMX (la API la lee y la escribe con SQL tras
    COL_LENGTH): sin este script todo sigue como hasta ahora y «Un compañero» da 409 («falta lanzar el script»).
    Necesita Scripts/Issue581_ReposicionesTraspasos_Recogida.sql (FechaRecogida, UsuarioRecogida).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene permiso). Idempotente.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ReposicionesTraspasos') IS NULL OR COL_LENGTH('dbo.ReposicionesTraspasos', 'FechaRecogida') IS NULL
BEGIN
    RAISERROR('Falta la tabla ReposicionesTraspasos o sus columnas de recogida: lanzar antes Scripts/Issue577_ReposicionesTraspasos.sql y Scripts/Issue581_ReposicionesTraspasos_Recogida.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

IF COL_LENGTH('dbo.ReposicionesTraspasos', 'LaLleva') IS NULL
BEGIN
    ALTER TABLE dbo.ReposicionesTraspasos ADD LaLleva varchar(50) NULL;
    PRINT 'Columna LaLleva añadida.';
END
ELSE
    PRINT 'La columna LaLleva ya existía.';
GO

-- Comprobación: las cabeceras de hoy de las tiendas a Algete
EXEC sp_executesql N'
SELECT Id, NumTraspaso, Origen, Destino, Herramienta, FechaPreparada, FechaRecogida, UsuarioRecogida, LaLleva
FROM dbo.ReposicionesTraspasos
WHERE Destino = ''ALG'' AND FechaCreacion >= CAST(GETDATE() AS date)
ORDER BY Id';
GO

SET NOEXEC OFF;
GO
