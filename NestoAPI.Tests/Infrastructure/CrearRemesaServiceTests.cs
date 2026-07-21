using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models;
using NestoAPI.Models.Remesas;
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
            string documento, string efecto = "1")
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
                CCC = "1"
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
