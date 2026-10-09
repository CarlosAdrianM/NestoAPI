using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#593 (c4): la campaña vista desde el canje. Además de lo de c2, lo que NO suma para el mínimo del pedido
    /// que canjea (columnas nuevas de ChequesRegaloCampanas, para no programar otra vez en febrero). Las listas van
    /// separadas por punto y coma.
    /// </summary>
    public class CampanaCanjeChequeRegalo : CampanaChequeRegalo
    {
        /// <summary>Prefijos del NOMBRE del producto que no suman para el mínimo («PACK 26»).</summary>
        public string PrefijosNombreExcluidosMinimo { get; set; }
        /// <summary>Grupos de producto (de la ficha) que no suman para el mínimo («PEL»).</summary>
        public string GruposExcluidosMinimo { get; set; }

        // Métodos y no propiedades: SqlQuery exige una columna por cada propiedad
        public List<string> Prefijos() => Partir(PrefijosNombreExcluidosMinimo);
        public List<string> Grupos() => Partir(GruposExcluidosMinimo);

        internal static List<string> Partir(string lista)
            => (lista ?? string.Empty).Split(';')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();
    }

    /// <summary>Un cheque de un cliente, con lo que hace falta para decidir si se puede usar en un pedido.</summary>
    public class ChequeRegaloCliente
    {
        public int Id { get; set; }
        public string Campana { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string EmpresaFactura { get; set; }
        public string FacturaOrigen { get; set; }
        public DateTime? FechaFactura { get; set; }
        public DateTime FechaGeneracion { get; set; }
        public DateTime? FechaActivacion { get; set; }
        /// <summary>Generado (libre), Canjeado (aplicado en PedidoCanje), Anulado o Caducado.</summary>
        public string Estado { get; set; }
        public string EmpresaCanje { get; set; }
        public int? PedidoCanje { get; set; }
        public DateTime? FechaCanje { get; set; }
        /// <summary>El pedido del canje todavía tiene la línea del cheque. Si no (se borró fuera de la API), el cheque
        /// está libre aunque ponga Canjeado: se cura solo al volver a usarlo.</summary>
        public bool PedidoCanjeTieneLaLinea { get; set; }
        /// <summary>La línea del cheque del pedido del canje ya está en albarán o factura.</summary>
        public bool LineaChequeServida { get; set; }
        /// <summary>El pedido que se está guardando es uno de los de la factura que generó el cheque.</summary>
        public bool EsPedidoDeLaFacturaOrigen { get; set; }
    }

    /// <summary>Lo que hace falta de la ficha del producto para saber si suma para el mínimo.</summary>
    public class ProductoParaChequeRegalo
    {
        public string Numero { get; set; }
        public string Nombre { get; set; }
        public string Grupo { get; set; }
        public bool Ficticio { get; set; }
        public string IvaRepercutido { get; set; }
    }

    /// <summary>
    /// NestoAPI#593 (c4): lo que devuelve GET api/ChequesRegalo/Cliente para que Nesto y NestoApp avisen en la
    /// plantilla y sepan cómo mandar la línea.
    /// </summary>
    public class ChequeRegaloClienteDTO
    {
        public string Campana { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        /// <summary>El producto de la línea del cheque (CHEQUE50_OCT26).</summary>
        public string Producto { get; set; }
        /// <summary>Base imponible del cheque (50): la línea va con cantidad −1 y este precio, más el IVA del producto.</summary>
        public decimal Importe { get; set; }
        /// <summary>El pedido tiene que SUPERAR esta base de producto computable (250,00 no vale).</summary>
        public decimal MinimoCanje { get; set; }
        /// <summary>Último día (incluido) para meter el cheque en un pedido.</summary>
        public DateTime CanjeHasta { get; set; }
        public List<string> PrefijosNombreExcluidosMinimo { get; set; }
        public List<string> GruposExcluidosMinimo { get; set; }
        /// <summary>Disponible, EnPedido, Canjeado, PendienteDeActivar, Caducado o Anulado.</summary>
        public string Estado { get; set; }
        public bool SePuedeUsar { get; set; }
        /// <summary>Frase para el usuario.</summary>
        public string Mensaje { get; set; }
        public string EmpresaFactura { get; set; }
        public string FacturaOrigen { get; set; }
        public DateTime? FechaFactura { get; set; }
        public DateTime? FechaActivacion { get; set; }
        public string EmpresaPedidoCanje { get; set; }
        public int? PedidoCanje { get; set; }
        public DateTime? FechaCanje { get; set; }
        /// <summary>El texto que pone el servidor en la línea.</summary>
        public string TextoLinea { get; set; }
    }

    /// <summary>Lo que hay que hacer con los cheques al guardar el pedido (dentro de la misma transacción).</summary>
    public class PlanCanjeChequeRegalo
    {
        public string EmpresaPedido { get; set; }
        public int Pedido { get; set; }
        public string Usuario { get; set; }
        /// <summary>El cheque que se reserva para este pedido (null si el pedido no lleva cheque).</summary>
        public ChequeRegaloCliente ChequeAReservar { get; set; }
        /// <summary>Soltar los cheques que estuvieran en este pedido y ya no le tocan (se borró la línea o se cambió
        /// de cliente). Solo en pedidos que ya existían.</summary>
        public bool LiberarOtros { get; set; }

        public bool HayQueTocarCheques => ChequeAReservar != null || LiberarOtros;

        internal static readonly PlanCanjeChequeRegalo Nada = new PlanCanjeChequeRegalo();
    }

    /// <summary>
    /// NestoAPI#593 (c4): las reglas del canje, sin base de datos.
    /// </summary>
    public static class ReglasCanjeChequeRegalo
    {
        public const string ESTADO_GENERADO = "Generado";
        public const string ESTADO_CANJEADO = "Canjeado";
        public const string ESTADO_ANULADO = "Anulado";
        public const string ESTADO_CADUCADO = "Caducado";

        public const string ERROR_SIN_CHEQUE = "CHEQUE_REGALO_SIN_CHEQUE";
        public const string ERROR_OTRA_CAMPANA = "CHEQUE_REGALO_OTRA_CAMPANA";
        public const string ERROR_YA_USADO = "CHEQUE_REGALO_YA_USADO";
        public const string ERROR_FUERA_DE_PLAZO = "CHEQUE_REGALO_FUERA_DE_PLAZO";
        public const string ERROR_CAMPANA_INACTIVA = "CHEQUE_REGALO_CAMPANA_INACTIVA";
        public const string ERROR_ANULADO = "CHEQUE_REGALO_ANULADO";
        public const string ERROR_NO_ACTIVADO = "CHEQUE_REGALO_NO_ACTIVADO";
        public const string ERROR_MISMO_PEDIDO = "CHEQUE_REGALO_MISMO_PEDIDO";
        public const string ERROR_MINIMO_NO_SUPERADO = "CHEQUE_REGALO_MINIMO_NO_SUPERADO";
        public const string ERROR_VARIAS_LINEAS = "CHEQUE_REGALO_VARIAS_LINEAS";

        private static readonly CultureInfo es = CultureInfo.GetCultureInfo("es-ES");

        public static CampanaCanjeChequeRegalo CampanaDelProducto(IEnumerable<CampanaCanjeChequeRegalo> campanas, string producto)
            => string.IsNullOrWhiteSpace(producto) ? null
                : campanas?.FirstOrDefault(c => string.Equals(c.Producto?.Trim(), producto.Trim(), StringComparison.OrdinalIgnoreCase));

        public static bool EsLineaCheque(byte? tipoLinea, string producto, IEnumerable<CampanaCanjeChequeRegalo> campanas)
            => tipoLinea == Constantes.TiposLineaVenta.PRODUCTO && CampanaDelProducto(campanas, producto) != null;

        public static string TextoLinea(CampanaChequeRegalo campana)
            => $"Cheque regalo {campana.ImporteBase.ToString("0.##", es)} € (campaña {campana.Codigo})";

        /// <summary>
        /// La línea del cheque la pone el servidor, venga como venga (NestoApp puede mandar cantidad +1 o +50): cantidad
        /// −1, precio = importe de la campaña, sin descuentos de línea, de producto ni de cliente (Aplicar_Dto falso), el
        /// IVA del producto y el texto de la campaña. El pronto pago de la cabecera sí le aplica, como a toda la factura.
        /// </summary>
        public static void Normalizar(LineaPedidoVentaDTO linea, CampanaChequeRegalo campana, string ivaProducto, bool pedidoConIva)
        {
            linea.tipoLinea = Constantes.TiposLineaVenta.PRODUCTO;
            linea.Producto = campana.Producto.Trim();
            linea.Cantidad = -1;
            linea.PrecioUnitario = campana.ImporteBase;
            linea.DescuentoLinea = 0;
            linea.DescuentoProducto = 0;
            linea.DescuentoEntidad = 0;
            linea.AplicarDescuento = false;
            linea.oferta = null;
            linea.texto = TextoLinea(campana);
            linea.EsFicticio = true;
            if (pedidoConIva && !string.IsNullOrWhiteSpace(ivaProducto))
            {
                linea.iva = ivaProducto.Trim();
            }
        }

        /// <summary>Lo mismo sobre la línea ya creada. Devuelve si ha cambiado algo (hay que recalcular importes).
        /// Solo se toca la línea mientras se puede modificar (sin picking ni albarán).</summary>
        public static bool Normalizar(LinPedidoVta linea, CampanaChequeRegalo campana, string ivaProducto, bool pedidoConIva)
        {
            if ((linea.Picking ?? 0) != 0 || linea.Estado > Constantes.EstadosLineaVenta.EN_CURSO)
            {
                return false;
            }
            bool cambia = false;
            string texto = TextoLinea(campana);
            if (linea.Cantidad != -1) { linea.Cantidad = -1; cambia = true; }
            if (linea.Precio != campana.ImporteBase) { linea.Precio = campana.ImporteBase; cambia = true; }
            if (linea.Descuento != 0) { linea.Descuento = 0; cambia = true; }
            if (linea.DescuentoProducto != 0) { linea.DescuentoProducto = 0; cambia = true; }
            if (linea.Aplicar_Dto) { linea.Aplicar_Dto = false; cambia = true; }
            if (linea.NºOferta != null) { linea.NºOferta = null; cambia = true; }
            if (linea.Texto?.Trim() != texto) { linea.Texto = texto; cambia = true; }
            if (pedidoConIva && !string.IsNullOrWhiteSpace(ivaProducto) && linea.IVA?.Trim() != ivaProducto.Trim())
            {
                linea.IVA = ivaProducto.Trim();
                cambia = true;
            }
            return cambia;
        }

        /// <summary>
        /// ¿Suma para el mínimo del pedido que canjea? Solo líneas de producto (no cuentas contables: portes, reembolso,
        /// cuotas, reparaciones; ni texto ni inmovilizado), que no sean ficticias (el propio cheque tampoco), cuyo nombre
        /// no empiece por uno de los prefijos de la campaña («PACK 26») y cuyo grupo no esté excluido («PEL»). Los
        /// excluidos pueden ir en el pedido y llevarse el descuento: solo no suman. Cursos sí suman.
        /// </summary>
        public static bool ComputaParaMinimo(byte? tipoLinea, ProductoParaChequeRegalo producto, CampanaCanjeChequeRegalo campana)
        {
            if (tipoLinea != Constantes.TiposLineaVenta.PRODUCTO || producto == null || producto.Ficticio)
            {
                return false;
            }
            string nombre = producto.Nombre?.Trim() ?? string.Empty;
            if (campana.Prefijos().Any(p => nombre.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            string grupo = producto.Grupo?.Trim() ?? string.Empty;
            return !campana.Grupos().Any(g => string.Equals(g, grupo, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Base imponible (después de descuentos) de las líneas que suman para el mínimo.</summary>
        public static decimal BaseComputable(IEnumerable<LinPedidoVta> lineas, IDictionary<string, ProductoParaChequeRegalo> productos,
            CampanaCanjeChequeRegalo campana)
            => lineas.Where(l => ComputaParaMinimo(l.TipoLinea, Buscar(productos, l.Producto), campana)).Sum(l => l.Base_Imponible);

        internal static ProductoParaChequeRegalo Buscar(IDictionary<string, ProductoParaChequeRegalo> productos, string producto)
            => producto != null && productos != null && productos.TryGetValue(producto.Trim(), out ProductoParaChequeRegalo p) ? p : null;

        public static bool SuperaElMinimo(decimal baseComputable, CampanaChequeRegalo campana) => baseComputable > campana.MinimoCanje;

        /// <summary>Estado para el cliente (endpoint de consulta).</summary>
        public static string EstadoParaCliente(ChequeRegaloCliente cheque, CampanaChequeRegalo campana, DateTime ahora)
        {
            if (cheque.Estado == ESTADO_ANULADO)
            {
                return "Anulado";
            }
            if (cheque.Estado == ESTADO_CANJEADO && cheque.PedidoCanje != null && cheque.PedidoCanjeTieneLaLinea)
            {
                return cheque.LineaChequeServida ? "Canjeado" : "EnPedido";
            }
            if (cheque.Estado == ESTADO_CADUCADO || ahora.Date > campana.CanjeHasta.Date)
            {
                return "Caducado";
            }
            if (cheque.FechaActivacion == null || cheque.FechaActivacion > ahora)
            {
                return "PendienteDeActivar";
            }
            return "Disponible";
        }

        public static string MensajeParaCliente(string estado, ChequeRegaloCliente cheque, CampanaCanjeChequeRegalo campana)
        {
            switch (estado)
            {
                case "Disponible":
                    return $"Tiene un cheque regalo de {Euros(campana.ImporteBase)} € + IVA para un pedido de más de " +
                        $"{Euros(campana.MinimoCanje)} € de producto{TextoExclusiones(campana)}, hasta el {campana.CanjeHasta.ToString("dd/MM/yyyy", es)}.";
                case "EnPedido":
                    return $"El cheque regalo ya está aplicado en el pedido {cheque.PedidoCanje}.";
                case "Canjeado":
                    return $"El cheque regalo ya se usó en el pedido {cheque.PedidoCanje}.";
                case "Caducado":
                    return $"El cheque regalo se podía usar hasta el {campana.CanjeHasta.ToString("dd/MM/yyyy", es)}.";
                case "PendienteDeActivar":
                    return cheque.FechaActivacion != null
                        ? $"El cheque regalo se podrá usar a partir del {cheque.FechaActivacion.Value.ToString("dd/MM/yyyy", es)}."
                        : "El cheque regalo todavía no se puede usar: se activa cuando se entregue entero el pedido que lo generó.";
                default:
                    return "El cheque regalo está anulado.";
            }
        }

        internal static string TextoExclusiones(CampanaCanjeChequeRegalo campana)
        {
            var partes = new List<string> { "portes y demás cuentas contables", "productos ficticios" };
            partes.AddRange(campana.Prefijos().Select(p => $"«{p}»"));
            partes.AddRange(campana.Grupos().Select(g => $"grupo {g}"));
            return " (base imponible, sin " + string.Join(", ", partes.Take(partes.Count - 1)) + " ni " + partes.Last() + ")";
        }

        internal static string Euros(decimal importe) => importe.ToString("N2", es);

        internal static NestoBusinessException Error(string codigo, string mensaje, string empresa, int pedido, string cliente,
            Dictionary<string, object> datos = null)
        {
            var contexto = new ErrorContext
            {
                ErrorCode = codigo,
                Empresa = empresa?.Trim(),
                Pedido = pedido == 0 ? (int?)null : pedido,
                Cliente = cliente?.Trim()
            };
            if (datos != null)
            {
                foreach (KeyValuePair<string, object> dato in datos)
                {
                    contexto.AdditionalData[dato.Key] = dato.Value;
                }
            }
            return new NestoBusinessException(mensaje, contexto);
        }
    }

    public interface IRepositorioCanjeChequesRegalo
    {
        /// <summary>Todas las campañas (activas o no): sus productos son líneas de cheque aunque la campaña esté apagada.</summary>
        Task<List<CampanaCanjeChequeRegalo>> LeerCampanas();
        /// <summary>Los cheques del cliente. <paramref name="pedido"/> = el que se está guardando (0 si ninguno), para
        /// saber si es el de la factura que generó el cheque.</summary>
        Task<List<ChequeRegaloCliente>> LeerChequesDelCliente(string empresa, string cliente, int pedido);
        Task<Dictionary<string, ProductoParaChequeRegalo>> LeerProductos(string empresa, IEnumerable<string> productos);
        /// <summary>UPDATE condicionado: false si el cheque ya está en otro pedido vivo (lo ha cogido otro a la vez).</summary>
        Task<bool> ReservarCheque(int id, string empresaPedido, int pedido, string usuario);
        /// <summary>Suelta los cheques reservados en el pedido salvo <paramref name="exceptoId"/> (0 = todos), si su línea
        /// no está ya servida.</summary>
        Task<int> LiberarChequesDelPedido(int pedido, int exceptoId, string usuario);
    }

    public interface IServicioCanjeChequesRegalo
    {
        /// <summary>Normaliza en el DTO las líneas de cheque (antes de crear las líneas y de validar el pedido).</summary>
        Task NormalizarLineas(PedidoVentaDTO pedido);
        /// <summary>Productos de cheque de todas las campañas (para saber si una línea es de cheque).</summary>
        Task<List<string>> ProductosCheque();
        /// <summary>
        /// Las reglas duras del canje sobre las líneas que van a quedar en el pedido. Lanza NestoBusinessException
        /// (400 con código propio) si no se cumplen. Devuelve lo que hay que hacer al guardar.
        /// </summary>
        Task<PlanCanjeChequeRegalo> Comprobar(string empresa, int pedido, string cliente, IList<LinPedidoVta> lineas,
            string ivaCabecera, Action<LinPedidoVta> recalcularImportes, string usuario, bool pedidoNuevo);
        /// <summary>Antes de guardar, en la misma transacción: reserva el cheque (lanza CHEQUE_REGALO_YA_USADO si otro
        /// pedido lo ha cogido a la vez).</summary>
        Task Reservar(PlanCanjeChequeRegalo plan);
        /// <summary>Después de guardar, en la misma transacción: suelta lo que ya no le toca al pedido.</summary>
        Task LiberarOtros(PlanCanjeChequeRegalo plan);
        /// <summary>Al borrar el pedido.</summary>
        Task LiberarPedido(int pedido, string usuario);
        Task<ChequeRegaloClienteDTO> LeerChequeVigente(string empresa, string cliente, DateTime ahora);
    }

    /// <summary>
    /// NestoAPI#593 (c4): el canje del cheque regalo en los pedidos (POST y PUT de api/PedidosVenta), como regla dura
    /// FUERA del pipeline de validación de precios (que se puede saltar). Decisiones:
    /// <list type="bullet">
    /// <item>La fecha límite (CanjeHasta, incluida) se mira contra el día en que el servidor guarda el cheque en el
    /// pedido, no contra CabPedidoVta.Fecha (la pone el cliente). Un pedido que ya tenía el cheque antes de la fecha
    /// límite lo conserva aunque se modifique después.</item>
    /// <item>El cheque queda «Canjeado» en ChequesRegalo en cuanto se guarda en un pedido (con EmpresaCanje,
    /// PedidoCanje y FechaCanje); si se quita la línea, se borra el pedido o se cambia de cliente, vuelve a «Generado».
    /// Si la línea ya está servida, ya no se suelta. Si el pedido o la línea se borran fuera de la API, el cheque se
    /// considera libre (PedidoCanjeTieneLaLinea) y se cura al volver a usarlo.</item>
    /// <item>El mínimo se mira en cada guardado sobre todas las líneas del pedido (también las ya servidas).</item>
    /// </list>
    /// </summary>
    public class ServicioCanjeChequesRegalo : IServicioCanjeChequesRegalo
    {
        private readonly IRepositorioCanjeChequesRegalo repositorio;
        private readonly Func<DateTime> ahora;
        private List<CampanaCanjeChequeRegalo> campanas;

        public ServicioCanjeChequesRegalo(IRepositorioCanjeChequesRegalo repositorio, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        /// <summary>Una lectura por petición (el servicio vive lo que el controller).</summary>
        private async Task<List<CampanaCanjeChequeRegalo>> Campanas()
        {
            if (campanas == null)
            {
                campanas = await repositorio.LeerCampanas().ConfigureAwait(false) ?? new List<CampanaCanjeChequeRegalo>();
            }
            return campanas;
        }

        public async Task<List<string>> ProductosCheque()
            => (await Campanas().ConfigureAwait(false)).Select(c => c.Producto?.Trim()).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();

        public async Task NormalizarLineas(PedidoVentaDTO pedido)
        {
            if (pedido?.Lineas == null || !pedido.Lineas.Any())
            {
                return;
            }
            List<CampanaCanjeChequeRegalo> todas = await Campanas().ConfigureAwait(false);
            if (!todas.Any())
            {
                return;
            }
            // Aquí el tipo de línea puede venir vacío: si el producto es el de una campaña, es la línea del cheque
            List<LineaPedidoVentaDTO> lineasCheque = pedido.Lineas
                .Where(l => (l.tipoLinea == null || l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    && ReglasCanjeChequeRegalo.CampanaDelProducto(todas, l.Producto) != null)
                .ToList();
            if (!lineasCheque.Any())
            {
                return;
            }
            Dictionary<string, ProductoParaChequeRegalo> productos = await repositorio
                .LeerProductos(pedido.empresa, lineasCheque.Select(l => l.Producto)).ConfigureAwait(false);
            foreach (LineaPedidoVentaDTO linea in lineasCheque)
            {
                CampanaCanjeChequeRegalo campana = ReglasCanjeChequeRegalo.CampanaDelProducto(todas, linea.Producto);
                ReglasCanjeChequeRegalo.Normalizar(linea, campana,
                    ReglasCanjeChequeRegalo.Buscar(productos, campana.Producto)?.IvaRepercutido, pedido.iva != null);
            }
        }

        public async Task<PlanCanjeChequeRegalo> Comprobar(string empresa, int pedido, string cliente, IList<LinPedidoVta> lineas,
            string ivaCabecera, Action<LinPedidoVta> recalcularImportes, string usuario, bool pedidoNuevo)
        {
            List<CampanaCanjeChequeRegalo> todas = await Campanas().ConfigureAwait(false);
            if (!todas.Any())
            {
                return PlanCanjeChequeRegalo.Nada;
            }
            var plan = new PlanCanjeChequeRegalo
            {
                EmpresaPedido = empresa?.Trim(),
                Pedido = pedido,
                Usuario = usuario,
                LiberarOtros = !pedidoNuevo
            };
            List<LinPedidoVta> lineasCheque = (lineas ?? new List<LinPedidoVta>())
                .Where(l => ReglasCanjeChequeRegalo.EsLineaCheque(l.TipoLinea, l.Producto, todas))
                .ToList();
            if (!lineasCheque.Any())
            {
                return plan;
            }
            if (lineasCheque.Count > 1)
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_VARIAS_LINEAS,
                    "Solo puede haber una línea de cheque regalo por pedido.", empresa, pedido, cliente);
            }

            LinPedidoVta lineaCheque = lineasCheque[0];
            CampanaCanjeChequeRegalo campana = ReglasCanjeChequeRegalo.CampanaDelProducto(todas, lineaCheque.Producto);
            Dictionary<string, ProductoParaChequeRegalo> productos = await repositorio.LeerProductos(empresa,
                lineas.Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && !string.IsNullOrWhiteSpace(l.Producto))
                    .Select(l => l.Producto.Trim()).Concat(new[] { campana.Producto.Trim() }).Distinct()).ConfigureAwait(false);

            if (ReglasCanjeChequeRegalo.Normalizar(lineaCheque, campana,
                ReglasCanjeChequeRegalo.Buscar(productos, campana.Producto)?.IvaRepercutido, !string.IsNullOrWhiteSpace(ivaCabecera)))
            {
                recalcularImportes?.Invoke(lineaCheque);
            }

            string clienteTrim = cliente?.Trim();
            List<ChequeRegaloCliente> cheques = ReglasChequeRegalo.ClienteExcluido(clienteTrim)
                ? new List<ChequeRegaloCliente>()
                : await repositorio.LeerChequesDelCliente(campana.Empresa, clienteTrim, pedido).ConfigureAwait(false) ?? new List<ChequeRegaloCliente>();
            ChequeRegaloCliente cheque = cheques.FirstOrDefault(c => c.Campana?.Trim() == campana.Codigo);
            if (cheque == null)
            {
                ChequeRegaloCliente otro = cheques.FirstOrDefault(c => c.Estado == ReglasCanjeChequeRegalo.ESTADO_GENERADO
                    && todas.Any(k => k.Codigo == c.Campana?.Trim() && k.Activa));
                if (otro != null)
                {
                    CampanaCanjeChequeRegalo campanaOtro = todas.First(k => k.Codigo == otro.Campana.Trim());
                    throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_OTRA_CAMPANA,
                        $"El cheque regalo del cliente {clienteTrim} es de la campaña {campanaOtro.Codigo}, no de la {campana.Codigo}: " +
                        $"la línea tiene que ser del producto {campanaOtro.Producto}.", empresa, pedido, cliente,
                        new Dictionary<string, object> { ["productoCheque"] = campanaOtro.Producto });
                }
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_SIN_CHEQUE,
                    $"El cliente {clienteTrim} no tiene cheque regalo de la campaña {campana.Codigo}: quite la línea {campana.Producto}.",
                    empresa, pedido, cliente);
            }

            bool yaEnEstePedido = cheque.Estado == ReglasCanjeChequeRegalo.ESTADO_CANJEADO && cheque.PedidoCanje == pedido;
            if (!yaEnEstePedido)
            {
                ComprobarQueSePuedeUsar(cheque, campana, empresa, pedido, clienteTrim);
            }

            decimal baseComputable = ReglasCanjeChequeRegalo.BaseComputable(lineas, productos, campana);
            if (!ReglasCanjeChequeRegalo.SuperaElMinimo(baseComputable, campana))
            {
                decimal falta = campana.MinimoCanje - baseComputable + 0.01M;
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_MINIMO_NO_SUPERADO,
                    $"Para usar el cheque regalo el pedido tiene que superar {ReglasCanjeChequeRegalo.Euros(campana.MinimoCanje)} € de producto" +
                    $"{ReglasCanjeChequeRegalo.TextoExclusiones(campana)}. Lleva {ReglasCanjeChequeRegalo.Euros(baseComputable)} €: " +
                    $"faltan {ReglasCanjeChequeRegalo.Euros(falta)} €.", empresa, pedido, cliente,
                    new Dictionary<string, object>
                    {
                        ["minimo"] = campana.MinimoCanje,
                        ["baseComputable"] = baseComputable,
                        ["falta"] = falta
                    });
            }

            plan.ChequeAReservar = cheque;
            return plan;
        }

        private void ComprobarQueSePuedeUsar(ChequeRegaloCliente cheque, CampanaCanjeChequeRegalo campana, string empresa, int pedido, string cliente)
        {
            DateTime momento = ahora();
            if (!campana.Activa)
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_CAMPANA_INACTIVA,
                    $"La campaña de cheque regalo {campana.Codigo} no está activa: no se puede usar el producto {campana.Producto}.",
                    empresa, pedido, cliente);
            }
            if (cheque.Estado == ReglasCanjeChequeRegalo.ESTADO_ANULADO)
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_ANULADO,
                    $"El cheque regalo del cliente {cliente} está anulado.", empresa, pedido, cliente);
            }
            if (cheque.Estado == ReglasCanjeChequeRegalo.ESTADO_CADUCADO || momento.Date > campana.CanjeHasta.Date)
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_FUERA_DE_PLAZO,
                    $"El cheque regalo de la campaña {campana.Codigo} se podía usar hasta el " +
                    $"{campana.CanjeHasta.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("es-ES"))}.", empresa, pedido, cliente);
            }
            if (cheque.Estado == ReglasCanjeChequeRegalo.ESTADO_CANJEADO && cheque.PedidoCanje != null && cheque.PedidoCanjeTieneLaLinea)
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_YA_USADO,
                    $"El cheque regalo del cliente {cliente} ya está aplicado en el pedido {cheque.PedidoCanje}: solo se puede usar una vez.",
                    empresa, pedido, cliente, new Dictionary<string, object> { ["pedidoCanje"] = cheque.PedidoCanje });
            }
            if (cheque.FechaActivacion == null || cheque.FechaActivacion > momento)
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_NO_ACTIVADO,
                    ReglasCanjeChequeRegalo.MensajeParaCliente("PendienteDeActivar", cheque, campana), empresa, pedido, cliente);
            }
            if (cheque.EsPedidoDeLaFacturaOrigen)
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_MISMO_PEDIDO,
                    $"El cheque regalo no se puede usar en el pedido cuya factura ({cheque.FacturaOrigen?.Trim()}) lo generó: " +
                    "tiene que ir en otro pedido.", empresa, pedido, cliente);
            }
        }

        public async Task Reservar(PlanCanjeChequeRegalo plan)
        {
            if (plan?.ChequeAReservar == null)
            {
                return;
            }
            if (!await repositorio.ReservarCheque(plan.ChequeAReservar.Id, plan.EmpresaPedido, plan.Pedido, plan.Usuario).ConfigureAwait(false))
            {
                throw ReglasCanjeChequeRegalo.Error(ReglasCanjeChequeRegalo.ERROR_YA_USADO,
                    $"El cheque regalo del cliente {plan.ChequeAReservar.Cliente?.Trim()} se acaba de aplicar en otro pedido: " +
                    "solo se puede usar una vez.", plan.EmpresaPedido, plan.Pedido, plan.ChequeAReservar.Cliente);
            }
        }

        public async Task LiberarOtros(PlanCanjeChequeRegalo plan)
        {
            if (plan == null || !plan.LiberarOtros)
            {
                return;
            }
            _ = await repositorio.LiberarChequesDelPedido(plan.Pedido, plan.ChequeAReservar?.Id ?? 0, plan.Usuario).ConfigureAwait(false);
        }

        public async Task LiberarPedido(int pedido, string usuario)
            => _ = await repositorio.LiberarChequesDelPedido(pedido, 0, usuario).ConfigureAwait(false);

        public async Task<ChequeRegaloClienteDTO> LeerChequeVigente(string empresa, string cliente, DateTime ahora)
        {
            string clienteTrim = cliente?.Trim();
            if (ReglasChequeRegalo.ClienteExcluido(clienteTrim))
            {
                return null;
            }
            List<CampanaCanjeChequeRegalo> activas = (await Campanas().ConfigureAwait(false)).Where(c => c.Activa).ToList();
            if (!activas.Any())
            {
                return null;
            }
            List<ChequeRegaloCliente> cheques = await repositorio.LeerChequesDelCliente(empresa, clienteTrim, 0).ConfigureAwait(false)
                ?? new List<ChequeRegaloCliente>();
            var candidatos = cheques
                .Select(c => new { Cheque = c, Campana = activas.FirstOrDefault(k => k.Codigo == c.Campana?.Trim()) })
                .Where(x => x.Campana != null)
                .Select(x => new { x.Cheque, x.Campana, Estado = ReglasCanjeChequeRegalo.EstadoParaCliente(x.Cheque, x.Campana, ahora) })
                .ToList();
            // Primero el que se puede usar o ya está en un pedido; después, el de la campaña que acaba más tarde
            var elegido = candidatos
                .OrderBy(x => x.Estado == "Disponible" ? 0 : x.Estado == "EnPedido" ? 1 : x.Estado == "PendienteDeActivar" ? 2 : 3)
                .ThenByDescending(x => x.Campana.CanjeHasta)
                .FirstOrDefault();
            if (elegido == null)
            {
                return null;
            }
            return new ChequeRegaloClienteDTO
            {
                Campana = elegido.Campana.Codigo,
                Empresa = elegido.Campana.Empresa?.Trim(),
                Cliente = clienteTrim,
                Producto = elegido.Campana.Producto?.Trim(),
                Importe = elegido.Campana.ImporteBase,
                MinimoCanje = elegido.Campana.MinimoCanje,
                CanjeHasta = elegido.Campana.CanjeHasta,
                PrefijosNombreExcluidosMinimo = elegido.Campana.Prefijos(),
                GruposExcluidosMinimo = elegido.Campana.Grupos(),
                Estado = elegido.Estado,
                SePuedeUsar = elegido.Estado == "Disponible",
                Mensaje = ReglasCanjeChequeRegalo.MensajeParaCliente(elegido.Estado, elegido.Cheque, elegido.Campana),
                EmpresaFactura = elegido.Cheque.EmpresaFactura?.Trim(),
                FacturaOrigen = elegido.Cheque.FacturaOrigen?.Trim(),
                FechaFactura = elegido.Cheque.FechaFactura,
                FechaActivacion = elegido.Cheque.FechaActivacion,
                EmpresaPedidoCanje = elegido.Cheque.PedidoCanjeTieneLaLinea ? elegido.Cheque.EmpresaCanje?.Trim() : null,
                PedidoCanje = elegido.Cheque.PedidoCanjeTieneLaLinea ? elegido.Cheque.PedidoCanje : null,
                FechaCanje = elegido.Cheque.PedidoCanjeTieneLaLinea ? elegido.Cheque.FechaCanje : null,
                TextoLinea = ReglasCanjeChequeRegalo.TextoLinea(elegido.Campana)
            };
        }
    }

    /// <summary>NestoAPI#593 (c4): el canje por SQL directo (sin EDMX), como la generación.</summary>
    public class RepositorioCanjeChequesRegalo : IRepositorioCanjeChequesRegalo
    {
        private readonly NVEntities db;

        public RepositorioCanjeChequesRegalo(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        // Sin tablas (script c2 sin lanzar): no hay campañas. Sin las columnas de c4 (script c4 sin lanzar): las campañas
        // salen sin exclusiones, para que sus productos se sigan reconociendo como cheque (y se rechacen mientras la
        // campaña esté apagada). Las columnas nuevas solo se nombran en SQL dinámico, porque una columna que no existe
        // no compila ni en la rama que no se ejecuta.
        internal const string SQL_CAMPANAS = @"
IF OBJECT_ID('dbo.ChequesRegaloCampanas') IS NULL
    SELECT CAST(NULL AS varchar(20)) AS Codigo, CAST(NULL AS char(3)) AS Empresa, CAST(NULL AS char(15)) AS Producto,
           CAST(0 AS decimal(10,2)) AS ImporteBase, GETDATE() AS GeneraDesde, GETDATE() AS GeneraHasta, GETDATE() AS CanjeHasta,
           CAST(0 AS decimal(10,2)) AS MinimoFacturaQueGenera, CAST(0 AS decimal(10,2)) AS MinimoCanje, 0 AS DiasEsperaTrasEntrega,
           CAST(0 AS bit) AS Activa, CAST(NULL AS nvarchar(200)) AS PrefijosNombreExcluidosMinimo, CAST(NULL AS varchar(200)) AS GruposExcluidosMinimo
    WHERE 1 = 0
ELSE IF COL_LENGTH('dbo.ChequesRegaloCampanas', 'GruposExcluidosMinimo') IS NULL
    SELECT RTRIM(Codigo) AS Codigo, RTRIM(Empresa) AS Empresa, RTRIM(Producto) AS Producto, ImporteBase, GeneraDesde, GeneraHasta, CanjeHasta,
           MinimoFacturaQueGenera, MinimoCanje, DiasEsperaTrasEntrega, Activa,
           CAST(NULL AS nvarchar(200)) AS PrefijosNombreExcluidosMinimo, CAST(NULL AS varchar(200)) AS GruposExcluidosMinimo
    FROM dbo.ChequesRegaloCampanas
ELSE
    EXEC sp_executesql N'SELECT RTRIM(Codigo) AS Codigo, RTRIM(Empresa) AS Empresa, RTRIM(Producto) AS Producto, ImporteBase, GeneraDesde,
           GeneraHasta, CanjeHasta, MinimoFacturaQueGenera, MinimoCanje, DiasEsperaTrasEntrega, Activa,
           PrefijosNombreExcluidosMinimo, GruposExcluidosMinimo
    FROM dbo.ChequesRegaloCampanas'";

        // La línea del pedido del canje se busca por número (los pedidos tienen numeración global y la serie GB pasa a la
        // empresa espejo al facturar) y producto de la campaña.
        internal const string SQL_CHEQUES_DEL_CLIENTE = @"
SELECT c.Id, RTRIM(c.Campana) AS Campana, RTRIM(c.Empresa) AS Empresa, RTRIM(c.Cliente) AS Cliente,
       RTRIM(c.EmpresaFactura) AS EmpresaFactura, RTRIM(c.FacturaOrigen) AS FacturaOrigen, c.FechaFactura, c.FechaGeneracion,
       c.FechaActivacion, RTRIM(c.Estado) AS Estado, RTRIM(c.EmpresaCanje) AS EmpresaCanje, c.PedidoCanje, c.FechaCanje,
       CAST(CASE WHEN c.PedidoCanje IS NOT NULL AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.PedidoCanje AND l.Producto = k.Producto)
                 THEN 1 ELSE 0 END AS bit) AS PedidoCanjeTieneLaLinea,
       CAST(CASE WHEN c.PedidoCanje IS NOT NULL AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.PedidoCanje AND l.Producto = k.Producto AND l.Estado >= 2)
                 THEN 1 ELSE 0 END AS bit) AS LineaChequeServida,
       CAST(CASE WHEN @p2 <> 0 AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.Empresa = c.EmpresaFactura AND l.[Nº Factura] = c.FacturaOrigen AND l.[Número] = @p2)
                 THEN 1 ELSE 0 END AS bit) AS EsPedidoDeLaFacturaOrigen
FROM dbo.ChequesRegalo c
     INNER JOIN dbo.ChequesRegaloCampanas k ON k.Codigo = c.Campana
WHERE c.Empresa = @p0 AND c.Cliente = @p1";

        // El candado del canje. Solo pasa si el cheque está libre (Generado), ya es de este pedido, o su pedido ya no
        // tiene la línea (se borró fuera de la API). Con dos pedidos a la vez, el segundo espera al bloqueo de fila del
        // primero (va en la transacción del guardado) y, cuando el primero confirma, ya no cumple el WHERE: 0 filas.
        internal const string SQL_RESERVAR = @"
UPDATE c
SET Estado = 'Canjeado', EmpresaCanje = @p1, PedidoCanje = @p2,
    FechaCanje = CASE WHEN c.Estado = 'Canjeado' AND c.PedidoCanje = @p2 THEN c.FechaCanje ELSE GETDATE() END,
    Usuario = @p3, FechaModificacion = GETDATE()
FROM dbo.ChequesRegalo c WITH (UPDLOCK, ROWLOCK)
     INNER JOIN dbo.ChequesRegaloCampanas k ON k.Codigo = c.Campana
WHERE c.Id = @p0
      AND c.Estado IN ('Generado', 'Canjeado')
      AND (c.PedidoCanje IS NULL
           OR c.PedidoCanje = @p2
           OR NOT EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = c.PedidoCanje AND l.Producto = k.Producto))";

        // Vuelve a Generado lo que estaba en el pedido, salvo el que se acaba de reservar y salvo que su línea ya esté
        // servida (albarán o factura): eso ya no se devuelve.
        internal const string SQL_LIBERAR = @"
IF OBJECT_ID('dbo.ChequesRegalo') IS NOT NULL
    UPDATE c
    SET Estado = 'Generado', EmpresaCanje = NULL, PedidoCanje = NULL, FechaCanje = NULL, Usuario = @p2, FechaModificacion = GETDATE()
    FROM dbo.ChequesRegalo c
         INNER JOIN dbo.ChequesRegaloCampanas k ON k.Codigo = c.Campana
    WHERE c.PedidoCanje = @p0 AND c.Estado = 'Canjeado' AND c.Id <> @p1
          AND NOT EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.[Número] = @p0 AND l.Producto = k.Producto AND l.Estado >= 2)";

        public async Task<List<CampanaCanjeChequeRegalo>> LeerCampanas()
            => await db.Database.SqlQuery<CampanaCanjeChequeRegalo>(SQL_CAMPANAS).ToListAsync().ConfigureAwait(false);

        /// <summary>Para el picking, que es síncrono.</summary>
        public List<CampanaCanjeChequeRegalo> LeerCampanasSincrono()
            => db.Database.SqlQuery<CampanaCanjeChequeRegalo>(SQL_CAMPANAS).ToList();

        public async Task<List<ChequeRegaloCliente>> LeerChequesDelCliente(string empresa, string cliente, int pedido)
            => await db.Database.SqlQuery<ChequeRegaloCliente>(SQL_CHEQUES_DEL_CLIENTE,
                new SqlParameter("@p0", empresa?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p1", cliente?.Trim() ?? string.Empty),
                new SqlParameter("@p2", pedido)).ToListAsync().ConfigureAwait(false);

        public async Task<Dictionary<string, ProductoParaChequeRegalo>> LeerProductos(string empresa, IEnumerable<string> productos)
        {
            List<string> codigos = (productos ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToList();
            if (!codigos.Any())
            {
                return new Dictionary<string, ProductoParaChequeRegalo>(StringComparer.OrdinalIgnoreCase);
            }
            // SQL compara char sin los espacios del final: Número = 'X' encuentra 'X              '
            var filas = await db.Productos
                .Where(p => p.Empresa == empresa && codigos.Contains(p.Número))
                .Select(p => new { p.Número, p.Nombre, p.Grupo, p.Ficticio, p.IVA_Repercutido })
                .ToListAsync().ConfigureAwait(false);
            return Diccionario(filas.Select(p => new ProductoParaChequeRegalo
            {
                Numero = p.Número?.Trim(),
                Nombre = p.Nombre?.Trim(),
                Grupo = p.Grupo?.Trim(),
                Ficticio = p.Ficticio,
                IvaRepercutido = p.IVA_Repercutido?.Trim()
            }));
        }

        internal static Dictionary<string, ProductoParaChequeRegalo> Diccionario(IEnumerable<ProductoParaChequeRegalo> productos)
        {
            var resultado = new Dictionary<string, ProductoParaChequeRegalo>(StringComparer.OrdinalIgnoreCase);
            foreach (ProductoParaChequeRegalo p in productos.Where(p => !string.IsNullOrEmpty(p.Numero)))
            {
                resultado[p.Numero] = p;
            }
            return resultado;
        }

        public async Task<bool> ReservarCheque(int id, string empresaPedido, int pedido, string usuario)
            => await db.Database.ExecuteSqlCommandAsync(SQL_RESERVAR,
                new SqlParameter("@p0", id),
                new SqlParameter("@p1", empresaPedido?.Trim() ?? Constantes.Empresas.EMPRESA_POR_DEFECTO),
                new SqlParameter("@p2", pedido),
                new SqlParameter("@p3", UsuarioAuditoria(usuario))).ConfigureAwait(false) > 0;

        public async Task<int> LiberarChequesDelPedido(int pedido, int exceptoId, string usuario)
            => await db.Database.ExecuteSqlCommandAsync(SQL_LIBERAR,
                new SqlParameter("@p0", pedido),
                new SqlParameter("@p1", exceptoId),
                new SqlParameter("@p2", UsuarioAuditoria(usuario))).ConfigureAwait(false);

        private static string UsuarioAuditoria(string usuario)
        {
            string u = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario.Trim();
            return u.Length > 50 ? u.Substring(0, 50) : u;
        }
    }
}
