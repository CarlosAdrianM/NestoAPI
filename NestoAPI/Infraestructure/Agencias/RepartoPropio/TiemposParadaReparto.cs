using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#881: las constantes del fin previsto de ruta (decisiones de Carlos del 19/11/26). Cada una se puede cambiar con una fila
    /// de ParametrosUsuario (empresa 1, usuario «(defecto)») SIN script: sin fila, o con un valor que no se entiende, la del código.
    /// </summary>
    public class ConstantesTiemposReparto
    {
        public const string CLAVE_ENVIO = "Reparto.MinutosParadaEnvio";
        public const string CLAVE_NOTA_ENTREGA = "Reparto.MinutosParadaNotaEntrega";
        public const string CLAVE_NOTA_COBRO = "Reparto.MinutosParadaNotaCobro";
        public const string CLAVE_NOTA_RECOGIDA = "Reparto.MinutosParadaNotaRecogida";
        public const string CLAVE_REPOSICION = "Reparto.MinutosParadaReposicion";
        public const string CLAVE_SERVICIO_TECNICO = "Reparto.MinutosServicioTecnico";
        /// <summary>En tanto por ciento («15»).</summary>
        public const string CLAVE_COLCHON = "Reparto.ColchonConduccion";
        public const string CLAVE_DESCARGA_ALGETE = "Reparto.MinutosDescargaAlgete";
        /// <summary>«17:00».</summary>
        public const string CLAVE_CIERRE_LUNES_JUEVES = "Reparto.CierreLunesAJueves";
        /// <summary>«15:00».</summary>
        public const string CLAVE_CIERRE_VIERNES = "Reparto.CierreViernes";
        /// <summary>NestoAPI#881 (corte 3): «Regla» (por defecto) o «Modelo»: quién da los minutos de cada parada en el fin de ruta.</summary>
        public const string CLAVE_QUIEN_MANDA = "Reparto.TiempoParadaQuienManda";
        public const string QUIEN_MANDA_REGLA = "Regla";
        public const string QUIEN_MANDA_MODELO = "Modelo";

        public static readonly IReadOnlyList<string> CLAVES = new[]
        {
            CLAVE_ENVIO, CLAVE_NOTA_ENTREGA, CLAVE_NOTA_COBRO, CLAVE_NOTA_RECOGIDA, CLAVE_REPOSICION, CLAVE_SERVICIO_TECNICO, CLAVE_COLCHON,
            CLAVE_DESCARGA_ALGETE, CLAVE_CIERRE_LUNES_JUEVES, CLAVE_CIERRE_VIERNES, CLAVE_QUIEN_MANDA
        };

        public double MinutosEnvio { get; set; } = 15;
        public double MinutosNotaEntrega { get; set; } = 15;
        public double MinutosNotaCobro { get; set; } = 10;
        public double MinutosNotaRecogida { get; set; } = 15;
        public double MinutosReposicion { get; set; } = 25;
        /// <summary>Un servicio técnico sin minutos tecleados ni minutos en su tipo.</summary>
        public double MinutosServicioTecnico { get; set; } = 60;
        /// <summary>Lo que se suma a los minutos del motor de rutas (0,15 = +15 %): el servicio gratuito no sabe de tráfico.</summary>
        public double ColchonConduccion { get; set; } = 0.15;
        /// <summary>Descargar en Algete al final de la ruta.</summary>
        public double MinutosDescargaAlgete { get; set; } = 15;
        public TimeSpan CierreLunesAJueves { get; set; } = new TimeSpan(17, 0, 0);
        public TimeSpan CierreViernes { get; set; } = new TimeSpan(15, 0, 0);
        /// <summary>
        /// NestoAPI#881 (corte 3): «Modelo» = los minutos de cada parada los da el modelo en sombra (si hay modelo guardado; si no, o si falla,
        /// la regla). Cualquier otro valor, o sin fila: «Regla».
        /// </summary>
        public string QuienManda { get; set; } = QUIEN_MANDA_REGLA;
        public bool MandaElModelo => string.Equals(QuienManda, QUIEN_MANDA_MODELO, StringComparison.OrdinalIgnoreCase);

        public double FactorConduccion => 1 + ColchonConduccion;

        public static ConstantesTiemposReparto PorDefecto() => new ConstantesTiemposReparto();

        /// <summary>
        /// PURA. Las constantes con lo que haya en los parámetros (<paramref name="valor"/> devuelve el texto de la clave o null). Lo que no se
        /// entiende o está fuera de rango se queda con el valor del código.
        /// </summary>
        public static ConstantesTiemposReparto Desde(Func<string, string> valor)
        {
            var c = new ConstantesTiemposReparto();
            if (valor == null)
            {
                return c;
            }
            c.MinutosEnvio = Minutos(valor(CLAVE_ENVIO), c.MinutosEnvio);
            c.MinutosNotaEntrega = Minutos(valor(CLAVE_NOTA_ENTREGA), c.MinutosNotaEntrega);
            c.MinutosNotaCobro = Minutos(valor(CLAVE_NOTA_COBRO), c.MinutosNotaCobro);
            c.MinutosNotaRecogida = Minutos(valor(CLAVE_NOTA_RECOGIDA), c.MinutosNotaRecogida);
            c.MinutosReposicion = Minutos(valor(CLAVE_REPOSICION), c.MinutosReposicion);
            c.MinutosServicioTecnico = Minutos(valor(CLAVE_SERVICIO_TECNICO), c.MinutosServicioTecnico);
            c.MinutosDescargaAlgete = Minutos(valor(CLAVE_DESCARGA_ALGETE), c.MinutosDescargaAlgete);
            double? colchon = Numero(valor(CLAVE_COLCHON));
            if (colchon.HasValue && colchon.Value >= 0 && colchon.Value <= 200)
            {
                c.ColchonConduccion = colchon.Value / 100d;
            }
            c.CierreLunesAJueves = Hora(valor(CLAVE_CIERRE_LUNES_JUEVES)) ?? c.CierreLunesAJueves;
            c.CierreViernes = Hora(valor(CLAVE_CIERRE_VIERNES)) ?? c.CierreViernes;
            c.QuienManda = string.Equals(valor(CLAVE_QUIEN_MANDA)?.Trim(), QUIEN_MANDA_MODELO, StringComparison.OrdinalIgnoreCase)
                ? QUIEN_MANDA_MODELO : QUIEN_MANDA_REGLA;
            return c;
        }

        private static double Minutos(string texto, double porDefecto)
        {
            double? n = Numero(texto);
            return n.HasValue && n.Value >= 0 && n.Value <= 600 ? n.Value : porDefecto;
        }

        private static double? Numero(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            string limpio = texto.Trim().Replace("%", string.Empty).Replace(',', '.');
            return double.TryParse(limpio, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : (double?)null;
        }

        private static TimeSpan? Hora(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            return TimeSpan.TryParseExact(texto.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out TimeSpan hora)
                && hora > TimeSpan.Zero && hora < TimeSpan.FromDays(1) ? hora : (TimeSpan?)null;
        }
    }

    /// <summary>NestoAPI#881: de dónde salen las constantes (sustituible en los tests).</summary>
    public interface IConfiguracionTiemposReparto
    {
        /// <summary>Nunca lanza: ante cualquier fallo, las del código.</summary>
        ConstantesTiemposReparto Leer();
    }

    /// <summary>
    /// NestoAPI#881: las constantes de ParametrosUsuario «(defecto)» de la empresa 1, con una caché de <see cref="MINUTOS_CACHE"/> minutos (se
    /// leen en cada estimación). Lee SOLO la fila de «(defecto)» (no crea filas).
    /// </summary>
    public class ConfiguracionTiemposRepartoParametros : IConfiguracionTiemposReparto
    {
        public const int MINUTOS_CACHE = 5;
        internal const string USUARIO_DEFECTO = "(defecto)";
        private static readonly object bloqueo = new object();
        private static ConstantesTiemposReparto cache;
        private static DateTime caduca;

        private readonly ILectorParametrosUsuario lector;

        public ConfiguracionTiemposRepartoParametros(ILectorParametrosUsuario lector = null)
        {
            this.lector = lector ?? new LectorParametrosUsuario();
        }

        public ConstantesTiemposReparto Leer()
        {
            lock (bloqueo)
            {
                if (cache != null && DateTime.Now < caduca)
                {
                    return cache;
                }
            }
            ConstantesTiemposReparto leidas;
            try
            {
                leidas = ConstantesTiemposReparto.Desde(clave => lector.LeerParametroSinDefecto(Constantes.Empresas.EMPRESA_POR_DEFECTO, USUARIO_DEFECTO, clave));
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] No se pudieron leer las constantes del fin de ruta; se usan las del código. {ex.Message}", ex));
                return ConstantesTiemposReparto.PorDefecto();
            }
            lock (bloqueo)
            {
                cache = leidas;
                caduca = DateTime.Now.AddMinutes(MINUTOS_CACHE);
            }
            return leidas;
        }

        internal static void Olvidar()
        {
            lock (bloqueo)
            {
                cache = null;
            }
        }
    }

    /// <summary>NestoAPI#881: lo que la regla necesita saber de una parada.</summary>
    public class ParadaParaTiempo
    {
        public string TipoParada { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        /// <summary>Lleva servicio técnico.</summary>
        public bool ConServicioTecnico { get; set; }
        /// <summary>Con servicio técnico: los tecleados o los del tipo (null = los de por defecto).</summary>
        public double? MinutosServicio { get; set; }

        public static ParadaParaTiempo De(EnvioRepartoDTO parada) => new ParadaParaTiempo
        {
            TipoParada = parada?.TipoParada,
            Empresa = parada?.Empresa,
            Cliente = parada?.Cliente,
            Contacto = parada?.Contacto,
            Latitud = parada?.Latitud,
            Longitud = parada?.Longitud,
            ConServicioTecnico = !string.IsNullOrWhiteSpace(parada?.ServicioTecnico),
            MinutosServicio = parada?.MinutosServicio
        };
    }

    /// <summary>
    /// NestoAPI#881: LA REGLA de los minutos que se cuentan en cada parada (la única fuente: fin previsto, llegadas estimadas del orden
    /// sugerido, «fuera de horario preferible» y los MinutosPrevistos de los tiempos reales). PURA.
    /// <list type="bullet">
    /// <item>Envío y nota de entrega 15; nota de cobro 10; nota de recogida 15; reposición de tienda 25.</item>
    /// <item>Con servicio técnico (nota de entrega o de recogida): los minutos tecleados en la nota, si no los de su tipo, si no 60.</item>
    /// <item>Las paradas en la MISMA dirección (mismo cliente y contacto, o mismas coordenadas) cuentan UNA vez: la mayor, en la primera
    /// del recorrido; las demás, 0.</item>
    /// </list>
    /// </summary>
    public static class MinutosParadaReparto
    {
        /// <summary>Decimales con los que dos coordenadas son «la misma dirección» (4 ≈ 11 m).</summary>
        internal const int DECIMALES_MISMA_DIRECCION = 4;

        public static double DeTipo(string tipoParada, ConstantesTiemposReparto c)
        {
            c = c ?? ConstantesTiemposReparto.PorDefecto();
            switch (tipoParada)
            {
                case TiposParadaReparto.NOTA_ENTREGA: return c.MinutosNotaEntrega;
                case TiposParadaReparto.NOTA_COBRO: return c.MinutosNotaCobro;
                case TiposParadaReparto.NOTA_RECOGIDA: return c.MinutosNotaRecogida;
                case TiposParadaReparto.REPOSICION: return c.MinutosReposicion;
                default: return c.MinutosEnvio;
            }
        }

        /// <summary>Los minutos de una parada sola (sin mirar si comparte dirección con otra).</summary>
        public static double De(ParadaParaTiempo parada, ConstantesTiemposReparto c)
        {
            c = c ?? ConstantesTiemposReparto.PorDefecto();
            if (parada == null)
            {
                return c.MinutosEnvio;
            }
            if (parada.ConServicioTecnico)
            {
                double? minutos = parada.MinutosServicio;
                return minutos.HasValue && minutos.Value > 0 ? minutos.Value : c.MinutosServicioTecnico;
            }
            return DeTipo(parada.TipoParada, c);
        }

        /// <summary>Los de cada parada del recorrido (en el mismo orden), con las de la misma dirección contadas una vez.</summary>
        public static List<double> Recorrido(IReadOnlyList<ParadaParaTiempo> paradas, ConstantesTiemposReparto c)
        {
            var resultado = new List<double>();
            if (paradas == null || paradas.Count == 0)
            {
                return resultado;
            }
            int n = paradas.Count;
            var padre = Enumerable.Range(0, n).ToArray();
            int Raiz(int i) => padre[i] == i ? i : (padre[i] = Raiz(padre[i]));
            void Unir(int a, int b)
            {
                int ra = Raiz(a), rb = Raiz(b);
                if (ra != rb)
                {
                    padre[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
            }
            var porContacto = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var porSitio = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                string contacto = ClaveContacto(paradas[i]);
                if (contacto != null)
                {
                    if (porContacto.TryGetValue(contacto, out int otro))
                    {
                        Unir(i, otro);
                    }
                    else
                    {
                        porContacto[contacto] = i;
                    }
                }
                string sitio = ClaveSitio(paradas[i]);
                if (sitio != null)
                {
                    if (porSitio.TryGetValue(sitio, out int otro))
                    {
                        Unir(i, otro);
                    }
                    else
                    {
                        porSitio[sitio] = i;
                    }
                }
            }
            var maximoGrupo = new Dictionary<int, double>();
            for (int i = 0; i < n; i++)
            {
                int raiz = Raiz(i);
                double minutos = De(paradas[i], c);
                maximoGrupo[raiz] = maximoGrupo.TryGetValue(raiz, out double m) ? Math.Max(m, minutos) : minutos;
            }
            var contado = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int raiz = Raiz(i);
                resultado.Add(contado.Add(raiz) ? maximoGrupo[raiz] : 0d);
            }
            return resultado;
        }

        public static List<double> Recorrido(IEnumerable<EnvioRepartoDTO> paradas, ConstantesTiemposReparto c)
            => Recorrido((paradas ?? Enumerable.Empty<EnvioRepartoDTO>()).Select(ParadaParaTiempo.De).ToList(), c);

        private static string ClaveContacto(ParadaParaTiempo p)
            => string.IsNullOrWhiteSpace(p?.Cliente) ? null
                : $"{p.Empresa?.Trim()}|{p.Cliente.Trim()}|{p.Contacto?.Trim()}";

        private static string ClaveSitio(ParadaParaTiempo p)
            => p == null || !CoordenadasGeo.Validas(p.Latitud, p.Longitud) ? null
                : Math.Round(p.Latitud.Value, DECIMALES_MISMA_DIRECCION).ToString("0.0000", CultureInfo.InvariantCulture) + "," +
                  Math.Round(p.Longitud.Value, DECIMALES_MISMA_DIRECCION).ToString("0.0000", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// NestoAPI#881: LA REGLA del cierre de Algete con el que se compara el fin de ruta. PURA. Lunes a jueves 17:00, viernes 15:00
    /// (parametrizables); sábados, domingos y días sin reparto propio (festivo de Algete o día sin reparto, GestorFestivos.EsFestivoRepartoPropio)
    /// no hay cierre que comparar (null).
    /// </summary>
    public static class HorarioCierreReparto
    {
        public static DateTime? De(DateTime fecha, ConstantesTiemposReparto c, Func<DateTime, bool> esDiaSinReparto)
        {
            c = c ?? ConstantesTiemposReparto.PorDefecto();
            DateTime dia = fecha.Date;
            if (dia.DayOfWeek == DayOfWeek.Saturday || dia.DayOfWeek == DayOfWeek.Sunday)
            {
                return null;
            }
            if (esDiaSinReparto != null && esDiaSinReparto(dia))
            {
                return null;
            }
            return dia + (dia.DayOfWeek == DayOfWeek.Friday ? c.CierreViernes : c.CierreLunesAJueves);
        }
    }
}
