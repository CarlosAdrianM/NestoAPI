/*
    Eventos: candidatos a invitar, en MODO SOMBRA (corte 1, Carlos 11/11/26). NestoAPI#850.
    Cuesta llenar los eventos de las tiendas y a los clientes sin vendedor (NV) no se los ofrece nadie. Hoy NO hay datos para entrenar
    (6 eventos, 35 inscripciones): la API solo RECOGE datos, sin enseñar nada a nadie ni pedir trabajo a nadie.

    1. dbo.EventosCandidatos: por evento, cliente y día de cálculo, una foto de las CARACTERÍSTICAS del cliente en ese momento y la
       puntuación por reglas (con su versión). EsControl = 1: muestra aleatoria de clientes NV que NO pasan el corte (para que el
       conjunto de entrenamiento no esté sesgado). El resultado (se inscribió / asistió) NO se copia: lo da la vista del punto 3.
       La escribe la API (al crear o anunciar un evento y el job «eventos-candidatos-sombra» de madrugada); idempotente por
       evento + cliente + día (la del día se sustituye).
    2. dbo.CentrosCodigosPostales: el centro (latitud/longitud) de cada código postal de España, para la distancia en línea recta a
       la tienda. Se carga con Scripts/CentrosCodigosPostales_GeoNames.ps1 (GeoNames, CC-BY 4.0). Sin filas, la distancia queda NULL
       (salvo los clientes con su dirección geocodificada en dbo.DireccionesCoordenadas, la caché del reparto).
    3. dbo.vEventosCandidatosResultado: cada foto con lo que pasó después (inscripción, estado, asistencia). Es el conjunto de
       entrenamiento futuro (cortes 2-4: calibrar, lista corta para WhatsApp, correos por Odoo, ML.NET).

    Lanzar como sa en NV. Puede ir ANTES o DESPUÉS de publicar la API (sin las tablas, la API no calcula nada: «sin tabla = sin
    función»). Idempotente.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.EventosCandidatos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventosCandidatos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_EventosCandidatos PRIMARY KEY CLUSTERED,
        EventoId int NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        FechaCalculo date NOT NULL,                       -- el día de la foto (una por evento, cliente y día)
        FechaHora datetime NOT NULL CONSTRAINT DF_EventosCandidatos_FechaHora DEFAULT (GETDATE()),
        Motivo varchar(20) NOT NULL,                      -- Alta, Anuncio, Diario, Manual
        EsControl bit NOT NULL,                           -- 1 = muestra aleatoria de los que NO pasan el corte
        VersionRegla varchar(20) NOT NULL,
        Puntuacion decimal(9,4) NOT NULL,
        DiasHastaEvento int NULL,
        -- Características (la foto)
        DistanciaKm decimal(9,2) NULL,
        FuenteDistancia varchar(20) NULL,                 -- Direccion (DireccionesCoordenadas) o CodigoPostal (CentrosCodigosPostales)
        CodigoPostal varchar(10) NULL,
        TieneMovil bit NOT NULL,
        TieneCorreo bit NOT NULL,
        AntiguedadDias int NULL,
        ComproRequerido bit NOT NULL,                     -- alguno de EventosProductosRequeridos, alguna vez
        DiasUltimaCompraRequerido int NULL,
        ImporteRequeridos24m decimal(12,2) NOT NULL,
        ImporteMarca24m decimal(12,2) NOT NULL,           -- misma familia (marca) que los requeridos, no el subgrupo
        LineasMarca24m int NOT NULL,
        DiasUltimaCompraMarca int NULL,
        InscripcionesPrevias int NOT NULL,                -- otros eventos, sin las anuladas ni caducadas
        AsistenciasPrevias int NOT NULL,                  -- otros eventos con estado Asistio
        CursosComprados int NOT NULL,                     -- líneas de grupo CUR, alguna vez
        DiasUltimoCurso int NULL,
        Importe12m decimal(12,2) NOT NULL,
        Importe24m decimal(12,2) NOT NULL,
        Pedidos24m int NOT NULL,
        DiasUltimaCompra int NULL,
        ImporteTienda24m decimal(12,2) NOT NULL,          -- líneas del almacén del evento
        Contactos int NOT NULL
    );
    CREATE UNIQUE INDEX UX_EventosCandidatos_EventoClienteDia ON dbo.EventosCandidatos (EventoId, Empresa, Cliente, FechaCalculo);
    CREATE INDEX IX_EventosCandidatos_EventoDia ON dbo.EventosCandidatos (EventoId, FechaCalculo) INCLUDE (EsControl, Puntuacion);
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosCandidatos TO [NUEVAVISION\RDS2016$];
GO

IF OBJECT_ID(N'dbo.CentrosCodigosPostales', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CentrosCodigosPostales (
        CodigoPostal char(5) NOT NULL CONSTRAINT PK_CentrosCodigosPostales PRIMARY KEY CLUSTERED,
        Latitud decimal(9,6) NOT NULL,
        Longitud decimal(9,6) NOT NULL,
        Poblacion nvarchar(180) NULL,
        Fuente varchar(30) NOT NULL,                      -- GeoNames (CC-BY 4.0, https://www.geonames.org)
        Fecha datetime NOT NULL CONSTRAINT DF_CentrosCodigosPostales_Fecha DEFAULT (GETDATE())
    );
END
GO
GRANT SELECT ON dbo.CentrosCodigosPostales TO [NUEVAVISION\RDS2016$];
GO

CREATE OR ALTER VIEW dbo.vEventosCandidatosResultado
AS
/* Cada foto de candidato con lo que pasó después: es el conjunto de entrenamiento (corte 2 en adelante). */
SELECT c.*,
       e.Fecha AS FechaEvento,
       e.Almacen,
       e.Formato,
       i.Id AS InscripcionId,
       i.FechaCreacion AS FechaInscripcion,
       RTRIM(i.Estado) AS EstadoInscripcion,
       CAST(CASE WHEN i.Id IS NOT NULL THEN 1 ELSE 0 END AS bit) AS SeInscribio,
       CAST(CASE WHEN i.Id IS NOT NULL AND i.FechaCreacion >= c.FechaHora THEN 1 ELSE 0 END AS bit) AS SeInscribioDespues,
       CAST(CASE WHEN RTRIM(i.Estado) = 'Asistio' THEN 1 ELSE 0 END AS bit) AS Asistio,
       CAST(CASE WHEN e.Fecha < CAST(GETDATE() AS date) THEN 1 ELSE 0 END AS bit) AS EventoPasado
FROM dbo.EventosCandidatos c
INNER JOIN dbo.Eventos e ON e.Id = c.EventoId
OUTER APPLY (SELECT TOP 1 ins.Id, ins.FechaCreacion, ins.Estado
             FROM dbo.EventosInscripciones ins
             WHERE ins.EventoId = c.EventoId AND ins.Empresa = c.Empresa AND ins.Cliente = c.Cliente
               AND ins.Estado NOT IN ('Anulada', 'Caducada')
             ORDER BY ins.FechaCreacion) i;
GO
GRANT SELECT ON dbo.vEventosCandidatosResultado TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT 'EventosCandidatos' AS Tabla, COUNT(*) AS Filas FROM dbo.EventosCandidatos
UNION ALL SELECT 'CentrosCodigosPostales', COUNT(*) FROM dbo.CentrosCodigosPostales;
GO
