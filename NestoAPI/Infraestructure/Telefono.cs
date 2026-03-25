using System;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Extrae teléfono fijo y móvil de una cadena con múltiples números separados por "/".
    /// Portado de Nesto VB.NET (Nesto.Models\Telefono.vb).
    /// </summary>
    public class Telefono
    {
        private static readonly string[] Separadores = { "/" };
        private readonly string[] telefonos;

        public Telefono(string listaTelefonos)
        {
            if (string.IsNullOrEmpty(listaTelefonos))
            {
                telefonos = Array.Empty<string>();
                return;
            }

            listaTelefonos = listaTelefonos
                .Replace("(", "")
                .Replace(")", "")
                .Replace(" ", "")
                .Replace("-", "");

            telefonos = listaTelefonos.Split(Separadores, StringSplitOptions.RemoveEmptyEntries);
        }

        public string FijoUnico()
        {
            foreach (var t in telefonos)
            {
                if (t.Length >= 9 && t[0] == '9')
                {
                    return t.Substring(0, 9);
                }
            }
            return string.Empty;
        }

        public string MovilUnico()
        {
            foreach (var t in telefonos)
            {
                if (t.Length >= 9 && (t[0] == '6' || t[0] == '7' || t[0] == '8'))
                {
                    return t.Substring(0, 9);
                }
            }
            return string.Empty;
        }
    }
}
