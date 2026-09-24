using NestoAPI.Models.Novedades;
using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// NestoAPI#520: reglas puras del feedback (quién es el usuario y qué se acepta en un comentario).
    /// </summary>
    public static class ReglasFeedbackNovedades
    {
        public const int LONGITUD_MAXIMA_TEXTO = 2000;
        public const int TAMANO_MAXIMO_IMAGEN = 2 * 1024 * 1024;
        public const string TIPO_PNG = "image/png";
        public const string TIPO_JPEG = "image/jpeg";
        public const string CLIENTE_NESTO = "Nesto";
        public const string CLIENTE_NESTOAPP = "NestoApp";
        public const string CLIENTE_TIENDA = "TiendaOnline";

        /// <summary>
        /// NestoAPI#531: el asistente IA (Claude) contesta con su propio nombre, no con el de quien
        /// lanza la petición: los usuarios tienen que saber que les responde un agente.
        /// </summary>
        public const string USUARIO_ASISTENTE = "Claude";
        public const string NOMBRE_ASISTENTE = "Claude (asistente IA)";
        public const string CLIENTE_ASISTENTE = "Asistente";

        /// <summary>
        /// Clave ESTABLE del usuario, sacada del token (nunca del cuerpo de la petición). Al renovar el
        /// token la identidad es la misma: Nesto → nameidentifier = NUEVAVISION\usuario; NestoApp → el
        /// Id de usuario de Identity (nameidentifier), no el UserName, que en teoría es editable.
        /// null si la petición no está autenticada.
        /// </summary>
        public static string ClaveUsuario(IPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }
            string id = (user as ClaimsPrincipal)?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            string clave = string.IsNullOrWhiteSpace(id) ? user.Identity.Name : id;
            return string.IsNullOrWhiteSpace(clave) ? null : Recortar(clave.Trim(), 128);
        }

        /// <summary>Nombre que ven los demás: el de Windows sin el dominio, o el nombre de usuario.</summary>
        public static string NombreVisible(IPrincipal user)
        {
            string nombre = user?.Identity?.Name?.Trim();
            if (string.IsNullOrEmpty(nombre))
            {
                return "Usuario";
            }
            int barra = nombre.LastIndexOf('\\');
            if (barra >= 0 && barra < nombre.Length - 1)
            {
                nombre = nombre.Substring(barra + 1);
            }
            return Recortar(nombre, 100);
        }

        /// <summary>Desde qué programa se vota o comenta, según el tipo de token.</summary>
        public static string Cliente(IPrincipal user)
        {
            var principal = user as ClaimsPrincipal;
            if (principal?.FindFirst(ClaimTypes.AuthenticationMethod)?.Value == "Windows")
            {
                return CLIENTE_NESTO;
            }
            if (principal?.Claims.Any(c => c.Type == "cliente") == true)
            {
                return CLIENTE_TIENDA;
            }
            return CLIENTE_NESTOAPP;
        }

        /// <summary>Solo 1 (me gusta), -1 (no me gusta) o 0 (quitar el voto).</summary>
        public static bool EsVotoValido(short voto) => voto == 1 || voto == -1 || voto == 0;

        /// <summary>
        /// Valida el comentario y decodifica la imagen. Devuelve el mensaje de error para el usuario, o
        /// null si todo está bien (y entonces <paramref name="imagen"/> y <paramref name="tipo"/> quedan
        /// rellenos si venía captura).
        /// </summary>
        public static string Validar(NuevoComentarioNovedadDTO comentario, out byte[] imagen, out string tipo)
        {
            imagen = null;
            tipo = null;
            string texto = comentario?.Texto?.Trim();
            if (string.IsNullOrEmpty(texto))
            {
                return "Escribe algo en el comentario.";
            }
            if (texto.Length > LONGITUD_MAXIMA_TEXTO)
            {
                return $"El comentario es demasiado largo (máximo {LONGITUD_MAXIMA_TEXTO} caracteres).";
            }
            if (string.IsNullOrWhiteSpace(comentario.ImagenBase64))
            {
                return null;
            }

            string base64 = comentario.ImagenBase64.Trim();
            int coma = base64.IndexOf(',');
            if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0)
            {
                base64 = base64.Substring(coma + 1);
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return "La imagen no se ha podido leer.";
            }
            if (bytes.Length > TAMANO_MAXIMO_IMAGEN)
            {
                return "La imagen es demasiado grande (máximo 2 MB). Recorta solo la parte que importa.";
            }
            string tipoReal = TipoPorContenido(bytes);
            if (tipoReal == null)
            {
                return "Solo se admiten imágenes PNG o JPEG (las capturas de Recortes lo son).";
            }
            imagen = bytes;
            tipo = tipoReal;
            return null;
        }

        /// <summary>El tipo se deduce de los bytes (firma del fichero), no del que diga el cliente.</summary>
        internal static string TipoPorContenido(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
            {
                return null;
            }
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                return TIPO_PNG;
            }
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return TIPO_JPEG;
            }
            return null;
        }

        private static string Recortar(string valor, int maximo) => valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
