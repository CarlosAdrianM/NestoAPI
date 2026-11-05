using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Web.Http.ModelBinding;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#819 (sugerencia 1049, Alfredo 05/11/26): Nesto solo enseña el Message del 400 y BadRequest(ModelState) da
    /// «La solicitud no es válida», así que el usuario no sabía qué corregir. Esto construye un texto en castellano, de usted,
    /// con el campo en lenguaje de usuario, el límite y la longitud que tiene, y todos los errores juntos.
    /// Funciones puras para poder probarlas sin el pipeline de Web API.
    /// </summary>
    public static class MensajesValidacionCliente
    {
        public const string SIN_DATOS = "No han llegado los datos del cliente.";
        public const string SEPARADOR = "\n";

        // Nombre técnico de la propiedad → cómo lo llama el usuario. Lo que no esté aquí usa el ErrorMessage del atributo.
        private static readonly Dictionary<string, string> CamposCliente = new Dictionary<string, string>
        {
            { nameof(ClienteCrear.Nombre), "el nombre fiscal" },
            { nameof(ClienteCrear.Direccion), "la dirección" },
            { nameof(ClienteCrear.Poblacion), "la población" },
            { nameof(ClienteCrear.Provincia), "la provincia" },
            { nameof(ClienteCrear.Telefono), "el teléfono" },
            { nameof(ClienteCrear.Pais), "el país" },
            { nameof(ClienteCrear.ComentariosPicking), "el comentario de picking" },
            { nameof(ClienteCrear.ComentariosRuta), "el comentario de ruta" },
        };

        private static readonly Dictionary<string, string> CamposPersona = new Dictionary<string, string>
        {
            { nameof(PersonaContactoDTO.Nombre), "el nombre de la persona de contacto" },
            { nameof(PersonaContactoDTO.CorreoElectronico), "el correo electrónico de la persona de contacto" },
            { nameof(PersonaContactoDTO.Telefono), "el teléfono de la persona de contacto" },
        };

        /// <summary>
        /// Los errores de validación (atributos de ClienteCrear y de cada PersonaContactoDTO) en un solo texto,
        /// o null si todo está bien.
        /// </summary>
        public static string Desde(ClienteCrear cliente)
        {
            if (cliente == null)
            {
                return SIN_DATOS;
            }
            var errores = new List<string>();
            errores.AddRange(ErroresDe(cliente, CamposCliente, null));
            if (cliente.PersonasContacto != null)
            {
                List<PersonaContactoDTO> personas = cliente.PersonasContacto.Where(p => p != null).ToList();
                foreach (PersonaContactoDTO persona in personas)
                {
                    // Con varias personas se dice cuál, para que el usuario sepa a quién corregir.
                    string cual = personas.Count > 1 && !string.IsNullOrWhiteSpace(persona.Nombre)
                        ? $" «{persona.Nombre.Trim()}»"
                        : null;
                    errores.AddRange(ErroresDe(persona, CamposPersona, cual));
                }
            }
            return errores.Count == 0 ? null : string.Join(SEPARADOR, errores);
        }

        /// <summary>
        /// Los datos que el binding no pudo leer (un número con letras, una fecha mal escrita…): esos errores llevan
        /// excepción y no dependen de los espacios. Los de los atributos se ignoran aquí porque se calcularon sin recortar.
        /// Null si no hay ninguno.
        /// </summary>
        public static string DatosIlegibles(ModelStateDictionary modelState)
        {
            if (modelState == null)
            {
                return null;
            }
            List<string> errores = modelState
                .Where(e => e.Value.Errors.Any(error => error.Exception != null))
                .Select(e => $"El dato «{NombreCampo(e.Key)}» no tiene un formato válido.")
                .Distinct()
                .ToList();
            return errores.Count == 0 ? null : string.Join(SEPARADOR, errores);
        }

        // «clienteCrear.PersonasContacto[0].Telefono» → «Telefono»
        private static string NombreCampo(string clave)
        {
            if (string.IsNullOrEmpty(clave))
            {
                return "cliente";
            }
            int punto = clave.LastIndexOf('.');
            return punto >= 0 ? clave.Substring(punto + 1) : clave;
        }

        private static IEnumerable<string> ErroresDe(object objeto, Dictionary<string, string> campos, string cual)
        {
            foreach (PropertyInfo propiedad in objeto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!propiedad.CanRead || propiedad.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                object valor = propiedad.GetValue(objeto);
                string campo = campos.TryGetValue(propiedad.Name, out string nombre) ? nombre + cual : null;
                foreach (ValidationAttribute atributo in propiedad.GetCustomAttributes<ValidationAttribute>(true))
                {
                    if (!atributo.IsValid(valor))
                    {
                        yield return Mensaje(atributo, propiedad.Name, campo, valor as string);
                    }
                }
            }
        }

        private static string Mensaje(ValidationAttribute atributo, string propiedad, string campo, string texto)
        {
            if (atributo is StringLengthAttribute largo && texto != null && texto.Length > largo.MaximumLength)
            {
                return campo != null
                    ? $"{Mayuscula(campo)} no puede superar {largo.MaximumLength} caracteres (tiene {texto.Length})."
                    : $"{atributo.FormatErrorMessage(propiedad).TrimEnd().TrimEnd('.')} (tiene {texto.Length}).";
            }
            if (atributo is EmailAddressAttribute && texto != null)
            {
                return $"El correo electrónico \"{texto}\" no es válido.";
            }
            return atributo.FormatErrorMessage(campo != null ? Mayuscula(campo) : propiedad);
        }

        private static string Mayuscula(string texto)
        {
            return string.IsNullOrEmpty(texto) ? texto : char.ToUpper(texto[0]) + texto.Substring(1);
        }
    }
}
