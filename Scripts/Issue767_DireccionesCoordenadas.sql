/*
    NestoAPI#767 (Ariadna#68): caché de coordenadas de las direcciones de entrega del reparto propio.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    No está en el EDMX (la API la lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy. Sin el script:
      - GET api/RepartoPropio/Envios sale con Latitud/Longitud null en los envíos y las notas (las reposiciones sí las traen:
        son las de la tienda, fijas en el código);
      - POST api/RepartoPropio/OrdenSugerido funciona, pero solo puede ordenar las reposiciones (lo demás va detrás, sin mover);
      - los jobs de geocodificación no hacen nada.
    Después de lanzarlo, reciclar el app pool (la API recuerda qué tablas existen).

    DireccionesCoordenadas: una fila por dirección normalizada (Clave = SHA-1 en hexadecimal de «DIRECCION|CP|POBLACION» en
    mayúsculas, sin tildes ni signos). La rellenan con Nominatim (OpenStreetMap, 1 petición/s) los jobs de Hangfire
    «GeocodificarParadas» (bajo demanda, cuando la lista del reparto tiene paradas sin coordenadas) y
    «geocodificar-clientes-rutas-propias» (3:40, paradas en reparto + clientes activos de las rutas propias, tope 300/noche).
    Latitud/Longitud NULL con Precision = 'NoEncontrada' es caché negativa: se vuelve a intentar pasados 30 días.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT a la cuenta máquina con la que corre la API (NUEVAVISION\RDS2016$).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.DireccionesCoordenadas') IS NULL
BEGIN
    CREATE TABLE dbo.DireccionesCoordenadas (
        Clave char(40) NOT NULL,                        -- SHA-1 (hex, mayúsculas) de la dirección normalizada
        Direccion nvarchar(200) NULL,                   -- tal como venía (para diagnosticar)
        CodigoPostal nvarchar(20) NULL,
        Poblacion nvarchar(100) NULL,
        Latitud decimal(9, 6) NULL,                     -- WGS84; NULL = no encontrada
        Longitud decimal(9, 6) NULL,
        Precision varchar(12) NOT NULL,                 -- Direccion | CodigoPostal | NoEncontrada
        Fuente varchar(20) NOT NULL,                    -- Nominatim
        Fecha datetime NOT NULL CONSTRAINT DF_DireccionesCoordenadas_Fecha DEFAULT (GETDATE()),

        CONSTRAINT PK_DireccionesCoordenadas PRIMARY KEY CLUSTERED (Clave),
        CONSTRAINT CK_DireccionesCoordenadas_Precision CHECK (Precision IN ('Direccion', 'CodigoPostal', 'NoEncontrada')),
        CONSTRAINT CK_DireccionesCoordenadas_Coordenadas CHECK (
            (Latitud IS NULL AND Longitud IS NULL AND Precision = 'NoEncontrada')
            OR (Latitud BETWEEN -90 AND 90 AND Longitud BETWEEN -180 AND 180 AND Precision <> 'NoEncontrada'))
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DireccionesCoordenadas TO [NUEVAVISION\RDS2016$];
GO

PRINT 'NestoAPI#767: DireccionesCoordenadas lista. Recicle el app pool de la API.';
GO
