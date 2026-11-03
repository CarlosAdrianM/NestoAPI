using NestoAPI.Models.Novedades;
using System;
using System.Collections.Generic;
using System.Linq;
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

        private const char SEPARADOR_ESPERADOS = ';';
        private const char SEPARADOR_CAMPOS = '|';

        /// <summary>Longitud de la columna Novedades.EsperandoRespuestaDe (Scripts/Issue783_EsperandoRespuestaDe.sql).</summary>
        public const int LONGITUD_ESPERANDO_RESPUESTA_DE = 1000;

        /// <summary>
        /// NestoAPI#783: los mencionados en la pregunta del asistente, para la columna EsperandoRespuestaDe
        /// («Aplicacion|Clave|Nombre;…»). Sin ninguno, null: se espera al autor de la sugerencia, como antes.
        /// Si no cupieran todos, los que quepan.
        /// </summary>
        public static string SerializarEsperados(IEnumerable<MencionableDTO> esperados)
        {
            var partes = new List<string>();
            int longitud = 0;
            foreach (MencionableDTO esperado in (esperados ?? Enumerable.Empty<MencionableDTO>()).Where(e => !string.IsNullOrWhiteSpace(e?.Clave)))
            {
                string parte = string.Join(SEPARADOR_CAMPOS.ToString(), Limpiar(esperado.Aplicacion), Limpiar(esperado.Clave), Limpiar(esperado.Nombre));
                int nueva = longitud + parte.Length + (partes.Count > 0 ? 1 : 0);
                if (nueva > LONGITUD_ESPERANDO_RESPUESTA_DE)
                {
                    break;
                }
                partes.Add(parte);
                longitud = nueva;
            }
            return partes.Count == 0 ? null : string.Join(SEPARADOR_ESPERADOS.ToString(), partes);
        }

        /// <summary>NestoAPI#783: a quién se espera, según la columna. Vacía = al autor.</summary>
        public static List<MencionableDTO> Esperados(EsperaRespuestaNovedad espera)
        {
            var resultado = new List<MencionableDTO>();
            if (string.IsNullOrWhiteSpace(espera?.EsperandoRespuestaDe))
            {
                return resultado;
            }
            foreach (string parte in espera.EsperandoRespuestaDe.Split(SEPARADOR_ESPERADOS))
            {
                string[] campos = parte.Split(SEPARADOR_CAMPOS);
                if (campos.Length < 3 || string.IsNullOrWhiteSpace(campos[1]))
                {
                    continue;
                }
                resultado.Add(new MencionableDTO { Aplicacion = campos[0].Trim(), Clave = campos[1].Trim(), Nombre = campos[2].Trim() });
            }
            return resultado;
        }

        /// <summary>
        /// NestoAPI#783: si quien comenta es una de las personas a las que se espera. Sin mencionados, el autor;
        /// con mencionados, solo ellos (por su clave o, en las apps, por su nombre de usuario).
        /// </summary>
        public static bool EsEsperado(EsperaRespuestaNovedad espera, string usuario, string nombreUsuario = null)
        {
            List<MencionableDTO> esperados = Esperados(espera);
            if (esperados.Count == 0)
            {
                return EsDelAutor(espera, usuario);
            }
            return esperados.Any(e => Igual(e.Clave, usuario) || Igual(e.Clave, nombreUsuario));
        }

        /// <summary>
        /// Solo la persona a la que se espera (NestoAPI#783: la mencionada en la pregunta; sin mención, el AUTOR)
        /// quita la espera. Si estaba Cerrada, la reabre ella o su autor. Cualquier otro comentario (el asistente,
        /// otro compañero, el autor cuando se esperaba a otro) la deja como estaba.
        /// </summary>
        public static DecisionAlComentar AlComentar(EsperaRespuestaNovedad espera, string usuario, string nombreUsuario = null)
        {
            var decision = new DecisionAlComentar();
            if (espera == null)
            {
                return decision;
            }
            bool esperado = EsEsperado(espera, usuario, nombreUsuario);
            bool cerrada = string.Equals(espera.Estado?.Trim(), ReglasSugerenciasNovedades.ESTADO_CERRADA, StringComparison.OrdinalIgnoreCase);
            if (cerrada)
            {
                decision.Reabrir = esperado || EsDelAutor(espera, usuario);
                decision.BorrarEspera = decision.Reabrir;
                return decision;
            }
            decision.BorrarEspera = esperado && (espera.EsperandoRespuestaDesde != null || espera.RecordatoriosRespuesta > 0);
            return decision;
        }

        /// <summary>El recordatorio <paramref name="numero"/> (1 o 2), de usted y con la @mención del autor.</summary>
        public static string TextoRecordatorio(string nombre, int numero, bool esIncidencia) =>
            TextoRecordatorio(new[] { nombre }, numero, esIncidencia);

        /// <summary>NestoAPI#783: el recordatorio a quienes se espera (los mencionados o el autor), cada uno con su @mención.</summary>
        public static string TextoRecordatorio(IEnumerable<string> nombres, int numero, bool esIncidencia)
        {
            string saludo = Saludo(nombres);
            return numero <= 1
                ? $"{saludo} Le recordamos que estamos esperando su respuesta a la pregunta de arriba para poder seguir con esto."
                : $"{saludo} Seguimos esperando su respuesta a la pregunta de arriba. Si no tenemos noticias suyas, en unos días daremos {Que(esIncidencia)} por cerrada.";
        }

        public static string TextoCierre(string nombre, bool esIncidencia) => TextoCierre(new[] { nombre }, esIncidencia);

        public static string TextoCierre(IEnumerable<string> nombres, bool esIncidencia) =>
            $"{Saludo(nombres)} Como no hemos tenido respuesta, damos {Que(esIncidencia)} por cerrada. Si la sigue necesitando, escriba aquí y se volverá a abrir.";

        /// <summary>NestoAPI#783: los nombres a los que va el recordatorio (los esperados o, sin ellos, el autor).</summary>
        public static List<string> NombresDestinatarios(EsperaRespuestaNovedad espera)
        {
            List<MencionableDTO> esperados = Esperados(espera);
            return esperados.Count > 0
                ? esperados.Select(e => e.Nombre).ToList()
                : new List<string> { espera?.SugeridaNombre };
        }

        private static bool Igual(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
            && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Limpiar(string valor) =>
            (valor ?? string.Empty).Trim().Replace(SEPARADOR_ESPERADOS.ToString(), string.Empty).Replace(SEPARADOR_CAMPOS.ToString(), string.Empty);

        public static bool EsIncidencia(EsperaRespuestaNovedad espera) =>
            string.Equals(espera?.Categoria?.Trim(), ReglasSugerenciasNovedades.CATEGORIA_INCIDENCIA, StringComparison.OrdinalIgnoreCase);

        private static string Que(bool esIncidencia) => esIncidencia ? "esta incidencia" : "esta sugerencia";

        /// <summary>
        /// «Hola, @Marta.» (si el nombre no vale como mención, sin la @; sin nombre, «Hola.»). Con varios:
        /// «Hola, @Santiago y @Pedro.».
        /// </summary>
        private static string Saludo(IEnumerable<string> nombres)
        {
            List<string> menciones = (nombres ?? Enumerable.Empty<string>())
                .Select(n => n?.Trim())
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(n => Regex.IsMatch(n, @"^[\p{L}][\p{L}\p{Nd}_]*$") ? "@" + n : n)
                .ToList();
            if (menciones.Count == 0)
            {
                return "Hola.";
            }
            string lista = menciones.Count == 1
                ? menciones[0]
                : string.Join(", ", menciones.Take(menciones.Count - 1)) + " y " + menciones[menciones.Count - 1];
            return $"Hola, {lista}.";
        }
    }
}
