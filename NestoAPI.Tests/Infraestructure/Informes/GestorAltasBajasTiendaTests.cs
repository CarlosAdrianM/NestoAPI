using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Models.Informes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Informes
{
    /// <summary>NestoAPI#886: LA REGLA de las altas y bajas de stock de las tiendas, el correo, el Excel y el job mensual.</summary>
    [TestClass]
    public class GestorAltasBajasTiendaTests
    {
        private static readonly DateTime DESDE = new DateTime(2026, 11, 1);
        private static readonly DateTime HASTA = new DateTime(2026, 11, 30);

        private static FilaAltaBajaTiendaDTO Fila(string producto, int cantidad, DateTime fechaHora, string diario = "_RegInvent",
            string texto = "Inventario", decimal? pvp = 10m, string usuario = "NUEVAVISION\\Paloma", int orden = 1)
            => new FilaAltaBajaTiendaDTO
            {
                Empresa = "1  ",
                NumeroOrden = orden,
                Producto = producto + "   ",
                Almacen = "REI",
                Fecha = fechaHora.Date,
                FechaModificacion = fechaHora,
                Cantidad = cantidad,
                Diario = diario + " ",
                Texto = texto,
                Usuario = usuario,
                Nombre = "NOMBRE " + producto + "   ",
                Familia = "Eva Visnu  ",
                Pvp = pvp
            };

        [TestMethod]
        public void Construir_TotalesDeAltasBajasYNetoAPvp()
        {
            AltasBajasTiendaDTO informe = GestorAltasBajasTienda.Construir(new[]
            {
                Fila("100", 3, new DateTime(2026, 11, 5, 10, 0, 0), pvp: 10m),
                Fila("200", -2, new DateTime(2026, 11, 6, 10, 0, 0), pvp: 7.5m),
                Fila("300", -1, new DateTime(2026, 11, 7, 10, 0, 0), pvp: 4m)
            }, "rei", DESDE, HASTA);

            Assert.AreEqual("REI", informe.Almacen);
            Assert.AreEqual("Reina", informe.NombreTienda);
            Assert.AreEqual(3, informe.UnidadesAlta);
            Assert.AreEqual(30m, informe.ValorPvpAlta);
            Assert.AreEqual(3, informe.UnidadesBaja);
            Assert.AreEqual(19m, informe.ValorPvpBaja);
            Assert.AreEqual(0, informe.UnidadesNeto);
            Assert.AreEqual(11m, informe.ValorPvpNeto);
            Assert.AreEqual(-15m, informe.Movimientos[1].ValorPvp);
            Assert.AreEqual(7.5m, informe.Movimientos[1].PvpUnitario);
        }

        [TestMethod]
        public void Construir_ApareceYDesaparece_SoloLosQueTienenAltaYBaja()
        {
            AltasBajasTiendaDTO informe = GestorAltasBajasTienda.Construir(new[]
            {
                Fila("100", -2, new DateTime(2026, 11, 5, 10, 0, 0), "_ErrRepo", "Reposición 81028: «No está» al enviar (Paloma)"),
                Fila("100", 1, new DateTime(2026, 11, 20, 10, 0, 0)),
                Fila("200", -1, new DateTime(2026, 11, 6, 10, 0, 0)),
                Fila("300", 4, new DateTime(2026, 11, 7, 10, 0, 0))
            }, "REI", DESDE, HASTA);

            Assert.AreEqual(1, informe.ApareceYDesaparece.Count);
            ProductoApareceDesapareceDTO p = informe.ApareceYDesaparece[0];
            Assert.AreEqual("100", p.Producto);
            Assert.AreEqual("NOMBRE 100", p.Nombre);
            Assert.AreEqual(1, p.UnidadesAlta);
            Assert.AreEqual(2, p.UnidadesBaja);
            Assert.AreEqual(-1, p.UnidadesNeto);
            Assert.AreEqual(-10m, p.ValorPvpNeto);
        }

        [TestMethod]
        public void Motivo_PorDiarioYTexto()
        {
            Assert.AreEqual("Inventario", GestorAltasBajasTienda.Motivo("_RegInvent ", "lo que sea"));
            Assert.AreEqual("«No está» al enviar la reposición 81028",
                GestorAltasBajasTienda.Motivo("_ErrRepo", "Reposición 81028: «No está» al enviar (Paloma)"));
            Assert.AreEqual("«No está» al enviar la reposición 81028",
                GestorAltasBajasTienda.Motivo("_ErrRepo", "Reposición 81028: «No está» al env"));
            Assert.AreEqual("Diferencia de la reposición 81463",
                GestorAltasBajasTienda.Motivo("_ErrRepo", "Diferencia de reposición 81463 (recibido en ALG)"));
            Assert.AreEqual("Regularización a mano", GestorAltasBajasTienda.Motivo("_ErrRepo", "  Regularización a mano  "));
            Assert.AreEqual("_ErrRepo", GestorAltasBajasTienda.Motivo("_ErrRepo", null));
        }

        [TestMethod]
        public void Construir_PvpNulo_ValeCero()
        {
            AltasBajasTiendaDTO informe = GestorAltasBajasTienda.Construir(new[]
            {
                Fila("100", -3, new DateTime(2026, 11, 5, 10, 0, 0), pvp: null)
            }, "ALC", DESDE, HASTA);

            Assert.AreEqual(0m, informe.Movimientos[0].PvpUnitario);
            Assert.AreEqual(0m, informe.Movimientos[0].ValorPvp);
            Assert.AreEqual(0m, informe.ValorPvpBaja);
            Assert.AreEqual(3, informe.UnidadesBaja);
            Assert.AreEqual("Alcobendas", informe.NombreTienda);
        }

        [TestMethod]
        public void Construir_UsuarioSinDominioYTextosLimpios()
        {
            AltasBajasTiendaDTO informe = GestorAltasBajasTienda.Construir(new[]
            {
                Fila("100", 1, new DateTime(2026, 11, 5, 10, 0, 0), usuario: "NUEVAVISION\\Paloma  "),
                Fila("200", 1, new DateTime(2026, 11, 6, 10, 0, 0), usuario: "Ariadna (No está)")
            }, "REI", DESDE, HASTA);

            Assert.AreEqual("Paloma", informe.Movimientos[0].Usuario);
            Assert.AreEqual("Ariadna (No está)", informe.Movimientos[1].Usuario);
            Assert.AreEqual("100", informe.Movimientos[0].Producto);
            Assert.AreEqual("Eva Visnu", informe.Movimientos[0].Familia);
            Assert.IsNull(GestorAltasBajasTienda.UsuarioSinDominio("  "));
        }

        [TestMethod]
        public void Construir_OrdenaPorFechaYHoraYQuitaLasDeCero()
        {
            AltasBajasTiendaDTO informe = GestorAltasBajasTienda.Construir(new[]
            {
                Fila("300", 1, new DateTime(2026, 11, 20, 9, 0, 0)),
                Fila("200", 1, new DateTime(2026, 11, 5, 18, 0, 0)),
                Fila("100", 1, new DateTime(2026, 11, 5, 9, 0, 0)),
                Fila("400", 0, new DateTime(2026, 11, 1, 9, 0, 0))
            }, "REI", DESDE, HASTA);

            CollectionAssert.AreEqual(new[] { "100", "200", "300" }, informe.Movimientos.Select(m => m.Producto).ToArray());
        }

        [TestMethod]
        public void Construir_SinFilas_InformeVacio()
        {
            AltasBajasTiendaDTO informe = GestorAltasBajasTienda.Construir(null, "REI", DESDE, HASTA);

            Assert.AreEqual(0, informe.Movimientos.Count);
            Assert.AreEqual(0, informe.ApareceYDesaparece.Count);
            Assert.AreEqual(0m, informe.ValorPvpNeto);
        }

        [TestMethod]
        public void MesAnterior_NoviembreDaOctubreYEneroDaDiciembreDelAnioAnterior()
        {
            Assert.AreEqual((new DateTime(2026, 10, 1), new DateTime(2026, 10, 31)), GestorAltasBajasTienda.MesAnterior(new DateTime(2026, 11, 23, 9, 0, 0)));
            Assert.AreEqual((new DateTime(2026, 12, 1), new DateTime(2026, 12, 31)), GestorAltasBajasTienda.MesAnterior(new DateTime(2027, 1, 1, 8, 15, 0)));
            Assert.AreEqual((new DateTime(2028, 2, 1), new DateTime(2028, 2, 29)), GestorAltasBajasTienda.MesAnterior(new DateTime(2028, 3, 1)));
        }

        [TestMethod]
        public void ResolverPeriodo_ValidaFechas()
        {
            DateTime hoy = new DateTime(2026, 11, 23);
            Assert.IsNull(GestorAltasBajasTienda.ResolverPeriodo(null, null, hoy, out DateTime d, out DateTime h));
            Assert.AreEqual(new DateTime(2026, 10, 1), d);
            Assert.AreEqual(new DateTime(2026, 10, 31), h);

            Assert.IsNull(GestorAltasBajasTienda.ResolverPeriodo(new DateTime(2026, 2, 10), null, hoy, out d, out h));
            Assert.AreEqual(new DateTime(2026, 2, 28), h);
            Assert.IsNull(GestorAltasBajasTienda.ResolverPeriodo(null, new DateTime(2026, 2, 10), hoy, out d, out h));
            Assert.AreEqual(new DateTime(2026, 2, 1), d);

            Assert.IsNotNull(GestorAltasBajasTienda.ResolverPeriodo(new DateTime(2026, 11, 30), new DateTime(2026, 11, 1), hoy, out _, out _));
            Assert.IsNotNull(GestorAltasBajasTienda.ResolverPeriodo(new DateTime(2025, 1, 1), new DateTime(2026, 1, 2), hoy, out _, out _));
            Assert.IsNull(GestorAltasBajasTienda.ResolverPeriodo(new DateTime(2025, 1, 1), new DateTime(2026, 1, 1), hoy, out _, out _)); // 366 días
        }

        [TestMethod]
        public void SinCoste_NingunDtoTienePropiedadDeCoste()
        {
            foreach (Type tipo in new[] { typeof(FilaAltaBajaTiendaDTO), typeof(MovimientoAltaBajaTiendaDTO), typeof(ProductoApareceDesapareceDTO),
                typeof(AltasBajasTiendaDTO), typeof(CorreoAltasBajasTiendaDTO) })
            {
                foreach (var propiedad in tipo.GetProperties())
                {
                    Assert.IsFalse(propiedad.Name.IndexOf("Coste", StringComparison.OrdinalIgnoreCase) >= 0, $"{tipo.Name}.{propiedad.Name}");
                    Assert.IsFalse(propiedad.Name.IndexOf("Importe", StringComparison.OrdinalIgnoreCase) >= 0, $"{tipo.Name}.{propiedad.Name}");
                }
            }
            StringAssert.DoesNotMatch(GestorAltasBajasTienda.SQL_ALTAS_BAJAS_TIENDA, new System.Text.RegularExpressions.Regex(@"Coste|Importe|PrecioMedio",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        }

        private static AltasBajasTiendaDTO InformeEjemplo(int lineas = 2) => GestorAltasBajasTienda.Construir(
            Enumerable.Range(1, lineas).Select(i => Fila((100 + i).ToString(), i % 2 == 0 ? -1 : 1, new DateTime(2026, 11, 2).AddMinutes(i), orden: i)),
            "REI", DESDE, HASTA);

        [TestMethod]
        public void Correo_TituloFraseTotalesYCopiaACompras()
        {
            AltasBajasTiendaDTO informe = GestorAltasBajasTienda.Construir(new[]
            {
                Fila("100", -1, new DateTime(2026, 11, 3, 10, 0, 0), "_ErrRepo", "Reposición 81028: «No está» al enviar (Paloma)"),
                Fila("100", 1, new DateTime(2026, 11, 21, 10, 0, 0))
            }, "REI", DESDE, HASTA);
            using (MailMessage mail = CorreoAltasBajasTienda.Construir(informe, new byte[] { 1 }, "tienda@nuevavision.es"))
            {
                Assert.AreEqual("Altas y bajas de stock de Reina · noviembre 2026", mail.Subject);
                CollectionAssert.AreEqual(new[] { "tienda@nuevavision.es" }, mail.To.Select(t => t.Address).ToArray());
                CollectionAssert.AreEqual(new[] { "compras@nuevavision.es" }, mail.CC.Select(t => t.Address).ToArray());
                Assert.IsTrue(mail.IsBodyHtml);
                StringAssert.Contains(mail.Body, CorreoAltasBajasTienda.FRASE_INICIAL);
                StringAssert.Contains(mail.Body, "Aparece y desaparece");
                Assert.AreEqual(1, mail.Attachments.Count);
                Assert.AreEqual("AltasBajasTienda_REI_20261101_20261130.xlsx", mail.Attachments[0].Name);
                Assert.IsFalse(mail.Body.IndexOf("coste", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        [TestMethod]
        public void Correo_MasDe300Lineas_SoloLas300PrimerasYElRestoEnElExcel()
        {
            AltasBajasTiendaDTO informe = InformeEjemplo(305);
            string cuerpo = CorreoAltasBajasTienda.Cuerpo(informe);

            StringAssert.Contains(cuerpo, "el resto, en el Excel adjunto");
            StringAssert.Contains(cuerpo, ">400<");
            Assert.IsFalse(cuerpo.Contains(">401<"));
        }

        [TestMethod]
        public void Excel_TieneLasTresHojas()
        {
            byte[] excel = new GeneradorExcelAltasBajasTienda().GenerarBytes(InformeEjemplo());
            using (SpreadsheetDocument libro = SpreadsheetDocument.Open(new MemoryStream(excel), false))
            {
                CollectionAssert.AreEqual(new[] { "Movimientos", "Aparece y desaparece", "Resumen" },
                    libro.WorkbookPart.Workbook.Sheets.Elements<Sheet>().Select(h => h.Name.Value).ToArray());
            }
        }

        [TestMethod]
        public async Task Job_SinMovimientos_NoSeMandaNada()
        {
            IInformesService servicio = A.Fake<IInformesService>();
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync(A<string>._, A<DateTime>._, A<DateTime>._))
                .ReturnsLazily((string a, DateTime d, DateTime h) => Task.FromResult(GestorAltasBajasTienda.Construir(null, a, d, h)));
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();

            List<CorreoAltasBajasTiendaDTO> resultados = await new AltasBajasTiendaJobsService(servicio, correo)
                .EnviarMesAnteriorAsync(new DateTime(2027, 1, 1));

            Assert.AreEqual(2, resultados.Count);
            Assert.IsTrue(resultados.All(r => !r.Enviado));
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync("REI", new DateTime(2026, 12, 1), new DateTime(2026, 12, 31))).MustHaveHappenedOnceExactly();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_UnaTiendaFalla_LaOtraSeManda()
        {
            IInformesService servicio = A.Fake<IInformesService>();
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync("REI", A<DateTime>._, A<DateTime>._)).Throws(new InvalidOperationException("caída"));
            A.CallTo(() => servicio.LeerAltasBajasTiendaAsync("ALC", A<DateTime>._, A<DateTime>._))
                .ReturnsLazily((string a, DateTime d, DateTime h) => Task.FromResult(GestorAltasBajasTienda.Construir(
                    new[] { Fila("100", -1, new DateTime(2026, 11, 3, 10, 0, 0)) }, a, d, h)));
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            List<string> para = new List<string>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) =>
            {
                para.AddRange(m.To.Select(t => t.Address));
                para.AddRange(m.CC.Select(t => "cc:" + t.Address));
                return true;
            });

            List<CorreoAltasBajasTiendaDTO> resultados = await new AltasBajasTiendaJobsService(servicio, correo)
                .EnviarMesAnteriorAsync(new DateTime(2026, 12, 1, 8, 15, 0));

            Assert.AreEqual(1, resultados.Count);
            Assert.AreEqual("ALC", resultados[0].Almacen);
            Assert.IsTrue(resultados[0].Enviado);
            CollectionAssert.AreEqual(new[] { "alcobendas@nuevavision.es", "cc:compras@nuevavision.es" }, para);
        }

        [TestMethod]
        public void Job_Cron_Dia1ALas815()
        {
            Assert.AreEqual("tiendas-altas-bajas-mensual", AltasBajasTiendaJobsService.JOB_ID);
            Assert.AreEqual("15 8 1 * *", AltasBajasTiendaJobsService.CRON);
        }
    }
}
