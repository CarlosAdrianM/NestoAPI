using NestoAPI.Infraestructure.Clientes.Embudo;
using System;

namespace NestoAPI.Infraestructure.ProductosIncompletos
{
    /// <summary>
    /// NestoAPI#902 (Fable con el criterio de Carlos «nada se pierde, todo por su cauce», 26/11/26): el almacén de incompletos
    /// (ParametrosUsuario «(defecto)» <c>AlmacenIncompletos</c>, hoy INC) solo se mueve desde Productos incompletos. No se inventaría
    /// (rellenar, contar, dejar a cero, contabilizar), no sale en los traspasos de almacenes ficticios (#894) ni en la lista de almacenes
    /// de los pedidos. Las propuestas e indicadores de stock NO cambian (la unidad incompleta sigue siendo nuestra).
    /// <para>El código se lee de la base de datos (nunca «INC» a fuego), con caché corta; si no se puede leer o no hay fila, no se
    /// frena nada (como antes de #902).</para>
    /// </summary>
    public static class GuardaAlmacenIncompletos
    {
        public const string MENSAJE_INVENTARIO =
            "El almacén de incompletos no se inventaría: sus unidades se controlan desde Productos incompletos.";

        public const string MENSAJE_TRASPASO =
            "El almacén de incompletos no admite traspasos de almacenes ficticios: sus unidades se mueven desde Productos incompletos.";

        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(5);

        private static readonly object candado = new object();
        private static Func<string> lector = LeerDeLaBaseDeDatos;
        private static string codigoGuardado;
        private static DateTime caducaEl = DateTime.MinValue;
        private static bool errorRegistrado;

        /// <summary>LA REGLA (pura): el almacén es el de incompletos (sin relleno ni mayúsculas). Sin código configurado, nunca.</summary>
        public static bool EsElDeIncompletos(string almacen, string codigoIncompletos)
        {
            string a = almacen?.Trim();
            string i = codigoIncompletos?.Trim();
            return !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(i) && string.Equals(a, i, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>El código del almacén de incompletos (en mayúsculas), o null si no está configurado o no se puede leer. Nunca lanza.</summary>
        public static string Codigo()
        {
            lock (candado)
            {
                DateTime ahora = DateTime.Now;
                if (ahora < caducaEl)
                {
                    return codigoGuardado;
                }
                string leido;
                try
                {
                    leido = lector()?.Trim().ToUpperInvariant();
                }
                catch (Exception ex)
                {
                    if (!errorRegistrado)
                    {
                        errorRegistrado = true;
                        ElmahHelper.Log(new Exception($"[Almacén de incompletos] No se ha podido leer {ReglasProductosIncompletos.CLAVE_PARAMETRO}: no se frena nada. {ex.Message}", ex));
                    }
                    leido = null;
                }
                codigoGuardado = string.IsNullOrEmpty(leido) ? null : leido;
                caducaEl = ahora.Add(DURACION_CACHE);
                return codigoGuardado;
            }
        }

        /// <summary>El almacén es el de incompletos (leído de la base de datos, con caché).</summary>
        public static bool Es(string almacen) => !string.IsNullOrWhiteSpace(almacen) && EsElDeIncompletos(almacen, Codigo());

        public static void Invalidar()
        {
            lock (candado)
            {
                caducaEl = DateTime.MinValue;
            }
        }

        /// <summary>Para los tests: otra forma de leer el código (null = la de verdad). Invalida la caché.</summary>
        internal static void FijarLectorParaTests(Func<string> otro)
        {
            lock (candado)
            {
                lector = otro ?? LeerDeLaBaseDeDatos;
                caducaEl = DateTime.MinValue;
                errorRegistrado = false;
            }
        }

        private static string LeerDeLaBaseDeDatos() => new ParametrosEmbudoDiarioEF().Leer(ReglasProductosIncompletos.CLAVE_PARAMETRO);
    }
}
