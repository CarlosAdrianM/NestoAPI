using System.Collections.Generic;
using System.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// Obtiene el correo electrónico de un cliente priorizando el contacto de agencia.
    /// Portado de Nesto VB.NET (Nesto.Models\CorreoCliente.vb).
    /// </summary>
    public class CorreoCliente
    {
        private const short CARGO_AGENCIA = 26;

        private readonly List<PersonaContactoCliente> listaPersonas;

        public CorreoCliente(ICollection<PersonaContactoCliente> listaPersonas)
        {
            this.listaPersonas = listaPersonas.ToList();
        }

        public string CorreoAgencia()
        {
            if (!listaPersonas.Any())
            {
                return string.Empty;
            }

            var personaAgencia = listaPersonas
                .FirstOrDefault(c => c.Cargo == CARGO_AGENCIA && !string.IsNullOrWhiteSpace(c.CorreoElectrónico));
            if (personaAgencia?.CorreoElectrónico != null)
            {
                var correo = personaAgencia.CorreoElectrónico.Trim();
                if (!string.IsNullOrWhiteSpace(correo))
                {
                    return correo;
                }
            }

            var personaCualquiera = listaPersonas
                .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.CorreoElectrónico));
            if (personaCualquiera?.CorreoElectrónico != null)
            {
                var correo = personaCualquiera.CorreoElectrónico.Trim();
                if (!string.IsNullOrWhiteSpace(correo))
                {
                    return correo;
                }
            }

            var primera = listaPersonas.FirstOrDefault();
            return primera?.CorreoElectrónico?.Trim() ?? string.Empty;
        }
    }
}
