using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Cupones
{
    /// <summary>Lo que hay que hacer con los cupones al guardar el pedido (dentro de la misma transacción).</summary>
    public class PlanCanjeCupon
    {
        public string EmpresaPedido { get; set; }
        public int Pedido { get; set; }
        public string Usuario { get; set; }
        public string Canal { get; set; }
        /// <summary>El cupón que se reserva para este pedido (null si el pedido no lleva cupón).</summary>
        public CuponLeido CuponAReservar { get; set; }
        /// <summary>Soltar los cupones que estuvieran en este pedido y ya no le tocan (se quitó la línea o se cambió de cupón).</summary>
        public bool LiberarOtros { get; set; }

        public bool HayQueTocarCupones => CuponAReservar != null || LiberarOtros;

        internal static readonly PlanCanjeCupon Nada = new PlanCanjeCupon();
    }

    public interface IServicioCanjeCupones
    {
        /// <summary>
        /// Normaliza en el DTO las líneas de cupón (las que traen CodigoCupon o el producto del cupón) antes de crear las
        /// líneas y de validar el pedido. 400 si el código no existe o si vienen dos códigos.
        /// </summary>
        Task NormalizarLineas(PedidoVentaDTO pedido);
        /// <summary>
        /// Las reglas duras sobre las líneas que van a quedar en el pedido (ReglasCupon). Lanza NestoBusinessException (400 con
        /// código propio) si no se cumplen. <paramref name="teniaLineaCupon"/>: el pedido guardado tenía (o tiene) línea de
        /// cupón, así que puede haber uno que soltar.
        /// </summary>
        Task<PlanCanjeCupon> Comprobar(string empresa, int pedido, string cliente, IList<LinPedidoVta> lineas, string ivaCabecera,
            Action<LinPedidoVta> recalcularImportes, string usuario, bool pedidoNuevo, bool teniaLineaCupon);
        Task Reservar(PlanCanjeCupon plan);
        Task LiberarOtros(PlanCanjeCupon plan);
        Task LiberarPedido(int pedido, string usuario);
        string ProductoCupon { get; }
    }

    /// <summary>
    /// NestoAPI#613: el uso del cupón en los pedidos (POST y PUT de api/PedidosVenta, y de ahí TNV), como regla dura FUERA del
    /// pipeline de validación (que se puede saltar), igual que el cheque regalo (#593). El cupón queda Usado en la misma
    /// transacción que el pedido (UPDATE condicionado: dos pedidos a la vez con el mismo cupón, solo entra uno); si se quita la
    /// línea o se borra el pedido antes de servirlo, vuelve a Activo. Un pedido que ya tenía el cupón lo conserva aunque se
    /// modifique después de la caducidad; el mínimo se mira en cada guardado.
    /// </summary>
    public class ServicioCanjeCupones : IServicioCanjeCupones
    {
        private readonly IRepositorioCupones repositorio;
        private readonly Func<DateTime> ahora;
        private readonly List<string> codigosPedidos = new List<string>();
        private readonly Func<bool> usoEnTiendaEncendido;

        /// <param name="usoEnTiendaEncendido">NestoAPI#614: el espejo en PrestaShop (Cupones:EspejoPrestashop). Null: el del Web.config.</param>
        public ServicioCanjeCupones(IRepositorioCupones repositorio, Func<DateTime> ahora = null, string productoCupon = null,
            Func<bool> usoEnTiendaEncendido = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
            ProductoCupon = productoCupon ?? ReglasCupon.ProductoCupon;
            this.usoEnTiendaEncendido = usoEnTiendaEncendido ?? (() => ReglasEspejoPrestashop.Encendido);
        }

        public string ProductoCupon { get; }

        public async Task NormalizarLineas(PedidoVentaDTO pedido)
        {
            if (pedido?.Lineas == null || !pedido.Lineas.Any())
            {
                return;
            }
            List<LineaPedidoVentaDTO> lineasCupon = pedido.Lineas
                .Where(l => !string.IsNullOrWhiteSpace(l.CodigoCupon) || ReglasCupon.EsLineaCupon(l.tipoLinea, l.Producto, ProductoCupon))
                .ToList();
            if (!lineasCupon.Any())
            {
                return; // lo normal: ni una consulta más
            }
            List<string> codigos = lineasCupon.Where(l => !string.IsNullOrWhiteSpace(l.CodigoCupon))
                .Select(l => l.CodigoCupon.Trim().ToUpperInvariant()).Distinct().ToList();
            if (codigos.Count > 1 || lineasCupon.Count > 1)
            {
                throw ReglasCupon.Error(ReglasCupon.ERROR_VARIOS, "Solo se puede aplicar un cupón por pedido.", pedido.empresa, pedido.numero, pedido.cliente);
            }
            CuponLeido cupon = null;
            if (codigos.Any())
            {
                cupon = await repositorio.LeerPorCodigo(codigos[0]).ConfigureAwait(false)
                    ?? throw ReglasCupon.Error(ReglasCupon.ERROR_NO_EXISTE, $"No existe el cupón {codigos[0]}.", pedido.empresa, pedido.numero, pedido.cliente);
                codigosPedidos.Clear();
                codigosPedidos.Add(cupon.Codigo.Trim());
            }
            Dictionary<string, ProductoParaCupon> productos = await repositorio.LeerProductos(pedido.empresa, new[] { ProductoCupon }).ConfigureAwait(false);
            string ivaProducto = ReglasCupon.Buscar(productos, ProductoCupon)?.IvaRepercutido;
            PorcentajesIvaCupon porcentajes = cupon == null ? null : await Porcentajes(pedido.empresa, pedido.iva, ivaProducto).ConfigureAwait(false);
            foreach (LineaPedidoVentaDTO linea in lineasCupon)
            {
                ReglasCupon.Normalizar(linea, cupon, ProductoCupon, ivaProducto, pedido.iva != null, porcentajes);
            }
        }

        public async Task<PlanCanjeCupon> Comprobar(string empresa, int pedido, string cliente, IList<LinPedidoVta> lineas, string ivaCabecera,
            Action<LinPedidoVta> recalcularImportes, string usuario, bool pedidoNuevo, bool teniaLineaCupon)
        {
            List<LinPedidoVta> lineasCupon = (lineas ?? new List<LinPedidoVta>())
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && ReglasCupon.EsProductoCupon(l.Producto, ProductoCupon))
                .ToList();
            if (!lineasCupon.Any())
            {
                return pedidoNuevo || !teniaLineaCupon
                    ? PlanCanjeCupon.Nada
                    : new PlanCanjeCupon { EmpresaPedido = empresa?.Trim(), Pedido = pedido, Usuario = usuario, LiberarOtros = true };
            }
            if (lineasCupon.Count > 1)
            {
                throw ReglasCupon.Error(ReglasCupon.ERROR_VARIOS, "Solo se puede aplicar un cupón por pedido.", empresa, pedido, cliente);
            }

            LinPedidoVta lineaCupon = lineasCupon[0];
            if (!codigosPedidos.Any() && Constantes.FormasVenta.EsCanalExterno(lineaCupon.Forma_Venta?.Trim()) && usoEnTiendaEncendido())
            {
                // NestoAPI#614: cupón aplicado en la tienda PrestaShop (CanalesExternos). Lo cobró la tienda con sus reglas y su
                // importe: la línea se deja como viene (ni precio ni mínimo) y el cupón se marca con POST api/Cupones/{codigo}/Usar.
                return PlanCanjeCupon.Nada;
            }
            CuponLeido cupon = codigosPedidos.Any()
                ? await repositorio.LeerPorCodigo(codigosPedidos[0]).ConfigureAwait(false)
                : pedidoNuevo ? null : await repositorio.LeerDelPedido(pedido).ConfigureAwait(false);
            if (cupon == null)
            {
                throw ReglasCupon.Error(ReglasCupon.ERROR_SIN_CODIGO,
                    $"Indique el código del cupón en la línea {ProductoCupon} (CodigoCupon) o quítela.", empresa, pedido, cliente);
            }

            bool yaEnEstePedido = !pedidoNuevo && cupon.Estado?.Trim() == EstadosCupon.USADO && cupon.UsadoEnPedido == pedido;
            Dictionary<string, ProductoParaCupon> productos = await repositorio.LeerProductos(empresa,
                lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim()).Concat(new[] { ProductoCupon }).Distinct()).ConfigureAwait(false);

            string ivaProductoCupon = ReglasCupon.Buscar(productos, ProductoCupon)?.IvaRepercutido;
            PorcentajesIvaCupon porcentajes = await Porcentajes(empresa, ivaCabecera, ivaProductoCupon).ConfigureAwait(false);
            if (ReglasCupon.Normalizar(lineaCupon, cupon, ivaProductoCupon, !string.IsNullOrWhiteSpace(ivaCabecera), porcentajes))
            {
                recalcularImportes?.Invoke(lineaCupon);
            }

            decimal totalComputable = ReglasCupon.TotalComputable(lineas, productos);
            ReglasCupon.Comprobar(cupon, empresa, pedido, cliente, ahora(), yaEnEstePedido, totalComputable);

            return new PlanCanjeCupon
            {
                EmpresaPedido = empresa?.Trim(),
                Pedido = pedido,
                Usuario = usuario,
                Canal = lineaCupon.Forma_Venta?.Trim(),
                CuponAReservar = cupon,
                LiberarOtros = !pedidoNuevo
            };
        }

        /// <summary>
        /// Carlos (15/10/26): el cupón descuenta siempre su importe exacto, también con recargo de equivalencia: la base sale del
        /// IVA y el RE del cliente (ParametrosIVA). Pedido sin IVA: base = importe. Sin la combinación en ParametrosIVA: 21 %.
        /// </summary>
        private async Task<PorcentajesIvaCupon> Porcentajes(string empresa, string ivaCabecera, string ivaProducto)
        {
            if (string.IsNullOrWhiteSpace(ivaCabecera))
            {
                return PorcentajesIvaCupon.SinIva;
            }
            if (string.IsNullOrWhiteSpace(ivaProducto))
            {
                return PorcentajesIvaCupon.PorDefecto;
            }
            return await repositorio.LeerPorcentajesIva(empresa, ivaCabecera, ivaProducto).ConfigureAwait(false) ?? PorcentajesIvaCupon.PorDefecto;
        }

        public async Task Reservar(PlanCanjeCupon plan)
        {
            if (plan?.CuponAReservar == null)
            {
                return;
            }
            if (!await repositorio.Reservar(plan.CuponAReservar.Id, plan.EmpresaPedido, plan.Pedido, plan.Canal, plan.Usuario).ConfigureAwait(false))
            {
                throw ReglasCupon.Error(ReglasCupon.ERROR_YA_USADO,
                    $"El cupón {plan.CuponAReservar.Codigo?.Trim()} se acaba de aplicar en otro pedido (o ya no vale): solo se puede usar una vez.",
                    plan.EmpresaPedido, plan.Pedido, plan.CuponAReservar.Cliente);
            }
        }

        public async Task LiberarOtros(PlanCanjeCupon plan)
        {
            if (plan == null || !plan.LiberarOtros)
            {
                return;
            }
            _ = await repositorio.LiberarDelPedido(plan.Pedido, plan.CuponAReservar?.Id ?? 0, plan.Usuario).ConfigureAwait(false);
        }

        public async Task LiberarPedido(int pedido, string usuario) =>
            _ = await repositorio.LiberarDelPedido(pedido, 0, usuario).ConfigureAwait(false);
    }
}
