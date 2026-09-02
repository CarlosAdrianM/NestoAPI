using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#447: quién puede cambiar, desde la app de clientes, el nivel de las personas de
    /// contacto de su centro, y a qué. Solo el TITULAR (una persona con cargo 22, factura
    /// electrónica) gestiona; solo entre los cargos 22 (ve todo), 31 (precios sin descuentos) y
    /// 30 (ni precios ni descuentos); y nunca puede dejar al cliente sin un titular con correo,
    /// que sería bloquearse a sí mismo. Reglas puras, sin base de datos.
    /// </summary>
    public static class PoliticaPersonasContacto
    {
        public static readonly short[] CargosGestionables =
        {
            Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO,
            Constantes.Clientes.PersonasContacto.CARGO_PEDIDOS_SIN_DESCUENTOS,
            Constantes.Clientes.PersonasContacto.CARGO_PEDIDOS_SIN_PRECIOS
        };

        public static bool EsTitular(IEnumerable<PersonaContactoCliente> personasDelCliente, string email)
        {
            return DeEsteEmail(personasDelCliente, email)
                .Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO);
        }

        /// <summary>
        /// Null si el cambio es válido; si no, el motivo para el cliente.
        /// </summary>
        public static string MotivoParaNoCambiar(IEnumerable<PersonaContactoCliente> personasDelCliente,
            PersonaContactoCliente persona, short cargoNuevo)
        {
            if (persona == null)
            {
                return "No encontramos esa persona de contacto en tu centro.";
            }
            if (!CargosGestionables.Contains(cargoNuevo))
            {
                return "Desde la app solo se puede elegir entre 'Ve precios y descuentos', 'Ve precios, no descuentos' y 'Solo pide, sin precios'.";
            }
            if (persona.Cargo == cargoNuevo)
            {
                return null;
            }
            bool dejaDeSerTitular = persona.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO
                && cargoNuevo != Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO;
            if (dejaDeSerTitular && !QuedaOtroTitularConCorreo(personasDelCliente, persona))
            {
                return "Tu centro se quedaría sin ninguna persona que vea las facturas y pueda gestionar a las demás. Da ese permiso a otra persona antes.";
            }
            return null;
        }

        public static string TextoNivel(short? cargo)
        {
            if (cargo == Constantes.Clientes.PersonasContacto.CARGO_PEDIDOS_SIN_PRECIOS)
            {
                return "Solo pide, sin precios";
            }
            if (cargo == Constantes.Clientes.PersonasContacto.CARGO_PEDIDOS_SIN_DESCUENTOS)
            {
                return "Ve precios, no descuentos";
            }
            if (cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
            {
                return "Ve todo y gestiona (facturas)";
            }
            return "Ve precios y descuentos";
        }

        private static bool QuedaOtroTitularConCorreo(IEnumerable<PersonaContactoCliente> personas, PersonaContactoCliente laQueCambia)
        {
            return (personas ?? Enumerable.Empty<PersonaContactoCliente>())
                .Where(p => !MismaPersona(p, laQueCambia))
                .Any(p => p.Cargo == Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO
                    && !string.IsNullOrWhiteSpace(p.CorreoElectrónico));
        }

        private static bool MismaPersona(PersonaContactoCliente a, PersonaContactoCliente b)
        {
            return string.Equals(a.Contacto?.Trim(), b.Contacto?.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Número?.Trim(), b.Número?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<PersonaContactoCliente> DeEsteEmail(IEnumerable<PersonaContactoCliente> personas, string email)
        {
            if (personas == null || string.IsNullOrWhiteSpace(email))
            {
                return Enumerable.Empty<PersonaContactoCliente>();
            }
            string correo = email.Trim();
            return personas.Where(p => string.Equals(p.CorreoElectrónico?.Trim(), correo, StringComparison.OrdinalIgnoreCase));
        }
    }
}
