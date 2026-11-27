using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// LA REGLA (pura) de los textos de la push de campaña por momento (27/11/26). Los ve el CLIENTE: de tú y con tildes. Todo sale de la
    /// campaña (su título sin el año y su fecha de fin): nada de una campaña en concreto. Cortos para que quepan en una notificación de
    /// móvil: título ≤ <see cref="MAXIMO_TITULO"/> y cuerpo ≤ <see cref="MAXIMO_CUERPO"/>, recortando el nombre del producto (o de la
    /// campaña) con «…».
    /// <list type="bullet">
    /// <item>Arranque: los de siempre (<see cref="TextosPushOfertas.CuerpoAutomatico(IReadOnlyList{OfertaElegida}, int?)"/> y
    /// <see cref="TextosPushOfertas.CuerpoGenerico"/>, título = el de la campaña), sin cambiar una coma.</item>
    /// <item>Recordatorio: «{Campaña}: más ofertas para ti» / «Te hemos elegido otras: {Producto} (−30 %) y 2 más.».</item>
    /// <item>Cierre: «Último día de {Campaña}» / «Termina hoy a las 23:59: {Producto} (−30 %) y 2 más.»; si acaba mañana,
    /// «{Campaña}: termina mañana» / «Termina mañana: …». Si el primero es de lo que miró: «{Producto} sigue al −30 % hasta esta noche.».</item>
    /// </list>
    /// </summary>
    public static class TextosMomentoPushCampana
    {
        public const int MAXIMO_TITULO = 45;
        public const int MAXIMO_CUERPO = 110;
        /// <summary>Aunque quepa, un nombre de producto más largo se recorta (se lee mal en la notificación).</summary>
        public const int MAXIMO_NOMBRE = 40;
        public const string PUNTOS = "…";

        /// <summary>El título de la push.</summary>
        public static string Titulo(string momento, string tituloCampana, DateTime? fin, DateTime ahora)
        {
            string m = MomentosPushCampana.Normalizar(momento) ?? MomentosPushCampana.ARRANQUE;
            string c = string.IsNullOrWhiteSpace(tituloCampana) ? null : tituloCampana.Trim();
            if (m == MomentosPushCampana.ARRANQUE)
            {
                return tituloCampana;
            }
            if (m == MomentosPushCampana.CIERRE && fin.HasValue && fin.Value.Date == ahora.Date)
            {
                return c == null ? "Último día de ofertas" : Encajar(x => $"Último día de {x}", c, MAXIMO_TITULO);
            }
            if (m == MomentosPushCampana.CIERRE && fin.HasValue && fin.Value.Date == ahora.Date.AddDays(1))
            {
                return c == null ? "Las ofertas terminan mañana" : Encajar(x => $"{x}: termina mañana", c, MAXIMO_TITULO);
            }
            return c == null ? "Más ofertas para ti" : Encajar(x => $"{x}: más ofertas para ti", c, MAXIMO_TITULO);
        }

        /// <summary>
        /// El cuerpo. <paramref name="nombrables"/> = las de lo suyo (las que se pueden nombrar, en orden); <paramref name="total"/> = las
        /// que lleva la pantalla. Sin nombrables, uno que no nombra ningún producto.
        /// </summary>
        public static string Cuerpo(string momento, string tituloCampana, DateTime? fin, DateTime ahora, IReadOnlyList<OfertaElegida> nombrables, int total)
        {
            string m = MomentosPushCampana.Normalizar(momento) ?? MomentosPushCampana.ARRANQUE;
            List<OfertaElegida> lista = (nombrables ?? new List<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            if (m == MomentosPushCampana.ARRANQUE)
            {
                return lista.Count == 0 ? TextosPushOfertas.CuerpoGenerico(tituloCampana) : TextosPushOfertas.CuerpoAutomatico(lista, total);
            }
            string c = string.IsNullOrWhiteSpace(tituloCampana) ? null : tituloCampana.Trim();
            bool cierre = m == MomentosPushCampana.CIERRE && fin.HasValue && (fin.Value.Date == ahora.Date || fin.Value.Date == ahora.Date.AddDays(1));
            int mas = Math.Max(lista.Count, total) - 1;
            string Resto() => mas > 0 ? $" y {mas} más" : string.Empty;

            if (lista.Count == 0)
            {
                if (cierre)
                {
                    return $"{Cuando(fin.Value, ahora)}: mira las ofertas que hemos elegido para ti.";
                }
                return c == null
                    ? "Te hemos elegido otras ofertas. ¡Échales un vistazo!"
                    : Encajar(x => $"Te hemos elegido otras ofertas de {x}. ¡Échales un vistazo!", c, MAXIMO_CUERPO);
            }

            CandidataOferta primera = lista[0].Candidata;
            string nombre = ReglaOfertasParaTi.FormatoTitulo(primera.Nombre ?? primera.Producto) ?? string.Empty;
            string porcentaje = TextosPushOfertas.Porcentaje(primera.Descuento);
            string conDescuento(string x) => x + (porcentaje.Length > 0 ? $" (−{porcentaje})" : string.Empty);

            if (cierre && lista[0].Motivo == MotivosOfertaParaTi.LO_MIRASTE)
            {
                string hasta = Hasta(fin.Value, ahora);
                return porcentaje.Length > 0
                    ? Encajar(x => $"{x} sigue al −{porcentaje} {hasta}.", nombre, MAXIMO_CUERPO)
                    : Encajar(x => $"{x} sigue con descuento {hasta}.", nombre, MAXIMO_CUERPO);
            }
            if (cierre)
            {
                string cuando = Cuando(fin.Value, ahora);
                return Encajar(x => $"{cuando}: {conDescuento(x)}{Resto()}.", nombre, MAXIMO_CUERPO);
            }
            return mas > 0
                ? Encajar(x => $"Te hemos elegido otras: {conDescuento(x)}{Resto()}.", nombre, MAXIMO_CUERPO)
                : Encajar(x => $"Te hemos elegido otra: {conDescuento(x)}.", nombre, MAXIMO_CUERPO);
        }

        /// <summary>Si la campaña acaba al final del día (23:59 o más tarde).</summary>
        public static bool FinDelDia(DateTime fin) => fin.TimeOfDay >= new TimeSpan(23, 59, 0);

        /// <summary>«Termina hoy a las 23:59» / «Termina mañana» (o «Termina mañana a las 14:00» si no acaba al final del día).</summary>
        public static string Cuando(DateTime fin, DateTime ahora)
        {
            string hora = fin.ToString("HH:mm");
            if (fin.Date == ahora.Date)
            {
                return $"Termina hoy a las {hora}";
            }
            return FinDelDia(fin) ? "Termina mañana" : $"Termina mañana a las {hora}";
        }

        /// <summary>«hasta esta noche» / «hasta las 14:00» (hoy); «hasta mañana» / «hasta mañana a las 14:00».</summary>
        public static string Hasta(DateTime fin, DateTime ahora)
        {
            string hora = fin.ToString("HH:mm");
            if (fin.Date == ahora.Date)
            {
                return FinDelDia(fin) ? "hasta esta noche" : $"hasta las {hora}";
            }
            return FinDelDia(fin) ? "hasta mañana" : $"hasta mañana a las {hora}";
        }

        /// <summary>
        /// El texto con <paramref name="hueco"/> dentro (un nombre), recortando el hueco con «…» (por una palabra entera si se puede)
        /// hasta que todo quepa en <paramref name="maximo"/>. El hueco nunca pasa de <see cref="MAXIMO_NOMBRE"/>.
        /// </summary>
        public static string Encajar(Func<string, string> plantilla, string hueco, int maximo)
        {
            string valor = (hueco ?? string.Empty).Trim();
            if (valor.Length > MAXIMO_NOMBRE)
            {
                valor = Recortar(valor, MAXIMO_NOMBRE);
            }
            string texto = plantilla(valor);
            if (texto.Length <= maximo)
            {
                return texto;
            }
            int sobra = texto.Length - maximo;
            int cabe = Math.Max(1, valor.Length - sobra);
            return plantilla(Recortar(valor, cabe));
        }

        /// <summary>«Crema hidratante intensiva…»: como mucho <paramref name="maximo"/> caracteres contando los puntos.</summary>
        public static string Recortar(string texto, int maximo)
        {
            string t = (texto ?? string.Empty).Trim();
            if (t.Length <= maximo)
            {
                return t;
            }
            int largo = Math.Max(0, maximo - PUNTOS.Length);
            string corto = t.Substring(0, largo);
            int espacio = corto.LastIndexOf(' ');
            if (espacio >= largo / 2)
            {
                corto = corto.Substring(0, espacio);
            }
            return corto.TrimEnd(' ', ',', '-', '.', '/', '(', ':') + PUNTOS;
        }
    }
}
