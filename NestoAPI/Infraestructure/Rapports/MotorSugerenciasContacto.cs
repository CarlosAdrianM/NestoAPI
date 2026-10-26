using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#603: los umbrales del motor de sugerencias. Constantes de momento (los valores acordados en la issue);
    /// si hay que afinarlos por vendedor, este es el único sitio que hay que leer de ParametrosUsuario.
    /// </summary>
    public class UmbralesSugerenciasContacto
    {
        /// <summary>Probabilidad del modelo desde la que un buen cliente al que ya le toca es prioridad Máxima.</summary>
        public const float PROBABILIDAD_MAXIMA = 0.6f;
        /// <summary>Pedidos (días distintos con compra) en 12 meses para ser «buen cliente».</summary>
        public const int PEDIDOS_BUEN_CLIENTE = 3;
        /// <summary>La cadencia nunca baja de una semana…</summary>
        public const int CADENCIA_MINIMA_DIAS = 7;
        /// <summary>… ni pasa de un mes (y es un mes para quien solo compró hace 12-24 meses).</summary>
        public const int CADENCIA_MAXIMA_DIAS = 30;
        /// <summary>Días sin contacto para las prioridades Alta, Media y Baja.</summary>
        public const int DIAS_SIN_CONTACTO_REPASO = 30;
        /// <summary>No se propone a quien no contestó hace menos de esto.</summary>
        public const int DIAS_TRAS_INTENTO = 2;
        /// <summary>No se propone a quien hizo un pedido hace menos de esto (como el endpoint antiguo).</summary>
        public const int DIAS_TRAS_PEDIDO = 6;
        /// <summary>Meses hacia atrás de compras para entrar en la cartera (prioridad Baja).</summary>
        public const int MESES_CARTERA = 24;
        /// <summary>Llamadas al mes que suma al objetivo cualquier cliente de la cartera, aunque compre poco…</summary>
        public const int LLAMADAS_MES_MINIMAS = 1;
        /// <summary>… y como mucho una por semana, aunque compre más.</summary>
        public const int LLAMADAS_MES_MAXIMAS = 4;
    }

    public static class PrioridadesContacto
    {
        public const string MAXIMA = "Máxima";
        public const string ALTA = "Alta";
        public const string MEDIA = "Media";
        public const string BAJA = "Baja";

        internal static int Rango(string prioridad)
        {
            switch (prioridad)
            {
                case MAXIMA: return 0;
                case ALTA: return 1;
                case MEDIA: return 2;
                case BAJA: return 3;
                default: return 9;
            }
        }
    }

    /// <summary>
    /// NestoAPI#677 (corte 1): por qué entró cada sugerencia en la lista del día. Se guarda SIN tocar el esquema: lo dice
    /// el principio del Motivo registrado (<see cref="DeMotivo"/>), para comparar la conversión de cada origen (~10/11/26).
    /// Las filas registradas antes del corte 1 salen como <see cref="PROBABILIDAD"/> (entonces el orden era por prioridad).
    /// </summary>
    public static class OrigenesSugerenciaContacto
    {
        /// <summary>Entró por el orden de probabilidad del modelo.</summary>
        public const string PROBABILIDAD = "Probabilidad";
        /// <summary>Entró por el cupo de cobertura: comprador sin contacto en el mes.</summary>
        public const string COBERTURA = "Cobertura";
        /// <summary>NestoAPI#593: cheque regalo sin usar en los últimos días de la campaña.</summary>
        public const string CHEQUE_REGALO = "ChequeRegalo";

        /// <summary>Principio del Motivo de las que entran por el cupo de cobertura.</summary>
        public const string PREFIJO_MOTIVO_COBERTURA = "Cobertura del mes: ";
        /// <summary>Principio de la frase del cheque (<c>EstadisticaChequesRegalo.FraseImpulso</c>).</summary>
        public const string PREFIJO_MOTIVO_CHEQUE = "Tiene un cheque";

        public static string DeMotivo(string motivo)
        {
            string texto = motivo?.TrimStart() ?? string.Empty;
            if (texto.StartsWith(PREFIJO_MOTIVO_COBERTURA, StringComparison.Ordinal))
            {
                return COBERTURA;
            }
            if (texto.StartsWith(PREFIJO_MOTIVO_CHEQUE, StringComparison.Ordinal))
            {
                return CHEQUE_REGALO;
            }
            return PROBABILIDAD;
        }
    }

    /// <summary>
    /// NestoAPI#603 (corte 1): decide a quién de la cartera hay que llamar, con qué prioridad y por qué, y calcula el
    /// ritmo (objetivo del mes y del día). Pura: recibe la cartera ya calculada, el día y qué días son laborables.
    /// <list type="bullet">
    /// <item><b>Cadencia</b> (días entre contactos) = clamp(365 / pedidos en 12 meses, 7, 30); 30 si solo compró en 24 meses.</item>
    /// <item><b>No se propone</b> si se habló con él hace menos que su cadencia, si no contestó hace menos de 2 días o si
    /// pidió hace menos de 6.</item>
    /// <item><b>Máxima</b>: buen cliente (≥ 3 pedidos en 12 meses), probabilidad ≥ 0,6 y ya le toca por cadencia.
    /// <b>Alta</b>: buen cliente al que ya le toca por cadencia (probabilidad &lt; 0,6) o con ≥ 30 días sin contacto. <b>Media</b>: compras en 12 meses y ≥ 30 días sin contacto.
    /// <b>Baja</b>: solo compras en 24 meses y ≥ 30 días sin contacto. Si no cumple ninguna, todavía no le toca.
    /// Desde NestoAPI#677 la prioridad es solo una etiqueta (color y motivo): ya no ordena.</item>
    /// <item><b>Orden</b> (NestoAPI#677): probabilidad del modelo desc; empata días sin contacto desc (nunca contactado, el primero).</item>
    /// <item><b>Cupo de cobertura</b> (NestoAPI#677, <see cref="ElegirDelDia"/>): en la lista del día se reserva
    /// ⌈compradores sin contacto este mes / laborables que quedan (hoy incluido)⌉ para ellos, intercalados; el último
    /// laborable del mes entran todos los que falten.</item>
    /// </list>
    /// </summary>
    public class MotorSugerenciasContacto
    {
        private static readonly CultureInfo es = new CultureInfo("es-ES");
        public const int LONGITUD_MAXIMA_MOTIVO = 200;

        public static bool EsBuenCliente(ClienteCarteraContacto c) => c.Pedidos12Meses >= UmbralesSugerenciasContacto.PEDIDOS_BUEN_CLIENTE;

        public static int Cadencia(ClienteCarteraContacto c)
        {
            if (c.Pedidos12Meses <= 0)
            {
                return UmbralesSugerenciasContacto.CADENCIA_MAXIMA_DIAS;
            }
            int dias = (int)Math.Round(365.0 / c.Pedidos12Meses, MidpointRounding.AwayFromZero);
            return Math.Max(UmbralesSugerenciasContacto.CADENCIA_MINIMA_DIAS, Math.Min(UmbralesSugerenciasContacto.CADENCIA_MAXIMA_DIAS, dias));
        }

        internal static int? DiasDesde(DateTime? fecha, DateTime hoy) => fecha.HasValue ? (int?)(hoy.Date - fecha.Value.Date).Days : null;

        /// <summary>True si hoy NO se le puede proponer (dentro de su cadencia, intento reciente o pedido reciente).</summary>
        public static bool EstaExcluido(ClienteCarteraContacto c, DateTime hoy)
        {
            int? diasContacto = DiasDesde(c.UltimoContacto, hoy);
            int? diasIntento = DiasDesde(c.UltimoIntento, hoy);
            // NestoAPI#620: «pidió hace poco» con cualquier pedido del cliente, también de otro grupo (de otro vendedor).
            int? diasPedido = DiasDesde(RepositorioCarteraContactoSql.MasReciente(c.UltimoPedido, c.UltimoPedidoCliente), hoy);
            return (diasContacto.HasValue && diasContacto.Value < Cadencia(c))
                || (diasIntento.HasValue && diasIntento.Value < UmbralesSugerenciasContacto.DIAS_TRAS_INTENTO)
                || (diasPedido.HasValue && diasPedido.Value < UmbralesSugerenciasContacto.DIAS_TRAS_PEDIDO);
        }

        /// <summary>La prioridad de hoy, o null si todavía no le toca.</summary>
        public static string Prioridad(ClienteCarteraContacto c, DateTime hoy)
        {
            if (EstaExcluido(c, hoy))
            {
                return null;
            }
            int? diasContacto = DiasDesde(c.UltimoContacto, hoy);
            bool repaso = !diasContacto.HasValue || diasContacto.Value >= UmbralesSugerenciasContacto.DIAS_SIN_CONTACTO_REPASO;
            bool bueno = EsBuenCliente(c);

            if (bueno && c.Probabilidad >= UmbralesSugerenciasContacto.PROBABILIDAD_MAXIMA)
            {
                return PrioridadesContacto.MAXIMA;
            }
            // Un buen cliente al que ya le toca por cadencia (y ≥ 30 días lo implica: la cadencia nunca pasa de 30) es Alta:
            // el que compra cada semana necesita una llamada cada semana aunque el modelo no lo vea claro.
            if (bueno)
            {
                return PrioridadesContacto.ALTA;
            }
            if (c.Pedidos12Meses > 0 && repaso)
            {
                return PrioridadesContacto.MEDIA;
            }
            if (c.Pedidos12Meses <= 0 && c.Pedidos24Meses > 0 && repaso)
            {
                return PrioridadesContacto.BAJA;
            }
            return null;
        }

        /// <summary>
        /// Toda la cartera a la que hoy le toca, ordenada por probabilidad (NestoAPI#677: la prioridad ya no ordena; los datos
        /// del 07 al 17/10/26 dieron que lo que entraba por regla convertía menos que el método manual) y con Orden 1, 2, 3…
        /// </summary>
        public List<SugerenciaContactoDTO> Priorizar(IEnumerable<ClienteCarteraContacto> cartera, DateTime hoy)
        {
            var lista = cartera
                .Select(c => new { Cliente = c, Prioridad = Prioridad(c, hoy), DiasContacto = DiasDesde(c.UltimoContacto, hoy) })
                .Where(x => x.Prioridad != null)
                .OrderByDescending(x => x.Cliente.Probabilidad)
                .ThenByDescending(x => x.DiasContacto ?? int.MaxValue)
                .ThenBy(x => x.Cliente.Clave, StringComparer.Ordinal)
                .ToList();

            var resultado = new List<SugerenciaContactoDTO>(lista.Count);
            for (int i = 0; i < lista.Count; i++)
            {
                SugerenciaContactoDTO sugerencia = ADTO(lista[i].Cliente, hoy);
                sugerencia.Prioridad = lista[i].Prioridad;
                sugerencia.Orden = i + 1;
                sugerencia.Motivo = Motivo(lista[i].Cliente, lista[i].Prioridad, hoy);
                sugerencia.Origen = OrigenesSugerenciaContacto.PROBABILIDAD;
                resultado.Add(sugerencia);
            }
            return resultado;
        }

        // ---------------- NestoAPI#677: cupo de cobertura ----------------

        /// <summary>
        /// NestoAPI#677: «comprador» = cliente con alguna compra en los últimos 12 meses (<see cref="ClienteCarteraContacto.Pedidos12Meses"/>
        /// &gt; 0), la misma ventana que ya usaban la prioridad Media y el «buen cliente». A fin de mes, todos los compradores
        /// del vendedor tienen que haber sido contactados.
        /// </summary>
        public static bool EsComprador(ClienteCarteraContacto c) => c.Pedidos12Meses > 0;

        /// <summary>Comprador sin ningún contacto (rapport Estado 0 de tipo T/V/W) desde el día 1 del mes.</summary>
        public static bool PendienteCobertura(ClienteCarteraContacto c, DateTime hoy)
        {
            DateTime primeroDeMes = new DateTime(hoy.Year, hoy.Month, 1);
            return EsComprador(c) && (!c.UltimoContacto.HasValue || c.UltimoContacto.Value.Date < primeroDeMes);
        }

        /// <summary>
        /// Pendiente de cobertura al que hoy se le puede proponer por el cupo: la cadencia no cuenta (si no, a fin de mes
        /// no entrarían todos), pero sí no haber contestado hace menos de 2 días o haber pedido hace menos de 6.
        /// </summary>
        public static bool DisponibleParaCobertura(ClienteCarteraContacto c, DateTime hoy)
        {
            if (!PendienteCobertura(c, hoy))
            {
                return false;
            }
            int? diasIntento = DiasDesde(c.UltimoIntento, hoy);
            int? diasPedido = DiasDesde(RepositorioCarteraContactoSql.MasReciente(c.UltimoPedido, c.UltimoPedidoCliente), hoy);
            return !(diasIntento.HasValue && diasIntento.Value < UmbralesSugerenciasContacto.DIAS_TRAS_INTENTO)
                && !(diasPedido.HasValue && diasPedido.Value < UmbralesSugerenciasContacto.DIAS_TRAS_PEDIDO);
        }

        public static int CompradoresSinContactoMes(IEnumerable<ClienteCarteraContacto> cartera, DateTime hoy)
            => (cartera ?? Enumerable.Empty<ClienteCarteraContacto>()).Count(c => PendienteCobertura(c, hoy));

        /// <summary>
        /// ⌈pendientes / laborables que quedan (hoy incluido)⌉: pequeño a principio de mes y, el último laborable, todos
        /// los que falten. Pasado el último laborable (fin de semana a fin de mes), también todos.
        /// </summary>
        public static int CupoCobertura(int pendientes, DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            if (pendientes <= 0)
            {
                return 0;
            }
            int restantes = DiasLaborablesRestantes(hoy, esLaborable);
            return (int)Math.Ceiling((double)pendientes / Math.Max(1, restantes));
        }

        /// <summary>
        /// NestoAPI#677: las <paramref name="cuantas"/> que se añaden hoy a la lista (ninguna de <paramref name="yaSugeridas"/>), en orden y con Orden 1, 2, 3…
        /// <list type="number">
        /// <item>Delante, las del cheque regalo (NestoAPI#593), como hasta ahora.</item>
        /// <item>Por probabilidad, las que caben dejando sitio a <paramref name="cupo"/>.</item>
        /// <item>Si esas ya traen compradores sin contacto este mes, cuentan para el cupo (no se gasta sitio dos veces); lo que
        /// falte del cupo, con los compradores sin contacto este mes que más tiempo llevan sin hablar con el vendedor.</item>
        /// <item>Si sobra sitio (cupo cubierto o sin candidatos), otra vez por probabilidad.</item>
        /// </list>
        /// Las del cupo van intercaladas a distancias iguales entre las de probabilidad (<see cref="Intercalar{T}"/>).
        /// </summary>
        public static List<SugerenciaContactoDTO> ElegirDelDia(IList<SugerenciaContactoDTO> pendientes, IEnumerable<ClienteCarteraContacto> cartera,
            DateTime hoy, int cuantas, int cupo, IEnumerable<string> yaSugeridas = null)
        {
            var resultado = new List<SugerenciaContactoDTO>();
            if (cuantas <= 0)
            {
                return resultado;
            }
            List<ClienteCarteraContacto> carteraLista = (cartera ?? Enumerable.Empty<ClienteCarteraContacto>()).ToList();
            Dictionary<string, ClienteCarteraContacto> porClave = carteraLista
                .GroupBy(c => c.Clave, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var usadas = new HashSet<string>(yaSugeridas ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            List<SugerenciaContactoDTO> disponibles = (pendientes ?? new List<SugerenciaContactoDTO>())
                .Where(p => !usadas.Contains(ClienteCarteraContacto.ClaveDe(p.Cliente, p.Contacto)))
                .ToList();

            List<SugerenciaContactoDTO> delante = disponibles.Where(p => p.Origen == OrigenesSugerenciaContacto.CHEQUE_REGALO).Take(cuantas).ToList();
            List<SugerenciaContactoDTO> porProbabilidad = disponibles.Where(p => p.Origen != OrigenesSugerenciaContacto.CHEQUE_REGALO).ToList();
            int libres = cuantas - delante.Count;
            int reservadas = Math.Min(libres, Math.Max(0, cupo));

            List<SugerenciaContactoDTO> elegidasProbabilidad = porProbabilidad.Take(libres - reservadas).ToList();
            foreach (SugerenciaContactoDTO s in delante.Concat(elegidasProbabilidad))
            {
                _ = usadas.Add(ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto));
            }
            int yaCubren = delante.Concat(elegidasProbabilidad)
                .Count(s => porClave.TryGetValue(ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto), out ClienteCarteraContacto c) && PendienteCobertura(c, hoy));
            int faltanCupo = Math.Max(0, Math.Min(libres - elegidasProbabilidad.Count, cupo - yaCubren));

            List<SugerenciaContactoDTO> elegidasCobertura = carteraLista
                .Where(c => !usadas.Contains(c.Clave) && DisponibleParaCobertura(c, hoy))
                .GroupBy(c => c.Clave, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(c => DiasDesde(c.UltimoContacto, hoy) ?? int.MaxValue)
                .ThenByDescending(c => c.Probabilidad)
                .ThenBy(c => c.Clave, StringComparer.Ordinal)
                .Take(faltanCupo)
                .Select(c => SugerenciaCobertura(c, hoy))
                .ToList();
            foreach (SugerenciaContactoDTO s in elegidasCobertura)
            {
                _ = usadas.Add(ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto));
            }

            int huecos = libres - elegidasProbabilidad.Count - elegidasCobertura.Count;
            if (huecos > 0)
            {
                elegidasProbabilidad.AddRange(porProbabilidad
                    .Where(p => !usadas.Contains(ClienteCarteraContacto.ClaveDe(p.Cliente, p.Contacto)))
                    .Take(huecos));
            }

            resultado.AddRange(delante);
            resultado.AddRange(Intercalar(elegidasProbabilidad, elegidasCobertura));
            for (int i = 0; i < resultado.Count; i++)
            {
                resultado[i].Orden = i + 1;
            }
            return resultado;
        }

        /// <summary>
        /// Las de <paramref name="intercaladas"/> repartidas a distancias iguales entre las de <paramref name="base"/> (cada una
        /// en el centro de su tramo): 4 de cupo en 20 van en los puestos 3, 8, 13 y 18. Ninguna cambia de orden relativo.
        /// </summary>
        public static List<T> Intercalar<T>(IList<T> @base, IList<T> intercaladas)
        {
            int total = @base.Count + intercaladas.Count;
            int c = intercaladas.Count;
            var resultado = new List<T>(total);
            if (c == 0)
            {
                resultado.AddRange(@base);
                return resultado;
            }
            var puestos = new HashSet<int>();
            for (int j = 0; j < c; j++)
            {
                // ⌈(j + ½) · total / c⌉ − 1, en enteros.
                long numerador = (2L * j + 1) * total;
                long denominador = 2L * c;
                _ = puestos.Add((int)((numerador + denominador - 1) / denominador) - 1);
            }
            int b = 0, k = 0;
            for (int i = 0; i < total; i++)
            {
                if ((puestos.Contains(i) && k < c) || b >= @base.Count)
                {
                    resultado.Add(intercaladas[k++]);
                }
                else
                {
                    resultado.Add(@base[b++]);
                }
            }
            return resultado;
        }

        /// <summary>
        /// Un comprador que entra por el cupo. La prioridad (solo etiqueta) es la de siempre si hoy le tocaba por sí solo;
        /// si no (dentro de su cadencia), Alta si es buen cliente y Media si no.
        /// </summary>
        public static SugerenciaContactoDTO SugerenciaCobertura(ClienteCarteraContacto c, DateTime hoy)
        {
            SugerenciaContactoDTO dto = ADTO(c, hoy);
            dto.Prioridad = Prioridad(c, hoy) ?? (EsBuenCliente(c) ? PrioridadesContacto.ALTA : PrioridadesContacto.MEDIA);
            dto.Motivo = MotivoCobertura(c, hoy);
            dto.Origen = OrigenesSugerenciaContacto.COBERTURA;
            return dto;
        }

        /// <summary>«Cobertura del mes: sin contacto desde el 12 de septiembre; ha comprado 5 veces en el último año».</summary>
        public static string MotivoCobertura(ClienteCarteraContacto c, DateTime hoy)
        {
            string texto = $"{OrigenesSugerenciaContacto.PREFIJO_MOTIVO_COBERTURA}{SinContactoDesde(c.UltimoContacto, hoy)}; ha comprado {Veces(c.Pedidos12Meses)} en el último año";
            return texto.Length > LONGITUD_MAXIMA_MOTIVO ? texto.Substring(0, LONGITUD_MAXIMA_MOTIVO) : texto;
        }

        /// <summary>Los datos del cliente (sin prioridad, orden ni motivo).</summary>
        public static SugerenciaContactoDTO ADTO(ClienteCarteraContacto c, DateTime hoy)
        {
            return new SugerenciaContactoDTO
            {
                Cliente = c.Cliente?.Trim(),
                Contacto = c.Contacto?.Trim(),
                Nombre = c.Nombre?.Trim(),
                Direccion = c.Direccion?.Trim(),
                CodigoPostal = c.CodigoPostal?.Trim(),
                Telefono = c.Telefono?.Trim(),
                Poblacion = c.Poblacion?.Trim(),
                Provincia = c.Provincia?.Trim(),
                Probabilidad = c.Probabilidad,
                DiasDesdeUltimoContacto = DiasDesde(c.UltimoContacto, hoy),
                DiasDesdeUltimoPedido = DiasDesde(c.UltimoPedido, hoy) ?? 9999,
                CadenciaDias = Cadencia(c),
                PedidosUltimos12Meses = c.Pedidos12Meses,
                ImporteUltimos12Meses = c.Importe12Meses,
                GrupoSubgrupoMasVendido = c.GrupoSubgrupoMasVendido?.Trim()
            };
        }

        /// <summary>Una frase que explica por qué hoy toca llamarle.</summary>
        public static string Motivo(ClienteCarteraContacto c, string prioridad, DateTime hoy)
        {
            int? diasContacto = DiasDesde(c.UltimoContacto, hoy);
            string texto;
            switch (prioridad)
            {
                case PrioridadesContacto.MAXIMA:
                    string frecuencia = Cadencia(c) < UmbralesSugerenciasContacto.CADENCIA_MAXIMA_DIAS
                        ? $"Compra cada {Cadencia(c)} días"
                        : $"Buen cliente ({Pedidos(c.Pedidos12Meses)} en el último año)";
                    string contacto = diasContacto.HasValue
                        ? $"lleva {diasContacto.Value} {(diasContacto.Value == 1 ? "día" : "días")} sin hablar con usted"
                        : "no consta ningún contacto";
                    texto = $"{frecuencia} y {contacto}; probabilidad de pedido del {Math.Round(c.Probabilidad * 100, MidpointRounding.AwayFromZero)} %";
                    break;
                case PrioridadesContacto.ALTA:
                    texto = Cadencia(c) < UmbralesSugerenciasContacto.CADENCIA_MAXIMA_DIAS && diasContacto.HasValue
                        ? $"Compra cada {Cadencia(c)} días y lleva {diasContacto.Value} {(diasContacto.Value == 1 ? "día" : "días")} sin hablar con usted"
                        : $"Buen cliente ({Pedidos(c.Pedidos12Meses)} en el último año), {SinContactoDesde(c.UltimoContacto, hoy)}";
                    break;
                case PrioridadesContacto.MEDIA:
                    texto = $"Ha comprado {Veces(c.Pedidos12Meses)} en el último año (la última {EnMes(c.UltimoPedido, hoy)}) y {SinContactoDesde(c.UltimoContacto, hoy)}: toca el repaso mensual";
                    break;
                case PrioridadesContacto.BAJA:
                    texto = $"Compró por última vez {EnMes(c.UltimoPedido, hoy)}: toca el repaso mensual";
                    break;
                default:
                    texto = string.Empty;
                    break;
            }
            return texto.Length > LONGITUD_MAXIMA_MOTIVO ? texto.Substring(0, LONGITUD_MAXIMA_MOTIVO) : texto;
        }

        private static string Pedidos(int n) => n == 1 ? "1 pedido" : $"{n} pedidos";
        private static string Veces(int n) => n == 1 ? "1 vez" : $"{n} veces";

        private static string SinContactoDesde(DateTime? ultimoContacto, DateTime hoy)
        {
            if (!ultimoContacto.HasValue)
            {
                return "sin ningún contacto registrado";
            }
            string formato = ultimoContacto.Value.Year == hoy.Year ? "d 'de' MMMM" : "d 'de' MMMM 'de' yyyy";
            return "sin contacto desde el " + ultimoContacto.Value.ToString(formato, es);
        }

        private static string EnMes(DateTime? fecha, DateTime hoy)
        {
            if (!fecha.HasValue)
            {
                return "hace tiempo";
            }
            string formato = fecha.Value.Year == hoy.Year ? "MMMM" : "MMMM 'de' yyyy";
            return "en " + fecha.Value.ToString(formato, es);
        }

        // ---------------- Ritmo ----------------

        public static int DiasLaborablesDelMes(DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            DateTime primero = new DateTime(hoy.Year, hoy.Month, 1);
            return ContarLaborables(primero, primero.AddMonths(1), esLaborable);
        }

        /// <summary>De hoy (incluido si es laborable) al último día del mes.</summary>
        public static int DiasLaborablesRestantes(DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            DateTime primeroSiguiente = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(1);
            return ContarLaborables(hoy.Date, primeroSiguiente, esLaborable);
        }

        private static int ContarLaborables(DateTime desde, DateTime hastaExcluido, Func<DateTime, bool> esLaborable)
        {
            int n = 0;
            for (DateTime d = desde; d < hastaExcluido; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday && esLaborable(d))
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>
        /// Σ por cliente con compras en 24 meses de min(4, max(1, redondeo(días NATURALES del mes / cadencia))): la cadencia va
        /// en días naturales, así que el mes también (decisión de Carlos, 08/10/26). Antes se dividían los laborables (≈21)
        /// entre la cadencia y el cliente semanal sumaba 3 en vez de 4; luego (9f38ed86) pedidos del año / 12, que no
        /// casaba con la cadencia con la que se le propone (42 pedidos: cada 9 días, 3 llamadas en un mes de 31, no 4).
        /// Lo que el vendedor hace en laborables es el REPARTO: <see cref="CalcularRitmo"/> divide lo que falta entre los
        /// laborables que quedan.
        /// OJO (sugerencia 782): los contadores ya son clientes distintos (<see cref="ContarContactos"/>) y el cliente que
        /// pide 2-4 llamadas solo puede sumar 1, así que con clientes de cadencia corta este objetivo no se llega a cubrir
        /// aunque se hable con toda la cartera (JGP, octubre: 246 clientes, objetivo 256). Pendiente de decidir si pasa a
        /// ser <c>CarteraObjetivo(cartera).Count()</c>.
        /// </summary>
        public static int ObjetivoMes(IEnumerable<ClienteCarteraContacto> cartera, DateTime hoy)
        {
            int diasDelMes = DateTime.DaysInMonth(hoy.Year, hoy.Month);
            return CarteraObjetivo(cartera).Sum(c => LlamadasMes(c, diasDelMes));
        }

        /// <summary>Los clientes de la cartera que entran en el objetivo del mes (y en los contadores, sugerencia 782): con compras en 24 meses.</summary>
        public static IEnumerable<ClienteCarteraContacto> CarteraObjetivo(IEnumerable<ClienteCarteraContacto> cartera)
            => (cartera ?? Enumerable.Empty<ClienteCarteraContacto>()).Where(c => c.Pedidos24Meses > 0 || c.Pedidos12Meses > 0);

        /// <summary>
        /// Sugerencia 782 (Carlos, 26/10/26): Hoy, Semana y Mes cuentan CLIENTES DISTINTOS (número + contacto) de la cartera
        /// del objetivo, no rapports. Varios rapports a un cliente en el periodo cuentan uno; uno que no es de la cartera no
        /// cuenta. El cliente contactado ayer y hoy cuenta en Hoy y una sola vez en Mes; MesAntesDeHoy son los del mes
        /// contactados antes de hoy (sin contar dos veces al que se repite hoy). PorDia, los de cada día anterior a hoy, con
        /// el mismo criterio que Hoy (racha y mejor día). Se cruza aquí, en C#, con la cartera ya leída
        /// (<see cref="IRepositorioCarteraContacto.LeerCartera"/>): la consulta de contactos solo trae cliente/contacto/día.
        /// </summary>
        public static ContactosVendedor ContarContactos(IEnumerable<ContactoClienteDia> registros, IEnumerable<ClienteCarteraContacto> cartera, DateTime hoy)
        {
            var claves = new HashSet<string>(CarteraObjetivo(cartera).Select(c => c.Clave), StringComparer.OrdinalIgnoreCase);
            DateTime dia0 = hoy.Date;
            DateTime inicioSemana = RepositorioCarteraContactoSql.InicioSemana(dia0);
            DateTime inicioMes = RepositorioCarteraContactoSql.InicioMes(dia0);
            var deCartera = (registros ?? Enumerable.Empty<ContactoClienteDia>())
                .Where(r => r != null && r.Dia.Date <= dia0 && claves.Contains(r.Clave))
                .Select(r => new { r.Clave, Dia = r.Dia.Date })
                .ToList();
            int Distintos(Func<DateTime, bool> enPeriodo) => deCartera
                .Where(r => enPeriodo(r.Dia)).Select(r => r.Clave).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            return new ContactosVendedor
            {
                Hoy = Distintos(d => d == dia0),
                Semana = Distintos(d => d >= inicioSemana),
                Mes = Distintos(d => d >= inicioMes),
                MesAntesDeHoy = Distintos(d => d >= inicioMes && d < dia0),
                PorDia = deCartera
                    .Where(r => r.Dia < dia0)
                    .GroupBy(r => r.Dia)
                    .ToDictionary(g => g.Key, g => g.Select(r => r.Clave).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            };
        }

        /// <summary>min(4, max(1, redondeo(días naturales del mes / cadencia))).</summary>
        public static int LlamadasMes(ClienteCarteraContacto c, int diasDelMes)
        {
            int llamadas = (int)Math.Round((double)diasDelMes / Cadencia(c), MidpointRounding.AwayFromZero);
            return Math.Min(UmbralesSugerenciasContacto.LLAMADAS_MES_MAXIMAS, Math.Max(UmbralesSugerenciasContacto.LLAMADAS_MES_MINIMAS, llamadas));
        }

        public RitmoContactosDTO CalcularRitmo(IList<ClienteCarteraContacto> cartera, ContactosVendedor contactos,
            IList<SugerenciaContactoDTO> pendientes, DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            contactos = contactos ?? new ContactosVendedor();
            int restantes = DiasLaborablesRestantes(hoy, esLaborable);
            int objetivoMes = ObjetivoMes(cartera, hoy);
            int faltan = Math.Max(0, objetivoMes - contactos.Mes);
            // El vendedor solo llama en laborables (lunes a viernes sin festivos; los viernes de reunión, de momento,
            // cuentan como uno más): lo que falta del mes se reparte entre los que quedan, hoy incluido.
            int objetivoHoy = restantes > 0 ? (int)Math.Ceiling((double)faltan / restantes) : 0;
            int sinContactoMes = CompradoresSinContactoMes(cartera, hoy);

            return new RitmoContactosDTO
            {
                ContactosHoy = contactos.Hoy,
                ContactosSemana = contactos.Semana,
                ContactosMes = contactos.Mes,
                ContactosMesAntesDeHoy = contactos.MesAntesDeHoy,
                ContactosPorDia = contactos.PorDia ?? new Dictionary<DateTime, int>(),
                ObjetivoMes = objetivoMes,
                ObjetivoHoy = objetivoHoy,
                DiasLaborablesRestantesMes = restantes,
                PendientesMaxima = pendientes.Count(p => p.Prioridad == PrioridadesContacto.MAXIMA),
                PendientesAlta = pendientes.Count(p => p.Prioridad == PrioridadesContacto.ALTA),
                PendientesMedia = pendientes.Count(p => p.Prioridad == PrioridadesContacto.MEDIA),
                PendientesBaja = pendientes.Count(p => p.Prioridad == PrioridadesContacto.BAJA),
                CompradoresSinContactoMes = sinContactoMes,
                CupoCoberturaHoy = CupoCobertura(sinContactoMes, hoy, esLaborable),
                Frase = Frase(contactos, objetivoMes, objetivoHoy, restantes)
            };
        }

        /// <summary>Corte 1: plantilla con los números (el corte 4 la sustituirá por frases variadas).</summary>
        public static string Frase(ContactosVendedor contactos, int objetivoMes, int objetivoHoy, int restantes)
        {
            string hoy = $"Lleva {contactos.Hoy} {(contactos.Hoy == 1 ? "contacto" : "contactos")} hoy";
            if (objetivoMes > 0 && contactos.Mes >= objetivoMes)
            {
                return $"{hoy} y {contactos.Mes} este mes: ya ha cubierto el objetivo de {objetivoMes}. ¡Buen trabajo!";
            }
            if (restantes == 0)
            {
                return $"{hoy}; este mes van {contactos.Mes} de los {objetivoMes} que hacían falta para cubrir la cartera.";
            }
            return $"{hoy}; para cubrir la cartera este mes necesita {objetivoHoy} al día.";
        }
    }
}
