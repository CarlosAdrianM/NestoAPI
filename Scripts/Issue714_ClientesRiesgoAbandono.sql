/*
    NestoAPI#714 (corte 1): tabla ClientesRiesgoAbandono.

    Lo que deja cada noche el job «riesgo-abandono-diario» (L-S 05:20) sobre la cartera de llamadas de cada vendedor activo, con
    la instantánea del día anterior (datos completos). Dos tipos de fila:
      Alerta       el primer día que la regla marca a un comprador regular sin tener ya una alerta sin resolver (una por cliente
                   hasta que vence su horizonte: lo que vería el vendedor cuando esto esté en Activo).
      Instantanea  los lunes, TODOS los compradores regulares de la cartera (marcados o no) con sus features: negativos y
                   positivos para entrenar el modelo de ML.NET del corte 2.

    Comprador regular:  4 o más días con compra en los 12 meses anteriores y última compra hace H días o menos.
    Cadencia:           mediana de los días entre compras en 12 meses (mínimo 7).
    Horizonte (H):      clamp(3 × cadencia, 60, 180). FechaLimite = FechaInstantanea + H.
    Baseline:           DiasSinComprar > 1,5 × Cadencia (regla «baseline-1»; VersionModelo lo dice). Probabilidad la pondrá el modelo (corte 3).
    Motivo:             «Suele pedir cada 21 días y lleva 52 sin pedir».
    Features:           JSON (CalculadoraAbandono.FeaturesAbandono, «features-1») con recencia, frecuencia, importe, grupos,
                        devoluciones, impagos y vencidos, rapports, cambio de vendedor y perfil, todo a fecha de la instantánea.
    Resolución:         el job «riesgo-abandono-resolver» (L-S 05:10) etiqueta las filas con FechaLimite ya vencida:
                        Baja (cambió a 9/99/-1/11/22/93/95 en SeguimientoCambiosEstadoCliente dentro del horizonte; manda sobre
                        Compro), Compro (algún pedido del cliente dentro del horizonte), Abandono (ni lo uno ni lo otro) o
                        Desconocido (la ficha ya no existe). FechaEvento: la del hecho que decide (baja o primera compra).
    Avisado y AtendidoRapportId quedan para el corte 4 (campana y llamadas sugeridas); Modo guarda con qué interruptor se escribió.

    Informe: GET api/Clientes/RiesgoAbandono/Informe?desde&hasta (Dirección e Informática): precisión de la regla por vendedor y mes.

    No está en el EDMX (la API la lee y escribe con SQL a mano, como PedidosPromesaRiesgo #693): se puede lanzar antes o después
    del deploy. Sin ella la API no registra nada (y el informe lo dice). Interruptor Abandono:Modo en Web.config (nace en Sombra).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE (la resolución y, en el corte 4, Avisado/AtendidoRapportId). Sin DELETE.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ClientesRiesgoAbandono', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesRiesgoAbandono (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente varchar(10) NOT NULL,
        Contacto varchar(3) NOT NULL,
        Vendedor varchar(10) NOT NULL,                  -- de cuya cartera de llamadas sale (ficha o grupo de producto)
        FechaInstantanea date NOT NULL,                 -- el día d (el anterior a la ejecución del job)
        Tipo varchar(12) NOT NULL,                      -- Alerta / Instantanea
        Probabilidad decimal(5, 4) NULL,                -- del modelo (corte 3)
        Baseline bit NOT NULL,                          -- la regla: DiasSinComprar > 1,5 × Cadencia
        Cadencia int NOT NULL,
        DiasSinComprar int NOT NULL,
        Horizonte int NOT NULL,
        FechaLimite date NOT NULL,                      -- FechaInstantanea + Horizonte
        Motivo nvarchar(300) NULL,
        Features nvarchar(max) NULL,                    -- JSON, versión dentro
        VersionModelo varchar(30) NULL,                 -- baseline-1 (corte 1) / versión del zip (corte 3)
        Modo varchar(10) NULL,                          -- Sombra / Activo
        Creado datetime NOT NULL CONSTRAINT DF_ClientesRiesgoAbandono_Creado DEFAULT (GETDATE()),
        Avisado datetime NULL,                          -- corte 4
        AtendidoRapportId int NULL,                     -- corte 4
        Resuelto datetime NULL,
        Resultado varchar(12) NULL,                     -- Compro / Abandono / Baja / Desconocido
        FechaEvento date NULL,                          -- la de la baja o de la primera compra que decide el resultado

        CONSTRAINT PK_ClientesRiesgoAbandono PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_ClientesRiesgoAbandono_Tipo CHECK (Tipo IN ('Alerta', 'Instantanea')),
        CONSTRAINT CK_ClientesRiesgoAbandono_Resultado CHECK (Resultado IS NULL OR Resultado IN ('Compro', 'Abandono', 'Baja', 'Desconocido'))
    );
END
GO

-- Una fila por cliente, vendedor, día y tipo: la pasada es idempotente aunque se lance dos veces el mismo día (la segunda falla
-- la inserción de las repetidas y lo dice ELMAH; nada se duplica).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ClientesRiesgoAbandono_Instantanea' AND object_id = OBJECT_ID('dbo.ClientesRiesgoAbandono'))
    CREATE UNIQUE INDEX UX_ClientesRiesgoAbandono_Instantanea ON dbo.ClientesRiesgoAbandono (Empresa, Cliente, Contacto, Vendedor, FechaInstantanea, Tipo);
GO

-- Para las alertas abiertas de un vendedor y la resolución de las vencidas. Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientesRiesgoAbandono_Resuelto_FechaLimite' AND object_id = OBJECT_ID('dbo.ClientesRiesgoAbandono'))
    CREATE INDEX IX_ClientesRiesgoAbandono_Resuelto_FechaLimite ON dbo.ClientesRiesgoAbandono (Resuelto, FechaLimite)
        INCLUDE (Empresa, Cliente, Contacto, Vendedor, Tipo, FechaInstantanea);
GO

-- Para el informe (por fecha de la instantánea).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClientesRiesgoAbandono_FechaInstantanea' AND object_id = OBJECT_ID('dbo.ClientesRiesgoAbandono'))
    CREATE INDEX IX_ClientesRiesgoAbandono_FechaInstantanea ON dbo.ClientesRiesgoAbandono (FechaInstantanea)
        INCLUDE (Vendedor, Tipo, Baseline, Resuelto, Resultado);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.ClientesRiesgoAbandono TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.ClientesRiesgoAbandono', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.ClientesRiesgoAbandono) AS Filas;
GO

-- Las alertas abiertas de hoy, por vendedor:
-- SELECT Vendedor, COUNT(*) Abiertas FROM dbo.ClientesRiesgoAbandono WHERE Tipo = 'Alerta' AND Resuelto IS NULL GROUP BY Vendedor ORDER BY Vendedor;
-- Lo que dejó la última pasada:
-- SELECT TOP 50 FechaInstantanea, Tipo, Vendedor, Cliente, Contacto, Cadencia, DiasSinComprar, Horizonte, FechaLimite, Motivo
-- FROM dbo.ClientesRiesgoAbandono ORDER BY Id DESC;
