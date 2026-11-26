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

        /// <summary>
        /// NestoAPI#876: en la respuesta de POST api/Clientes con ClienteCrear.CopiarDelAnterior, por qué no se ha podido copiar (el alta
        /// queda hecha). No viaja si se copió bien o no se pidió.
        /// </summary>
        [NotMapped]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string AvisoCopiaClienteAnterior { get; set; }

        /// <summary>NestoAPI#876: lo copiado del cliente anterior en el alta («Contacto 0: IVA G21 → E52»…). No viaja si no se pidió.</summary>
        [NotMapped]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public System.Collections.Generic.List<string> CopiadoDelAnterior { get; set; }
    }
}
