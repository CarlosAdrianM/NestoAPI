/*
    NestoAPI#623 (05/11/26, campaña de Meta de Harmonia): ORIGEN DEL CLIENTE.
    - dbo.OrigenesCliente: catálogo de orígenes (uno por campaña o canal). HorasParaLlamar = plazo para la primera llamada,
      contado en días laborables (de lunes a viernes no festivos; ver ReglasLeadsCampana.PlazoLlamadaLead en la API).
    - dbo.ClientesOrigen: el origen de cada CLIENTE (no por contacto; decisión de Carlos 05/11/26). FechaOrigen = cuando se le
      puso; FechaAvisoRetraso = cuándo avisó el job «leads-sin-llamar» de que pasó el plazo sin rapport (para no repetir).

    Fuera del EDMX (como ClientesDiaReparto): la API las lee por SQL y, si no existen, no hay orígenes (el alta de clientes
    sigue igual). Las escriben PUT api/Clientes/{empresa}/{cliente}/Origen y el alta/modificación de clientes (campo Origen).

    Lanzar como sa en NV ANTES de publicar la API. Idempotente.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.OrigenesCliente', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrigenesCliente (
        Codigo varchar(30) NOT NULL,
        Descripcion nvarchar(100) NOT NULL,
        Campana nvarchar(100) NULL,
        FechaDesde date NULL,
        FechaHasta date NULL,
        Activo bit NOT NULL CONSTRAINT DF_OrigenesCliente_Activo DEFAULT (1),
        HorasParaLlamar int NOT NULL CONSTRAINT DF_OrigenesCliente_HorasParaLlamar DEFAULT (24),
        Usuario varchar(50) NOT NULL CONSTRAINT DF_OrigenesCliente_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_OrigenesCliente_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_OrigenesCliente PRIMARY KEY CLUSTERED (Codigo),
        CONSTRAINT CK_OrigenesCliente_HorasParaLlamar CHECK (HorasParaLlamar > 0)
    );
END
GO
-- 05/11/26: dbo.OrigenesCliente YA EXISTÍA en producción (creada el 27/10/26 por Issue716, con EtiquetaMailchimp y sin HorasParaLlamar):
-- se añade la columna que usa la API si falta (idempotente).
IF COL_LENGTH(N'dbo.OrigenesCliente', N'HorasParaLlamar') IS NULL
BEGIN
    ALTER TABLE dbo.OrigenesCliente ADD HorasParaLlamar int NOT NULL CONSTRAINT DF_OrigenesCliente_HorasParaLlamar DEFAULT (24);
    ALTER TABLE dbo.OrigenesCliente ADD CONSTRAINT CK_OrigenesCliente_HorasParaLlamar CHECK (HorasParaLlamar > 0);
    PRINT 'OrigenesCliente: añadida HorasParaLlamar (24).';
END
GO
IF OBJECT_ID(N'dbo.ClientesOrigen', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesOrigen (
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Origen varchar(30) NOT NULL,
        FechaOrigen datetime NOT NULL CONSTRAINT DF_ClientesOrigen_FechaOrigen DEFAULT (GETDATE()),
        UsuarioOrigen varchar(50) NOT NULL,
        FechaAvisoRetraso datetime NULL,
        CONSTRAINT PK_ClientesOrigen PRIMARY KEY CLUSTERED (Empresa, Cliente),
        CONSTRAINT FK_ClientesOrigen_OrigenesCliente FOREIGN KEY (Origen) REFERENCES dbo.OrigenesCliente (Codigo)
    );
    -- El job «leads-sin-llamar» y las sugerencias de contacto buscan por origen.
    CREATE NONCLUSTERED INDEX IX_ClientesOrigen_Origen ON dbo.ClientesOrigen (Origen) INCLUDE (FechaOrigen, FechaAvisoRetraso);
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'META-HARMONIA-0926')
    INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, Usuario)
    VALUES ('META-HARMONIA-0926', N'Lead de Meta · campaña Harmonia', N'Harmonia Meta Ads', '20261105', NULL, 1, 24, 'NUEVAVISION\Carlos');
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.OrigenesCliente TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ClientesOrigen TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT Codigo, Descripcion, Campana, FechaDesde, FechaHasta, Activo, HorasParaLlamar, Usuario, FechaModificacion FROM dbo.OrigenesCliente;
SELECT TOP 50 Empresa, Cliente, Origen, FechaOrigen, UsuarioOrigen, FechaAvisoRetraso FROM dbo.ClientesOrigen ORDER BY FechaOrigen DESC;
