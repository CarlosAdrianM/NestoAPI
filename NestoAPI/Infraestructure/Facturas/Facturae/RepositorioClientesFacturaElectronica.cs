using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Facturas.Facturae
{
    /// <summary>NestoAPI#903: una fila de <c>dbo.ClientesFacturaElectronica</c> (los DIR3 del organismo de un cliente y contacto).</summary>
    public class FilaClienteFacturaElectronica : DatosOrganismoFacturae
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#903: la fila del contacto (o la única del cliente), o HayTabla = false si todavía no se ha lanzado el script.</summary>
    public class LecturaClienteFacturaElectronica
    {
        public bool HayTabla { get; set; }
        public FilaClienteFacturaElectronica Fila { get; set; }
    }

    /// <summary>NestoAPI#903: lo que ha pasado al escribir (los valores los devuelve el propio SQL).</summary>
    public enum ResultadoEscrituraFacturaElectronica
    {
        SinTabla = 0,
        Creado = 1,
        Modificado = 2,
        Borrado = 3,
        NoExiste = 4
    }

    /// <summary>NestoAPI#903: acceso a <c>dbo.ClientesFacturaElectronica</c> (fuera del EDMX; sustituible en los tests).</summary>
    public interface IRepositorioClientesFacturaElectronica
    {
        /// <summary>La fila de ese contacto; si no la hay y el cliente tiene UNA sola, esa (el organismo factura con un contacto).</summary>
        Task<LecturaClienteFacturaElectronica> Leer(string empresa, string cliente, string contacto);
        /// <summary>Alta o cambio (clave Empresa + Cliente + Contacto); sin oficina, órgano ni unidad, la borra. Deja la fila en Modificaciones.</summary>
        Task<ResultadoEscrituraFacturaElectronica> Guardar(FilaClienteFacturaElectronica fila, string usuario);
    }

    public class RepositorioClientesFacturaElectronica : IRepositorioClientesFacturaElectronica
    {
        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.ClientesFacturaElectronica', N'U') IS NULL " +
            "SELECT CAST(0 AS bit) AS HayTabla, CAST(NULL AS nvarchar(10)) AS Empresa, CAST(NULL AS nvarchar(10)) AS Cliente, CAST(NULL AS nvarchar(10)) AS Contacto, " +
            "CAST(NULL AS nvarchar(20)) AS OficinaContable, CAST(NULL AS nvarchar(20)) AS OrganoGestor, CAST(NULL AS nvarchar(20)) AS UnidadTramitadora, " +
            "CAST(NULL AS nvarchar(20)) AS OrganoProponente, CAST(NULL AS nvarchar(100)) AS NombreOficinaContable, CAST(NULL AS nvarchar(100)) AS NombreOrganoGestor, " +
            "CAST(NULL AS nvarchar(100)) AS NombreUnidadTramitadora, CAST(NULL AS nvarchar(100)) AS NombreOrganoProponente, CAST(NULL AS nvarchar(100)) AS Usuario, " +
            "CAST(NULL AS datetime) AS FechaModificacion " +
            "ELSE SELECT TOP 2 CAST(1 AS bit) AS HayTabla, Empresa, Cliente, Contacto, OficinaContable, OrganoGestor, UnidadTramitadora, OrganoProponente, " +
            "NombreOficinaContable, NombreOrganoGestor, NombreUnidadTramitadora, NombreOrganoProponente, Usuario, FechaModificacion " +
            "FROM dbo.ClientesFacturaElectronica WITH (NOLOCK) WHERE Empresa = @empresa AND Cliente = @cliente " +
            "ORDER BY CASE WHEN Contacto = @contacto THEN 0 ELSE 1 END, Contacto";

        internal const string SQL_GUARDAR = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @resultado int = 0;
IF OBJECT_ID(N'dbo.ClientesFacturaElectronica', N'U') IS NOT NULL
BEGIN
    BEGIN TRANSACTION;
    DECLARE @anterior nvarchar(400) = (SELECT CONCAT(N'Oficina contable ', OficinaContable, N', órgano gestor ', OrganoGestor, N', unidad tramitadora ',
        UnidadTramitadora, N', órgano proponente ', ISNULL(OrganoProponente, N'-'))
        FROM dbo.ClientesFacturaElectronica WITH (UPDLOCK, HOLDLOCK) WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto);
    DECLARE @nuevo nvarchar(400) = CONCAT(N'Oficina contable ', @oficina, N', órgano gestor ', @gestor, N', unidad tramitadora ', @unidad,
        N', órgano proponente ', ISNULL(@proponente, N'-'));
    IF @oficina IS NULL AND @gestor IS NULL AND @unidad IS NULL
    BEGIN
        IF @anterior IS NULL
            SET @resultado = 4;
        ELSE
        BEGIN
            DELETE FROM dbo.ClientesFacturaElectronica WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto;
            SET @nuevo = N'(borrado)';
            SET @resultado = 3;
        END
    END
    ELSE IF @anterior IS NULL
    BEGIN
        INSERT INTO dbo.ClientesFacturaElectronica (Empresa, Cliente, Contacto, OficinaContable, OrganoGestor, UnidadTramitadora, OrganoProponente,
            NombreOficinaContable, NombreOrganoGestor, NombreUnidadTramitadora, NombreOrganoProponente, Usuario, FechaModificacion)
        VALUES (@empresa, @cliente, @contacto, @oficina, @gestor, @unidad, @proponente,
            @nombreOficina, @nombreGestor, @nombreUnidad, @nombreProponente, @usuario, GETDATE());
        SET @resultado = 1;
    END
    ELSE
    BEGIN
        UPDATE dbo.ClientesFacturaElectronica SET OficinaContable = @oficina, OrganoGestor = @gestor, UnidadTramitadora = @unidad,
            OrganoProponente = @proponente, NombreOficinaContable = @nombreOficina, NombreOrganoGestor = @nombreGestor,
            NombreUnidadTramitadora = @nombreUnidad, NombreOrganoProponente = @nombreProponente, Usuario = @usuario, FechaModificacion = GETDATE()
        WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto;
        SET @resultado = 2;
    END
    IF @resultado IN (1, 2, 3)
        INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
        VALUES (N'ClientesFacturaElectronica',
                CONCAT(@empresa, N'/', @cliente, N'/', @contacto, N': ', ISNULL(@anterior, N'(sin DIR3)')),
                CONCAT(@empresa, N'/', @cliente, N'/', @contacto, N': ', @nuevo),
                @usuario);
    COMMIT TRANSACTION;
END
SELECT @resultado;";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioClientesFacturaElectronica(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        private class FilaLeida : FilaClienteFacturaElectronica
        {
            public bool HayTabla { get; set; }
        }

        public async Task<LecturaClienteFacturaElectronica> Leer(string empresa, string cliente, string contacto)
        {
            List<FilaLeida> filas;
            using (NVEntities db = crearContexto())
            {
                filas = await db.Database.SqlQuery<FilaLeida>(SQL_LEER,
                    new SqlParameter("@empresa", empresa?.Trim() ?? string.Empty),
                    new SqlParameter("@cliente", cliente?.Trim() ?? string.Empty),
                    new SqlParameter("@contacto", contacto?.Trim() ?? string.Empty)).ToListAsync().ConfigureAwait(false);
            }
            return Elegir(filas.Cast<FilaClienteFacturaElectronica>().ToList(), filas.Count == 0 || filas.Any(f => f.HayTabla), contacto);
        }

        /// <summary>La del contacto; si no, la única del cliente; si hay varias de otros contactos, ninguna.</summary>
        internal static LecturaClienteFacturaElectronica Elegir(List<FilaClienteFacturaElectronica> filas, bool hayTabla, string contacto)
        {
            var lectura = new LecturaClienteFacturaElectronica { HayTabla = hayTabla };
            List<FilaClienteFacturaElectronica> validas = filas.Where(f => !string.IsNullOrWhiteSpace(f.Cliente)).ToList();
            FilaClienteFacturaElectronica fila = validas.FirstOrDefault(f => string.Equals(f.Contacto?.Trim(), contacto?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? (validas.Count == 1 ? validas[0] : null);
            if (fila != null)
            {
                fila.Empresa = fila.Empresa?.Trim();
                fila.Cliente = fila.Cliente?.Trim();
                fila.Contacto = fila.Contacto?.Trim();
            }
            lectura.Fila = fila;
            return lectura;
        }

        public async Task<ResultadoEscrituraFacturaElectronica> Guardar(FilaClienteFacturaElectronica fila, string usuario)
        {
            object Valor(string texto) => string.IsNullOrWhiteSpace(texto) ? (object)DBNull.Value : texto.Trim();
            var parametros = new[]
            {
                new SqlParameter("@empresa", fila.Empresa?.Trim() ?? string.Empty),
                new SqlParameter("@cliente", fila.Cliente?.Trim() ?? string.Empty),
                new SqlParameter("@contacto", fila.Contacto?.Trim() ?? string.Empty),
                new SqlParameter("@oficina", System.Data.SqlDbType.NVarChar, 20) { Value = Valor(fila.OficinaContable) },
                new SqlParameter("@gestor", System.Data.SqlDbType.NVarChar, 20) { Value = Valor(fila.OrganoGestor) },
                new SqlParameter("@unidad", System.Data.SqlDbType.NVarChar, 20) { Value = Valor(fila.UnidadTramitadora) },
                new SqlParameter("@proponente", System.Data.SqlDbType.NVarChar, 20) { Value = Valor(fila.OrganoProponente) },
                new SqlParameter("@nombreOficina", System.Data.SqlDbType.NVarChar, 100) { Value = Valor(fila.NombreOficinaContable) },
                new SqlParameter("@nombreGestor", System.Data.SqlDbType.NVarChar, 100) { Value = Valor(fila.NombreOrganoGestor) },
                new SqlParameter("@nombreUnidad", System.Data.SqlDbType.NVarChar, 100) { Value = Valor(fila.NombreUnidadTramitadora) },
                new SqlParameter("@nombreProponente", System.Data.SqlDbType.NVarChar, 100) { Value = Valor(fila.NombreOrganoProponente) },
                new SqlParameter("@usuario", usuario ?? string.Empty)
            };
            using (NVEntities db = crearContexto())
            {
                int resultado = await db.Database.SqlQuery<int>(SQL_GUARDAR, parametros.Cast<object>().ToArray())
                    .FirstAsync().ConfigureAwait(false);
                return (ResultadoEscrituraFacturaElectronica)resultado;
            }
        }
    }
}
