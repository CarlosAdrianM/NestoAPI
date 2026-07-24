using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Nesto#340 Fase 1C.14 slice 2: remesas de cobro para el grid de RemesasViewModel.
    /// </summary>
    [TestClass]
    public class RemesasControllerTests
    {
        [TestMethod]
        public async Task GetRemesas_ConTop_DevuelveLasRemesasDelServicio()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            List<RemesaDTO> remesas = new List<RemesaDTO>
            {
                new RemesaDTO { Numero = 10897, Fecha = new DateTime(2026, 7, 17), Importe = 10546.66M, Banco = "5" },
                new RemesaDTO { Numero = 10896, Fecha = new DateTime(2026, 7, 16), Importe = 9420.99M, Banco = "5" }
            };
            _ = A.CallTo(() => servicio.LeerRemesasAsync("1", 100)).Returns(remesas);
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetRemesas("1", 100) as OkNegotiatedContentResult<List<RemesaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual(10897, resultado.Content[0].Numero);
        }

        [TestMethod]
        public async Task GetMovimientos_DevuelveLosEfectosDeLaRemesa()
        {
            // Slice 3: el grid de la derecha muestra los efectos incluidos en la remesa
            IRemesasService servicio = A.Fake<IRemesasService>();
            List<MovimientoRemesaDTO> movimientos = new List<MovimientoRemesaDTO>
            {
                new MovimientoRemesaDTO { Id = 1, Cliente = "15191", Contacto = "0", Importe = 250.50M, Ccc = "1" }
            };
            _ = A.CallTo(() => servicio.LeerMovimientosAsync("1", 10897)).Returns(movimientos);
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetMovimientos("1", 10897) as OkNegotiatedContentResult<List<MovimientoRemesaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.AreEqual("15191", resultado.Content[0].Cliente);
            Assert.AreEqual(250.50M, resultado.Content[0].Importe);
        }

        [TestMethod]
        public async Task GetMovimientos_RemesaSinEfectos_DevuelveListaVacia()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.LeerMovimientosAsync(A<string>.Ignored, A<int>.Ignored))
                .Returns(new List<MovimientoRemesaDTO>());
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetMovimientos("1", 99999) as OkNegotiatedContentResult<List<MovimientoRemesaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Content.Count);
        }

        [TestMethod]
        public async Task GetRemesas_SinTop_PideTodasAlServicio()
        {
            // El botón "Ver Todas" llama sin top: el servicio debe recibir null (sin límite).
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.LeerRemesasAsync("1", null)).Returns(new List<RemesaDTO>());
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetRemesas("1") as OkNegotiatedContentResult<List<RemesaDTO>>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => servicio.LeerRemesasAsync("1", null)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task GetImpagados_ConTop_DevuelveLosAsientosAgrupados()
        {
            // Slice 4: grid izquierdo de la pestaña Impagados (asiento, fecha, nº movimientos)
            IRemesasService servicio = A.Fake<IRemesasService>();
            List<ImpagadoRemesaDTO> impagados = new List<ImpagadoRemesaDTO>
            {
                new ImpagadoRemesaDTO { Asiento = 1195101, Fecha = new DateTime(2026, 7, 20), Cuenta = 3 },
                new ImpagadoRemesaDTO { Asiento = 1194800, Fecha = new DateTime(2026, 7, 15), Cuenta = 1 }
            };
            _ = A.CallTo(() => servicio.LeerImpagadosAsync("1", 100)).Returns(impagados);
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetImpagados("1", 100) as OkNegotiatedContentResult<List<ImpagadoRemesaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            Assert.AreEqual(1195101, resultado.Content[0].Asiento);
            Assert.AreEqual(3, resultado.Content[0].Cuenta);
        }

        [TestMethod]
        public async Task GetMovimientosImpagado_DevuelveLosMovimientosDelAsiento()
        {
            // Slice 5: grid derecho con los movimientos del asiento de impagados seleccionado
            IRemesasService servicio = A.Fake<IRemesasService>();
            List<MovimientoRemesaDTO> movimientos = new List<MovimientoRemesaDTO>
            {
                new MovimientoRemesaDTO { Id = 7, Cliente = "15191", Contacto = "0", Importe = 250.50M,
                    Fecha = new DateTime(2026, 7, 20) }
            };
            _ = A.CallTo(() => servicio.LeerMovimientosImpagadoAsync("1", 1195101)).Returns(movimientos);
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetMovimientosImpagado("1", 1195101) as OkNegotiatedContentResult<List<MovimientoRemesaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.AreEqual("15191", resultado.Content[0].Cliente);
            Assert.AreEqual(new DateTime(2026, 7, 20), resultado.Content[0].Fecha);
        }

        [TestMethod]
        public async Task GetMovimientosImpagado_AsientoSinMovimientos_DevuelveListaVacia()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.LeerMovimientosImpagadoAsync(A<string>.Ignored, A<int>.Ignored))
                .Returns(new List<MovimientoRemesaDTO>());
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetMovimientosImpagado("1", 99999) as OkNegotiatedContentResult<List<MovimientoRemesaDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(0, resultado.Content.Count);
        }

        [TestMethod]
        public async Task GetFicheroRemesa_DevuelveElXmlDelServicio()
        {
            // Slice 6: el fichero SEPA se genera en el servidor (único call site del SP)
            IRemesasService servicio = A.Fake<IRemesasService>();
            var fechaCobro = new DateTime(2026, 8, 1);
            _ = A.CallTo(() => servicio.CrearFicheroRemesaAsync(10897, "COR1", fechaCobro))
                .Returns("<Document>sepa</Document>");
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetFicheroRemesa(10897, "COR1", fechaCobro) as OkNegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("<Document>sepa</Document>", resultado.Content);
        }

        [TestMethod]
        public async Task ContabilizarImpagados_ConIdentity_PropagaElUsuarioRealAlServicio()
        {
            // Regresión: al modernizar la ventana de Remesas (EF -> API) el usuario dejó de ser
            // el de dominio y pasó a ser la cuenta de la API (NUEVAVISION\RDS2016$), lo que hacía
            // que prdContabilizar abortara. El usuario debe salir del Identity autenticado.
            IRemesasService servicio = A.Fake<IRemesasService>();
            RemesasController controller = new RemesasController(servicio);
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") }, "JWT");
            controller.RequestContext = new HttpRequestContext { Principal = new ClaimsPrincipal(identity) };

            var resultado = await controller.ContabilizarImpagados(
                new ContabilizarImpagadosRequest { Fichero = "<Document>pain</Document>" });

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            A.CallTo(() => servicio.ContabilizarImpagadosAsync("<Document>pain</Document>", "NUEVAVISION\\Carlos"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ContabilizarImpagados_SinIdentity_PasaUsuarioNuloComoFallback()
        {
            // Sin Identity (tests / llamada no autenticada) el usuario va null: el SP cae al
            // SYSTEM_USER, que es el comportamiento previo. Nunca se manda un usuario spoofeado.
            IRemesasService servicio = A.Fake<IRemesasService>();
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.ContabilizarImpagados(
                new ContabilizarImpagadosRequest { Fichero = "<Document>pain</Document>" });

            Assert.IsInstanceOfType(resultado, typeof(OkResult));
            A.CallTo(() => servicio.ContabilizarImpagadosAsync("<Document>pain</Document>", null))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ContabilizarImpagados_SinFichero_DevuelveBadRequestSinLlamarAlServicio()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            RemesasController controller = new RemesasController(servicio);

            var vacio = await controller.ContabilizarImpagados(new ContabilizarImpagadosRequest { Fichero = " " });
            var nulo = await controller.ContabilizarImpagados(null);

            Assert.IsInstanceOfType(vacio, typeof(BadRequestErrorMessageResult));
            Assert.IsInstanceOfType(nulo, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => servicio.ContabilizarImpagadosAsync(A<string>.Ignored, A<string>.Ignored)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetTareasImpagado_DevuelveLosEfectosConDatosDelCliente()
        {
            // Slice 8: datos para crear las tareas de Planner de gestión de cobro
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.LeerTareasImpagadoAsync("1", 1195101))
                .Returns(new List<TareaImpagadoDTO>
                {
                    new TareaImpagadoDTO
                    {
                        Cliente = "15191",
                        Contacto = "0",
                        Importe = 250.50M,
                        Concepto = "Impagado recibo",
                        Vendedor = "NV",
                        NombreCliente = "CLIENTE PRUEBA",
                        NombreEmpresa = "NUEVA VISION"
                    }
                });
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.GetTareasImpagado("1", 1195101) as OkNegotiatedContentResult<List<TareaImpagadoDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.AreEqual("CLIENTE PRUEBA", resultado.Content[0].NombreCliente);
            Assert.AreEqual("NUEVA VISION", resultado.Content[0].NombreEmpresa);
        }
    }
}
