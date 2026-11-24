using NestoAPI.Models;
using System;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#797 (Almacén, 03/11/26): las muestras (subgrupo MMP) que no traen código de barras del fabricante no se
    /// etiquetan: se preparan, se envían y se reciben tecleando la referencia o la cantidad, sin avisos. La regla única de
    /// <c>SinCodigoAdmitido</c> en todas las líneas que llevan <c>SinCodigo</c>.
    /// <para>NestoAPI#884 (Carlos 24/11/26): la muestra sin código de fabricante recibe el interno del 29 y su etiqueta va en el
    /// cajón o la gaveta, no en la unidad. Leerla es la vía cómoda, NO obligatoria: la muestra se puede seguir tecleando sin aviso.
    /// Por eso «sin código» es el de la ficha (<c>Productos.CodBarras</c>), donde el interno nunca va (no puede ser el principal):
    /// una muestra cuyo único código es el interno sigue siendo «admitida». Y una lectura de una muestra (de su etiqueta de
    /// contenedor o de su envase) no es una unidad: <see cref="PideCantidad"/>.</para>
    /// </summary>
    public static class MuestrasSinCodigo
    {
        /// <summary>El subgrupo es el de las muestras (MMP), con el relleno de los char y sin mirar mayúsculas.</summary>
        public static bool EsMuestra(string subGrupo)
        {
            return string.Equals(subGrupo?.Trim(), Constantes.Productos.SUBGRUPO_MUESTRAS, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sin código (el de la ficha, que nunca es un interno del 29) y muestra. Nunca true si el producto tiene código del
        /// fabricante en la ficha. NestoAPI#884: el interno no cuenta, la muestra se sigue pudiendo teclear.
        /// </summary>
        public static bool Admitido(bool sinCodigo, string subGrupo)
        {
            return sinCodigo && EsMuestra(subGrupo);
        }

        /// <summary>
        /// NestoAPI#884: la lectura de este producto no es una unidad: es una muestra y lo leído es casi siempre la etiqueta de su
        /// cajón o gaveta (una por referencia). La app tiene que pedir SIEMPRE la cantidad. Hoy = es muestra (MMP).
        /// </summary>
        public static bool PideCantidad(string subGrupo)
        {
            return EsMuestra(subGrupo);
        }
    }
}
