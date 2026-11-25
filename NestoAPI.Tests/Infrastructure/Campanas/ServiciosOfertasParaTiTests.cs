using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Campanas.OfertasParaTi;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Campanas
{
    [TestClass]
    public class ServiciosOfertasParaTiTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 26);
        private static readonly DateTime AHORA = new DateTime(2026, 11, 26, 10, 30, 0);
        private const string BF = "Black Friday 2026";

        private ILectorOfertasParaTi lector;
        private IPuntuadorReposicion puntuador;
        private List<Exception> errores;

        private static FilaCampanaOferta Fila(string producto, string familia = "EVA", decimal descuento = 0.30m) => new FilaCampanaOferta
        {
            Campana = BF,
            Producto = producto,
            Descuento = descuento,
            AudienciaOferta = 2,
            FechaDesde = new DateTime(2026, 11, 25),
            FechaHasta = new DateTime(2026, 11, 30),
            Nombre = "PRODUCTO " + producto,
            Familia = familia,
            Grupo = "COS",
            SubGrupo = "CRE",
            PVP = 10,
            Estado = 0
        };

        [TestInitialize]
        public void Preparar()
        {
            errores = new List<Exception>();
            lector = A.Fake<ILectorOfertasParaTi>();
            puntuador = A.Fake<IPuntuadorReposicion>();
            A.CallTo(() => puntuador.ModeloDisponible()).Returns(null);
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._))
                .Returns(new List<FilaCampanaOferta> { Fila("1", "A"), Fila("2", "B"), Fila("3", "C") });
            A.CallTo(() => lector.LeerPopularidad(A<IReadOnlyCollection<string>>._))
                .Returns(new Dictionary<string, int> { { "1", 30 }, { "2", 20 }, { "3", 10 } });
            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._))
                .ReturnsLazily((IReadOnlyCollection<string> ps) => ps.ToDictionary(p => p, p => p == "2" ? 0 : 5));
            A.CallTo(() => lector.LeerCompras(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._, A<DateTime>._))
                .Returns(new List<CompraDiaReposicion>());
            A.CallTo(() => lector.LeerPendientes(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new List<PendienteClienteOferta>());
            A.CallTo(() => lector.LeerFamilias(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new Dictionary<string, string>());
        }

        private ServicioOfertasParaTi Nucleo() => new ServicioOfertasParaTi(lector, puntuador, errores.Add);

        // ---------------------------------------------------------------- núcleo

        [TestMethod]
        public async Task Nucleo_SinStockFuera_YPorRegla()
        {
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            Dictionary<string, ResultadoOfertasCliente> r = await nucleo.Calcular(ctx, new[] { "1000" }, 5);

            CollectionAssert.AreEqual(new[] { "1", "3" }, r["1000"].Ofertas.Select(o => o.Candidata.Producto).ToList());
            Assert.IsNull(r["1000"].VersionModelo);
        }

        [TestMethod]
        public async Task Nucleo_ConModelo_OrdenaElModelo()
        {
            A.CallTo(() => puntuador.ModeloDisponible()).Returns("m1-20261125_0300");
            A.CallTo(() => puntuador.Puntuar(A<string>._, "1000", A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .Returns(new Dictionary<string, float> { { "3", 0.8f }, { "1", 0.2f } });
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            Dictionary<string, ResultadoOfertasCliente> r = await nucleo.Calcular(ctx, new[] { "1000" }, 5);

            CollectionAssert.AreEqual(new[] { "3", "1" }, r["1000"].Ofertas.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual("m1-20261125_0300", r["1000"].VersionModelo);
        }

        [TestMethod]
        public async Task Nucleo_SiElModeloFalla_LaReglaYNoRompe()
        {
            A.CallTo(() => puntuador.ModeloDisponible()).Returns("m1-x");
            A.CallTo(() => puntuador.Puntuar(A<string>._, A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._))
                .Throws(new InvalidOperationException("roto"));
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            Dictionary<string, ResultadoOfertasCliente> r = await nucleo.Calcular(ctx, new[] { "1000", "2000" }, 5);

            CollectionAssert.AreEqual(new[] { "1", "3" }, r["2000"].Ofertas.Select(o => o.Candidata.Producto).ToList());
            Assert.AreEqual(1, errores.Count, "Falla una vez y la tanda sigue por la regla");
            // una sola lectura de compras para los dos clientes (por tanda, no por cliente)
            A.CallTo(() => lector.LeerCompras(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._, A<DateTime>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Nucleo_ElStockSeLeeUnaVezParaTodaLaTanda()
        {
            ServicioOfertasParaTi nucleo = Nucleo();
            ContextoOfertasParaTi ctx = await nucleo.Preparar(BF, HOY);
            _ = await nucleo.Calcular(ctx, new[] { "1000", "2000", "3000" }, 5);
            A.CallTo(() => lector.LeerDisponibles(A<IReadOnlyCollection<string>>._)).MustHaveHappenedOnceExactly();
        }

        // ---------------------------------------------------------------- endpoint

        [TestMethod]
        public async Task Cliente_FueraLasQueNoMejoran_YConLaCabeceraDeLaCampana()
        {
            ICalculadorPreciosOferta precios = A.Fake<ICalculadorPreciosOferta>();
            A.CallTo(() => precios.Calcular(A<string>._, "1000", "0", "1", BF)).Returns(new PreciosOferta { Precio = 8.75m, PrecioAntes = 8.75m, Tarifa = 12.5m });
            A.CallTo(() => precios.Calcular(A<string>._, "1000", "0", "3", BF)).Returns(new PreciosOferta { Precio = 8.75m, PrecioAntes = 12.5m, Tarifa = 12.5m });
            var servicio = new ServicioOfertasCliente(Nucleo(), precios, c => Task.FromResult("0"), p => Task.FromResult("https://tienda.nuevavision.es/" + p + ".jpg"),
                registrar: errores.Add);

            OfertasParaTiRespuestaDTO r = await servicio.Calcular("1000", null, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.IsTrue(r.HayOfertas);
            Assert.AreEqual(BF, r.Campana);
            Assert.AreEqual("Black Friday", r.Titulo);
            Assert.AreEqual("2026-11-30", r.FechaHasta);
            Assert.AreEqual("Hasta el lunes 30 de noviembre", r.TextoHasta);
            OfertaParaTiDTO o = r.Ofertas.Single();
            Assert.AreEqual("3", o.Producto);
            Assert.AreEqual("Producto 3", o.Nombre);
            Assert.AreEqual(8.75m, o.Precio);
            Assert.AreEqual(12.5m, o.PrecioAntes);
            Assert.AreEqual(0.3m, o.Descuento);
            Assert.AreEqual("https://tienda.nuevavision.es/3.jpg", o.UrlFoto);
            Assert.AreEqual(MotivosOfertaParaTi.TE_PUEDE_INTERESAR, o.Motivo);
        }

        [TestMethod]
        public async Task Cliente_SinPrecios_SinImportes()
        {
            ICalculadorPreciosOferta precios = A.Fake<ICalculadorPreciosOferta>();
            A.CallTo(() => precios.Calcular(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._))
                .Returns(new PreciosOferta { Precio = 8m, PrecioAntes = 10m, Tarifa = 10m });
            var servicio = new ServicioOfertasCliente(Nucleo(), precios, c => Task.FromResult("0"), p => Task.FromResult<string>(null), registrar: errores.Add);

            OfertasParaTiRespuestaDTO r = await servicio.Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.SinPrecios, HOY);

            Assert.IsTrue(r.PrecioOculto);
            Assert.AreEqual(2, r.Ofertas.Count);
            Assert.IsTrue(r.Ofertas.All(o => o.Precio == null && o.PrecioAntes == null && o.Descuento == null));
        }

        [TestMethod]
        public async Task Cliente_SiTodoFalla_SinOfertasYNoLanza()
        {
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._)).Throws(new Exception("BD caída"));
            var servicio = new ServicioOfertasCliente(Nucleo(), A.Fake<ICalculadorPreciosOferta>(), c => Task.FromResult("0"), registrar: errores.Add);

            OfertasParaTiRespuestaDTO r = await servicio.Calcular("1000", BF, 5, PoliticaPreciosOcultos.NivelPrecios.Completo, HOY);

            Assert.IsFalse(r.HayOfertas);
            Assert.AreEqual(0, r.Ofertas.Count);
            Assert.AreEqual(BF, r.Campana);
            Assert.AreEqual(1, errores.Count);
        }

        // ---------------------------------------------------------------- push

        private IFuentePushCampana Fuente(params string[] conApp)
        {
            IFuentePushCampana fuente = A.Fake<IFuentePushCampana>();
            A.CallTo(() => fuente.ClientesConConsejos()).Returns(conApp.ToList());
            A.CallTo(() => fuente.ClientesDeBaja()).Returns(new HashSet<string>());
            A.CallTo(() => fuente.ClientesConPushDeCampana(A<string>._, A<IReadOnlyCollection<string>>._, A<DateTime>._)).Returns(new HashSet<string>());
            return fuente;
        }

        [TestMethod]
        public async Task Push_Ensayo_NoEnviaNiEscribe()
        {
            IFuentePushCampana fuente = Fuente("1000", "2000");
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var servicio = new ServicioPushCampana(Nucleo(), fuente, push, p => Task.FromResult("https://tienda.nuevavision.es/" + p + ".jpg"), errores.Add);

            ResultadoPushCampana r = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, true, AHORA, "Carlos");

            Assert.AreEqual(EstadoPushCampana.Ok, r.Estado);
            Assert.IsTrue(r.Respuesta.Ensayo);
            Assert.AreEqual(2, r.Respuesta.Destinatarios);
            Assert.AreEqual(2, r.Respuesta.ConOfertas);
            Assert.AreEqual("Black Friday", r.Respuesta.Mensajes[0].Titulo);
            Assert.AreEqual("Producto 1 (−30 %) y Producto 3, pensados para ti.", r.Respuesta.Mensajes[0].Cuerpo);
            Assert.AreEqual("https://tienda.nuevavision.es/1.jpg", r.Respuesta.Mensajes[0].ImagenUrl);
            CollectionAssert.AreEqual(new[] { "1", "3" }, r.Respuesta.Mensajes[0].Productos);
            Assert.AreEqual(0, r.Respuesta.Mensajes[0].Enviados);
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => fuente.Anotar(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Push_DeVerdad_EnviaAnotaYUnoQueFallaNoParaALosDemas()
        {
            IFuentePushCampana fuente = Fuente("1000", "2000");
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, "1000", A<NotificacionPushDTO>._)).Throws(new Exception("FCM"));
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, "2000", A<NotificacionPushDTO>._)).Returns(2);
            var servicio = new ServicioPushCampana(Nucleo(), fuente, push, p => Task.FromResult<string>(null), errores.Add);

            ResultadoPushCampana r = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF, ImagenUrl = "https://x.es/bf.jpg" }, false, AHORA, "Carlos");

            Assert.AreEqual(2, r.Respuesta.Mensajes.Single().Enviados);
            Assert.AreEqual("2000", r.Respuesta.Mensajes.Single().Cliente);
            Assert.AreEqual("https://x.es/bf.jpg", r.Respuesta.Mensajes.Single().ImagenUrl);
            Assert.IsTrue(r.Respuesta.Saltados.Any(s => s.Cliente == "1000" && s.Motivo.StartsWith("Error")));
            A.CallTo(() => push.EnviarConsejoACliente("1", "2000", A<NotificacionPushDTO>.That.Matches(n =>
                n.Datos["tipo"] == "ofertas" && n.Datos["campana"] == BF && !n.Datos.ContainsKey("productos")))).MustHaveHappenedOnceExactly();
            A.CallTo(() => fuente.Anotar(A<string>.That.Contains("\"ConOfertas\":2"), "Carlos")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Push_YaRecibidaEnLosDiasSinRepetir_SeSalta()
        {
            IFuentePushCampana fuente = Fuente("1000", "2000");
            A.CallTo(() => fuente.ClientesConPushDeCampana(BF, A<IReadOnlyCollection<string>>._, new DateTime(2026, 11, 25)))
                .Returns(new HashSet<string> { "1000" });
            var servicio = new ServicioPushCampana(Nucleo(), fuente, A.Fake<IServicioNotificacionesPush>(), p => Task.FromResult<string>(null), errores.Add);

            ResultadoPushCampana r = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF, DiasSinRepetir = 2 }, true, AHORA, "Carlos");

            Assert.AreEqual(1, r.Respuesta.Destinatarios);
            Assert.AreEqual("2000", r.Respuesta.Mensajes.Single().Cliente);
            Assert.AreEqual("1000", r.Respuesta.Saltados.Single().Cliente);
        }

        [TestMethod]
        public async Task Push_SinOfertas_GenericoOSaltado()
        {
            A.CallTo(() => lector.LeerPendientes(A<string>._, A<IReadOnlyCollection<string>>._)).Returns(new List<PendienteClienteOferta>
            {
                new PendienteClienteOferta { Cliente = "1000", Producto = "1" }, new PendienteClienteOferta { Cliente = "1000", Producto = "3" }
            });
            var servicio = new ServicioPushCampana(Nucleo(), Fuente("1000"), A.Fake<IServicioNotificacionesPush>(), p => Task.FromResult<string>(null), errores.Add);

            ResultadoPushCampana generico = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, true, AHORA, "Carlos");
            Assert.AreEqual(1, generico.Respuesta.SinOfertas);
            Assert.AreEqual("Ya tienes las ofertas de Black Friday en la app. ¡Échales un vistazo!", generico.Respuesta.Mensajes.Single().Cuerpo);
            Assert.AreEqual(0, generico.Respuesta.Mensajes.Single().Productos.Count);

            ResultadoPushCampana saltado = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF, IncluirSinOfertas = false }, true, AHORA, "Carlos");
            Assert.AreEqual(0, saltado.Respuesta.Mensajes.Count);
            Assert.AreEqual(ReglaPushCampana.MOTIVO_SIN_OFERTAS, saltado.Respuesta.Saltados.Single().Motivo);
        }

        [TestMethod]
        public async Task Push_DeVerdadFueraDeHorario_NoSeManda()
        {
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var servicio = new ServicioPushCampana(Nucleo(), Fuente("1000"), push, p => Task.FromResult<string>(null), errores.Add);

            ResultadoPushCampana r = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, false, new DateTime(2026, 11, 26, 22, 0, 0), "Carlos");

            Assert.AreEqual(EstadoPushCampana.PeticionIncorrecta, r.Estado);
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Push_CampanaSinOfertasVigentes_400()
        {
            A.CallTo(() => lector.LeerFilasCampana(A<string>._, A<string>._, A<DateTime>._)).Returns(new List<FilaCampanaOferta>());
            var servicio = new ServicioPushCampana(Nucleo(), Fuente("1000"), A.Fake<IServicioNotificacionesPush>(), p => Task.FromResult<string>(null), errores.Add);

            ResultadoPushCampana r = await servicio.Ejecutar(new PeticionCampanaPushDTO { Campana = BF }, true, AHORA, "Carlos");

            Assert.AreEqual(EstadoPushCampana.PeticionIncorrecta, r.Estado);
        }
    }
}
