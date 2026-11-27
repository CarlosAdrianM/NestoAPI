using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>Un producto de la entrega que sigue sin leer en la mesa del packing (lo esperado − lo metido − lo dado por falta &gt; 0).</summary>
    public class ProductoSinLeerPackingDTO
    {
        public string Producto { get; set; }
        public string Nombre { get; set; }
        /// <summary>Unidades sin leer.</summary>
        public int Cantidad { get; set; }
        /// <summary>El pedido de la entrega que lo lleva (la etiqueta es de todas las cajas de la entrega).</summary>
        public int Pedido { get; set; }
    }

    /// <summary>Los <c>Detalles</c> del 409 PACKING_INCOMPLETO.</summary>
    public class DetallesPackingIncompletoDTO
    {
        /// <summary>Productos sin leer.</summary>
        public int Faltan { get; set; }
        public int Unidades { get; set; }
        public List<ProductoSinLeerPackingDTO> Productos { get; set; } = new List<ProductoSinLeerPackingDTO>();
    }

    /// <summary>El «error» del sobre de los 409 con código (el formato de GlobalExceptionFilter, como COBRO_DE_MAS o NO_CARGADA).</summary>
    public class ErrorConCodigoDTO
    {
        public string code { get; set; }
        public string message { get; set; }
        public object details { get; set; }
    }

    /// <summary>
    /// El sobre de los 409 con código del reparto y del almacén: { error: { code, message, details }, Message, Codigo } (RepartoPropioController.Responder),
    /// y además Mensaje y Detalles en la raíz, que es donde los busca también la app.
    /// </summary>
    public class Conflicto409ConCodigoDTO
    {
        public ErrorConCodigoDTO error { get; set; }
        public string Message { get; set; }
        public string Codigo { get; set; }
        public string Mensaje { get; set; }
        public object Detalles { get; set; }

        public static Conflicto409ConCodigoDTO De(string codigo, string mensaje, object detalles) => new Conflicto409ConCodigoDTO
        {
            error = new ErrorConCodigoDTO { code = codigo, message = mensaje, details = detalles },
            Message = mensaje,
            Codigo = codigo,
            Mensaje = mensaje,
            Detalles = detalles
        };
    }

    /// <summary>Qué hacer con la etiqueta según el packing (<see cref="ReglaPackingIncompleto.Decidir"/>).</summary>
    public enum DecisionPackingIncompleto
    {
        /// <summary>El packing está completo (o no se sabe): se imprime y no se apunta nada.</summary>
        Imprimir,
        /// <summary>Incompleto, la app avisa y no ha confirmado: 409 PACKING_INCOMPLETO, no se imprime.</summary>
        Avisar,
        /// <summary>Incompleto y confirmado por el mozo: se imprime y se apunta «Confirmado».</summary>
        ImprimirConfirmado,
        /// <summary>Incompleto y la app no avisa (versiones de Ariadna anteriores a 2.48): se imprime como siempre y se apunta «SinAviso».</summary>
        ImprimirSinAviso
    }

    /// <summary>
    /// NestoAPI#914 (Carlos 27/11/26: «avisar y dejar seguir»): la etiqueta con el packing incompleto. LA REGLA, pura. Las cuentas son las del
    /// progreso del packing (<c>ServicioPreparacionAlmacen.LeerPacking</c>: Cantidad = lo que va en la caja, Metidas y Faltas por línea).
    /// </summary>
    public static class ReglaPackingIncompleto
    {
        public const string CODIGO = "PACKING_INCOMPLETO";
        public const string MODO_CONFIRMADO = "Confirmado";
        public const string MODO_SIN_AVISO = "SinAviso";

        /// <summary>
        /// Lo que queda sin leer en la mesa en TODOS los pedidos de la entrega (la etiqueta es de todas sus cajas): por pedido y producto,
        /// Σ Cantidad − Σ Metidas − Σ Faltas &gt; 0 (sumado por producto, porque lo metido de más va a la última línea). Sin las líneas de
        /// texto. Null si no hay entrega (no se sabe).
        /// </summary>
        public static List<ProductoSinLeerPackingDTO> SinLeer(EntregaPackingAlmacenDTO entrega)
        {
            if (entrega?.Pedidos == null)
            {
                return null;
            }
            var resultado = new List<ProductoSinLeerPackingDTO>();
            foreach (PedidoPackingAlmacenDTO pedido in entrega.Pedidos.Where(p => p != null).OrderBy(p => p.Pedido))
            {
                foreach (IGrouping<string, LineaPackingAlmacenDTO> producto in (pedido.Lineas ?? new List<LineaPackingAlmacenDTO>())
                    .Where(l => l != null && !l.EsTexto && !string.IsNullOrWhiteSpace(l.Producto))
                    .GroupBy(l => l.Producto.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    int sinLeer = producto.Sum(l => l.Cantidad) - producto.Sum(l => Math.Max(0, l.Metidas)) - producto.Sum(l => Math.Max(0, l.Faltas));
                    if (sinLeer > 0)
                    {
                        resultado.Add(new ProductoSinLeerPackingDTO
                        {
                            Producto = producto.Key,
                            Nombre = producto.Select(l => l.Descripcion?.Trim()).FirstOrDefault(d => !string.IsNullOrEmpty(d)),
                            Cantidad = sinLeer,
                            Pedido = pedido.Pedido
                        });
                    }
                }
            }
            return resultado;
        }

        /// <param name="sinLeer">Lo de <see cref="SinLeer"/> (null: no se sabe, como completo).</param>
        public static DecisionPackingIncompleto Decidir(IReadOnlyCollection<ProductoSinLeerPackingDTO> sinLeer, bool avisar, bool confirmar)
        {
            if (sinLeer == null || sinLeer.Count == 0)
            {
                return DecisionPackingIncompleto.Imprimir;
            }
            if (confirmar)
            {
                return DecisionPackingIncompleto.ImprimirConfirmado;
            }
            return avisar ? DecisionPackingIncompleto.Avisar : DecisionPackingIncompleto.ImprimirSinAviso;
        }

        /// <summary>
        /// «Faltan 2 productos por leer en la mesa (2 unidades). ¿Imprimir la etiqueta igualmente?». NO enumera los productos: la app pinta
        /// la lista debajo con los Detalles.
        /// </summary>
        public static string Mensaje(IReadOnlyCollection<ProductoSinLeerPackingDTO> sinLeer)
        {
            int productos = sinLeer?.Count ?? 0;
            int unidades = sinLeer?.Sum(p => p.Cantidad) ?? 0;
            string cabeza = productos == 1 ? "Falta 1 producto por leer en la mesa" : $"Faltan {productos} productos por leer en la mesa";
            string uds = unidades == 1 ? "1 unidad" : $"{unidades} unidades";
            return $"{cabeza} ({uds}). ¿Imprimir la etiqueta igualmente?";
        }

        public static DetallesPackingIncompletoDTO Detalles(IReadOnlyCollection<ProductoSinLeerPackingDTO> sinLeer) => new DetallesPackingIncompletoDTO
        {
            Faltan = sinLeer?.Count ?? 0,
            Unidades = sinLeer?.Sum(p => p.Cantidad) ?? 0,
            Productos = (sinLeer ?? new List<ProductoSinLeerPackingDTO>()).ToList()
        };

        public static Conflicto409ConCodigoDTO Conflicto(IReadOnlyCollection<ProductoSinLeerPackingDTO> sinLeer) =>
            Conflicto409ConCodigoDTO.De(CODIGO, Mensaje(sinLeer), Detalles(sinLeer));
    }
}
