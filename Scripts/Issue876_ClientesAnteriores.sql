/*
    NestoAPI#876 (26/11/26): CLIENTE QUE CAMBIA DE FORMA FISCAL.
    Un cliente que ya lo era pide facturar con otra razón social o NIF y se le da de alta con número nuevo y origen «CAMBIO-FISCAL»
    (OneShot_20261117_OrigenCambioFormaFiscal.sql). Fiscalmente es un cliente nuevo; comercialmente es el mismo.

    dbo.ClientesAnteriores: UNA fila por cliente nuevo con el número que tenía antes (Cliente → ClienteAnterior; PK Empresa+Cliente).
    Una cadena (el anterior del anterior…) la resuelve la API (ClientesEquivalentes); nada fiscal ni contable la usa.
      - La escribe el alta (POST api/Clientes con origen CAMBIO-FISCAL y ClienteAnterior, con su fila en Modificaciones) y, si no
        está y el nuevo tiene origen CAMBIO-FISCAL, la copia de condiciones (POST api/Clientes/CambioFiscal/Copiar) y el paso de
        pedidos (…/PasarPedidos).
      - Sin la tabla: el alta no pide el cliente anterior ni guarda nada (como hoy); Copiar y PasarPedidos contestan 503.

    Fuera del EDMX: la API la lee y escribe por SQL. Lo que copia la API (Clientes, CondPagoClientes, DescuentosProducto,
    OfertasPermitidas, VendedoresClienteGrupoProducto y Modificaciones) ya lo escribía antes: esas tablas no necesitan GRANT nuevo.

    Lanzar como sa en NV. Idempotente: se puede lanzar dos veces, antes o después del deploy. Si la tabla ya existiera con la
    forma provisional de una de las dos versiones de la mañana del 26/11/26 (Id IDENTITY + UNIQUE Empresa+Cliente+ClienteAnterior,
    Usuario varchar(30) NULL), se pasa a la definitiva conservando las filas (de cada cliente, la más reciente).
*/
SET NOCOUNT ON;
USE NV;
GO

-- 1) Forma provisional (con columna Id) → definitiva
IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ClientesAnteriores', N'Id') IS NOT NULL
BEGIN
    BEGIN TRANSACTION;
    EXEC sp_rename N'dbo.ClientesAnteriores', N'ClientesAnteriores_Provisional';
    -- Los nombres de las restricciones son únicos en el esquema: fuera las de la provisional antes de crear la definitiva
    DECLARE @sql nvarchar(max) = N'';
    SELECT @sql = @sql + N'ALTER TABLE dbo.ClientesAnteriores_Provisional DROP CONSTRAINT ' + QUOTENAME(name) + N'; '
    FROM sys.objects
    WHERE parent_object_id = OBJECT_ID(N'dbo.ClientesAnteriores_Provisional') AND type IN ('PK', 'UQ', 'C', 'D');
    EXEC sp_executesql @sql;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ClientesAnteriores_Provisional') AND name = N'IX_ClientesAnteriores_Anterior')
        DROP INDEX IX_ClientesAnteriores_Anterior ON dbo.ClientesAnteriores_Provisional;
    COMMIT TRANSACTION;
    PRINT 'dbo.ClientesAnteriores tenía la forma provisional: renombrada a ClientesAnteriores_Provisional.';
END
GO

-- 2) La tabla definitiva
IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesAnteriores (
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        ClienteAnterior char(10) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_ClientesAnteriores_Fecha DEFAULT (GETDATE()),
        Usuario varchar(50) NOT NULL,
        CONSTRAINT PK_ClientesAnteriores PRIMARY KEY CLUSTERED (Empresa, Cliente),
        CONSTRAINT CK_ClientesAnteriores_NoEsElMismo CHECK (RTRIM(Cliente) <> RTRIM(ClienteAnterior))
    );
    -- «¿Quién vino después de este cliente?» (cortes 3 y 4)
    CREATE NONCLUSTERED INDEX IX_ClientesAnteriores_Anterior ON dbo.ClientesAnteriores (Empresa, ClienteAnterior);
    PRINT 'Creada dbo.ClientesAnteriores.';
END
ELSE
    PRINT 'dbo.ClientesAnteriores ya existía.';
GO

-- 3) Las filas de la provisional (si la había), una por cliente: la más reciente
IF OBJECT_ID(N'dbo.ClientesAnteriores_Provisional', N'U') IS NOT NULL
BEGIN
    BEGIN TRANSACTION;
    EXEC sp_executesql N'
    INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Fecha, Usuario)
    SELECT p.Empresa, p.Cliente, p.ClienteAnterior, p.Fecha, ISNULL(NULLIF(RTRIM(p.Usuario), ''''), ''(desconocido)'')
    FROM (SELECT *, ROW_NUMBER() OVER (PARTITION BY Empresa, Cliente ORDER BY Fecha DESC, Id DESC) AS n
          FROM dbo.ClientesAnteriores_Provisional) p
    WHERE p.n = 1 AND RTRIM(p.Cliente) <> RTRIM(p.ClienteAnterior)
      AND NOT EXISTS (SELECT 1 FROM dbo.ClientesAnteriores a WHERE a.Empresa = p.Empresa AND a.Cliente = p.Cliente);';
    DROP TABLE dbo.ClientesAnteriores_Provisional;
    COMMIT TRANSACTION;
    PRINT 'Filas de la provisional pasadas a dbo.ClientesAnteriores.';
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ClientesAnteriores TO [NUEVAVISION\RDS2016$];
GO

-- 4) El caso del 26/11/26: el 42178 es el 37283 con otra forma fiscal (se dio de alta antes de que existiera el enlace)
IF EXISTS (SELECT 1 FROM Clientes WHERE Empresa = '1' AND [Nº Cliente] = '42178')
   AND EXISTS (SELECT 1 FROM Clientes WHERE Empresa = '1' AND [Nº Cliente] = '37283')
   AND NOT EXISTS (SELECT 1 FROM dbo.ClientesAnteriores WHERE Empresa = '1' AND Cliente = '42178')
    INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Usuario) VALUES ('1', '42178', '37283', 'Issue876');
GO

-- VERIFICACIÓN
SELECT TOP 50 RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(ClienteAnterior) AS ClienteAnterior, Fecha, Usuario
FROM dbo.ClientesAnteriores ORDER BY Fecha DESC;
GO
