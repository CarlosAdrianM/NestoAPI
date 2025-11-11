using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Sincronizacion
{
    /// <summary>
    /// Detecta cambios entre un cliente de Nesto y datos de sistemas externos
    /// CRÍTICO para prevenir bucles infinitos de sincronización
    /// </summary>
    public class ClienteChangeDetector
    {
        /// <summary>
        /// Detecta si hay cambios reales entre el cliente en Nesto y los datos externos
        /// </summary>
        /// <param name="clienteNesto">Cliente actual en la base de datos de Nesto</param>
        /// <param name="clienteExterno">Datos del cliente desde sistema externo</param>
        /// <returns>Lista de campos que han cambiado</returns>
        public List<string> DetectarCambios(Cliente clienteNesto, ExternalSyncMessageDTO clienteExterno)
        {
            var cambios = new List<string>();

            if (clienteNesto == null)
            {
                // Cliente nuevo - hay "cambios"
                return new List<string> { "CLIENTE_NUEVO" };
            }

            // Comparar cada campo, normalizando espacios y nulos
            if (!SonIguales(clienteNesto.Nombre, clienteExterno.Nombre))
            {
                cambios.Add($"Nombre: '{NormalizeString(clienteNesto.Nombre)}' → '{NormalizeString(clienteExterno.Nombre)}'");
            }

            if (!SonIguales(clienteNesto.Teléfono, clienteExterno.Telefono))
            {
                cambios.Add($"Teléfono: '{NormalizeString(clienteNesto.Teléfono)}' → '{NormalizeString(clienteExterno.Telefono)}'");
            }

            if (!SonIguales(clienteNesto.Dirección, clienteExterno.Direccion))
            {
                cambios.Add($"Dirección: '{NormalizeString(clienteNesto.Dirección)}' → '{NormalizeString(clienteExterno.Direccion)}'");
            }

            if (!SonIguales(clienteNesto.Población, clienteExterno.Poblacion))
            {
                cambios.Add($"Población: '{NormalizeString(clienteNesto.Población)}' → '{NormalizeString(clienteExterno.Poblacion)}'");
            }

            if (!SonIguales(clienteNesto.CodPostal, clienteExterno.CodigoPostal))
            {
                cambios.Add($"CodPostal: '{NormalizeString(clienteNesto.CodPostal)}' → '{NormalizeString(clienteExterno.CodigoPostal)}'");
            }

            if (!SonIguales(clienteNesto.Provincia, clienteExterno.Provincia))
            {
                cambios.Add($"Provincia: '{NormalizeString(clienteNesto.Provincia)}' → '{NormalizeString(clienteExterno.Provincia)}'");
            }

            if (!SonIguales(clienteNesto.CIF_NIF, clienteExterno.Nif))
            {
                cambios.Add($"CIF/NIF: '{NormalizeString(clienteNesto.CIF_NIF)}' → '{NormalizeString(clienteExterno.Nif)}'");
            }

            if (!SonIguales(clienteNesto.Comentarios, clienteExterno.Comentarios))
            {
                cambios.Add($"Comentarios: '{NormalizeString(clienteNesto.Comentarios)}' → '{NormalizeString(clienteExterno.Comentarios)}'");
            }

            return cambios;
        }

        /// <summary>
        /// Detecta cambios en una persona de contacto
        /// </summary>
        public List<string> DetectarCambiosPersonaContacto(
            PersonaContactoCliente personaNesto,
            PersonaContactoSyncDTO personaExterna)
        {
            var cambios = new List<string>();

            if (personaNesto == null)
            {
                return new List<string> { "PERSONA_CONTACTO_NUEVA" };
            }

            if (!SonIguales(personaNesto.Nombre, personaExterna.Nombre))
            {
                cambios.Add($"Nombre: '{NormalizeString(personaNesto.Nombre)}' → '{NormalizeString(personaExterna.Nombre)}'");
            }

            if (!SonIguales(personaNesto.Teléfono, personaExterna.Telefonos))
            {
                cambios.Add($"Teléfono: '{NormalizeString(personaNesto.Teléfono)}' → '{NormalizeString(personaExterna.Telefonos)}'");
            }

            if (!SonIguales(personaNesto.CorreoElectrónico, personaExterna.CorreoElectronico))
            {
                cambios.Add($"Email: '{NormalizeString(personaNesto.CorreoElectrónico)}' → '{NormalizeString(personaExterna.CorreoElectronico)}'");
            }

            return cambios;
        }

        /// <summary>
        /// Compara dos strings normalizados (trim + null = empty)
        /// </summary>
        private bool SonIguales(string valor1, string valor2)
        {
            var normalizado1 = NormalizeString(valor1);
            var normalizado2 = NormalizeString(valor2);

            return normalizado1 == normalizado2;
        }

        /// <summary>
        /// Normaliza un string para comparación
        /// - Trim de espacios
        /// - null se convierte a string vacío
        /// - Case-insensitive
        /// </summary>
        private string NormalizeString(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                return string.Empty;
            }

            return valor.Trim().ToUpperInvariant();
        }
    }
}
