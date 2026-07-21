-- NestoAPI#327: estado de validación del NIF contra el censo de la AEAT (VNifV2).
--
-- Tabla SATÉLITE: no se toca Clientes (triggers + EDMX Database-First). La fila registra
-- QUÉ NIF y nombre se validaron: si después alguien cambia el NIF o el nombre de la ficha
-- (desde Nesto, el VB6 o SQL directo), la fila deja de casar con la ficha y el estado
-- vuelve a "sin validar" IMPLÍCITAMENTE, sin necesidad de hooks en todos los caminos de
-- modificación. El estado efectivo lo calcula ServicioValidacionNif comparando ambos.
--
-- Estados: CORRECTO / INCORRECTO (ResultadoAeat guarda el literal de Hacienda, que
-- distingue IDENTIFICADO, NO IDENTIFICADO, IDENTIFICADO-BAJA, IDENTIFICADO-REVOCADO...).
-- Sin fila (o fila que no casa) = sin validar.
--
-- Ejecutar en la BD de NestoConnection (NV). GRANT según feedback_scripts_sql_grants_por_bd.

IF OBJECT_ID('dbo.ValidacionesNif') IS NULL
BEGIN
    CREATE TABLE dbo.ValidacionesNif (
        Empresa         char(3)      NOT NULL,
        Cliente         char(10)     NOT NULL,
        Contacto        char(3)      NOT NULL,
        Nif             varchar(20)  NOT NULL,  -- NIF de la ficha en el momento de validar
        Nombre          varchar(50)  NOT NULL,  -- Nombre de la ficha en el momento de validar
        Estado          varchar(10)  NOT NULL,  -- CORRECTO / INCORRECTO
        ResultadoAeat   varchar(100) NULL,      -- literal devuelto por VNifV2
        FechaValidacion datetime     NOT NULL CONSTRAINT DF_ValidacionesNif_Fecha DEFAULT GETDATE(),
        Usuario         varchar(50)  NULL,
        CONSTRAINT PK_ValidacionesNif PRIMARY KEY (Empresa, Cliente, Contacto)
    );

    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ValidacionesNif TO [NUEVAVISION\RDS2016$];
END
GO
