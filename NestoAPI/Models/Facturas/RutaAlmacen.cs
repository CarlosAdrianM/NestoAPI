using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Implementación de tipo de ruta para rutas de almacén (pruebas).
    ///
    /// RUTAS CONTENIDAS: AM
    ///
    /// COMPORTAMIENTO:
    /// - No imprime copias (0 copias)
    /// - SÍ inserta en ExtractoRuta (para pruebas de contabilidad)
    /// - Útil para pruebas de facturación sin necesidad de imprimir
    /// </summary>
    public class RutaAlmacen : ITipoRuta
    {
        // Lista de rutas de almacén
        private static readonly List<string> rutasAlmacen = new List<string>
        {
            "AM"
            // Para agregar más rutas de almacén, simplemente agregar aquí
        };

        public string Id => "ALMACEN";

        public string NombreParaMostrar => "Ruta Almacén";

        public string Descripcion => "Ruta de prueba. No imprime copias. Inserta en ExtractoRuta para pruebas de contabilidad.";

        public IReadOnlyList<string> RutasContenidas => rutasAlmacen.AsReadOnly();

        /// <summary>
        /// Verifica si un número de ruta pertenece a las rutas de almacén.
        /// La comparación es case-insensitive y elimina espacios.
        /// </summary>
        public bool ContieneRuta(string numeroRuta)
        {
            if (string.IsNullOrWhiteSpace(numeroRuta))
            {
                return false;
            }

            string rutaNormalizada = numeroRuta.Trim().ToUpperInvariant();
            return rutasAlmacen.Any(r => r.Equals(rutaNormalizada, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Para rutas de almacén, NO se imprime (0 copias).
        /// Es una ruta de prueba que no requiere impresión.
        /// </summary>
        public int ObtenerNumeroCopias(CabPedidoVta pedido, bool debeImprimirDocumento, string empresaPorDefecto)
        {
            // Rutas de almacén: 0 copias (no imprimir)
            return 0;
        }

        public string ObtenerBandeja()
        {
            return "Default";
        }

        /// <summary>
        /// Las rutas de almacén SÍ requieren insertar en ExtractoRuta.
        /// Esto permite probar el flujo completo de contabilización.
        /// </summary>
        public bool DebeInsertarEnExtractoRuta()
        {
            return true;
        }
    }
}
