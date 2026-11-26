using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Calidad;
using NestoAPI.Infraestructure.Calidad.PedidoPerfecto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Caching;
using System.Security.Principal;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>NestoAPI#914: api/Calidad/PedidoPerfecto (Resumen y Pedidos), solo Dirección e Informática, y el resumen del servicio.</summary>
    [TestClass]
    public class CalidadPedidoPerfectoControllerTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);
        private IServicioPedidoPerfecto servicio;

        [TestInitialize]
        public void Preparar()
        {
            servicio = A.Fake<IServicioPedidoPerfecto>();
            A.CallTo(() => servicio.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).Returns(new ResumenPedidoPerfectoDTO());
            A.CallTo(() => servicio.Pedidos(A<string>._, A<DateTime>._, A<DateTime>._, A<MotivoPedidoPerfecto?>._, A<int>._, A<int>._))
                .Returns(new PaginaPedidosPerfectoDTO());
        }

        private CalidadPedidoPerfectoController Controller(params string[] roles) =>
            new CalidadPedidoPerfectoController(() => servicio, () => HOY)
            {
                User = new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Prueba", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };

        [DataTestMethod]
        [DataRow("Administración")]
        [DataRow("Almacén")]
        [DataRow("Vendedores")]
        public void Resumen_SinGrupo_403DeUsted(string rol)
        {
            var resultado = (NegotiatedContentResult<string>)Controller(rol).GetResumen();

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
            Assert.AreEqual(CalidadPedidoPerfectoController.MENSAJE_SIN_PERMISO, resultado.Content);
            A.CallTo(() => servicio.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Pedidos_SinGrupo_403()
        {
            var resultado = (NegotiatedContentResult<string>)Controller("Tiendas").GetPedidos();

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public void Resumen_SinFechas_DeHace12MesesAHace21Dias()
        {
            var resultado = Controller("Dirección").GetResumen() as OkNegotiatedContentResult<ResumenPedidoPerfectoDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => servicio.Resumen("1", new DateTime(2025, 11, 26), new DateTime(2026, 11, 5))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Resumen_DesdeDespuesDeHasta_400()
        {
            var resultado = Controller("Informática").GetResumen("1", new DateTime(2026, 10, 1), new DateTime(2026, 9, 1)) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "anterior");
        }

        [TestMethod]
        public void Resumen_MasDe13Meses_400()
        {
            var resultado = Controller("Dirección").GetResumen("1", new DateTime(2025, 1, 1), new DateTime(2026, 11, 1)) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "13 meses");
        }

        [TestMethod]
        public void Resumen_Justo13Meses_Vale()
        {
            Assert.IsNotNull(Controller("Dirección").GetResumen("1", new DateTime(2025, 10, 1), new DateTime(2026, 11, 1)) as OkNegotiatedContentResult<ResumenPedidoPerfectoDTO>);
        }

        [TestMethod]
        public void Resumen_HastaEnElFuturo_400()
        {
            Assert.IsNotNull(Controller("Dirección").GetResumen("1", new DateTime(2026, 11, 1), new DateTime(2026, 12, 15)) as BadRequestErrorMessageResult);
        }

        [TestMethod]
        public void Resumen_ElServicioFalla_503DeUsted()
        {
            A.CallTo(() => servicio.Resumen(A<string>._, A<DateTime>._, A<DateTime>._)).Throws(new InvalidOperationException("timeout"));

            var resultado = (NegotiatedContentResult<string>)Controller("Dirección").GetResumen();

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, resultado.StatusCode);
            Assert.AreEqual(CalidadPedidoPerfectoController.MENSAJE_NO_SE_PUEDE_LEER, resultado.Content);
        }

        [TestMethod]
        public void Pedidos_MotivoDesconocido_400ConLaLista()
        {
            var resultado = Controller("Dirección").GetPedidos(motivo: "Tarde") as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "NoSalioAlDiaSiguiente");
        }

        [TestMethod]
        public void Pedidos_MotivoEnNumero_400()
        {
            Assert.IsNotNull(Controller("Dirección").GetPedidos(motivo: "3") as BadRequestErrorMessageResult);
        }

        [TestMethod]
        public void Pedidos_MotivoSinDistinguirMayusculas_Filtra()
        {
            var resultado = Controller("Dirección").GetPedidos(motivo: "nosalioaldiasiguiente", pagina: 2, tamano: 50) as OkNegotiatedContentResult<PaginaPedidosPerfectoDTO>;

            Assert.IsNotNull(resultado);
            A.CallTo(() => servicio.Pedidos("1", A<DateTime>._, A<DateTime>._, MotivoPedidoPerfecto.NoSalioAlDiaSiguiente, 2, 50)).MustHaveHappenedOnceExactly();
        }

        [DataTestMethod]
        [DataRow(0, 100)]
        [DataRow(1, 0)]
        [DataRow(1, 1001)]
        public void Pedidos_PaginaOTamanoFuera_400(int pagina, int tamano)
        {
            Assert.IsNotNull(Controller("Dirección").GetPedidos(pagina: pagina, tamano: tamano) as BadRequestErrorMessageResult);
        }

        // El servicio: resumen y página con un lector falso
        private static HechosPedidoPerfecto Hechos(int pedido, DateTime fecha, Action<HechosPedidoPerfecto> cambiar = null)
        {
            var h = new HechosPedidoPerfecto
            {
                Empresa = "1", Pedido = pedido, Fecha = fecha, Cliente = "40483", Contacto = "0", PlazosPago = "CONTADO", Almacen = "ALG",
                LineasProducto = 1, LineasServidas = 1, Albaranes = 1, PrimerAlbaran = fecha.AddDays(1), MinFechaEntrega = fecha, Vendedor = "DLM", Usuario = "dlopez"
            };
            cambiar?.Invoke(h);
            return h;
        }

        private static ServicioPedidoPerfecto Servicio(LecturaPedidoPerfecto lectura, Action alLeer = null)
        {
            ILectorPedidoPerfecto lector = A.Fake<ILectorPedidoPerfecto>();
            A.CallTo(() => lector.Leer(A<string>._, A<DateTime>._, A<DateTime>._)).ReturnsLazily(() => { alLeer?.Invoke(); return lectura; });
            return new ServicioPedidoPerfecto(() => lector, () => HOY, (d, a) => d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday,
                new MemoryCache("pruebas914_" + Guid.NewGuid().ToString("N")));
        }

        private static LecturaPedidoPerfecto LecturaDePrueba() => new LecturaPedidoPerfecto
        {
            QuitarPickingDesde = new DateTime(2026, 11, 9),
            EntregaIncompletaDesde = new DateTime(2026, 11, 19),
            Pedidos = new List<HechosPedidoPerfecto>
            {
                Hechos(1, new DateTime(2026, 10, 5)),                                                 // lunes → martes: perfecto
                Hechos(2, new DateTime(2026, 10, 5), h => h.PlazosPago = "1/30"),                     // solo informativo
                Hechos(3, new DateTime(2026, 11, 2), h => { h.Albaranes = 2; h.LineasAmazon = 1; }),  // Amazon, dos albaranes
                Hechos(4, new DateTime(2026, 11, 10), h => h.PickingQuitado = true)                   // tocado (desde el 09/11)
            },
            TablasQueFaltan = new List<string> { "dbo.CalidadEventos" }
        };

        [TestMethod]
        public void Servicio_Resumen_CuentaPerfectosInformativosMesesYCanales()
        {
            ResumenPedidoPerfectoDTO r = Servicio(LecturaDePrueba()).Resumen("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15));

            Assert.AreEqual(4, r.Pedidos);
            Assert.AreEqual(1, r.Perfectos);
            Assert.AreEqual(25m, r.PorcentajePerfectos);
            Assert.AreEqual(2, r.PerfectosSinContarInformativos);
            CollectionAssert.AreEqual(new[] { "2026-10", "2026-11" }, r.PorMes.Select(m => m.Mes).ToArray());
            Assert.AreEqual(2, r.PorMes[0].Pedidos);
            Assert.AreEqual(1, r.PorMes[0].Perfectos);
            Assert.AreEqual(50m, r.PorMes[0].Porcentaje);
            Assert.AreEqual(1, r.PorCanal.Single(c => c.Canal == ReglaPedidoPerfecto.Canales.AMAZON).Pedidos);
            StringAssert.Contains(r.Aviso, "dbo.CalidadEventos");
        }

        [TestMethod]
        public void Servicio_Resumen_PorcentajeSobreLosEvaluadosYNoEvaluados()
        {
            ResumenPedidoPerfectoDTO r = Servicio(LecturaDePrueba()).Resumen("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15));

            MotivoResumenDTO tocado = r.Motivos.Single(m => m.Motivo == "TocadoTrasElPicking");
            Assert.AreEqual(1, tocado.PedidosEvaluados, "Solo el del 10/11 tiene rastro");
            Assert.AreEqual(1, tocado.Pedidos);
            Assert.AreEqual(100m, tocado.Porcentaje);
            Assert.AreEqual(new DateTime(2026, 11, 9), tocado.EvaluadoDesde);
            Assert.IsTrue(tocado.EsError);
            MotivoNoEvaluadoDTO sinEvaluar = r.MotivosNoEvaluados.Single(m => m.Motivo == "TocadoTrasElPicking");
            Assert.AreEqual(3, sinEvaluar.PedidosSinEvaluar);

            MotivoResumenDTO ruta = r.Motivos.Single(m => m.Motivo == "EnvioDeRutaPropiaForzado");
            Assert.IsFalse(ruta.Evaluado);
            Assert.IsNull(ruta.Porcentaje);
            Assert.IsTrue(r.MotivosNoEvaluados.Any(m => m.Motivo == "EnvioDeRutaPropiaForzado"));

            MotivoResumenDTO contado = r.Motivos.Single(m => m.Motivo == "NoCobradoAlContado");
            Assert.IsFalse(contado.EsError);
            Assert.AreEqual(25m, contado.Porcentaje);
            Assert.IsFalse(r.MotivosNoEvaluados.Any(m => m.Motivo == "Impagado"), "Lo que siempre se mira no sale como no evaluado");
        }

        [TestMethod]
        public void Servicio_Pedidos_FiltraPorMotivoYPagina()
        {
            ServicioPedidoPerfecto s = Servicio(LecturaDePrueba());

            PaginaPedidosPerfectoDTO todos = s.Pedidos("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15), null, 2, 3);
            Assert.AreEqual(4, todos.Total);
            Assert.AreEqual(1, todos.Pedidos.Count);
            Assert.AreEqual(4, todos.Pedidos[0].Pedido);

            PaginaPedidosPerfectoDTO dosAlbaranes = s.Pedidos("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15), MotivoPedidoPerfecto.NoServidoDeUnaVez, 1, 100);
            Assert.AreEqual(1, dosAlbaranes.Total);
            PedidoPerfectoDTO p = dosAlbaranes.Pedidos.Single();
            Assert.AreEqual(3, p.Pedido);
            Assert.AreEqual(ReglaPedidoPerfecto.Canales.AMAZON, p.Canal);
            Assert.AreEqual("DLM", p.Vendedor);
            Assert.AreEqual("dlopez", p.Usuario);
            Assert.IsFalse(p.Perfecto);
            CollectionAssert.Contains(p.Motivos, "NoServidoDeUnaVez");
            CollectionAssert.Contains(p.MotivosNoEvaluados, "EnvioDeRutaPropiaForzado");
            Assert.AreEqual(1, p.DiasLaborablesHastaServir);
        }

        [TestMethod]
        public void Servicio_ResumenYPedidosDelMismoPeriodo_LeenUnaSolaVez()
        {
            int lecturas = 0;
            ServicioPedidoPerfecto s = Servicio(LecturaDePrueba(), () => lecturas++);

            _ = s.Resumen("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15));
            _ = s.Pedidos("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15), null, 1, 10);
            _ = s.Resumen("1", new DateTime(2026, 10, 2), new DateTime(2026, 11, 15));

            Assert.AreEqual(2, lecturas);
        }

        [TestMethod]
        public void Servicio_SiLaLecturaFalla_NoSeQuedaEnLaCache()
        {
            int lecturas = 0;
            ServicioPedidoPerfecto s = Servicio(LecturaDePrueba(), () => { lecturas++; if (lecturas == 1) { throw new InvalidOperationException("caída"); } });

            _ = Assert.ThrowsException<InvalidOperationException>(() => s.Resumen("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15)));
            Assert.AreEqual(4, s.Resumen("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15)).Pedidos);
        }

        [TestMethod]
        public void Servicio_SinNombresEnElResumen()
        {
            ResumenPedidoPerfectoDTO r = Servicio(LecturaDePrueba()).Resumen("1", new DateTime(2026, 10, 1), new DateTime(2026, 11, 15));

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(r);
            Assert.IsFalse(json.Contains("dlopez") || json.Contains("DLM"), "El resumen agregado no lleva personas (corte 1)");
        }
    }
}
