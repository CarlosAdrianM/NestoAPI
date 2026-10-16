using NestoAPI.Models.Novedades;
using System;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Novedades
{
    public enum AccionEsperaRespuesta
    {
        Nada,
        Recordatorio,
        Cerrar
    }

    /// <summary>Qué hay que hacer con la espera cuando alguien comenta.</summary>
    public class DecisionAlComentar
    {
        public bool BorrarEspera { get; set; }
        public bool Reabrir { get; set; }
    }

    /// <summary>
    /// Recordatorios de respuesta (Carlos, 16/10/26): se acumulaban sugerencias e incidencias porque el asistente
    /// le pregunta algo al usuario y el usuario no contesta. Con «EsperaRespuesta» en el POST del asistente la
    /// sugerencia queda esperando al autor; cada <see cref="DIAS_LABORABLES_ENTRE_AVISOS"/> días laborables sin
    /// respuesta se le recuerda (dos veces) y, a la tercera, se da por cerrada (estado «Cerrada», que su autor
    /// sigue viendo) y se le dice que, si la quiere, escriba y se vuelve a abrir. Reglas puras para testear sin BD.
    /// </summary>
    public static class ReglasEsperaRespuestaNovedades
    {
        public const int DIAS_LABORABLES_ENTRE_AVISOS = 2;
        public const int MAXIMO_RECORDATORIOS = 2;

        /// <summary>Interruptor del job en Web.config (true = manda recordatorios y cierra).</summary>
        public const string CLAVE_INTERRUPTOR = "Novedades:RecordatoriosRespuesta";

        public const string ACCION_RECORDATORIO = "Recordatorio";
        public const string ACCION_CERRAR = "Cerrar";

        /// <summary>
        /// Días laborables (lunes a viernes y no festivos según <paramref name="esLaborable"/>) DESPUÉS del día de
        /// <paramref name="referencia"/> hasta hoy incluido. Pregunta el lunes → el miércoles van 2; el jueves → el lunes.
        /// </summary>
        public static int DiasLaborablesDesde(DateTime referencia, DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            int dias = 0;
            for (DateTime dia = referencia.Date.AddDays(1); dia <= hoy.Date; dia = dia.AddDays(1))
            {
                if (dia.DayOfWeek != DayOfWeek.Saturday && dia.DayOfWeek != DayOfWeek.Sunday && (esLaborable == null || esLaborable(dia)))
                {
                    dias++;
                }
            }
            return dias;
        }

        /// <summary>
        /// Lo que toca hoy con una sugerencia en espera: nada, el siguiente recordatorio o cerrarla (tras el 2.º sin
        /// respuesta). Solo las abiertas y sin versión: si ya se ha implementado o descartado, no se molesta a nadie.
        /// </summary>
        public static AccionEsperaRespuesta Decidir(EsperaRespuestaNovedad espera, DateTime hoy, Func<DateTime, bool> esLaborable)
        {
            if (espera?.EsperandoRespuestaDesde == null
                || !string.IsNullOrWhiteSpace(espera.Version)
                || !ReglasSugerenciasNovedades.EstaAbierta(espera.Estado?.Trim()))
            {
                return AccionEsperaRespuesta.Nada;
            }
            DateTime referencia = espera.UltimoRecordatorioRespuesta ?? espera.EsperandoRespuestaDesde.Value;
            if (DiasLaborablesDesde(referencia, hoy, esLaborable) < DIAS_LABORABLES_ENTRE_AVISOS)
            {
                return AccionEsperaRespuesta.Nada;
            }
            return espera.RecordatoriosRespuesta >= MAXIMO_RECORDATORIOS ? AccionEsperaRespuesta.Cerrar : AccionEsperaRespuesta.Recordatorio;
        }

        public static bool EsDelAutor(EsperaRespuestaNovedad espera, string usuario) =>
            espera != null
            && !string.IsNullOrWhiteSpace(usuario)
            && !string.IsNullOrWhiteSpace(espera.SugeridaPor)
            && string.Equals(espera.SugeridaPor.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Solo el AUTOR de la sugerencia quita la espera (y, si estaba Cerrada, la reabre). Cualquier otro comentario
        /// (el asistente, otro compañero) la deja como estaba.
        /// </summary>
        public static DecisionAlComentar AlComentar(EsperaRespuestaNovedad espera, string usuario)
        {
            var decision = new DecisionAlComentar();
            if (!EsDelAutor(espera, usuario))
            {
                return decision;
            }
            decision.Reabrir = string.Equals(espera.Estado?.Trim(), ReglasSugerenciasNovedades.ESTADO_CERRADA, StringComparison.OrdinalIgnoreCase);
            decision.BorrarEspera = decision.Reabrir || espera.EsperandoRespuestaDesde != null || espera.RecordatoriosRespuesta > 0;
            return decision;
        }

        /// <summary>El recordatorio <paramref name="numero"/> (1 o 2), de usted y con la @mención del autor.</summary>
        public static string TextoRecordatorio(string nombre, int numero, bool esIncidencia)
        {
            string saludo = Saludo(nombre);
            return numero <= 1
                ? $"{saludo} Le recordamos que estamos esperando su respuesta a la pregunta de arriba para poder seguir con esto."
                : $"{saludo} Seguimos esperando su respuesta a la pregunta de arriba. Si no tenemos noticias suyas, en unos días daremos {Que(esIncidencia)} por cerrada.";
        }

        public static string TextoCierre(string nombre, bool esIncidencia) =>
            $"{Saludo(nombre)} Como no hemos tenido respuesta, damos {Que(esIncidencia)} por cerrada. Si la sigue necesitando, escriba aquí y se volverá a abrir.";

        public static bool EsIncidencia(EsperaRespuestaNovedad espera) =>
            string.Equals(espera?.Categoria?.Trim(), ReglasSugerenciasNovedades.CATEGORIA_INCIDENCIA, StringComparison.OrdinalIgnoreCase);

        private static string Que(bool esIncidencia) => esIncidencia ? "esta incidencia" : "esta sugerencia";

        /// <summary>«Hola, @Marta.» (si el nombre no vale como mención, sin la @; sin nombre, «Hola.»).</summary>
        private static string Saludo(string nombre)
        {
            string limpio = nombre?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return "Hola.";
            }
            return Regex.IsMatch(limpio, @"^[\p{L}][\p{L}\p{Nd}_]*$") ? $"Hola, @{limpio}." : $"Hola, {limpio}.";
        }
    }
}
