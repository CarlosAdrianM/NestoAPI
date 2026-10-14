using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Stocks.Tendencias;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using static NestoAPI.Tests.Infrastructure.Stocks.Tendencias.SeriesTendencia;

namespace NestoAPI.Tests.Infrastructure.Stocks.Tendencias
{
    /// <summary>NestoAPI#630: el job mensual (calcula, guarda sin pisar, Excel con 4 hojas y correo a los destinatarios).</summary>
    [TestClass]
    public class PropuestasStockJobsServiceTests
    {
        private IRepositorioTendencias repositorio;
        private IServicioCorreoElectronico correo;
        private List<Exception> errores;
        private Dictionary<string, string> parametros;
        private List<(string asunto, string[] para, string fichero, byte[] excel, string cuerpo)> enviados;

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioTendencias>();
            correo = A.Fake<IServicioCorreoElectronico>();
            errores = new List<Exception>();
            parametros = new Dictionary<string, string>();
            enviados = new List<(string, string[], string, byte[], string)>();
            List<DatosProductoTendencia> productos = new List<DatosProductoTendencia>
            {
                Producto("1", ventas: Constante(30), stock: 1000),
                Producto("2", ventas: Constante(30), stock: 90)
            };
            DatosProductoTendencia nuevo = Producto("3", ventas: Ultimos(10, 10), stock: 30);
            nuevo.FechaPrimeraVenta = new DateTime(2026, 8, 3);
            productos.Add(nuevo);
            _ = A.CallTo(() => repositorio.LeerProductos()).Returns(productos);
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(true);
            _ = A.CallTo(() => repositorio.GuardarPropuestas(A<DateTime>._, A<IEnumerable<ResultadoTendencia>>._, A<DateTime>._))
                .ReturnsLazily((DateTime m, IEnumerable<ResultadoTendencia> filas, DateTime f) => filas.Count());
            _ = A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage mail) =>
            {
                Attachment adjunto = mail.Attachments.Single();
                using (MemoryStream ms = new MemoryStream())
                {
                    adjunto.ContentStream.CopyTo(ms);
                    enviados.Add((mail.Subject, mail.To.Select(t => t.Address).ToArray(), adjunto.Name, ms.ToArray(), mail.Body));
                }
                return true;
            });
        }

        private PropuestasStockJobsService Servicio()
            => new PropuestasStockJobsService(() => repositorio, correo, c => parametros.TryGetValue(c, out string v) ? v : null,
                () => new DateTime(2026, 10, 1, 4, 30, 0), errores.Add);

        [TestMethod]
        public void MesCerrado_EsElAnterior()
        {
            Assert.AreEqual(new DateTime(2026, 9, 1), PropuestasStockJobsService.MesCerrado(new DateTime(2026, 10, 1, 4, 30, 0)));
            Assert.AreEqual(new DateTime(2025, 12, 1), PropuestasStockJobsService.MesCerrado(new DateTime(2026, 1, 1)));
            Assert.AreEqual(new DateTime(2026, 10, 1), PropuestasStockJobsService.LeerMes("2026-10"));
            Assert.IsNull(PropuestasStockJobsService.LeerMes("10/2026"));
        }

        [TestMethod]
        public void Ejecutar_GuardaYMandaElExcelACarlosPorDefecto()
        {
            ResultadoPropuestasStock r = Servicio().Ejecutar(MesReferencia, "prueba");

            Assert.IsNotNull(r);
            A.CallTo(() => repositorio.GuardarPropuestas(MesReferencia, A<IEnumerable<ResultadoTendencia>>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, enviados.Count);
            CollectionAssert.AreEqual(new[] { "carlosadrian@nuevavision.es" }, enviados[0].para);
            Assert.AreEqual("Propuestas de stock de septiembre 2026", enviados[0].asunto);
            Assert.AreEqual("PropuestasStock_2026-09.xlsx", enviados[0].fichero);
            StringAssert.Contains(enviados[0].cuerpo, "3 filas nuevas en PropuestasStock");
            Assert.AreEqual(0, errores.Count);

            CollectionAssert.AreEqual(new[] { "Top 50 por exceso", "Todos los productos", "Nuevos (fase A)", "Calibración" }, Hojas(enviados[0].excel));
            Assert.AreEqual("Producto", Celda(enviados[0].excel, "Top 50 por exceso", "A3"));
            Assert.AreEqual("1", Celda(enviados[0].excel, "Top 50 por exceso", "A4")); // el de más exceso, primero
            Assert.AreEqual("3", Celda(enviados[0].excel, "Nuevos (fase A)", "A4"));
        }

        [TestMethod]
        public void Ejecutar_DestinatariosDeConfiguracion()
        {
            parametros[ParametrosTendencias.CLAVE_DESTINATARIOS] = "a@nuevavision.es; b@nuevavision.es";

            _ = Servicio().Ejecutar(MesReferencia, "prueba");

            CollectionAssert.AreEqual(new[] { "a@nuevavision.es", "b@nuevavision.es" }, enviados[0].para);
        }

        [TestMethod]
        public void Ejecutar_SinTabla_NoGuardaPeroMandaElExcelAvisando()
        {
            _ = A.CallTo(() => repositorio.ExisteTabla()).Returns(false);

            _ = Servicio().Ejecutar(MesReferencia, "prueba");

            A.CallTo(() => repositorio.GuardarPropuestas(A<DateTime>._, A<IEnumerable<ResultadoTendencia>>._, A<DateTime>._)).MustNotHaveHappened();
            Assert.AreEqual(1, enviados.Count);
            StringAssert.Contains(enviados[0].cuerpo, "Issue630_PropuestasStock.sql");
        }

        [TestMethod]
        public void Ejecutar_SiFallaGuardar_ElExcelSaleIgual()
        {
            _ = A.CallTo(() => repositorio.GuardarPropuestas(A<DateTime>._, A<IEnumerable<ResultadoTendencia>>._, A<DateTime>._))
                .Throws(new InvalidOperationException("se denegó el permiso INSERT"));

            _ = Servicio().Ejecutar(MesReferencia, "prueba");

            Assert.AreEqual(1, enviados.Count);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void Ejecutar_SiFallaLeerLosProductos_NoLanzaYVaAElmah()
        {
            _ = A.CallTo(() => repositorio.LeerProductos()).Throws(new TimeoutException("caído"));

            Assert.IsNull(Servicio().Ejecutar(MesReferencia, "prueba"));
            Assert.AreEqual(1, errores.Count);
            Assert.AreEqual(0, enviados.Count);
        }

        [TestMethod]
        public void Excel_TopLimitadoAFilasTop()
        {
            ResultadoPropuestasStock r = new ResultadoPropuestasStock
            {
                Mes = MesReferencia,
                Parametros = new ParametrosTendencias { FilasTop = 2 },
                Productos = Enumerable.Range(1, 5).Select(i => new ResultadoTendencia { Producto = i.ToString(), ExcesoValorado = i, Fase = FasesProducto.MADURO }).ToList()
            };

            byte[] excel = new GeneradorExcelPropuestasStock().Generar(r);

            Assert.AreEqual("5", Celda(excel, "Top 2 por exceso", "A4"));
            Assert.AreEqual("4", Celda(excel, "Top 2 por exceso", "A5"));
            Assert.IsNull(Celda(excel, "Top 2 por exceso", "A6"));
            Assert.AreEqual("1", Celda(excel, "Todos los productos", "A8"));
        }

        private static string[] Hojas(byte[] excel)
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(new MemoryStream(excel), false))
            {
                return doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Select(h => h.Name.Value).ToArray();
            }
        }

        /// <summary>Texto de una celda (cadena compartida, en línea o número), o null si no existe.</summary>
        private static string Celda(byte[] excel, string hoja, string referencia)
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(new MemoryStream(excel), false))
            {
                Sheet sheet = doc.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Single(h => h.Name.Value == hoja);
                WorksheetPart parte = (WorksheetPart)doc.WorkbookPart.GetPartById(sheet.Id.Value);
                Cell celda = parte.Worksheet.Descendants<Cell>().FirstOrDefault(c => c.CellReference?.Value == referencia);
                if (celda == null)
                {
                    return null;
                }
                if (celda.DataType != null && celda.DataType.Value == CellValues.SharedString)
                {
                    return doc.WorkbookPart.SharedStringTablePart.SharedStringTable.Elements<SharedStringItem>()
                        .ElementAt(int.Parse(celda.CellValue.Text)).InnerText;
                }
                if (celda.DataType != null && celda.DataType.Value == CellValues.InlineString)
                {
                    return celda.InnerText;
                }
                return celda.CellValue?.Text;
            }
        }
    }
}
