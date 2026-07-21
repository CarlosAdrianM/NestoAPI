using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ExtractosCliente;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#333: liquidar movimientos del extracto. Las validaciones de prdLiquidar se
    /// adelantan en C# porque varios raiserror del SP van con severidad 1 (aviso): NO llegan
    /// como SqlException a EF y el fallo sería silencioso.
    /// </summary>
    [TestClass]
    public class ServicioLiquidarEfectosTests
    {
        private static EfectoParaLiquidar Efecto(string cliente = "15191", decimal pendiente = 500m,
            string remesa = null, bool bloqueado = false, string descripcionEstado = null)
        {
            return new EfectoParaLiquidar
            {
                Cliente = cliente,
                ImportePdte = pendiente,
                Remesa = remesa,
                EstadoBloqueaLiquidacion = bloqueado,
                DescripcionEstado = descripcionEstado
            };
        }

        [TestMethod]
        public void ErroresLiquidacion_ParValido_SinErrores()
        {
            // El caso de #332: abono de -200 contra efecto de 500 (liquidación parcial nativa)
            var errores = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(pendiente: -200m), Efecto(pendiente: 500m), 1, 2);

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ErroresLiquidacion_MovimientoInexistente_LoDice()
        {
            var errores = ServicioLiquidarEfectos.ErroresLiquidacion(null, Efecto(), 111, 222);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "111");
        }

        [TestMethod]
        public void ErroresLiquidacion_DistintoCliente_Rechaza()
        {
            var errores = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(cliente: "15191", pendiente: -200m), Efecto(cliente: "30676", pendiente: 500m), 1, 2);

            Assert.IsTrue(errores.Any(e => e.Contains("mismo cliente")));
        }

        [TestMethod]
        public void ErroresLiquidacion_MismoSignoOImporteCero_Rechaza()
        {
            // Las dos variantes del raiserror silencioso (severidad 1) del SP
            var mismoSigno = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(pendiente: 200m), Efecto(pendiente: 500m), 1, 2);
            var importeCero = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(pendiente: 0m), Efecto(pendiente: 500m), 1, 2);

            Assert.IsTrue(mismoSigno.Any(e => e.Contains("signo contrario")));
            Assert.IsTrue(importeCero.Any(e => e.Contains("signo contrario")));
        }

        [TestMethod]
        public void ErroresLiquidacion_AmbosRemesados_Rechaza()
        {
            // Regla clave de #332: hay que liquidar ANTES de asignar la remesa
            var errores = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(pendiente: -200m, remesa: "10897"), Efecto(pendiente: 500m, remesa: "10898"), 1, 2);

            Assert.IsTrue(errores.Any(e => e.Contains("remesados")));
        }

        [TestMethod]
        public void ErroresLiquidacion_SoloUnoRemesado_SePermite()
        {
            // El SP propaga la remesa del remesado al otro: es el caso "dejar el efecto en la
            // remesa por el importe modificado" del diseño de #332
            var errores = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(pendiente: -200m, remesa: "10897"), Efecto(pendiente: 500m), 1, 2);

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ErroresLiquidacion_EstadoBloqueado_RechazaConLaDescripcion()
        {
            var errores = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(pendiente: -200m), Efecto(pendiente: 500m, bloqueado: true, descripcionEstado: "Impagado en gestión"),
                1, 2);

            Assert.IsTrue(errores.Any(e => e.Contains("bloquea la liquidación") && e.Contains("Impagado en gestión")));
        }

        [TestMethod]
        public void ErroresLiquidacion_OrigenIgualQueDestino_Rechaza()
        {
            var errores = ServicioLiquidarEfectos.ErroresLiquidacion(
                Efecto(pendiente: -200m), Efecto(pendiente: 500m), 7, 7);

            Assert.IsTrue(errores.Any(e => e.Contains("mismo movimiento")));
        }

        // Controller (regla de la casa: cada endpoint nuevo lleva tests de controller)

        [TestMethod]
        public async Task Liquidar_ConExito_DevuelveLosImportesResultantes()
        {
            var servicio = A.Fake<IServicioLiquidarEfectos>();
            var resultadoServicio = new ResultadoLiquidacionEfectos
            {
                Exito = true,
                ImportePdteOrigen = 0m,
                ImportePdteDestino = 300m
            };
            _ = A.CallTo(() => servicio.Liquidar("1", 111, 222, A<string>.Ignored)).Returns(resultadoServicio);
            var controller = new ExtractosClienteController(A.Fake<IServicioCorreoElectronico>(), A.Fake<NVEntities>(), servicio);

            var resultado = await controller.Liquidar(new ExtractosClienteController.LiquidarEfectosRequest
            { Empresa = "1", Origen = 111, Destino = 222 }) as OkNegotiatedContentResult<ResultadoLiquidacionEfectos>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(300m, resultado.Content.ImportePdteDestino);
        }

        [TestMethod]
        public async Task Liquidar_ConErroresDeValidacion_BadRequestConLosMotivos()
        {
            var servicio = A.Fake<IServicioLiquidarEfectos>();
            var resultadoServicio = new ResultadoLiquidacionEfectos();
            resultadoServicio.Errores.Add("Ambos movimientos están ya remesados...");
            _ = A.CallTo(() => servicio.Liquidar(A<string>.Ignored, A<int>.Ignored, A<int>.Ignored, A<string>.Ignored))
                .Returns(resultadoServicio);
            var controller = new ExtractosClienteController(A.Fake<IServicioCorreoElectronico>(), A.Fake<NVEntities>(), servicio);

            var resultado = await controller.Liquidar(new ExtractosClienteController.LiquidarEfectosRequest
            { Empresa = "1", Origen = 111, Destino = 222 }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "remesados");
        }

        [TestMethod]
        public async Task Liquidar_SinDatos_BadRequest()
        {
            var controller = new ExtractosClienteController(A.Fake<IServicioCorreoElectronico>(), A.Fake<NVEntities>(),
                A.Fake<IServicioLiquidarEfectos>());

            var sinCuerpo = await controller.Liquidar(null);
            var sinDestino = await controller.Liquidar(new ExtractosClienteController.LiquidarEfectosRequest
            { Empresa = "1", Origen = 111 });

            Assert.IsInstanceOfType(sinCuerpo, typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(sinDestino, typeof(BadRequestErrorMessageResult));
        }
    }
}
