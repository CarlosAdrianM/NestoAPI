using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System.Linq;
using System.Reflection;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#819 (sugerencia 1049, Alfredo 05/11/26): el asistente de Nesto mandó un correo con muchos espacios al final,
    /// pasaba de 50 caracteres y el alta fallaba. Los espacios de los extremos nunca significan nada (la BD rellena los char),
    /// así que se recortan TODOS los textos del alta/modificación y de sus personas de contacto antes de validar.
    /// Por reflexión para que un campo nuevo quede cubierto sin acordarse de tocar esto. Null se queda null.
    /// </summary>
    public static class NormalizadorClienteCrear
    {
        public static void Normalizar(ClienteCrear cliente)
        {
            if (cliente == null)
            {
                return;
            }
            RecortarTextos(cliente);
            if (cliente.PersonasContacto != null)
            {
                foreach (PersonaContactoDTO persona in cliente.PersonasContacto.Where(p => p != null))
                {
                    RecortarTextos(persona);
                }
            }
        }

        private static void RecortarTextos(object objeto)
        {
            foreach (PropertyInfo propiedad in objeto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (propiedad.PropertyType != typeof(string) || !propiedad.CanRead || !propiedad.CanWrite
                    || propiedad.GetIndexParameters().Length > 0)
                {
                    continue;
                }
                string valor = (string)propiedad.GetValue(objeto);
                if (valor != null)
                {
                    propiedad.SetValue(objeto, valor.Trim());
                }
            }
        }
    }
}
