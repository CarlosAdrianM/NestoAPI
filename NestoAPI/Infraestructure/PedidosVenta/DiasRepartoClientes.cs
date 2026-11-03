using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>Una fila de <c>dbo.ClientesDiaReparto</c> (NestoAPI#799): la excepción del día de reparto de un contacto.</summary>
    public class ExcepcionDiaRepartoCliente
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>'01' (lunes) … '05' (viernes).</summary>
        public string Ruta { get; set; }
        public string Motivo { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>
    /// NestoAPI#799 (Alfredo 03/11/26, pedido 928723): el día de reparto de NUESTRA ruta de un contacto concreto cuando no es el
    /// de su código postal («aunque su zona sea de martes, se sirve los miércoles»). Sale de la tabla <c>dbo.ClientesDiaReparto</c>
    /// (Empresa, Cliente, Contacto, Ruta '01'..'05', Motivo, Usuario, FechaModificacion), fuera del EDMX como
    /// <see cref="DiasRepartoCodigosPostales"/>: se lee por SQL y, si la tabla no existe, no hay excepciones (todo por el CP).
    /// <para>La excepción MANDA sobre el código postal (<see cref="DiasRepartoCodigosPostales.DiaRutaInterna(string, string, string, string, string)"/>).
    /// Se lee como mucho una vez cada <see cref="DURACION_CACHE"/>; <see cref="Invalidar"/> hace que se vuelva a leer en la
    /// siguiente consulta (el PUT de api/Clientes/DiaReparto). Sin lector configurado (las pruebas) no toca la BD.</para>
    /// </summary>
    public static class DiasRepartoClientes
    {
        public static readonly TimeSpan DURACION_CACHE = TimeSpan.FromMinutes(10);

        internal const string SQL_LEER =
            "IF OBJECT_ID(N'dbo.ClientesDiaReparto', N'U') IS NOT NULL " +
            "SELECT RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(Contacto) AS Contacto, RTRIM(Ruta) AS Ruta, " +
            "Motivo, Usuario, FechaModificacion FROM dbo.ClientesDiaReparto WITH (NOLOCK)";

        private static readonly IReadOnlyDictionary<string, DayOfWeek> VACIO = new Dictionary<string, DayOfWeek>();
        private static readonly object candado = new object();
        private static Func<IEnumerable<ExcepcionDiaRepartoCliente>> lector;
        private static Action<Exception> registrar;
        private static Func<DateTime> reloj = () => DateTime.UtcNow;
        private static IReadOnlyDictionary<string, DayOfWeek> enCache = VACIO;
        private static DateTime caducaEn = DateTime.MinValue;

        /// <summary>«empresa|cliente|contacto» → día de la semana. Nunca lanza; vacío si no hay tabla o no se ha podido leer.</summary>
        public static IReadOnlyDictionary<string, DayOfWeek> Actuales
        {
            get
            {
                Func<IEnumerable<ExcepcionDiaRepartoCliente>> lectorActual = lector;
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
                        enCache = Interpretar(lectorActual());
                    }
                    catch (Exception ex)
                    {
                        // Se queda con lo último que leyó y lo vuelve a intentar al caducar.
                        try
                        {
                            registrar?.Invoke(new Exception($"NestoAPI#799: no se han podido leer los días de reparto por cliente: {ex.Message}", ex));
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

        /// <summary>¿Hay alguna excepción? (para leer el contacto del pedido aunque ningún código postal tenga día).</summary>
        public static bool HayAlguna => Actuales.Count > 0;

        internal static string Clave(string empresa, string cliente, string contacto) =>
            $"{empresa?.Trim()}|{cliente?.Trim()}|{contacto?.Trim()}";

        /// <summary>
        /// El día de reparto de nuestra ruta de ese contacto, o null si no tiene excepción (entonces, el de su código postal).
        /// Sin empresa, la por defecto; un pedido de otra empresa sin fila propia mira la de la empresa por defecto (como las rutas).
        /// </summary>
        public static DayOfWeek? DiaDe(string empresa, string cliente, string contacto)
        {
            if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(contacto))
            {
                return null;
            }
            IReadOnlyDictionary<string, DayOfWeek> actuales = Actuales;
            if (actuales.Count == 0)
            {
                return null;
            }
            string empresaLimpia = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            if (actuales.TryGetValue(Clave(empresaLimpia, cliente, contacto), out DayOfWeek dia))
            {
                return dia;
            }
            if (empresaLimpia != Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                actuales.TryGetValue(Clave(Constantes.Empresas.EMPRESA_POR_DEFECTO, cliente, contacto), out dia))
            {
                return dia;
            }
            return null;
        }

        /// <summary>La siguiente consulta vuelve a leer la tabla (tras cambiar una excepción).</summary>
        public static void Invalidar()
        {
            lock (candado)
            {
                caducaEn = DateTime.MinValue;
            }
        }

        /// <summary>Filas → diccionario. Ignora las filas sin cliente o contacto y las rutas que no son de 01 a 05.</summary>
        internal static IReadOnlyDictionary<string, DayOfWeek> Interpretar(IEnumerable<ExcepcionDiaRepartoCliente> filas)
        {
            var resultado = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase);
            foreach (ExcepcionDiaRepartoCliente fila in filas ?? Enumerable.Empty<ExcepcionDiaRepartoCliente>())
            {
                if (fila == null || string.IsNullOrWhiteSpace(fila.Cliente) || string.IsNullOrWhiteSpace(fila.Contacto))
                {
                    continue;
                }
                DayOfWeek? dia = CalendarioRutasDia.DiaDeLaSemana(fila.Ruta);
                if (dia.HasValue)
                {
                    string empresa = string.IsNullOrWhiteSpace(fila.Empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : fila.Empresa;
                    resultado[Clave(empresa, fila.Cliente, fila.Contacto)] = dia.Value;
                }
            }
            return resultado;
        }

        /// <summary>Startup: lee la tabla con su propio contexto.</summary>
        public static void UsarBaseDeDatos(Action<Exception> registrarError)
        {
            Configurar(LeerDeLaBaseDeDatos, registrarError);
        }

        internal static IEnumerable<ExcepcionDiaRepartoCliente> LeerDeLaBaseDeDatos()
        {
            using (var db = new NVEntities())
            {
                return db.Database.SqlQuery<ExcepcionDiaRepartoCliente>(SQL_LEER).ToList();
            }
        }

        /// <summary>Pone el lector (null = sin BD: ninguna excepción) y vacía la caché.</summary>
        internal static void Configurar(Func<IEnumerable<ExcepcionDiaRepartoCliente>> nuevoLector, Action<Exception> registrarError = null,
            Func<DateTime> nuevoReloj = null)
        {
            lock (candado)
            {
                lector = nuevoLector;
                registrar = registrarError;
                reloj = nuevoReloj ?? (() => DateTime.UtcNow);
                enCache = VACIO;
                caducaEn = DateTime.MinValue;
            }
        }

        /// <summary>Pruebas: con estas excepciones.</summary>
        internal static void ConfigurarPara(params ExcepcionDiaRepartoCliente[] excepciones)
        {
            List<ExcepcionDiaRepartoCliente> filas = excepciones?.ToList() ?? new List<ExcepcionDiaRepartoCliente>();
            Configurar(() => filas);
        }

        /// <summary>Pruebas: vuelve al estado de arranque (sin lector, sin excepciones).</summary>
        internal static void Reiniciar() => Configurar(null);
    }
}
