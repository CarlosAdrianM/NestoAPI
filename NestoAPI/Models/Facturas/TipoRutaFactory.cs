using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Factory para obtener implementaciones de ITipoRuta.
    /// Permite agregar nuevos tipos de ruta de forma extensible:
    /// simplemente agregue una nueva implementación al registro.
    /// </summary>
    public static class TipoRutaFactory
    {
        // Registro de todos los tipos de ruta disponibles
        private static readonly List<ITipoRuta> tiposRutaRegistrados = new List<ITipoRuta>
        {
            new RutaPropia(),
            new RutaAgencia(),
            new RutaAlmacen()
            // Para agregar un nuevo tipo de ruta, simplemente agregue una nueva línea aquí:
            // new MiNuevoTipoRuta()
        };

        /// <summary>
        /// Obtiene todos los tipos de ruta disponibles.
        /// Útil para generar UI dinámica (radio buttons, dropdowns, etc.)
        /// </summary>
        public static IEnumerable<ITipoRuta> ObtenerTodosLosTipos()
        {
            return tiposRutaRegistrados.AsReadOnly();
        }

        /// <summary>
        /// Obtiene todas las rutas manejadas por el sistema (unión de todas las implementaciones).
        /// Útil para filtrar pedidos: solo procesar pedidos cuya ruta esté en esta lista.
        /// </summary>
        /// <returns>Lista de números de ruta manejados por el sistema</returns>
        public static IEnumerable<string> ObtenerTodasLasRutasManejadas()
        {
            var rutas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tipo in tiposRutaRegistrados)
            {
                foreach (var ruta in tipo.RutasContenidas)
                {
                    rutas.Add(ruta);
                }
            }

            return rutas.ToList();
        }

        /// <summary>
        /// Obtiene un tipo de ruta específico por su ID.
        /// </summary>
        /// <param name="id">ID del tipo de ruta (ej: "PROPIA", "AGENCIA")</param>
        /// <returns>Implementación de ITipoRuta correspondiente</returns>
        /// <exception cref="ArgumentException">Si el ID no existe</exception>
        public static ITipoRuta ObtenerPorId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("El ID del tipo de ruta no puede ser null o vacío", nameof(id));
            }

            var tipoRuta = tiposRutaRegistrados.FirstOrDefault(t =>
                t.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

            if (tipoRuta == null)
            {
                throw new ArgumentException(
                    $"No existe un tipo de ruta con ID '{id}'. " +
                    $"Tipos disponibles: {string.Join(", ", tiposRutaRegistrados.Select(t => t.Id))}",
                    nameof(id));
            }

            return tipoRuta;
        }

        /// <summary>
        /// Obtiene el tipo de ruta apropiado para un número de ruta específico.
        /// Busca en todos los tipos registrados y devuelve el que contiene la ruta.
        /// </summary>
        /// <param name="numeroRuta">Número de ruta del pedido (ej: "AT", "16", "00", "FW")</param>
        /// <returns>Implementación de ITipoRuta apropiada, o null si la ruta no está manejada</returns>
        public static ITipoRuta ObtenerPorNumeroRuta(string numeroRuta)
        {
            if (string.IsNullOrWhiteSpace(numeroRuta))
            {
                return null;
            }

            // Buscar el tipo que contenga la ruta
            foreach (var tipo in tiposRutaRegistrados)
            {
                if (tipo.ContieneRuta(numeroRuta))
                {
                    return tipo;
                }
            }

            // Si ningún tipo la contiene, devolver null
            // El código que llama debe manejar este caso (ej: no procesar el pedido)
            return null;
        }

        /// <summary>
        /// Verifica si existe un tipo de ruta con el ID especificado.
        /// </summary>
        public static bool Existe(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            return tiposRutaRegistrados.Any(t =>
                t.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Verifica si un número de ruta está manejado por alguna implementación.
        /// </summary>
        /// <param name="numeroRuta">Número de ruta a verificar</param>
        /// <returns>True si la ruta está en alguna implementación</returns>
        public static bool EstaRutaManejada(string numeroRuta)
        {
            return ObtenerPorNumeroRuta(numeroRuta) != null;
        }
    }
}
