using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http.ModelBinding;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>
    /// Novedades 1314: el 400 de ModelState del POST/PUT de pedidos decía solo «La solicitud no es válida.» (el texto de
    /// Web API), sin decir qué faltaba. Esta pieza pura arma el Message con lo que falla («Faltan los plazos de pago del
    /// pedido»). El formato de la respuesta no cambia (Message + ModelState), así que Nesto, NestoApp y la tienda lo leen igual.
    /// </summary>
    public static class MensajeModelStatePedido
    {
        public const string MENSAJE_GENERICO = "El pedido no es válido";

        // Los campos que, si fallan sin excepción (atributo [Required]), es que faltan. Hoy solo plazosPago es [Required].
        private static readonly Dictionary<string, string> NombresCampos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "plazosPago", "Faltan los plazos de pago del pedido" },
            { "formaPago", "Falta la forma de pago del pedido" },
            { "cliente", "Falta el cliente del pedido" },
            { "contacto", "Falta el contacto del pedido" },
            { "empresa", "Falta la empresa del pedido" },
        };

        public static string Mensaje(ModelStateDictionary modelState)
        {
            if (modelState == null)
            {
                return MENSAJE_GENERICO;
            }
            List<string> mensajes = new List<string>();
            foreach (KeyValuePair<string, ModelState> entrada in modelState)
            {
                if (entrada.Value?.Errors == null || entrada.Value.Errors.Count == 0)
                {
                    continue;
                }
                foreach (ModelError error in entrada.Value.Errors)
                {
                    string mensaje = MensajeDeUnError(entrada.Key, error);
                    if (!mensajes.Contains(mensaje))
                    {
                        mensajes.Add(mensaje);
                    }
                }
            }
            return mensajes.Any() ? string.Join(". ", mensajes) : MENSAJE_GENERICO;
        }

        internal static string Campo(string clave)
        {
            if (string.IsNullOrWhiteSpace(clave))
            {
                return string.Empty;
            }
            int punto = clave.IndexOf('.');
            return punto >= 0 ? clave.Substring(punto + 1) : string.Empty;
        }

        private static string MensajeDeUnError(string clave, ModelError error)
        {
            string campo = Campo(clave);
            if (string.IsNullOrEmpty(campo))
            {
                // Clave «pedido» (o vacía): el cuerpo no se ha podido leer
                return "No se ha podido leer el pedido enviado";
            }
            if (error.Exception != null)
            {
                return $"El dato «{campo}» del pedido no es válido";
            }
            if (NombresCampos.TryGetValue(campo, out string nombre))
            {
                return nombre;
            }
            return string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? $"El dato «{campo}» del pedido no es válido"
                : $"{campo}: {error.ErrorMessage}";
        }
    }
}
