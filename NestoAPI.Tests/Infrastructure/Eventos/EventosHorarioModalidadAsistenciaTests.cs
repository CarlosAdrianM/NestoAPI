using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models;
using NestoAPI.Models.Pagos;
using NestoAPI.Tests.Helpers;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#690 (corte 1b de #610): horario, ponente, imagen, dos señales (reembolsable / no reembolsable), evento
    /// gratuito, confirmación de asistencia con recordatorio y base imponible del día.
    /// </summary>
    [TestClass]
    public class EventosHorarioModalidadAsistenciaTests
    {
        // Miércoles
        private static readonly DateTime HOY = new DateTime(2026, 10, 28, 10, 0, 0);
        private const int EVENTO_PAGO = 1;
        private const int EVENTO_GRATIS = 2;
        private const int EVENTO_SOLO_NR = 3;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<Cliente> clientes;
        private IServicioPagos pagos;
        private IProcesadorInscripcionPagada procesador;
        private int siguienteId;
        private List<SolicitudPagoTPV> enlaces;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            siguienteId = 100;
            enlaces = new List<SolicitudPagoTPV>();
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO_PAGO, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 10, 30), ImporteSenal = 100,
                    ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = HOY,
                    HoraInicio = new TimeSpan(10, 0, 0), HoraFin = new TimeSpan(14, 0, 0), Ponente = "Maystar", ProductoCurso = "CURSO01" },
                new Evento { Id = EVENTO_GRATIS, Empresa = "1", Titulo = "Presentación Maystar", Fecha = new DateTime(2026, 10, 30), ImporteSenal = 0,
                    ImporteSenalNoReembolsable = 0, Activo = true, Almacen = "ALC", Usuario = "Paloma", FechaModificacion = HOY },
                new Evento { Id = EVENTO_SOLO_NR, Empresa = "1", Titulo = "Curso Peeling", Fecha = new DateTime(2026, 11, 20), ImporteSenal = 0,
                    ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "REI", Usuario = "Reina", FechaModificacion = HOY }
            };
            inscripciones = new List<EventoInscripcion>();
            clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "CENTRO DE ESTÉTICA LUNA", ClientePrincipal = true, Vendedor = "JE " },
                new Cliente { Empresa = "1", Nº_Cliente = "20000", Contacto = "0  ", Nombre = "OTRA", ClientePrincipal = true, Vendedor = "NV " }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            DbSet<EventoInscripcion> fakeInscripciones = Crear(inscripciones);
            A.CallTo(() => fakeInscripciones.Add(A<EventoInscripcion>._)).ReturnsLazily((EventoInscripcion i) =>
            {
                i.Id = siguienteId++;
                inscripciones.Add(i);
                return i;
            });
            A.CallTo(() => db.EventosInscripciones).Returns(fakeInscripciones);
            A.CallTo(() => db.EventosSenales).Returns(Crear(new List<EventoSenal>()));
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
            A.CallTo(() => db.PagosTPV).Returns(Crear(new List<PagoTPV>()));
            A.CallTo(() => db.Productos).Returns(Crear(new List<Producto> { new Producto { Empresa = "1", Número = "CURSO01" } }));

            pagos = A.Fake<IServicioPagos>();
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).ReturnsLazily((SolicitudPagoTPV s, string u) =>
            {
                enlaces.Add(s);
                return Task.FromResult(new RespuestaIniciarPago { IdPago = 555, TokenAcceso = Guid.NewGuid(), UrlPaginaPago = "https://api.nuevavision.es/pago/x" });
            });
            procesador = A.Fake<IProcesadorInscripcionPagada>();
        }

        private ServicioInscripcionesEventos Servicio(DateTime? ahora = null) =>
            new ServicioInscripcionesEventos(db, () => ahora ?? HOY, () => pagos, AlmacenDe, procesador);

        private ServicioEventos ServicioEventos(IRepositorioImagenesEventos imagenes = null) =>
            new ServicioEventos(db, () => HOY, AlmacenDe, A.Fake<IRepositorioFacturacionCursos>(), A.Fake<IRepositorioCupones>(),
                trabajo => trabajo(), imagenes);

        private static string AlmacenDe(string empresa, string usuario) =>
            usuario == "Reina" ? "REI" : usuario == "Paloma" ? "ALC" : null;

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static IPrincipal ClienteTienda(string cliente) =>
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "luna@example.com"), new Claim("cliente", cliente) }, "prueba"));

        private static readonly IPrincipal VENDEDOR = Usuario("NUEVAVISION\\Jesus", "Ventas");
        private static readonly IPrincipal REINA = Usuario("NUEVAVISION\\Reina", "Tiendas");
        private static readonly IPrincipal PALOMA = Usuario("NUEVAVISION\\Paloma", "Tiendas");
        private static readonly IPrincipal TIENDA_ONLINE = Usuario("NUEVAVISION\\Laura", "TiendaOnline");

        private EventoInscripcion Inscripcion(int id, int eventoId, string cliente, EstadoInscripcionEvento estado, string modalidad = null)
        {
            var i = new EventoInscripcion
            {
                Id = id,
                EventoId = eventoId,
                Empresa = "1",
                Cliente = cliente,
                Contacto = "0  ",
                Origen = "NestoApp",
                Estado = estado.ToString(),
                Modalidad = modalidad,
                UsuarioCreacion = "NUEVAVISION\\Jesus",
                FechaCreacion = HOY.AddDays(-3),
                Usuario = "NUEVAVISION\\Jesus",
                FechaModificacion = HOY.AddDays(-3)
            };
            inscripciones.Add(i);
            return i;
        }

        // ---------------- Reglas de las modalidades ----------------

        [TestMethod]
        public void Modalidades_DisponiblesSegunLasSenales()
        {
            CollectionAssert.AreEqual(new[] { ModalidadInscripcionEvento.Reembolsable, ModalidadInscripcionEvento.NoReembolsable },
                ReglasModalidadEvento.Disponibles(eventos[0]).ToArray());
            CollectionAssert.AreEqual(new[] { ModalidadInscripcionEvento.Gratuita }, ReglasModalidadEvento.Disponibles(eventos[1]).ToArray());
            CollectionAssert.AreEqual(new[] { ModalidadInscripcionEvento.NoReembolsable }, ReglasModalidadEvento.Disponibles(eventos[2]).ToArray());
            Assert.IsTrue(ReglasModalidadEvento.EsGratuito(eventos[1]));
            Assert.IsFalse(ReglasModalidadEvento.EsGratuito(eventos[2]));
        }

        [TestMethod]
        public void Modalidades_LasDeAntesSinModalidadSonReembolsables()
        {
            var antigua = new EventoInscripcion { Modalidad = null };
            Assert.AreEqual(ModalidadInscripcionEvento.Reembolsable, ReglasModalidadEvento.DeInscripcion(antigua));
            Assert.IsTrue(ReglasModalidadEvento.EmiteCupon(antigua));
            Assert.AreEqual(100, ReglasModalidadEvento.ImporteDe(antigua, eventos[0]));
            Assert.AreEqual(60, ReglasModalidadEvento.ImporteDe(new EventoInscripcion { Modalidad = "NoReembolsable" }, eventos[0]));
            Assert.IsFalse(ReglasModalidadEvento.EmiteCupon(ModalidadInscripcionEvento.NoReembolsable));
            Assert.IsFalse(ReglasModalidadEvento.EmiteCupon(ModalidadInscripcionEvento.Gratuita));
        }

        [TestMethod]
        public void Modalidades_ElegirSinDecirNada()
        {
            Assert.AreEqual(ModalidadInscripcionEvento.Reembolsable, ReglasModalidadEvento.Elegir(eventos[0], null));
            Assert.AreEqual(ModalidadInscripcionEvento.Gratuita, ReglasModalidadEvento.Elegir(eventos[1], null));
            Assert.AreEqual(ModalidadInscripcionEvento.NoReembolsable, ReglasModalidadEvento.Elegir(eventos[2], ""));
            Assert.AreEqual(ModalidadInscripcionEvento.NoReembolsable, ReglasModalidadEvento.Elegir(eventos[0], "noreembolsable"));
        }

        [TestMethod]
        public void Modalidades_ElegirUnaQueElEventoNoOfrece_Lanza()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() => ReglasModalidadEvento.Elegir(eventos[2], "Reembolsable"));
            StringAssert.Contains(ex.Message, "no tiene la modalidad reembolsable");
            Assert.ThrowsException<NestoBusinessException>(() => ReglasModalidadEvento.Elegir(eventos[0], "Gratuita"));
            Assert.ThrowsException<NestoBusinessException>(() => ReglasModalidadEvento.Elegir(eventos[1], "Reembolsable"));
            Assert.ThrowsException<NestoBusinessException>(() => ReglasModalidadEvento.Elegir(eventos[0], "Otra"));
        }

        [TestMethod]
        public void Modalidades_PorImporteDeLaTransferencia()
        {
            Assert.AreEqual(ModalidadInscripcionEvento.NoReembolsable, ReglasModalidadEvento.PorImporte(eventos[0], 60));
            Assert.AreEqual(ModalidadInscripcionEvento.Reembolsable, ReglasModalidadEvento.PorImporte(eventos[0], 100));
            Assert.AreEqual(ModalidadInscripcionEvento.Reembolsable, ReglasModalidadEvento.PorImporte(eventos[0], 75), "Si no coincide, como hasta ahora");
        }

        [TestMethod]
        public void Horas_SeLeenYSeEscribenComoHHmm()
        {
            Assert.AreEqual(new TimeSpan(9, 30, 0), ReglasModalidadEvento.LeerHora("9:30", "inicio"));
            Assert.IsNull(ReglasModalidadEvento.LeerHora(" ", "inicio"));
            Assert.AreEqual("09:30", ReglasModalidadEvento.EscribirHora(new TimeSpan(9, 30, 0)));
            Assert.ThrowsException<NestoBusinessException>(() => ReglasModalidadEvento.LeerHora("25:00", "inicio"));
        }

        [TestMethod]
        public void Asistencia_EstadoQueSeVeEnLaLista()
        {
            Assert.AreEqual(EstadoAsistenciaEvento.Pendiente, ReglasAsistenciaEvento.Estado(new EventoInscripcion { Estado = "Pagada" }));
            Assert.AreEqual(EstadoAsistenciaEvento.Confirmada, ReglasAsistenciaEvento.Estado(new EventoInscripcion { Estado = "Pagada", AsistenciaRespuesta = "Confirma" }));
            Assert.AreEqual(EstadoAsistenciaEvento.NoPodraIr, ReglasAsistenciaEvento.Estado(new EventoInscripcion { Estado = "Inscrita", AsistenciaRespuesta = "NoPodraIr" }));
            Assert.AreEqual(EstadoAsistenciaEvento.NoAsistio, ReglasAsistenciaEvento.Estado(new EventoInscripcion { Estado = "NoAsistio", AsistenciaRespuesta = "Confirma" }),
                "La lista pasada manda sobre la respuesta");
            Assert.AreEqual("No podrá ir", ReglasAsistenciaEvento.Texto(EstadoAsistenciaEvento.NoPodraIr));
            Assert.AreEqual("Asistió", ReglasAsistenciaEvento.Texto(EstadoAsistenciaEvento.Asistio));
        }

        [TestMethod]
        public void Inscrita_OcupaPlazaEntraEnLaListaYSeAnula()
        {
            Assert.IsTrue(ReglasInscripcionEvento.OCUPAN_PLAZA.Contains(EstadoInscripcionEvento.Inscrita));
            Assert.IsTrue(ReglasInscripcionEvento.EN_LISTA_ASISTENCIA.Contains(EstadoInscripcionEvento.Inscrita));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Inscrita, EstadoInscripcionEvento.Asistio));
            Assert.IsTrue(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Inscrita, EstadoInscripcionEvento.Anulada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Inscrita, EstadoInscripcionEvento.Pagada));
            Assert.IsFalse(ReglasInscripcionEvento.PuedePasar(EstadoInscripcionEvento.Inscrita, EstadoInscripcionEvento.Caducada));
        }

        // ---------------- Evento: defaults, horario, ponente ----------------

        [TestMethod]
        public async Task CrearEvento_SinImportes_CienYSesenta()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Masterclass\", \"Fecha\": \"2026-11-10\", \"Activo\": true, \"Almacen\": \"REI\" }");

            EventoDTO creado = await ServicioEventos().CrearEvento(cuerpo, "NUEVAVISION\\Reina");

            Assert.AreEqual(100, creado.ImporteSenal);
            Assert.AreEqual(60, creado.ImporteSenalNoReembolsable);
            Assert.IsFalse(creado.Gratuito);
            CollectionAssert.AreEqual(new[] { "Reembolsable", "NoReembolsable" }, creado.Modalidades);
        }

        [TestMethod]
        public async Task CrearEvento_CeroExplicitoEnLasDos_Gratuito()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Presentación\", \"Fecha\": \"2026-11-10\", \"Activo\": true, \"ImporteSenal\": 0, \"ImporteSenalNoReembolsable\": 0 }");

            EventoDTO creado = await ServicioEventos().CrearEvento(cuerpo, "NUEVAVISION\\Carlos");

            Assert.AreEqual(0, creado.ImporteSenal);
            Assert.AreEqual(0, creado.ImporteSenalNoReembolsable);
            Assert.IsTrue(creado.Gratuito);
            CollectionAssert.AreEqual(new[] { "Gratuita" }, creado.Modalidades);
        }

        [TestMethod]
        public async Task CrearEvento_ComoElNestoDeHoy_SoloImporteSenal_LaNoReembolsablePorDefecto()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Masterclass\", \"Fecha\": \"2026-11-10\", \"Activo\": true, \"ImporteSenal\": 50 }");

            EventoDTO creado = await ServicioEventos().CrearEvento(cuerpo, "NUEVAVISION\\Carlos");

            Assert.AreEqual(50, creado.ImporteSenal);
            Assert.AreEqual(60, creado.ImporteSenalNoReembolsable);
        }

        [TestMethod]
        public async Task CrearEvento_HorarioYPonente()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Masterclass\", \"Fecha\": \"2026-11-10\", \"Activo\": true, \"HoraInicio\": \"10:00\", \"HoraFin\": \"14:30\", \"Ponente\": \" Maystar \" }");

            EventoDTO creado = await ServicioEventos().CrearEvento(cuerpo, "NUEVAVISION\\Carlos");

            Assert.AreEqual("10:00", creado.HoraInicio);
            Assert.AreEqual("14:30", creado.HoraFin);
            Assert.AreEqual("Maystar", creado.Ponente);
        }

        [TestMethod]
        public async Task CrearEvento_FinAntesQueInicioOPonenteLargo_Lanza()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioEventos().CrearEvento(
                new EventoDTO { Titulo = "X", Fecha = new DateTime(2026, 11, 10), HoraInicio = "14:00", HoraFin = "10:00" }, "x"));
            StringAssert.Contains(ex.Message, "posterior");
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioEventos().CrearEvento(
                new EventoDTO { Titulo = "X", Fecha = new DateTime(2026, 11, 10), Ponente = new string('a', 101) }, "x"));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioEventos().CrearEvento(
                new EventoDTO { Titulo = "X", Fecha = new DateTime(2026, 11, 10), ImporteSenalNoReembolsable = -1 }, "x"));
        }

        [TestMethod]
        public async Task ModificarEvento_ElNestoDeHoyNoBorraLoNuevo()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Masterclass Cloasma\", \"Fecha\": \"2026-10-30\", \"ImporteSenal\": 100, \"Activo\": true }");

            EventoDTO modificado = await ServicioEventos().ModificarEvento(EVENTO_PAGO, cuerpo, "NUEVAVISION\\Carlos");

            Assert.AreEqual(60, modificado.ImporteSenalNoReembolsable);
            Assert.AreEqual("10:00", modificado.HoraInicio);
            Assert.AreEqual("14:00", modificado.HoraFin);
            Assert.AreEqual("Maystar", modificado.Ponente);
        }

        [TestMethod]
        public async Task ModificarEvento_SoloHoraFinAntesDeLaConservada_Lanza()
        {
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Masterclass Cloasma\", \"Fecha\": \"2026-10-30\", \"ImporteSenal\": 100, \"Activo\": true, \"HoraFin\": \"09:00\" }");

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioEventos().ModificarEvento(EVENTO_PAGO, cuerpo, "x"));
        }

        [TestMethod]
        public async Task ModificarEvento_GratuitoConInscritosNoPasaASerDePago()
        {
            _ = Inscripcion(1, EVENTO_GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");
            EventoDTO cuerpo = JsonConvert.DeserializeObject<EventoDTO>(
                "{ \"Titulo\": \"Presentación Maystar\", \"Fecha\": \"2026-10-30\", \"ImporteSenal\": 50, \"Activo\": true }");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => ServicioEventos().ModificarEvento(EVENTO_GRATIS, cuerpo, "x"));

            StringAssert.Contains(ex.Message, "no puede pasar a ser de pago");
        }

        [TestMethod]
        public async Task LeerEventos_ConImagen_LlevaSuUrlPublica()
        {
            IRepositorioImagenesEventos imagenes = A.Fake<IRepositorioImagenesEventos>();
            A.CallTo(() => imagenes.LeerFechas(A<IEnumerable<int>>._)).Returns(Task.FromResult(
                new Dictionary<int, DateTime> { [EVENTO_PAGO] = new DateTime(2026, 10, 22, 10, 15, 0) }));

            List<EventoDTO> lista = await ServicioEventos(imagenes).LeerEventos("1", true);

            Assert.AreEqual("https://api.nuevavision.es/api/Eventos/1/Imagen?v=20261022101500", lista.Single(e => e.Id == EVENTO_PAGO).ImagenUrl);
            Assert.IsNull(lista.Single(e => e.Id == EVENTO_GRATIS).ImagenUrl);
        }

        [TestMethod]
        public async Task LeerEventos_SiFallanLasImagenes_SalenSinEllas()
        {
            IRepositorioImagenesEventos imagenes = A.Fake<IRepositorioImagenesEventos>();
            A.CallTo(() => imagenes.LeerFechas(A<IEnumerable<int>>._)).Throws(new InvalidOperationException("sin tabla"));

            List<EventoDTO> lista = await ServicioEventos(imagenes).LeerEventos("1", true);

            Assert.AreEqual(3, lista.Count);
            Assert.IsTrue(lista.All(e => e.ImagenUrl == null));
        }

        // ---------------- Imagen ----------------

        [TestMethod]
        public void Imagen_SoloImagenesYUnaSola()
        {
            byte[] png = { 1, 2, 3 };
            Assert.IsNotNull(ReglasImagenesEventos.Validar(new[] { ("cartel.png", "image/png", png) }).Imagen);
            ReglasImagenesEventos.Validacion pdf = ReglasImagenesEventos.Validar(new[] { ("normas.pdf", "application/pdf", png) });
            Assert.IsTrue(pdf.TipoNoPermitido);
            StringAssert.Contains(pdf.Error, "PNG, JPEG, GIF o WebP");
            Assert.IsNotNull(ReglasImagenesEventos.Validar(new[] { ("a.png", "image/png", png), ("b.png", "image/png", png) }).Error);
            Assert.IsNotNull(ReglasImagenesEventos.Validar(new (string, string, byte[])[0]).Error);
        }

        [TestMethod]
        public void Imagen_Permisos_TiendaDelEventoTiendaOnlineYDireccion()
        {
            Assert.IsTrue(PermisosEventos.PuedeSubirImagen(REINA, "REI", "REI"));
            Assert.IsFalse(PermisosEventos.PuedeSubirImagen(PALOMA, "ALC", "REI"));
            Assert.IsTrue(PermisosEventos.PuedeSubirImagen(TIENDA_ONLINE, null, "REI"));
            Assert.IsTrue(PermisosEventos.PuedeSubirImagen(Usuario("NUEVAVISION\\Carlos", "Dirección"), null, "ALC"));
            Assert.IsFalse(PermisosEventos.PuedeSubirImagen(VENDEDOR, null, "REI"));
            Assert.IsFalse(PermisosEventos.PuedeSubirImagen(Usuario("NUEVAVISION\\Admin", "Administración"), null, "REI"));
            Assert.IsFalse(PermisosEventos.PuedeSubirImagen(ClienteTienda("15191"), null, "REI"));
        }

        [TestMethod]
        public async Task Controlador_PostImagen_SinPermiso_403()
        {
            var controlador = new EventosController(db, ServicioEventos(A.Fake<IRepositorioImagenesEventos>()), Servicio())
            {
                Request = new HttpRequestMessage(),
                User = VENDEDOR
            };
            controlador.Request.SetConfiguration(new HttpConfiguration());

            var resultado = await controlador.PostImagen(EVENTO_PAGO) as ResponseMessageResult;

            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode);
        }

        [TestMethod]
        public async Task Controlador_PostImagen_GuardaYDevuelveLaUrl()
        {
            IRepositorioImagenesEventos imagenes = A.Fake<IRepositorioImagenesEventos>();
            A.CallTo(() => imagenes.Guardar(EVENTO_PAGO, A<ImagenEventoAGrabar>._, A<string>._)).Returns(Task.FromResult(new DateTime(2026, 10, 28, 10, 0, 0)));
            var contenido = new MultipartFormDataContent();
            var fichero = new ByteArrayContent(new byte[] { 1, 2, 3 });
            fichero.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            contenido.Add(fichero, "fichero", "cartel.png");
            var controlador = new EventosController(db, ServicioEventos(imagenes), Servicio())
            {
                Request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Eventos/1/Imagen") { Content = contenido },
                User = REINA
            };
            controlador.Request.SetConfiguration(new HttpConfiguration());

            var resultado = await controlador.PostImagen(EVENTO_PAGO) as OkNegotiatedContentResult<EventoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual("https://api.nuevavision.es/api/Eventos/1/Imagen?v=20261028100000", resultado.Content.ImagenUrl);
            A.CallTo(() => imagenes.Guardar(EVENTO_PAGO, A<ImagenEventoAGrabar>.That.Matches(i => i.Tipo == "image/png" && i.Nombre == "cartel.png"), A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Controlador_GetImagen_SinImagen_404_ConImagen_Cacheable()
        {
            IRepositorioImagenesEventos imagenes = A.Fake<IRepositorioImagenesEventos>();
            A.CallTo(() => imagenes.Leer(EVENTO_GRATIS)).Returns(Task.FromResult<ImagenEvento>(null));
            A.CallTo(() => imagenes.Leer(EVENTO_PAGO)).Returns(Task.FromResult(new ImagenEvento
            {
                EventoId = EVENTO_PAGO,
                Tipo = "image/jpeg",
                Contenido = new byte[] { 9, 9 },
                Fecha = HOY
            }));
            var controlador = new EventosController(db, ServicioEventos(imagenes), Servicio()) { Request = new HttpRequestMessage() };

            Assert.IsInstanceOfType(await controlador.GetImagen(EVENTO_GRATIS), typeof(NotFoundResult));
            var resultado = await controlador.GetImagen(EVENTO_PAGO) as ResponseMessageResult;
            Assert.AreEqual(HttpStatusCode.OK, resultado.Response.StatusCode);
            Assert.AreEqual("image/jpeg", resultado.Response.Content.Headers.ContentType.MediaType);
            Assert.IsTrue(resultado.Response.Headers.CacheControl.Public);
        }

        // ---------------- Inscripción por modalidad ----------------

        [TestMethod]
        public async Task Inscribir_EventoGratuito_InscritaSinEnlaceNiCaducidad()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_GRATIS,
                new InscribirEventoDTO { Cliente = "15191", Origen = "NestoApp", FormaPago = "Tarjeta" }, VENDEDOR);

            EventoInscripcion guardada = inscripciones.Single();
            Assert.AreEqual("Inscrita", guardada.Estado);
            Assert.AreEqual("Gratuita", guardada.Modalidad);
            Assert.IsNull(guardada.FechaLimitePago);
            Assert.IsNull(guardada.FormaPago);
            Assert.IsNull(dto.UrlPago);
            Assert.AreEqual(ModalidadInscripcionEvento.Gratuita, dto.Modalidad);
            Assert.AreEqual(0, dto.ImporteSenal);
            Assert.IsFalse(dto.ConCupon);
            A.CallTo(() => pagos.IniciarPago(A<SolicitudPagoTPV>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => procesador.Procesar(A<EventoInscripcion>._, A<Evento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Inscribir_NoReembolsable_EnlacePorSesentaYSinCupon()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_PAGO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "NestoApp", Modalidad = "NoReembolsable" }, VENDEDOR);

            Assert.AreEqual(60, enlaces.Single().Importe);
            Assert.AreEqual("NoReembolsable", inscripciones.Single().Modalidad);
            Assert.AreEqual("Reservada", inscripciones.Single().Estado);
            Assert.AreEqual(60, dto.ImporteSenal);
            Assert.IsFalse(dto.ConCupon);
        }

        [TestMethod]
        public async Task Inscribir_SinModalidad_ReembolsablePorLaSenalDeSiempre()
        {
            InscripcionEventoDTO dto = await Servicio().Inscribir(EVENTO_PAGO,
                new InscribirEventoDTO { Cliente = "15191", Origen = "NestoApp" }, VENDEDOR);

            Assert.AreEqual(100, enlaces.Single().Importe);
            Assert.AreEqual(ModalidadInscripcionEvento.Reembolsable, dto.Modalidad);
            Assert.IsTrue(dto.ConCupon);
        }

        [TestMethod]
        public async Task Inscribir_ModalidadQueNoHay_400SinInscripcion()
        {
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_SOLO_NR,
                new InscribirEventoDTO { Cliente = "15191", Origen = "NestoApp", Modalidad = "Reembolsable" }, VENDEDOR));
            Assert.AreEqual(0, inscripciones.Count);
        }

        [TestMethod]
        public async Task Inscribir_ClienteDeLaTiendaPorOtro_MensajeDeUsted()
        {
            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Inscribir(EVENTO_PAGO,
                new InscribirEventoDTO { Cliente = "20000" }, ClienteTienda("15191")));
            Assert.AreEqual("Solo puede inscribirse a sí mismo.", ex.Message);
        }

        [TestMethod]
        public async Task MarcarPagada_Gratuita_400()
        {
            _ = Inscripcion(7, EVENTO_GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().MarcarPagada(7, new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }, PALOMA));

            StringAssert.Contains(ex.Message, "gratuita");
        }

        [TestMethod]
        public async Task MarcarPagada_NoReembolsableSinImporte_ElDeSuModalidad()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Reservada, "NoReembolsable");

            await Servicio().MarcarPagada(7, new MarcarPagoInscripcionDTO { FormaPago = "Efectivo" }, REINA);

            Assert.AreEqual(60, inscripciones.Single().ImportePagado);
            Assert.AreEqual("Pagada", inscripciones.Single().Estado);
        }

        [TestMethod]
        public async Task Anular_Gratuita_SeAnula()
        {
            _ = Inscripcion(7, EVENTO_GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");

            InscripcionEventoDTO dto = await Servicio().Anular(7, ClienteTienda("15191"));

            Assert.AreEqual(EstadoInscripcionEvento.Anulada, dto.Estado);
        }

        [TestMethod]
        public async Task Anular_DeOtroCliente_MensajeDeUsted()
        {
            _ = Inscripcion(7, EVENTO_GRATIS, "20000", EstadoInscripcionEvento.Inscrita, "Gratuita");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Anular(7, ClienteTienda("15191")));

            Assert.AreEqual("Solo puede anular sus inscripciones.", ex.Message);
        }

        [TestMethod]
        public async Task PasarLista_LasGratuitasEstanEnLaLista()
        {
            _ = Inscripcion(7, EVENTO_GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");
            _ = Inscripcion(8, EVENTO_GRATIS, "20000", EstadoInscripcionEvento.Inscrita, "Gratuita");

            List<InscripcionEventoDTO> lista = await Servicio(new DateTime(2026, 10, 30, 18, 0, 0))
                .PasarLista(EVENTO_GRATIS, new AsistenciaEventoDTO { Asistentes = new List<int> { 7 } }, PALOMA);

            Assert.AreEqual(EstadoInscripcionEvento.Asistio, lista.Single(i => i.Id == 7).Estado);
            Assert.AreEqual(EstadoInscripcionEvento.NoAsistio, lista.Single(i => i.Id == 8).Estado);
        }

        [TestMethod]
        public async Task DarPorAsistidosSinLista_TambienLasGratuitas()
        {
            _ = Inscripcion(7, EVENTO_GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");

            var resultado = await Servicio(new DateTime(2026, 10, 31, 7, 55, 0)).DarPorAsistidosSinLista();

            Assert.AreEqual(1, resultado.Count);
            Assert.AreEqual("Asistio", inscripciones.Single().Estado);
        }

        // ---------------- Base imponible del día ----------------

        [TestMethod]
        public async Task ListaDeInscritos_DiaDelEvento_BaseImponibleDeCadaCliente()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            _ = Inscripcion(8, EVENTO_PAGO, "20000", EstadoInscripcionEvento.Pagada);
            ILectorBaseImponibleDia lector = A.Fake<ILectorBaseImponibleDia>();
            A.CallTo(() => lector.Leer("1", new DateTime(2026, 10, 30), A<IReadOnlyCollection<string>>._, "CURSO01"))
                .Returns(Task.FromResult(new Dictionary<string, decimal> { ["15191"] = 123.456m }));
            ServicioInscripcionesEventos servicio = Servicio(new DateTime(2026, 10, 30, 18, 0, 0));
            servicio.BaseImponibleDia = lector;

            List<InscripcionEventoDTO> lista = await servicio.LeerInscripcionesEvento(EVENTO_PAGO, REINA);

            Assert.AreEqual(123.46m, lista.Single(i => i.Id == 7).BaseImponibleDia);
            Assert.AreEqual(0, lista.Single(i => i.Id == 8).BaseImponibleDia, "No compró: 0");
        }

        [TestMethod]
        public async Task ListaDeInscritos_AntesDelEvento_NiSeConsulta()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            ILectorBaseImponibleDia lector = A.Fake<ILectorBaseImponibleDia>();
            ServicioInscripcionesEventos servicio = Servicio();
            servicio.BaseImponibleDia = lector;

            List<InscripcionEventoDTO> lista = await servicio.LeerInscripcionesEvento(EVENTO_PAGO, REINA);

            Assert.IsNull(lista.Single().BaseImponibleDia);
            A.CallTo(() => lector.Leer(A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ListaDeInscritos_SiFallaLaBase_LaListaSaleIgual()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            ILectorBaseImponibleDia lector = A.Fake<ILectorBaseImponibleDia>();
            A.CallTo(() => lector.Leer(A<string>._, A<DateTime>._, A<IReadOnlyCollection<string>>._, A<string>._)).Throws(new Exception("timeout"));
            ServicioInscripcionesEventos servicio = Servicio(new DateTime(2026, 10, 30, 18, 0, 0));
            servicio.BaseImponibleDia = lector;

            List<InscripcionEventoDTO> lista = await servicio.LeerInscripcionesEvento(EVENTO_PAGO, REINA);

            Assert.AreEqual(1, lista.Count);
            Assert.IsNull(lista.Single().BaseImponibleDia);
        }

        [TestMethod]
        public void LectorBaseImponible_ConsultaLigera()
        {
            StringAssert.Contains(LectorBaseImponibleDia.SQL, "WITH (NOLOCK)");
            StringAssert.Contains(LectorBaseImponibleDia.SQL, "l.Almacén IN ('REI', 'ALC')");
            StringAssert.Contains(LectorBaseImponibleDia.SQL, "c.[Nº Cliente] IN ({0})");
        }

        // ---------------- Confirmación de asistencia ----------------

        [TestMethod]
        public async Task ConfirmarAsistencia_ElCliente_ConfirmaLaSuya()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);

            InscripcionEventoDTO dto = await Servicio().ConfirmarAsistencia(7, new ConfirmarAsistenciaDTO { Respuesta = "Confirma" }, ClienteTienda("15191"));

            Assert.AreEqual(EstadoAsistenciaEvento.Confirmada, dto.EstadoAsistencia);
            Assert.AreEqual(RespuestaAsistenciaEvento.Confirma, dto.AsistenciaConfirmada.Respuesta);
            Assert.AreEqual(OrigenRespuestaAsistenciaEvento.Cliente, dto.AsistenciaConfirmada.Origen);
            Assert.AreEqual(HOY, dto.AsistenciaConfirmada.Fecha);
            Assert.AreEqual("Facturada", inscripciones.Single().Estado, "No toca el pago");
        }

        [TestMethod]
        public async Task ConfirmarAsistencia_ElClientePorOtro_403DeUsted()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "20000", EstadoInscripcionEvento.Pagada);

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().ConfirmarAsistencia(7, new ConfirmarAsistenciaDTO { Respuesta = "Confirma" }, ClienteTienda("15191")));

            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
            Assert.AreEqual("Solo puede contestar por sus inscripciones.", ex.Message);
        }

        [TestMethod]
        public async Task ConfirmarAsistencia_LaTienda_NoPodraIr()
        {
            _ = Inscripcion(7, EVENTO_GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");

            InscripcionEventoDTO dto = await Servicio().ConfirmarAsistencia(7, new ConfirmarAsistenciaDTO { Respuesta = "NoPodraIr" }, PALOMA);

            Assert.AreEqual(EstadoAsistenciaEvento.NoPodraIr, dto.EstadoAsistencia);
            Assert.AreEqual("No podrá ir", dto.EstadoAsistenciaTexto);
            Assert.AreEqual(OrigenRespuestaAsistenciaEvento.Tienda, dto.AsistenciaConfirmada.Origen);
            Assert.AreEqual("Inscrita", inscripciones.Single().Estado, "«No podré ir» no anula");
        }

        [TestMethod]
        public async Task ConfirmarAsistencia_OtraTiendaOVendedor_403()
        {
            _ = Inscripcion(7, EVENTO_GRATIS, "15191", EstadoInscripcionEvento.Inscrita, "Gratuita");

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().ConfirmarAsistencia(7, new ConfirmarAsistenciaDTO { Respuesta = "Confirma" }, REINA));
            Assert.AreEqual(HttpStatusCode.Forbidden, ex.StatusCode);
        }

        [TestMethod]
        public async Task ConfirmarAsistencia_EventoPasadoORespuestaRara_400()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Pagada);

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio(new DateTime(2026, 10, 31)).ConfirmarAsistencia(7, new ConfirmarAsistenciaDTO { Respuesta = "Confirma" }, ClienteTienda("15191")));
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() =>
                Servicio().ConfirmarAsistencia(7, new ConfirmarAsistenciaDTO { Respuesta = "Quizá" }, ClienteTienda("15191")));
        }

        [TestMethod]
        public async Task EnlaceDelCorreo_ElGetNoApuntaYElPostSi()
        {
            Guid clave = Guid.NewGuid();
            EventoInscripcion inscripcion = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            inscripcion.ClaveAsistencia = clave;

            ResultadoAsistenciaEnlace leido = await Servicio().LeerAsistenciaConClave(clave);
            Assert.IsTrue(leido.Existe);
            Assert.IsNull(leido.Mensaje);
            Assert.IsNull(inscripcion.AsistenciaRespuesta, "El GET no apunta nada");

            ResultadoAsistenciaEnlace apuntado = await Servicio().ConfirmarAsistenciaConClave(clave, "Confirma");
            Assert.IsTrue(apuntado.Apuntada);
            StringAssert.Contains(apuntado.Mensaje, "a las 10:00");
            Assert.AreEqual("Confirma", inscripcion.AsistenciaRespuesta);
            Assert.AreEqual("Cliente", inscripcion.AsistenciaOrigen);
        }

        [TestMethod]
        public async Task EnlaceDelCorreo_ClaveDesconocida_NoExiste()
        {
            ResultadoAsistenciaEnlace resultado = await Servicio().ConfirmarAsistenciaConClave(Guid.NewGuid(), "Confirma");

            Assert.IsFalse(resultado.Existe);
        }

        [TestMethod]
        public async Task Controlador_EnlaceDelCorreo_PaginaConBotonYFormularioPost()
        {
            Guid clave = Guid.NewGuid();
            EventoInscripcion inscripcion = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            inscripcion.ClaveAsistencia = clave;
            var controlador = new EventosController(db, ServicioEventos(), Servicio()) { Request = new HttpRequestMessage() };

            var pagina = await controlador.GetAsistenciaEnlace(clave, "Confirma") as ResponseMessageResult;
            string html = WebUtility.HtmlDecode(await pagina.Response.Content.ReadAsStringAsync());
            StringAssert.Contains(html, "Confirmo asistencia");
            Assert.IsFalse(html.Contains("No podré ir"), "Con respuesta=Confirma, solo su botón");
            StringAssert.Contains(html, "method=\"post\"");

            controlador.Request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/Eventos/Asistencia/" + clave)
            {
                Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("respuesta", "NoPodraIr") })
            };
            var resultado = await controlador.PostAsistenciaEnlace(clave) as ResponseMessageResult;
            Assert.AreEqual(HttpStatusCode.OK, resultado.Response.StatusCode);
            Assert.AreEqual("NoPodraIr", inscripcion.AsistenciaRespuesta);
        }

        // ---------------- Recordatorio ----------------

        [TestMethod]
        public async Task PendientesDeRecordatorio_DosDiasAntes_PagadasYGratuitasConClave()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            _ = Inscripcion(8, EVENTO_GRATIS, "20000", EstadoInscripcionEvento.Inscrita, "Gratuita");
            _ = Inscripcion(9, EVENTO_PAGO, "20000", EstadoInscripcionEvento.Reservada);
            EventoInscripcion yaAvisada = Inscripcion(10, EVENTO_PAGO, "30000", EstadoInscripcionEvento.Pagada);
            yaAvisada.RecordatorioEnviado = HOY.AddDays(-1);
            _ = Inscripcion(11, EVENTO_SOLO_NR, "15191", EstadoInscripcionEvento.Pagada, "NoReembolsable");

            var pendientes = await Servicio().PendientesDeRecordatorio(RecordatorioEventosJobsService.DIAS_ANTES);

            CollectionAssert.AreEquivalent(new[] { 7, 8 }, pendientes.SelectMany(p => p.Value).Select(i => i.Id).ToArray());
            Assert.IsTrue(pendientes.SelectMany(p => p.Value).All(i => i.ClaveAsistencia.HasValue));
        }

        private DependenciasRecordatorioEventos Dependencias(DateTime ahora, IServicioNotificacionesPush push, IServicioCorreoElectronico correo,
            List<EventoInscripcion> marcadas, bool encendido = true) => new DependenciasRecordatorioEventos
            {
                Encendido = () => encendido,
                Ahora = () => ahora,
                Pendientes = dias => Servicio(ahora).PendientesDeRecordatorio(dias),
                MarcarEnviados = l => { marcadas.AddRange(l); return Task.CompletedTask; },
                CorreoCliente = (e, c, k) => Task.FromResult(c == "15191" ? "luna@example.com" : null),
                NombreCliente = (e, c, k) => Task.FromResult("Luna"),
                Push = push,
                Correo = correo
            };

        [TestMethod]
        public async Task Recordatorio_Apagado_NoHaceNada()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            var marcadas = new List<EventoInscripcion>();

            ResultadoRecordatorioEventos r = await RecordatorioEventosJobsService.Procesar(
                Dependencias(HOY, push, A.Fake<IServicioCorreoElectronico>(), marcadas, encendido: false));

            Assert.AreEqual(RecordatorioEventosJobsService.MOTIVO_APAGADO, r.MotivoNoSeEnvia);
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Recordatorio_DeMadrugada_NoSale()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();

            ResultadoRecordatorioEventos r = await RecordatorioEventosJobsService.Procesar(
                Dependencias(new DateTime(2026, 10, 28, 3, 0, 0), push, A.Fake<IServicioCorreoElectronico>(), new List<EventoInscripcion>()));

            Assert.AreEqual(RecordatorioEventosJobsService.MOTIVO_HORARIO, r.MotivoNoSeEnvia);
            Assert.IsFalse(RecordatorioEventosJobsService.DentroDeHorario(new DateTime(2026, 10, 28, 21, 0, 0)));
            Assert.IsTrue(RecordatorioEventosJobsService.DentroDeHorario(new DateTime(2026, 10, 28, 9, 0, 0)));
            A.CallTo(() => push.EnviarACliente(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Recordatorio_PushYCorreoDeUstedYSeApuntaUnaVez()
        {
            _ = Inscripcion(7, EVENTO_PAGO, "15191", EstadoInscripcionEvento.Facturada);
            _ = Inscripcion(8, EVENTO_GRATIS, "20000", EstadoInscripcionEvento.Inscrita, "Gratuita");
            IServicioNotificacionesPush push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.EnviarACliente("1", "15191", A<NotificacionPushDTO>._)).Returns(Task.FromResult(1));
            A.CallTo(() => push.EnviarACliente("1", "20000", A<NotificacionPushDTO>._)).Returns(Task.FromResult(0));
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).ReturnsLazily((MailMessage m) => { enviado = m; return true; });
            var marcadas = new List<EventoInscripcion>();

            ResultadoRecordatorioEventos r = await RecordatorioEventosJobsService.Procesar(Dependencias(HOY, push, correo, marcadas));

            Assert.AreEqual(2, r.Inscripciones);
            Assert.AreEqual(1, r.Push);
            Assert.AreEqual(1, r.Correos);
            CollectionAssert.AreEqual(new[] { 8 }, r.SinCanal);
            CollectionAssert.AreEquivalent(new[] { 7, 8 }, marcadas.Select(i => i.Id).ToArray());
            A.CallTo(() => push.EnviarACliente("1", "15191", A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == "RecordatorioEvento" && n.Datos["inscripcionId"] == "7" && n.Datos["horaInicio"] == "10:00"
                && n.Cuerpo.Contains("Le esperamos") && n.Cuerpo.Contains("¿Nos confirma su asistencia?")))).MustHaveHappenedOnceExactly();
            Assert.AreEqual("luna@example.com", enviado.To.Single().Address);
            StringAssert.Contains(enviado.Body, "Confirmo asistencia");
            StringAssert.Contains(WebUtility.HtmlDecode(enviado.Body), "No podré ir");
            StringAssert.Contains(enviado.Body, "/api/Eventos/Asistencia/" + inscripciones.Single(i => i.Id == 7).ClaveAsistencia.Value.ToString("D") + "?respuesta=Confirma");
            StringAssert.Contains(enviado.Body, "Lo imparte Maystar");
        }

        [TestMethod]
        public void Recordatorio_CuandoYDonde()
        {
            Assert.AreEqual("el viernes 30 de octubre a las 10:00 en Reina", RecordatorioEventosJobsService.CuandoYDonde(eventos[0]));
            Assert.AreEqual("el viernes 30 de octubre en Alcobendas", RecordatorioEventosJobsService.CuandoYDonde(eventos[1]));
        }

        // ---------------- Facturación y cupón por modalidad ----------------

        [TestMethod]
        public void Facturacion_NoReembolsablePagadaPorSuImporte_SeAutomatiza()
        {
            var inscripcion = new EventoInscripcion { Modalidad = "NoReembolsable", ImportePagado = 60 };
            Assert.IsNull(ReglasFacturacionCurso.MotivoNoAutomatizable(inscripcion, eventos[0]));
            StringAssert.Contains(ReglasFacturacionCurso.MotivoNoAutomatizable(new EventoInscripcion { Modalidad = "NoReembolsable", ImportePagado = 100 }, eventos[0]),
                "la señal no reembolsable");
            Assert.IsNull(ReglasFacturacionCurso.MotivoNoAutomatizable(new EventoInscripcion { ImportePagado = 100 }, eventos[0]), "Las de antes, como hoy");
        }

        [TestMethod]
        public async Task Facturacion_Gratuita_NiFacturaNiAvisa()
        {
            IRepositorioFacturacionCursos repositorio = A.Fake<IRepositorioFacturacionCursos>();
            IAvisadorFacturacionCurso avisador = A.Fake<IAvisadorFacturacionCurso>();
            var confirmador = new ConfirmadorSenalEvento(db, () => true, repositorio, A.Fake<ICreadorPedidoCurso>(), A.Fake<IFacturadorPedidoCurso>(),
                A.Fake<ILiquidadorSenalCurso>(), avisador, A.Fake<IEmisorCuponEvento>(), () => HOY, "SenalesEventos");
            var inscripcion = new EventoInscripcion { Id = 7, Estado = "Inscrita", Modalidad = "Gratuita", Cliente = "15191" };

            ResultadoConfirmacionSenal r = await confirmador.ConfirmarSenal(inscripcion, eventos[1], aMano: false);

            Assert.IsTrue(r.Omitido);
            A.CallTo(() => repositorio.Reclamar(A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._)).MustNotHaveHappened();
            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => confirmador.ConfirmarSenal(inscripcion, eventos[1], aMano: true));
        }

        [TestMethod]
        public async Task Cupon_NoReembolsable_NoSeEmite()
        {
            IRepositorioCupones repositorio = A.Fake<IRepositorioCupones>();
            var emisor = new EmisorCuponesEvento(repositorio, A.Fake<IAvisadorCupones>(), () => HOY);

            await emisor.Emitir(new EventoInscripcion { Id = 7, Empresa = "1", Cliente = "15191", Estado = "Facturada", Modalidad = "NoReembolsable" },
                eventos[0], new FacturacionCursoInscripcion { Factura = "CV2600700" }, "SenalesEventos");

            A.CallTo(() => repositorio.Insertar(A<CuponNuevo>._, A<Func<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void FacturadasSinCupon_SoloLasReembolsables()
        {
            StringAssert.Contains(RepositorioFacturacionCursos.SQL_FACTURADAS_SIN_CUPON, "ISNULL(i.Modalidad, ''Reembolsable'') = ''Reembolsable''");
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
