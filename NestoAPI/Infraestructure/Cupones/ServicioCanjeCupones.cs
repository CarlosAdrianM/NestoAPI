using NestoAPI.Infraestructure.Exceptions;
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
        /// <summary>
        /// NestoAPI#694: ¿puede el cliente usar su cupón con este carrito de la tienda PrestaShop? Con las mismas reglas que
        /// <see cref="Comprobar"/> (mínimo sin cursos ni portes, estado, fechas, del cliente). No reserva nada.
        /// </summary>
        Task<ComprobacionCarritoCuponDTO> ComprobarCarritoTienda(ComprobarCarritoCuponDTO carrito);
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
        private readonly Func<Task<List<string>>> productosCheque;

        /// <param name="usoEnTiendaEncendido">NestoAPI#614: el espejo en PrestaShop (Cupones:EspejoPrestashop). Null: el del Web.config.</param>
        /// <param name="productosCheque">
        /// NestoAPI#696: los productos de cheque regalo de las campañas (<c>IServicioCanjeChequesRegalo.ProductosCheque</c>), para
        /// rechazar el pedido que lleva cheque y cupón (no son acumulables). Null: no se mira (el carrito de la tienda, que no trae
        /// el cheque, y los tests que no van de esto).
        /// </param>
        public ServicioCanjeCupones(IRepositorioCupones repositorio, Func<DateTime> ahora = null, string productoCupon = null,
            Func<bool> usoEnTiendaEncendido = null, Func<Task<List<string>>> productosCheque = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
            ProductoCupon = productoCupon ?? ReglasCupon.ProductoCupon;
            this.usoEnTiendaEncendido = usoEnTiendaEncendido ?? (() => ReglasEspejoPrestashop.Encendido);
            this.productosCheque = productosCheque;
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
            // NestoAPI#696 (Carlos, 22/10/26): cheque regalo y cupón NO son acumulables. Regla dura, también para el pedido de la
            // tienda que importa Nesto#518: no entra en silencio, lo ve quien importa y lo resuelve Administración.
            await ComprobarSinChequeRegalo(lineas, empresa, pedido, cliente).ConfigureAwait(false);
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

            // NestoAPI#694: el cupón se gastó en la tienda PrestaShop (módulo nestocheques) y este es su pedido, que entra por
            // CanalesExternos: no se vuelve a canjear (ni estado ni fechas) ni se mira el mínimo, que ya miró la tienda al cobrarlo.
            bool gastadoEnLaTienda = Constantes.FormasVenta.EsCanalExterno(lineaCupon.Forma_Venta?.Trim()) && ReglasCupon.GastadoEnLaTienda(cupon, pedido);
            bool yaEnEstePedido = gastadoEnLaTienda || (!pedidoNuevo && cupon.Estado?.Trim() == EstadosCupon.USADO && cupon.UsadoEnPedido == pedido);
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
            ReglasCupon.Comprobar(cupon, empresa, pedido, cliente, ahora(), yaEnEstePedido, totalComputable, comprobarMinimo: !gastadoEnLaTienda);

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
        /// NestoAPI#694: el carrito de la tienda (módulo nestocheques) se monta como lo grabará Nesto al importarlo (sus productos con
        /// el IVA y el recargo del cliente, más la línea del cupón con su código) y pasa por el MISMO <see cref="Comprobar"/> que
        /// POST/PUT de api/PedidosVenta y TNV. Sin código, el cupón del cliente que elige <see cref="ReglasCupon.ElegirParaPedido"/>.
        /// </summary>
        public async Task<ComprobacionCarritoCuponDTO> ComprobarCarritoTienda(ComprobarCarritoCuponDTO carrito)
        {
            string empresa = string.IsNullOrWhiteSpace(carrito?.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : carrito.Empresa.Trim();
            string cliente = carrito?.Cliente?.Trim();
            DateTime hoy = ahora();

            string codigo = carrito?.Codigo?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(codigo))
            {
                List<CuponDTO> delCliente = ((await repositorio.LeerDelCliente(empresa, cliente).ConfigureAwait(false)) ?? new List<CuponLeido>())
                    .Select(c => ReglasCupon.ADTO(c, hoy, ProductoCupon)).ToList();
                codigo = ReglasCupon.ElegirParaPedido(delCliente)?.Codigo;
                if (codigo == null)
                {
                    return CarritoCupones.SinCupon();
                }
            }
            CuponLeido cupon = await repositorio.LeerPorCodigo(codigo).ConfigureAwait(false);
            if (cupon == null)
            {
                return CarritoCupones.NoExiste(codigo);
            }

            string ivaCliente = await repositorio.LeerIvaCliente(empresa, cliente).ConfigureAwait(false);
            List<LineaCarritoCuponDTO> lineasCarrito = (carrito?.Lineas ?? new List<LineaCarritoCuponDTO>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.Producto) && !ReglasCupon.EsProductoCupon(l.Producto, ProductoCupon))
                .ToList();
            Dictionary<string, ProductoParaCupon> productos = await repositorio.LeerProductos(empresa,
                lineasCarrito.Select(l => l.Producto.Trim()).Concat(new[] { ProductoCupon }).Distinct()).ConfigureAwait(false);

            // El mínimo es IVA incluido y la tienda manda la base: cada línea con el IVA (y el recargo) del cliente y de su producto
            var porcentajes = new Dictionary<string, PorcentajesIvaCupon>(StringComparer.OrdinalIgnoreCase);
            var lineas = new List<LinPedidoVta>();
            foreach (LineaCarritoCuponDTO l in lineasCarrito)
            {
                string ivaProducto = ReglasCupon.Buscar(productos, l.Producto)?.IvaRepercutido;
                string clave = ivaProducto ?? string.Empty;
                if (!porcentajes.TryGetValue(clave, out PorcentajesIvaCupon p))
                {
                    p = await Porcentajes(empresa, ivaCliente, ivaProducto).ConfigureAwait(false);
                    porcentajes[clave] = p;
                }
                lineas.Add(new LinPedidoVta
                {
                    Empresa = empresa,
                    TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                    Producto = l.Producto.Trim(),
                    Cantidad = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, l.Cantidad)),
                    Base_Imponible = l.BaseImponible,
                    Total = Math.Round(l.BaseImponible * (1 + (p.Iva + p.RecargoEquivalencia) / 100M), 2, MidpointRounding.AwayFromZero),
                    IVA = ivaProducto,
                    Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                    Picking = 0
                });
            }
            var lineaCupon = new LinPedidoVta
            {
                Empresa = empresa,
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = ProductoCupon,
                Cantidad = -1,
                Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                Picking = 0
            };
            lineas.Add(lineaCupon);

            // Comprobar normaliza la línea del cupón (precio = importe / (1 + IVA + RE) del cliente) antes de mirar las reglas
            codigosPedidos.Clear();
            codigosPedidos.Add(cupon.Codigo.Trim());
            try
            {
                _ = await Comprobar(empresa, 0, cliente, lineas, ivaCliente, null, CarritoCupones.USUARIO_TIENDA,
                    pedidoNuevo: true, teniaLineaCupon: false).ConfigureAwait(false);
            }
            catch (NestoBusinessException ex) when (CarritoCupones.EsErrorDelCupon(ex))
            {
                return CarritoCupones.NoAplicable(cupon, BaseCupon(lineaCupon), ex);
            }
            finally
            {
                codigosPedidos.Clear();
            }
            return CarritoCupones.Aplicable(cupon, BaseCupon(lineaCupon));
        }

        private static decimal BaseCupon(LinPedidoVta lineaCupon) => Math.Round(lineaCupon.Precio ?? 0, 2, MidpointRounding.AwayFromZero);

        /// <summary>NestoAPI#696: 400 CUPON_CON_CHEQUE_REGALO si entre las líneas hay un producto de cheque regalo.</summary>
        private async Task ComprobarSinChequeRegalo(IList<LinPedidoVta> lineas, string empresa, int pedido, string cliente)
        {
            if (productosCheque == null)
            {
                return;
            }
            List<string> cheques = await productosCheque().ConfigureAwait(false);
            if (ReglasCupon.LlevaChequeRegalo(lineas, cheques))
            {
                throw ReglasCupon.Error(ReglasCupon.ERROR_CON_CHEQUE_REGALO, ReglasCupon.MensajeConChequeRegalo(codigosPedidos.FirstOrDefault()),
                    empresa, pedido, cliente);
            }
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
