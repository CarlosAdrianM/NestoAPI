using NestoAPI.Infraestructure.OpenAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>Un rapport tal como entra en el resumen (fecha, comentario y si terminó en pedido).</summary>
    public class RapportParaResumen
    {
        public DateTime Fecha { get; set; }
        public string Comentarios { get; set; }
        public bool Pedido { get; set; }
    }

    /// <summary>
    /// Sugerencia 830 (Javier, 28/10/26): «Resumir rapports» en la ficha del cliente daba «Object variable or With block variable
    /// not set». El resumen llamaba a OpenAI a pelo con gpt-3.5-turbo, que OpenAI retiró (model_not_found); el controlador
    /// contestaba un 500 sin cuerpo ni rastro en ELMAH y Nesto se estrellaba al leer la respuesta vacía. Ahora va por
    /// <see cref="IServicioOpenAI"/> (gpt-4o-mini, como el resumen semanal y la clasificación de rapports), y si OpenAI falla
    /// se registra y el usuario recibe el motivo.
    /// </summary>
    public static class ResumenRapportsCliente
    {
        public const string PROMPT_SISTEMA = "Eres un asistente que analiza comentarios de clientes y extrae información comercial útil. " +
            "Identifica detalles como las marcas que utiliza el cliente, los tratamientos que realiza, los productos que le interesan, " +
            "y cualquier otra preferencia relevante. Organiza el resumen de forma clara para que sea útil en una interacción comercial.";
        public const int MAX_TOKENS = 500;
        public const double TEMPERATURA = 0.4;

        /// <summary>El texto que se manda: los rapports en orden cronológico, con fecha y si terminaron en pedido.</summary>
        public static string TextoEntrada(IEnumerable<RapportParaResumen> rapports)
        {
            var texto = new StringBuilder("Resumen de seguimientos de un cliente. Los siguientes son los comentarios organizados por fecha:\n\n");
            foreach (RapportParaResumen rapport in (rapports ?? Enumerable.Empty<RapportParaResumen>()).OrderBy(r => r.Fecha))
            {
                texto.Append($"Fecha: {rapport.Fecha:yyyy-MM-dd HH:mm:ss}\nComentario: {rapport.Comentarios}\nTerminó en pedido: {(rapport.Pedido ? "Sí" : "No")}\n\n");
            }
            return texto.ToString();
        }

        /// <summary>El resumen, o null si OpenAI no devuelve nada. Lo que lance OpenAI sale tal cual (el controlador lo traduce).</summary>
        public static async Task<string> Resumir(IServicioOpenAI openAI, IEnumerable<RapportParaResumen> rapports)
        {
            if (openAI == null)
            {
                throw new ArgumentNullException(nameof(openAI));
            }
            string resumen = await openAI.GenerarContenidoAsync(PROMPT_SISTEMA, TextoEntrada(rapports), MAX_TOKENS, TEMPERATURA).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(resumen) ? null : resumen.Trim();
        }
    }
}
