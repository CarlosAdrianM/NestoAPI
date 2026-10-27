/*
    NestoAPI#713 (incidencia 785, picking 99941, 27/10/26): cada escaneo hecho desde una parada de la recogida guarda
    también lo que pedía esa parada, para poder reconstruir un caso como el de Pedro (42 unidades tecleadas de la 45958
    cuando el picking pedía 10; la cantidad de la parada no quedaba en ningún sitio).

      PreparacionEscaneos.PendienteParada int NULL   lo que quedaba por recoger en la parada ANTES de aplicar el escaneo
      PreparacionEscaneos.TotalParada     int NULL   la cantidad total de la parada

    NULL = el escaneo no viene de una parada, o lo mandó una Ariadna anterior.

    Ejecutar en SSMS contra NV como sa (es DDL: el login nuevavision no tiene ALTER). Idempotente: se puede volver a lanzar.
    Orden indiferente respecto al deploy: mientras las columnas no existan, la API guarda los escaneos sin ellas (lo mira
    con COL_LENGTH y, si no están, vuelve a mirar cada 5 minutos, así que no hace falta reiniciar la API).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas. Sin índice: solo se miran al
    investigar una incidencia.
*/

USE NV;
GO

IF COL_LENGTH('dbo.PreparacionEscaneos', 'PendienteParada') IS NULL
    ALTER TABLE dbo.PreparacionEscaneos ADD PendienteParada int NULL;
GO

IF COL_LENGTH('dbo.PreparacionEscaneos', 'TotalParada') IS NULL
    ALTER TABLE dbo.PreparacionEscaneos ADD TotalParada int NULL;
GO

-- Comprobación: tienen que salir las dos, int y admitiendo nulos
SELECT c.name AS Columna, t.name AS Tipo, c.is_nullable AS AdmiteNulos
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND c.name IN ('PendienteParada', 'TotalParada');
GO
