using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure.Informes
{
    /// <summary>NestoAPI#658: el cálculo del informe de lo facturado en tienda (cursos incluidos) y su Excel.</summary>
    [TestClass]
    public class GestorFacturacionTiendaTests
    {
        [TestMethod]
        public void SemanaCerradaAnterior_UnLunes_DaLaSemanaPasadaDeLunesADomingo()
        {
            (DateTime desde, DateTime hasta) = GestorFacturacionTienda.SemanaCerradaAnterior(new DateTime(2026, 10, 19, 9, 30, 0));

            Assert.AreEqual(new DateTime(2026, 10, 12), desde);
            Assert.AreEqual(new DateTime(2026, 10, 18), hasta);
        }

        [TestMethod]
        public void SemanaCerradaAnterior_UnDomingo_NoCuentaLaSemanaEnCurso()
        {
            (DateTime desde, DateTime hasta) = GestorFacturacionTienda.SemanaCerradaAnterior(new DateTime(2026, 10, 18, 20, 0, 0));

            Assert.AreEqual(new DateTime(2026, 10, 5), desde);
            Assert.AreEqual(new DateTime(2026, 10, 11), hasta);
        }

        [TestMethod]
        public void ResolverPeriodo_SoloDesde_EsLaSemanaQueEmpiezaEseDia()
        {
            string error = GestorFacturacionTienda.ResolverPeriodo(new DateTime(2026, 10, 1, 15, 0, 0), null, DateTime.Today, out DateTime desde, out DateTime hasta);

            Assert.IsNull(error);
            Assert.AreEqual(new DateTime(2026, 10, 1), desde);
            Assert.AreEqual(new DateTime(2026, 10, 7), hasta);
        }

        [TestMethod]
        public void ResolverPeriodo_SoloHasta_EsLaSemanaQueAcabaEseDia()
        {
            string error = GestorFacturacionTienda.ResolverPeriodo(null, new DateTime(2026, 10, 7), DateTime.Today, out DateTime desde, out DateTime hasta);

            Assert.IsNull(error);
            Assert.AreEqual(new DateTime(2026, 10, 1), desde);
            Assert.AreEqual(new DateTime(2026, 10, 7), hasta);
        }

        [TestMethod]
        public void ResolverPeriodo_DesdePosteriorAHasta_DaError()
        {
            string error = GestorFacturacionTienda.ResolverPeriodo(new DateTime(2026, 10, 8), new DateTime(2026, 10, 7), DateTime.Today, out _, out _);

            Assert.IsNotNull(error);
        }

        [TestMethod]
        public void ResolverPeriodo_MasDeUnAnno_DaError()
        {
            string error = GestorFacturacionTienda.ResolverPeriodo(new DateTime(2025, 1, 1), new DateTime(2026, 1, 2), DateTime.Today, out _, out _);
            string unAnnoJusto = GestorFacturacionTienda.ResolverPeriodo(new DateTime(2025, 1, 1), new DateTime(2026, 1, 1), DateTime.Today, out _, out _);

            Assert.IsNotNull(error, "367 días");
            Assert.IsNull(unAnnoJusto, "366 días (un año con los dos extremos) sí vale");
        }

        [TestMethod]
        public void NormalizarTienda_AceptaReiYAlcSinImportarMayusculasNiEspacios()
        {
            Assert.AreEqual("REI", GestorFacturacionTienda.NormalizarTienda(" rei "));
            Assert.AreEqual("ALC", GestorFacturacionTienda.NormalizarTienda("ALC"));
            Assert.IsNull(GestorFacturacionTienda.NormalizarTienda("ALG"));
            Assert.IsNull(GestorFacturacionTienda.NormalizarTienda(null));
        }

        [TestMethod]
        public void Sql_FiltraPorElAlmacenDeLasLineasNoPorLaDelegacionYLeeSinBloquear()
        {
            string sql = GestorFacturacionTienda.SQL_FACTURACION_TIENDA;

            StringAssert.Contains(sql, "l.[Almacén] = @p4");
            Assert.IsFalse(sql.Contains("[Delegación]"), "Decisión 20/10/26: lo servido desde ALG con delegación de la tienda no cuenta");
            StringAssert.Contains(sql, "LinPedidoVta l WITH (NOLOCK)");
            StringAssert.Contains(sql, "CabFacturaVta f WITH (NOLOCK)");
            Assert.IsFalse(sql.ToUpperInvariant().Contains("UPDATE") || sql.ToUpperInvariant().Contains("INSERT") || sql.ToUpperInvariant().Contains("DELETE"));
            Assert.AreEqual(6, GestorFacturacionTienda.Parametros("REI", DateTime.Today, DateTime.Today).Length);
        }

        [TestMethod]
        public void Sql_ElComercialEsElVendedorDeLaCabeceraDeLaFactura()
        {
            string sql = GestorFacturacionTienda.SQL_FACTURACION_TIENDA;

            StringAssert.Contains(sql, "RTRIM(f.Vendedor) AS Vendedor");
            StringAssert.Contains(sql, "v.[Número] = f.Vendedor");
            Assert.IsFalse(sql.Contains("c.Vendedor"), "Decisión 20/10/26: no el vendedor de la ficha");
            Assert.IsFalse(sql.Contains("VendedoresClienteGrupoProducto"), "Decisión 20/10/26: no el de peluquería");
        }

        [TestMethod]
        public void Construir_UnClienteConFacturasDeDosComerciales_SaleUnaFilaPorComercial()
        {
            List<FacturaTiendaDTO> filas = Filas();
            filas[2].Vendedor = "JE";
            filas[2].NombreVendedor = "Jesús Ejemplo";

            FacturacionTiendaDTO informe = GestorFacturacionTienda.Construir(filas, "REI", new DateTime(2026, 10, 12), new DateTime(2026, 10, 18));

            Assert.AreEqual(3, informe.Clientes.Count);
            Assert.AreEqual(2, informe.NumeroClientes, "Los clientes se cuentan una vez aunque salgan con dos comerciales");
            Assert.AreEqual(300m, informe.Clientes.Single(c => c.Cliente == "15191" && c.Vendedor == "PA").BaseImponible);
            Assert.AreEqual(-20m, informe.Clientes.Single(c => c.Cliente == "15191" && c.Vendedor == "JE").BaseImponible);
        }

        [TestMethod]
        public void Construir_LimpiaTextosMarcaCursosYCalculaTotales()
        {
            FacturacionTiendaDTO informe = GestorFacturacionTienda.Construir(Filas(), "REI", new DateTime(2026, 10, 12), new DateTime(2026, 10, 18));

            Assert.AreEqual("REI", informe.Tienda);
            Assert.AreEqual(3, informe.NumeroFacturas);
            Assert.AreEqual(2, informe.NumeroClientes);
            Assert.AreEqual(150m + 300m - 20m, informe.TotalBaseImponible);
            Assert.AreEqual(181.5m + 363m - 24.2m, informe.Total);
            Assert.AreEqual(300m, informe.TotalBaseImponibleCursos);

            FacturaTiendaDTO curso = informe.Facturas.Single(f => f.Factura == "CV2600010");
            Assert.IsTrue(curso.EsCurso);
            Assert.AreEqual("15191", curso.Cliente);
            Assert.AreEqual("Calle Mayor, 1", curso.Direccion);
            Assert.AreEqual("PA", curso.Vendedor);
            Assert.IsNull(informe.Facturas.Single(f => f.Factura == "NV2600100").NombreVendedor, "Un nombre en blanco sale como null");
            Assert.IsFalse(informe.Facturas.Single(f => f.Factura == "NV2600100").EsCurso);
        }

        [TestMethod]
        public void Construir_AgrupaPorClienteYOrdenaDeMasAMenos()
        {
            FacturacionTiendaDTO informe = GestorFacturacionTienda.Construir(Filas(), "REI", new DateTime(2026, 10, 12), new DateTime(2026, 10, 18));

            ClienteFacturacionTiendaDTO primero = informe.Clientes[0];
            Assert.AreEqual("15191", primero.Cliente);
            Assert.AreEqual(2, primero.NumeroFacturas);
            Assert.AreEqual(280m, primero.BaseImponible, "La rectificativa resta");
            Assert.AreEqual("PA", primero.Vendedor);
            Assert.AreEqual("24680", informe.Clientes[1].Cliente);
        }

        [TestMethod]
        public void Construir_SinFacturas_DaElInformeVacioConTotalesACero()
        {
            FacturacionTiendaDTO informe = GestorFacturacionTienda.Construir(null, "ALC", new DateTime(2026, 10, 12), new DateTime(2026, 10, 18));

            Assert.AreEqual(0, informe.NumeroFacturas);
            Assert.AreEqual(0m, informe.Total);
            Assert.AreEqual(0, informe.Clientes.Count);
        }

        [TestMethod]
        public void GenerarExcel_TieneLasDosHojasConLosClientesYElTotal()
        {
            FacturacionTiendaDTO informe = GestorFacturacionTienda.Construir(Filas(), "REI", new DateTime(2026, 10, 12), new DateTime(2026, 10, 18));
            GeneradorExcelFacturacionTienda generador = new GeneradorExcelFacturacionTienda();

            byte[] bytes = generador.GenerarBytes(informe);

            Assert.AreEqual(0x50, bytes[0]);
            Assert.AreEqual(0x4B, bytes[1]);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(ms, false))
            {
                List<string> hojas = doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Select(s => s.Name.Value).ToList();
                CollectionAssert.AreEqual(new[] { "Facturas", "Por cliente" }, hojas);
                List<string> textos = doc.WorkbookPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>().Select(i => i.InnerText).ToList();
                CollectionAssert.Contains(textos, "Peluquería Ejemplo");
                CollectionAssert.Contains(textos, "PA - Paloma Ejemplo");
                CollectionAssert.Contains(textos, "CV (curso)");
                CollectionAssert.Contains(textos, "Total (3 facturas):");
            }
            Assert.AreEqual("FacturacionTienda_REI_20261012_20261018.xlsx", GeneradorExcelFacturacionTienda.NombreFichero(informe));
            Assert.AreEqual(GeneradorExcelFacturacionTienda.CONTENT_TYPE, generador.GenerarExcel(informe).Headers.ContentType.MediaType);
        }

        internal static List<FacturaTiendaDTO> Filas() => new List<FacturaTiendaDTO>
        {
            new FacturaTiendaDTO
            {
                Empresa = "1  ", Factura = "NV2600100 ", Fecha = new DateTime(2026, 10, 13), Serie = "NV", Cliente = "24680     ", Contacto = "0  ",
                Nombre = "Peluquería Ejemplo  ", Direccion = "Avenida Ficticia, 7", CodigoPostal = "28100", Poblacion = "Alcobendas",
                Vendedor = "JE ", NombreVendedor = "   ",
                BaseImponible = 150m, Total = 181.5m
            },
            new FacturaTiendaDTO
            {
                Empresa = "1", Factura = "CV2600010", Fecha = new DateTime(2026, 10, 14), Serie = "CV ", Cliente = "15191", Contacto = "0",
                Nombre = "Centro Estético Inventado", Direccion = "Calle Mayor, 1 ", CodigoPostal = "28001", Poblacion = "Madrid",
                Vendedor = "PA", NombreVendedor = "Paloma Ejemplo", BaseImponible = 300m, Total = 363m
            },
            new FacturaTiendaDTO
            {
                Empresa = "1", Factura = "RV2600005", Fecha = new DateTime(2026, 10, 16), Serie = "RV", Cliente = "15191", Contacto = "0",
                Nombre = "Centro Estético Inventado", Direccion = "Calle Mayor, 1", CodigoPostal = "28001", Poblacion = "Madrid",
                Vendedor = "PA", NombreVendedor = "Paloma Ejemplo", BaseImponible = -20m, Total = -24.2m
            }
        };
    }
}
