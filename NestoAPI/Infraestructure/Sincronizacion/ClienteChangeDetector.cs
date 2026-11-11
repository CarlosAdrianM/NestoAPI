using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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
        public List<string> DetectarCambios(Cliente clienteNesto, ClienteSyncMessage clienteExterno)
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

            if (!SonIgualesComentarios(clienteNesto.Comentarios, clienteExterno.Comentarios))
            {
                cambios.Add($"Comentarios: '{NormalizeComentarios(clienteNesto.Comentarios)}' → '{NormalizeComentarios(clienteExterno.Comentarios)}'");
            }

            // Detectar cambio de vendedor (por código o por email)
            if (!string.IsNullOrWhiteSpace(clienteExterno.Vendedor) &&
                !SonIguales(clienteNesto.Vendedor, clienteExterno.Vendedor))
            {
                cambios.Add($"Vendedor: '{NormalizeString(clienteNesto.Vendedor)}' → '{NormalizeString(clienteExterno.Vendedor)}'");
            }

            return cambios;
        }

        /// <summary>
        /// Los campos del cliente que son distintos (sin el vendedor), con los nombres de <see cref="BarreraClientesExternos"/>.
        /// Misma comparación que <see cref="DetectarCambios"/>.
        /// </summary>
        public HashSet<string> CamposDistintos(Cliente clienteNesto, ClienteSyncMessage clienteExterno)
        {
            var campos = new HashSet<string>();
            if (clienteNesto == null || clienteExterno == null)
            {
                return campos;
            }
            if (!SonIguales(clienteNesto.Nombre, clienteExterno.Nombre)) campos.Add(BarreraClientesExternos.CAMPO_NOMBRE);
            if (!SonIguales(clienteNesto.Teléfono, clienteExterno.Telefono)) campos.Add(BarreraClientesExternos.CAMPO_TELEFONO);
            if (!SonIguales(clienteNesto.Dirección, clienteExterno.Direccion)) campos.Add(BarreraClientesExternos.CAMPO_DIRECCION);
            if (!SonIguales(clienteNesto.Población, clienteExterno.Poblacion)) campos.Add(BarreraClientesExternos.CAMPO_POBLACION);
            if (!SonIguales(clienteNesto.CodPostal, clienteExterno.CodigoPostal)) campos.Add(BarreraClientesExternos.CAMPO_CODIGO_POSTAL);
            if (!SonIguales(clienteNesto.Provincia, clienteExterno.Provincia)) campos.Add(BarreraClientesExternos.CAMPO_PROVINCIA);
            if (!SonIguales(clienteNesto.CIF_NIF, clienteExterno.Nif)) campos.Add(BarreraClientesExternos.CAMPO_NIF);
            if (!SonIgualesComentarios(clienteNesto.Comentarios, clienteExterno.Comentarios)) campos.Add(BarreraClientesExternos.CAMPO_COMENTARIOS);
            return campos;
        }

        /// <summary>Los campos distintos de una persona de contacto (Nombre, Teléfono, Email).</summary>
        public HashSet<string> CamposDistintosPersonaContacto(PersonaContactoCliente personaNesto, PersonaContactoSyncDTO personaExterna)
        {
            var campos = new HashSet<string>();
            if (personaNesto == null || personaExterna == null)
            {
                return campos;
            }
            if (!SonIguales(personaNesto.Nombre, personaExterna.Nombre)) campos.Add(BarreraClientesExternos.CAMPO_NOMBRE);
            if (!SonIguales(personaNesto.Teléfono, personaExterna.Telefonos)) campos.Add(BarreraClientesExternos.CAMPO_TELEFONO);
            if (!SonIguales(personaNesto.CorreoElectrónico, personaExterna.CorreoElectronico)) campos.Add(BarreraClientesExternos.CAMPO_EMAIL);
            return campos;
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

        /// <summary>
        /// Compara dos comentarios normalizando HTML y orden de líneas
        /// </summary>
        private bool SonIgualesComentarios(string comentario1, string comentario2)
        {
            var normalizado1 = NormalizeComentarios(comentario1);
            var normalizado2 = NormalizeComentarios(comentario2);

            return normalizado1 == normalizado2;
        }

        /// <summary>
        /// Normaliza comentarios para comparación:
        /// - Quita etiquetas HTML (<p>, </p>, etc.)
        /// - Normaliza saltos de línea (\r\n → \n)
        /// - Ordena las líneas alfabéticamente para evitar falsos positivos por diferente orden
        /// - Trim y mayúsculas
        /// </summary>
        private string NormalizeComentarios(string comentario)
        {
            if (string.IsNullOrWhiteSpace(comentario))
            {
                return string.Empty;
            }

            // Quitar etiquetas HTML
            string sinHtml = Regex.Replace(comentario, @"<[^>]+>", string.Empty);

            // Normalizar saltos de línea
            sinHtml = sinHtml.Replace("\r\n", "\n").Replace("\r", "\n");

            // Dividir en líneas, ordenar alfabéticamente, y volver a unir
            var lineas = sinHtml.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(linea => linea.Trim())
                .Where(linea => !string.IsNullOrWhiteSpace(linea))
                .OrderBy(linea => linea)
                .ToList();

            // Unir líneas ordenadas
            string resultado = string.Join("\n", lineas);

            return resultado.Trim().ToUpperInvariant();
        }
    }
}
