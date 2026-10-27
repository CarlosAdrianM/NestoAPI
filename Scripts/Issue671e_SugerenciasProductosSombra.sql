/*
    NestoAPI#671 (corte 2e de la épica #669): sombra del modelo 2 de las sugerencias de la plantilla (aceptación: P(el
    cliente dice «Añadir» | se lo ofrecieron)).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. Requiere Issue670 e Issue671a (ya lanzados). La tabla no está en el EDMX (la API la escribe con SQL a mano) y la
    API mira con OBJECT_ID si existe, así que se puede lanzar antes o después del deploy: sin ella, Calcular no registra la
    sombra (todo lo demás igual) y el entrenamiento semanal usa solo las columnas de SugerenciasProductosMostradas.

    SugerenciasProductosSombra: una fila por sugerencia enseñada con el modelo 1 (VersionMotor «m1-…» o «m2-…»), con:
      - VersionSombra: «bayes-1» (P1 × tasa de aceptación suavizada, hasta que haya un modelo 2 que pase la puerta) o
        «m2-AAAAMMDD_HHmm» (el zip del modelo 2).
      - PuntuacionSombra: P2.
      - RangoSombra / RangoModelo1: su puesto (1 = el mejor) en el orden del modelo 2 y en el del modelo 1 (el de hoy) entre
        los Candidatos de esa llamada. Para medir: tasa de «Añadido» cuando RangoSombra <= 3 frente a cuando no.
      - VersionFeatures y Features: la instantánea de las features del modelo 1 de ese candidato al enseñarlo
        («Nombre=valor|…»), para entrenar el modelo 2 con lo que de verdad había (sin recalcular con fuga).
    Unos 1-2 KB por sugerencia (≈ 500 al día): menos de 1 MB al día.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT (nunca se actualiza ni se borra desde la API).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.SugerenciasProductosSombra') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosSombra (
        MostradaId int NOT NULL,                        -- SugerenciasProductosMostradas.Id
        VersionSombra varchar(20) NOT NULL,             -- bayes-1 | m2-AAAAMMDD_HHmm
        PuntuacionSombra decimal(12, 6) NULL,           -- P2
        RangoSombra smallint NULL,                      -- 1 = el mejor según el modelo 2
        RangoModelo1 smallint NULL,                     -- 1 = el mejor según el modelo 1 (lo de hoy)
        Candidatos smallint NULL,                       -- candidatos puntuados en esa llamada
        VersionFeatures tinyint NULL,                   -- versión de las features del modelo 1 de la instantánea
        Features varchar(max) NULL,                     -- instantánea «Nombre=valor|…»
        FechaCalculo datetime NOT NULL CONSTRAINT DF_SugerenciasProductosSombra_FechaCalculo DEFAULT (GETDATE()),

        CONSTRAINT PK_SugerenciasProductosSombra PRIMARY KEY CLUSTERED (MostradaId),
        CONSTRAINT FK_SugerenciasProductosSombra_Mostrada FOREIGN KEY (MostradaId) REFERENCES dbo.SugerenciasProductosMostradas (Id)
    );
END
GO

GRANT SELECT, INSERT ON dbo.SugerenciasProductosSombra TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación:
-- SELECT VersionSombra, COUNT(*) AS Filas, AVG(CAST(RangoSombra AS float)) AS RangoMedio, MAX(FechaCalculo) AS Ultima
-- FROM dbo.SugerenciasProductosSombra GROUP BY VersionSombra;
--
-- Medición (cuando haya respuestas): tasa de «Añadido» según lo pondría arriba el modelo 2 o no
-- SELECT CASE WHEN sm.RangoSombra <= 3 THEN 'Top 3 sombra' ELSE 'Fuera' END AS Sombra,
--        COUNT(*) AS Respuestas, SUM(CASE WHEN m.Respuesta = 'Anadido' THEN 1 ELSE 0 END) AS Anadidos
-- FROM dbo.SugerenciasProductosSombra sm JOIN dbo.SugerenciasProductosMostradas m ON m.Id = sm.MostradaId
-- WHERE m.Respuesta IN ('Anadido', 'NoLeInteresa')
-- GROUP BY CASE WHEN sm.RangoSombra <= 3 THEN 'Top 3 sombra' ELSE 'Fuera' END;
