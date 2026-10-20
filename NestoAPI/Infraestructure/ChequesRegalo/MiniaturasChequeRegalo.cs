using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>La miniatura del cheque de una campaña, ya en JPEG, con su ETag (entre comillas, como va en la cabecera).</summary>
    public class MiniaturaChequeRegalo
    {
        public byte[] Jpeg { get; set; }
        public string ETag { get; set; }
        public int Ancho { get; set; }
        public int Alto { get; set; }
    }

    /// <summary>
    /// NestoAPI#660 / #593 (Carlos 20/10/26): la foto de la línea del cheque regalo en el correo «Gracias por su pedido».
    /// El producto ficticio de la campaña (CHEQUE50_OCT26) no está en la tienda, así que la foto la sirve la API
    /// (GET api/ChequesRegalo/{campana}/Miniatura): la imagen del correo del cheque (ChequesRegaloCampanas.ImagenCorreo,
    /// el recurso incrustado que lee <see cref="ImagenesChequeRegalo"/>) reducida a <see cref="ANCHO"/> px de ancho (el
    /// doble de lo que ocupa en el correo, para las pantallas de alta densidad) y en JPEG de calidad
    /// <see cref="CALIDAD_JPEG"/>. Se genera una vez por imagen y se queda en memoria.
    /// </summary>
    public static class MiniaturasChequeRegalo
    {
        internal const int ANCHO = 128;
        internal const long CALIDAD_JPEG = 80;
        /// <summary>Lo que pueden guardarla los navegadores y los proxys de imágenes de Gmail y Outlook.</summary>
        internal static readonly TimeSpan VIGENCIA_CACHE = TimeSpan.FromDays(7);
        /// <summary>appSetting con la URL pública de la API (la de los enlaces que salen en los correos).</summary>
        internal const string CLAVE_URL_PUBLICA = "UrlPublicaApi";
        internal const string URL_PUBLICA_POR_DEFECTO = "https://api.nuevavision.es";

        private static readonly ConcurrentDictionary<string, Lazy<MiniaturaChequeRegalo>> cache =
            new ConcurrentDictionary<string, Lazy<MiniaturaChequeRegalo>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// La miniatura de la imagen <paramref name="nombre"/> (Resources\ChequesRegalo), o null si no hay nombre o no
        /// existe el recurso. <paramref name="leerImagen"/> existe para los tests; por defecto, <see cref="ImagenesChequeRegalo.Leer"/>.
        /// </summary>
        public static MiniaturaChequeRegalo DeImagen(string nombre, Func<string, byte[]> leerImagen = null)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                return null;
            }
            Func<string, byte[]> leer = leerImagen ?? ImagenesChequeRegalo.Leer;
            Lazy<MiniaturaChequeRegalo> miniatura = cache.GetOrAdd(nombre.Trim(),
                n => new Lazy<MiniaturaChequeRegalo>(() => Generar(leer(n))));
            return miniatura.Value;
        }

        /// <summary>¿Tiene la campaña una imagen de verdad (nombre y recurso)? Sin ella no hay miniatura (404).</summary>
        internal static bool TieneImagen(string nombre, Func<string, byte[]> leerImagen = null)
            => !string.IsNullOrWhiteSpace(nombre) && (leerImagen ?? ImagenesChequeRegalo.Leer)(nombre.Trim()) != null;

        /// <summary>Reduce la imagen a <see cref="ANCHO"/> px de ancho (el alto, en proporción) con interpolación de alta calidad.</summary>
        internal static MiniaturaChequeRegalo Generar(byte[] original)
        {
            if (original == null || original.Length == 0)
            {
                return null;
            }
            using (var entrada = new MemoryStream(original))
            using (Image imagen = Image.FromStream(entrada))
            {
                int alto = Math.Max(1, (int)Math.Round(imagen.Height * (double)ANCHO / imagen.Width, MidpointRounding.AwayFromZero));
                using (var miniatura = new Bitmap(ANCHO, alto, PixelFormat.Format24bppRgb))
                {
                    miniatura.SetResolution(imagen.HorizontalResolution, imagen.VerticalResolution);
                    using (Graphics g = Graphics.FromImage(miniatura))
                    using (var atributos = new ImageAttributes())
                    {
                        g.CompositingMode = CompositingMode.SourceCopy;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        // Sin esto, los bordes salen con un halo (la interpolación lee píxeles de fuera de la imagen)
                        atributos.SetWrapMode(WrapMode.TileFlipXY);
                        g.DrawImage(imagen, new Rectangle(0, 0, ANCHO, alto), 0, 0, imagen.Width, imagen.Height, GraphicsUnit.Pixel, atributos);
                    }
                    ImageCodecInfo codificador = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using (var parametros = new EncoderParameters(1))
                    using (var salida = new MemoryStream())
                    {
                        parametros.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, CALIDAD_JPEG);
                        miniatura.Save(salida, codificador, parametros);
                        byte[] jpeg = salida.ToArray();
                        return new MiniaturaChequeRegalo { Jpeg = jpeg, ETag = CalcularETag(jpeg), Ancho = ANCHO, Alto = alto };
                    }
                }
            }
        }

        private static string CalcularETag(byte[] contenido)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return "\"" + BitConverter.ToString(sha.ComputeHash(contenido), 0, 12).Replace("-", string.Empty).ToLowerInvariant() + "\"";
            }
        }

        /// <summary>appSetting UrlPublicaApi (sin la barra final); por defecto https://api.nuevavision.es.</summary>
        internal static string UrlPublicaApi
        {
            get
            {
                string url = ConfigurationManager.AppSettings[CLAVE_URL_PUBLICA];
                return string.IsNullOrWhiteSpace(url) ? URL_PUBLICA_POR_DEFECTO : url.Trim().TrimEnd('/');
            }
        }

        /// <summary>La URL absoluta de la miniatura de la campaña: https://api.nuevavision.es/api/ChequesRegalo/CHEQUE50_OCT_2026/Miniatura.</summary>
        public static string Url(string campana, string urlBase = null)
            => $"{(string.IsNullOrWhiteSpace(urlBase) ? UrlPublicaApi : urlBase.Trim().TrimEnd('/'))}/api/ChequesRegalo/{Uri.EscapeDataString(campana.Trim())}/Miniatura";
    }

    /// <summary>Una campaña con imagen, para relacionar la línea del cheque (su producto ficticio) con su miniatura.</summary>
    public class CampanaMiniaturaChequeRegalo
    {
        public string Codigo { get; set; }
        /// <summary>El producto ficticio de la campaña (CHEQUE50_OCT26): el de la línea del cheque en el pedido.</summary>
        public string Producto { get; set; }
        public string ImagenCorreo { get; set; }
    }

    public interface IRepositorioMiniaturasChequeRegalo
    {
        /// <summary>Las campañas (activas o no, como en el canje) que tienen imagen de correo. Vacía si aún no existen las tablas o la columna.</summary>
        List<CampanaMiniaturaChequeRegalo> LeerCampanasConImagen();
    }

    /// <summary>NestoAPI#660 / #593: por SQL directo (sin EDMX), como el resto de los cheques. Síncrono: lo pide el job del correo.</summary>
    public class RepositorioMiniaturasChequeRegalo : IRepositorioMiniaturasChequeRegalo
    {
        private readonly NVEntities db;

        public RepositorioMiniaturasChequeRegalo(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        internal const string SQL_CAMPANAS_CON_IMAGEN = @"
IF OBJECT_ID('dbo.ChequesRegaloCampanas') IS NULL OR COL_LENGTH('dbo.ChequesRegaloCampanas', 'ImagenCorreo') IS NULL
    SELECT CAST(NULL AS varchar(20)) AS Codigo, CAST(NULL AS varchar(15)) AS Producto, CAST(NULL AS varchar(100)) AS ImagenCorreo
    WHERE 1 = 0
ELSE
    EXEC sp_executesql N'SELECT RTRIM(Codigo) AS Codigo, RTRIM(Producto) AS Producto, RTRIM(ImagenCorreo) AS ImagenCorreo
    FROM dbo.ChequesRegaloCampanas
    WHERE Producto IS NOT NULL AND ImagenCorreo IS NOT NULL AND RTRIM(ImagenCorreo) <> ''''
    ORDER BY Codigo'";

        public List<CampanaMiniaturaChequeRegalo> LeerCampanasConImagen()
            => db.Database.SqlQuery<CampanaMiniaturaChequeRegalo>(SQL_CAMPANAS_CON_IMAGEN).ToList();
    }
}
