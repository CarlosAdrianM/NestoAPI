/*
    NestoAPI#876 (Carlos 26/11/26): cliente que cambia de forma fiscal (otra razón social o NIF) y se da de alta con número nuevo.
    dbo.ClientesAnteriores guarda el enlace «el cliente nuevo es el anterior con otra forma fiscal» (una fila por pareja):

      - La graba el alta (POST api/Clientes con origen CAMBIO-FISCAL y ClienteAnterior) y, si no está y el nuevo tiene origen
        CAMBIO-FISCAL, la copia de condiciones (POST api/Clientes/CambioFiscal/Copiar) y el paso de pedidos (…/PasarPedidos).
      - Sin la tabla, Copiar y PasarPedidos contestan 503 y el alta se hace igual (sin enlace ni copia; la respuesta lo dice).

    Lo que copia la API (Clientes, CondPagoClientes, DescuentosProducto, OfertasPermitidas, VendedoresClienteGrupoProducto y
    Modificaciones) ya lo escribía antes: esas tablas no necesitan GRANT nuevo.

    Fuera del EDMX. Ejecutar en SSMS contra NV como sa. Idempotente: se puede lanzar dos veces, antes o después del deploy.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesAnteriores
    (
        Id int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ClientesAnteriores PRIMARY KEY,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,          -- el nuevo
        ClienteAnterior char(10) NOT NULL,  -- el que cambia de forma fiscal
        Fecha datetime NOT NULL CONSTRAINT DF_ClientesAnteriores_Fecha DEFAULT (GETDATE()),
        Usuario varchar(30) NULL,
        CONSTRAINT UQ_ClientesAnteriores UNIQUE (Empresa, Cliente, ClienteAnterior),
        CONSTRAINT CK_ClientesAnteriores_Distintos CHECK (Cliente <> ClienteAnterior)
    );
    CREATE INDEX IX_ClientesAnteriores_Anterior ON dbo.ClientesAnteriores (Empresa, ClienteAnterior);
END
GO

GRANT SELECT, INSERT ON dbo.ClientesAnteriores TO [NUEVAVISION\RDS2016$];
GO

-- El caso del 26/11/26: el 42178 es el 37283 con otra forma fiscal (se dio de alta antes de que existiera el enlace)
IF EXISTS (SELECT 1 FROM Clientes WHERE Empresa = '1' AND [Nº Cliente] = '42178')
   AND EXISTS (SELECT 1 FROM Clientes WHERE Empresa = '1' AND [Nº Cliente] = '37283')
   AND NOT EXISTS (SELECT 1 FROM dbo.ClientesAnteriores WHERE Empresa = '1' AND Cliente = '42178' AND ClienteAnterior = '37283')
    INSERT INTO dbo.ClientesAnteriores (Empresa, Cliente, ClienteAnterior, Usuario) VALUES ('1', '42178', '37283', 'Issue876');
GO

-- VERIFICACIÓN
SELECT Id, RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(ClienteAnterior) AS ClienteAnterior, Fecha, Usuario
FROM dbo.ClientesAnteriores ORDER BY Id;
GO
