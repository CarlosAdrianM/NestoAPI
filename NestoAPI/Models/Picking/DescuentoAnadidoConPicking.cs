using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Infraestructure.Cupones;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Picking
{
    /// <summary>Por qué la línea de cupón o cheque añadida NO toma el picking vivo del pedido (NestoAPI#900). Ninguno = lo toma.</summary>
    public enum MotivoDescuentoSinPicking
    {
        Ninguno,
        SinLineasDeDescuento,
        NotaEntrega,
        SinPickingVivo,
        VariosPickingsVivos,
        EtiquetaEnCurso,
        LoTieneLaAgencia,
        FechaPosterior,
        NoLlegaAlMinimo
    }

    /// <summary>NestoAPI#900: lo que decide <see cref="ReglaDescuentoAnadidoConPicking.Decidir"/>.</summary>
    public class ResultadoDescuentoAnadidoConPicking
    {
        /// <summary>El picking vivo que toman las líneas (0 si ninguna lo toma).</summary>
        public int Picking { get; set; }
        /// <summary>Las líneas nuevas (cupón o cheque) que toman ese picking.</summary>
        public List<LinPedidoVta> Lineas { get; set; } = new List<LinPedidoVta>();
        public MotivoDescuentoSinPicking Motivo { get; set; }

        internal static ResultadoDescuentoAnadidoConPicking No(MotivoDescuentoSinPicking motivo)
            => new ResultadoDescuentoAnadidoConPicking { Motivo = motivo };
    }

    /// <summary>
    /// NestoAPI#900 (pedido 930535, cupón EV-2611-RG88, 24/11/26): una línea de cupón de evento o de cheque regalo AÑADIDA a un pedido
    /// que ya tiene un picking vivo (líneas -1..1 con picking, sin albarán) se quedaba sin picking: el picking del pedido no la coge
    /// (la línea −1 sola no «tiene stock de algo») y había que albaranarla a mano. LA REGLA (pura): toma ESE picking, para salir en
    /// el mismo albarán y factura, si con lo ya entregado más lo que lleva ese picking llega a su mínimo; la decisión del mínimo es
    /// la MISMA del picking (<see cref="CuponesPicking"/> + <see cref="GestorReservasStock.ReservarCupones"/>, y
    /// <see cref="ChequesRegaloPicking"/> + <see cref="GestorReservasStock.ReservarChequesRegalo"/>). Si no, como antes: se queda
    /// sin picking para la siguiente entrega.
    /// <para>Prudente: no lo toma si hay más de un picking vivo, si el pedido ya tiene etiqueta en curso (cambiaría su reembolso),
    /// si la agencia ya tiene ese picking (<see cref="Infraestructure.PedidosVenta.Retencion.AplazamientoPedidoConPicking.YaLoTieneLaAgencia(IEnumerable{Infraestructure.PedidosVenta.Retencion.EnvioDelPedido}, IEnumerable{int}, out int)"/>),
    /// en notas de entrega, fuera de Algete o con una fecha de entrega posterior a la del picking.</para>
    /// </summary>
    public static class ReglaDescuentoAnadidoConPicking
    {
        /// <param name="lineasDelPedido">Las líneas que quedan en el pedido SIN las nuevas.</param>
        /// <param name="lineasNuevas">Las líneas que añade este guardado.</param>
        /// <param name="laAgenciaYaLoTiene">picking → la agencia ya tiene un envío que puede llevarlo.</param>
        /// <param name="cupon">El cupón del pedido (importe IVA incluido y mínimo guardado); null = el importe de la línea.</param>
        /// <param name="campanas">Las campañas de cheque regalo (solo se piden si hay una línea negativa que no es de cupón).</param>
        public static ResultadoDescuentoAnadidoConPicking Decidir(
            string empresa, int pedido, bool esNotaEntrega, bool hayEtiquetaEnCurso,
            IList<LinPedidoVta> lineasDelPedido, IList<LinPedidoVta> lineasNuevas,
            Func<int, bool> laAgenciaYaLoTiene,
            string productoCupon, CuponUsadoPicking cupon,
            Func<string, List<string>, Dictionary<string, ProductoParaCupon>> leerProductosCupon,
            Func<List<CampanaCanjeChequeRegalo>> campanas,
            Func<string, List<string>, Dictionary<string, ProductoParaChequeRegalo>> leerProductosCheque,
            DateTime hoy)
        {
            List<LinPedidoVta> negativas = (lineasNuevas ?? new List<LinPedidoVta>())
                .Where(l => l != null && l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO && (l.Cantidad ?? 0) < 0
                    && (l.Picking ?? 0) == 0
                    && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO
                    && string.Equals(l.Almacén?.Trim(), Constantes.Productos.ALMACEN_POR_DEFECTO, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (!negativas.Any())
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.SinLineasDeDescuento);
            }
            if (esNotaEntrega)
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.NotaEntrega);
            }
            List<LinPedidoVta> existentes = (lineasDelPedido ?? new List<LinPedidoVta>()).Where(l => l != null).ToList();
            List<LinPedidoVta> conPickingVivo = existentes
                .Where(l => (l.Picking ?? 0) > 0 && l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO)
                .ToList();
            List<int> pickings = conPickingVivo.Select(l => l.Picking.Value).Distinct().ToList();
            if (pickings.Count == 0)
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.SinPickingVivo);
            }
            if (pickings.Count > 1)
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.VariosPickingsVivos);
            }
            if (hayEtiquetaEnCurso)
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.EtiquetaEnCurso);
            }
            int picking = pickings[0];
            if (laAgenciaYaLoTiene != null && laAgenciaYaLoTiene(picking))
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.LoTieneLaAgencia);
            }

            // Una línea con fecha posterior no habría salido en ese picking
            DateTime fechaPicking = new[] { hoy.Date }.Concat(conPickingVivo.Select(l => l.Fecha_Entrega.Date)).Max();
            List<LinPedidoVta> aTiempo = negativas.Where(l => l.Fecha_Entrega.Date <= fechaPicking).ToList();
            if (!aTiempo.Any())
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.FechaPosterior);
            }

            // Lo entregado: como en el picking, lo albaranado o facturado y lo que tiene picking de una pasada anterior
            List<LinPedidoVta> entregadas = existentes
                .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO
                    && (l.Estado >= Constantes.EstadosLineaVenta.ALBARAN
                        || (l.Estado >= Constantes.EstadosLineaVenta.PENDIENTE && l.Estado <= Constantes.EstadosLineaVenta.EN_CURSO && (l.Picking ?? 0) > 0)))
                .ToList();

            var origen = new Dictionary<LineaPedidoPicking, LinPedidoVta>();
            var pedidoPicking = new PedidoPicking { Empresa = empresa, Id = pedido, EsNotaEntrega = false, Lineas = new List<LineaPedidoPicking>() };
            foreach (LinPedidoVta linea in aTiempo)
            {
                var lineaPicking = new LineaPedidoPicking
                {
                    Id = linea.Nº_Orden,
                    NumeroPedido = pedido,
                    TipoLinea = linea.TipoLinea.Value,
                    Almacen = linea.Almacén,
                    Producto = linea.Producto,
                    Cantidad = (linea.Cantidad ?? 0) - linea.Recoger,
                    CantidadRecogida = linea.Recoger,
                    BaseImponible = linea.Base_Imponible,
                    Total = linea.Total,
                    FechaEntrega = linea.Fecha_Entrega
                };
                origen[lineaPicking] = linea;
                pedidoPicking.Lineas.Add(lineaPicking);
            }
            var lista = new List<PedidoPicking> { pedidoPicking };

            bool hayCupon = pedidoPicking.Lineas.Any(l => ReglasCupon.EsProductoCupon(l.Producto, productoCupon));
            if (hayCupon)
            {
                CuponesPicking.Rellenar(lista, productoCupon, leerProductosCupon,
                    p => entregadas.Select(l => new LineaEntregadaCupon { Producto = l.Producto, Total = l.Total }).ToList(),
                    _ => cupon);
            }
            if (pedidoPicking.Lineas.Any(l => !ReglasCupon.EsProductoCupon(l.Producto, productoCupon)) && campanas != null)
            {
                ChequesRegaloPicking.Rellenar(lista, campanas(), leerProductosCheque,
                    p => entregadas.Select(l => new LineaEntregadaChequeRegalo { Empresa = l.Empresa, Producto = l.Producto, BaseImponible = l.Base_Imponible }).ToList());
            }
            // Solo las de cupón o cheque: cualquier otra línea negativa (un abono, una devolución…) no se toca
            _ = pedidoPicking.Lineas.RemoveAll(l => !l.EsCupon && !l.EsChequeRegalo);
            if (!pedidoPicking.Lineas.Any())
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.SinLineasDeDescuento);
            }

            // La MISMA decisión del picking (y sin otras líneas que salgan ahora: todo lo del picking vivo ya está en lo entregado)
            GestorReservasStock.ReservarChequesRegalo(lista);
            GestorReservasStock.ReservarCupones(lista);

            List<LinPedidoVta> toman = pedidoPicking.Lineas
                .Where(l => (l.EsCupon || l.EsChequeRegalo) && l.CantidadReservada == l.Cantidad)
                .Select(l => origen[l])
                .ToList();
            if (!toman.Any())
            {
                return ResultadoDescuentoAnadidoConPicking.No(MotivoDescuentoSinPicking.NoLlegaAlMinimo);
            }
            return new ResultadoDescuentoAnadidoConPicking { Picking = picking, Lineas = toman, Motivo = MotivoDescuentoSinPicking.Ninguno };
        }

        /// <summary>Lo que hace el picking al asignar (AsignadorPicking): el número y, si estaba pendiente, en curso.</summary>
        public static void Aplicar(ResultadoDescuentoAnadidoConPicking resultado)
        {
            if (resultado == null || resultado.Picking <= 0)
            {
                return;
            }
            foreach (LinPedidoVta linea in resultado.Lineas)
            {
                linea.Picking = resultado.Picking;
                if (linea.Estado == Constantes.EstadosLineaVenta.PENDIENTE)
                {
                    linea.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                }
            }
        }
    }
}
