/*
    NestoAPI#903: factura electrónica para organismos públicos (FACe por B2Brouter). Los códigos DIR3 del organismo, por cliente y
    contacto: oficina contable (rol 01), órgano gestor (02), unidad tramitadora (03) y, si lo hay, órgano proponente (04), con el
    nombre de cada centro opcional. La API los pone en BuyerParty/AdministrativeCentres del fichero Facturae que se descarga con
    GET api/Facturas/Facturae; se leen y guardan con GET/PUT api/Clientes/FacturaElectronica (escribir: Administración, Dirección,
    Informática; cada cambio deja su fila en Modificaciones, Tabla «ClientesFacturaElectronica»).

    Fuera del EDMX: la API la lee por SQL (RepositorioClientesFacturaElectronica). Sin la tabla, el GET devuelve los códigos vacíos, el
    PUT da 503 y la descarga funciona igual si los DIR3 se pasan en la llamada. Los clientes están siempre en la empresa 1.

    Lanzar como sa en NV, antes o después de publicar la API. Idempotente: se puede lanzar dos veces.
    Sin índices filtrados ni columnas calculadas indexadas (rompen los SET de las conexiones viejas de Nesto).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ClientesFacturaElectronica', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesFacturaElectronica (
        Empresa nvarchar(10) NOT NULL,
        Cliente nvarchar(10) NOT NULL,
        Contacto nvarchar(10) NOT NULL,
        OficinaContable nvarchar(20) NOT NULL,          -- DIR3, rol 01 (Fiscal)
        OrganoGestor nvarchar(20) NOT NULL,             -- DIR3, rol 02 (Receptor)
        UnidadTramitadora nvarchar(20) NOT NULL,        -- DIR3, rol 03 (Pagador)
        OrganoProponente nvarchar(20) NULL,             -- DIR3, rol 04 (opcional)
        NombreOficinaContable nvarchar(100) NULL,
        NombreOrganoGestor nvarchar(100) NULL,
        NombreUnidadTramitadora nvarchar(100) NULL,
        NombreOrganoProponente nvarchar(100) NULL,
        Usuario nvarchar(100) NOT NULL,                 -- quien lo guardó por última vez
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ClientesFacturaElectronica_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_ClientesFacturaElectronica PRIMARY KEY CLUSTERED (Empresa, Cliente, Contacto)
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ClientesFacturaElectronica TO [NUEVAVISION\RDS2016$];
GO
