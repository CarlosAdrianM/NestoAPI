using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Stocks;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>Novedades 1228: lo que el Terminar de un picking ha hecho con una falta de una línea (para el aviso).</summary>
    public class FaltaPickingPedido
    {
        public string Empresa { get; set; }
        public int Pedido { get; set; }
        public int Linea { get; set; }
        public string Producto { get; set; }
        public int Unidades { get; set; }
        public bool EsMuestra { get; set; }
        /// <summary>Muestra quitada del pedido; false = queda pendiente (lo de siempre).</summary>
        public bool QuitadaDelPedido { get; set; }
        /// <summary>Al pedido no le queda ningún producto en el picking.</summary>
        public bool NoSaleNadaDelPedido { get; set; }
        /// <summary>NestoAPI#858 punto 4: el pedido («todo junto») ha quedado retenido por esta falta.</summary>
        public bool Retenido { get; set; }
        /// <summary>NestoAPI#858 punto 4: hasta cuándo puede decidir el vendedor.</summary>
        public DateTime? PlazoRetencion { get; set; }
    }

    /// <summary>Novedades 1228: avisa de las faltas de un picking terminado. Nunca lanza ni hace esperar al Terminar.</summary>
    public interface IAvisadorFaltasPicking
    {
        void Avisar(string empresa, int picking, string almacen, IReadOnlyList<FaltaPickingPedido> faltas);
    }

    /// <summary>Un pedido a avisar, con quién tiene que enterarse.</summary>
    public class PedidoAAvisarFaltas
    {
        public PedidoConFaltasPicking Pedido { get; set; }
        public ComercialesPedido Comerciales { get; set; }
    }

    public interface ILectorDatosFaltasPicking
    {
        List<PedidoAAvisarFaltas> Leer(string empresa, int picking, string almacen, IReadOnlyList<FaltaPickingPedido> faltas);
    }

    /// <summary>
    /// Novedades 1228 (picking 100070, 12/11/26): al terminar un picking con faltas, por cada pedido, la campana de Nesto y la
    /// push de NestoApp de quien lo hizo y de su vendedor (<see cref="AvisoFaltasPicking"/>), y a Compras (buzón de Nesto) lo
    /// que queda pendiente sin compra pendiente y no es muestra. En segundo plano, con su propio contexto (el de la petición ya
    /// se habrá cerrado) y sin lanzar nunca: un fallo va a ELMAH.
    /// </summary>
    public class AvisadorFaltasPicking : IAvisadorFaltasPicking
    {
        public const string USUARIO_AUDITORIA = "Picking (faltas al terminar)";

        private readonly IAvisadorCompras compras;
        private readonly Func<ILectorDatosFaltasPicking> crearLector;
        private readonly Func<IServicioNotificacionesPush> crearNotificaciones;
        private readonly Action<Exception> registrar;

        public AvisadorFaltasPicking(IAvisadorCompras compras)
            : this(compras, () => new LectorDatosFaltasPickingSql(), () => new ServicioNotificacionesPush(), null)
        {
        }

        internal AvisadorFaltasPicking(IAvisadorCompras compras, Func<ILectorDatosFaltasPicking> crearLector,
            Func<IServicioNotificacionesPush> crearNotificaciones, Action<Exception> registrar)
        {
            this.compras = compras;
            this.crearLector = crearLector;
            this.crearNotificaciones = crearNotificaciones;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, USUARIO_AUDITORIA));
        }

        public void Avisar(string empresa, int picking, string almacen, IReadOnlyList<FaltaPickingPedido> faltas)
        {
            List<FaltaPickingPedido> copia = (faltas ?? new List<FaltaPickingPedido>()).Where(f => f != null && f.Unidades > 0).ToList();
            if (copia.Count == 0)
            {
                return;
            }
            // La push va por Firebase: no alarga el Terminar (como los avisos del picking, GestorPicking)
            _ = Task.Run(() => AvisarAsync(empresa, picking, almacen, copia));
        }

        /// <summary>El trabajo de verdad. Nunca lanza.</summary>
        internal async Task AvisarAsync(string empresa, int picking, string almacen, IReadOnlyList<FaltaPickingPedido> faltas)
        {
            ILectorDatosFaltasPicking lector = null;
            try
            {
                lector = crearLector();
                List<PedidoAAvisarFaltas> pedidos = lector.Leer(empresa, picking, almacen, faltas) ?? new List<PedidoAAvisarFaltas>();
                IServicioNotificacionesPush notificaciones = crearNotificaciones();
                foreach (PedidoAAvisarFaltas pedido in pedidos.Where(p => p?.Pedido != null))
                {
                    try
                    {
                        await AvisarPedido(pedido, notificaciones).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        registrar(new Exception($"[Picking faltas] No se pudo avisar de las faltas del pedido {empresa?.Trim()}/{pedido.Pedido.Pedido} " +
                            $"(picking {picking}): {ex.Message}", ex));
                    }
                }
                try
                {
                    List<string> avisosCompras = AvisoFaltasPicking.AvisosCompras(pedidos.Where(p => p?.Pedido != null).Select(p => p.Pedido));
                    if (avisosCompras.Any() && compras != null)
                    {
                        await compras.Avisar(AvisoFaltasPicking.TituloCompras(picking), avisosCompras, AvisadorCompras.TIPO_PICKING_FALTA).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Picking faltas] No se pudo avisar a Compras de las faltas del picking {picking}: {ex.Message}", ex));
                }
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Picking faltas] No se pudo avisar de las faltas del picking {picking}: {ex.Message}", ex));
            }
            finally
            {
                try
                {
                    (lector as IDisposable)?.Dispose();
                }
                catch
                {
                    // nunca romper nada por cerrar el contexto
                }
            }
        }

        private static async Task AvisarPedido(PedidoAAvisarFaltas pedido, IServicioNotificacionesPush notificaciones)
        {
            NotificacionPushDTO notificacion = AvisoFaltasPicking.Componer(pedido.Pedido);
            if (notificacion == null)
            {
                return;
            }
            DestinatariosAvisoFaltasPicking destinatarios = AvisoFaltasPicking.Destinatarios(pedido.Comerciales);
            foreach (string usuario in destinatarios.BuzonNesto)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(destinatarios.UsuarioNestoApp))
            {
                _ = await notificaciones.EnviarAUsuario(destinatarios.UsuarioNestoApp, Constantes.Aplicaciones.NESTO_APP, notificacion).ConfigureAwait(false);
            }
            foreach (string vendedor in destinatarios.VendedoresNestoApp)
            {
                _ = await notificaciones.EnviarAVendedor(pedido.Pedido.Empresa?.Trim(), vendedor, notificacion).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Novedades 1228: los datos del aviso de faltas, con su propio contexto. No se testa porque lee de la base de datos.</summary>
    public class LectorDatosFaltasPickingSql : ILectorDatosFaltasPicking, IDisposable
    {
        private readonly NVEntities db;

        public LectorDatosFaltasPickingSql()
        {
            db = new NVEntities();
            db.Configuration.LazyLoadingEnabled = false;
            db.Configuration.ProxyCreationEnabled = false;
        }

        public List<PedidoAAvisarFaltas> Leer(string empresa, int picking, string almacen, IReadOnlyList<FaltaPickingPedido> faltas)
        {
            List<int> pedidos = faltas.Select(f => f.Pedido).Distinct().ToList();
            List<string> productos = faltas.Select(f => f.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
            var cabeceras = db.CabPedidoVtas
                .Where(c => c.Empresa == empresa && pedidos.Contains(c.Número))
                .Select(c => new { c.Número, c.MantenerJunto })
                .ToList();
            Dictionary<string, string> nombres = db.Productos
                .Where(p => p.Empresa == empresa && productos.Contains(p.Número))
                .Select(p => new { p.Número, p.Nombre })
                .ToList()
                .GroupBy(p => p.Número.Trim())
                .ToDictionary(g => g.Key, g => g.First().Nombre?.Trim(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Dictionary<string, int>> stocks = new LectorRepartoStock(db).LeerStocksPorAlmacen(productos);
            Dictionary<string, CompraPendientePicking> comprasPendientes = GestorPicking.ComprasPendientes(db, empresa, productos);
            var lectorComerciales = new LectorComercialesPedido(db);

            var resultado = new List<PedidoAAvisarFaltas>();
            foreach (IGrouping<int, FaltaPickingPedido> pedido in faltas.GroupBy(f => f.Pedido).OrderBy(g => g.Key))
            {
                ComercialesPedido comerciales = lectorComerciales.Leer(empresa, pedido.Key);
                resultado.Add(new PedidoAAvisarFaltas
                {
                    Comerciales = comerciales,
                    Pedido = new PedidoConFaltasPicking
                    {
                        Empresa = empresa?.Trim(),
                        Pedido = pedido.Key,
                        Picking = picking,
                        Cliente = TextoCliente(comerciales),
                        Almacen = string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim(),
                        MantenerJunto = cabeceras.FirstOrDefault(c => c.Número == pedido.Key)?.MantenerJunto ?? false,
                        NoSaleNada = pedido.Any(f => f.NoSaleNadaDelPedido),
                        Retenido = pedido.Any(f => f.Retenido),
                        PlazoRetencion = pedido.Where(f => f.Retenido).Select(f => f.PlazoRetencion).FirstOrDefault(p => p.HasValue),
                        Faltas = pedido.GroupBy(f => new { Producto = f.Producto?.Trim(), f.QuitadaDelPedido }).Select(g => new FaltaAvisoPicking
                        {
                            Producto = g.Key.Producto,
                            Nombre = g.Key.Producto != null && nombres.TryGetValue(g.Key.Producto, out string nombre) ? nombre : null,
                            Unidades = g.Sum(f => f.Unidades),
                            QuitadaDelPedido = g.Key.QuitadaDelPedido,
                            EsMuestra = g.Any(f => f.EsMuestra),
                            StockOtrosAlmacenes = OtrosAlmacenes(stocks, g.Key.Producto, almacen),
                            Compra = g.Key.Producto != null && comprasPendientes.TryGetValue(g.Key.Producto, out CompraPendientePicking compra) ? compra : null
                        }).ToList()
                    }
                });
            }
            return resultado;
        }

        internal static List<KeyValuePair<string, int>> OtrosAlmacenes(Dictionary<string, Dictionary<string, int>> stocks, string producto, string almacen)
        {
            string delPicking = string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim();
            if (producto == null || !stocks.TryGetValue(LectorRepartoStock.Clave(producto), out Dictionary<string, int> almacenes))
            {
                return new List<KeyValuePair<string, int>>();
            }
            return new[] { Constantes.Almacenes.ALGETE, Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS }
                .Where(a => !string.Equals(a, delPicking, StringComparison.OrdinalIgnoreCase))
                .Where(a => almacenes.TryGetValue(a, out int unidades) && unidades > 0)
                .Select(a => new KeyValuePair<string, int>(StockLibreAgotadoPicking.NombreAlmacen(a), almacenes[a]))
                .ToList();
        }

        private static string TextoCliente(ComercialesPedido comerciales)
        {
            if (comerciales == null)
            {
                return null;
            }
            string numero = comerciales.Cliente?.Trim();
            if (!string.IsNullOrWhiteSpace(comerciales.Contacto))
            {
                numero = $"{numero}/{comerciales.Contacto.Trim()}";
            }
            return string.IsNullOrWhiteSpace(comerciales.NombreCliente) ? numero : $"{numero} - {comerciales.NombreCliente.Trim()}";
        }

        public void Dispose()
        {
            db.Dispose();
        }
    }
}
