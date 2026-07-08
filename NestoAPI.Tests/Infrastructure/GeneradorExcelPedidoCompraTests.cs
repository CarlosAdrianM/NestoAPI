using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Tests del generador Excel (.xlsx, ClosedXML) de la "ORDEN DE COMPRA". Verifican que se produce
    /// un .xlsx válido (firma ZIP "PK") en los escenarios clave. Al generar de verdad el libro, estos
    /// tests ejercen ClosedXML/OpenXml en runtime: cazan cualquier problema de carga de ensamblado o
    /// binding redirect (System.IO.Packaging, etc.) antes de llegar a producción.
    /// </summary>
    [TestClass]
    public class GeneradorExcelPedidoCompraTests
    {
        private GeneradorExcelPedidoCompra _generador;

        [TestInitialize]
        public void Setup()
        {
            // Sin logo (null) -> no se añade la imagen del logo; el sello sí (recurso embebido).
            _generador = new GeneradorExcelPedidoCompra(() => null);
        }

        [TestMethod]
        public void GenerarExcel_PedidoValorado_DevuelveXlsxValido()
        {
            byte[] bytes = GenerarBytes(PedidoEjemplo(valorado: true));
            ComprobarFirmaXlsx(bytes);
        }

        [TestMethod]
        public void GenerarExcel_PedidoNoValorado_DevuelveXlsxValido()
        {
            byte[] bytes = GenerarBytes(PedidoEjemplo(valorado: false));
            ComprobarFirmaXlsx(bytes);
        }

        [TestMethod]
        public void GenerarExcel_SinLineas_DevuelveXlsxValido()
        {
            var pedido = PedidoEjemplo(valorado: true);
            pedido.Lineas = new List<LineaPedidoCompraInformeDTO>();
            ComprobarFirmaXlsx(GenerarBytes(pedido));
        }

        [TestMethod]
        public void GenerarExcel_LineasNull_NoLanza()
        {
            var pedido = PedidoEjemplo(valorado: true);
            pedido.Lineas = null;
            ComprobarFirmaXlsx(GenerarBytes(pedido));
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void GenerarExcel_PedidoNull_Lanza()
        {
            _generador.GenerarExcel(null);
        }

        [TestMethod]
        public void GenerarExcel_ContieneTituloProveedorYNotas()
        {
            // Abre el .xlsx real y comprueba que aparecen el título, datos del proveedor y la nota de
            // los palets (la que se había perdido en el PDF), garantizando que el contenido es completo.
            byte[] bytes = GenerarBytes(PedidoEjemplo(valorado: true));
            List<string> textos = LeerTextosCompartidos(bytes);

            Assert.IsTrue(textos.Any(t => t.Contains("ORDEN DE COMPRA")), "Falta el título");
            Assert.IsTrue(textos.Any(t => t.Contains("DISTRIBUCIONES EJEMPLO")), "Falta el proveedor");
            Assert.IsTrue(textos.Any(t => t.Contains("palets")), "Falta la nota de los palets");
            Assert.IsTrue(textos.Any(t => t.Contains("Muchas gracias")), "Falta el cierre");
        }

        [TestMethod]
        public void GenerarExcel_UnidadDeMedida_VaEnElTamannoNoEnLaCantidad()
        {
            // NestoAPI#269: producto de 100 ml del que se piden 1000 uds. La unidad ("ml") califica al
            // Tamaño ("100 ml"), no a la Cantidad. Además la Cantidad debe quedar como número puro
            // (celda numérica -> no aparece en la tabla de cadenas compartidas).
            var pedido = PedidoEjemplo(valorado: true);
            pedido.Lineas = new List<LineaPedidoCompraInformeDTO>
            {
                new LineaPedidoCompraInformeDTO
                {
                    SuReferencia = "REF", NuestraReferencia = "99999",
                    Descripcion = "Producto de cien mililitros", Tamanno = 100, UnidadMedida = "ml",
                    Cantidad = 1000, PrecioUnitario = 1m, SumaDescuentos = 0m, BaseImponible = 1000m
                }
            };

            List<string> textos = LeerTextosCompartidos(GenerarBytes(pedido));

            Assert.IsTrue(textos.Any(t => t == "100 ml"), "El Tamaño debe llevar la unidad de medida (\"100 ml\")");
            Assert.IsFalse(textos.Any(t => t.Contains("1000 ml")), "La Cantidad no debe llevar la unidad de medida (era el bug)");
            Assert.IsFalse(textos.Any(t => t == "1000"), "La Cantidad debe ser numérica, no texto");
        }

        private byte[] GenerarBytes(PedidoCompraInformeDTO pedido)
        {
            var contenido = _generador.GenerarExcel(pedido);
            return contenido.ReadAsByteArrayAsync().Result;
        }

        private static PedidoCompraInformeDTO PedidoEjemplo(bool valorado)
        {
            return new PedidoCompraInformeDTO
            {
                Id = 219485,
                Proveedor = "50",
                Nombre = "DISTRIBUCIONES EJEMPLO, S.L.",
                Direccion = "C/ del Comercio, 25",
                CodigoPostal = "08907",
                Poblacion = "L'HOSPITALET DE LLOBREGAT",
                Provincia = "BARCELONA",
                Telefono = "933003278",
                Cif = "37741263B",
                Fecha = new DateTime(2026, 6, 19),
                PedidoValorado = valorado,
                Lineas = new List<LineaPedidoCompraInformeDTO>
                {
                    new LineaPedidoCompraInformeDTO
                    {
                        SuReferencia = "REF-A", NuestraReferencia = "38697",
                        Descripcion = "Crema hidratante facial", Tamanno = 250, UnidadMedida = "Ud",
                        Cantidad = 12, PrecioUnitario = 8.50m, SumaDescuentos = 0.05m, BaseImponible = 96.90m
                    },
                    new LineaPedidoCompraInformeDTO
                    {
                        SuReferencia = "REF-B", NuestraReferencia = "12345",
                        Descripcion = "Champú anticaspa 500 ml", Tamanno = 500, UnidadMedida = "Ud",
                        Cantidad = 6, PrecioUnitario = 4.20m, SumaDescuentos = 0m, BaseImponible = 25.20m
                    }
                }
            };
        }

        // Los .xlsx son ZIP: empiezan por "PK" (0x50 0x4B).
        private static void ComprobarFirmaXlsx(byte[] bytes)
        {
            Assert.IsTrue(bytes.Length > 0, "El Excel debe tener contenido");
            Assert.AreEqual(0x50, bytes[0]);
            Assert.AreEqual(0x4B, bytes[1]);
        }

        private static List<string> LeerTextosCompartidos(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var doc = SpreadsheetDocument.Open(ms, false))
            {
                var sst = doc.WorkbookPart.SharedStringTablePart?.SharedStringTable;
                return sst == null
                    ? new List<string>()
                    : sst.Elements<SharedStringItem>().Select(i => i.InnerText).ToList();
            }
        }
    }
}
