using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.Origenes
{
    /// <summary>
    /// NestoAPI#623 corte 4: SQL del importador de leads de Meta. Las columnas LeadExternoId y AceptaComunicaciones de
    /// dbo.ClientesOrigen (<c>Scripts/Issue623_ClientesOrigenLeadExterno.sql</c>) son opcionales: sin ellas, los leads ya
    /// importados se reconocen por el teléfono o el correo.
    /// </summary>
    public class RepositorioLeadsMetaSql : IRepositorioLeadsMeta
    {
        internal const string SQL_CLIENTE_DEL_LEAD = @"
IF COL_LENGTH(N'dbo.ClientesOrigen', N'LeadExternoId') IS NOT NULL
    EXEC sp_executesql N'SELECT TOP 1 RTRIM(Cliente) FROM dbo.ClientesOrigen WITH (NOLOCK) WHERE Empresa = @empresa AND LeadExternoId = @lead',
        N'@empresa char(3), @lead nvarchar(50)', @empresa, @lead;
ELSE
    SELECT CAST(NULL AS varchar(10));";

        // El teléfono de la ficha y el de las personas de contacto; el correo, el de las personas de contacto.
        private const string CONDICION_PARECIDO = @"
WHERE c.Empresa = @empresa AND c.Estado >= 0 AND (
    (@telefono IS NOT NULL AND (REPLACE(c.Teléfono, ' ', '') LIKE '%' + @telefono + '%'
        OR EXISTS (SELECT 1 FROM PersonasContactoCliente p WITH (NOLOCK) WHERE p.Empresa = c.Empresa AND p.NºCliente = c.[Nº Cliente]
                   AND p.Contacto = c.Contacto AND REPLACE(p.Teléfono, ' ', '') LIKE '%' + @telefono + '%')))
    OR (@email IS NOT NULL AND EXISTS (SELECT 1 FROM PersonasContactoCliente p WITH (NOLOCK) WHERE p.Empresa = c.Empresa
                   AND p.NºCliente = c.[Nº Cliente] AND p.Contacto = c.Contacto AND LTRIM(RTRIM(p.CorreoElectrónico)) = @email)))
ORDER BY c.ClientePrincipal DESC, c.[Nº Cliente], c.Contacto;";

        internal const string SQL_PARECIDO = @"
IF OBJECT_ID(N'dbo.ClientesOrigen', N'U') IS NOT NULL
    SELECT TOP 1 RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(co.Origen) AS Origen, RTRIM(c.Vendedor) AS Vendedor,
        RTRIM(c.Contacto) AS Contacto, RTRIM(c.Nombre) AS Nombre, RTRIM(c.Población) AS Poblacion, RTRIM(c.CodPostal) AS CodigoPostal
    FROM Clientes c WITH (NOLOCK)
    LEFT JOIN dbo.ClientesOrigen co WITH (NOLOCK) ON co.Empresa = c.Empresa AND co.Cliente = c.[Nº Cliente]" + CONDICION_PARECIDO + @"
ELSE
    SELECT TOP 1 RTRIM(c.[Nº Cliente]) AS Cliente, CAST(NULL AS varchar(30)) AS Origen, RTRIM(c.Vendedor) AS Vendedor,
        RTRIM(c.Contacto) AS Contacto, RTRIM(c.Nombre) AS Nombre, RTRIM(c.Población) AS Poblacion, RTRIM(c.CodPostal) AS CodigoPostal
    FROM Clientes c WITH (NOLOCK)" + CONDICION_PARECIDO;

        internal const string SQL_PONER_ORIGEN = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @anterior varchar(30) = (SELECT RTRIM(Origen) FROM dbo.ClientesOrigen WITH (UPDLOCK, HOLDLOCK)
                                 WHERE Empresa = @empresa AND Cliente = @cliente);
DECLARE @resultado int = 0;
IF @anterior IS NULL
BEGIN
    INSERT INTO dbo.ClientesOrigen (Empresa, Cliente, Origen, FechaOrigen, UsuarioOrigen)
    VALUES (@empresa, @cliente, @origen, @fecha, @usuario);
    INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
    VALUES (N'ClientesOrigen', CONCAT(RTRIM(@cliente), N': sin origen'),
            CONCAT(RTRIM(@cliente), N': ', @origen, N' (lead de Meta ', @lead, N')'), @usuario);
    SET @resultado = 1;
END
ELSE IF @anterior = @origen
    SET @resultado = 2;
IF @resultado > 0 AND COL_LENGTH(N'dbo.ClientesOrigen', N'LeadExternoId') IS NOT NULL
    EXEC sp_executesql N'UPDATE dbo.ClientesOrigen SET LeadExternoId = @lead
        WHERE Empresa = @empresa AND Cliente = @cliente AND LeadExternoId IS NULL
          AND NOT EXISTS (SELECT 1 FROM dbo.ClientesOrigen x WHERE x.LeadExternoId = @lead)',
        N'@empresa char(3), @cliente char(10), @lead nvarchar(50)', @empresa, @cliente, @lead;
IF @resultado > 0 AND COL_LENGTH(N'dbo.ClientesOrigen', N'AceptaComunicaciones') IS NOT NULL
    EXEC sp_executesql N'UPDATE dbo.ClientesOrigen SET AceptaComunicaciones = @acepta WHERE Empresa = @empresa AND Cliente = @cliente',
        N'@empresa char(3), @cliente char(10), @acepta bit', @empresa, @cliente, @acepta;
COMMIT TRANSACTION;
SELECT @resultado;";

        internal const string SQL_QUITAR_BOLETIN =
            "UPDATE PersonasContactoCliente SET EnviarBoletin = 0 WHERE Empresa = @empresa AND NºCliente = @cliente AND EnviarBoletin = 1";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioLeadsMetaSql(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        public async Task<string> ClienteDelLead(string empresa, string lead)
        {
            using (NVEntities db = crearContexto())
            {
                List<string> filas = await db.Database.SqlQuery<string>(SQL_CLIENTE_DEL_LEAD,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@lead", SqlDbType.NVarChar, 50) { Value = lead }).ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f));
            }
        }

        public async Task<ClienteParecidoLead> BuscarParecido(string empresa, string telefono, string email)
        {
            using (NVEntities db = crearContexto())
            {
                List<ClienteParecidoLead> filas = await db.Database.SqlQuery<ClienteParecidoLead>(SQL_PARECIDO,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@telefono", SqlDbType.VarChar, 29) { Value = string.IsNullOrWhiteSpace(telefono) ? (object)DBNull.Value : telefono.Trim() },
                    new SqlParameter("@email", SqlDbType.VarChar, 50) { Value = string.IsNullOrWhiteSpace(email) ? (object)DBNull.Value : email.Trim() })
                    .ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault();
            }
        }

        public async Task<ResultadoOrigenLead> PonerOrigenLead(string empresa, string cliente, string origen, DateTime fechaOrigen, string lead, bool aceptaComunicaciones, string usuario)
        {
            using (NVEntities db = crearContexto())
            {
                List<int> filas = await db.Database.SqlQuery<int>(SQL_PONER_ORIGEN,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente },
                    new SqlParameter("@origen", SqlDbType.VarChar, 30) { Value = origen },
                    new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fechaOrigen },
                    new SqlParameter("@lead", SqlDbType.NVarChar, 50) { Value = (object)lead ?? DBNull.Value },
                    new SqlParameter("@acepta", SqlDbType.Bit) { Value = aceptaComunicaciones },
                    new SqlParameter("@usuario", SqlDbType.VarChar, 50) { Value = UsuarioAuditoriaHelper.ParaAuditoria(usuario) })
                    .ToListAsync().ConfigureAwait(false);
                return (ResultadoOrigenLead)filas.FirstOrDefault();
            }
        }

        public async Task QuitarBoletin(string empresa, string cliente)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(TransactionalBehavior.DoNotEnsureTransaction, SQL_QUITAR_BOLETIN,
                    new SqlParameter("@empresa", SqlDbType.Char, 3) { Value = empresa },
                    new SqlParameter("@cliente", SqlDbType.Char, 10) { Value = cliente }).ConfigureAwait(false);
            }
        }
    }

    /// <summary>NestoAPI#623 corte 4: el alta de leads por GestorClientes, igual que POST api/Clientes.</summary>
    public class AltaClientesLeadsGestor : IAltaClientesLeads
    {
        private readonly IGestorClientes gestor;
        private readonly IServicioGestorClientes servicio;

        public AltaClientesLeadsGestor(IGestorClientes gestor, IServicioGestorClientes servicio = null)
        {
            this.gestor = gestor;
            this.servicio = servicio ?? new ServicioGestorClientes();
        }

        public Task<RespuestaDatosGeneralesClientes> DatosCodigoPostal(string codigoPostal) => servicio.CogerDatosCodigoPostal(codigoPostal);

        public async Task<string> Crear(ClienteCrear cliente)
        {
            NormalizadorClienteCrear.Normalizar(cliente);
            string mensaje = MensajesValidacionCliente.Desde(cliente);
            if (mensaje != null)
            {
                throw new System.ComponentModel.DataAnnotations.ValidationException(mensaje);
            }
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                try
                {
                    Cliente creado = await gestor.CrearCliente(cliente, db).ConfigureAwait(false);
                    return creado.Nº_Cliente?.Trim();
                }
                catch (System.Data.Entity.Infrastructure.DbUpdateException ex) when (ErroresTriggerSql.MensajeDeValidacion(ex) != null)
                {
                    throw new System.ComponentModel.DataAnnotations.ValidationException(ErroresTriggerSql.MensajeDeValidacion(ex));
                }
            }
        }

        public Task PublicarConOrigen(string empresa, string cliente, string origen, DateTime fechaOrigen, bool aceptaComunicaciones, string usuario)
            => gestor.PublicarClienteConOrigenSincronizar(empresa, cliente, origen, fechaOrigen, aceptaComunicaciones, usuario);
    }
}
