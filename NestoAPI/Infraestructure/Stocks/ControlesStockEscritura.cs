using NestoAPI.Models;
using System;

namespace NestoAPI.Infraestructure.Stocks
{
    /// <summary>
    /// Cómo se cambia el máximo y el mínimo de un control de stock: lo comparten PUT/POST api/ControlesStock (la ficha del
    /// producto en Nesto) y las decisiones de las propuestas de stock (NestoAPI#631), para que los dos caminos dejen igual el
    /// usuario y la fecha de modificación. No guarda: el que llama hace el SaveChanges.
    /// </summary>
    public static class ControlesStockEscritura
    {
        /// <summary>ControlesStock.Usuario es varchar(30).</summary>
        internal const int LONGITUD_USUARIO = 30;
        /// <summary>Estacionalidad «plana»: la que llevan casi todos (el campo no se mantiene, #628).</summary>
        internal const string ESTACIONALIDAD_PLANA = "111111111111";

        /// <summary>
        /// NestoAPI#667: solo Algete tiene stock mínimo (lo mantiene Compras); en las tiendas solo hay máximo. Fuera de Algete
        /// el mínimo es siempre 0, lo mande quien lo mande (el 16/10/26 «Actualizar Stocks por Proveedor» lo grabó en REI y ALC).
        /// </summary>
        public static int MinimoDelAlmacen(string almacen, int minimo)
        {
            return string.Equals(almacen?.Trim(), Constantes.Almacenes.ALGETE, StringComparison.OrdinalIgnoreCase) ? minimo : 0;
        }

        public static void CambiarMaximoMinimo(ControlStock control, short minimo, short maximo, string usuario, DateTime ahora)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }
            control.StockMínimo = (short)MinimoDelAlmacen(control.Almacén, minimo);
            control.StockMáximo = maximo;
            control.Usuario = Usuario(usuario);
            control.Fecha_Modificación = ahora;
        }

        public static ControlStock Nuevo(string empresa, string almacen, string producto, short minimo, short maximo, short multiplos, string usuario, DateTime ahora)
        {
            return new ControlStock
            {
                Empresa = empresa,
                Almacén = almacen,
                Número = producto,
                StockMínimo = (short)MinimoDelAlmacen(almacen, minimo),
                StockMáximo = maximo,
                Múltiplos = multiplos <= 0 ? (short)1 : multiplos,
                Estacionalidad = ESTACIONALIDAD_PLANA,
                Usuario = Usuario(usuario),
                Fecha_Modificación = ahora
            };
        }

        internal static string Usuario(string usuario)
        {
            string texto = string.IsNullOrWhiteSpace(usuario) ? "API" : usuario.Trim();
            return texto.Length <= LONGITUD_USUARIO ? texto : texto.Substring(0, LONGITUD_USUARIO);
        }
    }
}
