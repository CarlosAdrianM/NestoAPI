using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace NestoAPI.Models
{
    /// <summary>Clase parcial de Inventario (generado por el EDMX): datos de la respuesta que no son columnas.</summary>
    public partial class Inventario
    {
        /// <summary>
        /// NestoAPI#884: la fila es de una muestra (Subgrupo MMP): lo leído es casi siempre la etiqueta de su cajón o gaveta (una
        /// por referencia), no una unidad: la app pide SIEMPRE la cantidad. Solo lectura: sale en el GET, el POST y el PUT de
        /// api/Inventarios y lo que mande el cliente se ignora. Ver <see cref="Infraestructure.PreparacionAlmacen.MuestrasSinCodigo.PideCantidad"/>.
        /// </summary>
        [NotMapped]
        [JsonProperty]
        public bool PideCantidad => Infraestructure.PreparacionAlmacen.MuestrasSinCodigo.PideCantidad(Subgrupo);

        /// <summary>
        /// 25/11/26 (botón «Borrar» de la lista «Completo» de Inventario Tienda): si quien pregunta puede borrar esta fila con
        /// DELETE api/Inventarios/Filas/{n}, con la MISMA regla que el DELETE (<see cref="Infraestructure.Inventarios.ReglaBorrarFilaInventario.SePuedeBorrarFila"/>).
        /// Solo la rellena GET api/Inventarios?empresa&amp;almacen&amp;fecha (en el resto, false). Solo lectura: lo que mande el cliente en un
        /// POST o PUT se ignora (el setter no es público).
        /// </summary>
        [NotMapped]
        public bool SePuedeBorrar { get; internal set; }

        /// <summary>
        /// 25/11/26: la fila es una pendiente de un día ANTERIOR al pedido, añadida al final por GET api/Inventarios con
        /// <c>incluirPendientesAnteriores=true</c> (su día va en Fecha). Solo lectura, como <see cref="SePuedeBorrar"/>.
        /// </summary>
        [NotMapped]
        public bool PendienteDeDiaAnterior { get; internal set; }
    }
}
