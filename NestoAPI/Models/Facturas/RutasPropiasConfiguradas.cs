using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Models.Facturas
{
    /// <summary>
    /// NestoAPI#758 (Carlos 30/10/26): la lista de rutas que son reparto propio sale de un parámetro en BD, ParametrosUsuario
    /// <c>RutasRepartoPropio</c> del usuario «(defecto)» en la empresa 1 (p. ej. «AT,16» o «AT,16,01,02,03,04,05»), para poder
    /// añadir las rutas 01-05 de Madrid sin publicar. Sin la fila (o vacía, o ilegible) son las de siempre, <see cref="POR_DEFECTO"/>.
    /// Es la ÚNICA lista: la leen <see cref="RutaPropia"/> (Facturar rutas, copias), y a través de ella el reparto propio de la
    /// propuesta de envío, los días de entrega del picking, las sugerencias de ruta, el picking de rutas propias y las paradas de
    /// las notas de entrega. Ojo: añadir una ruta cambia la agencia propuesta y el packing de todos sus clientes.
    /// <para>No va en el Web.config a propósito: los flags del Web.config vuelven al valor del repositorio en cada publicación.</para>
    /// <para>Se lee como mucho una vez cada <see cref="DURACION_CACHE"/>; sin lector configurado (las pruebas) no toca la BD. El lector
    /// de la BD lo pone Application_Start (<see cref="UsarBaseDeDatos"/>).</para>
    /// </summary>
    public static class RutasPropiasConfiguradas
    {
        public const string CLAVE = "RutasRepartoPropio";
        public const string USUARIO = Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO;
        public const string VALOR_POR_DEFECTO = "AT,16";
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(5);

        /// <summary>AT y 16, las de siempre.</summary>
        public static readonly IReadOnlyList<string> POR_DEFECTO = Parsear(VALOR_POR_DEFECTO);

        private static readonly object candado = new object();
        private static Func<string> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyList<string> enCache = POR_DEFECTO;
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>Las rutas propias ahora (mayúsculas, sin espacios, sin repetir). Nunca lanza ni devuelve una lista vacía.</summary>
        public static IReadOnlyList<string> Actuales
        {
            get
            {
                Func<string> lectorActual = lector;
                if (lectorActual == null)
                {
                    return enCache;
                }
                DateTime ahora = reloj();
                if (ahora < caducaEn)
                {
                    return enCache;
                }
                lock (candado)
                {
                    if (ahora < caducaEn)
                    {
                        return enCache;
                    }
                    try
                    {
                        enCache = Parsear(lectorActual());
                    }
                    catch (Exception ex)
                    {
                        // Se queda con lo último que leyó (al arrancar, AT y 16) y lo vuelve a intentar al caducar.
                        try
                        {
                            registrar?.Invoke(new Exception($"No se ha podido leer el parámetro {CLAVE}: siguen las rutas propias {string.Join(",", enCache)}. {ex.Message}", ex));
                        }
                        catch
                        {
                            // El registro nunca puede tumbar la lectura.
                        }
                    }
                    caducaEn = ahora + DURACION_CACHE;
                    return enCache;
                }
            }
        }

        public static bool Contiene(string ruta)
        {
            if (string.IsNullOrWhiteSpace(ruta))
            {
                return false;
            }
            string normalizada = ruta.Trim().ToUpperInvariant();
            return Actuales.Contains(normalizada);
        }

        /// <summary>
        /// «AT, 16;01 02» → AT, 16, 01, 02. Admite comas, punto y coma y espacios. Vacío o null → <see cref="POR_DEFECTO"/>
        /// (nunca deja a nadie sin reparto propio por una fila mal escrita).
        /// </summary>
        public static IReadOnlyList<string> Parsear(string valor)
        {
            List<string> rutas = (valor ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(r => r.Trim().ToUpperInvariant())
                .Where(r => r.Length > 0)
                .Distinct()
                .ToList();
            return rutas.Count == 0 && POR_DEFECTO != null ? POR_DEFECTO : rutas.AsReadOnly();
        }

        /// <summary>Application_Start: lee el parámetro de la BD (fila «(defecto)» de la empresa 1) con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(LeerDeLaBaseDeDatos, registrarError);
        }

        internal static string LeerDeLaBaseDeDatos()
        {
            using (var db = new NVEntities())
            {
                db.Configuration.LazyLoadingEnabled = false;
                db.Configuration.ProxyCreationEnabled = false;
                ParametroUsuario parametro = db.ParametrosUsuario.AsNoTracking().FirstOrDefault(p =>
                    p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO && p.Usuario == USUARIO && p.Clave == CLAVE);
                return parametro?.Valor;
            }
        }

        /// <summary>Pone el lector (null = sin BD, AT y 16) y vacía la caché.</summary>
        internal static void Configurar(Func<string> nuevoLector, Action<Exception> registrarError = null, Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                lector = nuevoLector;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                enCache = POR_DEFECTO;
                caducaEn = DateTime.MinValue;
            }
        }

        /// <summary>Pruebas: vuelve al estado de arranque (sin lector, AT y 16).</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
