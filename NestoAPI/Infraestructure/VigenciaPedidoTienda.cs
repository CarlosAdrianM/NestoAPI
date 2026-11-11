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
        IServicioPrecios ParaDia(DateTime dia);
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
    /// </summary>
    public static class VigenciaPedidoTienda
    {
        public const int DIAS_MAXIMOS_ATRAS = 15;

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
        /// El día contra el que se mira la vigencia al validar, o null si es hoy (lo de siempre).
        /// </summary>
        public static DateTime? DiaVigencia(PedidoVentaDTO pedido, DateTime? fechaPedido, DateTime hoy)
        {
            if (!fechaPedido.HasValue || !EsPedidoTiendaOnline(pedido) || pedido.Lineas == null || !pedido.Lineas.Any())
            {
                return null;
            }
            DateTime dia = fechaPedido.Value.Date;
            DateTime hoyDia = hoy.Date;
            if (dia >= hoyDia || dia < hoyDia.AddDays(-DIAS_MAXIMOS_ATRAS))
            {
                return null;
            }
            return dia;
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
                ? conDia.ParaDia(dia.Value)
                : servicio;
        }
    }
}
