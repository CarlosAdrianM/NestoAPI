using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#799: acceso a <c>dbo.ClientesDiaReparto</c> y a los pedidos pendientes del contacto (sustituible en los tests).</summary>
    public interface IRepositorioDiasRepartoClientes
    {
        /// <summary>La excepción del contacto, o null si no tiene (o no hay tabla).</summary>
        Task<ExcepcionDiaRepartoCliente> Leer(string empresa, string cliente, string contacto);
        Task<bool> ContactoExiste(string empresa, string cliente, string contacto);
        /// <summary>Pone la excepción (Ruta '01'..'05') o la quita (Ruta null) y deja la fila en Modificaciones.</summary>
        Task Guardar(ExcepcionDiaRepartoCliente excepcion, string usuario);
        /// <summary>
        /// Vuelve a calcular la fecha de entrega de las líneas pendientes (-1 y 1, sin picking ni albarán) de los pedidos del contacto
        /// que van por ruta propia, con el día que tenga ahora (la caché ya invalidada). Devuelve cuántos pedidos han cambiado.
        /// </summary>
        Task<int> RecalcularFechasPendientes(string empresa, string cliente, string contacto, string usuario);
    }

    public class RepositorioDiasRepartoClientes : IRepositorioDiasRepartoClientes
    {
        internal const string SQL_LEER_UNO =
            "IF OBJECT_ID(N'dbo.ClientesDiaReparto', N'U') IS NOT NULL " +
            "SELECT RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, RTRIM(Ruta) AS Ruta, " +
            "Motivo, Usuario, FechaModificacion FROM dbo.ClientesDiaReparto WITH (NOLOCK) " +
            "WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto";

        internal const string SQL_GUARDAR = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;
DECLARE @anterior nvarchar(400) = (SELECT CONCAT(RTRIM(Ruta), N' ', Motivo) FROM dbo.ClientesDiaReparto WITH (UPDLOCK, HOLDLOCK)
                                   WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto);
IF @ruta IS NULL
    DELETE FROM dbo.ClientesDiaReparto WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto;
ELSE IF @anterior IS NULL
    INSERT INTO dbo.ClientesDiaReparto (Empresa, Cliente, Contacto, Ruta, Motivo, Usuario, FechaModificacion)
    VALUES (@empresa, @cliente, @contacto, @ruta, @motivo, @usuario, GETDATE());
ELSE
    UPDATE dbo.ClientesDiaReparto SET Ruta = @ruta, Motivo = @motivo, Usuario = @usuario, FechaModificacion = GETDATE()
    WHERE Empresa = @empresa AND Cliente = @cliente AND Contacto = @contacto;
INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario)
VALUES (N'ClientesDiaReparto',
        CONCAT(RTRIM(@cliente), N'/', RTRIM(@contacto), N': ', ISNULL(@anterior, N'por su código postal')),
        CONCAT(RTRIM(@cliente), N'/', RTRIM(@contacto), N': ', ISNULL(@ruta + N' ' + ISNULL(@motivo, N''), N'por su código postal')),
        @usuario);
COMMIT TRANSACTION;";

        private readonly Func<NVEntities> crearContexto;

        public RepositorioDiasRepartoClientes(Func<NVEntities> crearContexto = null)
        {
            this.crearContexto = crearContexto ?? (() =>
            {
                var db = new NVEntities();
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                return db;
            });
        }

        public async Task<ExcepcionDiaRepartoCliente> Leer(string empresa, string cliente, string contacto)
        {
            using (NVEntities db = crearContexto())
            {
                List<ExcepcionDiaRepartoCliente> filas = await db.Database.SqlQuery<ExcepcionDiaRepartoCliente>(SQL_LEER_UNO,
                    new SqlParameter("@empresa", empresa ?? string.Empty),
                    new SqlParameter("@cliente", cliente ?? string.Empty),
                    new SqlParameter("@contacto", contacto ?? string.Empty)).ToListAsync().ConfigureAwait(false);
                return filas.FirstOrDefault();
            }
        }

        public async Task<bool> ContactoExiste(string empresa, string cliente, string contacto)
        {
            using (NVEntities db = crearContexto())
            {
                return await db.Clientes.AnyAsync(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto).ConfigureAwait(false);
            }
        }

        public async Task Guardar(ExcepcionDiaRepartoCliente excepcion, string usuario)
        {
            using (NVEntities db = crearContexto())
            {
                _ = await db.Database.ExecuteSqlCommandAsync(System.Data.Entity.TransactionalBehavior.DoNotEnsureTransaction, SQL_GUARDAR,
                    new SqlParameter("@empresa", excepcion.Empresa),
                    new SqlParameter("@cliente", excepcion.Cliente),
                    new SqlParameter("@contacto", excepcion.Contacto),
                    new SqlParameter("@ruta", System.Data.SqlDbType.Char, 2) { Value = (object)excepcion.Ruta ?? DBNull.Value },
                    new SqlParameter("@motivo", System.Data.SqlDbType.NVarChar, 200) { Value = (object)excepcion.Motivo ?? DBNull.Value },
                    new SqlParameter("@usuario", System.Data.SqlDbType.VarChar, 50) { Value = usuario }).ConfigureAwait(false);
            }
        }

        public async Task<int> RecalcularFechasPendientes(string empresa, string cliente, string contacto, string usuario)
        {
            using (NVEntities db = crearContexto())
            {
                List<LinPedidoVta> lineas = await db.LinPedidoVtas
                    .Where(l => l.Empresa == empresa && l.Nº_Cliente == cliente && l.Contacto == contacto
                        && (l.Estado == Constantes.EstadosLineaVenta.PENDIENTE || l.Estado == Constantes.EstadosLineaVenta.EN_CURSO)
                        && (l.Picking == null || l.Picking == 0) && l.Nº_Albarán == null)
                    .ToListAsync().ConfigureAwait(false);
                if (!lineas.Any())
                {
                    return 0;
                }
                List<int> numeros = lineas.Select(l => l.Número).Distinct().ToList();
                Dictionary<int, string> rutas = (await db.CabPedidoVtas
                    .Where(c => c.Empresa == empresa && numeros.Contains(c.Número))
                    .Select(c => new { c.Número, c.Ruta })
                    .ToListAsync().ConfigureAwait(false))
                    .ToDictionary(c => c.Número, c => c.Ruta?.Trim());
                string codigoPostal = (await db.Clientes
                    .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                    .Select(c => c.CodPostal)
                    .FirstOrDefaultAsync().ConfigureAwait(false))?.Trim();

                var cambiados = new HashSet<int>();
                DateTime hoy = DateTime.Today;
                string usuarioAuditoria = UsuarioAuditoriaHelper.ParaAuditoria(usuario);
                foreach (LinPedidoVta linea in lineas)
                {
                    if (!rutas.TryGetValue(linea.Número, out string ruta))
                    {
                        continue;
                    }
                    DateTime? nueva = NuevaFechaEntrega(linea.Fecha_Entrega, ruta, linea.Almacén?.Trim(), codigoPostal, empresa, cliente, contacto, hoy,
                        GestorPedidosVenta.FechaEntregaAjustadaConCodigoPostal);
                    if (nueva.HasValue)
                    {
                        linea.Fecha_Entrega = nueva.Value;
                        linea.Usuario = usuarioAuditoria;
                        _ = cambiados.Add(linea.Número);
                    }
                }
                if (cambiados.Count > 0)
                {
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                }
                return cambiados.Count;
            }
        }

        /// <summary>
        /// La fecha nueva de una línea pendiente al cambiar el día de reparto del contacto, o null si no cambia. Solo las de ruta
        /// propia desde Algete con día de reparto (excepción o código postal) y que no estén retenidas (2099…). Se busca el primer día
        /// de reparto posible desde el lunes de la semana de la fecha que tenía (nunca antes de hoy ni de la hora de corte: eso lo pone
        /// <paramref name="ajustar"/>), para que una línea del martes pase al miércoles de ESA semana y no a la siguiente (y al revés).
        /// </summary>
        internal static DateTime? NuevaFechaEntrega(DateTime fechaActual, string ruta, string almacen, string codigoPostal,
            string empresa, string cliente, string contacto, DateTime hoy,
            Func<DateTime, string, string, string, string, string, string, DateTime> ajustar)
        {
            if (!RutasPropiasConfiguradas.Contiene(ruta) || fechaActual >= GestorPedidosVenta.FECHA_RETENCION)
            {
                return null;
            }
            if (!string.IsNullOrEmpty(almacen) && almacen != Constantes.Almacenes.ALGETE)
            {
                return null;
            }
            if (!DiasRepartoCodigosPostales.DiaRutaInterna(ruta, codigoPostal, empresa, cliente, contacto).HasValue)
            {
                return null;
            }
            DateTime desde = fechaActual.Date.AddDays(-(((int)fechaActual.DayOfWeek + 6) % 7)); // el lunes de su semana
            if (desde < hoy.Date)
            {
                desde = hoy.Date;
            }
            DateTime nueva = ajustar(desde, ruta, Constantes.Almacenes.ALGETE, codigoPostal, empresa, cliente, contacto);
            return nueva.Date == fechaActual.Date ? (DateTime?)null : nueva.Date;
        }
    }
}
