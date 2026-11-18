using NestoAPI.Infrastructure;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Linq;
using System.Security.Principal;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Un <see cref="IServicioPrecios"/> que sabe dar otro igual que mira la vigencia de las ofertas un día
    /// concreto en vez de hoy. Lo implementa <see cref="ServicioPrecios"/>; los fakes de los tests, si quieren.
    /// </summary>
    public interface IServicioPreciosConDiaVigencia
    {
        /// <param name="diasMargenFin">Black Friday 2026: días de margen por el FINAL de la vigencia (0 = ninguno).</param>
        IServicioPrecios ParaDia(DateTime dia, int diasMargenFin);
    }

    /// <summary>
    /// Feria online 12-16/11/26 (decisión de Carlos, 10/11/26): un pedido de la tienda online se valida con las
    /// ofertas y descuentos vigentes EL DÍA DEL PEDIDO, no hoy. La tienda admite pedidos hasta el último día de
    /// la oferta y se pasan a Nesto (CanalesExternos) uno o más días después: con hoy, la oferta ya ha caducado
    /// y la API rechazaría un pedido que en la tienda era correcto.
    ///
    /// <para>Acotado a propósito: solo pedidos de la tienda (<see cref="EsPedidoTiendaOnline"/>), con fecha
    /// anterior a hoy y como mucho <see cref="DIAS_MAXIMOS_ATRAS"/> días atrás. En cualquier otro caso, hoy
    /// (lo de siempre). Solo afecta a la VALIDACIÓN (GestorPrecios.EsPedidoValido); el cálculo de precios de la
    /// plantilla sigue con hoy.</para>
    /// <para>Y solo si quien pasa el pedido es de la tienda online o de Dirección (<see cref="PuedeValidarConDiaDelPedido"/>,
    /// Carlos 11/11/26): para cualquier otro usuario, con hoy. El controlador lo decide con su principal y se lo pasa al gestor.</para>
    /// <para>Black Friday 2026 (Carlos 18/11/26): un día de margen por el FINAL (<see cref="DIAS_MARGEN_TIENDA"/>). Una campaña
    /// que acaba el día D se retira de la tienda de madrugada del D+1 (job vigencia-campanas hacia la 1:00, y la tienda tarda un
    /// par de horas en actualizar ~7.000 productos): un pedido web del D+1 lleva aún el descuento y se da por bueno. Por eso
    /// el día del pedido cuenta también cuando es HOY. Por el principio no hay margen (lo que no ha empezado no vale) y los
    /// pedidos de vendedores (Nesto, NestoApp) siguen acabando el día D.</para>
    /// </summary>
    public static class VigenciaPedidoTienda
    {
        public const int DIAS_MAXIMOS_ATRAS = 15;

        /// <summary>Black Friday 2026 (Carlos 18/11/26): una oferta, descuento o campaña que acabó el día D vale para los pedidos
        /// de la tienda online del D+1 (lo que tarda la tienda en retirarla). Solo pedidos de la tienda.</summary>
        public const int DIAS_MARGEN_TIENDA = 1;

        public const string MARCA_COMENTARIO_TIENDA = "TOTAL PEDIDO:";

        /// <summary>
        /// La regla única de «pedido de la tienda online»: el comentario lleva «TOTAL PEDIDO:» (lo pone
        /// CanalExternoPedidosPrestashop) y todas las líneas son de forma de venta WEB. La app (APP) no entra
        /// (NestoAPI#435).
        /// </summary>
        public static bool EsPedidoTiendaOnline(PedidoVentaDTO pedido)
        {
            if (pedido?.comentarios == null || !pedido.comentarios.Contains(MARCA_COMENTARIO_TIENDA))
            {
                return false;
            }
            return pedido.Lineas == null || pedido.Lineas.All(l => l.formaVenta == Constantes.FormasVenta.TIENDA_ONLINE);
        }

        /// <summary>
        /// ¿Puede este usuario pasar pedidos de la tienda validados con las ofertas del día del pedido? Solo los grupos de la
        /// tienda online y Dirección (sin dominio, como el resto de la API). Sin usuario, no.
        /// </summary>
        public static bool PuedeValidarConDiaDelPedido(IPrincipal usuario)
        {
            if (usuario == null)
            {
                return false;
            }
            try
            {
                return usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.TIENDA_ON_LINE) ||
                       usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// El día contra el que se mira la vigencia al validar (con <see cref="DIAS_MARGEN_TIENDA"/> de margen por el final), o
        /// null si no es un pedido de la tienda con fecha de hoy o de los últimos <see cref="DIAS_MAXIMOS_ATRAS"/> días: hoy y sin
        /// margen (lo de siempre). Desde el 18/11/26 también HOY, por el margen.
        /// </summary>
        public static DateTime? DiaVigencia(PedidoVentaDTO pedido, DateTime? fechaPedido, DateTime hoy)
        {
            if (!fechaPedido.HasValue || !EsPedidoTiendaOnline(pedido) || pedido.Lineas == null || !pedido.Lineas.Any())
            {
                return null;
            }
            DateTime dia = fechaPedido.Value.Date;
            DateTime hoyDia = hoy.Date;
            if (dia > hoyDia || dia < hoyDia.AddDays(-DIAS_MAXIMOS_ATRAS))
            {
                return null;
            }
            return dia;
        }

        /// <summary>
        /// Black Friday 2026: ¿vale para un pedido de la tienda online del <paramref name="diaPedido"/> una oferta con estas fechas?
        /// La regla con nombre (la aplica ServicioPrecios con <see cref="Vigencia"/>): empezada ese día y acabada, como mucho,
        /// <see cref="DIAS_MARGEN_TIENDA"/> días antes.
        /// </summary>
        public static bool VigenteParaPedidoTienda(DateTime? fechaDesde, DateTime? fechaHasta, DateTime diaPedido)
        {
            return Vigencia.EsVigente(fechaDesde, fechaHasta, diaPedido.Date, DIAS_MARGEN_TIENDA);
        }

        /// <summary>
        /// El servicio con el que se valida: el mismo, o uno que mira la vigencia el día del pedido si aplica y
        /// el servicio lo sabe hacer (un fake que no implemente <see cref="IServicioPreciosConDiaVigencia"/> se
        /// queda como está).
        /// </summary>
        public static IServicioPrecios ServicioParaValidar(IServicioPrecios servicio, PedidoVentaDTO pedido, DateTime? fechaPedido, DateTime hoy,
            bool usuarioPuedeValidarConDiaDelPedido)
        {
            if (!usuarioPuedeValidarConDiaDelPedido)
            {
                return servicio;
            }
            DateTime? dia = DiaVigencia(pedido, fechaPedido, hoy);
            return dia.HasValue && servicio is IServicioPreciosConDiaVigencia conDia
                ? conDia.ParaDia(dia.Value, DIAS_MARGEN_TIENDA)
                : servicio;
        }
    }
}
