using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Facturas
{
    /// <summary>
    /// NestoAPI#806: LA regla de los documentos en papel, una sola para todos los caminos que facturan (Facturar rutas,
    /// FacturarPedido de Agencias y de la etiqueta del packing, «Crear factura» del detalle del pedido): si se imprime (las frases
    /// «Factura física», «Factura en papel» o «Albarán físico» en los comentarios o en el comentario de picking), cuántas copias y
    /// en qué bandeja (según el tipo de ruta: <see cref="ITipoRuta.ObtenerNumeroCopias"/> y <see cref="ITipoRuta.ObtenerBandeja"/>).
    /// Antes vivía en GestorFacturacionRutas.DebeImprimirDocumento, que ahora la llama.
    /// </summary>
    public static class ReglaImpresionDocumentos
    {
        private static readonly string[] FRASES_IMPRESION = { "factura fisica", "factura en papel", "albaran fisico" };

        /// <summary>Busca las frases en los comentarios y en el comentario de picking (sugerencia 565: casi siempre está en este).</summary>
        public static bool DebeImprimir(CabPedidoVta pedido)
        {
            return pedido != null && DebeImprimir($"{pedido.Comentarios} {pedido.ComentarioPicking}");
        }

        /// <summary>Las frases, sin distinguir mayúsculas ni tildes.</summary>
        public static bool DebeImprimir(string comentarios)
        {
            if (string.IsNullOrWhiteSpace(comentarios))
            {
                return false;
            }
            string normalizados = QuitarTildes(comentarios.ToLower());
            return FRASES_IMPRESION.Any(frase => normalizados.Contains(frase));
        }

        /// <summary>Las copias del documento del pedido (0 = no se imprime: sin frase, traspasado o ruta sin tipo).</summary>
        public static int NumeroCopias(CabPedidoVta pedido)
        {
            ITipoRuta tipoRuta = pedido == null ? null : TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            return tipoRuta != null
                ? tipoRuta.ObtenerNumeroCopias(pedido, DebeImprimir(pedido), Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : 0;
        }

        /// <summary>La bandeja del documento (Middle si la ruta no tiene tipo, como en Facturar rutas).</summary>
        public static TipoBandejaImpresion Bandeja(CabPedidoVta pedido, bool esFactura)
        {
            ITipoRuta tipoRuta = pedido == null ? null : TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
            return tipoRuta != null
                ? tipoRuta.ObtenerBandeja(pedido, esFactura, Constantes.Empresas.EMPRESA_POR_DEFECTO)
                : TipoBandejaImpresion.Middle;
        }

        private static string QuitarTildes(string texto)
        {
            string descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    _ = sb.Append(c);
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
