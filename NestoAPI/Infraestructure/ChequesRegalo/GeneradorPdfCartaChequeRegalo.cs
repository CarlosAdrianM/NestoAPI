using NestoAPI.Models.Facturas;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.ChequesRegalo
{
    /// <summary>NestoAPI#838: lo que lleva la carta en papel, sacado de la plantilla del correo (los mismos textos).</summary>
    public class ContenidoCartaChequeRegalo
    {
        /// <summary>Saludo y párrafos, los del correo (<see cref="PlantillaCorreoChequeRegalo.Parrafos"/>).</summary>
        public List<string> Parrafos { get; set; }
        /// <summary>«Canjéelo en la tienda online: www…» (en papel no hay botón).</summary>
        public string Tienda { get; set; }
        public string LetraPequena { get; set; }
        public string Firma { get; set; }
        /// <summary>La imagen del cheque de la campaña; null = carta sin imagen.</summary>
        public byte[] Imagen { get; set; }
    }

    public interface IGeneradorCartaChequeRegalo
    {
        /// <summary>El PDF (A4) de la carta del cheque. Lanza si no se puede generar (quien lo llama decide qué hacer).</summary>
        byte[] Generar(CampanaCorreoChequeRegalo campana, string nombreDePila);
    }

    /// <summary>
    /// NestoAPI#838 (Carlos 06/11/26: «al sacar el packing se debe imprimir en papel la carta donde se informa del cheque, como
    /// el correo pero en papel»): la carta A4 del cheque regalo con QuestPDF, como los demás PDF del servidor. El contenido es
    /// el del correo (<see cref="PlantillaCorreoChequeRegalo"/>): la imagen del cheque de la campaña arriba, el saludo con el
    /// nombre de pila, los párrafos (importe, «hasta el …», cómo se usa), la tienda, la letra pequeña y la firma.
    /// </summary>
    public class GeneradorPdfCartaChequeRegalo : IGeneradorCartaChequeRegalo
    {
        private const float TAMANO_NORMAL = 11f;
        private const float TAMANO_LETRA_PEQUENA = 8.5f;
        /// <summary>
        /// El PDF viaja en la respuesta de la facturación: con Arial (como el correo) pesaba 1,1 MB porque la fuente va casi
        /// entera dentro; con Verdana, unos 370 KB con la imagen (el JPEG va tal cual).
        /// </summary>
        internal const string FUENTE = "Verdana";

        private readonly Func<string, byte[]> leerImagen;

        public GeneradorPdfCartaChequeRegalo() : this(null) { }

        /// <param name="leerImagen">Nombre de la imagen de la campaña → bytes (por defecto, el recurso incrustado).</param>
        internal GeneradorPdfCartaChequeRegalo(Func<string, byte[]> leerImagen)
        {
            this.leerImagen = leerImagen ?? ImagenesChequeRegalo.Leer;
        }

        /// <summary>Los textos de la carta: los mismos del correo, de la misma plantilla.</summary>
        internal static ContenidoCartaChequeRegalo Contenido(CampanaCorreoChequeRegalo campana, string nombreDePila, byte[] imagen)
            => new ContenidoCartaChequeRegalo
            {
                Parrafos = PlantillaCorreoChequeRegalo.Parrafos(campana, nombreDePila),
                Tienda = PlantillaCorreoChequeRegalo.TiendaEnPapel(),
                LetraPequena = PlantillaCorreoChequeRegalo.LETRA_PEQUENA,
                Firma = PlantillaCorreoChequeRegalo.FIRMA,
                Imagen = imagen != null && imagen.Length > 0 ? imagen : null
            };

        public byte[] Generar(CampanaCorreoChequeRegalo campana, string nombreDePila)
        {
            if (campana == null)
            {
                throw new ArgumentNullException(nameof(campana));
            }
            byte[] imagen = null;
            try
            {
                imagen = leerImagen(campana.ImagenCorreo);
            }
            catch
            {
                // Sin imagen la carta sigue diciendo lo mismo
            }
            return Generar(Contenido(campana, nombreDePila, imagen));
        }

        internal static byte[] Generar(ContenidoCartaChequeRegalo contenido)
        {
            Document documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.DefaultTextStyle(x => x.FontSize(TAMANO_NORMAL).FontFamily(FUENTE));

                    page.Content().Column(columna =>
                    {
                        columna.Spacing(12);
                        if (contenido.Imagen != null)
                        {
                            columna.Item().PaddingBottom(8).Image(contenido.Imagen, ImageScaling.FitWidth);
                        }
                        foreach (string parrafo in contenido.Parrafos ?? Enumerable.Empty<string>())
                        {
                            columna.Item().Text(parrafo).LineHeight(1.4f);
                        }
                        columna.Item().Text(contenido.Tienda).Bold();
                        columna.Item().PaddingTop(8).Text(texto =>
                        {
                            texto.DefaultTextStyle(x => x.FontSize(TAMANO_LETRA_PEQUENA).FontColor(Colors.Grey.Darken2));
                            texto.Span("Letra pequeña: ").Bold();
                            texto.Span(contenido.LetraPequena);
                        });
                        columna.Item().PaddingTop(8).Text(contenido.Firma);
                    });
                });
            }).WithMetadata(new DocumentMetadata
            {
                Title = "Carta cheque regalo",
                Author = "Nueva Visión"
            });
            return documento.GeneratePdf();
        }
    }

    /// <summary>
    /// NestoAPI#838: las cartas de los cheques que acaba de generar una factura, para imprimirlas con sus documentos
    /// (<see cref="Facturas.ImpresionDocumentosServidor"/>). Nunca lanza: si una carta no se puede generar, ELMAH y sin carta.
    /// </summary>
    public static class CartasChequeRegalo
    {
        internal const int COPIAS = 1;

        public static string Nombre(string numeroFactura) => $"Carta cheque regalo {numeroFactura?.Trim()}";

        public static string Descripcion(string numeroFactura) => $"la carta del cheque regalo de la factura {numeroFactura?.Trim()}";

        /// <summary>
        /// Una carta por cada cheque de <paramref name="avisos"/> que haya generado ESTA factura (los avisos son de todos los
        /// cheques del cliente sin avisar: uno viejo sin correo no vuelve a sacar carta en cada factura).
        /// </summary>
        public static List<CartaChequeRegaloDTO> Crear(IEnumerable<AvisoChequeRegalo> avisos, CrearFacturaResponseDTO factura, string cliente,
            IGeneradorCartaChequeRegalo generador, Action<Exception> registrarError)
        {
            var cartas = new List<CartaChequeRegaloDTO>();
            if (avisos == null || factura == null || generador == null || string.IsNullOrWhiteSpace(factura.NumeroFactura))
            {
                return cartas;
            }
            string numeroFactura = factura.NumeroFactura.Trim();
            foreach (AvisoChequeRegalo aviso in avisos.Where(a => a?.Campana != null &&
                string.Equals(a.FacturaOrigen?.Trim(), numeroFactura, StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    cartas.Add(new CartaChequeRegaloDTO
                    {
                        Empresa = factura.Empresa?.Trim(),
                        NumeroPedido = factura.NumeroPedido,
                        Cliente = cliente?.Trim(),
                        NumeroFactura = numeroFactura,
                        Nombre = Nombre(numeroFactura),
                        Descripcion = Descripcion(numeroFactura),
                        DatosImpresion = new DocumentoParaImprimir
                        {
                            BytesPDF = generador.Generar(aviso.Campana, aviso.NombreDePila),
                            NumeroCopias = COPIAS,
                            TipoBandeja = TipoBandejaImpresion.AutomaticFeed
                        }
                    });
                }
                catch (Exception ex)
                {
                    try
                    {
                        registrarError?.Invoke(new Exception($"[Cheques regalo #838] No se ha podido generar la carta del cheque de la " +
                            $"factura {numeroFactura} (cliente {cliente?.Trim()}): {ex.Message}", ex));
                    }
                    catch
                    {
                        // El registro nunca rompe la facturación
                    }
                }
            }
            return cartas;
        }
    }
}
