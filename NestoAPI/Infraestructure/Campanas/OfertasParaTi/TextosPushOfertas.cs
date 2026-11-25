using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>
    /// «Ofertas para ti»: los textos y los datos de la push de campaña (puros). Los ve el CLIENTE: de tú.
    /// </summary>
    public static class TextosPushOfertas
    {
        public const string TIPO_OFERTAS = "ofertas";
        /// <summary>El de la push de reposición (#662): la app ya publicada lo abre en el carrito con «productos».</summary>
        public const string TIPO_REPOSICION = RecordatorioReposicionPushJobsService.TIPO;
        public const int LONGITUD_NOMBRE = 26;
        public const string MARCA_PRODUCTO = "{producto}";
        public const string MARCA_DESCUENTO = "{descuento}";
        public const string MARCA_N = "{n}";
        public const string MARCA_CAMPANA = "{campana}";

        private static readonly CultureInfo ES = CultureInfo.GetCultureInfo("es-ES");

        /// <summary>El nombre para la push: en formato título y recortado a ~26 letras por una palabra entera.</summary>
        public static string NombreCorto(string nombre, int longitud = LONGITUD_NOMBRE)
        {
            string titulo = ReglaOfertasParaTi.FormatoTitulo(nombre) ?? string.Empty;
            if (titulo.Length <= longitud)
            {
                return titulo;
            }
            int corte = titulo.LastIndexOf(' ', longitud);
            string recortado = corte > 0 ? titulo.Substring(0, corte) : titulo.Substring(0, longitud);
            return recortado.TrimEnd(' ', ',', '-', '.', '/');
        }

        /// <summary>«30 %» (sin decimales, redondeo comercial). Vacío si no hay descuento.</summary>
        public static string Porcentaje(decimal descuento)
        {
            int entero = (int)Math.Round(descuento * 100, MidpointRounding.AwayFromZero);
            return entero <= 0 ? string.Empty : entero.ToString(ES) + " %";
        }

        /// <summary>
        /// El cuerpo automático: «Sicurezza BB Cream (−30 %), Jabón Isdruc y 3 más pensados para ti.». Uno: «Sicurezza BB Cream (−30 %),
        /// pensado para ti.»; dos: «Sicurezza BB Cream (−30 %) y Jabón Isdruc, pensados para ti.». Sin productos, null.
        /// </summary>
        public static string CuerpoAutomatico(IReadOnlyList<OfertaElegida> ofertas)
        {
            List<OfertaElegida> lista = (ofertas ?? new List<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            if (lista.Count == 0)
            {
                return null;
            }
            string porcentaje = Porcentaje(lista[0].Candidata.Descuento);
            string primero = NombreCorto(lista[0].Candidata.Nombre ?? lista[0].Candidata.Producto)
                + (porcentaje.Length > 0 ? $" (−{porcentaje})" : string.Empty);
            switch (lista.Count)
            {
                case 1:
                    return $"{primero}, pensado para ti.";
                case 2:
                    return $"{primero} y {NombreCorto(lista[1].Candidata.Nombre ?? lista[1].Candidata.Producto)}, pensados para ti.";
                default:
                    return $"{primero}, {NombreCorto(lista[1].Candidata.Nombre ?? lista[1].Candidata.Producto)} y {lista.Count - 2} más pensados para ti.";
            }
        }

        /// <summary>El texto por defecto de quien no tiene ofertas propias (con <c>IncluirSinOfertas</c>).</summary>
        public static string CuerpoSinOfertasPorDefecto(string titulo)
            => string.IsNullOrWhiteSpace(titulo)
                ? "Ya tienes las ofertas en la app. ¡Échales un vistazo!"
                : $"Ya tienes las ofertas de {titulo} en la app. ¡Échales un vistazo!";

        /// <summary>
        /// Una plantilla con marcadores (sin distinguir mayúsculas): {producto} = el primero (nombre corto), {descuento} = su descuento
        /// («30 %»), {n} = cuántos productos, {campana} = el título de la campaña (sin el año). Sin productos, {producto} y {descuento}
        /// quedan vacíos. Sin espacios de más.
        /// </summary>
        public static string AplicarPlantilla(string plantilla, IReadOnlyList<OfertaElegida> ofertas, string titulo)
        {
            if (plantilla == null)
            {
                return null;
            }
            List<OfertaElegida> lista = (ofertas ?? new List<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            OfertaElegida primera = lista.FirstOrDefault();
            string texto = Reemplazar(plantilla, MARCA_PRODUCTO, primera == null ? string.Empty : NombreCorto(primera.Candidata.Nombre ?? primera.Candidata.Producto));
            texto = Reemplazar(texto, MARCA_DESCUENTO, primera == null ? string.Empty : Porcentaje(primera.Candidata.Descuento));
            texto = Reemplazar(texto, MARCA_N, lista.Count.ToString(ES));
            texto = Reemplazar(texto, MARCA_CAMPANA, titulo ?? string.Empty);
            texto = Regex.Replace(texto, @"[ \t]{2,}", " ");
            texto = Regex.Replace(texto, @" +([,.;:!?)])", "$1");
            return texto.Trim();
        }

        private static string Reemplazar(string texto, string marca, string valor)
            => Regex.Replace(texto, Regex.Escape(marca), (valor ?? string.Empty).Replace("$", "$$"), RegexOptions.IgnoreCase);

        /// <summary>«ref:cant;ref:cant» como la push de reposición (cantidad mínima 1).</summary>
        public static string ListaProductos(IEnumerable<OfertaElegida> ofertas)
            => string.Join(";", (ofertas ?? Enumerable.Empty<OfertaElegida>()).Where(o => o?.Candidata != null)
                .Select(o => $"{o.Candidata.Producto.Trim()}:{Math.Max(1, o.CantidadSugerida)}"));

        /// <summary>
        /// La push. Modo Ofertas: tipo «ofertas» (+ productos si hay). Modo Carrito: tipo «reposicion» con productos (el servicio ya
        /// solo le pasa habituales). Siempre «campana»; «imagenUrl» si hay foto.
        /// </summary>
        public static NotificacionPushDTO Notificacion(string modo, string campana, string titulo, string cuerpo, IReadOnlyList<OfertaElegida> ofertas,
            string imagenUrl)
        {
            List<OfertaElegida> lista = (ofertas ?? new List<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            bool carrito = string.Equals(modo, ModosPushCampana.CARRITO, StringComparison.OrdinalIgnoreCase) && lista.Count > 0;
            string tipo = carrito ? TIPO_REPOSICION : TIPO_OFERTAS;
            var datos = new Dictionary<string, string>
            {
                { "tipo", tipo },
                { "campana", campana?.Trim() }
            };
            if (lista.Count > 0)
            {
                datos["productos"] = ListaProductos(lista);
            }
            string foto = PlantillaRecordatorioReposicion.NormalizarUrlFoto(imagenUrl);
            if (foto != null)
            {
                datos["imagenUrl"] = foto;
            }
            return new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = cuerpo,
                Tipo = tipo,
                Datos = datos
            };
        }
    }
}
