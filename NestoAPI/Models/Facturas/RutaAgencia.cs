using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Implementación de tipo de ruta para rutas de agencias.
    ///
    /// RUTAS CONTENIDAS: 00, FW
    ///
    /// COMPORTAMIENTO:
    /// - Si el pedido fue traspasado de empresa (empresa != empresa por defecto): 0 copias
    /// - Si el pedido está en empresa por defecto:
    ///   - Si tiene comentario de impresión ("factura física", "albarán físico", etc.): 1 copia (solo original)
    ///   - Si NO tiene comentario de impresión: 0 copias
    /// </summary>
    public class RutaAgencia : ITipoRuta
    {
        // Lista de rutas de agencias
        private static readonly List<string> rutasAgencia = new List<string>
        {
            "00",
            "FW"
            // Para agregar más rutas de agencias, simplemente agregar aquí
        };

        public string Id => "AGENCIA";

        public string NombreParaMostrar => "Ruta de Agencias";

        public string Descripcion => "Imprime 1 copia (solo original) si está en empresa por defecto y tiene comentario de impresión física. " +
                                     "No imprime si fue traspasado o no tiene comentario.";

        public IReadOnlyList<string> RutasContenidas => rutasAgencia.AsReadOnly();

        /// <summary>
        /// Verifica si un número de ruta pertenece a las rutas de agencias.
        /// La comparación es case-insensitive y elimina espacios.
        /// </summary>
        public bool ContieneRuta(string numeroRuta)
        {
            if (string.IsNullOrWhiteSpace(numeroRuta))
            {
                return false;
            }

            string rutaNormalizada = numeroRuta.Trim().ToUpperInvariant();
            return rutasAgencia.Any(r => r.Equals(rutaNormalizada, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Para rutas de agencias:
        /// - Pedidos traspasados (empresa != empresa por defecto): 0 copias
        /// - Pedidos en empresa por defecto:
        ///   - Con comentario de impresión: 1 copia (solo original)
        ///   - Sin comentario: 0 copias
        /// </summary>
        public int ObtenerNumeroCopias(CabPedidoVta pedido, bool debeImprimirDocumento, string empresaPorDefecto)
        {
            if (pedido == null)
            {
                return 0;
            }

            // Si el pedido fue traspasado a otra empresa, NO imprimir
            bool estaEnEmpresaPorDefecto = pedido.Empresa?.Trim() == empresaPorDefecto?.Trim();
            if (!estaEnEmpresaPorDefecto)
            {
                return 0; // Pedido traspasado: 0 copias
            }

            // Si está en empresa por defecto, verificar comentario de impresión
            if (debeImprimirDocumento)
            {
                return 1; // Solo el original
            }

            return 0; // Sin comentario: 0 copias
        }

        public string ObtenerBandeja()
        {
            return "Default";
        }

        /// <summary>
        /// Las rutas de agencias NO requieren insertar en ExtractoRuta.
        /// El registro contable se hace de forma diferente para estas rutas.
        /// </summary>
        public bool DebeInsertarEnExtractoRuta()
        {
            return false;
        }
    }
}
