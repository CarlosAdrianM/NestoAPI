using NestoAPI.Models.Informes;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Nesto#340 (Fase 2, RDLC→QuestPDF): etiquetas de precio para la tienda, sustituyendo el
    /// EtiquetasTienda.rdlc que Nesto renderizaba en local (ProductoViewModel). Es papel FÍSICO
    /// de etiquetas adhesivas precortadas (A4, 3 columnas x 6 filas), así que las dimensiones
    /// están CALCADAS del RDLC: página A4 con márgenes 1,4/1,3/0,9/5,5 cm, fila de 3,82075 cm
    /// y anchos de columna 5,97178 / 6,09525 / 5,7415 cm. Cada etiqueta: QR de la tienda online
    /// (1,9 cm) arriba-izquierda, nombre+tamaño y familia a la derecha, referencia codificada
    /// abajo-izquierda y PVP público en negrita 14pt abajo-derecha. Al cliente se le sirve con
    /// flag por usuario (MotorPdfEtiquetasTienda) y fallback al RDLC hasta validar contra el
    /// papel real (lección Picking/Packing: lo que sale del almacén, siempre con flag).
    /// </summary>
    public class GeneradorPdfEtiquetasTienda
    {
        // Dimensiones del RDLC (cm). El papel es precortado: no tocar sin compararlo impreso.
        private const float ANCHO_COLUMNA_1 = 5.97178f;
        private const float ANCHO_COLUMNA_2 = 6.09525f;
        private const float ANCHO_COLUMNA_3 = 5.7415f;
        private const float ALTO_FILA = 3.82075f;
        private const float LADO_QR = 1.9f;

        // Resolutor de la URL pública del producto en la tienda online (chapuza heredada del
        // cliente: PHP custom que traduce referencia → URL). Best-effort e inyectable en tests.
        private static readonly HttpClient _clienteHttp = new HttpClient
        {
            BaseAddress = new Uri("http://www.productosdeesteticaypeluqueriaprofesional.com/enlacePorReferencia.php"),
            Timeout = TimeSpan.FromSeconds(10)
        };
        private readonly Func<string, Task<string>> _resolverUrlProducto;

        public GeneradorPdfEtiquetasTienda(Func<string, Task<string>> resolverUrlProducto = null)
        {
            _resolverUrlProducto = resolverUrlProducto ?? ResolverUrlProductoTiendaOnline;
        }

        /// <summary>Una posición de la hoja de etiquetas, ya compuesta. Null = hueco (posición
        /// ya gastada de la hoja, controlado por etiquetaPrimera).</summary>
        internal class EtiquetaCompuesta
        {
            public string ProductoId { get; set; }
            public string NombreConTamanno { get; set; }
            public string Familia { get; set; }
            public string Referencia { get; set; }
            public decimal PrecioPublico { get; set; }
            public string Url { get; set; }
        }

        public async Task<ByteArrayContent> GenerarPdf(List<EtiquetasTiendaDTO> etiquetas, int etiquetaPrimera)
        {
            List<EtiquetaCompuesta> posiciones = Componer(etiquetas, etiquetaPrimera);
            foreach (EtiquetaCompuesta etiqueta in posiciones.Where(e => e != null))
            {
                etiqueta.Url = await _resolverUrlProducto(etiqueta.ProductoId).ConfigureAwait(false);
            }

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginLeft(1.4f, Unit.Centimetre);
                    page.MarginRight(1.3f, Unit.Centimetre);
                    page.MarginTop(0.9f, Unit.Centimetre);
                    page.MarginBottom(5.5f, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Content().Column(column =>
                    {
                        foreach (var fila in EnFilasDeTres(posiciones))
                        {
                            column.Item().Height(ALTO_FILA, Unit.Centimetre).Row(row =>
                            {
                                row.ConstantItem(ANCHO_COLUMNA_1, Unit.Centimetre).Element(c => ComponerEtiqueta(c, fila[0]));
                                row.ConstantItem(ANCHO_COLUMNA_2, Unit.Centimetre).Element(c => ComponerEtiqueta(c, fila[1]));
                                row.ConstantItem(ANCHO_COLUMNA_3, Unit.Centimetre).Element(c => ComponerEtiqueta(c, fila[2]));
                            });
                        }
                    });
                });
            });

            return new ByteArrayContent(documento.GeneratePdf());
        }

        private static void ComponerEtiqueta(IContainer container, EtiquetaCompuesta etiqueta)
        {
            if (etiqueta == null)
            {
                return; // hueco: posición ya gastada de la hoja
            }

            container.PaddingRight(0.2f, Unit.Centimetre).Column(column =>
            {
                // Zona superior: QR a la izquierda (solo si hay precio, como el RDLC) y
                // nombre+familia a la derecha.
                column.Item().Height(2.05f, Unit.Centimetre).Row(row =>
                {
                    row.ConstantItem(LADO_QR, Unit.Centimetre).Element(c =>
                    {
                        byte[] qr = etiqueta.PrecioPublico != 0 ? GenerarQrPng(etiqueta.Url) : null;
                        if (qr != null)
                        {
                            c.Height(LADO_QR, Unit.Centimetre).Image(qr, ImageScaling.FitArea);
                        }
                    });
                    row.RelativeItem().PaddingLeft(2).Column(textos =>
                    {
                        textos.Item().Height(1.36f, Unit.Centimetre).Text(etiqueta.NombreConTamanno ?? string.Empty);
                        textos.Item().AlignBottom().Text(etiqueta.Familia ?? string.Empty).FontSize(9);
                    });
                });
                // Zona inferior: referencia codificada a la izquierda y PVP en grande a la derecha.
                column.Item().Row(row =>
                {
                    row.ConstantItem(2.3f, Unit.Centimetre).AlignBottom()
                        .Text(etiqueta.Referencia ?? string.Empty).FontSize(9);
                    row.RelativeItem().AlignBottom().AlignCenter()
                        .Text(etiqueta.PrecioPublico != 0 ? $"{etiqueta.PrecioPublico:0.00}€" : string.Empty)
                        .Bold().FontSize(14);
                });
            });
        }

        /// <summary>
        /// Composición de la hoja: huecos delante según etiquetaPrimera (para aprovechar hojas
        /// empezadas) y una etiqueta por producto, en el orden pedido. Pura y testeable —
        /// misma lógica que el FilaEtiquetasModel.ComponerAsync del cliente que sustituye.
        /// </summary>
        internal static List<EtiquetaCompuesta> Componer(List<EtiquetasTiendaDTO> etiquetas, int etiquetaPrimera)
        {
            var posiciones = new List<EtiquetaCompuesta>();
            int huecos = etiquetaPrimera > 0 ? etiquetaPrimera - 1 : 0;
            for (int i = 0; i < huecos; i++)
            {
                posiciones.Add(null);
            }
            foreach (EtiquetasTiendaDTO dto in etiquetas ?? new List<EtiquetasTiendaDTO>())
            {
                posiciones.Add(new EtiquetaCompuesta
                {
                    ProductoId = dto.ProductoId,
                    NombreConTamanno = dto.Tamanno == 0
                        ? dto.Nombre
                        : $"{dto.Nombre} {dto.Tamanno} {dto.UnidadMedida}",
                    Familia = dto.Familia,
                    Referencia = ComponerReferencia(dto.ProductoId, dto.PrecioProfesional),
                    PrecioPublico = CalcularPrecioPublico(dto.PrecioProfesional)
                });
            }
            return posiciones;
        }

        private static IEnumerable<EtiquetaCompuesta[]> EnFilasDeTres(List<EtiquetaCompuesta> posiciones)
        {
            for (int i = 0; i < posiciones.Count; i += 3)
            {
                yield return new[]
                {
                    posiciones[i],
                    i + 1 < posiciones.Count ? posiciones[i + 1] : null,
                    i + 2 < posiciones.Count ? posiciones[i + 2] : null
                };
            }
        }

        /// <summary>PVP público de tienda a partir del precio profesional: x2 de margen, -35%
        /// de descuento de tienda y +21% de IVA (fórmula heredada del RDLC del cliente).</summary>
        internal static decimal CalcularPrecioPublico(decimal precioProfesional)
        {
            return Math.Round(precioProfesional * 2 * .65M * 1.21M, 2, MidpointRounding.AwayFromZero);
        }

        /// <summary>Referencia codificada de la etiqueta: id del producto + precio profesional
        /// en 7 dígitos (5 enteros + 2 decimales, sin separador). La lee la tienda para saber
        /// el precio profesional sin exponerlo en claro.</summary>
        internal static string ComponerReferencia(string productoId, decimal precioProfesional)
        {
            string cadena = Math.Round(precioProfesional, 2).ToString("0.00")
                .Replace(".", string.Empty).Replace(",", string.Empty);
            while (cadena.Length < 7)
            {
                cadena = "0" + cadena;
            }
            string parteDecimal = cadena.Substring(cadena.Length - 2);
            string parteEntera = cadena.Substring(0, 5);
            return $"{productoId}{parteEntera}{parteDecimal}";
        }

        private static byte[] GenerarQrPng(string contenido)
        {
            if (string.IsNullOrEmpty(contenido))
            {
                return null;
            }
            var generador = new QRCodeGenerator();
            QRCodeData datos = generador.CreateQrCode(contenido, QRCodeGenerator.ECCLevel.Q);
            using (var png = new PngByteQRCode(datos))
            {
                return png.GetGraphic(20);
            }
        }

        private static async Task<string> ResolverUrlProductoTiendaOnline(string producto)
        {
            try
            {
                HttpResponseMessage respuesta = await _clienteHttp
                    .GetAsync("?producto=" + Uri.EscapeDataString(producto ?? string.Empty))
                    .ConfigureAwait(false);
                if (!respuesta.IsSuccessStatusCode)
                {
                    return null;
                }
                string ruta = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(ruta)
                    ? null
                    : ruta + "?utm_source=nuevavision&utm_campaign=tienda_alcobendas";
            }
            catch
            {
                // Sin URL no hay QR, pero la etiqueta se imprime igual (best-effort, como el cliente).
                return null;
            }
        }
    }
}
