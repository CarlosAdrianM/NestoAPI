using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#606: datos de <see cref="ServicioFechaEntregaAgencia"/>. El pedido, el cliente y el calendario con EF; el stock
    /// con las MISMAS consultas agrupadas (ADO.NET, NOLOCK) que la sombra del modo de servicio
    /// (<see cref="RepositorioSombraModoServicioSql.LeerDatos"/>).
    ///
    /// <para>La fecha prometida (<c>CabPedidoVta.FechaEntregaAgenciaPrometida</c>) NO está en el EDMX a propósito: si estuviera
    /// y el script (Scripts/Issue606_FechaEntregaAgenciaPrometida.sql) no se hubiera lanzado, EF la pediría en TODAS las
    /// lecturas de CabPedidoVta y tumbaría la API. Se lee y se escribe con SQL parametrizado y, si la columna no existe,
    /// leer devuelve null y escribir no hace nada (lo registra quien llama).</para>
    /// </summary>
    public class RepositorioFechaEntregaAgencia : IRepositorioFechaEntregaAgencia
    {
        internal const string SQL_LEER_PROMETIDA =
            "SELECT FechaEntregaAgenciaPrometida FROM dbo.CabPedidoVta WITH (NOLOCK) WHERE Empresa = @empresa AND Número = @numero";

        internal const string SQL_GUARDAR_PROMETIDA =
            "UPDATE dbo.CabPedidoVta SET FechaEntregaAgenciaPrometida = @fecha " +
            "WHERE Empresa = @empresa AND Número = @numero AND FechaEntregaAgenciaPrometida IS NULL";

        private readonly NVEntities db;

        public RepositorioFechaEntregaAgencia(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<PedidoFechaEntregaAgencia> LeerPedido(string empresa, int numero)
        {
            var cabecera = await db.CabPedidoVtas
                .Where(c => c.Empresa == empresa && c.Número == numero)
                .Select(c => new
                {
                    c.Ruta,
                    c.ModoServicio,
                    c.ServirJunto,
                    c.ModoFacturacion,
                    c.MantenerJunto,
                    c.Cliente.DiasEnServir
                })
                .FirstOrDefaultAsync().ConfigureAwait(false);
            if (cabecera == null)
            {
                return null;
            }

            var lineas = await db.LinPedidoVtas
                .Where(l => l.Empresa == empresa && l.Número == numero && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE)
                .Select(l => new
                {
                    l.Nº_Orden,
                    l.TipoLinea,
                    l.Producto,
                    l.Almacén,
                    l.Cantidad,
                    l.Recoger,
                    l.Base_Imponible,
                    l.Fecha_Entrega,
                    l.Picking,
                    l.Estado,
                    l.Fecha_Modificación
                })
                .ToListAsync().ConfigureAwait(false);

            return new PedidoFechaEntregaAgencia
            {
                Empresa = empresa,
                Numero = numero,
                Ruta = cabecera.Ruta?.Trim(),
                ModoServicio = cabecera.ModoServicio,
                ServirJunto = cabecera.ServirJunto,
                ModoFacturacion = cabecera.ModoFacturacion,
                MantenerJunto = cabecera.MantenerJunto,
                DiasEnServir = cabecera.DiasEnServir,
                TieneLineasServidas = lineas.Any(l => l.Estado >= Constantes.EstadosLineaVenta.ALBARAN),
                // Como RellenadorPickingService: pendientes y en curso, Cantidad − Recoger, por fecha de modificación y Nº orden.
                Lineas = lineas
                    .Where(l => l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    .OrderBy(l => l.Fecha_Modificación).ThenBy(l => l.Nº_Orden)
                    .Select(l => new LineaPedidoFechaEntregaAgencia
                    {
                        Producto = l.Producto?.Trim(),
                        Almacen = l.Almacén?.Trim(),
                        Cantidad = (l.Cantidad ?? 0) - l.Recoger,
                        BaseImponible = l.Base_Imponible,
                        FechaEntrega = l.Fecha_Entrega,
                        YaEnPicking = l.Picking.GetValueOrDefault() != 0
                    })
                    .ToList()
            };
        }

        public async Task<string> LeerDiasEnServir(string empresa, string cliente, string contacto)
        {
            var fichas = await db.Clientes
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente)
                .Select(c => new { c.Contacto, c.ClientePrincipal, c.DiasEnServir })
                .ToListAsync().ConfigureAwait(false);
            var ficha = fichas.FirstOrDefault(c => string.Equals(c.Contacto?.Trim(), contacto, StringComparison.OrdinalIgnoreCase))
                ?? fichas.FirstOrDefault(c => c.ClientePrincipal)
                ?? fichas.FirstOrDefault();
            return ficha?.DiasEnServir;
        }

        public DatosSombraModoServicio LeerStock(string empresa, int? pedidoExcluir, IEnumerable<string> productos)
        {
            return RepositorioSombraModoServicioSql.DesdeConfiguracion().LeerDatos(empresa, pedidoExcluir, productos);
        }

        public Task<List<ReposicionCalendario>> LeerCalendario(string empresa)
        {
            return db.ReposicionesCalendario.Where(f => f.Empresa == empresa && f.Activo).ToListAsync();
        }

        public TimeSpan LeerHoraCorte(string empresa)
        {
            return HoraCortePicking.Leer(empresa);
        }

        public async Task<DateTime?> LeerPrometida(string empresa, int numero)
        {
            try
            {
                return await db.Database.SqlQuery<DateTime?>(SQL_LEER_PROMETIDA, ParametroEmpresa(empresa), ParametroNumero(numero))
                    .FirstOrDefaultAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // La columna aún no existe (script sin lanzar): no hay prometida, y el resto de la respuesta sigue valiendo.
                return null;
            }
        }

        public async Task<bool> GuardarPrometida(string empresa, int numero, DateTime fecha)
        {
            int filas = await db.Database.ExecuteSqlCommandAsync(SQL_GUARDAR_PROMETIDA,
                new SqlParameter("@fecha", SqlDbType.DateTime) { Value = fecha.Date },
                ParametroEmpresa(empresa), ParametroNumero(numero)).ConfigureAwait(false);
            return filas > 0;
        }

        // VarChar e Int, no NVarChar: Empresa es char y un parámetro Unicode obligaría a convertir la columna (sin índice).
        private static SqlParameter ParametroEmpresa(string empresa) =>
            new SqlParameter("@empresa", SqlDbType.VarChar, 3) { Value = empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO };

        private static SqlParameter ParametroNumero(int numero) =>
            new SqlParameter("@numero", SqlDbType.Int) { Value = numero };
    }
}
