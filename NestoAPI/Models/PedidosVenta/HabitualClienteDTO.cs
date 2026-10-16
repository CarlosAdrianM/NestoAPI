using System;

namespace NestoAPI.Models.PedidosVenta
{
    /// <summary>
    /// NestoAPI#661 (TNV#86): un producto que el cliente ha comprado, para la lista de «tus productos» de la
    /// app (<c>GET api/Pedidos/Cliente/Habituales</c>). La lista ya viene ordenada (<see cref="Orden"/>): arriba
    /// lo que por su propio ritmo le tocaría reponer. El cliente no ve nada de ritmos ni cálculos: solo nota
    /// que lo que necesita sale primero, con la cantidad de siempre ya puesta.
    /// </summary>
    public class HabitualClienteDTO
    {
        public string Producto { get; set; }

        /// <summary>El nombre de la ficha del producto (tal cual está en Nesto).</summary>
        public string Nombre { get; set; }

        /// <summary>
        /// Lo que suele llevarse: la mediana de las unidades por día de compra, redondeada y nunca menos de 1
        /// (ver CalculadoraReposicion.CantidadHabitual).
        /// </summary>
        public int CantidadHabitual { get; set; }

        /// <summary>El último día que se lo llevó (fecha del albarán).</summary>
        public DateTime UltimaCompra { get; set; }

        /// <summary>
        /// Por su ritmo de compra ya le toca reponerlo: el mismo criterio que el recordatorio de reposición
        /// (NestoAPI#532), con sus mismas exclusiones (pedido pendiente, sustituto, sin stock...).
        /// </summary>
        public bool TocaReponer { get; set; }

        /// <summary>Posición en la lista, empezando en 1.</summary>
        public int Orden { get; set; }
    }
}
