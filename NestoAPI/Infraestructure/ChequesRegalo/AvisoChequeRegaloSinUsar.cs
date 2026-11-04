using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#808 (clienta 9238, pedido 929678, sugerencia 1003): el cliente tiene un cheque regalo «Disponible», el pedido que
    /// se acaba de grabar supera el mínimo del canje y no lleva la línea del cheque. Se avisa en la respuesta del POST/PUT (Avisos)
    /// para que el usuario lo añada si quiere; nunca bloquea. La regla, sin base de datos.
    /// </summary>
    public static class AvisoChequeRegaloSinUsar
    {
        public const string TIPO = "ChequeRegaloSinUsar";

        /// <summary>
        /// «El cliente tiene un cheque regalo de 50,00 € sin usar y este pedido supera los 250,00 € de producto: añádalo desde el
        /// detalle del pedido si quiere aplicarlo.», o null si no hay nada que avisar: sin cheque, cheque que no se puede usar (en
        /// otro pedido, caducado, sin activar…), el pedido ya lleva la línea del cheque o la base computable no supera el mínimo.
        /// </summary>
        public static string Mensaje(ChequeRegaloClienteDTO cheque, decimal baseComputable, bool llevaLineaCheque)
        {
            if (cheque == null || llevaLineaCheque || !cheque.SePuedeUsar || cheque.Estado != "Disponible")
            {
                return null;
            }
            if (!ReglasCanjeChequeRegalo.SuperaElMinimo(baseComputable, Campana(cheque)))
            {
                return null;
            }
            return $"El cliente tiene un cheque regalo de {ReglasCanjeChequeRegalo.Euros(cheque.Importe)} € sin usar y este pedido supera " +
                $"los {ReglasCanjeChequeRegalo.Euros(cheque.MinimoCanje)} € de producto: añádalo desde el detalle del pedido si quiere aplicarlo.";
        }

        /// <summary>
        /// El aviso para el pedido tal como ha quedado: la base computable con las mismas exclusiones que el canje
        /// (<see cref="ReglasCanjeChequeRegalo.BaseComputable"/>) y «lleva la línea» si alguna línea de producto es de un producto de
        /// cheque de cualquier campaña (<paramref name="productosCheque"/>). Null si no hay nada que avisar.
        /// </summary>
        public static AvisoPedidoDTO Evaluar(ChequeRegaloClienteDTO cheque, IEnumerable<LinPedidoVta> lineas,
            IDictionary<string, ProductoParaChequeRegalo> productos, IEnumerable<string> productosCheque)
        {
            if (cheque == null)
            {
                return null;
            }
            List<LinPedidoVta> todas = (lineas ?? Enumerable.Empty<LinPedidoVta>()).Where(l => l != null).ToList();
            var cheques = new HashSet<string>((productosCheque ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()), StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(cheque.Producto))
            {
                _ = cheques.Add(cheque.Producto.Trim());
            }
            bool llevaLineaCheque = todas.Any(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                && !string.IsNullOrWhiteSpace(l.Producto) && cheques.Contains(l.Producto.Trim()));
            decimal baseComputable = ReglasCanjeChequeRegalo.BaseComputable(todas, productos, Campana(cheque));
            string mensaje = Mensaje(cheque, baseComputable, llevaLineaCheque);
            return mensaje == null
                ? null
                : new AvisoPedidoDTO
                {
                    Tipo = TIPO,
                    Mensaje = mensaje,
                    Datos = new
                    {
                        cheque.Campana,
                        cheque.Producto,
                        cheque.Importe,
                        cheque.MinimoCanje,
                        BaseComputable = baseComputable,
                        cheque.CanjeHasta
                    }
                };
        }

        /// <summary>La campaña del cheque leído (la del DTO de GET api/ChequesRegalo/Cliente), para las reglas del canje.</summary>
        internal static CampanaCanjeChequeRegalo Campana(ChequeRegaloClienteDTO cheque) => new CampanaCanjeChequeRegalo
        {
            Codigo = cheque.Campana,
            Empresa = cheque.Empresa,
            Producto = cheque.Producto,
            ImporteBase = cheque.Importe,
            MinimoCanje = cheque.MinimoCanje,
            CanjeHasta = cheque.CanjeHasta,
            Activa = true,
            PrefijosNombreExcluidosMinimo = string.Join(";", cheque.PrefijosNombreExcluidosMinimo ?? new List<string>()),
            GruposExcluidosMinimo = string.Join(";", cheque.GruposExcluidosMinimo ?? new List<string>())
        };
    }

    /// <summary>NestoAPI#808: el aviso del cheque sin usar de un pedido ya grabado (doble en los tests).</summary>
    public interface IAvisadorChequeRegaloSinUsar
    {
        /// <summary>El aviso, o null. Nunca lanza: si algo falla, sin aviso (y a ELMAH).</summary>
        Task<AvisoPedidoDTO> Aviso(string empresa, int pedido, string cliente);
    }

    /// <summary>NestoAPI#808: lee el cheque vigente del cliente, las líneas del pedido y sus productos, y aplica <see cref="AvisoChequeRegaloSinUsar"/>.</summary>
    public class AvisadorChequeRegaloSinUsar : IAvisadorChequeRegaloSinUsar
    {
        private readonly IServicioCanjeChequesRegalo servicio;
        private readonly IRepositorioCanjeChequesRegalo repositorio;
        private readonly Func<string, int, Task<List<LinPedidoVta>>> leerLineas;
        private readonly Func<DateTime> ahora;

        public AvisadorChequeRegaloSinUsar(IServicioCanjeChequesRegalo servicio, IRepositorioCanjeChequesRegalo repositorio,
            Func<string, int, Task<List<LinPedidoVta>>> leerLineas, Func<DateTime> ahora = null)
        {
            this.servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.leerLineas = leerLineas ?? throw new ArgumentNullException(nameof(leerLineas));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        public async Task<AvisoPedidoDTO> Aviso(string empresa, int pedido, string cliente)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(cliente))
                {
                    return null;
                }
                // Los cheques son de la empresa de la campaña (la 1), como GET api/ChequesRegalo/Cliente por defecto.
                ChequeRegaloClienteDTO cheque = await servicio.LeerChequeVigente(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente.Trim(), ahora())
                    .ConfigureAwait(false);
                if (cheque == null || !cheque.SePuedeUsar)
                {
                    return null;
                }
                string empresaPedido = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
                List<LinPedidoVta> lineas = await leerLineas(empresaPedido, pedido).ConfigureAwait(false) ?? new List<LinPedidoVta>();
                List<string> productosLineas = lineas
                    .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim()).Distinct().ToList();
                if (!productosLineas.Any())
                {
                    return null;
                }
                Dictionary<string, ProductoParaChequeRegalo> productos = await repositorio.LeerProductos(empresaPedido, productosLineas)
                    .ConfigureAwait(false);
                List<string> productosCheque = await servicio.ProductosCheque().ConfigureAwait(false);
                return AvisoChequeRegaloSinUsar.Evaluar(cheque, lineas, productos, productosCheque);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"NestoAPI#808: no se ha podido comprobar si el cliente {cliente?.Trim()} del pedido {pedido} " +
                    $"tiene un cheque regalo sin usar: {ex.Message}", ex));
                return null;
            }
        }
    }
}
