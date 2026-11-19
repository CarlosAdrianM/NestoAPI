using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace NestoAPI.Models
{
    /// <summary>Clase parcial de Cliente (generado por el EDMX): datos de la respuesta que no son columnas.</summary>
    public partial class Cliente
    {
        /// <summary>
        /// Alta con dirección de entrega distinta (19/11/26): en la respuesta de POST api/Clientes, el contacto creado con la dirección de
        /// entrega (ClienteCrear.DireccionEntrega), al que apunta el ContactoDefecto del principal. No viaja si el alta no lo trae.
        /// </summary>
        [NotMapped]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string ContactoEntrega { get; set; }
    }
}
