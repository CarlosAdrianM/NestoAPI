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

    CORTE 3 — tiempos reales y modelo en sombra:
      - dbo.RepartoParadasTiempos: una fila por parada cerrada (entrega, incidencia, recogida, cobro, reposición) con lo necesario para
        medir lo que se tarda de verdad (horas Ir/llegada/fin, posición, la parada anterior del mismo repartidor ese día y su posición,
        los MinutosPrevistos de la regla). Idempotente por la clave de la entrega. Solo inserciones.
      - dbo.RepartoEstimaciones: la foto de cada previsión de fin de ruta (como mucho una por repartidor cada 10 minutos).
      - dbo.RepartoTiemposModeloEntrenamientos: una fila por entrenamiento nocturno del modelo en sombra (job 'reparto-tiempos-parada-modelo',
        4:25), también «sin datos suficientes»: error medio de la regla y del modelo y veredicto. GET api/RepartoPropio/TiemposParada/Comparacion.
      - dbo.RepartoJornadas: Latitud y Longitud (dónde se termina la jornada).
      - ParametrosUsuario «(defecto)» Reparto.TiempoParadaQuienManda = 'Regla' (con 'Modelo', el fin de ruta usa el modelo si lo hay).
      Sin esto: las entregas, incidencias, cargas y jornadas van como hoy (no se guarda nada nuevo; ELMAH una vez) y el job no entrena.

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

-- ==================================================================== corte 3: tiempos reales, fotos y modelo en sombra

IF OBJECT_ID('dbo.RepartoParadasTiempos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoParadasTiempos (
        Id bigint IDENTITY(1,1) NOT NULL,
        ClaveIdempotencia uniqueidentifier NOT NULL,         -- la de la entrega / incidencia / recogida
        Repartidor varchar(50) NOT NULL,                     -- sin dominio
        Dia date NOT NULL,
        TipoParada varchar(20) NOT NULL,                     -- Envio, NotaEntrega, NotaRecogida, NotaCobro, Reposicion, RecogidaReposicion
        Resultado varchar(12) NOT NULL,                      -- Entregada, Incidencia, Recogida
        EnvioId int NULL,                                    -- negativo en las de reposición
        Pedido int NULL,
        Traspaso int NULL,
        Empresa char(3) NULL,
        Cliente varchar(10) NULL,
        Contacto varchar(3) NULL,
        CodigoPostal varchar(10) NULL,
        Bultos int NULL,
        FormaCobro varchar(20) NULL,                         -- Efectivo, Enlace, Tarjeta, NoCobrado (null sin cobro)
        ImporteCobro decimal(12, 2) NULL,
        TipoServicioTecnicoId int NULL,
        ServicioTecnico nvarchar(50) NULL,                   -- el nombre del tipo en ese momento
        MinutosServicioTecleados int NULL,
        FechaHoraIr datetime NULL,                           -- cuando pulsó «Ir» hacia ella (PDA)
        FechaHoraLlegada datetime NULL,                      -- primera apertura o lectura de la parada (PDA)
        FechaHoraFin datetime NOT NULL,                      -- la de la entrega (PDA)
        Latitud decimal(9, 6) NULL,
        Longitud decimal(9, 6) NULL,
        FechaHoraAnterior datetime NULL,                     -- el fin de la parada anterior del mismo repartidor ese día
        LatitudAnterior decimal(9, 6) NULL,
        LongitudAnterior decimal(9, 6) NULL,
        MinutosPrevistos decimal(6, 1) NULL,                 -- los que daba la regla (MinutosParadaReparto)
        FechaRegistro datetime NOT NULL CONSTRAINT DF_RepartoParadasTiempos_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_RepartoParadasTiempos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_RepartoParadasTiempos_Clave UNIQUE (ClaveIdempotencia)
    );
    CREATE INDEX IX_RepartoParadasTiempos_Repartidor ON dbo.RepartoParadasTiempos (Repartidor, Dia, FechaHoraFin) INCLUDE (Latitud, Longitud);
    CREATE INDEX IX_RepartoParadasTiempos_Dia ON dbo.RepartoParadasTiempos (Dia);
    PRINT 'RepartoParadasTiempos: creada.';
END
ELSE
    PRINT 'RepartoParadasTiempos: ya existía.';
GO

IF OBJECT_ID('dbo.RepartoEstimaciones', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoEstimaciones (
        Id int IDENTITY(1,1) NOT NULL,
        Repartidor varchar(50) NOT NULL,
        FechaAlta datetime NOT NULL,                         -- la primera de su ventana de 10 minutos
        FechaHora datetime NOT NULL,                         -- la última que la actualizó
        Dia date NOT NULL,
        HoraSalida datetime NOT NULL,
        NumeroParadas int NOT NULL,
        MinutosConduccion int NOT NULL,                      -- con colchón
        MinutosParadas int NOT NULL,
        MinutosDescarga int NOT NULL,
        Km decimal(8, 1) NOT NULL,
        FinEstimado datetime NOT NULL,
        HoraCierre datetime NULL,
        PasaDelCierre bit NOT NULL,
        Aproximada bit NOT NULL,
        Motor varchar(20) NULL,                              -- OSRM o Heuristica
        OrigenRuta varchar(20) NULL,                         -- Posicion o Almacen
        Destino varchar(20) NULL,                            -- Almacen o Personalizado
        Calculo varchar(20) NULL,                            -- Estimacion u OrdenSugerido
        MinutosDe varchar(10) NULL,                          -- Regla o Modelo
        Actualizaciones int NOT NULL CONSTRAINT DF_RepartoEstimaciones_Actualizaciones DEFAULT (0),
        CONSTRAINT PK_RepartoEstimaciones PRIMARY KEY CLUSTERED (Id)
    );
    CREATE INDEX IX_RepartoEstimaciones_Repartidor ON dbo.RepartoEstimaciones (Repartidor, FechaAlta);
    PRINT 'RepartoEstimaciones: creada.';
END
ELSE
    PRINT 'RepartoEstimaciones: ya existía.';
GO

IF OBJECT_ID('dbo.RepartoTiemposModeloEntrenamientos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RepartoTiemposModeloEntrenamientos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RepartoTiemposModeloEntrenamientos PRIMARY KEY CLUSTERED,
        Fecha datetime NOT NULL,
        Ejemplos int NOT NULL,                               -- paradas válidas (con anterior el mismo día, intervalo de 0 a 120 min)
        EjemplosEntrenamiento int NOT NULL,
        EjemplosValidacion int NOT NULL,                     -- las de las últimas 4 semanas
        ValidacionDesde date NULL,
        ErrorMedioRegla decimal(9, 2) NULL,                  -- minutos: |real - regla| de media
        ErrorMedioModelo decimal(9, 2) NULL,
        EjemplosConLlegada int NOT NULL,                     -- las de validación con FechaHoraLlegada (segundo objetivo: fin - llegada)
        ErrorMedioReglaLlegada decimal(9, 2) NULL,
        ErrorMedioModeloLlegada decimal(9, 2) NULL,
        Veredicto nvarchar(400) NULL,
        ModeloActivo bit NOT NULL,                           -- su modelo es el guardado (una fila a la vez)
        Motivo nvarchar(500) NULL,
        Origen nvarchar(100) NULL,
        Version varchar(20) NULL,
        Segundos int NULL
    );
    PRINT 'RepartoTiemposModeloEntrenamientos: creada.';
END
ELSE
    PRINT 'RepartoTiemposModeloEntrenamientos: ya existía.';
GO

IF OBJECT_ID('dbo.RepartoJornadas', 'U') IS NOT NULL AND COL_LENGTH('dbo.RepartoJornadas', 'Latitud') IS NULL
BEGIN
    ALTER TABLE dbo.RepartoJornadas ADD Latitud decimal(9, 6) NULL;
    PRINT 'RepartoJornadas.Latitud: añadida.';
END
GO

IF OBJECT_ID('dbo.RepartoJornadas', 'U') IS NOT NULL AND COL_LENGTH('dbo.RepartoJornadas', 'Longitud') IS NULL
BEGIN
    ALTER TABLE dbo.RepartoJornadas ADD Longitud decimal(9, 6) NULL;
    PRINT 'RepartoJornadas.Longitud: añadida.';
END
GO

GRANT SELECT, INSERT ON dbo.RepartoParadasTiempos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE ON dbo.RepartoEstimaciones TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE ON dbo.RepartoTiemposModeloEntrenamientos TO [NUEVAVISION\RDS2016$];
GO

-- Quién da los minutos de las paradas en el fin de ruta: 'Regla' (las constantes). Con 'Modelo', el modelo en sombra si hay uno guardado.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'Reparto.TiempoParadaQuienManda')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Reparto.TiempoParadaQuienManda', '(defecto)', 'Regla', 'Issue881', GETDATE());
GO

SELECT name FROM sys.tables WHERE name IN ('RepartoTiposServicioTecnico', 'RepartoParadasTiempos', 'RepartoEstimaciones', 'RepartoTiemposModeloEntrenamientos');
SELECT COL_LENGTH('dbo.NotasEntregaCabecera', 'TipoServicioTecnicoId') AS TipoServicioTecnicoId,
       COL_LENGTH('dbo.NotasEntregaCabecera', 'MinutosServicio') AS MinutosServicio;
