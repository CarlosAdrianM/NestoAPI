using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// LA REGLA de quién es distribuidor, en UN sitio (antes vivía en el SQL de los cheques regalo): un cliente lo es si ALGUNO de
    /// sus contactos tiene el estado <see cref="Constantes.Clientes.Estados.DISTRIBUIDOR"/> (6) o
    /// <see cref="Constantes.Clientes.Estados.CONTACTO_DE_DISTRIBUIDOR"/> (67). La usan los cheques regalo y el descuento de
    /// campaña que se suma encima del suyo (<c>DescuentoDistribuidorCampana</c>).
    /// <para>La consulta suelta para el motor de precios va con una caché corta por cliente (<see cref="DURACION_CACHE"/>). Sin
    /// lector configurado (las pruebas) nadie es distribuidor; si falla la lectura, tampoco (lo de siempre: el mejor de los dos).</para>
    /// </summary>
    public static class ClientesDistribuidores
    {
        public static readonly short[] ESTADOS = { Constantes.Clientes.Estados.DISTRIBUIDOR, Constantes.Clientes.Estados.CONTACTO_DE_DISTRIBUIDOR };
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        /// <summary>@p0 empresa, @p1 cliente, @p2 y @p3 los dos estados.</summary>
        internal const string SQL_ES_DISTRIBUIDOR = @"
SELECT COUNT(*) FROM Clientes WHERE Empresa = @p0 AND [Nº Cliente] = @p1 AND Estado IN (@p2, @p3)";

        /// <summary>El estado de una ficha (de un contacto) es de distribuidor.</summary>
        public static bool EsEstadoDistribuidor(short? estado) => estado.HasValue && ESTADOS.Contains(estado.Value);

        /// <summary>Alguno de los estados de sus contactos es de distribuidor.</summary>
        public static bool EsDistribuidor(IEnumerable<short?> estadosDeSusContactos) =>
            (estadosDeSusContactos ?? Enumerable.Empty<short?>()).Any(EsEstadoDistribuidor);

        public static SqlParameter[] Parametros(string empresa, string cliente) => new[]
        {
            new SqlParameter("@p0", empresa),
            new SqlParameter("@p1", cliente),
            new SqlParameter("@p2", Constantes.Clientes.Estados.DISTRIBUIDOR),
            new SqlParameter("@p3", Constantes.Clientes.Estados.CONTACTO_DE_DISTRIBUIDOR)
        };

        private static readonly ConcurrentDictionary<string, Tuple<bool, DateTime>> cache = new ConcurrentDictionary<string, Tuple<bool, DateTime>>();
        private static Func<string, string, bool> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;

        /// <summary>Para el motor de precios (síncrono). Nunca lanza.</summary>
        public static bool EsDistribuidor(string empresa, string cliente)
        {
            Func<string, string, bool> lectorActual = lector;
            if (lectorActual == null || string.IsNullOrWhiteSpace(cliente))
            {
                return false;
            }
            string clave = $"{empresa?.Trim()}|{cliente.Trim()}";
            DateTime ahora = reloj();
            if (cache.TryGetValue(clave, out Tuple<bool, DateTime> guardado) && ahora < guardado.Item2)
            {
                return guardado.Item1;
            }
            bool es;
            try
            {
                es = lectorActual(empresa?.Trim(), cliente.Trim());
            }
            catch (Exception ex)
            {
                try
                {
                    registrar?.Invoke(new Exception($"[Distribuidores] No se ha podido saber si el cliente {cliente.Trim()} es distribuidor: {ex.Message}", ex));
                }
                catch
                {
                    // El registro nunca puede tumbar el cálculo del precio.
                }
                es = false;
            }
            cache[clave] = Tuple.Create(es, ahora + DURACION_CACHE);
            return es;
        }

        /// <summary>Startup: lee la ficha con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar((empresa, cliente) =>
            {
                using (var db = new NVEntities())
                {
                    return db.Database.SqlQuery<int>(SQL_ES_DISTRIBUIDOR, Parametros(empresa, cliente)).FirstOrDefault() > 0;
                }
            }, registrarError);
        }

        internal static void Configurar(Func<string, string, bool> nuevoLector, Action<Exception> registrarError = null, Func<DateTime> nuevoReloj = null)
        {
            lector = nuevoLector;
            registrar = registrarError;
            reloj = nuevoReloj ?? (() => DateTime.UtcNow);
            cache.Clear();
        }

        /// <summary>Pruebas: estos clientes son distribuidores.</summary>
        internal static void ConfigurarPara(params string[] clientes)
        {
            var conjunto = new HashSet<string>((clientes ?? new string[0]).Select(c => c.Trim()), StringComparer.OrdinalIgnoreCase);
            Configurar((_, cliente) => conjunto.Contains(cliente));
        }

        /// <summary>Pruebas: nadie es distribuidor.</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
