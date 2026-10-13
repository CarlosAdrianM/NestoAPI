using NestoAPI.Models.Clientes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Nesto#490 / Nesto#340 (RDLC → QuestPDF): orden de domiciliación de adeudo directo SEPA
    /// (mandato CORE), sustituto de Models\Clientes\MandatoCoreSEPA.rdlc. Es un papel que va al
    /// CLIENTE para que lo firme: va detrás del parámetro por usuario MotorPdfMandatoSepa (Nesto
    /// añade «motor=QuestPDF» a la URL); sin él sigue saliendo el RDLC.
    ///
    /// Replica el RDLC: A4 con márgenes de 1 cm; logo arriba a la izquierda con la referencia y
    /// el identificador del acreedor a la derecha; título centrado (12 pt negrita + subtítulo en
    /// inglés 8 pt); recuadro de 2 pt con los datos del acreedor; texto legal en castellano
    /// (8 pt) y en inglés (6 pt cursiva); recuadro de 2 pt con los datos del deudor, el tipo de
    /// pago, fecha-localidad, firmante y hueco para la firma; y el aviso final centrado. Las
    /// etiquetas van en negrita cursiva con la traducción inglesa en 6 pt. El RDLC solo pintaba
    /// el primer mandato (First); aquí cada mandato es una página (la API manda siempre uno).
    /// </summary>
    public class GeneradorPdfMandatoSepa
    {
        internal const string URL_LOGO = "http://www.productosdeesteticaypeluqueriaprofesional.com/logofra.jpg";

        internal const string TITULO = "Orden de domiciliación de adeudo directo SEPA";
        internal const string SUBTITULO = "SEPA Direct Debit Mandate";
        internal const string TIPO_PAGO = "Pago Recurrente / ";
        internal const string TIPO_PAGO_EN = "Recurrent payment";

        internal const string TEXTO_LEGAL_ES = "Mediante la firma de esta orden de domiciliación, el deudor autoriza (A) al acreedor a enviar instrucciones a la entidad del deudor para adeudar en su cuenta y (B) a la entidad para efectuar los adeudos en su cuenta siguiendo las instrucciones del acreedor. Como parte de sus derechos, el deudor está legitimado al reembolso por su entidad en los términos y condiciones del contrato suscrito con la misma. La solicitud de reembolso deberá efectuarse dentro de las ocho semanas que siguen a la fecha de adeudo en cuenta. Puede obtener información adicional sobre sus derechos en su entidad financiera.";
        internal const string TEXTO_LEGAL_EN = "By signing this mandate form, you authorise (A) the Creditor to send instructions to your bank to debit your account and (B) your bank to debit your account in accordance with the instructions from the Creditor. As part of your rights, you are entitled to a refund from your bank under the terms and conditions of your agreement with your bank. A refund must be claimed within eight weeks starting from the date on which your account was debited. Your rights are explained in a statement that you can obtain from your bank.";
        internal const string AVISO_1 = "TODOS LOS CAMPOS HAN DE SER CUMPLIMENTADOS OBLIGATORIAMENTE.";
        internal const string AVISO_2 = "UNA VEZ FIRMADA ESTA ORDEN DE DOMICILIACIÓN DEBE SER ENVIADA AL ACREEDOR PARA SU CUSTODIA.";
        internal const string AVISO_EN = "ALL GAPS ARE MANDATORY. ONCE THIS MANDATE HAS BEEN SIGNED MUST BE SENT TO CREDITOR FOR STORAGE.";

        private const float TAMANO_NORMAL = 10f;  // Arial 10 pt: el defecto del RDLC
        private const float TAMANO_TRADUCCION = 6f;
        private const float ANCHO_ETIQUETA_CM = 8.5f;

        private readonly Func<byte[]> _cargarLogo;

        public GeneradorPdfMandatoSepa() : this(DescargarLogo) { }

        /// <summary>Para tests: permite no ir a internet a por el logo.</summary>
        internal GeneradorPdfMandatoSepa(Func<byte[]> cargarLogo)
        {
            _cargarLogo = cargarLogo ?? (() => null);
        }

        /// <summary>Una fila etiqueta (castellano + traducción en 6 pt) → valor, como en el RDLC.</summary>
        internal class FilaMandato
        {
            public string Etiqueta { get; set; }
            public string Traduccion { get; set; }
            public bool DosPuntos { get; set; } = true;
            public string Valor { get; set; }
            /// <summary>Texto en 6 pt debajo de la etiqueta (p. ej. «(titular/es de la cuenta de cargo)»).</summary>
            public string Nota { get; set; }
        }

        internal static List<FilaMandato> FilasAcreedor(Mandato m) => new List<FilaMandato>
        {
            new FilaMandato { Etiqueta = "Referencia de la orden de domiciliación / ", Traduccion = "Mandate reference", Valor = m.Referencia },
            new FilaMandato { Etiqueta = "Identificador del acreedor / ", Traduccion = "Creditor Identifier", Valor = m.IdentificadorAcreedor },
            new FilaMandato { Etiqueta = "Nombre del acreedor / ", Traduccion = "Creditor's name", Valor = m.NombreAcreedor },
            new FilaMandato { Etiqueta = "Dirección / ", Traduccion = "Address", Valor = m.DireccionAcreedor },
            new FilaMandato { Etiqueta = "Código postal - Población - Provincia / ", Traduccion = "Postal Code - City - Town", Valor = m.PoblacionCompletaAcreedor },
            new FilaMandato { Etiqueta = "País / ", Traduccion = "Country", Valor = m.PaisAcreedor },
        };

        internal static List<FilaMandato> FilasDeudor(Mandato m, DateTime hoy) => new List<FilaMandato>
        {
            new FilaMandato { Etiqueta = "Nombre del deudor/es / ", Traduccion = "Debtor's name:", DosPuntos = false, Nota = "(titular/es de la cuenta de cargo)", Valor = m.NombreDeudor },
            new FilaMandato { Etiqueta = "Dirección del deudor / ", Traduccion = "Address of the debtor", Valor = m.DireccionDeudor },
            new FilaMandato { Etiqueta = "Código postal - Población - Provincia / ", Traduccion = "Postal Code - City - Town", Valor = m.PoblacionCompletaDeudor },
            new FilaMandato { Etiqueta = "País del deudor:", DosPuntos = false, Valor = m.PaisDeudor },
            new FilaMandato { Etiqueta = "Swift BIC ", Traduccion = "(puede contener 8 u 11 posiciones) / Swift BIC (up to 8 or 11 characters)", DosPuntos = false, Valor = m.SwiftBic },
            new FilaMandato { Etiqueta = "Número de cuenta - IBAN / ", Traduccion = "Account number - IBAN", Valor = m.Iban?.ToString() },
            // El tipo de pago es fijo y en el RDLC se pinta en negrita cursiva como las etiquetas.
            new FilaMandato { Etiqueta = "Tipo de pago / ", Traduccion = "Type of payment", Valor = null },
            new FilaMandato { Etiqueta = "Fecha - Localidad / ", Traduccion = "Date - location in which you are signing", Valor = $"{hoy:dd-MM-yyyy} - {m.PoblacionDeudor}" },
            new FilaMandato { Etiqueta = "Nombre de la persona que firma / ", Traduccion = "Signer's name", Valor = m.PersonaFirmante },
        };

        public ByteArrayContent GenerarPdf(List<Mandato> mandatos) => GenerarPdf(mandatos, DateTime.Today);

        internal ByteArrayContent GenerarPdf(List<Mandato> mandatos, DateTime hoy)
        {
            List<Mandato> datos = mandatos?.Where(m => m != null).ToList() ?? new List<Mandato>();
            if (!datos.Any())
            {
                throw new ArgumentException("No hay ningún mandato que imprimir", nameof(mandatos));
            }
            byte[] logo = CargarLogoSeguro();

            var documento = Document.Create(container =>
            {
                foreach (Mandato mandato in datos)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(1, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(TAMANO_NORMAL));

                        page.Content().Element(c => ComponerMandato(c, mandato, hoy, logo));
                        page.Footer().Element(ComponerAviso);
                    });
                }
            });

            return new ByteArrayContent(documento.GeneratePdf());
        }

        private byte[] CargarLogoSeguro()
        {
            try
            {
                return _cargarLogo();
            }
            catch
            {
                return null; // Sin logo el mandato sigue siendo válido
            }
        }

        private static byte[] DescargarLogo()
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
            {
                return client.GetByteArrayAsync(URL_LOGO).GetAwaiter().GetResult();
            }
        }

        private void ComponerMandato(IContainer container, Mandato m, DateTime hoy, byte[] logo)
        {
            container.Column(col =>
            {
                // Cabecera: logo (3,72 x 3,17 cm) y, a la derecha, referencia + identificador del acreedor
                col.Item().Height(3.17f, Unit.Centimetre).Row(row =>
                {
                    row.ConstantItem(3.72f, Unit.Centimetre).Element(c =>
                    {
                        if (logo != null)
                        {
                            c.AlignLeft().AlignTop().Image(logo, ImageScaling.FitArea);
                        }
                    });
                    row.ConstantItem(0.5f, Unit.Centimetre);
                    row.RelativeItem().PaddingTop(0.29f, Unit.Centimetre).Column(cab =>
                    {
                        cab.Item().AlignRight().Width(8.27f, Unit.Centimetre).Text(m.Referencia ?? string.Empty);
                        cab.Item().PaddingTop(0.03f, Unit.Centimetre).Row(r =>
                        {
                            r.RelativeItem().Text(t => EscribirEtiqueta(t, "Identificador del acreedor / ", "Creditor Identifier", true));
                            r.ConstantItem(8.27f, Unit.Centimetre).Text(m.IdentificadorAcreedor ?? string.Empty);
                        });
                    });
                });

                // Título
                col.Item().PaddingTop(0.2f, Unit.Centimetre).AlignCenter().Text(TITULO).FontSize(12).Bold();
                col.Item().PaddingBottom(0.2f, Unit.Centimetre).AlignCenter().Text(SUBTITULO).FontSize(8).Bold().Italic();

                // Recuadro del acreedor
                col.Item().PaddingHorizontal(0.6f, Unit.Centimetre).Border(2).Padding(0.3f, Unit.Centimetre).Column(caja =>
                {
                    caja.Spacing(2);
                    foreach (FilaMandato fila in FilasAcreedor(m))
                    {
                        caja.Item().Element(c => ComponerFila(c, fila));
                    }
                });

                // Texto legal
                col.Item().PaddingHorizontal(0.6f, Unit.Centimetre).PaddingTop(0.2f, Unit.Centimetre)
                    .Text(TEXTO_LEGAL_ES).FontSize(8);
                col.Item().PaddingHorizontal(0.6f, Unit.Centimetre).PaddingTop(0.15f, Unit.Centimetre)
                    .Text(TEXTO_LEGAL_EN).FontSize(TAMANO_TRADUCCION).Italic();

                // Recuadro del deudor
                col.Item().PaddingHorizontal(0.6f, Unit.Centimetre).PaddingTop(0.4f, Unit.Centimetre)
                    .Border(2).Padding(0.3f, Unit.Centimetre).Column(caja =>
                {
                    caja.Spacing(2);
                    foreach (FilaMandato fila in FilasDeudor(m, hoy))
                    {
                        if (fila.Traduccion == "Type of payment")
                        {
                            caja.Item().Row(r =>
                            {
                                r.ConstantItem(ANCHO_ETIQUETA_CM, Unit.Centimetre).Text(t => EscribirEtiqueta(t, fila.Etiqueta, fila.Traduccion, fila.DosPuntos));
                                r.RelativeItem().Text(t => EscribirEtiqueta(t, TIPO_PAGO, TIPO_PAGO_EN, false));
                            });
                            caja.Item().Height(0.4f, Unit.Centimetre); // el RDLC deja aire antes de la fecha
                            continue;
                        }
                        caja.Item().Element(c => ComponerFila(c, fila));
                    }
                    caja.Item().PaddingTop(0.4f, Unit.Centimetre)
                        .Text(t => EscribirEtiqueta(t, "Firma del deudor / ", "Signature of the debtor", true));
                    caja.Item().Height(3f, Unit.Centimetre); // hueco para la firma
                });
            });
        }

        private static void ComponerFila(IContainer container, FilaMandato fila)
        {
            container.Row(r =>
            {
                r.ConstantItem(ANCHO_ETIQUETA_CM, Unit.Centimetre).Column(c =>
                {
                    c.Item().Text(t => EscribirEtiqueta(t, fila.Etiqueta, fila.Traduccion, fila.DosPuntos));
                    if (!string.IsNullOrEmpty(fila.Nota))
                    {
                        c.Item().Text(fila.Nota).FontSize(TAMANO_TRADUCCION).Bold().Italic();
                    }
                });
                r.RelativeItem().Text(fila.Valor?.Trim() ?? string.Empty);
            });
        }

        private static void EscribirEtiqueta(TextDescriptor t, string etiqueta, string traduccion, bool dosPuntos)
        {
            t.Span(etiqueta).Bold().Italic();
            if (!string.IsNullOrEmpty(traduccion))
            {
                t.Span(traduccion).FontSize(TAMANO_TRADUCCION).Bold().Italic();
            }
            if (dosPuntos)
            {
                t.Span(":").Bold().Italic();
            }
        }

        private static void ComponerAviso(IContainer container)
        {
            container.Column(col =>
            {
                col.Item().AlignCenter().Text(AVISO_1).FontSize(8);
                col.Item().AlignCenter().Text(AVISO_2).FontSize(8);
                col.Item().AlignCenter().Text(AVISO_EN).FontSize(TAMANO_TRADUCCION).Italic();
            });
        }
    }
}
