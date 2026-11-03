using NestoAPI.Models;
using System;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#797 (Almacén, 03/11/26): las muestras (subgrupo MMP) que no traen código de barras del fabricante no se
    /// etiquetan: se preparan, se envían y se reciben tecleando la referencia o la cantidad, sin avisos. La regla única de
    /// <c>SinCodigoAdmitido</c> en todas las líneas que llevan <c>SinCodigo</c>.
    /// </summary>
    public static class MuestrasSinCodigo
    {
        /// <summary>El subgrupo es el de las muestras (MMP), con el relleno de los char y sin mirar mayúsculas.</summary>
        public static bool EsMuestra(string subGrupo)
        {
            return string.Equals(subGrupo?.Trim(), Constantes.Productos.SUBGRUPO_MUESTRAS, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Sin código y muestra. Nunca true si el producto tiene código.</summary>
        public static bool Admitido(bool sinCodigo, string subGrupo)
        {
            return sinCodigo && EsMuestra(subGrupo);
        }
    }
}
