/*
    NestoAPI#603 (corte 1): registro de las sugerencias de contacto de Rapports.

    Ejecutar en SSMS contra NV como sa (crea una tabla: el login nuevavision no tiene ALTER). Idempotente: se puede
    lanzar dos veces. Lanzarlo ANTES de publicar la API: la tabla está en el EDMX y, sin ella, GET api/Clientes/SugerenciasContacto
    falla (y el POST de rapports registraría en ELMAH que no puede marcar la sugerencia como atendida).

    - SugerenciasContacto: una fila por cliente sugerido a un vendedor en un día. La primera consulta del día del vendedor
      inserta las N sugerencias (20 por defecto); las siguientes del mismo día no duplican. Atendida = 1 (con RapportId y
      FechaAtendida) cuando se crea un rapport de ese cliente ese mismo día. Sirve para saber quién usa la lista y si las
      sugerencias venden más que las llamadas libres (GET api/Clientes/SugerenciasContacto/Uso, corte 5 de la issue).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.SugerenciasContacto') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasContacto (
        Id int IDENTITY(1, 1) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_SugerenciasContacto_Fecha DEFAULT (GETDATE()),   -- cuándo se sugirió (primera consulta del día)
        Vendedor char(3) NOT NULL,                      -- Vendedores.Número de la cartera consultada
        Usuario varchar(30) NOT NULL,                   -- usuario del Identity que abrió la lista
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        Prioridad varchar(10) NOT NULL,                 -- Máxima, Alta, Media o Baja
        Orden int NOT NULL,                             -- 1, 2, 3… dentro del día y el vendedor
        Probabilidad real NOT NULL,                     -- la del modelo en ese momento (0 a 1)
        Motivo nvarchar(200) NOT NULL,
        Atendida bit NOT NULL CONSTRAINT DF_SugerenciasContacto_Atendida DEFAULT (0),
        RapportId int NULL,                             -- SeguimientoCliente.[NºOrden] que la atendió
        FechaAtendida datetime NULL,
        CONSTRAINT PK_SugerenciasContacto PRIMARY KEY (Id),
        CONSTRAINT CK_SugerenciasContacto_Prioridad CHECK (Prioridad IN ('Máxima', 'Alta', 'Media', 'Baja'))
    );
    CREATE INDEX IX_SugerenciasContacto_Vendedor_Fecha ON dbo.SugerenciasContacto (Vendedor, Fecha);
    CREATE INDEX IX_SugerenciasContacto_Cliente_Fecha ON dbo.SugerenciasContacto (Cliente, Contacto, Fecha);
END
GO

GRANT SELECT, INSERT, UPDATE ON dbo.SugerenciasContacto TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: la tabla, vacía la primera vez
------------------------------------------------------------------------------------------------
SELECT 'SugerenciasContacto' AS Tabla, COUNT(*) AS Filas FROM dbo.SugerenciasContacto;
GO
