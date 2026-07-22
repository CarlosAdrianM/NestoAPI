using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#332 (slices 2-3): crear la remesa. Las piezas puras (validación de la
    /// selección contra candidatos frescos y composición de las líneas de PreContabilidad,
    /// calcadas del asiento real 1195101) se testean aisladas; la orquestación (contador,
    /// alta, contabilizar) es SQL fino sobre el único call site ya probado.
    /// </summary>
    [TestClass]
    public class CrearRemesaServiceTests
    {
        private static EfectoCandidatoDTO Candidato(int id, bool preseleccionado = true,
            string motivo = null, bool conNegativos = false, string cliente = "15191")
        {
            return new EfectoCandidatoDTO
            {
                Id = id,
                Cliente = cliente,
                Preseleccionado = preseleccionado,
                Motivo = motivo,
                ClienteConNegativos = conNegativos
            };
        }

        [TestMethod]
        public void ValidarSeleccion_TodoCandidatoYLimpio_SinErrores()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1, 2 },
                new List<EfectoCandidatoDTO> { Candidato(1), Candidato(2), Candidato(3) });

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ValidarSeleccion_EfectoQueYaNoEsCandidato_ErrorDeRefresco()
        {
            // Lección Nesto#397: revalidar con datos FRESCOS en el POST
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 99 },
                new List<EfectoCandidatoDTO> { Candidato(1) });

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "refresque");
        }

        [TestMethod]
        public void ValidarSeleccion_EfectoRetenidoPorElGating_ErrorConElMotivo()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1 },
                new List<EfectoCandidatoDTO> { Candidato(1, preseleccionado: false,
                    motivo: "Retenido: el pedido tiene envíos de agencia sin confirmar la entrega (#172).") });

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "sin confirmar la entrega");
        }

        [TestMethod]
        public void ValidarSeleccion_ClienteConNegativos_LaPuertaDeNeteoSeRevalidaEnElPost()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1, 2 },
                new List<EfectoCandidatoDTO>
                {
                    Candidato(1, conNegativos: true, cliente: "15191"),
                    Candidato(2, cliente: "30676")
                });

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "15191");
            StringAssert.Contains(errores.Single(), "negativos");
        }

        // Composición de líneas: calcada del asiento real 1195101 (remesa 10898, 20/07/26)

        private static ExtractoCliente Efecto(int orden, string cliente, decimal pendiente,
            string documento, string efecto = "1", DateTime? vencimiento = null)
        {
            return new ExtractoCliente
            {
                Empresa = "1",
                Nº_Orden = orden,
                Número = cliente,
                Contacto = "0",
                ImportePdte = pendiente,
                Nº_Documento = documento,
                Efecto = efecto,
                FormaPago = "RCB",
                FormaVenta = "VAR",
                Delegación = "ALG",
                Vendedor = "NV",
                CCC = "1",
                FechaVto = vencimiento
            };
        }

        private static Banco BancoSabadell() => new Banco
        {
            Empresa = "1",
            Número = "5",
            Cuenta_Contable = "57200013"
        };

        [TestMethod]
        public void ConstruirLineasRemesa_CuadraDebeYHaberYLiquidaCadaEfecto()
        {
            var efectos = new List<ExtractoCliente>
            {
                Efecto(3016000, "40227", 14.65m, "NV2608461", "2"),
                Efecto(3019661, "40185", 326.77m, "NV2609151")
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "NUEVAVISION\\Carlos");

            Assert.AreEqual(3, lineas.Count, "Una línea por efecto + la del banco");
            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "El asiento debe cuadrar");

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual("57200013", banco.Nº_Cuenta);
            Assert.AreEqual(341.42m, banco.Debe);
            StringAssert.Contains(banco.Concepto, "Remesa:10900");
            Assert.AreEqual("10900", banco.Nº_Documento);

            PreContabilidad pago = lineas.Single(l => l.Liquidado == 3016000);
            Assert.AreEqual("40227", pago.Nº_Cuenta);
            Assert.AreEqual(14.65m, pago.Haber);
            Assert.AreEqual(Constantes.TiposExtractoCliente.PAGO, pago.TipoApunte);
            Assert.AreEqual("10900", pago.Nº_Remesa, "El pago nace con la remesa: prdLiquidar la propaga a la cartera");
            StringAssert.Contains(pago.Concepto, "Pago Factura NV2608461");
            Assert.AreEqual(CrearRemesaService.DIARIO_REMESA, pago.Diario);
        }

        [TestMethod]
        public void ConstruirLineasRemesa_TodasLasLineasLlevanRemesaDiarioYUsuario()
        {
            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), new List<ExtractoCliente> { Efecto(1, "15191", 100m, "NV1") }, "carlos");

            Assert.IsTrue(lineas.All(l => l.Nº_Remesa == "10900"));
            Assert.IsTrue(lineas.All(l => l.Diario == CrearRemesaService.DIARIO_REMESA));
            Assert.IsTrue(lineas.All(l => l.Usuario == "carlos"));
            Assert.IsTrue(lineas.All(l => l.Asiento_Automático));
        }

        // NestoAPI#345: remesa multi-fecha (viernes cubre sábado y domingo; vísperas de festivo)

        [TestMethod]
        public void ConstruirLineasRemesa_RespetandoVencimientos_UnaLineaDeBancoPorFecha()
        {
            DateTime hoy = DateTime.Today;
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1", vencimiento: hoy.AddDays(1)),
                Efecto(2, "26985", 50m, "NV2", vencimiento: hoy.AddDays(1)),
                Efecto(3, "40227", 25m, "NV3", vencimiento: hoy.AddDays(2))
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: true);

            List<PreContabilidad> bancos = lineas
                .Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE)
                .OrderBy(l => l.Fecha).ToList();
            Assert.AreEqual(2, bancos.Count, "Un apunte de banco POR FECHA de cargo");
            Assert.AreEqual(hoy.AddDays(1), bancos[0].Fecha);
            Assert.AreEqual(150m, bancos[0].Debe);
            Assert.AreEqual(hoy.AddDays(2), bancos[1].Fecha);
            Assert.AreEqual(25m, bancos[1].Debe);
            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "El asiento debe cuadrar");

            PreContabilidad pagoLunes = lineas.Single(l => l.Liquidado == 3);
            Assert.AreEqual(hoy.AddDays(2), pagoLunes.Fecha, "El pago lleva la fecha de SU vencimiento");
            Assert.AreEqual(hoy.AddDays(2), pagoLunes.FechaVto);
        }

        [TestMethod]
        public void ConstruirLineasRemesa_VencimientoPasadoONulo_SeCobraEnLaFechaDeCargo()
        {
            // "Hay que controlar que las fechas nunca sean anteriores a hoy" (Carlos 22/07)
            DateTime hoy = DateTime.Today;
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1", vencimiento: hoy.AddDays(-10)),
                Efecto(2, "26985", 50m, "NV2", vencimiento: null)
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: true);

            Assert.IsTrue(lineas.All(l => l.Fecha == hoy), "Nada puede ir con fecha anterior a hoy");
            Assert.AreEqual(1, lineas.Count(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE),
                "Todos caen en la misma fecha: un solo apunte de banco");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_ForzandoFechaDeCargo_TodoVaAEsaFecha()
        {
            // Modo forzado (default): vísperas de festivo — remesa de hoy con fecha de mañana
            DateTime manana = DateTime.Today.AddDays(1);
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1", vencimiento: DateTime.Today.AddDays(5)),
                Efecto(2, "26985", 50m, "NV2", vencimiento: DateTime.Today)
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: false, fechaCargo: manana);

            Assert.IsTrue(lineas.All(l => l.Fecha == manana && l.FechaVto == manana),
                "En modo forzado TODOS los efectos van a la fecha de cargo, ignorando su vencimiento");
            Assert.AreEqual(1, lineas.Count(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE));
        }

        [TestMethod]
        public void FechaCargoEfectiva_NuncaAnteriorAHoy()
        {
            Assert.AreEqual(DateTime.Today, CrearRemesaService.FechaCargoEfectiva(null));
            Assert.AreEqual(DateTime.Today, CrearRemesaService.FechaCargoEfectiva(DateTime.Today.AddDays(-3)));
            Assert.AreEqual(DateTime.Today.AddDays(2), CrearRemesaService.FechaCargoEfectiva(DateTime.Today.AddDays(2)));
        }

        [TestMethod]
        public void ProximaFechaCargo_SaltaFinesDeSemanaYFestivos()
        {
            // Solo fines de semana como no laborables para el test
            Func<DateTime, bool> finde = f => f.DayOfWeek == DayOfWeek.Saturday || f.DayOfWeek == DayOfWeek.Sunday;
            var jueves = new DateTime(2026, 7, 23);
            var viernes = new DateTime(2026, 7, 24);

            Assert.AreEqual(viernes, CrearRemesaService.ProximaFechaCargo(jueves, 1, finde), "Jueves + 1 = viernes");
            Assert.AreEqual(new DateTime(2026, 7, 27), CrearRemesaService.ProximaFechaCargo(viernes, 1, finde),
                "Viernes + 1 cae en sábado → salta al LUNES (mejor que el domingo)");

            // Festivo: el viernes 24 es festivo → jueves + 1 salta al lunes
            Func<DateTime, bool> findeYViernesFestivo = f => finde(f) || f == viernes;
            Assert.AreEqual(new DateTime(2026, 7, 27), CrearRemesaService.ProximaFechaCargo(jueves, 1, findeYViernesFestivo));

            // Antelación configurable (parámetro DiasAntelacionRemesa): estilo eléctricas, 5 días
            Assert.AreEqual(new DateTime(2026, 7, 28), CrearRemesaService.ProximaFechaCargo(jueves, 5, finde),
                "Jueves + 5 = martes siguiente (el 28); si cayera en finde saltaría");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_LaLineaDelBancoLlevaTipoApunte()
        {
            // Bug 22/07/26 (2º intento en vivo): PreContabilidad.TipoApunte es NOT NULL y la
            // línea del banco no lo rellenaba → DbEntityValidationException al guardar y remesa
            // abortada. El asiento real de la remesa 10898 lleva TipoApunte "1" en el banco.
            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), new List<ExtractoCliente> { Efecto(1, "15191", 100m, "NV1") }, "carlos");

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual(Constantes.TiposExtractoCliente.FACTURA, banco.TipoApunte);
            Assert.IsTrue(lineas.All(l => !string.IsNullOrEmpty(l.TipoApunte)),
                "Ninguna línea puede ir sin TipoApunte (columna NOT NULL)");
        }

        // Controller (regla de la casa)

        [TestMethod]
        public async Task CrearRemesa_ConExito_DevuelveLaRespuesta()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.CrearRemesaAsync(A<CrearRemesaRequest>.Ignored, A<string>.Ignored))
                .Returns(new CrearRemesaResponse { NumeroRemesa = 10900, Importe = 341.42m, NumeroEfectos = 2 });
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.CrearRemesa(new CrearRemesaRequest
            { Empresa = "1", Banco = "5", Efectos = new List<int> { 1, 2 } })
                as OkNegotiatedContentResult<CrearRemesaResponse>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(10900, resultado.Content.NumeroRemesa);
        }

        [TestMethod]
        public async Task CrearRemesa_ConValidacionFallida_BadRequestConElMotivo()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.CrearRemesaAsync(A<CrearRemesaRequest>.Ignored, A<string>.Ignored))
                .Throws(new System.InvalidOperationException("El efecto 99 ya no es candidato a remesa"));
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.CrearRemesa(new CrearRemesaRequest
            { Empresa = "1", Banco = "5", Efectos = new List<int> { 99 } }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "ya no es candidato");
        }
    }
}
