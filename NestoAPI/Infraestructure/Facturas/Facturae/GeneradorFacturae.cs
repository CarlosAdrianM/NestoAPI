using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace NestoAPI.Infraestructure.Facturas.Facturae
{
    /// <summary>Los totales del fichero, calculados de las líneas y los impuestos del modelo (los mismos que escribe el XML).</summary>
    public class TotalesFacturae
    {
        public decimal TotalBruto { get; set; }
        public decimal TotalImpuestos { get; set; }
        public decimal TotalFactura { get; set; }
    }

    /// <summary>
    /// NestoAPI#903: escribe el XML Facturae 3.2.2 SIN firmar de un <see cref="ModeloFacturae"/>. Puro (sin BD).
    /// <para>El esquema es estricto con el ORDEN de los elementos (xs:sequence): aquí se escriben en el orden de
    /// Facturaev3_2_2.xsd y solo los opcionales que hacen falta. Los hijos van SIN espacio de nombres (elementFormDefault
    /// unqualified): solo la raíz lleva el prefijo «fe».</para>
    /// <para>Decimales: totales, bases y cuotas de la cabecera a 2; tipos (TaxRate, EquivalenceSurcharge) a 2; DiscountRate a 4;
    /// precios e importes de línea (UnitPriceWithoutTax, TotalCost, DiscountAmount, GrossAmount) con los suyos, hasta 8
    /// (DoubleUpToEightDecimalType). Punto decimal, sin separador de miles.</para>
    /// </summary>
    public static class GeneradorFacturae
    {
        public const string NAMESPACE_FACTURAE = "http://www.facturae.gob.es/formato/Versiones/Facturaev3_2_2.xml";
        public const string NAMESPACE_FIRMA = "http://www.w3.org/2000/09/xmldsig#";
        public const string VERSION_ESQUEMA = "3.2.2";
        public const string MONEDA = "EUR";
        public const string IVA = "01";
        public const string UNIDADES = "01";
        public const string MOTIVO_REDONDEO = "Redondeo a céntimos";

        private static readonly CultureInfo INVARIANTE = CultureInfo.InvariantCulture;

        public static TotalesFacturae Totales(ModeloFacturae modelo)
        {
            decimal bruto = RoundingHelper.DosDecimalesRound(modelo.Lineas.Sum(l => l.ImporteBruto));
            decimal impuestos = modelo.Impuestos.Sum(i => RoundingHelper.DosDecimalesRound(i.Cuota) + RoundingHelper.DosDecimalesRound(i.CuotaRecargo));
            return new TotalesFacturae
            {
                TotalBruto = bruto,
                TotalImpuestos = impuestos,
                TotalFactura = bruto + impuestos
            };
        }

        /// <summary>El fichero en UTF-8 (sin BOM).</summary>
        public static byte[] GenerarBytes(ModeloFacturae modelo)
        {
            if (modelo == null)
            {
                throw new ArgumentNullException(nameof(modelo));
            }
            var ajustes = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  "
            };
            using (var memoria = new MemoryStream())
            {
                using (XmlWriter x = XmlWriter.Create(memoria, ajustes))
                {
                    Escribir(x, modelo);
                }
                return memoria.ToArray();
            }
        }

        public static string Generar(ModeloFacturae modelo) => new UTF8Encoding(false).GetString(GenerarBytes(modelo));

        private static void Escribir(XmlWriter x, ModeloFacturae m)
        {
            TotalesFacturae totales = Totales(m);
            x.WriteStartDocument();
            x.WriteStartElement("fe", "Facturae", NAMESPACE_FACTURAE);
            x.WriteAttributeString("xmlns", "ds", null, NAMESPACE_FIRMA);

            // FileHeader
            x.WriteStartElement("FileHeader");
            Texto(x, "SchemaVersion", VERSION_ESQUEMA);
            Texto(x, "Modality", "I");
            Texto(x, "InvoiceIssuerType", "EM");
            x.WriteStartElement("Batch");
            Texto(x, "BatchIdentifier", m.IdentificadorLote);
            Texto(x, "InvoicesCount", "1");
            Importe(x, "TotalInvoicesAmount", totales.TotalFactura);
            Importe(x, "TotalOutstandingAmount", totales.TotalFactura);
            Importe(x, "TotalExecutableAmount", totales.TotalFactura);
            Texto(x, "InvoiceCurrencyCode", MONEDA);
            x.WriteEndElement(); // Batch
            x.WriteEndElement(); // FileHeader

            // Parties
            x.WriteStartElement("Parties");
            x.WriteStartElement("SellerParty");
            Identificacion(x, m.Emisor);
            EntidadLegal(x, m.Emisor);
            x.WriteEndElement();
            x.WriteStartElement("BuyerParty");
            Identificacion(x, m.Receptor);
            if (m.Centros != null && m.Centros.Any())
            {
                x.WriteStartElement("AdministrativeCentres");
                foreach (CentroAdministrativoFacturae centro in m.Centros.OrderBy(c => c.Rol))
                {
                    x.WriteStartElement("AdministrativeCentre");
                    Texto(x, "CentreCode", centro.Codigo);
                    Texto(x, "RoleTypeCode", centro.Rol);
                    TextoOpcional(x, "Name", ModeloFacturae.Recortar(centro.Nombre, 40));
                    Direccion(x, centro.Direccion);
                    TextoOpcional(x, "CentreDescription", centro.Descripcion);
                    x.WriteEndElement();
                }
                x.WriteEndElement();
            }
            EntidadLegal(x, m.Receptor);
            x.WriteEndElement();
            x.WriteEndElement(); // Parties

            // Invoices
            x.WriteStartElement("Invoices");
            x.WriteStartElement("Invoice");

            x.WriteStartElement("InvoiceHeader");
            Texto(x, "InvoiceNumber", m.Numero);
            TextoOpcional(x, "InvoiceSeriesCode", m.Serie);
            Texto(x, "InvoiceDocumentType", "FC");
            Texto(x, "InvoiceClass", m.Rectificativa == null ? "OO" : "OR");
            if (m.Rectificativa != null)
            {
                RectificativaFacturae r = m.Rectificativa;
                x.WriteStartElement("Corrective");
                Texto(x, "InvoiceNumber", r.NumeroOriginal);
                TextoOpcional(x, "InvoiceSeriesCode", r.SerieOriginal);
                Texto(x, "ReasonCode", r.CodigoMotivo);
                Texto(x, "ReasonDescription", r.DescripcionMotivo);
                x.WriteStartElement("TaxPeriod");
                Texto(x, "StartDate", Fecha(r.FechaOriginal));
                Texto(x, "EndDate", Fecha(r.FechaOriginal));
                x.WriteEndElement();
                Texto(x, "CorrectionMethod", r.CodigoMetodo);
                Texto(x, "CorrectionMethodDescription", r.DescripcionMetodo);
                TextoOpcional(x, "AdditionalReasonDescription", r.MotivoAdicional);
                x.WriteEndElement();
            }
            x.WriteEndElement(); // InvoiceHeader

            x.WriteStartElement("InvoiceIssueData");
            Texto(x, "IssueDate", Fecha(m.Fecha));
            Texto(x, "InvoiceCurrencyCode", MONEDA);
            Texto(x, "TaxCurrencyCode", MONEDA);
            Texto(x, "LanguageName", "es");
            x.WriteEndElement();

            x.WriteStartElement("TaxesOutputs");
            foreach (ImpuestoFacturae impuesto in m.Impuestos.OrderByDescending(i => i.TipoIva).ThenByDescending(i => i.TipoRecargo))
            {
                Impuesto(x, impuesto.TipoIva, RoundingHelper.DosDecimalesRound(impuesto.Base), impuesto.Cuota, impuesto.TipoRecargo, impuesto.CuotaRecargo, Dos);
            }
            x.WriteEndElement();

            x.WriteStartElement("InvoiceTotals");
            Texto(x, "TotalGrossAmount", Dos(totales.TotalBruto));
            Texto(x, "TotalGeneralDiscounts", Dos(0));
            Texto(x, "TotalGeneralSurcharges", Dos(0));
            Texto(x, "TotalGrossAmountBeforeTaxes", Dos(totales.TotalBruto));
            Texto(x, "TotalTaxOutputs", Dos(totales.TotalImpuestos));
            Texto(x, "TotalTaxesWithheld", Dos(0));
            Texto(x, "InvoiceTotal", Dos(totales.TotalFactura));
            Texto(x, "TotalOutstandingAmount", Dos(totales.TotalFactura));
            Texto(x, "TotalExecutableAmount", Dos(totales.TotalFactura));
            x.WriteEndElement();

            x.WriteStartElement("Items");
            foreach (LineaFacturae linea in m.Lineas)
            {
                Linea(x, linea);
            }
            x.WriteEndElement();

            if (m.Vencimientos != null && m.Vencimientos.Any())
            {
                x.WriteStartElement("PaymentDetails");
                foreach (VencimientoFacturae v in m.Vencimientos.OrderBy(v => v.Fecha))
                {
                    x.WriteStartElement("Installment");
                    Texto(x, "InstallmentDueDate", Fecha(v.Fecha));
                    Texto(x, "InstallmentAmount", Dos(v.Importe));
                    Texto(x, "PaymentMeans", v.MedioPago);
                    if (!string.IsNullOrWhiteSpace(v.Iban))
                    {
                        x.WriteStartElement("AccountToBeCredited");
                        Texto(x, "IBAN", v.Iban);
                        x.WriteEndElement();
                    }
                    x.WriteEndElement();
                }
                x.WriteEndElement();
            }

            x.WriteEndElement(); // Invoice
            x.WriteEndElement(); // Invoices
            x.WriteEndElement(); // fe:Facturae
            x.WriteEndDocument();
        }

        private static void Linea(XmlWriter x, LineaFacturae l)
        {
            x.WriteStartElement("InvoiceLine");
            // Orden de InvoiceLineType: …ReceiverContractReference, ReceiverContractDate, ReceiverTransactionReference,
            // ReceiverTransactionDate, FileReference, FileDate, SequenceNumber, DeliveryNotesReferences, ItemDescription…
            TextoOpcional(x, "ReceiverContractReference", l.Contrato);
            TextoOpcional(x, "ReceiverTransactionReference", l.ReferenciaPedido);
            TextoOpcional(x, "FileReference", l.Expediente);
            if (!string.IsNullOrWhiteSpace(l.Albaran))
            {
                x.WriteStartElement("DeliveryNotesReferences");
                x.WriteStartElement("DeliveryNote");
                Texto(x, "DeliveryNoteNumber", l.Albaran);
                if (l.FechaAlbaran.HasValue)
                {
                    Texto(x, "DeliveryNoteDate", Fecha(l.FechaAlbaran.Value));
                }
                x.WriteEndElement();
                x.WriteEndElement();
            }
            Texto(x, "ItemDescription", ModeloFacturae.Recortar(l.Descripcion, 2500));
            Texto(x, "Quantity", l.Cantidad.ToString("0.########", INVARIANTE));
            Texto(x, "UnitOfMeasure", UNIDADES);
            Texto(x, "UnitPriceWithoutTax", HastaOcho(l.PrecioUnitario));
            Texto(x, "TotalCost", HastaOcho(l.CosteTotal));
            // El redondeo a céntimos del Bruto: cargo si agranda el importe (mismo signo que el Bruto), descuento si lo achica
            bool redondeoEsCargo = l.ImporteRedondeo != 0 && Math.Sign(l.ImporteRedondeo) == Math.Sign(l.CosteTotal);
            bool redondeoEsDescuento = l.ImporteRedondeo != 0 && !redondeoEsCargo;
            if (l.ImporteDescuento != 0 || redondeoEsDescuento)
            {
                x.WriteStartElement("DiscountsAndRebates");
                if (l.ImporteDescuento != 0)
                {
                    x.WriteStartElement("Discount");
                    Texto(x, "DiscountReason", string.IsNullOrWhiteSpace(l.MotivoDescuento) ? "Descuento" : l.MotivoDescuento);
                    if (l.PorcentajeDescuento.HasValue)
                    {
                        Texto(x, "DiscountRate", RoundingHelper.Round(l.PorcentajeDescuento.Value, 4).ToString("0.0000", INVARIANTE));
                    }
                    Texto(x, "DiscountAmount", HastaOcho(l.ImporteDescuento));
                    x.WriteEndElement();
                }
                if (redondeoEsDescuento)
                {
                    x.WriteStartElement("Discount");
                    Texto(x, "DiscountReason", MOTIVO_REDONDEO);
                    Texto(x, "DiscountAmount", HastaOcho(-l.ImporteRedondeo));
                    x.WriteEndElement();
                }
                x.WriteEndElement();
            }
            if (redondeoEsCargo)
            {
                x.WriteStartElement("Charges");
                x.WriteStartElement("Charge");
                Texto(x, "ChargeReason", MOTIVO_REDONDEO);
                Texto(x, "ChargeAmount", HastaOcho(l.ImporteRedondeo));
                x.WriteEndElement();
                x.WriteEndElement();
            }
            Texto(x, "GrossAmount", HastaOcho(l.ImporteBruto));
            x.WriteStartElement("TaxesOutputs");
            Impuesto(x, l.TipoIva, l.ImporteBruto, l.CuotaIva, l.TipoRecargo, l.CuotaRecargo, HastaOcho);
            x.WriteEndElement();
            TextoOpcional(x, "ArticleCode", ModeloFacturae.Recortar(l.CodigoArticulo, 20));
            x.WriteEndElement();
        }

        private static void Impuesto(XmlWriter x, decimal tipoIva, decimal baseImponible, decimal cuota, decimal tipoRecargo, decimal cuotaRecargo, Func<decimal, string> formatoBase)
        {
            x.WriteStartElement("Tax");
            Texto(x, "TaxTypeCode", IVA);
            Texto(x, "TaxRate", Dos(tipoIva));
            x.WriteStartElement("TaxableBase");
            Texto(x, "TotalAmount", formatoBase(baseImponible));
            x.WriteEndElement();
            Importe(x, "TaxAmount", cuota);
            if (tipoRecargo != 0)
            {
                Texto(x, "EquivalenceSurcharge", Dos(tipoRecargo));
                Importe(x, "EquivalenceSurchargeAmount", cuotaRecargo);
            }
            x.WriteEndElement();
        }

        private static void Identificacion(XmlWriter x, ParteFacturae parte)
        {
            x.WriteStartElement("TaxIdentification");
            Texto(x, "PersonTypeCode", parte.TipoPersona ?? "J");
            Texto(x, "ResidenceTypeCode", "R");
            Texto(x, "TaxIdentificationNumber", parte.Nif);
            x.WriteEndElement();
        }

        private static void EntidadLegal(XmlWriter x, ParteFacturae parte)
        {
            x.WriteStartElement("LegalEntity");
            Texto(x, "CorporateName", ModeloFacturae.Recortar(parte.Nombre, 80));
            Direccion(x, parte.Direccion);
            x.WriteEndElement();
        }

        private static void Direccion(XmlWriter x, DireccionFacturae d)
        {
            if (d == null)
            {
                return;
            }
            x.WriteStartElement("AddressInSpain");
            Texto(x, "Address", ModeloFacturae.Recortar(d.Direccion, 80));
            Texto(x, "PostCode", d.CodigoPostal);
            Texto(x, "Town", ModeloFacturae.Recortar(d.Poblacion, 50));
            Texto(x, "Province", ModeloFacturae.Recortar(d.Provincia, 20));
            Texto(x, "CountryCode", d.Pais ?? "ESP");
            x.WriteEndElement();
        }

        private static void Importe(XmlWriter x, string elemento, decimal valor)
        {
            x.WriteStartElement(elemento);
            Texto(x, "TotalAmount", Dos(valor));
            x.WriteEndElement();
        }

        private static void Texto(XmlWriter x, string elemento, string valor) => x.WriteElementString(elemento, valor ?? string.Empty);

        private static void TextoOpcional(XmlWriter x, string elemento, string valor)
        {
            if (!string.IsNullOrWhiteSpace(valor))
            {
                x.WriteElementString(elemento, valor.Trim());
            }
        }

        internal static string Dos(decimal valor) => RoundingHelper.DosDecimalesRound(valor).ToString("0.00", INVARIANTE);

        internal static string HastaOcho(decimal valor) => RoundingHelper.Round(valor, 8).ToString("0.00######", INVARIANTE);

        internal static string Fecha(DateTime fecha) => fecha.ToString("yyyy-MM-dd", INVARIANTE);
    }
}
