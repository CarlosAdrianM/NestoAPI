using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Facturas.Facturae;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace NestoAPI.Tests.Infrastructure.Facturas
{
    /// <summary>NestoAPI#903: el fichero Facturae 3.2.2 sin firmar (constructor del modelo, generador y DIR3).</summary>
    [TestClass]
    public class FacturaeTests
    {
        internal const string IBAN_EMPRESA = "ES91 2100 0418 4502 0005 1332";
        private static readonly XNamespace FE = GeneradorFacturae.NAMESPACE_FACTURAE;

        internal static LinPedidoVta Linea(int orden, short cantidad, decimal precio, decimal sumaDescuentos, byte iva, decimal re = 0,
            string producto = "17404", string texto = "CREMA HIDRATANTE 50 ML", byte tipoLinea = 1, int? albaran = 732939)
        {
            decimal bruto = cantidad * precio;
            decimal importeDto = Math.Round(bruto * sumaDescuentos, 2, MidpointRounding.AwayFromZero);
            decimal baseImponible = Math.Round(bruto, 2, MidpointRounding.AwayFromZero) - importeDto;
            return new LinPedidoVta
            {
                Empresa = "1",
                Número = 930001,
                Nº_Orden = orden,
                TipoLinea = tipoLinea,
                Producto = producto,
                Texto = texto,
                Cantidad = cantidad,
                Precio = precio,
                Bruto = bruto,
                SumaDescuentos = sumaDescuentos,
                Descuento = sumaDescuentos,
                ImporteDto = importeDto,
                Base_Imponible = baseImponible,
                PorcentajeIVA = iva,
                PorcentajeRE = re,
                ImporteIVA = baseImponible * iva / 100M,
                ImporteRE = baseImponible * re,
                Total = baseImponible + baseImponible * iva / 100M + baseImponible * re,
                Nº_Albarán = albaran,
                Fecha_Albarán = albaran.HasValue ? new DateTime(2026, 11, 20) : (DateTime?)null
            };
        }

        /// <summary>Lo mismo que hace GestorFacturas.LeerFactura con los totales.</summary>
        internal static decimal TotalComoElPdf(IEnumerable<LinPedidoVta> lineas)
        {
            decimal total = 0;
            foreach (var g in lineas.GroupBy(l => new { l.PorcentajeIVA, l.PorcentajeRE }))
            {
                total += g.Sum(l => l.Base_Imponible)
                    + Math.Round(g.Sum(l => l.ImporteIVA), 2, MidpointRounding.AwayFromZero)
                    + Math.Round(g.Sum(l => l.PorcentajeRE * l.Base_Imponible), 2, MidpointRounding.AwayFromZero);
            }
            return Math.Round(total, 2, MidpointRounding.AwayFromZero);
        }

        internal static DatosOrigenFacturae Datos(List<LinPedidoVta> lineas, params (DateTime fecha, decimal importe, string forma)[] vencimientos)
        {
            decimal total = TotalComoElPdf(lineas);
            List<VencimientoFactura> vtos = vencimientos.Any()
                ? vencimientos.Select(v => new VencimientoFactura { Vencimiento = v.fecha, Importe = v.importe, FormaPago = v.forma, Iban = v.forma == "TRN" ? IBAN_EMPRESA : "<<< No Procede >>>" }).ToList()
                : new List<VencimientoFactura> { new VencimientoFactura { Vencimiento = new DateTime(2026, 12, 24), Importe = total, FormaPago = "TRN", Iban = IBAN_EMPRESA } };
            return new DatosOrigenFacturae
            {
                Factura = new Factura
                {
                    Cliente = "33895",
                    NumeroFactura = "NV2616075",
                    Serie = "NV",
                    Fecha = new DateTime(2026, 11, 24),
                    Nif = "P2800000J",
                    ImporteTotal = total,
                    SuPedido = "PO-2026/118",
                    Direcciones = new List<DireccionFactura>
                    {
                        new DireccionFactura { Tipo = "Empresa", Nombre = "NUEVA VISIÓN, S.A.", Direccion = "C/ Río Tiétar, 11\nPolígono Industrial Río de Janeiro", CodigoPostal = "28110", Poblacion = "Algete", Provincia = "Madrid" },
                        new DireccionFactura { Tipo = "Fiscal", Nombre = "AYUNTAMIENTO DE VILLAEJEMPLO", Direccion = "PLAZA MAYOR, 1", CodigoPostal = "28001", Poblacion = "Madrid", Provincia = "Madrid" },
                        new DireccionFactura { Tipo = "Entrega", Nombre = "AYUNTAMIENTO DE VILLAEJEMPLO", Direccion = "PLAZA MAYOR, 1", CodigoPostal = "28001", Poblacion = "Madrid", Provincia = "Madrid" }
                    },
                    Vencimientos = vtos
                },
                Lineas = lineas,
                NifEmpresa = "ESB99999999",
                Organismo = new DatosOrganismoFacturae
                {
                    OficinaContable = "L01280796",
                    OrganoGestor = "L01280796",
                    UnidadTramitadora = "LA0007357",
                    NombreOficinaContable = "Intervención General"
                },
                Referencias = new ReferenciasFacturae { Pedido = "PO-2026/118" }
            };
        }

        private static XDocument Xml(DatosOrigenFacturae datos, out ResultadoModeloFacturae resultado)
        {
            resultado = ConstructorModeloFacturae.Construir(datos);
            Assert.IsTrue(resultado.Listo, "Faltas: " + string.Join(" | ", resultado.Faltas));
            string xml = GeneradorFacturae.Generar(resultado.Modelo);
            return XDocument.Parse(xml); // bien formado
        }

        private static decimal D(XElement e) => decimal.Parse(e.Value, CultureInfo.InvariantCulture);
        private static XElement Hijo(XElement e, string nombre) => e.Element(nombre) ?? throw new AssertFailedException($"Falta {nombre} en {e.Name}");
        private static string[] Nombres(XElement e) => e.Elements().Select(x => x.Name.LocalName).ToArray();

        private static void ComprobarCuadre(XDocument doc, decimal totalFactura)
        {
            XElement invoice = doc.Root.Element("Invoices").Element("Invoice");
            XElement totales = Hijo(invoice, "InvoiceTotals");
            List<XElement> lineas = invoice.Element("Items").Elements("InvoiceLine").ToList();
            decimal sumaBrutoLineas = lineas.Sum(l => D(Hijo(l, "GrossAmount")));
            Assert.AreEqual(D(Hijo(totales, "TotalGrossAmount")), sumaBrutoLineas, "Suma de líneas = TotalGrossAmount");
            foreach (XElement l in lineas)
            {
                decimal descuentos = l.Element("DiscountsAndRebates")?.Elements("Discount").Sum(d => D(Hijo(d, "DiscountAmount"))) ?? 0;
                decimal cargos = l.Element("Charges")?.Elements("Charge").Sum(c => D(Hijo(c, "ChargeAmount"))) ?? 0;
                Assert.AreEqual(D(Hijo(l, "TotalCost")) - descuentos + cargos, D(Hijo(l, "GrossAmount")), "GrossAmount = TotalCost − descuentos + cargos");
                Assert.AreEqual(D(Hijo(l, "Quantity")) * D(Hijo(l, "UnitPriceWithoutTax")), D(Hijo(l, "TotalCost")), "TotalCost = Quantity × UnitPrice");
            }
            List<XElement> impuestos = Hijo(invoice, "TaxesOutputs").Elements("Tax").ToList();
            decimal cuotas = impuestos.Sum(t => D(t.Element("TaxAmount").Element("TotalAmount")) + (t.Element("EquivalenceSurchargeAmount") is XElement r ? D(r.Element("TotalAmount")) : 0));
            Assert.AreEqual(D(Hijo(totales, "TotalTaxOutputs")), cuotas, "TotalTaxOutputs = IVA + recargo");
            foreach (XElement t in impuestos)
            {
                string tipo = t.Element("TaxRate").Value;
                string recargo = t.Element("EquivalenceSurcharge")?.Value;
                var deLineas = lineas.Select(l => l.Element("TaxesOutputs").Element("Tax"))
                    .Where(x => x.Element("TaxRate").Value == tipo && x.Element("EquivalenceSurcharge")?.Value == recargo).ToList();
                Assert.AreEqual(D(t.Element("TaxableBase").Element("TotalAmount")), deLineas.Sum(x => D(x.Element("TaxableBase").Element("TotalAmount"))), $"Base del {tipo} %");
                Assert.AreEqual(D(t.Element("TaxAmount").Element("TotalAmount")), deLineas.Sum(x => D(x.Element("TaxAmount").Element("TotalAmount"))), $"Cuota del {tipo} %");
                if (recargo != null)
                {
                    Assert.AreEqual(D(t.Element("EquivalenceSurchargeAmount").Element("TotalAmount")), deLineas.Sum(x => D(x.Element("EquivalenceSurchargeAmount").Element("TotalAmount"))), "Recargo");
                }
            }
            decimal total = D(Hijo(totales, "InvoiceTotal"));
            Assert.AreEqual(totalFactura, total, "InvoiceTotal = el total de la factura");
            Assert.AreEqual(D(Hijo(totales, "TotalGrossAmountBeforeTaxes")) + cuotas - D(Hijo(totales, "TotalTaxesWithheld")), total);
            Assert.AreEqual(total, D(Hijo(totales, "TotalExecutableAmount")));
            decimal plazos = invoice.Element("PaymentDetails").Elements("Installment").Sum(i => D(i.Element("InstallmentAmount")));
            Assert.AreEqual(total, plazos, "Suma de vencimientos = TotalExecutableAmount");
            XElement lote = doc.Root.Element("FileHeader").Element("Batch");
            Assert.AreEqual(total, D(lote.Element("TotalInvoicesAmount").Element("TotalAmount")));
            Assert.AreEqual(total, D(lote.Element("TotalExecutableAmount").Element("TotalAmount")));
        }

        [TestMethod]
        public void Generar_UnTipoDeIva_XmlBienFormadoConNamespaceYOrdenDelEsquema()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 2, 10M, 0, 21), Linea(2, 3, 3.3333M, 0, 21, producto: "40695", texto: "JABÓN & <GEL>") };

            XDocument doc = Xml(Datos(lineas), out _);

            Assert.AreEqual(FE + "Facturae", doc.Root.Name);
            Assert.AreEqual("fe", doc.Root.GetPrefixOfNamespace(FE));
            Assert.AreEqual(GeneradorFacturae.NAMESPACE_FIRMA, doc.Root.GetNamespaceOfPrefix("ds")?.NamespaceName);
            CollectionAssert.AreEqual(new[] { "FileHeader", "Parties", "Invoices" }, Nombres(doc.Root));
            Assert.IsTrue(doc.Root.Elements().All(e => e.Name.Namespace == XNamespace.None), "Los hijos van sin espacio de nombres (unqualified)");

            XElement cabecera = doc.Root.Element("FileHeader");
            CollectionAssert.AreEqual(new[] { "SchemaVersion", "Modality", "InvoiceIssuerType", "Batch" }, Nombres(cabecera));
            Assert.AreEqual("3.2.2", cabecera.Element("SchemaVersion").Value);
            Assert.AreEqual("I", cabecera.Element("Modality").Value);
            Assert.AreEqual("EM", cabecera.Element("InvoiceIssuerType").Value);
            CollectionAssert.AreEqual(new[] { "BatchIdentifier", "InvoicesCount", "TotalInvoicesAmount", "TotalOutstandingAmount", "TotalExecutableAmount", "InvoiceCurrencyCode" },
                Nombres(cabecera.Element("Batch")));
            Assert.AreEqual("B999999992616075NV", cabecera.Element("Batch").Element("BatchIdentifier").Value, "NIF sin ES + número + serie");

            XElement vendedor = doc.Root.Element("Parties").Element("SellerParty");
            CollectionAssert.AreEqual(new[] { "TaxIdentification", "LegalEntity" }, Nombres(vendedor));
            CollectionAssert.AreEqual(new[] { "PersonTypeCode", "ResidenceTypeCode", "TaxIdentificationNumber" }, Nombres(vendedor.Element("TaxIdentification")));
            Assert.AreEqual("B99999999", vendedor.Element("TaxIdentification").Element("TaxIdentificationNumber").Value);
            XElement direccionVendedor = vendedor.Element("LegalEntity").Element("AddressInSpain");
            CollectionAssert.AreEqual(new[] { "Address", "PostCode", "Town", "Province", "CountryCode" }, Nombres(direccionVendedor));
            Assert.AreEqual("C/ Río Tiétar, 11, Polígono Industrial Río de Janeiro", direccionVendedor.Element("Address").Value);
            Assert.AreEqual("ESP", direccionVendedor.Element("CountryCode").Value);

            XElement comprador = doc.Root.Element("Parties").Element("BuyerParty");
            CollectionAssert.AreEqual(new[] { "TaxIdentification", "AdministrativeCentres", "LegalEntity" }, Nombres(comprador));
            List<XElement> centros = comprador.Element("AdministrativeCentres").Elements("AdministrativeCentre").ToList();
            CollectionAssert.AreEqual(new[] { "01", "02", "03" }, centros.Select(c => c.Element("RoleTypeCode").Value).ToArray());
            CollectionAssert.AreEqual(new[] { "CentreCode", "RoleTypeCode", "Name", "AddressInSpain", "CentreDescription" }, Nombres(centros[0]));
            CollectionAssert.AreEqual(new[] { "CentreCode", "RoleTypeCode", "AddressInSpain", "CentreDescription" }, Nombres(centros[1]), "Sin nombre, sin Name");
            Assert.AreEqual("LA0007357", centros[2].Element("CentreCode").Value);

            XElement invoice = doc.Root.Element("Invoices").Element("Invoice");
            CollectionAssert.AreEqual(new[] { "InvoiceHeader", "InvoiceIssueData", "TaxesOutputs", "InvoiceTotals", "Items", "PaymentDetails" }, Nombres(invoice));
            CollectionAssert.AreEqual(new[] { "InvoiceNumber", "InvoiceSeriesCode", "InvoiceDocumentType", "InvoiceClass" }, Nombres(invoice.Element("InvoiceHeader")));
            Assert.AreEqual("2616075", invoice.Element("InvoiceHeader").Element("InvoiceNumber").Value);
            Assert.AreEqual("NV", invoice.Element("InvoiceHeader").Element("InvoiceSeriesCode").Value);
            Assert.AreEqual("OO", invoice.Element("InvoiceHeader").Element("InvoiceClass").Value);
            CollectionAssert.AreEqual(new[] { "IssueDate", "InvoiceCurrencyCode", "TaxCurrencyCode", "LanguageName" }, Nombres(invoice.Element("InvoiceIssueData")));
            Assert.AreEqual("2026-11-24", invoice.Element("InvoiceIssueData").Element("IssueDate").Value);
            CollectionAssert.AreEqual(new[] { "TotalGrossAmount", "TotalGeneralDiscounts", "TotalGeneralSurcharges", "TotalGrossAmountBeforeTaxes", "TotalTaxOutputs",
                "TotalTaxesWithheld", "InvoiceTotal", "TotalOutstandingAmount", "TotalExecutableAmount" }, Nombres(invoice.Element("InvoiceTotals")));
            XElement tax = invoice.Element("TaxesOutputs").Element("Tax");
            CollectionAssert.AreEqual(new[] { "TaxTypeCode", "TaxRate", "TaxableBase", "TaxAmount" }, Nombres(tax));
            Assert.AreEqual("01", tax.Element("TaxTypeCode").Value);
            Assert.AreEqual("21.00", tax.Element("TaxRate").Value);
            Assert.AreEqual("30.00", tax.Element("TaxableBase").Element("TotalAmount").Value);
            Assert.AreEqual("6.30", tax.Element("TaxAmount").Element("TotalAmount").Value);

            List<XElement> lineasXml = invoice.Element("Items").Elements("InvoiceLine").ToList();
            Assert.AreEqual(2, lineasXml.Count);
            CollectionAssert.AreEqual(new[] { "ReceiverTransactionReference", "DeliveryNotesReferences", "ItemDescription", "Quantity", "UnitOfMeasure",
                "UnitPriceWithoutTax", "TotalCost", "GrossAmount", "TaxesOutputs", "ArticleCode" }, Nombres(lineasXml[0]));
            Assert.AreEqual("PO-2026/118", lineasXml[0].Element("ReceiverTransactionReference").Value);
            Assert.AreEqual("732939", lineasXml[0].Element("DeliveryNotesReferences").Element("DeliveryNote").Element("DeliveryNoteNumber").Value);
            Assert.AreEqual("JABÓN & <GEL>", lineasXml[1].Element("ItemDescription").Value, "Se escapa al escribir y se lee igual");
            Assert.AreEqual("3.3333", lineasXml[1].Element("UnitPriceWithoutTax").Value);
            Assert.AreEqual("9.9999", lineasXml[1].Element("TotalCost").Value);
            Assert.IsNull(lineasXml[1].Element("DiscountsAndRebates"));
            CollectionAssert.AreEqual(new[] { "ChargeReason", "ChargeAmount" }, Nombres(lineasXml[1].Element("Charges").Element("Charge")));
            Assert.AreEqual("0.0001", lineasXml[1].Element("Charges").Element("Charge").Element("ChargeAmount").Value,
                "El redondeo del Bruto a céntimos (9,9999 → 10,00) va como cargo: GrossAmount es la base de la factura");
            Assert.AreEqual("10.00", lineasXml[1].Element("GrossAmount").Value);
            Assert.AreEqual("40695", lineasXml[1].Element("ArticleCode").Value);

            XElement plazo = invoice.Element("PaymentDetails").Element("Installment");
            CollectionAssert.AreEqual(new[] { "InstallmentDueDate", "InstallmentAmount", "PaymentMeans", "AccountToBeCredited" }, Nombres(plazo));
            Assert.AreEqual("04", plazo.Element("PaymentMeans").Value);
            Assert.AreEqual("ES9121000418450200051332", plazo.Element("AccountToBeCredited").Element("IBAN").Value);

            ComprobarCuadre(doc, 36.30M);
        }

        [TestMethod]
        public void Generar_DosTiposDeIva_UnTaxPorTipoYCuadra()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 1, 100M, 0, 21), Linea(2, 2, 7.35M, 0, 10, producto: "90324", texto: "LIBRO") };

            XDocument doc = Xml(Datos(lineas), out _);

            List<XElement> impuestos = doc.Root.Element("Invoices").Element("Invoice").Element("TaxesOutputs").Elements("Tax").ToList();
            CollectionAssert.AreEqual(new[] { "21.00", "10.00" }, impuestos.Select(t => t.Element("TaxRate").Value).ToArray());
            Assert.AreEqual("1.47", impuestos[1].Element("TaxAmount").Element("TotalAmount").Value);
            ComprobarCuadre(doc, 100M + 21M + 14.70M + 1.47M);
        }

        [TestMethod]
        public void Generar_ConRecargoDeEquivalencia_VaDentroDelMismoImpuesto()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 4, 25M, 0, 21, 0.052M) };

            XDocument doc = Xml(Datos(lineas), out _);

            XElement tax = doc.Root.Element("Invoices").Element("Invoice").Element("TaxesOutputs").Element("Tax");
            CollectionAssert.AreEqual(new[] { "TaxTypeCode", "TaxRate", "TaxableBase", "TaxAmount", "EquivalenceSurcharge", "EquivalenceSurchargeAmount" }, Nombres(tax));
            Assert.AreEqual("5.20", tax.Element("EquivalenceSurcharge").Value);
            Assert.AreEqual("5.20", tax.Element("EquivalenceSurchargeAmount").Element("TotalAmount").Value);
            Assert.AreEqual("26.20", doc.Root.Element("Invoices").Element("Invoice").Element("InvoiceTotals").Element("TotalTaxOutputs").Value);
            ComprobarCuadre(doc, 126.20M);
        }

        [TestMethod]
        public void Generar_ConDescuentoDeLinea_DiscountsAndRebatesEntreTotalCostYGrossAmount()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 4, 12.5M, 0.15M, 21) };

            XDocument doc = Xml(Datos(lineas), out _);

            XElement linea = doc.Root.Element("Invoices").Element("Invoice").Element("Items").Element("InvoiceLine");
            CollectionAssert.AreEqual(new[] { "ReceiverTransactionReference", "DeliveryNotesReferences", "ItemDescription", "Quantity", "UnitOfMeasure",
                "UnitPriceWithoutTax", "TotalCost", "DiscountsAndRebates", "GrossAmount", "TaxesOutputs", "ArticleCode" }, Nombres(linea));
            XElement descuento = linea.Element("DiscountsAndRebates").Element("Discount");
            CollectionAssert.AreEqual(new[] { "DiscountReason", "DiscountRate", "DiscountAmount" }, Nombres(descuento));
            Assert.AreEqual("15.0000", descuento.Element("DiscountRate").Value);
            Assert.AreEqual("7.50", descuento.Element("DiscountAmount").Value);
            Assert.AreEqual("50.00", linea.Element("TotalCost").Value);
            Assert.AreEqual("42.50", linea.Element("GrossAmount").Value);
            ComprobarCuadre(doc, 42.50M + 8.93M);
        }

        [TestMethod]
        public void Generar_CuotasDeLineaQueNoSumanLaDeCabecera_LaLineaMayorSeLlevaElCentimo()
        {
            // 3 × 1,05 al 10 %: cada línea 0,105 → 0,11 (0,33), pero la factura dice ROUND(0,315) = 0,32
            var lineas = new List<LinPedidoVta> { Linea(1, 1, 1.05M, 0, 10), Linea(2, 1, 1.05M, 0, 10), Linea(3, 1, 1.05M, 0, 10) };

            XDocument doc = Xml(Datos(lineas), out _);

            XElement invoice = doc.Root.Element("Invoices").Element("Invoice");
            Assert.AreEqual("0.32", invoice.Element("TaxesOutputs").Element("Tax").Element("TaxAmount").Element("TotalAmount").Value);
            ComprobarCuadre(doc, 3.47M);
        }

        [TestMethod]
        public void Generar_BrutoQueRedondeaHaciaAbajo_ElRedondeoVaComoDescuento()
        {
            // 3 × 1,0001 = 3,0003 → la factura usa 3,00
            var lineas = new List<LinPedidoVta> { Linea(1, 3, 1.0001M, 0, 21) };

            XDocument doc = Xml(Datos(lineas), out _);

            XElement linea = doc.Root.Element("Invoices").Element("Invoice").Element("Items").Element("InvoiceLine");
            XElement descuento = linea.Element("DiscountsAndRebates").Element("Discount");
            Assert.AreEqual(GeneradorFacturae.MOTIVO_REDONDEO, descuento.Element("DiscountReason").Value);
            Assert.IsNull(descuento.Element("DiscountRate"));
            Assert.AreEqual("0.0003", descuento.Element("DiscountAmount").Value);
            Assert.IsNull(linea.Element("Charges"));
            ComprobarCuadre(doc, 3.63M);
        }

        [TestMethod]
        public void Generar_DosVencimientos_UnInstallmentPorVencimiento()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 10, 10M, 0, 21) };

            XDocument doc = Xml(Datos(lineas, (new DateTime(2026, 12, 24), 60.50M, "TRN"), (new DateTime(2027, 1, 23), 60.50M, "TRN")), out _);

            List<XElement> plazos = doc.Root.Element("Invoices").Element("Invoice").Element("PaymentDetails").Elements("Installment").ToList();
            CollectionAssert.AreEqual(new[] { "2026-12-24", "2027-01-23" }, plazos.Select(p => p.Element("InstallmentDueDate").Value).ToArray());
            CollectionAssert.AreEqual(new[] { "60.50", "60.50" }, plazos.Select(p => p.Element("InstallmentAmount").Value).ToArray());
            ComprobarCuadre(doc, 121M);
        }

        [TestMethod]
        public void Generar_Rectificativa_ClaseOryBloqueCorrectiveConLaOriginal()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, -2, 10M, 0, 21) };
            DatosOrigenFacturae datos = Datos(lineas);
            datos.Factura.NumeroFactura = "RV2600189";
            datos.Factura.Serie = "RV";
            datos.EsRectificativa = true;
            datos.FacturasOriginales = new List<FacturaOriginalFacturae> { new FacturaOriginalFacturae { Serie = "NV", Numero = "2616075", Fecha = new DateTime(2026, 11, 24) } };

            XDocument doc = Xml(datos, out _);

            XElement cabecera = doc.Root.Element("Invoices").Element("Invoice").Element("InvoiceHeader");
            CollectionAssert.AreEqual(new[] { "InvoiceNumber", "InvoiceSeriesCode", "InvoiceDocumentType", "InvoiceClass", "Corrective" }, Nombres(cabecera));
            Assert.AreEqual("OR", cabecera.Element("InvoiceClass").Value);
            XElement corrective = cabecera.Element("Corrective");
            CollectionAssert.AreEqual(new[] { "InvoiceNumber", "InvoiceSeriesCode", "ReasonCode", "ReasonDescription", "TaxPeriod", "CorrectionMethod", "CorrectionMethodDescription" }, Nombres(corrective));
            Assert.AreEqual("2616075", corrective.Element("InvoiceNumber").Value);
            Assert.AreEqual("02", corrective.Element("CorrectionMethod").Value);
            Assert.AreEqual("-20.00", doc.Root.Element("Invoices").Element("Invoice").Element("InvoiceTotals").Element("TotalGrossAmount").Value);
            ComprobarCuadre(doc, -24.20M);
        }

        [TestMethod]
        public void Construir_RectificativaSinOriginal_Falta()
        {
            DatosOrigenFacturae datos = Datos(new List<LinPedidoVta> { Linea(1, -1, 10M, 0, 21) });
            datos.EsRectificativa = true;

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(datos);

            Assert.IsFalse(resultado.Listo);
            Assert.IsTrue(resultado.Faltas.Any(f => f.Contains("no tiene vinculada la factura que rectifica")));
        }

        [TestMethod]
        public void Construir_SinDir3_FaltaConElMensajeDeUsted()
        {
            DatosOrigenFacturae datos = Datos(new List<LinPedidoVta> { Linea(1, 1, 10M, 0, 21) });
            datos.Organismo = null;

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(datos);

            Assert.IsFalse(resultado.Listo);
            CollectionAssert.Contains(resultado.Faltas, ConstructorModeloFacturae.FALTAN_DIR3);
        }

        [TestMethod]
        public void Construir_SinNifDelCliente_Falta()
        {
            DatosOrigenFacturae datos = Datos(new List<LinPedidoVta> { Linea(1, 1, 10M, 0, 21) });
            datos.Factura.Nif = " ";

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(datos);

            Assert.IsTrue(resultado.Faltas.Any(f => f.Contains("no tiene NIF")));
        }

        [TestMethod]
        public void Construir_FormaDePagoSinEquivalencia_Falta()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 1, 10M, 0, 21) };
            DatosOrigenFacturae datos = Datos(lineas, (new DateTime(2026, 12, 1), 12.10M, "XYZ"));

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(datos);

            Assert.IsTrue(resultado.Faltas.Any(f => f.Contains("«XYZ»")));
        }

        [TestMethod]
        public void Construir_VencimientosQueNoSumanElTotal_Falta()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 1, 10M, 0, 21) };
            DatosOrigenFacturae datos = Datos(lineas, (new DateTime(2026, 12, 1), 12.00M, "TRN"));

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(datos);

            Assert.IsTrue(resultado.Faltas.Any(f => f.StartsWith("Los vencimientos suman 12.00")));
        }

        [TestMethod]
        public void Construir_ReferenciaDeMasDe20_Falta()
        {
            DatosOrigenFacturae datos = Datos(new List<LinPedidoVta> { Linea(1, 1, 10M, 0, 21) });
            datos.Referencias.Pedido = "123456789012345678901";

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(datos);

            Assert.IsTrue(resultado.Faltas.Any(f => f.Contains("como mucho 20")));
        }

        [TestMethod]
        public void Construir_LineaDeTextoSinImporte_NoVa()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 1, 10M, 0, 21), Linea(2, 0, 0, 0, 21, producto: null, texto: "ENTREGAR EN CONSERJERÍA", tipoLinea: 0, albaran: null) };

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(Datos(lineas));

            Assert.IsTrue(resultado.Listo);
            Assert.AreEqual(1, resultado.Modelo.Lineas.Count);
        }

        [TestMethod]
        public void Construir_TransferenciaConDosCuentas_VaLaPrimeraYAvisa()
        {
            DatosOrigenFacturae datos = Datos(new List<LinPedidoVta> { Linea(1, 1, 10M, 0, 21) });
            datos.Factura.Vencimientos[0].Iban = IBAN_EMPRESA + Environment.NewLine + "ES79 2100 0813 6101 2345 6789";

            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(datos);

            Assert.AreEqual("ES9121000418450200051332", resultado.Modelo.Vencimientos[0].Iban);
            Assert.IsTrue(resultado.Avisos.Any(a => a.StartsWith("La empresa tiene 2 cuentas")));
        }

        [DataTestMethod]
        [DataRow("TRN", "04")]
        [DataRow("CNF", "04")]
        [DataRow("RCB", "02")]
        [DataRow("EFC", "01")]
        [DataRow("TAR", "19")]
        [DataRow("CHC", null)]
        public void MedioPago_FormasDeNesto(string forma, string esperado)
        {
            Assert.AreEqual(esperado, ConstructorModeloFacturae.MedioPago(forma));
        }

        [DataTestMethod]
        [DataRow("L01280796", true)]
        [DataRow("l0128 0796", true)]
        [DataRow("EA0003214", true)]
        [DataRow("GE0011893", true)]
        [DataRow("LA0007357", true)]
        [DataRow("L0128079", false)]
        [DataRow("012807961", false)]
        [DataRow("LABCDEFGH", false)]
        [DataRow("", false)]
        public void ReglasDir3_Formato(string codigo, bool valido)
        {
            string motivo = ReglasDir3.Validar(codigo, "Oficina contable", out string limpio);

            Assert.AreEqual(valido, motivo == null, motivo);
            if (valido)
            {
                Assert.AreEqual(9, limpio.Length);
                Assert.AreEqual(limpio.ToUpperInvariant(), limpio);
            }
        }

        [TestMethod]
        public void ReglasDir3_MotivoDeUstedConArticulo()
        {
            Assert.AreEqual("Falta el código DIR3 del órgano gestor.", ReglasDir3.Validar(null, "Órgano gestor", out _));
            StringAssert.StartsWith(ReglasDir3.Validar("X1", "Oficina contable", out _), "El código DIR3 de la oficina contable «X1» no es válido");
        }

        [DataTestMethod]
        [DataRow("ESB99999999", "B99999999")]
        [DataRow(" p-2800000-j ", "P2800000J")]
        [DataRow("ES12345678Z", "12345678Z")]
        public void NormalizarNif(string nif, string esperado)
        {
            Assert.AreEqual(esperado, ConstructorModeloFacturae.NormalizarNif(nif));
        }

        [TestMethod]
        public void Generar_Ejemplo_SeEscribeEnLaSalida()
        {
            var lineas = new List<LinPedidoVta> { Linea(1, 4, 12.5M, 0.15M, 21), Linea(2, 1, 6.95M, 0, 21, producto: "624000", texto: "PORTES", tipoLinea: 2, albaran: 732939) };
            ResultadoModeloFacturae resultado = ConstructorModeloFacturae.Construir(Datos(lineas));
            Assert.IsTrue(resultado.Listo, string.Join(" | ", resultado.Faltas));

            string xml = GeneradorFacturae.Generar(resultado.Modelo);

            StringAssert.StartsWith(xml, "<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            Console.WriteLine(xml);
        }
    }
}
