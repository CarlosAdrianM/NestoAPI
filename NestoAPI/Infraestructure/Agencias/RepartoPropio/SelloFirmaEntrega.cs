using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#770: lo que va en la imagen sellada, y NADA MÁS (Carlos 31/10/26).</summary>
    public class DatosSelloFirma
    {
        /// <summary>El texto de conformidad que vio quien firma.</summary>
        public string TextoConformidad { get; set; }
        /// <summary>La hora del aparato (FechaHora de la entrega).</summary>
        public DateTime FechaHora { get; set; }
        public string Receptor { get; set; }
        public string DniReceptor { get; set; }
        public decimal? Latitud { get; set; }
        public decimal? Longitud { get; set; }
        /// <summary>La dirección por geocodificación inversa; null si no se pudo (entonces solo las coordenadas).</summary>
        public string Direccion { get; set; }
    }

    /// <summary>
    /// NestoAPI#770: compone la firma sellada: la firma arriba y, debajo, el texto de conformidad, quién firma (nombre y DNI), la
    /// fecha y hora y la ubicación. Fondo blanco, <see cref="ANCHO"/> px de ancho, PNG. Lanza si la firma no es una imagen: quien
    /// lo llama guarda entonces la firma limpia.
    /// </summary>
    public static class SelloFirmaEntrega
    {
        public const int ANCHO = 700;
        internal const int MARGEN = 20;
        internal const int ALTO_MAXIMO_FIRMA = 260;
        internal const float AMPLIACION_MAXIMA = 2f;
        public const string SIN_UBICACION = "Sin ubicación";

        /// <summary>Las líneas de texto bajo la firma (sin la de conformidad), en orden.</summary>
        internal static List<string> LineasDatos(DatosSelloFirma datos)
        {
            var lineas = new List<string>();
            var quien = new List<string>();
            if (!string.IsNullOrWhiteSpace(datos.Receptor))
            {
                quien.Add("Nombre: " + datos.Receptor.Trim());
            }
            if (!string.IsNullOrWhiteSpace(datos.DniReceptor))
            {
                quien.Add("DNI: " + datos.DniReceptor.Trim());
            }
            if (quien.Count > 0)
            {
                lineas.Add(string.Join(" · ", quien));
            }
            lineas.Add("Fecha: " + datos.FechaHora.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            lineas.Add(TextoUbicacion(datos));
            return lineas;
        }

        internal static string TextoUbicacion(DatosSelloFirma datos)
        {
            if (!datos.Latitud.HasValue || !datos.Longitud.HasValue)
            {
                return SIN_UBICACION;
            }
            string coordenadas = datos.Latitud.Value.ToString("0.000000", CultureInfo.InvariantCulture) + ", "
                + datos.Longitud.Value.ToString("0.000000", CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(datos.Direccion)
                ? "Ubicación: " + coordenadas
                : $"Ubicación: {datos.Direccion.Trim()} ({coordenadas})";
        }

        public static byte[] Sellar(byte[] firmaPng, DatosSelloFirma datos)
        {
            if (firmaPng == null || firmaPng.Length == 0)
            {
                throw new ArgumentException("No hay firma que sellar.", nameof(firmaPng));
            }
            if (datos == null)
            {
                throw new ArgumentNullException(nameof(datos));
            }
            using (var entrada = new MemoryStream(firmaPng))
            using (Image firma = Image.FromStream(entrada))
            using (var fuenteConformidad = new Font("Arial", 15f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var fuenteDatos = new Font("Arial", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var formato = new StringFormat(StringFormat.GenericTypographic) { Trimming = StringTrimming.Word })
            {
                formato.FormatFlags &= ~StringFormatFlags.NoWrap;
                int anchoUtil = ANCHO - 2 * MARGEN;
                float escala = Math.Min(AMPLIACION_MAXIMA, Math.Min((float)anchoUtil / Math.Max(firma.Width, 1), (float)ALTO_MAXIMO_FIRMA / Math.Max(firma.Height, 1)));
                int anchoFirma = Math.Max(1, (int)Math.Round(firma.Width * escala));
                int altoFirma = Math.Max(1, (int)Math.Round(firma.Height * escala));

                string conformidad = string.IsNullOrWhiteSpace(datos.TextoConformidad) ? null : datos.TextoConformidad.Trim();
                List<string> lineas = LineasDatos(datos);

                // Medir antes de dibujar: el alto depende de cuántas líneas ocupe cada texto al envolver.
                float altoConformidad, altoDatos;
                var altos = new List<float>();
                using (var medida = new Bitmap(1, 1))
                using (Graphics g = Graphics.FromImage(medida))
                {
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    altoConformidad = conformidad == null ? 0f : (float)Math.Ceiling(g.MeasureString(conformidad, fuenteConformidad, anchoUtil, formato).Height);
                    foreach (string linea in lineas)
                    {
                        altos.Add((float)Math.Ceiling(g.MeasureString(linea, fuenteDatos, anchoUtil, formato).Height));
                    }
                }
                altoDatos = 0f;
                foreach (float alto in altos)
                {
                    altoDatos += alto + 4f;
                }

                int altoTotal = MARGEN + altoFirma + 12 + 1 + 12 + (conformidad == null ? 0 : (int)altoConformidad + 10) + (int)Math.Ceiling(altoDatos) + MARGEN;
                using (var lienzo = new Bitmap(ANCHO, altoTotal, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(lienzo))
                    using (var tinta = new SolidBrush(Color.FromArgb(20, 20, 20)))
                    using (var gris = new SolidBrush(Color.FromArgb(70, 70, 70)))
                    using (var linea = new Pen(Color.FromArgb(190, 190, 190), 1f))
                    {
                        g.Clear(Color.White);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                        // La firma, centrada (si es transparente, queda sobre el blanco).
                        int x = (ANCHO - anchoFirma) / 2;
                        g.DrawImage(firma, new Rectangle(x, MARGEN, anchoFirma, altoFirma));
                        float y = MARGEN + altoFirma + 12;
                        g.DrawLine(linea, MARGEN, y, ANCHO - MARGEN, y);
                        y += 13;

                        if (conformidad != null)
                        {
                            g.DrawString(conformidad, fuenteConformidad, tinta, new RectangleF(MARGEN, y, anchoUtil, altoConformidad + 2), formato);
                            y += altoConformidad + 10;
                        }
                        for (int i = 0; i < lineas.Count; i++)
                        {
                            g.DrawString(lineas[i], fuenteDatos, gris, new RectangleF(MARGEN, y, anchoUtil, altos[i] + 2), formato);
                            y += altos[i] + 4;
                        }
                    }
                    using (var salida = new MemoryStream())
                    {
                        lienzo.Save(salida, ImageFormat.Png);
                        return salida.ToArray();
                    }
                }
            }
        }
    }
}
