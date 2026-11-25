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
        public static string CuerpoAutomatico(IReadOnlyList<OfertaElegida> ofertas) => CuerpoAutomatico(ofertas, null);

        /// <summary>
        /// Igual, pero con <paramref name="total"/> ofertas en la pantalla de las que solo se nombran las de <paramref name="nombrables"/>
        /// (las suyas): «Sicurezza BB Cream (−30 %) y 4 más pensados para ti.». Sin nombrables, null.
        /// </summary>
        public static string CuerpoAutomatico(IReadOnlyList<OfertaElegida> nombrables, int? total)
        {
            List<OfertaElegida> lista = (nombrables ?? new List<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            if (lista.Count == 0)
            {
                return null;
            }
            int cuantas = Math.Max(lista.Count, total ?? lista.Count);
            string porcentaje = Porcentaje(lista[0].Candidata.Descuento);
            string primero = NombreCorto(lista[0].Candidata.Nombre ?? lista[0].Candidata.Producto)
                + (porcentaje.Length > 0 ? $" (−{porcentaje})" : string.Empty);
            if (cuantas == 1)
            {
                return $"{primero}, pensado para ti.";
            }
            if (lista.Count == 1)
            {
                return $"{primero} y {cuantas - 1} más pensados para ti.";
            }
            string segundo = NombreCorto(lista[1].Candidata.Nombre ?? lista[1].Candidata.Producto);
            return cuantas == 2
                ? $"{primero} y {segundo}, pensados para ti."
                : $"{primero}, {segundo} y {cuantas - 2} más pensados para ti.";
        }

        /// <summary>
        /// Cuando todas sus ofertas son del respaldo general (ninguna es de lo suyo), no se nombra ningún producto: «Mira las ofertas del
        /// Black Friday que hemos elegido para ti.».
        /// </summary>
        public static string CuerpoGenerico(string titulo)
            => string.IsNullOrWhiteSpace(titulo)
                ? "Mira las ofertas que hemos elegido para ti."
                : $"Mira las ofertas del {titulo} que hemos elegido para ti.";

        /// <summary>Si la plantilla nombra un producto ({producto} o {descuento}).</summary>
        public static bool NombraProducto(string plantilla)
            => plantilla != null && (plantilla.IndexOf(MARCA_PRODUCTO, StringComparison.OrdinalIgnoreCase) >= 0
                || plantilla.IndexOf(MARCA_DESCUENTO, StringComparison.OrdinalIgnoreCase) >= 0);

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
            => AplicarPlantilla(plantilla, ofertas, titulo, null);

        /// <summary>Igual, con {n} = <paramref name="total"/> (las que lleva la pantalla) aunque solo se nombren las de <paramref name="ofertas"/>.</summary>
        public static string AplicarPlantilla(string plantilla, IReadOnlyList<OfertaElegida> ofertas, string titulo, int? total)
        {
            if (plantilla == null)
            {
                return null;
            }
            List<OfertaElegida> lista = (ofertas ?? new List<OfertaElegida>()).Where(o => o?.Candidata != null).ToList();
            OfertaElegida primera = lista.FirstOrDefault();
            string texto = Reemplazar(plantilla, MARCA_PRODUCTO, primera == null ? string.Empty : NombreCorto(primera.Candidata.Nombre ?? primera.Candidata.Producto));
            texto = Reemplazar(texto, MARCA_DESCUENTO, primera == null ? string.Empty : Porcentaje(primera.Candidata.Descuento));
            texto = Reemplazar(texto, MARCA_N, Math.Max(lista.Count, total ?? 0).ToString(ES));
            texto = Reemplazar(texto, MARCA_CAMPANA, titulo ?? string.Empty);
            texto = Regex.Replace(texto, @"[ \t]{2,}", " ");
            texto = Regex.Replace(texto, @" +([,.;:!?)])", "$1");
            return texto.Trim();
        }

        private static string Reemplazar(string texto, string marca, string valor)
            => Regex.Replace(texto, Regex.Escape(marca), (valor ?? string.Empty).Replace("$", "$$"), RegexOptions.IgnoreCase);

        /// <summary>
        /// La push: SIEMPRE tipo «ofertas» (la app abre la pantalla de ofertas), «campana» e «imagenUrl» si hay foto. NUNCA «productos»
        /// (Carlos 25/11/26: los push de campaña no tocan el carrito del cliente; las ofertas las pide la app a GET api/Pedidos/Cliente/Ofertas).
        /// </summary>
        public static NotificacionPushDTO Notificacion(string campana, string titulo, string cuerpo, string imagenUrl)
        {
            string tipo = TIPO_OFERTAS;
            var datos = new Dictionary<string, string>
            {
                { "tipo", tipo },
                { "campana", campana?.Trim() }
            };
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
