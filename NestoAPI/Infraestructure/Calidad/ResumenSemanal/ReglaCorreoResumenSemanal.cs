using NestoAPI.Infraestructure.Calidad.Almacen;
using NestoAPI.Infraestructure.Calidad.Encuestas;
using NestoAPI.Infraestructure.Calidad.Tablero;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NestoAPI.Infraestructure.Calidad.ResumenSemanal
{
    // NestoAPI#914, el correo semanal de calidad (Carlos 27/11/26): «un correo automático cada lunes, SOLO A ÉL, con el resumen». Aquí, LO PURO:
    // los datos ya leídos (DatosResumenSemanal) → el modelo del correo (CorreoResumenSemanal), con todos los textos de usted. Lo pinta
    // PintorCorreoResumenSemanal; lo lee y lo manda ServicioCorreoResumenSemanal. Definiciones en docs/claude/calidad-kpi.md.

    /// <summary>Lo que ya está guardado del tablero (las semanas de la ventana y los umbrales).</summary>
    public class DatosTableroResumen
    {
        public IReadOnlyList<IKpiTablero> Catalogo { get; set; } = CatalogoKpiTablero.Todos;
        public List<SemanaKpiGuardada> Semanas { get; set; } = new List<SemanaKpiGuardada>();
        public List<UmbralesKpi> Umbrales { get; set; } = new List<UmbralesKpi>();
    }

    /// <summary>Todo lo que hace falta para el correo de una semana. Una parte null = no disponible (con su motivo).</summary>
    public class DatosResumenSemanal
    {
        /// <summary>El lunes de la semana cerrada.</summary>
        public DateTime Semana { get; set; }
        /// <summary>Hoy en Madrid (decide qué semanas están cerradas para cada KPI).</summary>
        public DateTime Hoy { get; set; }
        public DateTime GeneradoEl { get; set; }

        public DatosTableroResumen Tablero { get; set; }
        public string TableroNoDisponible { get; set; }
        public bool TableroSinTablas { get; set; }

        public ResumenCalidadAlmacenDTO Almacen { get; set; }
        public string AlmacenNoDisponible { get; set; }

        public SimulacionEncuestasDTO Encuestas { get; set; }
        public string EncuestasNoDisponible { get; set; }
        public bool EncuestasSinTablas { get; set; }
        /// <summary>El interruptor Calidad.EncuestasSombra (null si no se ha podido leer).</summary>
        public bool? EncuestasSombraEncendida { get; set; }
    }

    public class FilaKpiCorreo
    {
        public string Dueno { get; set; }
        public string Valor { get; set; }
        /// <summary>POOR…FANTASTIC, «sin umbral» o «sin nota».</summary>
        public string Nota { get; set; }
        /// <summary>Las últimas 8 semanas (de la más antigua a la más nueva): P, F, G, ★, «·» (valor sin nota) o «–» (sin dato).</summary>
        public List<string> Tira { get; set; } = new List<string>();
    }

    public class KpiCorreo
    {
        public string Clave { get; set; }
        public string Nombre { get; set; }
        /// <summary>La semana de la tabla (null si no hay tabla).</summary>
        public DateTime? SemanaMostrada { get; set; }
        /// <summary>Lo que hay que decir antes de la tabla (madura, sin calcular, sin fuente…). Null si nada.</summary>
        public string Linea { get; set; }
        public List<FilaKpiCorreo> Filas { get; set; } = new List<FilaKpiCorreo>();
    }

    public class SeccionTableroCorreo
    {
        public string NoDisponible { get; set; }
        public List<KpiCorreo> Kpis { get; set; } = new List<KpiCorreo>();
    }

    public class FilaAlmacenCorreo
    {
        public string Preparador { get; set; }
        public int Lineas { get; set; }
        public string ErrorDeEnvioReclamado { get; set; }
        public string FaltaEnPacking { get; set; }
        public bool EsTotal { get; set; }
    }

    public class SeccionAlmacenCorreo
    {
        public string NoDisponible { get; set; }
        public List<FilaAlmacenCorreo> Filas { get; set; } = new List<FilaAlmacenCorreo>();
        public List<string> Notas { get; set; } = new List<string>();
    }

    public class RecuentoCorreo
    {
        public string Que { get; set; }
        public int Candidatas { get; set; }
        public int Encuestas { get; set; }
    }

    public class SeccionEncuestasCorreo
    {
        public string NoDisponible { get; set; }
        public List<string> Lineas { get; set; } = new List<string>();
        public List<RecuentoCorreo> PorTema { get; set; } = new List<RecuentoCorreo>();
        public List<RecuentoCorreo> PorCanal { get; set; } = new List<RecuentoCorreo>();
    }

    /// <summary>El modelo del correo: textos ya hechos; el pintor solo los coloca.</summary>
    public class CorreoResumenSemanal
    {
        public DateTime Semana { get; set; }
        public DateTime Domingo { get; set; }
        public string Asunto { get; set; }
        public List<string> Cambios { get; set; } = new List<string>();
        public SeccionTableroCorreo Tablero { get; set; } = new SeccionTableroCorreo();
        public SeccionAlmacenCorreo Almacen { get; set; } = new SeccionAlmacenCorreo();
        public SeccionEncuestasCorreo Encuestas { get; set; } = new SeccionEncuestasCorreo();
        public List<string> Pendientes { get; set; } = new List<string>();
        public List<string> Pie { get; set; } = new List<string>();
        public DateTime GeneradoEl { get; set; }
    }

    /// <summary>NestoAPI#914: LA REGLA del correo semanal de calidad (pura). Datos ya leídos → modelo con los textos.</summary>
    public static class ReglaCorreoResumenSemanal
    {
        public const int MAXIMO_CAMBIOS = 5;
        public const string PARAMETRO_DESTINATARIO = "Calidad.CorreoResumenSemanal";
        public const string TIRA_SIN_NOTA = "·";
        public const string TIRA_SIN_DATO = "–";
        internal static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>La semana que toca el lunes <paramref name="hoy"/>: la última cerrada (lunes a domingo anterior).</summary>
        public static DateTime SemanaCerrada(DateTime hoy) => SemanaKpi.LunesDe(hoy).AddDays(-7);

        /// <summary>«Calidad: semana del 23 al 29/11/2026» (con el mes o el año delante si la semana los cruza).</summary>
        public static string Asunto(DateTime lunes)
        {
            lunes = SemanaKpi.LunesDe(lunes);
            DateTime domingo = lunes.AddDays(6);
            string desde = lunes.Year != domingo.Year ? lunes.ToString("dd/MM/yyyy", Es)
                : lunes.Month != domingo.Month ? lunes.ToString("dd/MM", Es)
                : lunes.ToString("dd", Es);
            return $"Calidad: semana del {desde} al {domingo.ToString("dd/MM/yyyy", Es)}";
        }

        /// <summary>
        /// La semana que se enseña de un KPI: la pedida si ya está cerrada para él; si no (madura), la última cerrada a <paramref name="hoy"/>.
        /// </summary>
        public static DateTime SemanaMostrada(IKpiTablero kpi, DateTime semana, DateTime hoy)
        {
            DateTime lunes = SemanaKpi.LunesDe(semana);
            if (SemanaKpi.EstaCerrada(lunes, kpi.DiasMaduracion, hoy))
            {
                return lunes;
            }
            DateTime ultima = SemanaKpi.UltimaCerrada(kpi.DiasMaduracion, hoy);
            return ultima < lunes ? ultima : lunes;
        }

        /// <summary>Desde qué lunes hay que leer dbo.CalidadKpiSemanas para la tira (8) y la tendencia (4 anteriores) de todos los KPI.</summary>
        public static DateTime DesdeLectura(IEnumerable<IKpiTablero> catalogo, DateTime semana, DateTime hoy)
        {
            DateTime primera = (catalogo ?? CatalogoKpiTablero.Todos).Where(k => k.TieneFuente)
                .Select(k => SemanaMostrada(k, semana, hoy)).DefaultIfEmpty(SemanaKpi.LunesDe(semana)).Min();
            return primera.AddDays(-7 * (ServicioTablero.SEMANAS_TIRA - 1 + ReglaNotaKpi.SEMANAS_TENDENCIA));
        }

        /// <summary>
        /// ¿El tablero aún no tiene la semana? (tablas sí, pero ningún KPI sin maduración con fuente tiene filas de ella: el job de las 4:20 no ha
        /// pasado o se fue a la noche). La pérdida desconocida siempre guarda los tres almacenes, así que «sin filas» es «sin calcular».
        /// </summary>
        public static bool TableroSinLaSemana(DatosResumenSemanal datos)
        {
            if (datos?.Tablero == null)
            {
                return false;
            }
            DateTime lunes = SemanaKpi.LunesDe(datos.Semana);
            List<IKpiTablero> sinMadurar = datos.Tablero.Catalogo.Where(k => k.TieneFuente && k.DiasMaduracion == 0).ToList();
            return sinMadurar.Count > 0 && !sinMadurar.Any(k => datos.Tablero.Semanas.Any(s =>
                s.Semana.Date == lunes && string.Equals(s.Kpi, k.Clave, StringComparison.OrdinalIgnoreCase)));
        }

        public static CorreoResumenSemanal Componer(DatosResumenSemanal datos)
        {
            if (datos == null)
            {
                throw new ArgumentNullException(nameof(datos));
            }
            DateTime lunes = SemanaKpi.LunesDe(datos.Semana);
            var correo = new CorreoResumenSemanal
            {
                Semana = lunes,
                Domingo = lunes.AddDays(6),
                Asunto = Asunto(lunes),
                GeneradoEl = datos.GeneradoEl
            };
            ComponerTablero(datos, lunes, correo);
            ComponerAlmacen(datos, correo);
            ComponerEncuestas(datos, correo);
            ComponerPendientes(datos, correo);
            ComponerPie(datos, lunes, correo);
            return correo;
        }

        // ---------------------------------------------------------------- Tablero y «lo que más ha cambiado»

        private class Cambio
        {
            public decimal Peso { get; set; }
            public decimal Diferencia { get; set; }
            public int Orden { get; set; }
            public string Texto { get; set; }
        }

        private static void ComponerTablero(DatosResumenSemanal datos, DateTime lunes, CorreoResumenSemanal correo)
        {
            if (datos.Tablero == null)
            {
                correo.Tablero.NoDisponible = "No disponible: " + (datos.TableroNoDisponible ?? "no se ha podido leer el tablero.");
                correo.Cambios.Add("No disponible: sin el tablero no se puede comparar con las semanas anteriores.");
                return;
            }
            var cambios = new List<Cambio>();
            int orden = 0;
            foreach (IKpiTablero kpi in datos.Tablero.Catalogo)
            {
                var k = new KpiCorreo { Clave = kpi.Clave, Nombre = kpi.Nombre };
                correo.Tablero.Kpis.Add(k);
                if (!kpi.TieneFuente)
                {
                    k.Linea = "Todavía no disponible: este indicador aún no tiene de dónde sacar los datos.";
                    continue;
                }
                DateTime mostrada = SemanaMostrada(kpi, lunes, datos.Hoy);
                List<SemanaKpiGuardada> delKpi = datos.Tablero.Semanas
                    .Where(s => string.Equals(s.Kpi, kpi.Clave, StringComparison.OrdinalIgnoreCase)).ToList();
                var lineas = new List<string>();
                if (mostrada != lunes)
                {
                    string porQue = string.IsNullOrWhiteSpace(kpi.PorQueMadura) ? string.Empty : kpi.PorQueMadura.Trim().TrimEnd('.') + ". ";
                    lineas.Add($"La semana del {Dia(lunes)} estará el {Dia(SemanaKpi.CerradaEl(lunes, kpi.DiasMaduracion))}. {porQue}" +
                        $"Abajo, la última cerrada: la del {Dia(mostrada)}.");
                }
                List<SemanaKpiGuardada> deLaSemana = delKpi.Where(s => s.Semana.Date == mostrada)
                    .OrderBy(s => s.TipoDueno).ThenBy(s => s.Dueno, StringComparer.Ordinal).ToList();
                if (deLaSemana.Count == 0)
                {
                    lineas.Add($"La semana del {Dia(mostrada)} no está calculada todavía (el job del tablero la calcula los lunes a las 4:20; si la cola " +
                        "iba con retraso, se pasa a las 21:00).");
                    k.Linea = string.Join(" ", lineas);
                    continue;
                }
                k.SemanaMostrada = mostrada;
                k.Linea = lineas.Count == 0 ? null : string.Join(" ", lineas);
                foreach (SemanaKpiGuardada fila in deLaSemana)
                {
                    List<SemanaKpiGuardada> suyas = delKpi.Where(s => s.TipoDueno == fila.TipoDueno
                        && string.Equals(s.Dueno?.Trim(), fila.Dueno?.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                    UmbralesKpi umbral = ReglaNotaKpi.Vigente(datos.Tablero.Umbrales, kpi.Clave, fila.TipoDueno, mostrada);
                    NotaKpi? nota = ReglaNotaKpi.Nota(fila.Valor, umbral, kpi.Sentido);
                    k.Filas.Add(new FilaKpiCorreo
                    {
                        Dueno = NombreDueno(fila.TipoDueno, fila.Dueno),
                        Valor = Valor(fila.Valor, kpi.Unidad),
                        Nota = umbral == null ? "sin umbral" : nota?.ToString() ?? "sin nota",
                        Tira = Tira(kpi, suyas, mostrada, datos.Tablero.Umbrales)
                    });
                    Cambio cambio = CambioDe(kpi, fila, suyas, nota, ++orden);
                    if (cambio != null)
                    {
                        cambios.Add(cambio);
                    }
                }
            }
            correo.Cambios = cambios.OrderByDescending(c => c.Peso).ThenByDescending(c => c.Diferencia).ThenBy(c => c.Orden)
                .Take(MAXIMO_CAMBIOS).Select(c => c.Texto).ToList();
            if (correo.Cambios.Count == 0)
            {
                correo.Cambios.Add("Nada ha cambiado de forma apreciable respecto a las 4 semanas anteriores (o aún no hay semanas con las que comparar).");
            }
        }

        /// <summary>Las notas de las 8 semanas que acaban en <paramref name="mostrada"/>, de la más antigua a la más nueva.</summary>
        internal static List<string> Tira(IKpiTablero kpi, IEnumerable<SemanaKpiGuardada> suyas, DateTime mostrada, IEnumerable<UmbralesKpi> umbrales)
        {
            List<SemanaKpiGuardada> lista = suyas.ToList();
            var tira = new List<string>();
            for (int i = ServicioTablero.SEMANAS_TIRA - 1; i >= 0; i--)
            {
                DateTime semana = mostrada.AddDays(-7 * i);
                SemanaKpiGuardada s = lista.FirstOrDefault(x => x.Semana.Date == semana);
                if (s == null || !s.Valor.HasValue)
                {
                    tira.Add(TIRA_SIN_DATO);
                    continue;
                }
                NotaKpi? nota = ReglaNotaKpi.Nota(s.Valor, ReglaNotaKpi.Vigente(umbrales, kpi.Clave, s.TipoDueno, semana), kpi.Sentido);
                tira.Add(LetraNota(nota));
            }
            return tira;
        }

        public static string LetraNota(NotaKpi? nota)
        {
            switch (nota)
            {
                case NotaKpi.POOR: return "P";
                case NotaKpi.FAIR: return "F";
                case NotaKpi.GOOD: return "G";
                case NotaKpi.FANTASTIC: return "★";
                default: return TIRA_SIN_NOTA;
            }
        }

        private static Cambio CambioDe(IKpiTablero kpi, SemanaKpiGuardada fila, List<SemanaKpiGuardada> suyas, NotaKpi? nota, int orden)
        {
            List<decimal> previas = suyas.Where(s => s.Semana.Date < fila.Semana.Date && s.Valor.HasValue)
                .OrderByDescending(s => s.Semana).Take(ReglaNotaKpi.SEMANAS_TENDENCIA).Select(s => s.Valor.Value).ToList();
            TendenciaKpi? tendencia = ReglaNotaKpi.Tendencia(fila.Valor, previas.Cast<decimal?>());
            if (!tendencia.HasValue || tendencia.Value == TendenciaKpi.Igual)
            {
                return null;
            }
            decimal media = previas.Average();
            decimal diferencia = Math.Abs(fila.Valor.Value - media);
            bool? mejora = ReglaNotaKpi.Mejora(tendencia, kpi.Sentido);
            string anteriores = previas.Count == 1 ? "la semana anterior" : $"las {previas.Count} semanas anteriores";
            string notaTexto = nota.HasValue ? $" ({nota})" : string.Empty;
            string verbo = tendencia == TendenciaKpi.Sube ? "sube" : "baja";
            string sentido = mejora == true ? "a mejor" : "a peor";
            return new Cambio
            {
                // Con media 0 cualquier cambio es «infinito»: va delante de todo (pasar de nada a algo es lo primero que hay que ver).
                Peso = media == 0 ? decimal.MaxValue : diferencia / Math.Abs(media),
                Diferencia = diferencia,
                Orden = orden,
                Texto = $"{kpi.Nombre} {EnDueno(fila.TipoDueno, fila.Dueno)}: {Valor(fila.Valor, kpi.Unidad)}{notaTexto}, {verbo} respecto a {anteriores} " +
                    $"({Valor(Math.Round(media, 2, MidpointRounding.AwayFromZero), kpi.Unidad)}), {sentido}."
            };
        }

        // ---------------------------------------------------------------- Almacén

        private static void ComponerAlmacen(DatosResumenSemanal datos, CorreoResumenSemanal correo)
        {
            ResumenCalidadAlmacenDTO a = datos.Almacen;
            if (a == null)
            {
                correo.Almacen.NoDisponible = "No disponible: " + (datos.AlmacenNoDisponible ?? "no se han podido calcular los indicadores de Almacén.");
                return;
            }
            foreach (PreparadorAlmacenDTO p in (a.Preparadores ?? new List<PreparadorAlmacenDTO>()).OrderBy(p => p.Preparador, StringComparer.OrdinalIgnoreCase))
            {
                correo.Almacen.Filas.Add(FilaAlmacen(p, p.Preparador, false));
            }
            if (a.Total != null)
            {
                correo.Almacen.Filas.Add(FilaAlmacen(a.Total, "Total", true));
            }
            if (correo.Almacen.Filas.Count == 0)
            {
                correo.Almacen.Notas.Add("No hay pickings con rastro de Ariadna esta semana.");
            }
            correo.Almacen.Notas.Add($"Error de envío reclamado: sobre las líneas servidas hace al menos {a.DiasReclamacionEnvio} días (el cliente aún puede " +
                "reclamar las de la semana recién cerrada, así que casi siempre sale «sin evaluar»). Falta en packing: sobre las líneas empaquetadas con Ariadna.");
            if (a.PickingsSinRastro != null && a.PickingsSinRastro.Pickings > 0)
            {
                correo.Almacen.Notas.Add($"{a.PickingsSinRastro.Pickings} pickings ({a.PickingsSinRastro.Lineas} líneas) sin ninguna lectura de Ariadna (hechos en papel): no cuentan.");
            }
            if (a.LineasSinPreparador > 0)
            {
                correo.Almacen.Notas.Add($"{a.LineasSinPreparador} líneas sin preparador (nadie las leyó en el recorrido).");
            }
            if (a.EtiquetasConPackingIncompleto?.Total != null && a.EtiquetasConPackingIncompleto.Total.Etiquetas > 0)
            {
                correo.Almacen.Notas.Add($"Etiquetas impresas con el packing incompleto: {a.EtiquetasConPackingIncompleto.Total.Etiquetas} (informativo, del empaquetador).");
            }
            if (!string.IsNullOrWhiteSpace(a.Aviso))
            {
                correo.Almacen.Notas.Add("Aviso: " + a.Aviso.Trim());
            }
        }

        private static FilaAlmacenCorreo FilaAlmacen(PreparadorAlmacenDTO p, string nombre, bool total) => new FilaAlmacenCorreo
        {
            Preparador = string.IsNullOrWhiteSpace(nombre) ? "(sin nombre)" : nombre.Trim(),
            Lineas = p.Lineas,
            ErrorDeEnvioReclamado = Indicador(p.ErrorDeEnvioReclamado),
            FaltaEnPacking = Indicador(p.FaltaEnPacking),
            EsTotal = total
        };

        internal static string Indicador(IndicadorPreparadorDTO i)
        {
            if (i == null || !i.Evaluadas.HasValue || i.Evaluadas.Value == 0)
            {
                return "sin evaluar";
            }
            string porcentaje = i.Porcentaje.HasValue ? i.Porcentaje.Value.ToString("0.0", Es) + " %" : "—";
            return $"{porcentaje} ({i.Con} de {i.Evaluadas.Value})";
        }

        // ---------------------------------------------------------------- Encuestas en sombra

        private static void ComponerEncuestas(DatosResumenSemanal datos, CorreoResumenSemanal correo)
        {
            SeccionEncuestasCorreo e = correo.Encuestas;
            if (datos.EncuestasSombraEncendida == false)
            {
                e.Lineas.Add($"El job de las encuestas en sombra está apagado (ParametrosUsuario «(defecto)» {ParametrosEncuestas.SOMBRA} distinto de «1»): " +
                    "no se ha decidido nada nuevo.");
            }
            SimulacionEncuestasDTO s = datos.Encuestas;
            if (s == null)
            {
                e.NoDisponible = "No disponible: " + (datos.EncuestasNoDisponible ?? "no se han podido leer las encuestas en sombra.");
                return;
            }
            string dias = s.DiasProcesados.HasValue ? $" en los {s.DiasProcesados.Value} de {Math.Max(1, s.Dias)} días procesados" : string.Empty;
            e.Lineas.Add($"Se habrían mandado {s.Encuestas} encuestas (de {s.Candidatas} candidatas){dias}. Nada se ha mandado: es en sombra.");
            e.Lineas.Add($"El tope entre encuestas (un cliente, como mucho una cada {s.DiasEntre} días) ha parado {s.PerdidasPorTope} candidatas.");
            e.PorTema = (s.PorTema ?? new List<TemaEncuestasDTO>()).Where(t => t.Candidatas > 0 || t.Encuestas > 0)
                .Select(t => new RecuentoCorreo { Que = TextoTema(t.Tema), Candidatas = t.Candidatas, Encuestas = t.Encuestas }).ToList();
            e.PorCanal = (s.PorCanal ?? new List<CanalEncuestasDTO>())
                .Select(c => new RecuentoCorreo { Que = c.Canal == CanalesEncuesta.PUSH ? "Push (app)" : c.Canal == CanalesEncuesta.CORREO ? "Correo" : c.Canal, Encuestas = c.Encuestas })
                .ToList();
        }

        public static string TextoTema(string tema)
        {
            if (!Enum.TryParse(tema, out TemaEncuesta t))
            {
                return tema;
            }
            switch (t)
            {
                case TemaEncuesta.PreparacionEntrega: return "Preparación y entrega";
                case TemaEncuesta.Vendedor: return "Vendedor";
                case TemaEncuesta.ProblemaResuelto: return "Problema resuelto";
                case TemaEncuesta.AdministracionCobro: return "Administración y cobro";
                case TemaEncuesta.InformacionTecnica: return "Información técnica";
                default: return tema;
            }
        }

        // ---------------------------------------------------------------- Pendiente de usted y pie

        private static void ComponerPendientes(DatosResumenSemanal datos, CorreoResumenSemanal correo)
        {
            var p = correo.Pendientes;
            if (datos.TableroSinTablas)
            {
                p.Add($"Lanzar {TableroKpiJobsService.SCRIPT} (tablero de KPI): sin él no hay tablero ni notas.");
            }
            if (datos.Tablero != null)
            {
                foreach (IKpiTablero kpi in datos.Tablero.Catalogo.Where(k => k.TieneFuente))
                {
                    List<TipoDuenoKpi> sinUmbral = kpi.TiposDueno.Where(t => !datos.Tablero.Umbrales.Any(u =>
                        u.TipoDueno == t && string.Equals(u.Kpi, kpi.Clave, StringComparison.OrdinalIgnoreCase))).ToList();
                    if (sinUmbral.Count > 0)
                    {
                        p.Add($"{kpi.Nombre} ({string.Join(", ", sinUmbral.Select(TextoTipo))}): sin umbrales aprobados, se enseña sin nota. " +
                            $"Propuesta: GET api/Calidad/Tablero/Umbrales/Propuesta?kpi={kpi.Clave}; se aprueba con PUT api/Calidad/Tablero/Umbrales.");
                    }
                }
            }
            if (datos.EncuestasSinTablas)
            {
                p.Add("Lanzar Scripts/Issue914e_CalidadEncuestas.sql (encuestas en sombra): sin él el job no guarda nada.");
            }
            if (datos.EncuestasSombraEncendida == false)
            {
                p.Add($"Encender las encuestas en sombra: ParametrosUsuario «(defecto)» {ParametrosEncuestas.SOMBRA} = 1 (no manda nada; solo decide y guarda).");
            }
            foreach (IndicadorSinRastroAlmacenDTO s in datos.Almacen?.SinRastro ?? new List<IndicadorSinRastroAlmacenDTO>())
            {
                if (s?.HaceFalta != null && s.HaceFalta.IndexOf("Scripts/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    p.Add($"Almacén, {s.Indicador}: {s.HaceFalta.Trim()}");
                }
            }
            if (p.Count == 0)
            {
                p.Add("Nada pendiente.");
            }
        }

        private static void ComponerPie(DatosResumenSemanal datos, DateTime lunes, CorreoResumenSemanal correo)
        {
            string d = lunes.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string h = lunes.AddDays(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            int diasSombra = Math.Max(1, (int)(datos.Hoy.Date - lunes).TotalDays);
            correo.Pie.Add($"Tablero: GET api/Calidad/Tablero?semana={d} (la evolución de cada uno, GET api/Calidad/Tablero/{{kpi}}/Semanas). " +
                "Lee dbo.CalidadKpiSemanas, que calcula el job de los lunes a las 4:20.");
            correo.Pie.Add($"Almacén: GET api/Calidad/Almacen/Resumen?desde={d}&hasta={h} (el detalle, GET api/Calidad/Almacen/Detalle con las mismas fechas).");
            correo.Pie.Add($"Encuestas en sombra: GET api/Calidad/Encuestas/Sombra?dias={diasSombra} (desde el lunes {Dia(lunes)} hasta hoy). Lee dbo.CalidadEncuestas.");
            correo.Pie.Add("Este correo es solo para usted: va a la dirección de ParametrosUsuario «(defecto)» " + PARAMETRO_DESTINATARIO +
                " y no se manda a Dirección. Para verlo sin mandarlo: POST api/Calidad/ResumenSemanal/Enviar?semana=" + d + "&ensayo=true.");
            correo.Pie.Add($"Generado el {datos.GeneradoEl.ToString("dd/MM/yyyy HH:mm", Es)}.");
        }

        // ---------------------------------------------------------------- Textos

        internal static string Dia(DateTime dia) => dia.ToString("dd/MM/yyyy", Es);

        public static string Valor(decimal? valor, UnidadKpi unidad)
        {
            if (!valor.HasValue)
            {
                return "sin base";
            }
            switch (unidad)
            {
                case UnidadKpi.Euros: return valor.Value.ToString("#,##0", Es) + " €";
                case UnidadKpi.Porcentaje: return valor.Value.ToString("#,##0.0", Es) + " %";
                default: return valor.Value.ToString("#,##0", Es) + " ud.";
            }
        }

        public static string NombreAlmacen(string codigo)
        {
            switch (codigo?.Trim().ToUpperInvariant())
            {
                case "ALG": return "Algete";
                case "REI": return "Reina";
                case "ALC": return "Alcobendas";
                default: return codigo?.Trim();
            }
        }

        public static string NombreDueno(TipoDuenoKpi tipo, string dueno)
        {
            switch (tipo)
            {
                case TipoDuenoKpi.Almacen: return NombreAlmacen(dueno);
                case TipoDuenoKpi.Empresa: return "Toda la empresa";
                case TipoDuenoKpi.Vendedor: return "Vendedor " + dueno?.Trim();
                default: return dueno?.Trim();
            }
        }

        /// <summary>«en Alcobendas», «en toda la empresa», «del vendedor JE», «de Alfredo».</summary>
        public static string EnDueno(TipoDuenoKpi tipo, string dueno)
        {
            switch (tipo)
            {
                case TipoDuenoKpi.Almacen: return "en " + NombreAlmacen(dueno);
                case TipoDuenoKpi.Empresa: return "en toda la empresa";
                case TipoDuenoKpi.Vendedor: return "del vendedor " + dueno?.Trim();
                default: return "de " + dueno?.Trim();
            }
        }

        private static string TextoTipo(TipoDuenoKpi tipo)
        {
            switch (tipo)
            {
                case TipoDuenoKpi.Almacen: return "por almacén";
                case TipoDuenoKpi.Empresa: return "de la empresa";
                case TipoDuenoKpi.Vendedor: return "por vendedor";
                default: return "por preparador";
            }
        }
    }
}
