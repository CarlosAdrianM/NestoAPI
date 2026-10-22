using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Tests.Infraestructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.CorreosPostCompra
{
    /// <summary>
    /// NestoAPI#662: la push de reposición a los clientes de NestoTiendas. Mismo cálculo que el correo de #532, con sus
    /// propias reglas para no agobiar: L-J antes de la hora límite y sin víspera de festivo, solo a quien tiene la app con
    /// los consejos encendidos, una cada 7 días como mucho y texto de ayuda, no de venta.
    /// </summary>
    [TestClass]
    public class RecordatorioReposicionPushJobsServiceTests
    {
        private static readonly DateTime MARTES = new DateTime(2026, 10, 20, 9, 30, 0);
        private static readonly DateTime JUEVES = new DateTime(2026, 10, 22, 9, 30, 0);
        private static readonly DateTime VIERNES = new DateTime(2026, 10, 23, 9, 30, 0);

        private ILectorParametrosUsuario lector;
        private IServicioNotificacionesPush push;
        private IServicioCorreoElectronico correo;
        private List<MailMessage> correos;
        private List<string> clientesConApp;
        private Dictionary<string, DateTime> ultimaPush;
        private Dictionary<string, List<DispositivoNotificacion>> dispositivos;
        private HashSet<DateTime> festivos;
        private List<IReadOnlyCollection<string>> calculados;

        [TestInitialize]
        public void Setup()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_PUSH, "Activo");
            push = A.Fake<IServicioNotificacionesPush>();
            correo = A.Fake<IServicioCorreoElectronico>();
            correos = new List<MailMessage>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) => correos.Add(m)).Returns(true);
            clientesConApp = new List<string> { "15191" };
            ultimaPush = new Dictionary<string, DateTime>();
            dispositivos = new Dictionary<string, List<DispositivoNotificacion>>
            {
                ["15191"] = new List<DispositivoNotificacion> { Dispositivo("15191") }
            };
            A.CallTo(() => push.ObtenerDispositivosCliente(A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string empresa, string cliente, string aplicacion) => Task.FromResult(
                    aplicacion == Constantes.Aplicaciones.NESTO_TIENDAS && dispositivos.TryGetValue(cliente, out var lista)
                        ? lista.Where(d => d.Activo).ToList()
                        : new List<DispositivoNotificacion>()));
            festivos = new HashSet<DateTime>();
            calculados = new List<IReadOnlyCollection<string>>();
        }

        private void Parametro(string clave, string valor)
            => A.CallTo(() => lector.LeerParametro("1", "(defecto)", clave)).Returns(valor);

        private static DispositivoNotificacion Dispositivo(string cliente, bool consejos = true, bool activo = true)
            => new DispositivoNotificacion
            {
                Cliente = cliente,
                Empresa = "1",
                Usuario = cliente + "@cliente.es",
                Aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS,
                Activo = activo,
                RecibirConsejos = consejos
            };

        private static CandidatoReposicionDTO Producto(string id, string nombre, decimal importe, int cantidad)
            => new CandidatoReposicionDTO { Producto = id, NombreProducto = nombre, ImporteHabitual = importe, CantidadHabitual = cantidad };

        /// <summary>A cada cliente que se le pida le tocan estos productos.</summary>
        private List<CandidatoReposicionDTO> productosQueTocan = new List<CandidatoReposicionDTO>
        {
            Producto("17404", "CHAMPÚ REPARADOR 1000 ML", 40m, 2)
        };

        private Task<ResultadoRecordatorioReposicionDTO> Calcular(IReadOnlyCollection<string> clientes, string consumibles, DateTime hoy)
        {
            calculados.Add(clientes);
            var resultado = new ResultadoRecordatorioReposicionDTO { Fecha = hoy };
            foreach (string cliente in clientes)
            {
                resultado.SeAvisarian.Add(new RecordatorioReposicionClienteDTO
                {
                    Cliente = cliente,
                    Nombre = "CLIENTE " + cliente,
                    Productos = productosQueTocan.Select(p => new CandidatoReposicionDTO
                    {
                        Cliente = cliente,
                        Producto = p.Producto,
                        NombreProducto = p.NombreProducto,
                        ImporteHabitual = p.ImporteHabitual,
                        CantidadHabitual = p.CantidadHabitual
                    }).ToList()
                });
            }
            return Task.FromResult(resultado);
        }

        private Task<ResultadoRecordatorioReposicionPushDTO> Procesar(DateTime ahora, Func<string, Task<string>> leerFoto = null)
            => RecordatorioReposicionPushJobsService.Procesar(lector, push, correo, Calcular,
                () => Task.FromResult(clientesConApp.ToList()),
                (clientes, desde) => Task.FromResult<ISet<string>>(new HashSet<string>(
                    clientes.Where(c => ultimaPush.TryGetValue(c, out DateTime fecha) && fecha >= desde))),
                dia => festivos.Contains(dia.Date),
                ahora,
                leerFoto);

        private void NoSeHaEnviadoNada()
            => A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();

        #region Interruptor

        [TestMethod]
        public async Task Apagado_NoCalculaNiEnviaNada()
        {
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_PUSH, null);

            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            Assert.AreEqual(ModoRecordatorioReposicionPush.Apagado, resultado.Modo);
            Assert.AreEqual(0, calculados.Count);
            NoSeHaEnviadoNada();
            Assert.AreEqual(0, correos.Count);
        }

        [TestMethod]
        public void InterpretarModo_SoloSombraYActivo_ElRestoApagado()
        {
            Assert.AreEqual(ModoRecordatorioReposicionPush.Activo, RecordatorioReposicionPushJobsService.InterpretarModo(" activo "));
            Assert.AreEqual(ModoRecordatorioReposicionPush.Sombra, RecordatorioReposicionPushJobsService.InterpretarModo("Sombra"));
            Assert.AreEqual(ModoRecordatorioReposicionPush.Apagado, RecordatorioReposicionPushJobsService.InterpretarModo("Apagado"));
            Assert.AreEqual(ModoRecordatorioReposicionPush.Apagado, RecordatorioReposicionPushJobsService.InterpretarModo("1"));
            Assert.AreEqual(ModoRecordatorioReposicionPush.Apagado, RecordatorioReposicionPushJobsService.InterpretarModo(null));
        }

        [TestMethod]
        public async Task Sombra_NoMandaNadaAClientes_YSiElCorreoInternoConLaListaYElTexto()
        {
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_PUSH, "Sombra");

            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            NoSeHaEnviadoNada();
            Assert.AreEqual(1, resultado.Pushes.Count);
            Assert.AreEqual(1, correos.Count);
            Assert.AreEqual(Constantes.Correos.INFORMATICA, correos[0].To.Single().Address);
            StringAssert.Contains(correos[0].Body, "15191");
            StringAssert.Contains(System.Net.WebUtility.HtmlDecode(correos[0].Body), "Que no le falte champú reparador 1000 ml esta semana");
        }

        [TestMethod]
        public async Task Activo_EnviaLaPushYNoMandaCorreoSombra()
        {
            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            A.CallTo(() => push.EnviarConsejoACliente("1", "15191", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(1, resultado.Pushes.Count);
            Assert.AreEqual(0, correos.Count);
        }

        #endregion

        #region Cuándo

        [TestMethod]
        public async Task Martes930_SeManda()
        {
            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            Assert.IsNull(resultado.MotivoNoSeEnvia);
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Viernes_NoSeManda()
        {
            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(VIERNES);

            Assert.AreEqual(RecordatorioReposicionPushJobsService.MOTIVO_DIA, resultado.MotivoNoSeEnvia);
            Assert.AreEqual(0, calculados.Count);
            NoSeHaEnviadoNada();
        }

        [TestMethod]
        public async Task Jueves12ConHoraLimite11_NoSeManda()
        {
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_HORA_LIMITE, "11:00");

            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(JUEVES.Date.AddHours(12));

            Assert.AreEqual(RecordatorioReposicionPushJobsService.MOTIVO_HORA, resultado.MotivoNoSeEnvia);
            NoSeHaEnviadoNada();
        }

        [TestMethod]
        public async Task FueraDe9a21_NuncaSeManda_AunqueLaHoraLimiteLoPermita()
        {
            Parametro(Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_HORA_LIMITE, "23:59");

            foreach (DateTime ahora in new[] { MARTES.Date.AddHours(8).AddMinutes(59), MARTES.Date.AddHours(21), MARTES.Date.AddHours(23),
                MARTES.Date.AddMinutes(30) })
            {
                ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(ahora);
                Assert.AreEqual(RecordatorioReposicionPushJobsService.MOTIVO_FUERA_DE_HORARIO, resultado.MotivoNoSeEnvia, ahora.ToString("HH:mm"));
            }
            NoSeHaEnviadoNada();
            Assert.AreEqual(0, calculados.Count);

            Assert.IsNull((await Procesar(MARTES.Date.AddHours(20).AddMinutes(59))).MotivoNoSeEnvia, "Con hora límite 23:59, a las 20:59 sí");
            Assert.IsNull((await Procesar(MARTES.Date.AddHours(9))).MotivoNoSeEnvia, "A las 9:00 en punto sí");
        }

        [TestMethod]
        public async Task VisperaDeFestivo_NoSeManda()
        {
            festivos.Add(MARTES.Date.AddDays(1));

            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            Assert.AreEqual(RecordatorioReposicionPushJobsService.MOTIVO_FESTIVO, resultado.MotivoNoSeEnvia);
            NoSeHaEnviadoNada();
        }

        [TestMethod]
        public void HoraLimite_HHmm_YSiNoSePuedeLeer11()
        {
            Assert.AreEqual(new TimeSpan(12, 30, 0), RecordatorioReposicionPushJobsService.InterpretarHoraLimite("12:30"));
            Assert.AreEqual(new TimeSpan(9, 0, 0), RecordatorioReposicionPushJobsService.InterpretarHoraLimite(" 9:00 "));
            Assert.AreEqual(new TimeSpan(11, 0, 0), RecordatorioReposicionPushJobsService.InterpretarHoraLimite(null));
            Assert.AreEqual(new TimeSpan(11, 0, 0), RecordatorioReposicionPushJobsService.InterpretarHoraLimite("a las once"));
        }

        #endregion

        #region A quién

        [TestMethod]
        public async Task ClienteSinDispositivoActivoDeNestoTiendas_NoRecibeNadaNiCuentaComoEnviado()
        {
            dispositivos["15191"] = new List<DispositivoNotificacion> { Dispositivo("15191", activo: false) };

            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            NoSeHaEnviadoNada();
            Assert.AreEqual(0, resultado.Pushes.Count);
            CollectionAssert.AreEqual(new[] { "15191" }, resultado.SinDispositivoConConsejos);
        }

        [TestMethod]
        public async Task ClienteConLosConsejosApagados_NoRecibeNada()
        {
            dispositivos["15191"] = new List<DispositivoNotificacion> { Dispositivo("15191", consejos: false) };

            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            NoSeHaEnviadoNada();
            Assert.AreEqual(0, resultado.Pushes.Count);
        }

        [TestMethod]
        public async Task PushHace5Dias_NoRecibeOtraAunqueTengaProductosNuevos()
        {
            ultimaPush["15191"] = MARTES.Date.AddDays(-5).AddHours(9.5);

            ResultadoRecordatorioReposicionPushDTO resultado = await Procesar(MARTES);

            NoSeHaEnviadoNada();
            CollectionAssert.AreEqual(new[] { "15191" }, resultado.ConPushReciente);
            Assert.AreEqual(0, calculados.Count, "Si no queda nadie, ni se calcula");
        }

        [TestMethod]
        public async Task PushHace7Dias_SiRecibe()
        {
            ultimaPush["15191"] = MARTES.Date.AddDays(-7).AddHours(9.5);

            _ = await Procesar(MARTES);

            A.CallTo(() => push.EnviarConsejoACliente("1", "15191", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ElCalculoSoloMiraALosClientesConLaApp()
        {
            clientesConApp = new List<string> { "15191 ", "20000", "15191" };

            _ = await Procesar(MARTES);

            CollectionAssert.AreEquivalent(new[] { "15191", "20000" }, calculados.Single().ToArray());
        }

        #endregion

        #region Qué

        [TestMethod]
        public void Texto_UnProducto()
        {
            NotificacionPushDTO n = RecordatorioReposicionPushJobsService.Notificacion(new List<CandidatoReposicionDTO>
            {
                Producto("17404", "CHAMPÚ REPARADOR  1000 ML", 40m, 2)
            });

            Assert.AreEqual("Nueva Visión", n.Titulo);
            Assert.AreEqual("Que no le falte champú reparador 1000 ml esta semana", n.Cuerpo);
        }

        [TestMethod]
        public void Texto_DosYTresProductos()
        {
            Assert.AreEqual("Que no le falte laca fuerte ni tinte 6.0 esta semana",
                RecordatorioReposicionPushJobsService.Cuerpo(new[] { "LACA FUERTE", "TINTE 6.0" }));
            Assert.AreEqual("Laca fuerte, tinte 6.0 y oxidante 20 vol: mejor tenerlos a mano esta semana",
                RecordatorioReposicionPushJobsService.Cuerpo(new[] { "LACA FUERTE", "TINTE 6.0", "OXIDANTE 20 VOL" }));
        }

        [TestMethod]
        public void Texto_DeUsted_NuncaDeTu()
        {
            foreach (string[] nombres in new[] { new string[0], new[] { "A" }, new[] { "A", "B" }, new[] { "A", "B", "C" } })
            {
                string cuerpo = " " + RecordatorioReposicionPushJobsService.Cuerpo(nombres).ToLowerInvariant() + " ";
                foreach (string tu in new[] { " tu ", " te ", " planifica", " quedarte", " puedes" })
                {
                    Assert.IsFalse(cuerpo.Contains(tu), cuerpo);
                }
            }
        }

        [TestMethod]
        public async Task ConFoto_LaDelPrimeroVaEnImagenUrl()
        {
            productosQueTocan = new List<CandidatoReposicionDTO> { Producto("P1", "UNO", 10m, 1), Producto("P2", "DOS", 50m, 1) };
            var pedidas = new List<string>();
            NotificacionPushDTO enviada = null;
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string e, string c, NotificacionPushDTO n) => enviada = n).Returns(1);

            _ = await Procesar(MARTES, p => { pedidas.Add(p); return Task.FromResult("https://www.tienda.es/img/" + p + ".jpg"); });

            CollectionAssert.AreEqual(new[] { "P2" }, pedidas, "Solo la del primero, el de más importe habitual");
            Assert.AreEqual("https://www.tienda.es/img/P2.jpg", enviada.Datos["imagenUrl"]);
        }

        [TestMethod]
        public async Task SinFoto_NoLlevaImagenUrl()
        {
            NotificacionPushDTO enviada = null;
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string e, string c, NotificacionPushDTO n) => enviada = n).Returns(1);

            _ = await Procesar(MARTES, p => Task.FromResult("https://"));
            Assert.IsFalse(enviada.Datos.ContainsKey("imagenUrl"));

            _ = await Procesar(MARTES, p => throw new Exception("tienda caída"));
            Assert.IsFalse(enviada.Datos.ContainsKey("imagenUrl"), "Si falla la tienda, la push sale igual sin foto");
        }

        [TestMethod]
        public void MensajeFcm_ConImagen_EnNotificationYEnApns_SinImagen_ComoSiempre()
        {
            var tokens = new List<string> { "t1" };
            var conFoto = RecordatorioReposicionPushJobsService.Notificacion(
                new List<CandidatoReposicionDTO> { Producto("P1", "UNO", 10m, 1) }, "https://www.tienda.es/img/p1.jpg");
            FirebaseAdmin.Messaging.MulticastMessage mensaje = NestoAPI.Infraestructure.Notificaciones.ServicioNotificacionesPush.ConstruirMensaje(tokens, conFoto);
            Assert.AreEqual("https://www.tienda.es/img/p1.jpg", mensaje.Notification.ImageUrl);
            Assert.AreEqual("https://www.tienda.es/img/p1.jpg", mensaje.Apns.FcmOptions.ImageUrl);
            Assert.IsTrue(mensaje.Apns.Aps.MutableContent);

            var sinFoto = RecordatorioReposicionPushJobsService.Notificacion(new List<CandidatoReposicionDTO> { Producto("P1", "UNO", 10m, 1) });
            mensaje = NestoAPI.Infraestructure.Notificaciones.ServicioNotificacionesPush.ConstruirMensaje(tokens, sinFoto);
            Assert.IsNull(mensaje.Notification.ImageUrl);
            Assert.IsNull(mensaje.Apns);
        }

        [TestMethod]
        public void Texto_NuncaHablaDeOfertasDescuentosNiImportes()
        {
            NotificacionPushDTO n = RecordatorioReposicionPushJobsService.Notificacion(new List<CandidatoReposicionDTO>
            {
                Producto("A", "LACA", 99.95m, 1), Producto("B", "TINTE", 50m, 2), Producto("C", "CHAMPÚ", 10m, 3)
            });

            foreach (string texto in new[] { n.Titulo, n.Cuerpo })
            {
                Assert.IsFalse(texto.IndexOf("oferta", StringComparison.OrdinalIgnoreCase) >= 0, texto);
                Assert.IsFalse(texto.IndexOf("descuento", StringComparison.OrdinalIgnoreCase) >= 0, texto);
                Assert.IsFalse(texto.Contains("€"), texto);
                Assert.IsFalse(texto.Contains("99"), texto);
            }
        }

        [TestMethod]
        public void Datos_LlevanElTipoYLosProductosConSuCantidadHabitual()
        {
            NotificacionPushDTO n = RecordatorioReposicionPushJobsService.Notificacion(new List<CandidatoReposicionDTO>
            {
                Producto("17404   ", "CHAMPÚ", 40m, 2), Producto("AB12", "LACA", 20m, 1)
            });

            Assert.AreEqual("reposicion", n.Datos["tipo"]);
            Assert.AreEqual("reposicion", n.Tipo);
            Assert.AreEqual("17404:2;AB12:1", n.Datos["productos"]);
        }

        [TestMethod]
        public async Task ComoMuchoTresProductos_LosDeMasImporteHabitual()
        {
            productosQueTocan = new List<CandidatoReposicionDTO>
            {
                Producto("P1", "UNO", 10m, 1), Producto("P2", "DOS", 50m, 1), Producto("P3", "TRES", 30m, 1), Producto("P4", "CUATRO", 40m, 1)
            };
            NotificacionPushDTO enviada = null;
            A.CallTo(() => push.EnviarConsejoACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string e, string c, NotificacionPushDTO n) => enviada = n).Returns(1);

            _ = await Procesar(MARTES);

            Assert.AreEqual("P2:1;P4:1;P3:1", enviada.Datos["productos"]);
            Assert.AreEqual("Dos, cuatro y tres: mejor tenerlos a mano esta semana", enviada.Cuerpo);
        }

        #endregion

        #region Buzón: una push cada 7 días

        [TestMethod]
        public async Task ClientesConPushDesde_SoloLasDeReposicionDeNestoTiendasDesdeEseDia()
        {
            DateTime desde = MARTES.Date.AddDays(-6);
            NVEntities db = A.Fake<NVEntities>();
            List<NotificacionBuzon> buzon = new List<NotificacionBuzon>
            {
                Buzon("15191", desde.AddHours(9.5), "{\"tipo\":\"reposicion\",\"productos\":\"A:1\"}"),
                Buzon("20000", desde.AddDays(-1), "{\"tipo\":\"reposicion\",\"productos\":\"A:1\"}"),     // hace 7 días
                Buzon("30000", desde.AddDays(1), "{\"tipo\":\"protocolo\",\"videoId\":\"1\"}"),           // otro tipo
                Buzon("40000", desde.AddDays(1), null),
                Buzon("50000", desde.AddDays(1), "{\"tipo\":\"reposicion\"}", Constantes.Aplicaciones.NESTO_APP)
            };
            A.CallTo(() => db.NotificacionesBuzon).Returns(ServicioPedidosClienteTests.Fake(buzon));

            ISet<string> conPush = await RecordatorioReposicionPushJobsService.ClientesConPushDesde(db,
                new[] { "15191", "20000", "30000", "40000", "50000" }, desde);

            CollectionAssert.AreEquivalent(new[] { "15191" }, conPush.ToArray());
        }

        private static NotificacionBuzon Buzon(string cliente, DateTime fecha, string datos, string aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS)
            => new NotificacionBuzon { Cliente = cliente, FechaCreacion = fecha, Datos = datos, Aplicacion = aplicacion, Usuario = "u" };

        #endregion
    }
}
