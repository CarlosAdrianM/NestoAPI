/*
    NestoAPI#881, corte 5 (26/11/26): kilómetros por mapa de cada jornada del reparto propio, para el informe de reparto
    (GET api/RepartoPropio/Informe: Km, KmAproximados y ParadasSinPosicion por jornada; Km, JornadasConKm y MediaKm en los totales).

    Los rellena el job nocturno reparto-km-jornadas (todos los días a las 3:50 de Madrid): sede (Algete) → paradas cerradas del día con
    posición (dbo.RepartoParadasTiempos, por la hora real) → sede, con OSRM (el mismo motor que el fin de ruta) o, si no responde, la línea
    recta × 1,3 (Aproximada = 1). Revisa los últimos 7 días: lo que falte, cambie o se calculase sin OSRM.

    Requiere Scripts/Issue881_RepartoFinRutaYTiempos.sql (RepartoParadasTiempos). Fuera del EDMX: sin esta tabla el job no hace nada y el
    informe sale con Km null, como antes.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RepartoJornadasKm', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoJornadasKm (
        Repartidor varchar(50) NOT NULL,                    -- sin dominio, como RepartoParadasTiempos
        Dia date NOT NULL,
        Km decimal(9, 1) NOT NULL,
        Paradas int NOT NULL,                               -- paradas con posición que entran en el recorrido
        ParadasSinPosicion int NOT NULL,
        Motor varchar(15) NOT NULL,                         -- OSRM o Heuristica
        Aproximada bit NOT NULL,
        FechaCalculo datetime NOT NULL,
        CONSTRAINT PK_RepartoJornadasKm PRIMARY KEY CLUSTERED (Repartidor, Dia),
        CONSTRAINT CK_RepartoJornadasKm_Motor CHECK (Motor IN ('OSRM', 'Heuristica')),
        CONSTRAINT CK_RepartoJornadasKm_Cifras CHECK (Km >= 0 AND Paradas >= 0 AND ParadasSinPosicion >= 0)
    );
    PRINT 'RepartoJornadasKm: creada.';
END
ELSE
    PRINT 'RepartoJornadasKm: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RepartoJornadasKm_Dia' AND object_id = OBJECT_ID('dbo.RepartoJornadasKm'))
    CREATE INDEX IX_RepartoJornadasKm_Dia ON dbo.RepartoJornadasKm (Dia) INCLUDE (Km, Aproximada);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.RepartoJornadasKm TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COUNT(*) AS Jornadas, SUM(Km) AS Km FROM dbo.RepartoJornadasKm;
GO
