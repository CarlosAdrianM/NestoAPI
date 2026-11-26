/*
    NestoAPI#896: probabilidad de devolución de cada recibo de una remesa (modelo ML.NET), EN SOMBRA.

    Qué crea (todo fuera del EDMX):
      - dbo.RemesasModeloEntrenamientos: una fila por entrenamiento del job 'remesas-impagos-modelo' (los domingos a las 4:40 de Madrid,
        cola «entrenamiento») o de POST api/Remesas/ModeloImpagados/Entrenar: ejemplos, métricas de VALIDACIÓN (los últimos 6 meses de
        giros ya resueltos) del modelo y de LA REGLA simple de pesos por tramos (AUC, % de devoluciones recogidas en el 5 % y el 10 % más
        arriesgado, calibración en los tramos altos y los cuatro tramos previsto/real en JSON), la calibración de Platt, versión y
        ModeloActivo (la fila cuyo modelo está en uso).
      - dbo.RemesasPrediccionesImpago: LA SOMBRA. Una fila por efecto de cada remesa que se crea por la API (POST api/Remesas): la
        probabilidad del modelo (NULL si aún no hay modelo guardado), la de LA REGLA, los rasgos (JSON) y las causas; y después su
        resultado (Devuelto 1/0, fecha y motivo), que rellena el job 'remesas-impagos-resultado' (todos los días a las 3:55, cola
        «indicadores») cuando llega el impagado o al cumplirse 45 días del giro.
      - En ParametrosUsuario (empresa '1', '(defecto)') 'Remesas.ModeloImpagadosEnsenar' = '0' si no existe: el interruptor de ENSEÑAR
        la probabilidad en Nesto y NestoApp. Nace APAGADO y hoy no enseña nada aunque se encienda (ese corte está sin hacer): se
        encenderá cuando GET api/Remesas/ModeloImpagados/Comparacion diga que el modelo es fiable.

    ORDEN: puede ir antes o después de publicar la API. Sin este script la API hace EXACTAMENTE lo de hoy: las remesas se crean igual,
    los dos jobs no hacen nada (una línea en ELMAH la primera vez) y los dos endpoints dan 503. Con él, lo primero es entrenar (el
    domingo, o POST api/Remesas/ModeloImpagados/Entrenar): hasta entonces no hay regla ni modelo guardados y la sombra no apunta nada.

    No toca ninguna tabla existente (solo lee ExtractoCliente, Remesas y CCC desde la API). Lanzar como sa en NV. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.RemesasModeloEntrenamientos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemesasModeloEntrenamientos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_RemesasModeloEntrenamientos PRIMARY KEY CLUSTERED,
        Fecha datetime NOT NULL,
        Ejemplos int NOT NULL,                         -- recibos girados con resultado conocido (sin los de los últimos 45 días)
        EjemplosEntrenamiento int NOT NULL,            -- los anteriores a la validación
        EjemplosValidacion int NOT NULL,               -- los de los últimos 6 meses
        ValidacionDesde date NULL,
        DevueltosValidacion int NULL,
        AucModelo float NULL,                          -- área bajo la curva ROC en validación (0,5 = azar; 1 = perfecto)
        AucRegla float NULL,
        Captado5Modelo float NULL,                     -- % de las devoluciones que cae en el 5 % de recibos con más riesgo previsto
        Captado5Regla float NULL,
        Captado10Modelo float NULL,                    -- lo mismo en el 10 %
        Captado10Regla float NULL,
        CalibracionAltosModelo float NULL,             -- en lo previsto >= 15 %: devueltos reales / devueltos previstos (1 = clavado)
        CalibracionAltosRegla float NULL,
        TramosModelo nvarchar(max) NULL,               -- JSON: < 5 %, 5-15 %, 15-30 %, >= 30 % con recibos, devueltos, previsto y real
        TramosRegla nvarchar(max) NULL,
        PlattA float NULL,                             -- calibración del modelo: probabilidad = sigmoide(A x puntuación + B)
        PlattB float NULL,
        Version varchar(20) NULL,
        Algoritmo varchar(100) NULL,
        ModeloActivo bit NOT NULL CONSTRAINT DF_RemesasModeloEntrenamientos_Activo DEFAULT (0),
        Motivo nvarchar(1000) NULL,
        Origen nvarchar(100) NULL,
        Segundos int NULL
    );
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.RemesasModeloEntrenamientos TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.RemesasPrediccionesImpago', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemesasPrediccionesImpago (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_RemesasPrediccionesImpago PRIMARY KEY CLUSTERED,
        Empresa char(3) NOT NULL,
        Remesa int NOT NULL,
        NumOrden int NOT NULL,                         -- [Nº Orden] del efecto (cartera) en ExtractoCliente
        Cliente char(10) NOT NULL,                     -- mismos tipos que ExtractoCliente (para buscar su impagado por índice)
        Contacto char(3) NULL,
        Documento char(10) NOT NULL,
        Efecto char(3) NULL,
        Importe money NOT NULL,
        FechaGiro date NOT NULL,                       -- el día de la remesa
        FechaVto date NULL,
        FechaPrediccion datetime NOT NULL CONSTRAINT DF_RemesasPrediccionesImpago_Fecha DEFAULT (GETDATE()),
        VersionModelo varchar(40) NULL,                -- NULL = ese día no había modelo guardado (solo la regla)
        Probabilidad float NULL,                       -- la del modelo, de 0 a 1
        ProbabilidadRegla float NULL,                  -- la de LA REGLA simple, de 0 a 1
        Rasgos nvarchar(max) NULL,                     -- JSON con los rasgos del recibo ese día
        MotivosRiesgo nvarchar(500) NULL,              -- las dos causas que más suben el riesgo
        Devuelto bit NULL,                             -- NULL = pendiente; 1 = volvió en los 45 días siguientes; 0 = no volvió
        FechaDevolucion date NULL,
        MotivoDevolucion nvarchar(60) NULL,
        ResueltoEl datetime NULL
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_RemesasPrediccionesImpago_Efecto' AND object_id = OBJECT_ID(N'dbo.RemesasPrediccionesImpago'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_RemesasPrediccionesImpago_Efecto ON dbo.RemesasPrediccionesImpago (Empresa, Remesa, NumOrden);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RemesasPrediccionesImpago_FechaGiro' AND object_id = OBJECT_ID(N'dbo.RemesasPrediccionesImpago'))
    CREATE NONCLUSTERED INDEX IX_RemesasPrediccionesImpago_FechaGiro ON dbo.RemesasPrediccionesImpago (FechaGiro) INCLUDE (Probabilidad, ProbabilidadRegla, Devuelto);
GO
GRANT SELECT, INSERT, UPDATE ON dbo.RemesasPrediccionesImpago TO [NUEVAVISION\RDS2016$];
GO
-- Interruptor de ENSEÑAR, apagado. Si la fila ya existe no se toca.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Remesas.ModeloImpagadosEnsenar' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Remesas.ModeloImpagadosEnsenar', '(defecto)', '0', 'Impagos896', GETDATE());
GO
-- Comprobación
SELECT CASE WHEN OBJECT_ID(N'dbo.RemesasModeloEntrenamientos', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.RemesasPrediccionesImpago', N'U') IS NOT NULL
            THEN N'OK: NestoAPI#896 (RemesasModeloEntrenamientos y RemesasPrediccionesImpago)'
            ELSE N'FALTA algo' END AS Resultado;
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'Remesas.ModeloImpagadosEnsenar';
GO
