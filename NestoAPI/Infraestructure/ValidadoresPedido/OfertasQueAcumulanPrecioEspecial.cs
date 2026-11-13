using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ValidadoresPedido
{
    /// <summary>
    /// Novedades 1263 (13/11/26), continuación de NestoAPI#589: qué ofertas permitidas admiten ADEMÁS el precio especial.
    /// <para>
    /// La regla general no cambia: un N+M exige cobrar las unidades de pago a precio de ficha y sin descuento (o precio
    /// especial o la oferta, nunca las dos). La excepción es un DATO: <c>OfertasPermitidas.AcumulaPrecioEspecial = 1</c>
    /// en la fila de la oferta (primer caso: los packs 45929, 45930 y 45931 durante la feria online, autorizado por
    /// Dirección comercial). La columna está fuera del EDMX: sin ella (API publicada antes del script
    /// <c>Scripts/Novedades1263_OfertaAcumulaPrecioEspecial.sql</c>) ninguna oferta acumula y todo va como siempre.
    /// </para>
    /// </summary>
    public static class OfertasQueAcumulanPrecioEspecial
    {
        internal const string TABLA = "OfertasPermitidas";
        internal const string COLUMNA = "AcumulaPrecioEspecial";
        internal static readonly TimeSpan CADUCIDAD = TimeSpan.FromMinutes(5);

        /// <summary>De dónde salen los Nº Orden de las ofertas marcadas (sustituible en los tests). Nunca debe lanzar.</summary>
        internal static Func<HashSet<int>> Lector = LeerDeLaBaseDeDatos;

        /// <summary>El reloj (sustituible en los tests).</summary>
        internal static Func<DateTime> Ahora = () => DateTime.Now;

        private static readonly object cerrojo = new object();
        private static HashSet<int> enCache;
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>¿Esta oferta permitida admite además el precio especial del producto?</summary>
        public static bool LaAcumula(OfertaPermitida oferta)
            => oferta != null && Marcadas().Contains(oferta.NºOrden);

        internal static HashSet<int> Marcadas()
        {
            lock (cerrojo)
            {
                if (enCache != null && Ahora() < caducaEn)
                {
                    return enCache;
                }
            }
            HashSet<int> leidas;
            try
            {
                leidas = Lector() ?? new HashSet<int>();
            }
            catch (Exception ex)
            {
                // Sin poder leer la marca, ninguna oferta acumula: la regla de siempre. Se reintenta en la siguiente lectura.
                ElmahHelper.Log(new Exception("[Ofertas que acumulan precio especial] No se ha podido leer la marca: " + ex.Message, ex));
                return new HashSet<int>();
            }
            lock (cerrojo)
            {
                enCache = leidas;
                caducaEn = Ahora().Add(CADUCIDAD);
                return enCache;
            }
        }

        /// <summary>Tira la caché (tests, y al cambiar la marca de una oferta).</summary>
        internal static void Invalidar()
        {
            lock (cerrojo)
            {
                enCache = null;
                caducaEn = DateTime.MinValue;
            }
        }

        private static HashSet<int> LeerDeLaBaseDeDatos()
        {
            using (NVEntities db = new NVEntities())
            {
                if (!ColumnasOpcionales.Existen(db, TABLA, COLUMNA))
                {
                    return new HashSet<int>();
                }
                List<int> ids = db.Database
                    .SqlQuery<int>("SELECT NºOrden FROM dbo.OfertasPermitidas WHERE AcumulaPrecioEspecial = 1")
                    .ToList();
                return new HashSet<int>(ids);
            }
        }
    }
}
