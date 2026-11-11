using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#571 (cortes 1 y 2): la marca «Recoge en tienda» del pedido traducida a ruta. Sin columna nueva: la ruta es
    /// la verdad (<see cref="TiendasRecogida.EsRutaDeRecogida"/>). Lógica pura: el controlador lee lo que haga falta y
    /// aplica lo que decida esta clase.
    /// <list type="bullet">
    /// <item>Marca null (Nesto, la NestoApp publicada, TNV#70 por su camino): no se toca nada.</item>
    /// <item>Marca true: ruta de recogida del almacén del pedido (ALG → AM, ALC → ALC, REI → REI).</item>
    /// <item>Marca false: en ALC y REI no se puede (se recogen siempre en la tienda); en ALG, al crear, como siempre, y al
    /// modificar un pedido que era de recogida, vuelve la ruta del cliente (la de su ficha, <see cref="RutaQueVale"/>).</item>
    /// <item>Nota de entrega: se ignora la marca.</item>
    /// </list>
    /// Los portes y la comisión contra reembolso no se tocan aquí: los recalcula el POST/PUT a partir de la ruta nueva
    /// (<see cref="GestorPortes.CalcularPortes"/>, que en una ruta de recogida no pone ninguno de los dos).
    /// </summary>
    public static class RecogeEnTienda
    {
        public const string PREFIJO_BLOQUEO = "No se puede cambiar «Recoge en tienda»: ";

        /// <summary>Lo que hay que hacer: la ruta nueva (null = no tocar la ruta) o el error (400).</summary>
        public class Resultado
        {
            public string Ruta { get; set; }
            public string Error { get; set; }

            internal static Resultado Nada() => new Resultado();
            internal static Resultado ConRuta(string ruta) => new Resultado { Ruta = ruta };
            internal static Resultado ConError(string error) => new Resultado { Error = error };
        }

        /// <summary>Cómo se llama el almacén en los mensajes.</summary>
        private static string NombreAlmacen(string almacen)
        {
            switch (almacen?.Trim().ToUpperInvariant())
            {
                case Constantes.Almacenes.ALGETE: return "Algete";
                case Constantes.Almacenes.ALCOBENDAS: return "Alcobendas";
                case Constantes.Almacenes.REINA: return "Reina";
                default: return almacen?.Trim();
            }
        }

        private static bool EsAlgete(string almacen)
            => string.Equals(almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase);

        private static bool EsLineaViva(LineaPedidoVentaDTO linea)
            => linea.estado <= Constantes.EstadosLineaVenta.EN_CURSO && linea.estado != Constantes.EstadosLineaVenta.NOTA_ENTREGA;

        /// <summary>
        /// El almacén del pedido: el de sus líneas vivas (presupuesto, pendiente o en curso) de producto, o de cualquier tipo
        /// si no hay de producto. Null con el motivo en <paramref name="error"/> si no hay ninguna o son de varios almacenes.
        /// </summary>
        internal static string AlmacenDelPedido(IEnumerable<LineaPedidoVentaDTO> lineas, out string error)
        {
            error = null;
            List<LineaPedidoVentaDTO> vivas = (lineas ?? Enumerable.Empty<LineaPedidoVentaDTO>())
                .Where(l => l != null && EsLineaViva(l) && !string.IsNullOrWhiteSpace(l.almacen))
                .ToList();
            List<LineaPedidoVentaDTO> deProducto = vivas.Where(l => l.tipoLinea == Constantes.TiposLineaVenta.PRODUCTO).ToList();
            List<string> almacenes = (deProducto.Any() ? deProducto : vivas)
                .Select(l => l.almacen.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            if (!almacenes.Any())
            {
                error = "No se puede saber si el pedido se recoge en tienda: no tiene líneas pendientes de las que sacar el almacén.";
                return null;
            }
            if (almacenes.Count > 1)
            {
                error = $"No se puede marcar ni desmarcar «Recoge en tienda»: el pedido tiene líneas de varios almacenes ({string.Join(", ", almacenes)}). " +
                    "Ponga todas las líneas en el mismo almacén.";
                return null;
            }
            return almacenes[0];
        }

        /// <summary>La ruta de recogida del almacén, o el error si el almacén no es de ninguna tienda.</summary>
        private static Resultado Marcar(string almacen)
        {
            TiendasRecogida.Tienda tienda = TiendasRecogida.Buscar(almacen);
            if (tienda == null)
            {
                return Resultado.ConError($"No se puede marcar «Recoge en tienda»: el almacén {almacen} no es una tienda de recogida " +
                    "(solo Algete, Alcobendas y Reina).");
            }
            return Resultado.ConRuta(tienda.Ruta);
        }

        /// <summary>El 400 de desmarcar en un almacén que no es Algete.</summary>
        internal static string MensajeNoSePuedeDesmarcar(string almacen)
            => $"Los pedidos del almacén de {NombreAlmacen(almacen)} se recogen siempre en la tienda: no se puede desmarcar «Recoge en tienda».";

        /// <summary>
        /// POST. <paramref name="recoge"/> es lo que manda el cliente (<see cref="PedidoVentaDTO.RecogeEnTiendaRecibido"/>).
        /// </summary>
        public static Resultado AlCrear(bool? recoge, bool notaEntrega, IEnumerable<LineaPedidoVentaDTO> lineas)
        {
            if (recoge == null || notaEntrega)
            {
                return Resultado.Nada();
            }
            string almacen = AlmacenDelPedido(lineas, out string error);
            if (almacen == null)
            {
                // Sin marcar no hay nada que traducir: el pedido se crea como siempre.
                return recoge.Value ? Resultado.ConError(error) : Resultado.Nada();
            }
            if (recoge.Value)
            {
                return Marcar(almacen);
            }
            return EsAlgete(almacen) || TiendasRecogida.Buscar(almacen) == null
                ? Resultado.Nada()
                : Resultado.ConError(MensajeNoSePuedeDesmarcar(almacen));
        }

        /// <summary>
        /// PUT, primer paso: si la marca pide un cambio respecto a la ruta GUARDADA. Null = no hay nada que hacer (no la
        /// manda, coincide con lo guardado, que es lo normal porque los clientes devuelven el DTO tal cual lo leyeron, o es
        /// nota de entrega); true = marcar; false = desmarcar. Solo si hay cambio el controlador mira el picking y la etiqueta.
        /// </summary>
        public static bool? CambioAlModificar(bool? recoge, bool notaEntrega, string rutaGuardada)
        {
            if (recoge == null || notaEntrega)
            {
                return null;
            }
            return recoge.Value == TiendasRecogida.EsRutaDeRecogida(rutaGuardada) ? (bool?)null : recoge.Value;
        }

        /// <summary>
        /// PUT, segundo paso (solo si <see cref="CambioAlModificar"/> dio un cambio).
        /// </summary>
        /// <param name="marcar">true = marcar, false = desmarcar.</param>
        /// <param name="lineas">Las líneas que manda el cliente (las que se van a guardar).</param>
        /// <param name="motivoBloqueo">Por qué no se puede tocar el pedido (picking, etiqueta… <see cref="CambioAlmacenPedido.Motivo"/>
        /// con <see cref="PREFIJO_BLOQUEO"/>), o null.</param>
        /// <param name="rutaCliente">Solo para desmarcar en Algete: la ruta de la ficha del contacto (<see cref="RutaQueVale.Leer"/>).</param>
        public static Resultado AlModificar(bool marcar, IEnumerable<LineaPedidoVentaDTO> lineas, string motivoBloqueo, Func<string> rutaCliente)
        {
            string almacen = AlmacenDelPedido(lineas, out string error);
            if (almacen == null)
            {
                return Resultado.ConError(error);
            }
            if (!marcar && !EsAlgete(almacen) && TiendasRecogida.Buscar(almacen) != null)
            {
                return Resultado.ConError(MensajeNoSePuedeDesmarcar(almacen));
            }
            if (motivoBloqueo != null)
            {
                return Resultado.ConError(motivoBloqueo);
            }
            if (marcar)
            {
                return Marcar(almacen);
            }

            string ruta = rutaCliente?.Invoke()?.Trim();
            if (string.IsNullOrEmpty(ruta))
            {
                return Resultado.ConError("No se puede desmarcar «Recoge en tienda»: la ficha del cliente no tiene ruta. " +
                    "Póngale una ruta en la ficha o elija la ruta del pedido a mano.");
            }
            if (TiendasRecogida.EsRutaDeRecogida(ruta))
            {
                return Resultado.ConError($"No se puede desmarcar «Recoge en tienda»: la ruta de la ficha del cliente ({ruta}) es de recogida en tienda. " +
                    "Elija la ruta del pedido a mano.");
            }
            return Resultado.ConRuta(ruta);
        }
    }
}
