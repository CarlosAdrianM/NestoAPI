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
    }
}
