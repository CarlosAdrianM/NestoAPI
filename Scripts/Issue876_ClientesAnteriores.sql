/*
    NestoAPI#876 corte 1 (26/11/26): CLIENTE QUE CAMBIA DE FORMA FISCAL.
    Un cliente que ya lo era pide facturar con otra razón social o NIF y se le da de alta con número nuevo y origen «CAMBIO-FISCAL»
    (OneShot_20261117_OrigenCambioFormaFiscal.sql). Fiscalmente es un cliente nuevo; comercialmente es el mismo.
    - dbo.ClientesAnteriores: una fila por cliente nuevo con el número que tenía antes (Cliente → ClienteAnterior). Una cadena
      (el anterior del anterior…) la resuelve la API (ClientesEquivalentes); nada fiscal ni contable la usa.

    Fuera del EDMX: la API la lee y escribe por SQL. Sin la tabla, el alta no pide el cliente anterior ni guarda nada (como hoy).
    La escribe POST api/Clientes (campo ClienteAnterior con origen CAMBIO-FISCAL), con su fila en Modificaciones.

    Lanzar como sa en NV. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
GO
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
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ClientesAnteriores TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT TOP 50 Empresa, Cliente, ClienteAnterior, Fecha, Usuario FROM dbo.ClientesAnteriores ORDER BY Fecha DESC;
