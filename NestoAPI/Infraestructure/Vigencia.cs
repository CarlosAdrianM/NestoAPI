using NestoAPI.Models;
using System;
using System.Linq;

namespace NestoAPI.Infraestructure
{
    /// <summary>
    /// La vigencia de una fila que puede caducar sola, en UN solo sitio.
    ///
    /// Nacio con las campanas de descuento (#423) y la comparten las ofertas "6+2"
    /// (OfertasPermitidas), que tienen exactamente la misma semantica. Si cada tabla se
    /// escribiera la suya, acabarian discrepando en si el ultimo dia cuenta o no.
    ///
    /// Hasta ahora una fila de descuento valía para siempre: apagarla era borrarla o cambiarle el
    /// porcentaje a mano. Con <c>FechaDesde</c>/<c>FechaHasta</c> una campaña puede caducar sola,
    /// que es lo que hacían las reglas de catálogo de PrestaShop y lo que se pierde al traerse las
    /// campañas a Nesto.
    ///
    /// NULL = sin límite por ese lado, así que <b>NULL/NULL = siempre vigente</b>: las filas que ya
    /// existían siguen comportándose igual y la vigencia es opt-in. Las dos fechas son INCLUSIVAS y
    /// se comparan contra el DÍA, no contra el instante: una campaña con FechaHasta = 31/08 vale
    /// todo el día 31 y deja de valer el 1 de septiembre. Por eso la columna es <c>date</c>: no hay
    /// hora que se cuele ni campañas que caduquen a mitad de un pedido.
    ///
    /// Aplica a TODAS las filas, sea cual sea su nivel (producto, familia, familia+grupo,
    /// grupo+cliente, cliente, contacto, proveedor). La vigencia es una propiedad de la FILA, no
    /// del nivel: si solo la respetaran unos niveles, el motor de precios quedaría incoherente
    /// consigo mismo — un producto podría llevar caducado su descuento de producto y seguir
    /// arrastrando el de su familia.
    /// </summary>
    internal static class Vigencia
    {
        /// <summary>
        /// Filtra a las filas vigentes HOY. Es la sobrecarga que usan todos los sitios de
        /// producción; la del día explícito existe para los tests.
        /// </summary>
        internal static IQueryable<DescuentosProducto> Vigentes(IQueryable<DescuentosProducto> descuentos)
        {
            return Vigentes(descuentos, DateTime.Today);
        }

        /// <summary>
        /// Filtra a las filas vigentes un día concreto. Se compone sobre el <see cref="IQueryable"/>
        /// para que EF lo traduzca a SQL y el filtro viaje a la base de datos, no a memoria.
        /// </summary>
        internal static IQueryable<DescuentosProducto> Vigentes(IQueryable<DescuentosProducto> descuentos, DateTime dia)
        {
            return descuentos.Where(d => (d.FechaDesde == null || d.FechaDesde <= dia)
                                      && (d.FechaHasta == null || d.FechaHasta >= dia));
        }

        /// <summary>
        /// La misma regla para una fila suelta ya cargada en memoria. Hace falta porque
        /// <c>GestorPrecios.comprobarCondiciones</c> admite que le rellenen la lista desde fuera
        /// (los tests lo hacen), y ahí no hay consulta que filtrar.
        /// </summary>
        internal static bool EsVigente(IConVigencia fila)
        {
            return EsVigente(fila, DateTime.Today);
        }

        internal static bool EsVigente(IConVigencia fila, DateTime dia)
        {
            if (fila == null)
            {
                return false;
            }

            return (fila.FechaDesde == null || fila.FechaDesde <= dia)
                && (fila.FechaHasta == null || fila.FechaHasta >= dia);
        }
    }
}
