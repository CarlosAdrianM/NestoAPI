/*
    NestoAPI#799 (03/11/26, Alfredo, pedido 928723): día de reparto de NUESTRA ruta por cliente y contacto, como excepción al
    de su código postal (dbo.CodigosPostalesDiaReparto, NestoAPI#764). «Es una excepción de ruta de miércoles, aunque su zona
    sea de martes». Una fila = ese contacto se sirve ese día (Ruta '01' lunes … '05' viernes); sin fila, el de su CP.

    Fuera del EDMX (como CodigosPostalesDiaReparto): la API la lee por SQL (DiasRepartoClientes, caché de 10 min que el PUT
    invalida) y, si la tabla no existe, todo sigue por el código postal. La escribe PUT api/Clientes/DiaReparto (Dirección,
    Almacén, Administración): Ruta null borra la fila. Cada cambio deja su fila en Modificaciones.

    Lanzar como sa en NV ANTES de publicar la API. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ClientesDiaReparto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesDiaReparto (
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        Ruta char(2) NOT NULL,                  -- 01 lunes … 05 viernes
        Motivo nvarchar(200) NULL,
        Usuario varchar(50) NOT NULL,           -- quien puso la excepción
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ClientesDiaReparto_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_ClientesDiaReparto PRIMARY KEY CLUSTERED (Empresa, Cliente, Contacto),
        CONSTRAINT CK_ClientesDiaReparto_Ruta CHECK (Ruta IN ('01', '02', '03', '04', '05'))
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ClientesDiaReparto TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT Empresa, Cliente, Contacto, Ruta, Motivo, Usuario, FechaModificacion FROM dbo.ClientesDiaReparto ORDER BY Cliente, Contacto;

-- El caso de Alfredo (también se puede hacer desde Nesto, Nesto#606, o con PUT api/Clientes/DiaReparto, que además recalcula
-- las fechas de sus pedidos pendientes):
-- INSERT INTO dbo.ClientesDiaReparto (Empresa, Cliente, Contacto, Ruta, Motivo, Usuario)
-- VALUES ('1', '2077', '3', '03', N'Se sirve los miércoles aunque su zona sea de martes (Alfredo 03/11/26)', 'NUEVAVISION\Carlos');
