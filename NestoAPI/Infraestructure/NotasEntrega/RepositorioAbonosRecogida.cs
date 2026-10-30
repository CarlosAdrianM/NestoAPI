using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.NotasEntrega
{
    /// <summary>NestoAPI#760: la nota de recogida, con su cabecera (NotasEntregaCabecera) y lo que hace falta de su pedido.</summary>
    public class NotaRecogidaLeida
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public bool NotaEntrega { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public string Serie { get; set; }
        /// <summary>"Entrega" / "Recogida" (de NotasEntregaCabecera; sin fila, null = Entrega).</summary>
        public string Tipo { get; set; }
        public short Bultos { get; set; }
        public string FacturaOriginal { get; set; }
        public int? PedidoAbono { get; set; }
        public string TextoNota { get; set; }
    }

    /// <summary>NestoAPI#760: la parada de la recogida (el último envío de la agencia 14 del pedido) y su prueba de entrega.</summary>
    public class ParadaRecogidaLeida
    {
        public int EnvioId { get; set; }
        public short Estado { get; set; }
        public DateTime? FechaRecogida { get; set; }
        public string Receptor { get; set; }
    }

    /// <summary>NestoAPI#760: lo que se copia de la ficha del cliente a la cabecera del abono.</summary>
    public class FichaClienteAbono
    {
        public string FormaPago { get; set; }
        public string Iva { get; set; }
        public string Vendedor { get; set; }
        public string Ruta { get; set; }
        public string Ccc { get; set; }
        public string ContactoCobro { get; set; }
    }

    /// <summary>NestoAPI#760: la cabecera de un pedido de abono ya creado.</summary>
    public class PedidoAbonoLeido
    {
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Serie { get; set; }
    }

    public interface IRepositorioAbonosRecogida
    {
        /// <summary>¿Existe NotasEntregaCabecera (Scripts/Issue757_NotasEntregaCabecera.sql)?</summary>
        bool ScriptAplicado();
        /// <summary>La nota (pedido + cabecera + texto), o null si el pedido no existe.</summary>
        Task<NotaRecogidaLeida> LeerNota(string empresa, int pedido);
        /// <summary>La parada de reparto propio de la recogida, o null si no tiene envío de la agencia 14.</summary>
        Task<ParadaRecogidaLeida> LeerParada(string empresa, int pedido);
        /// <summary>La cabecera de un pedido (para el abono ya creado), o null si no existe (lo han borrado).</summary>
        Task<PedidoAbonoLeido> LeerPedido(string empresa, int numero);
        /// <summary>La ficha del cliente/contacto, o null.</summary>
        Task<FichaClienteAbono> LeerFicha(string empresa, string cliente, string contacto);
        /// <summary>Reserva el número del pedido nuevo (ContadoresGlobales, atómico, #275).</summary>
        int ReservarNumeroPedido();
        /// <summary>
        /// Guarda la cabecera del abono, su fila en Modificaciones y NotasEntregaCabecera.PedidoAbono, todo junto. El UPDATE de la
        /// nota solo entra si PedidoAbono sigue siendo <paramref name="pedidoAbonoAnterior"/>: si otro se ha adelantado (dos bultos
        /// leídos a la vez) no guarda nada y devuelve false.
        /// </summary>
        Task<bool> GuardarAbono(CabPedidoVta cabecera, Modificacion modificacion, int pedidoNota, int? pedidoAbonoAnterior, string usuario);
        /// <summary>Los productos (recortados) de las líneas de la factura, o lista vacía.</summary>
        Task<List<string>> LeerProductosFactura(string empresa, string factura);
        /// <summary>La FacturaOriginal de la nota de recogida cuyo abono es <paramref name="pedidoAbono"/>, o null.</summary>
        Task<string> LeerFacturaOriginalDelAbono(string empresa, int pedidoAbono);
    }

    /// <summary>
    /// NestoAPI#760: NotasEntregaCabecera va por SQL (fuera del EDMX, como en #757); el pedido, la ficha, los envíos y la cabecera
    /// del abono, por EF. La prueba de entrega (EnviosAgenciaPruebasEntrega, #739) también por SQL y solo si está la tabla.
    /// </summary>
    public class RepositorioAbonosRecogida : IRepositorioAbonosRecogida
    {
        internal const string SQL_RESERVAR_ABONO = "UPDATE dbo." + RepositorioNotasEntregaCabecera.TABLA +
            " SET PedidoAbono = @Abono, Usuario = @Usuario, FechaModificacion = GETDATE() " +
            "WHERE Empresa = @Empresa AND Pedido = @Pedido AND ISNULL(PedidoAbono, 0) = @Anterior";

        internal const string SQL_FACTURA_DEL_ABONO = "SELECT TOP 1 RTRIM(FacturaOriginal) FROM dbo." + RepositorioNotasEntregaCabecera.TABLA +
            " WITH (NOLOCK) WHERE Empresa = @p0 AND PedidoAbono = @p1 AND FacturaOriginal IS NOT NULL";

        private readonly NVEntities db;
        private readonly RepositorioNotasEntregaCabecera cabeceras;
        private readonly RepositorioParadasNotaEntrega paradas;

        public RepositorioAbonosRecogida(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
            cabeceras = new RepositorioNotasEntregaCabecera(db);
            paradas = new RepositorioParadasNotaEntrega(db);
        }

        public bool ScriptAplicado() => cabeceras.ScriptAplicado();

        public async Task<NotaRecogidaLeida> LeerNota(string empresa, int pedido)
        {
            var pedidos = await db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Número == pedido)
                .Select(c => new { c.Nº_Cliente, c.Contacto, c.NotaEntrega, c.Serie })
                .ToListAsync().ConfigureAwait(false);
            var cabecera = pedidos.FirstOrDefault();
            if (cabecera == null)
            {
                return null;
            }
            FilaNotaEntregaCabecera fila = await cabeceras.Leer(empresa, pedido).ConfigureAwait(false);
            DestinoNotaEntrega destino = await paradas.LeerDestinoDelPedido(empresa, pedido).ConfigureAwait(false);
            return new NotaRecogidaLeida
            {
                Empresa = empresa,
                Pedido = pedido,
                NotaEntrega = cabecera.NotaEntrega,
                Cliente = cabecera.Nº_Cliente?.Trim(),
                Contacto = cabecera.Contacto?.Trim(),
                Nombre = destino?.Nombre,
                Serie = cabecera.Serie?.Trim(),
                Tipo = fila?.Tipo,
                Bultos = fila?.Bultos ?? Models.PedidosVenta.TiposNotaEntrega.BULTOS_POR_DEFECTO,
                FacturaOriginal = string.IsNullOrWhiteSpace(fila?.FacturaOriginal) ? null : fila.FacturaOriginal.Trim(),
                PedidoAbono = fila?.PedidoAbono,
                TextoNota = destino?.Texto
            };
        }

        public async Task<ParadaRecogidaLeida> LeerParada(string empresa, int pedido)
        {
            int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO;
            var envios = await db.EnviosAgencias.AsNoTracking()
                .Where(e => e.Empresa == empresa && e.Pedido == pedido && e.Agencia == agencia)
                .OrderByDescending(e => e.Numero)
                .Select(e => new { e.Numero, e.Estado, e.FechaEntrega })
                .Take(1)
                .ToListAsync().ConfigureAwait(false);
            var envio = envios.FirstOrDefault();
            if (envio == null)
            {
                return null;
            }
            var parada = new ParadaRecogidaLeida { EnvioId = envio.Numero, Estado = envio.Estado, FechaRecogida = envio.FechaEntrega };
            if (ColumnasOpcionales.ExisteTabla(db, RepositorioRepartoPropio.TABLA))
            {
                List<PruebaRecogida> pruebas = await db.Database.SqlQuery<PruebaRecogida>(
                    "SELECT TOP 1 FechaHora, Receptor FROM dbo." + RepositorioRepartoPropio.TABLA +
                    " WITH (NOLOCK) WHERE EnvioId = @p0 AND Tipo = @p1 ORDER BY Id DESC",
                    envio.Numero, PruebaEntregaReparto.TIPO_ENTREGA).ToListAsync().ConfigureAwait(false);
                PruebaRecogida prueba = pruebas.FirstOrDefault();
                if (prueba != null)
                {
                    parada.FechaRecogida = prueba.FechaHora ?? parada.FechaRecogida;
                    parada.Receptor = string.IsNullOrWhiteSpace(prueba.Receptor) ? null : prueba.Receptor.Trim();
                }
            }
            return parada;
        }

        private class PruebaRecogida
        {
            public DateTime? FechaHora { get; set; }
            public string Receptor { get; set; }
        }

        public async Task<PedidoAbonoLeido> LeerPedido(string empresa, int numero)
        {
            List<PedidoAbonoLeido> pedidos = await db.CabPedidoVtas.AsNoTracking()
                .Where(c => c.Empresa == empresa && c.Número == numero)
                .Select(c => new PedidoAbonoLeido { Cliente = c.Nº_Cliente, Contacto = c.Contacto, Serie = c.Serie })
                .ToListAsync().ConfigureAwait(false);
            PedidoAbonoLeido pedido = pedidos.FirstOrDefault();
            if (pedido != null)
            {
                pedido.Cliente = pedido.Cliente?.Trim();
                pedido.Contacto = pedido.Contacto?.Trim();
                pedido.Serie = pedido.Serie?.Trim();
            }
            return pedido;
        }

        public async Task<FichaClienteAbono> LeerFicha(string empresa, string cliente, string contacto)
        {
            List<Cliente> fichas = await db.Clientes.AsNoTracking()
                .Include(c => c.CondPagoClientes)
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.Contacto == contacto)
                .ToListAsync().ConfigureAwait(false);
            Cliente ficha = fichas.FirstOrDefault();
            if (ficha == null)
            {
                return null;
            }
            // Como Copiar factura (#85): la condición de pago de importe mínimo más bajo.
            CondPagoCliente condicion = ficha.CondPagoClientes?.OrderBy(c => c.ImporteMínimo).FirstOrDefault();
            return new FichaClienteAbono
            {
                FormaPago = condicion?.FormaPago?.Trim(),
                Iva = ficha.IVA?.Trim(),
                Vendedor = ficha.Vendedor?.Trim(),
                Ruta = ficha.Ruta?.Trim(),
                Ccc = ficha.CCC?.Trim(),
                ContactoCobro = ficha.ContactoCobro?.Trim()
            };
        }

        public int ReservarNumeroPedido() => db.TomarSiguienteNumeroPedido();

        public async Task<bool> GuardarAbono(CabPedidoVta cabecera, Modificacion modificacion, int pedidoNota, int? pedidoAbonoAnterior, string usuario)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int reservada = await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR_ABONO,
                        Parametro("@Abono", SqlDbType.Int, cabecera.Número),
                        Parametro("@Usuario", SqlDbType.VarChar, Recortar(usuario, 50)),
                        Parametro("@Empresa", SqlDbType.Char, cabecera.Empresa),
                        Parametro("@Pedido", SqlDbType.Int, pedidoNota),
                        Parametro("@Anterior", SqlDbType.Int, pedidoAbonoAnterior ?? 0)).ConfigureAwait(false);
                    if (reservada == 0)
                    {
                        transaccion.RollbackSeguro();
                        return false;
                    }
                    _ = db.CabPedidoVtas.Add(cabecera);
                    // Comisiones: los vendedores por grupo de producto del cliente, como al crear cualquier pedido.
                    GestorComisiones.CrearVendedorPedidoGrupoProducto(db, cabecera, new Models.PedidosVenta.PedidoVentaDTO { Usuario = cabecera.Usuario });
                    if (modificacion != null)
                    {
                        _ = db.Modificaciones.Add(modificacion);
                    }
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return true;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    db.Entry(cabecera).State = EntityState.Detached;
                    throw;
                }
            }
        }

        public async Task<List<string>> LeerProductosFactura(string empresa, string factura)
        {
            List<string> productos = await db.LinPedidoVtas.AsNoTracking()
                .Where(l => l.Empresa == empresa && l.Nº_Factura == factura && l.Producto != null)
                .Select(l => l.Producto)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            return productos.Select(p => p.Trim()).ToList();
        }

        public async Task<string> LeerFacturaOriginalDelAbono(string empresa, int pedidoAbono)
        {
            if (!ScriptAplicado())
            {
                return null;
            }
            List<string> facturas = await db.Database.SqlQuery<string>(SQL_FACTURA_DEL_ABONO, empresa?.Trim(), pedidoAbono)
                .ToListAsync().ConfigureAwait(false);
            string factura = facturas.FirstOrDefault();
            return string.IsNullOrWhiteSpace(factura) ? null : factura.Trim();
        }

        private static SqlParameter Parametro(string nombre, SqlDbType tipo, object valor)
            => new SqlParameter(nombre, tipo) { Value = valor ?? DBNull.Value };

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
