using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// Implementación de tipo de ruta para rutas propias.
    ///
    /// RUTAS CONTENIDAS: AT, 16
    ///
    /// COMPORTAMIENTO:
    /// - Siempre imprime 2 copias (original + 1 copia)
    /// - Independiente de si tiene comentario de impresión o no
    /// - Independiente de si fue traspasado de empresa o no
    /// </summary>
    public class RutaPropia : ITipoRuta
    {
        // Lista de rutas que se consideran propias
        private static readonly List<string> rutasPropias = new List<string>
        {
            "AT",
            "16"
            // Para agregar más rutas propias, simplemente agregar aquí
        };

        public string Id => "PROPIA";

        public string NombreParaMostrar => "Ruta Propia";

        public string Descripcion => "Siempre imprime 2 copias (original + 1 copia), independientemente de comentarios o empresa.";

        public IReadOnlyList<string> RutasContenidas => rutasPropias.AsReadOnly();

        /// <summary>
        /// Verifica si un número de ruta pertenece a las rutas propias.
        /// La comparación es case-insensitive y elimina espacios.
        /// </summary>
        public bool ContieneRuta(string numeroRuta)
        {
            if (string.IsNullOrWhiteSpace(numeroRuta))
            {
                return false;
            }

            string rutaNormalizada = numeroRuta.Trim().ToUpperInvariant();
            return rutasPropias.Any(r => r.Equals(rutaNormalizada, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Para rutas propias, SIEMPRE se imprimen 2 copias.
        /// </summary>
        public int ObtenerNumeroCopias(CabPedidoVta pedido, bool debeImprimirDocumento, string empresaPorDefecto)
        {
            // Rutas propias: siempre 2 copias (original + 1 copia)
            return 2;
        }

        /// <summary>
        /// Obtiene la bandeja de impresión según el tipo de documento y si fue traspasado.
        /// Usa tipos estándar compatibles con cualquier impresora.
        /// - Facturas no traspasadas (en empresa por defecto): Lower (bandeja inferior)
        /// - Resto de documentos (albaranes, notas de entrega, facturas traspasadas): Middle (bandeja media)
        /// </summary>
        public TipoBandejaImpresion ObtenerBandeja(CabPedidoVta pedido, bool esFactura, string empresaPorDefecto)
        {
            if (pedido == null)
            {
                return TipoBandejaImpresion.Middle;
            }

            // Si es factura Y está en la empresa por defecto (no traspasada): Lower (bandeja inferior)
            bool estaEnEmpresaPorDefecto = pedido.Empresa?.Trim() == empresaPorDefecto?.Trim();
            if (esFactura && estaEnEmpresaPorDefecto)
            {
                return TipoBandejaImpresion.Lower;
            }

            // Resto de casos: Middle (bandeja media)
            return TipoBandejaImpresion.Middle;
        }

        /// <summary>
        /// Las rutas propias SÍ requieren insertar en ExtractoRuta para registro contable.
        /// </summary>
        public bool DebeInsertarEnExtractoRuta()
        {
            return true;
        }
    }
}
