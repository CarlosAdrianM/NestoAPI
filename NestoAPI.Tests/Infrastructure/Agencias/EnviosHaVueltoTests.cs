using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#870 (novedad 1280): la caja de un devuelto «ha vuelto» (Ariadna, «Ya está aquí» de Nesto o forzado al imprimir) y
    /// solo sale el reenvío de lo que está aquí. El caso: el 250950, reenvío del 250875 de CTT incidentado, salió en el manifiesto
    /// con «Tramitar todos» sin que la caja hubiera vuelto.
    /// </summary>
    [TestClass]
    public class EnviosHaVueltoTests
    {
        private const int ORIGEN = 250875;
        private const int NUEVO = 250950;

        // ===== Reglas puras =====

        [TestMethod]
        public void CandidatosCodigo_LoLeidoYSinElSufijoDeBulto()
        {
            CollectionAssert.AreEqual(new List<string> { "6522393001", "652239300", "65223930", "6522393", "652239" }.Take(3).ToList(),
                ReglasHaVuelto.CandidatosCodigo(" 6522393001 ").Take(3).ToList());
            List<string> candidatos = ReglasHaVuelto.CandidatosCodigo("61083520250875001");
            CollectionAssert.Contains(candidatos, "61083520250875001");
            CollectionAssert.Contains(candidatos, "61083520250875", "Sin las 3 cifras del bulto: el código del envío de GLS");
            Assert.AreEqual(5, candidatos.Count, "Lo leído y hasta 4 cifras menos");
        }

        [TestMethod]
        public void CandidatosCodigo_UnCodigoCortoNoSeRecortaYLasLetrasParan()
        {
            CollectionAssert.AreEqual(new List<string> { "250875" }, ReglasHaVuelto.CandidatosCodigo("250875"));
            Assert.AreEqual("RP250875-1", ReglasHaVuelto.CandidatosCodigo(" rp250875-1 ")[0], "Sin espacios y en mayúsculas");
            Assert.AreEqual(0, ReglasHaVuelto.CandidatosCodigo("   ").Count);
        }

        [TestMethod]
        public void NumeroEnvioTecleado_SoloCifrasYHastaSiete()
        {
            Assert.AreEqual(250875, ReglasHaVuelto.NumeroEnvioTecleado(" 250875 "));
            Assert.IsNull(ReglasHaVuelto.NumeroEnvioTecleado("61083520250875"));
            Assert.IsNull(ReglasHaVuelto.NumeroEnvioTecleado("RP250875"));
            Assert.IsNull(ReglasHaVuelto.NumeroEnvioTecleado("0"));
        }

        [TestMethod]
        public void Elegir_ElExactoYLuegoElDevuelto()
        {
            var exacto = new EnviosAgencia { Numero = 1, CodigoBarras = "6522393001", Estado = Constantes.Agencias.ESTADO_ENTREGADO };
            var corto = new EnviosAgencia { Numero = 2, CodigoBarras = "6522393", Estado = Constantes.Agencias.ESTADO_DEVUELTO };
            Assert.AreEqual(1, ReglasHaVuelto.Elegir(new[] { corto, exacto }, "6522393001", out _).Numero);

            var devuelto = new EnviosAgencia { Numero = 3, CodigoBarras = "X1", Estado = Constantes.Agencias.ESTADO_DEVUELTO };
            var entregado = new EnviosAgencia { Numero = 4, CodigoBarras = "X1", Estado = Constantes.Agencias.ESTADO_ENTREGADO };
            Assert.AreEqual(3, ReglasHaVuelto.Elegir(new[] { entregado, devuelto }, "X1", out _).Numero);

            var otroDevuelto = new EnviosAgencia { Numero = 5, CodigoBarras = "X1", Estado = Constantes.Agencias.ESTADO_INCIDENTADO };
            Assert.IsNull(ReglasHaVuelto.Elegir(new[] { devuelto, otroDevuelto }, "X1", out List<int> empatados));
            CollectionAssert.AreEqual(new List<int> { 3, 5 }, empatados);
        }

        [TestMethod]
        public void EsperaLaCaja_SoloElReenvioSinTramitarYSinVuelta()
        {
            RegistroReenvioEnvio fila = Fila();
            Assert.IsTrue(ReglasHaVuelto.EsperaLaCaja(fila, NUEVO, (short)Constantes.Agencias.ESTADO_PENDIENTE, null));
            Assert.IsTrue(ReglasHaVuelto.EsperaLaCaja(fila, NUEVO, (short)Constantes.Agencias.ESTADO_EN_CURSO, null), "Uno de antes de #870 en curso tampoco sale");
            Assert.IsFalse(ReglasHaVuelto.EsperaLaCaja(fila, NUEVO, Constantes.Agencias.ESTADO_TRAMITADO, null), "Lo ya tramitado no se toca");
            Assert.IsFalse(ReglasHaVuelto.EsperaLaCaja(fila, NUEVO, (short)Constantes.Agencias.ESTADO_PENDIENTE, new RegistroHaVuelto { Envio = ORIGEN }));
            Assert.IsFalse(ReglasHaVuelto.EsperaLaCaja(null, NUEVO, (short)Constantes.Agencias.ESTADO_PENDIENTE, null), "Un envío que no es reenvío");
            Assert.IsFalse(ReglasHaVuelto.EsperaLaCaja(new RegistroReenvioEnvio { EnvioOrigen = ORIGEN, Decision = DecisionesReenvio.ABONAR },
                NUEVO, (short)Constantes.Agencias.ESTADO_PENDIENTE, null));
        }

        [TestMethod]
        public void PutLoHaceSalir_DePendienteAEnCursoODeEnCursoATramitado()
        {
            Assert.IsTrue(ReglasHaVuelto.PutLoHaceSalir(-1, 0));
            Assert.IsTrue(ReglasHaVuelto.PutLoHaceSalir(-1, 1));
            Assert.IsTrue(ReglasHaVuelto.PutLoHaceSalir(0, 1));
            Assert.IsFalse(ReglasHaVuelto.PutLoHaceSalir(-1, -1), "Guardar un pendiente sin imprimirlo");
            Assert.IsFalse(ReglasHaVuelto.PutLoHaceSalir(0, 0));
            Assert.IsFalse(ReglasHaVuelto.PutLoHaceSalir(1, 1));
        }

        [TestMethod]
        public void MensajeEsperandoCaja_DiceQueEnvioYComoMarcarlo()
        {
            string m = ReglasHaVuelto.MensajeEsperandoCaja(ORIGEN, NUEVO);
            Assert.AreEqual("La caja del envío 250875 todavía no ha vuelto al almacén: el envío 250950 es su reenvío y no puede salir hasta " +
                "que esté aquí. Cuando llegue, léala en Ariadna («Ha vuelto un paquete») o pulse «Ya está aquí» en Agencias › Devueltos.", m);
        }

        // ===== El servicio =====

        [TestMethod]
        public async Task MarcarPorLectura_ConReenvioEsperando_MarcaAvisaAQuienLoPidioYLoDice()
        {
            var datos = new DatosFalsos();
            datos.ConOrigenYReenvio();
            var avisos = A.Fake<IAvisosEnvioHaVuelto>();
            var servicio = new ServicioEnviosHaVuelto(datos, avisos);

            ResultadoHaVuelto r = await servicio.MarcarPorLecturaAsync("6522393001002", "Alfredo");

            Assert.AreEqual(TipoResultadoHaVuelto.Correcto, r.Tipo, r.Mensaje);
            Assert.AreEqual(ORIGEN, r.Envio.Envio, "La etiqueta del bulto 2 casa con el código del envío");
            Assert.IsFalse(r.Envio.YaEstabaMarcado);
            Assert.IsTrue(r.Envio.ReenvioEsperando);
            Assert.AreEqual(NUEVO, r.Envio.EnvioNuevo);
            Assert.AreEqual("Inaki", r.Envio.AvisoEnviadoA);
            Assert.AreEqual(ComoHaVuelto.LECTURA, datos.Marcas[ORIGEN].Como);
            Assert.AreEqual("6522393001002", datos.Marcas[ORIGEN].Codigo);
            StringAssert.Contains(datos.HistoriaMarca, "leída en Ariadna");
            Assert.AreEqual("La caja del envío 250875 de CTT (15191/0 PELUQUERÍA PEPA) ha vuelto al almacén. Ya se puede imprimir la etiqueta " +
                "del envío 250950 (su reenvío); se ha avisado a Inaki.", r.Envio.Mensaje);
            A.CallTo(() => avisos.CajaHaVuelto(A<EnvioHaVueltoDTO>.That.Matches(d => d.Envio == ORIGEN && d.EnvioNuevo == NUEVO), "NUEVAVISION\\Inaki"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Marcar_DosVeces_EsIdempotenteYNoAvisaOtraVez()
        {
            var datos = new DatosFalsos();
            datos.ConOrigenYReenvio();
            var avisos = A.Fake<IAvisosEnvioHaVuelto>();
            var servicio = new ServicioEnviosHaVuelto(datos, avisos);

            _ = await servicio.MarcarAsync(ORIGEN, ComoHaVuelto.YA_ESTA_AQUI, null, "Alfredo");
            ResultadoHaVuelto segunda = await servicio.MarcarPorLecturaAsync(ORIGEN.ToString(), "Andre");

            Assert.AreEqual(TipoResultadoHaVuelto.Correcto, segunda.Tipo);
            Assert.IsTrue(segunda.Envio.YaEstabaMarcado);
            Assert.AreEqual(ComoHaVuelto.YA_ESTA_AQUI, segunda.Envio.ComoHaVuelto, "Se queda la marca original");
            Assert.AreEqual("Alfredo", segunda.Envio.UsuarioHaVuelto);
            StringAssert.StartsWith(segunda.Envio.Mensaje, "Ya estaba marcado: la caja del envío 250875 de CTT");
            Assert.AreEqual(1, datos.Inserciones);
            A.CallTo(() => avisos.CajaHaVuelto(A<EnvioHaVueltoDTO>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Marcar_SinReenvio_SeMarcaIgualYNoAvisa()
        {
            var datos = new DatosFalsos();
            datos.Envios[ORIGEN] = Origen();
            var avisos = A.Fake<IAvisosEnvioHaVuelto>();

            ResultadoHaVuelto r = await new ServicioEnviosHaVuelto(datos, avisos).MarcarAsync(ORIGEN, ComoHaVuelto.YA_ESTA_AQUI, null, "Alfredo");

            Assert.AreEqual(TipoResultadoHaVuelto.Correcto, r.Tipo);
            Assert.AreEqual(DecisionesReenvio.SIN_DECIDIR, r.Envio.Decision);
            Assert.IsTrue(datos.Marcas.ContainsKey(ORIGEN));
            StringAssert.EndsWith(r.Envio.Mensaje, "No tiene reenvío: decida en Agencias › Devueltos si se vuelve a enviar o se abona.");
            A.CallTo(() => avisos.CajaHaVuelto(A<EnvioHaVueltoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Marcar_ElMismoQueLoPidioOForzadoAlImprimir_NoAvisa()
        {
            var datos = new DatosFalsos();
            datos.ConOrigenYReenvio();
            var avisos = A.Fake<IAvisosEnvioHaVuelto>();
            _ = await new ServicioEnviosHaVuelto(datos, avisos).MarcarAsync(ORIGEN, ComoHaVuelto.YA_ESTA_AQUI, null, "Inaki");

            var datos2 = new DatosFalsos();
            datos2.ConOrigenYReenvio();
            _ = await new ServicioEnviosHaVuelto(datos2, avisos).MarcarAsync(ORIGEN, ComoHaVuelto.AL_IMPRIMIR, null, "Alfredo");

            A.CallTo(() => avisos.CajaHaVuelto(A<EnvioHaVueltoDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Marcar_UnEnvioQueNoHaSalido_409()
        {
            var datos = new DatosFalsos();
            datos.ConOrigenYReenvio();

            ResultadoHaVuelto r = await new ServicioEnviosHaVuelto(datos, null).MarcarAsync(NUEVO, ComoHaVuelto.YA_ESTA_AQUI, null, "Alfredo");

            Assert.AreEqual(TipoResultadoHaVuelto.Conflicto, r.Tipo);
            Assert.AreEqual(ResultadoHaVuelto.CODIGO_NO_HA_SALIDO, r.Codigo);
            Assert.AreEqual("El envío 250950 no ha salido del almacén (está pendiente): no puede haber vuelto.", r.Mensaje);
            Assert.AreEqual(0, datos.Inserciones);
        }

        [TestMethod]
        public async Task MarcarPorLectura_SinEnvioSinCodigoOSinTabla()
        {
            var datos = new DatosFalsos();
            datos.ConOrigenYReenvio();
            var servicio = new ServicioEnviosHaVuelto(datos, null);

            ResultadoHaVuelto noHay = await servicio.MarcarPorLecturaAsync("999999999999", "Alfredo");
            Assert.AreEqual(TipoResultadoHaVuelto.NoEncontrado, noHay.Tipo);
            Assert.AreEqual("No hay ningún envío con la etiqueta «999999999999». Lea la etiqueta de la agencia o teclee el número del envío.", noHay.Mensaje);

            Assert.AreEqual(TipoResultadoHaVuelto.Invalido, (await servicio.MarcarPorLecturaAsync("  ", "Alfredo")).Tipo);

            datos.TablaHaVuelto = false;
            ResultadoHaVuelto sinTabla = await servicio.MarcarPorLecturaAsync("6522393001", "Alfredo");
            Assert.AreEqual(TipoResultadoHaVuelto.SinTabla, sinTabla.Tipo);
            StringAssert.Contains(sinTabla.Mensaje, "Issue870_EnviosAgenciaHaVuelto.sql");
        }

        [TestMethod]
        public async Task EsperandoCaja_ElReenvioSinVuelta_YYaNoTrasMarcar()
        {
            var datos = new DatosFalsos();
            datos.ConOrigenYReenvio();
            var servicio = new ServicioEnviosHaVuelto(datos, null);

            ReenvioEsperandoCaja esperando = await servicio.EsperandoCajaAsync(NUEVO);
            Assert.IsNotNull(esperando);
            Assert.AreEqual(ORIGEN, esperando.EnvioOrigen);
            Assert.IsNull(await servicio.EsperandoCajaAsync(ORIGEN), "El original no es un reenvío");

            _ = await servicio.MarcarAsync(ORIGEN, ComoHaVuelto.YA_ESTA_AQUI, null, "Alfredo");
            Assert.IsNull(await servicio.EsperandoCajaAsync(NUEVO), "Con la caja aquí, el reenvío ya puede salir");
        }

        [TestMethod]
        public async Task EsperandoCaja_SinLasTablas_NoActua()
        {
            var datos = new DatosFalsos();
            datos.ConOrigenYReenvio();
            datos.TablaHaVuelto = false;
            Assert.IsNull(await new ServicioEnviosHaVuelto(datos, null).EsperandoCajaAsync(NUEVO), "Sin el script, todo como antes de #870");
        }

        // ===== El aviso =====

        [TestMethod]
        public async Task Aviso_CampanaDeNestoYPushDeNestoApp()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var avisos = new AvisosEnvioHaVuelto(() => push, null);
            var dto = new EnvioHaVueltoDTO { Envio = ORIGEN, EnvioNuevo = NUEVO, Empresa = "1", Cliente = "15191", Contacto = "0", Nombre = "PELUQUERÍA PEPA", Pedido = 930487 };

            await avisos.Avisar(dto, "NUEVAVISION\\Inaki");

            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Inaki", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == AvisosEnvioHaVuelto.TIPO
                    && n.Titulo == "Ha vuelto la caja del envío 250875"
                    && n.Cuerpo == "La caja del envío 250875 ha vuelto: ya se puede imprimir la etiqueta del 250950 (15191/0 PELUQUERÍA PEPA, pedido 930487)."
                    && n.Datos["pedido"] == "930487" && n.Datos["envioNuevo"] == "250950")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarAUsuario("Inaki", Constantes.Aplicaciones.NESTO_APP, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Aviso_VendedorDeNestoApp_SoloPush()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            await new AvisosEnvioHaVuelto(() => push, null).Avisar(new EnvioHaVueltoDTO { Envio = ORIGEN, EnvioNuevo = NUEVO }, "carlos");

            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => push.EnviarAUsuario("carlos", Constantes.Aplicaciones.NESTO_APP, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        // ===== Devueltos =====

        [TestMethod]
        public void Combinar_HaVueltoYEsperandoCaja()
        {
            var listado = new List<EnvioAgenciaListadoDTO>
            {
                new EnvioAgenciaListadoDTO { Numero = ORIGEN, Estado = Constantes.Agencias.ESTADO_INCIDENTADO, Fecha = new DateTime(2026, 11, 12) },
                new EnvioAgenciaListadoDTO { Numero = 250876, Estado = Constantes.Agencias.ESTADO_DEVUELTO, Fecha = new DateTime(2026, 11, 12) }
            };
            var decisiones = new Dictionary<int, RegistroReenvioEnvio>
            {
                [ORIGEN] = Fila(),
                [250876] = new RegistroReenvioEnvio { EnvioOrigen = 250876, Decision = DecisionesReenvio.REENVIAR, EnvioNuevo = 250951 }
            };
            var estados = new Dictionary<int, short> { [NUEVO] = (short)Constantes.Agencias.ESTADO_PENDIENTE, [250951] = (short)Constantes.Agencias.ESTADO_PENDIENTE };
            var vueltas = new Dictionary<int, RegistroHaVuelto>
            {
                [250876] = new RegistroHaVuelto { Envio = 250876, Fecha = new DateTime(2026, 11, 16, 9, 30, 0), Usuario = "Andre", Como = ComoHaVuelto.LECTURA }
            };

            List<EnvioDevueltoDTO> r = ServicioReenviosEnvio.Combinar(listado, decisiones, estados, vueltas);

            EnvioDevueltoDTO esperando = r.Single(d => d.Numero == ORIGEN);
            Assert.IsTrue(esperando.ReenvioEsperandoCaja);
            Assert.IsFalse(esperando.HaVuelto);
            EnvioDevueltoDTO vuelto = r.Single(d => d.Numero == 250876);
            Assert.IsTrue(vuelto.HaVuelto);
            Assert.IsFalse(vuelto.ReenvioEsperandoCaja);
            Assert.AreEqual(new DateTime(2026, 11, 16, 9, 30, 0), vuelto.FechaHaVuelto);
            Assert.AreEqual("Andre", vuelto.UsuarioHaVuelto);
            Assert.AreEqual(ComoHaVuelto.LECTURA, vuelto.ComoHaVuelto);

            Assert.IsFalse(ServicioReenviosEnvio.Combinar(listado, decisiones, estados).Any(d => d.ReenvioEsperandoCaja),
                "Sin la tabla nada se da por esperando");
        }

        [TestMethod]
        public void TextoReenvioCreado_SegunLaCajaHayaVuelto()
        {
            StringAssert.Contains(ServicioReenviosEnvio.TextoReenvioCreado(NUEVO, ORIGEN, true), "ya ha vuelto al almacén: puede imprimir su etiqueta");
            StringAssert.Contains(ServicioReenviosEnvio.TextoReenvioCreado(NUEVO, ORIGEN, false), "del envío 250875 esté de vuelta en el almacén");
        }

        // ===== La guarda en el controlador =====

        private static EnviosAgenciasController Controlador(NVEntities db, IServicioEnviosHaVuelto haVuelto, ITramitacionEnviosService cierre)
        {
            var controller = new EnviosAgenciasController(db, cierre)
            {
                ServicioHaVuelto = haVuelto,
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/1") }
            };
            controller.Configuration = new HttpConfiguration();
            return controller;
        }

        private static IServicioEnviosHaVuelto Esperando()
        {
            var haVuelto = A.Fake<IServicioEnviosHaVuelto>();
            A.CallTo(() => haVuelto.EsperandoCajaAsync(A<int>._)).Returns(Task.FromResult<ReenvioEsperandoCaja>(null));
            A.CallTo(() => haVuelto.EsperandoCajaAsync(NUEVO)).Returns(Task.FromResult(new ReenvioEsperandoCaja
            {
                EnvioOrigen = ORIGEN,
                EnvioNuevo = NUEVO,
                Mensaje = ReglasHaVuelto.MensajeEsperandoCaja(ORIGEN, NUEVO)
            }));
            A.CallTo(() => haVuelto.MarcarAsync(A<int>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(ResultadoHaVuelto.Correcto(new EnvioHaVueltoDTO { Envio = ORIGEN })));
            return haVuelto;
        }

        [TestMethod]
        public async Task ConfirmarTramitacion_ReenvioSinCaja_409EsperandoCajaYNoTramita()
        {
            var cierre = A.Fake<ITramitacionEnviosService>();
            var haVuelto = Esperando();
            EnviosAgenciasController controller = Controlador(A.Fake<NVEntities>(), haVuelto, cierre);

            IHttpActionResult resultado = await controller.ConfirmarTramitacion(NUEVO);

            var mensaje = resultado as ResponseMessageResult;
            Assert.IsNotNull(mensaje);
            Assert.AreEqual(HttpStatusCode.Conflict, mensaje.Response.StatusCode);
            HttpError error = await mensaje.Response.Content.ReadAsAsync<HttpError>();
            Assert.AreEqual(ReenvioEsperandoCaja.CODIGO, error["Codigo"]);
            Assert.AreEqual(ReglasHaVuelto.MensajeEsperandoCaja(ORIGEN, NUEVO), error.Message);
            A.CallTo(() => cierre.TramitarAsync(A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => haVuelto.MarcarAsync(A<int>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ConfirmarTramitacion_Forzado_MarcaQueHaVueltoYTramita()
        {
            var cierre = A.Fake<ITramitacionEnviosService>();
            A.CallTo(() => cierre.TramitarAsync(NUEVO, A<string>._)).Returns(Task.FromResult(new ResultadoTramitacionEnvio { Numero = NUEVO }));
            var haVuelto = Esperando();
            EnviosAgenciasController controller = Controlador(A.Fake<NVEntities>(), haVuelto, cierre);

            IHttpActionResult resultado = await controller.ConfirmarTramitacion(NUEVO, yaEstaAqui: true);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoTramitacionEnvio>));
            A.CallTo(() => haVuelto.MarcarAsync(ORIGEN, ComoHaVuelto.AL_IMPRIMIR, null, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => cierre.TramitarAsync(NUEVO, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ConfirmarTramitacion_SiLaGuardaFalla_SigueComoAntes()
        {
            var cierre = A.Fake<ITramitacionEnviosService>();
            A.CallTo(() => cierre.TramitarAsync(NUEVO, A<string>._)).Returns(Task.FromResult(new ResultadoTramitacionEnvio { Numero = NUEVO }));
            var haVuelto = A.Fake<IServicioEnviosHaVuelto>();
            A.CallTo(() => haVuelto.EsperandoCajaAsync(A<int>._)).Throws(new InvalidOperationException("sin BD"));
            EnviosAgenciasController controller = Controlador(A.Fake<NVEntities>(), haVuelto, cierre);
            var registrados = new List<Exception>();
            controller.RegistrarEnElmah = registrados.Add;

            IHttpActionResult resultado = await controller.ConfirmarTramitacion(NUEVO);

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ResultadoTramitacionEnvio>));
            Assert.AreEqual(1, registrados.Count);
        }

        [TestMethod]
        public async Task TramitarEnvio_ReenvioPendienteSinCaja_409SinLlamarALaAgencia()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<EnviosAgencia> envios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            A.CallTo(() => db.EnviosAgencias).Returns(envios);
            A.CallTo(() => envios.FindAsync(NUEVO)).Returns(Task.FromResult(new EnviosAgencia
            {
                Numero = NUEVO,
                Agencia = Constantes.Agencias.AGENCIA_CTT,
                Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE
            }));
            var fabrica = A.Fake<IFabricaAgenciasRemotas>();
            var controller = new EnviosAgenciasController(db, fabrica)
            {
                ServicioHaVuelto = Esperando(),
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/250950/Tramitar") }
            };

            IHttpActionResult resultado = await controller.TramitarEnvio(NUEVO);

            Assert.AreEqual(HttpStatusCode.Conflict, (resultado as ResponseMessageResult)?.Response.StatusCode);
            A.CallTo(() => fabrica.Crear(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task TramitarTodosGls_SeSaltaElReenvioSinCajaYTramitaLosDemas()
        {
            NVEntities db = A.Fake<NVEntities>();
            DbSet<EnviosAgencia> fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            DbSet<AgenciaLlamadaWeb> fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            var envios = new List<EnviosAgencia>();
            foreach ((int numero, int pedido) in new[] { (NUEVO, 930487), (250951, 930500) })
            {
                EnviosAgencia envio = TramitadorGlsTests.Envio();
                envio.Numero = numero;
                envio.Pedido = pedido;
                envio.CodigoBarras = PlazaYCodigoBarrasGls.CodigoBarras(envio, "6108352");
                envios.Add(envio);
                A.CallTo(() => fakeEnvios.FindAsync(numero)).Returns(Task.FromResult(envio));
            }
            var gls = A.Fake<IClienteGrabaServiciosGls>();
            A.CallTo(() => gls.Url).Returns(PeticionGls.URL_GRABA_SERVICIOS);
            A.CallTo(() => gls.EnviarAsync(A<string>._)).Returns(Task.FromResult(TramitadorGlsTests.RESPUESTA_OK));
            var cierre = A.Fake<ITramitacionEnviosService>();
            A.CallTo(() => cierre.TramitarAsync(A<int>._, A<string>._)).ReturnsLazily((int numero, string _) =>
                Task.FromResult(new ResultadoTramitacionEnvio { Numero = numero, Mensaje = "ok" }));
            var controller = new EnviosAgenciasController(db, cierre)
            {
                ServicioHaVuelto = Esperando(),
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                GlsTramitarPorApi = () => true,
                ClienteGls = gls,
                HoyGls = () => new DateTime(2026, 11, 16),
                CargarDatosGls = e => Task.FromResult((TramitadorGlsTests.EmpresaNuevaVision(),
                    new AgenciaTransporte { Numero = 1, Nombre = "ASM", Identificador = "9ABAEE33", PrefijoCodigoBarras = "6108352" })),
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/TramitarTodos") }
            };

            var respuesta = await controller.TramitarTodos(new TramitarEnviosDTO { Agencia = 1, Envios = new List<int> { NUEVO, 250951 } })
                as OkNegotiatedContentResult<ResultadoTramitarEnviosDTO>;

            Assert.IsNotNull(respuesta);
            ResultadoTramitarEnvioDTO saltado = respuesta.Content.Envios.Single(e => e.Numero == NUEVO);
            Assert.AreEqual(ResultadoTramitarEnvioDTO.ESPERANDO_CAJA, saltado.Resultado);
            Assert.AreEqual(ReglasHaVuelto.MensajeEsperandoCaja(ORIGEN, NUEVO), saltado.Mensaje);
            Assert.AreEqual(1, respuesta.Content.EsperandoCaja);
            Assert.AreEqual(1, respuesta.Content.Tramitados);
            A.CallTo(() => cierre.TramitarAsync(NUEVO, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => cierre.TramitarAsync(250951, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => gls.EnviarAsync(A<string>._)).MustHaveHappenedOnceExactly();
        }

        // ===== Ayudas =====

        private static RegistroReenvioEnvio Fila() => new RegistroReenvioEnvio
        {
            EnvioOrigen = ORIGEN,
            Decision = DecisionesReenvio.REENVIAR,
            EnvioNuevo = NUEVO,
            Usuario = "NUEVAVISION\\Inaki",
            Fecha = new DateTime(2026, 11, 13)
        };

        private static EnviosAgencia Origen() => new EnviosAgencia
        {
            Numero = ORIGEN,
            Empresa = "1  ",
            Agencia = Constantes.Agencias.AGENCIA_CTT,
            Estado = Constantes.Agencias.ESTADO_INCIDENTADO,
            CodigoBarras = "6522393001",
            Pedido = 930487,
            Cliente = "15191     ",
            Contacto = "0  ",
            Nombre = "PELUQUERÍA PEPA",
            Poblacion = "ALGETE",
            Bultos = 2
        };

        private sealed class DatosFalsos : IDatosEnviosHaVuelto
        {
            public bool TablaHaVuelto { get; set; } = true;
            public Dictionary<int, EnviosAgencia> Envios { get; } = new Dictionary<int, EnviosAgencia>();
            public Dictionary<int, RegistroReenvioEnvio> Filas { get; } = new Dictionary<int, RegistroReenvioEnvio>();
            public Dictionary<int, RegistroHaVuelto> Marcas { get; } = new Dictionary<int, RegistroHaVuelto>();
            public int Inserciones { get; set; }
            public string HistoriaMarca { get; set; }

            public IRepositorioEnviosHaVuelto HaVuelto => new RepoHaVuelto(this);
            public IRepositorioReenviosEnvio Reenvios
            {
                get
                {
                    var repo = A.Fake<IRepositorioReenviosEnvio>();
                    A.CallTo(() => repo.TablaDisponible()).Returns(true);
                    A.CallTo(() => repo.LeerAsync(A<int>._)).ReturnsLazily((int n) => Task.FromResult(Filas.TryGetValue(n, out RegistroReenvioEnvio f) ? f : null));
                    return repo;
                }
            }

            public void ConOrigenYReenvio()
            {
                Envios[ORIGEN] = Origen();
                Envios[NUEVO] = new EnviosAgencia { Numero = NUEVO, Agencia = Constantes.Agencias.AGENCIA_CTT, Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE, Pedido = 930487 };
                Filas[ORIGEN] = Fila();
            }

            public Task<EnviosAgencia> LeerEnvioAsync(int numero) => Task.FromResult(Envios.TryGetValue(numero, out EnviosAgencia e) ? e : null);

            public Task<List<EnviosAgencia>> BuscarPorCodigosAsync(IList<string> codigos)
                => Task.FromResult(Envios.Values.Where(e => e.CodigoBarras != null && codigos.Contains(e.CodigoBarras.Trim())).ToList());

            public Task<string> NombreAgenciaAsync(int agencia) => Task.FromResult("CTT");

            public Task<RegistroReenvioEnvio> LeerReenvioPorNuevoAsync(int envioNuevo)
                => Task.FromResult(Filas.Values.FirstOrDefault(f => f.EnvioNuevo == envioNuevo && f.Decision == DecisionesReenvio.REENVIAR));

            public Task<short?> LeerEstadoAsync(int envio) => Task.FromResult(Envios.TryGetValue(envio, out EnviosAgencia e) ? e.Estado : (short?)null);

            private sealed class RepoHaVuelto : IRepositorioEnviosHaVuelto
            {
                private readonly DatosFalsos d;
                public RepoHaVuelto(DatosFalsos d) { this.d = d; }
                public bool TablaDisponible() => d.TablaHaVuelto;
                public Task<RegistroHaVuelto> LeerAsync(int envio) => Task.FromResult(d.Marcas.TryGetValue(envio, out RegistroHaVuelto r) ? r : null);
                public Task<Dictionary<int, RegistroHaVuelto>> LeerAsync(IEnumerable<int> envios)
                    => Task.FromResult(envios.Where(d.Marcas.ContainsKey).ToDictionary(n => n, n => d.Marcas[n]));
                public Task<bool> MarcarAsync(RegistroHaVuelto registro, string observacionesHistoria)
                {
                    if (d.Marcas.ContainsKey(registro.Envio))
                    {
                        return Task.FromResult(false);
                    }
                    d.Inserciones++;
                    d.HistoriaMarca = observacionesHistoria;
                    d.Marcas[registro.Envio] = new RegistroHaVuelto
                    {
                        Envio = registro.Envio,
                        Fecha = new DateTime(2026, 11, 16, 10, 5, 0),
                        Usuario = registro.Usuario,
                        Como = registro.Como,
                        Codigo = registro.Codigo
                    };
                    return Task.FromResult(true);
                }
            }
        }
    }
}
