/*
    NestoAPI#881 (Carlos, 19/11/26): fin previsto de ruta del reparto propio, tipos de servicio técnico y tiempos reales.

    El corte 1 (fin previsto: POST api/RepartoPropio/Estimacion y FinRuta en OrdenSugerido) NO necesita este script: sus constantes
    viven en el código y se pueden cambiar con filas de ParametrosUsuario «(defecto)» (Reparto.MinutosParadaEnvio, …) sin crearlas aquí.

    CORTE 2 — servicio técnico de las notas de entrega y de recogida:
      - dbo.RepartoTiposServicioTecnico: los tipos del combo de Nesto (empieza VACÍA; Alfredo y Santiago la amplían escribiendo uno nuevo).
        MinutosEstimados NULL = los de por defecto (60, parámetro Reparto.MinutosServicioTecnico).
      - dbo.NotasEntregaCabecera: TipoServicioTecnicoId y MinutosServicio (tecleados, opcionales).
      Sin esto: GET api/RepartoPropio/TiposServicioTecnico da la lista vacía, POST y PUT …/ServicioTecnico dan 503 (ELMAH una vez), las
      paradas no traen ServicioTecnico y el fin de ruta va sin servicio técnico. Nada más cambia.

    Fuera del EDMX: la API comprueba que existen (OBJECT_ID / COL_LENGTH). Puede ir antes o después de publicar la API.
    Ejecutar en SSMS contra NV como sa (CREATE, ALTER y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas. Sin CHECK de valores cerrados.
*/

SET NOCOUNT ON;
USE NV;
GO

-- ==================================================================== corte 2: tipos de servicio técnico

IF OBJECT_ID('dbo.RepartoTiposServicioTecnico', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoTiposServicioTecnico (
        Id int IDENTITY(1,1) NOT NULL,
        Nombre nvarchar(50) NOT NULL,                       -- «Instalación de un sillón» (único, sin mayúsculas: la intercalación de la BD)
        MinutosEstimados int NULL,                          -- NULL = los de por defecto (60)
        Activo bit NOT NULL CONSTRAINT DF_RepartoTiposServicioTecnico_Activo DEFAULT (1),
        Usuario varchar(50) NOT NULL,
        FechaAlta datetime NOT NULL CONSTRAINT DF_RepartoTiposServicioTecnico_FechaAlta DEFAULT (GETDATE()),
        CONSTRAINT PK_RepartoTiposServicioTecnico PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoTiposServicioTecnico_Nombre UNIQUE (Nombre)
    );
    PRINT 'RepartoTiposServicioTecnico: creada (vacía).';
END
ELSE
    PRINT 'RepartoTiposServicioTecnico: ya existía.';
GO

-- Cada columna nueva en su propio lote (si no, el ALTER y su uso no compilan juntos).
IF COL_LENGTH('dbo.NotasEntregaCabecera', 'TipoServicioTecnicoId') IS NULL
BEGIN
    ALTER TABLE dbo.NotasEntregaCabecera ADD TipoServicioTecnicoId int NULL;
    PRINT 'NotasEntregaCabecera.TipoServicioTecnicoId: añadida.';
END
ELSE
    PRINT 'NotasEntregaCabecera.TipoServicioTecnicoId: ya existía.';
GO

IF COL_LENGTH('dbo.NotasEntregaCabecera', 'MinutosServicio') IS NULL
BEGIN
    ALTER TABLE dbo.NotasEntregaCabecera ADD MinutosServicio smallint NULL;   -- tecleados (1..600), opcional
    PRINT 'NotasEntregaCabecera.MinutosServicio: añadida.';
END
ELSE
    PRINT 'NotasEntregaCabecera.MinutosServicio: ya existía.';
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina.
GRANT SELECT, INSERT, UPDATE ON dbo.RepartoTiposServicioTecnico TO [NUEVAVISION\RDS2016$];
GO

SELECT name FROM sys.tables WHERE name IN ('RepartoTiposServicioTecnico');
SELECT COL_LENGTH('dbo.NotasEntregaCabecera', 'TipoServicioTecnicoId') AS TipoServicioTecnicoId,
       COL_LENGTH('dbo.NotasEntregaCabecera', 'MinutosServicio') AS MinutosServicio;
