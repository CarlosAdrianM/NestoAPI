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
    /// NestoAPI#603 (corte 1): decide a quién de la cartera hay que llamar, con qué prioridad y por qué, y calcula el
    /// ritmo (objetivo del mes y del día). Pura: recibe la cartera ya calculada, el día y qué días son laborables.
    /// <list type="bullet">
    /// <item><b>Cadencia</b> (días entre contactos) = clamp(365 / pedidos en 12 meses, 7, 30); 30 si solo compró en 24 meses.</item>
    /// <item><b>No se propone</b> si se habló con él hace menos que su cadencia, si no contestó hace menos de 2 días o si
    /// pidió hace menos de 6.</item>
    /// <item><b>Máxima</b>: buen cliente (≥ 3 pedidos en 12 meses), probabilidad ≥ 0,6 y ya le toca por cadencia.
    /// <b>Alta</b>: buen cliente al que ya le toca por cadencia (probabilidad &lt; 0,6) o con ≥ 30 días sin contacto. <b>Media</b>: compras en 12 meses y ≥ 30 días sin contacto.
    /// <b>Baja</b>: solo compras en 24 meses y ≥ 30 días sin contacto. Si no cumple ninguna, todavía no le toca.</item>
    /// <item><b>Orden</b>: prioridad; dentro, probabilidad desc y días sin contacto desc (nunca contactado, el primero).</item>
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
            int? diasPedido = DiasDesde(c.UltimoPedido, hoy);
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

        /// <summary>Toda la cartera a la que hoy le toca, ya ordenada y con Orden 1, 2, 3…</summary>
        public List<SugerenciaContactoDTO> Priorizar(IEnumerable<ClienteCarteraContacto> cartera, DateTime hoy)
        {
            var lista = cartera
                .Select(c => new { Cliente = c, Prioridad = Prioridad(c, hoy), DiasContacto = DiasDesde(c.UltimoContacto, hoy) })
                .Where(x => x.Prioridad != null)
                .OrderBy(x => PrioridadesContacto.Rango(x.Prioridad))
                .ThenByDescending(x => x.Cliente.Probabilidad)
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
                resultado.Add(sugerencia);
            }
            return resultado;
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
                        ? $"lleva {diasContacto.Value} {(diasContacto.Value == 1 ? "día" : "días")} sin hablar contigo"
                        : "no consta ningún contacto";
                    texto = $"{frecuencia} y {contacto}; probabilidad de pedido del {Math.Round(c.Probabilidad * 100, MidpointRounding.AwayFromZero)} %";
                    break;
                case PrioridadesContacto.ALTA:
                    texto = Cadencia(c) < UmbralesSugerenciasContacto.CADENCIA_MAXIMA_DIAS && diasContacto.HasValue
                        ? $"Compra cada {Cadencia(c)} días y lleva {diasContacto.Value} {(diasContacto.Value == 1 ? "día" : "días")} sin hablar contigo"
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

        /// <summary>Σ por cliente de max(1, redondeo(laborables del mes / cadencia)).</summary>
        public static int ObjetivoMes(IEnumerable<ClienteCarteraContacto> cartera, int diasLaborablesMes)
        {
            return cartera
                .Where(c => c.Pedidos24Meses > 0 || c.Pedidos12Meses > 0)
                .Sum(c => Math.Max(1, (int)Math.Round((double)diasLaborablesMes / Cadencia(c), MidpointRounding.AwayFromZero)));
        }

        public RitmoContactosDTO CalcularRitmo(IList<ClienteCarteraContacto> cartera, ContactosVendedor contactos,
            IList<SugerenciaContactoDTO> pendientes, DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            contactos = contactos ?? new ContactosVendedor();
            int laborablesMes = DiasLaborablesDelMes(hoy, esLaborable);
            int restantes = DiasLaborablesRestantes(hoy, esLaborable);
            int objetivoMes = ObjetivoMes(cartera, laborablesMes);
            int faltan = Math.Max(0, objetivoMes - contactos.Mes);
            int objetivoHoy = restantes > 0 ? (int)Math.Ceiling((double)faltan / restantes) : 0;

            return new RitmoContactosDTO
            {
                ContactosHoy = contactos.Hoy,
                ContactosSemana = contactos.Semana,
                ContactosMes = contactos.Mes,
                ObjetivoMes = objetivoMes,
                ObjetivoHoy = objetivoHoy,
                DiasLaborablesRestantesMes = restantes,
                PendientesMaxima = pendientes.Count(p => p.Prioridad == PrioridadesContacto.MAXIMA),
                PendientesAlta = pendientes.Count(p => p.Prioridad == PrioridadesContacto.ALTA),
                PendientesMedia = pendientes.Count(p => p.Prioridad == PrioridadesContacto.MEDIA),
                PendientesBaja = pendientes.Count(p => p.Prioridad == PrioridadesContacto.BAJA),
                Frase = Frase(contactos, objetivoMes, objetivoHoy, restantes)
            };
        }

        /// <summary>Corte 1: plantilla con los números (el corte 4 la sustituirá por frases variadas).</summary>
        public static string Frase(ContactosVendedor contactos, int objetivoMes, int objetivoHoy, int restantes)
        {
            string hoy = $"Llevas {contactos.Hoy} {(contactos.Hoy == 1 ? "contacto" : "contactos")} hoy";
            if (objetivoMes > 0 && contactos.Mes >= objetivoMes)
            {
                return $"{hoy} y {contactos.Mes} este mes: ya has cubierto el objetivo de {objetivoMes}. ¡Buen trabajo!";
            }
            if (restantes == 0)
            {
                return $"{hoy}; este mes van {contactos.Mes} de los {objetivoMes} que hacían falta para cubrir la cartera.";
            }
            return $"{hoy}; para cubrir la cartera este mes necesitas {objetivoHoy} al día.";
        }
    }
}
