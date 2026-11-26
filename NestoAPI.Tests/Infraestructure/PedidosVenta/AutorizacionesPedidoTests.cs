using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PedidosVenta.Autorizaciones;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    // Novedades 1258: pedir autorización de un pedido que no pasa la validación de ofertas y descuentos.
    [TestClass]
    public class AutorizacionesPedidoTests
    {
        private const string PERMITIR = Constantes.ParametrosUsuario.PERMITIR_CREAR_PEDIDO_CON_ERRORES;

        private IRepositorioAutorizacionesPedido repositorio;
        private IDestinatariosAutorizacionPedido destinatarios;
        private IAvisosAutorizacionPedido avisos;
        private Dictionary<string, string> parametros;
        private RespuestaValidacion validacion;
        private readonly DateTime ahora = new DateTime(2026, 11, 20, 10, 0, 0);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private string LeerParametro(string empresa, string usuario, string clave)
            => parametros.TryGetValue(ReglaOmitirValidacionPedido.SinDominio(usuario) + "|" + clave, out string v) ? v : null;

        private ServicioAutorizacionesPedido Servicio() => new ServicioAutorizacionesPedido(repositorio, destinatarios, avisos,
            (p, u) => validacion, LeerParametro, (p, fallback) => p?.Identity?.Name ?? fallback, () => ahora);

        private static PedidoVentaDTO Pedido(params (string producto, int cantidad, string almacen)[] lineas)
        {
            var p = new PedidoVentaDTO { empresa = "1", cliente = "15191", contacto = "0", vendedor = "JE", Usuario = "NUEVAVISION\\Ana" };
            foreach (var l in lineas)
            {
                p.Lineas.Add(new LineaPedidoVentaDTO { Producto = l.producto, Cantidad = l.cantidad, almacen = l.almacen, PrecioUnitario = 10, tipoLinea = 1 });
            }
            return p;
        }

        private static RespuestaValidacion NoPasa(string permiso = null) => new RespuestaValidacion
        {
            ValidacionSuperada = false,
            Motivos = new List<string> { "No se encuentra autorizado el descuento del 20 % en el producto 17404" },
            Errores = new List<ErrorValidacion> { new ErrorValidacion { ProductoId = "17404", Motivo = "No se encuentra autorizado el descuento del 20 %" } },
            PermisoNecesario = permiso
        };

        private FilaAutorizacionPedido Fila(int id = 7, string estado = EstadosAutorizacionPedido.PENDIENTE, string almacenes = "ALG", string permiso = null)
        {
            PedidoVentaDTO pedido = Pedido(("17404", 2, "ALG"));
            return new FilaAutorizacionPedido
            {
                Id = id,
                Empresa = "1",
                Cliente = "15191",
                Contacto = "0",
                Solicitante = "NUEVAVISION\\Ana",
                FechaSolicitud = ahora.AddHours(-1),
                PedidoJson = ServicioAutorizacionesPedido.SerializarPedido(pedido),
                Motivos = MotivosAutorizacionPedido.De(NoPasa()).AJson(),
                Estado = estado,
                Almacenes = almacenes,
                PermisoNecesario = permiso,
                Vendedor = "JE",
                Importe = 20,
                NumeroLineas = 1
            };
        }

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioAutorizacionesPedido>();
            destinatarios = A.Fake<IDestinatariosAutorizacionPedido>();
            avisos = A.Fake<IAvisosAutorizacionPedido>();
            parametros = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            validacion = NoPasa();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.Insertar(A<FilaAutorizacionPedido>._)).Returns(7);
            A.CallTo(() => repositorio.BloquearParaAutorizar(A<int>._)).Returns(A.Fake<IDisposable>());
            A.CallTo(() => repositorio.Resolver(A<int>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<int?>._)).Returns(true);
            A.CallTo(() => destinatarios.Leer(A<string>._, A<IReadOnlyCollection<string>>._, A<string>._, A<string>._))
                .Returns(new List<string> { "NUEVAVISION\\Carlos", "NUEVAVISION\\Alfredo" });
        }

        // ---------- LA REGLA ----------

        [TestMethod]
        public void Regla_DireccionAlmacenOParametro_PuedenSinMirarElAlmacen()
        {
            Assert.IsTrue(ReglaOmitirValidacionPedido.GrupoPermite(true, false, null, false, null, new[] { "REI" }));
            Assert.IsTrue(ReglaOmitirValidacionPedido.GrupoPermite(false, true, null, false, null, new[] { "REI" }));
            Assert.IsTrue(ReglaOmitirValidacionPedido.GrupoPermite(false, false, () => true, false, null, new[] { "REI" }));
            Assert.IsFalse(ReglaOmitirValidacionPedido.GrupoPermite(false, false, () => false, false, () => "REI", new[] { "REI" }));
        }

        [TestMethod]
        public void Regla_Tiendas_SoloConTodasLasLineasDeSuAlmacen()
        {
            Assert.IsTrue(ReglaOmitirValidacionPedido.GrupoPermite(false, false, () => false, true, () => "REI ", new[] { "REI", " REI" }));
            Assert.IsFalse(ReglaOmitirValidacionPedido.GrupoPermite(false, false, () => false, true, () => "REI", new[] { "REI", "ALG" }));
            Assert.IsFalse(ReglaOmitirValidacionPedido.GrupoPermite(false, false, () => false, true, () => null, new[] { "REI" }));
            Assert.IsFalse(ReglaOmitirValidacionPedido.GrupoPermite(false, false, () => false, true, () => "REI", new string[0]));
        }

        [TestMethod]
        public void Regla_DireccionNoLeeLosParametros()
        {
            bool leido = false;
            Assert.IsTrue(ReglaOmitirValidacionPedido.GrupoPermite(true, false, () => { leido = true; return false; }, false, null, new[] { "ALG" }));
            Assert.IsFalse(leido, "leer un parámetro con «(defecto)» crea la fila del usuario: solo si hace falta");
        }

        [TestMethod]
        public void Regla_PermisoNecesario_HaceFaltaAdemas()
        {
            Assert.IsTrue(ReglaOmitirValidacionPedido.PuedeOmitir(true, null, () => false));
            Assert.IsFalse(ReglaOmitirValidacionPedido.PuedeOmitir(true, "PermisoFamiliaX", () => false));
            Assert.IsTrue(ReglaOmitirValidacionPedido.PuedeOmitir(true, "PermisoFamiliaX", () => true));
            Assert.IsFalse(ReglaOmitirValidacionPedido.PuedeOmitir(false, null, () => true));
        }

        [TestMethod]
        public void Regla_ValorActivado()
        {
            foreach (string si in new[] { "1", "true", " Sí ", "SI" })
            {
                Assert.IsTrue(ReglaOmitirValidacionPedido.ValorActivado(si), si);
            }
            foreach (string no in new[] { null, "", "0", "no", "false" })
            {
                Assert.IsFalse(ReglaOmitirValidacionPedido.ValorActivado(no), no ?? "null");
            }
        }

        [TestMethod]
        public void Regla_ConPrincipal_TokenDeClienteNunca()
        {
            var cliente = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "a@b.es"), new Claim("cliente", "15191"),
                new Claim(ClaimTypes.Role, "NUEVAVISION\\Dirección") }, "prueba"));
            Assert.IsFalse(ReglaOmitirValidacionPedido.PuedeOmitir(cliente, "a@b.es", "1", new[] { "ALG" }, null, LeerParametro));
            Assert.IsTrue(ReglaOmitirValidacionPedido.PuedeOmitir(Usuario("NUEVAVISION\\Carlos", "Dirección"), "NUEVAVISION\\Carlos", "1", new[] { "ALG" }, null, LeerParametro));
        }

        [TestMethod]
        public void Regla_ConPrincipal_ParametroYTiendas()
        {
            parametros["Pepe|" + PERMITIR] = "1";
            parametros["Laura|" + ReglaOmitirValidacionPedido.PARAMETRO_ALMACEN] = "REI";
            Assert.IsTrue(ReglaOmitirValidacionPedido.PuedeOmitir(Usuario("NUEVAVISION\\Pepe"), "NUEVAVISION\\Pepe", "1", new[] { "ALG" }, null, LeerParametro));
            Assert.IsTrue(ReglaOmitirValidacionPedido.PuedeOmitir(Usuario("NUEVAVISION\\Laura", "Tiendas"), "NUEVAVISION\\Laura", "1", new[] { "REI" }, null, LeerParametro));
            Assert.IsFalse(ReglaOmitirValidacionPedido.PuedeOmitir(Usuario("NUEVAVISION\\Laura", "Tiendas"), "NUEVAVISION\\Laura", "1", new[] { "ALG" }, null, LeerParametro));
            Assert.IsFalse(ReglaOmitirValidacionPedido.PuedeOmitir(Usuario("NUEVAVISION\\Ana"), "NUEVAVISION\\Ana", "1", new[] { "ALG" }, null, LeerParametro));
        }

        // ---------- destinatarios, huella, estados ----------

        [TestMethod]
        public void Destinatarios_GruposParametroYTiendasDeEseAlmacen_SinElSolicitanteNiRepetidos()
        {
            var filas = new List<ParametroDestinatarioAutorizacion>
            {
                new ParametroDestinatarioAutorizacion { Usuario = "Pepe", Clave = PERMITIR, Valor = "1" },
                new ParametroDestinatarioAutorizacion { Usuario = "Nadie", Clave = PERMITIR, Valor = "0" },
                new ParametroDestinatarioAutorizacion { Usuario = "Laura", Clave = ReglaOmitirValidacionPedido.PARAMETRO_ALMACEN, Valor = "REI" },
                new ParametroDestinatarioAutorizacion { Usuario = "Paloma", Clave = ReglaOmitirValidacionPedido.PARAMETRO_ALMACEN, Valor = "ALC" }
            };
            List<string> lista = ReglasAutorizacionPedido.Destinatarios(new[] { "Carlos", "Ana" }, new[] { "Alfredo", "carlos" }, new[] { "Laura", "Paloma" },
                filas, new[] { "REI" }, null, "NUEVAVISION\\Ana");
            CollectionAssert.AreEquivalent(new[] { "NUEVAVISION\\Carlos", "NUEVAVISION\\Alfredo", "NUEVAVISION\\Pepe", "NUEVAVISION\\Laura" }, lista);
        }

        [TestMethod]
        public void Destinatarios_ConPermisoNecesario_SoloQuienLoTiene()
        {
            var filas = new List<ParametroDestinatarioAutorizacion>
            {
                new ParametroDestinatarioAutorizacion { Usuario = "Alfredo", Clave = "PermisoFamiliaX", Valor = "1" }
            };
            List<string> lista = ReglasAutorizacionPedido.Destinatarios(new[] { "Carlos" }, new[] { "Alfredo" }, null, filas, new[] { "ALG" }, "PermisoFamiliaX", "Ana");
            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Alfredo" }, lista);
        }

        [TestMethod]
        public void Huella_NoDependeDelOrdenYCambiaConLasLineas()
        {
            string a = ReglasAutorizacionPedido.Huella(Pedido(("17404", 2, "ALG"), ("40010", 1, "ALG")));
            string b = ReglasAutorizacionPedido.Huella(Pedido(("40010", 1, "ALG"), ("17404 ", 2, "ALG")));
            string c = ReglasAutorizacionPedido.Huella(Pedido(("17404", 3, "ALG"), ("40010", 1, "ALG")));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
            Assert.AreEqual(64, a.Length);
        }

        [TestMethod]
        public void MotivoNoPendiente_SoloSiNoEstaPendiente()
        {
            Assert.IsNull(ReglasAutorizacionPedido.MotivoNoPendiente(Fila()));
            FilaAutorizacionPedido autorizada = Fila(estado: EstadosAutorizacionPedido.AUTORIZADA);
            autorizada.PedidoCreado = 931500;
            autorizada.UsuarioResolucion = "NUEVAVISION\\Carlos";
            StringAssert.Contains(ReglasAutorizacionPedido.MotivoNoPendiente(autorizada), "931500");
            Assert.IsNotNull(ReglasAutorizacionPedido.MotivoNoPendiente(Fila(estado: EstadosAutorizacionPedido.DENEGADA)));
            Assert.IsNotNull(ReglasAutorizacionPedido.MotivoNoPendiente(Fila(estado: EstadosAutorizacionPedido.ANULADA)));
        }

        [TestMethod]
        public void SerializarPedido_RecogeEnTiendaSinMandar_SigueSinMandar()
        {
            PedidoVentaDTO p = Pedido(("17404", 1, "REI"));
            p.ruta = "FW";
            PedidoVentaDTO vuelta = Newtonsoft.Json.JsonConvert.DeserializeObject<PedidoVentaDTO>(ServicioAutorizacionesPedido.SerializarPedido(p));
            Assert.IsNull(vuelta.RecogeEnTiendaRecibido, "sin mandar = no tocar la ruta (NestoAPI#571)");
            Assert.AreEqual(1, vuelta.Lineas.Count);
        }

        // ---------- servicio ----------

        [TestMethod]
        public async Task Solicitar_SiPasaLaValidacion_400NoNecesita()
        {
            validacion = new RespuestaValidacion { ValidacionSuperada = true };
            ResultadoAutorizacionPedido r = await Servicio().Solicitar(Usuario("NUEVAVISION\\Ana"), new SolicitarAutorizacionPedidoRequest { Pedido = Pedido(("17404", 1, "ALG")) });
            Assert.AreEqual(HttpStatusCode.BadRequest, r.Codigo);
            Assert.AreEqual(TextosAutorizacionPedido.ERROR_NO_NECESITA, r.Mensaje);
            A.CallTo(() => repositorio.Insertar(A<FilaAutorizacionPedido>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Solicitar_NoPasa_GuardaConSusMotivosYAvisaALosQuePuedenAutorizar()
        {
            FilaAutorizacionPedido guardada = null;
            A.CallTo(() => repositorio.Insertar(A<FilaAutorizacionPedido>._)).Invokes((FilaAutorizacionPedido f) => guardada = f).Returns(7);
            ResultadoAutorizacionPedido r = await Servicio().Solicitar(Usuario("NUEVAVISION\\Ana"),
                new SolicitarAutorizacionPedidoRequest { Pedido = Pedido(("17404", 2, "ALG")), Comentario = "Lo ha pedido el cliente" });

            Assert.AreEqual(HttpStatusCode.Created, r.Codigo);
            var creada = (SolicitudAutorizacionCreadaDTO)r.Cuerpo;
            Assert.AreEqual(7, creada.Id);
            Assert.AreEqual("Pendiente", creada.Estado);
            Assert.AreEqual("17404", creada.Errores.Single().Producto);
            CollectionAssert.AreEqual(new[] { "Carlos", "Alfredo" }, creada.Destinatarios);
            Assert.AreEqual("NUEVAVISION\\Ana", guardada.Solicitante);
            Assert.AreEqual(20m, guardada.Importe);
            Assert.AreEqual("ALG", guardada.Almacenes);
            Assert.AreEqual(ahora, guardada.FechaSolicitud);
            A.CallTo(() => avisos.Pedida(A<IReadOnlyCollection<string>>.That.Matches(l => l.Count == 2),
                A<NotificacionPushDTO>.That.Matches(n => n.Tipo == TextosAutorizacionPedido.TIPO_PEDIDA && n.Datos["id"] == "7"
                    && n.Cuerpo.StartsWith("Ana pide autorización para un pedido del cliente 15191")))).MustHaveHappenedOnceExactly();
            Assert.IsFalse(NotificacionesUrgentes.EsUrgente(TextosAutorizacionPedido.TIPO_PEDIDA), "no es urgente");
        }

        [TestMethod]
        public async Task Solicitar_IdenticaPendiente_409ConSuId()
        {
            A.CallTo(() => repositorio.PendienteIdentica("NUEVAVISION\\Ana", A<string>._, ahora.AddHours(-24))).Returns(5);
            ResultadoAutorizacionPedido r = await Servicio().Solicitar(Usuario("NUEVAVISION\\Ana"), new SolicitarAutorizacionPedidoRequest { Pedido = Pedido(("17404", 2, "ALG")) });
            Assert.AreEqual(HttpStatusCode.Conflict, r.Codigo);
            Assert.AreEqual(5, r.IdExistente);
            A.CallTo(() => repositorio.Insertar(A<FilaAutorizacionPedido>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Solicitar_ComentarioLargo_400()
        {
            ResultadoAutorizacionPedido r = await Servicio().Solicitar(Usuario("NUEVAVISION\\Ana"),
                new SolicitarAutorizacionPedidoRequest { Pedido = Pedido(("17404", 2, "ALG")), Comentario = new string('x', 501) });
            Assert.AreEqual(HttpStatusCode.BadRequest, r.Codigo);
        }

        [TestMethod]
        public async Task Autorizar_QuienNoPuede_403YNoCreaNada()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            bool creado = false;
            ResultadoAutorizacionPedido r = await Servicio().Autorizar(Usuario("NUEVAVISION\\Ana"), 7, null,
                (p, u) => { creado = true; return Task.FromResult<IHttpActionResult>(null); });
            Assert.AreEqual(HttpStatusCode.Forbidden, r.Codigo);
            Assert.IsFalse(creado);
        }

        [TestMethod]
        public async Task Autorizar_CreaPorElPostDeSiempreANombreDelSolicitante()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            PedidoVentaDTO recibido = null;
            string autorizadorRecibido = null;
            ResultadoAutorizacionPedido r = await Servicio().Autorizar(Usuario("NUEVAVISION\\Carlos", "Dirección"), 7, new AutorizarPedidoRequest { Comentario = "Por esta vez" },
                (p, u) =>
                {
                    recibido = p;
                    autorizadorRecibido = u;
                    p.numero = 931500;
                    return Task.FromResult<IHttpActionResult>(new CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>("DefaultApi",
                        new Dictionary<string, object>(), p, new FakeController()));
                });

            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            Assert.AreEqual(931500, ((ResolucionAutorizacionPedidoDTO)r.Cuerpo).Pedido);
            Assert.AreEqual("NUEVAVISION\\Ana", recibido.Usuario);
            Assert.IsTrue(recibido.CreadoSinPasarValidacion);
            Assert.AreEqual("NUEVAVISION\\Carlos", autorizadorRecibido);
            A.CallTo(() => repositorio.Resolver(7, EstadosAutorizacionPedido.AUTORIZADA, "NUEVAVISION\\Carlos", ahora, "Por esta vez", 931500)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Rastro(RepositorioAutorizacionesPedido.TABLA_RASTRO, A<string>._, A<string>.That.Contains("931500"), "NUEVAVISION\\Carlos"))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.Resuelta("NUEVAVISION\\Ana", "1", "JE", A<NotificacionPushDTO>.That.Matches(n =>
                n.Tipo == TextosAutorizacionPedido.TIPO_RESUELTA && n.Datos["estado"] == "Autorizada" && n.Datos["pedido"] == "931500"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.QuitarDeLosDemas(A<IReadOnlyCollection<string>>.That.Matches(l => l.Count == 1 && l.Contains("NUEVAVISION\\Alfredo")),
                A<NotificacionResueltaDTO>.That.Matches(n => n.Referencia == "7" && n.Resultado == "Autorizada"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Autorizar_SiElAltaFalla_DevuelveSuRespuestaYSigueSiendoPendiente()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            var fallo = new BadRequestErrorMessageResult("No hay stock", new FakeController());
            ResultadoAutorizacionPedido r = await Servicio().Autorizar(Usuario("NUEVAVISION\\Carlos", "Almacén"), 7, null,
                (p, u) => Task.FromResult<IHttpActionResult>(fallo));
            Assert.AreSame(fallo, r.RespuestaAlta);
            A.CallTo(() => repositorio.Resolver(A<int>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<int?>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Autorizar_YaNoPendiente_409()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila(estado: EstadosAutorizacionPedido.DENEGADA));
            ResultadoAutorizacionPedido r = await Servicio().Autorizar(Usuario("NUEVAVISION\\Carlos", "Dirección"), 7, null,
                (p, u) => throw new AssertFailedException("no debe crear"));
            Assert.AreEqual(HttpStatusCode.Conflict, r.Codigo);
        }

        [TestMethod]
        public async Task Autorizar_OtroLaEstaAutorizando_409()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            A.CallTo(() => repositorio.BloquearParaAutorizar(7)).Returns(null);
            ResultadoAutorizacionPedido r = await Servicio().Autorizar(Usuario("NUEVAVISION\\Carlos", "Dirección"), 7, null,
                (p, u) => throw new AssertFailedException("no debe crear"));
            Assert.AreEqual(HttpStatusCode.Conflict, r.Codigo);
        }

        [TestMethod]
        public async Task Autorizar_TiendasDeOtroAlmacen_403()
        {
            parametros["Laura|" + ReglaOmitirValidacionPedido.PARAMETRO_ALMACEN] = "REI";
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila(almacenes: "ALG"));
            ResultadoAutorizacionPedido r = await Servicio().Autorizar(Usuario("NUEVAVISION\\Laura", "Tiendas"), 7, null,
                (p, u) => throw new AssertFailedException("no debe crear"));
            Assert.AreEqual(HttpStatusCode.Forbidden, r.Codigo);
        }

        [TestMethod]
        public async Task Denegar_SinMotivo400_ConMotivoDenegadaYAvisa()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            ResultadoAutorizacionPedido sin = await Servicio().Denegar(Usuario("NUEVAVISION\\Carlos", "Dirección"), 7, new DenegarPedidoRequest { Motivo = " " });
            Assert.AreEqual(HttpStatusCode.BadRequest, sin.Codigo);

            ResultadoAutorizacionPedido r = await Servicio().Denegar(Usuario("NUEVAVISION\\Carlos", "Dirección"), 7, new DenegarPedidoRequest { Motivo = "El 20 % no" });
            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            Assert.AreEqual("Denegada", ((ResolucionAutorizacionPedidoDTO)r.Cuerpo).Estado);
            A.CallTo(() => repositorio.Resolver(7, EstadosAutorizacionPedido.DENEGADA, "NUEVAVISION\\Carlos", ahora, "El 20 % no", null)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.Resuelta("NUEVAVISION\\Ana", "1", "JE", A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("El 20 % no"))))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Denegar_QuienNoPuede_403()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            ResultadoAutorizacionPedido r = await Servicio().Denegar(Usuario("NUEVAVISION\\Ana"), 7, new DenegarPedidoRequest { Motivo = "no" });
            Assert.AreEqual(HttpStatusCode.Forbidden, r.Codigo);
        }

        [TestMethod]
        public async Task Anular_SoloQuienLaPidio()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Servicio().Anular(Usuario("NUEVAVISION\\Carlos", "Dirección"), 7)).Codigo);
            ResultadoAutorizacionPedido r = await Servicio().Anular(Usuario("NUEVAVISION\\ana"), 7);
            Assert.AreEqual(HttpStatusCode.OK, r.Codigo);
            Assert.AreEqual("Anulada", ((ResolucionAutorizacionPedidoDTO)r.Cuerpo).Estado);
            A.CallTo(() => avisos.Resuelta(A<string>._, A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Listado_QuienNoPuedeAutorizar_SoloLasSuyas_YQuienPuede_TodasSalvoMias()
        {
            FilaAutorizacionPedido deAna = Fila(1);
            FilaAutorizacionPedido deOtro = Fila(2);
            deOtro.Solicitante = "NUEVAVISION\\Pepa";
            A.CallTo(() => repositorio.Listado(ahora.Date.AddDays(-30), true)).Returns(new List<FilaAutorizacionPedido> { deAna, deOtro });

            List<AutorizacionPedidoDTO> ana = await Servicio().Listado(Usuario("NUEVAVISION\\Ana"), true, false);
            CollectionAssert.AreEqual(new[] { 1 }, ana.Select(a => a.Id).ToList());
            Assert.IsNull(ana[0].Pedido, "el listado no trae el pedido");

            List<AutorizacionPedidoDTO> carlos = await Servicio().Listado(Usuario("NUEVAVISION\\Carlos", "Dirección"), true, false);
            Assert.AreEqual(2, carlos.Count);
            Assert.AreEqual(0, (await Servicio().Listado(Usuario("NUEVAVISION\\Carlos", "Dirección"), true, true)).Count);
        }

        [TestMethod]
        public async Task Leer_TraeElPedido_Y403SiNoEsSuyaNiPuede()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            ResultadoAutorizacionPedido suya = await Servicio().Leer(Usuario("NUEVAVISION\\Ana"), 7);
            Assert.AreEqual("17404", ((AutorizacionPedidoDTO)suya.Cuerpo).Pedido.Lineas.Single().Producto);
            Assert.AreEqual(HttpStatusCode.Forbidden, (await Servicio().Leer(Usuario("NUEVAVISION\\Pepa"), 7)).Codigo);
        }

        // ---------- NestoAPI#909: PuedeAutorizar y PuedeAnular para quien pregunta ----------

        [TestMethod]
        public async Task Listado_PuedeAutorizar_LasDeOtrosPendientes_YPuedeAnularLasSuyasPendientes()
        {
            FilaAutorizacionPedido deAna = Fila(1);
            FilaAutorizacionPedido dePepa = Fila(2);
            dePepa.Solicitante = "NUEVAVISION\\Pepa";
            FilaAutorizacionPedido dePepaDenegada = Fila(3, EstadosAutorizacionPedido.DENEGADA);
            dePepaDenegada.Solicitante = "NUEVAVISION\\Pepa";
            FilaAutorizacionPedido deAnaAutorizada = Fila(4, EstadosAutorizacionPedido.AUTORIZADA);
            A.CallTo(() => repositorio.Listado(ahora.Date.AddDays(-30), false))
                .Returns(new List<FilaAutorizacionPedido> { deAna, dePepa, dePepaDenegada, deAnaAutorizada });
            // Ana puede autorizar por el parámetro (sin grupo): NestoApp solo conocía este caso
            parametros["Ana|" + PERMITIR] = "1";

            List<AutorizacionPedidoDTO> ana = await Servicio().Listado(Usuario("NUEVAVISION\\Ana"), false, false);

            Dictionary<int, AutorizacionPedidoDTO> porId = ana.ToDictionary(a => a.Id);
            Assert.IsFalse(porId[1].PuedeAutorizar, "la suya no");
            Assert.IsTrue(porId[1].PuedeAnular);
            Assert.IsTrue(porId[2].PuedeAutorizar);
            Assert.IsFalse(porId[2].PuedeAnular);
            Assert.IsFalse(porId[3].PuedeAutorizar, "ya resuelta");
            Assert.IsFalse(porId[4].PuedeAnular, "ya resuelta");
        }

        [TestMethod]
        public async Task Listado_DireccionSinParametro_PuedeAutorizar_Y_ParametrosLeidosUnaVezPorPeticion()
        {
            var filas = Enumerable.Range(1, 5).Select(i =>
            {
                FilaAutorizacionPedido f = Fila(i, permiso: "PermisoFamiliaX");
                f.Solicitante = "NUEVAVISION\\Pepa";
                return f;
            }).ToList();
            A.CallTo(() => repositorio.Listado(ahora.Date.AddDays(-30), true)).Returns(filas);
            parametros["Carlos|PermisoFamiliaX"] = "1";
            int lecturas = 0;
            var servicio = new ServicioAutorizacionesPedido(repositorio, destinatarios, avisos, (p, u) => validacion,
                (e, u, c) => { lecturas++; return LeerParametro(e, u, c); }, (p, fallback) => p?.Identity?.Name ?? fallback, () => ahora);

            List<AutorizacionPedidoDTO> carlos = await servicio.Listado(Usuario("NUEVAVISION\\Carlos", "Dirección"), true, false);

            Assert.AreEqual(5, carlos.Count);
            Assert.IsTrue(carlos.All(a => a.PuedeAutorizar));
            Assert.AreEqual(1, lecturas, "el permiso de la denegación se lee una vez, no una por fila");
        }

        [TestMethod]
        public async Task Listado_TiendasConLineasDeOtroAlmacen_LaVePeroNoPuedeAutorizar()
        {
            // Tiendas ve todas (PuedeAutorizarAlguno), pero la regla de ESTA solicitud dice que no (líneas de ALG)
            FilaAutorizacionPedido dePepa = Fila(2, almacenes: "ALG");
            dePepa.Solicitante = "NUEVAVISION\\Pepa";
            FilaAutorizacionPedido deReina = Fila(3, almacenes: "REI");
            deReina.Solicitante = "NUEVAVISION\\Pepa";
            A.CallTo(() => repositorio.Listado(ahora.Date.AddDays(-30), true)).Returns(new List<FilaAutorizacionPedido> { dePepa, deReina });
            parametros["Laura|" + ReglaOmitirValidacionPedido.PARAMETRO_ALMACEN] = "REI";

            List<AutorizacionPedidoDTO> laura = await Servicio().Listado(Usuario("NUEVAVISION\\Laura", "Tiendas"), true, false);

            CollectionAssert.AreEqual(new[] { 3 }, laura.Select(a => a.Id).ToList());
            Assert.IsTrue(laura.Single().PuedeAutorizar);
        }

        [TestMethod]
        public async Task Leer_LlevaPuedeAutorizarYPuedeAnular()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());

            var suya = (AutorizacionPedidoDTO)(await Servicio().Leer(Usuario("NUEVAVISION\\Ana"), 7)).Cuerpo;
            var deCarlos = (AutorizacionPedidoDTO)(await Servicio().Leer(Usuario("NUEVAVISION\\Carlos", "Almacén"), 7)).Cuerpo;

            Assert.IsFalse(suya.PuedeAutorizar);
            Assert.IsTrue(suya.PuedeAnular);
            Assert.IsTrue(deCarlos.PuedeAutorizar);
            Assert.IsFalse(deCarlos.PuedeAnular);
        }

        // ---------- controlador ----------

        [TestMethod]
        public void Controlador_PideEstarIdentificado()
        {
            Assert.IsTrue(typeof(PedidosVentaAutorizacionesController).GetCustomAttributes(true).OfType<AuthorizeAttribute>().Any());
        }

        [TestMethod]
        public async Task Controlador_SinTabla_503EnLosSeis()
        {
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            var c = new PedidosVentaAutorizacionesController
            {
                CrearServicio = Servicio,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("NUEVAVISION\\Carlos", "Dirección")
            };
            foreach (IHttpActionResult resultado in new[]
            {
                await c.PostSolicitud(new SolicitarAutorizacionPedidoRequest()),
                await c.GetSolicitudes(),
                await c.GetSolicitud(7),
                await c.PostAutorizar(7, null),
                await c.PostDenegar(7, new DenegarPedidoRequest { Motivo = "x" }),
                await c.DeleteSolicitud(7)
            })
            {
                HttpResponseMessage m = await resultado.ExecuteAsync(default);
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, m.StatusCode);
            }
        }

        [TestMethod]
        public async Task Controlador_Autorizar_UsaSuAltaDePedido_YDevuelveLaDelPostSiFalla()
        {
            A.CallTo(() => repositorio.Leer(7)).Returns(Fila());
            bool usada = false;
            var c = new PedidosVentaAutorizacionesController
            {
                CrearServicio = Servicio,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("NUEVAVISION\\Carlos", "Dirección"),
                CrearPedido = (p, u) => { usada = true; return Task.FromResult<IHttpActionResult>(new BadRequestErrorMessageResult("Sin stock", new FakeController())); }
            };
            IHttpActionResult r = await c.PostAutorizar(7, null);
            Assert.IsTrue(usada);
            Assert.IsInstanceOfType(r, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task Controlador_YaSolicitada_409ConCodigoEId()
        {
            A.CallTo(() => repositorio.PendienteIdentica(A<string>._, A<string>._, A<DateTime>._)).Returns(5);
            var c = new PedidosVentaAutorizacionesController
            {
                CrearServicio = Servicio,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = Usuario("NUEVAVISION\\Ana")
            };
            HttpResponseMessage m = await (await c.PostSolicitud(new SolicitarAutorizacionPedidoRequest { Pedido = Pedido(("17404", 1, "ALG")) })).ExecuteAsync(default);
            Assert.AreEqual(HttpStatusCode.Conflict, m.StatusCode);
            string cuerpo = await m.Content.ReadAsStringAsync();
            StringAssert.Contains(cuerpo, "YA_SOLICITADA");
            StringAssert.Contains(cuerpo, "\"Id\":5");
        }

        [TestMethod]
        public void PedidosVentaController_UsaLaRegla_ConLosParametrosDeQuienAutoriza()
        {
            // El POST de PedidosVenta es el núcleo: la autorización solo le dice de quién son los parámetros que cuentan.
            var prop = typeof(PedidosVentaController).GetProperty("UsuarioPermisosValidacion",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            Assert.IsNotNull(prop);
        }

        // ================================================================ Carlos 26/11/26: push también a quien la pidió

        [TestMethod]
        public void ReglaPushAutorizacionResuelta_SoloSiTieneNestoAppYNoEsElVendedor()
        {
            Assert.IsTrue(ReglaPushAutorizacionResuelta.PushAlSolicitante(new[] { 3 }, new[] { 9 }));
            Assert.IsTrue(ReglaPushAutorizacionResuelta.PushAlSolicitante(new[] { 3 }, null), "Pedido sin vendedor con app");
            Assert.IsFalse(ReglaPushAutorizacionResuelta.PushAlSolicitante(new int[0], new[] { 9 }), "Sin NestoApp, nada");
            Assert.IsFalse(ReglaPushAutorizacionResuelta.PushAlSolicitante(new[] { 9 }, new[] { 9, 10 }), "Es el vendedor: ya lo recibe");
        }

        [TestMethod]
        public async Task AvisosResuelta_PushAlVendedorYAQuienLaPidio_SinDuplicar()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            A.CallTo(() => push.ObtenerDispositivosVendedor("1", "JE", Constantes.Aplicaciones.NESTO_APP))
                .Returns(Task.FromResult(new List<DispositivoNotificacion> { new DispositivoNotificacion { Id = 9 } }));
            A.CallTo(() => push.ObtenerDispositivosUsuario("ana", Constantes.Aplicaciones.NESTO_APP))
                .Returns(Task.FromResult(new List<DispositivoNotificacion> { new DispositivoNotificacion { Id = 3 } }));
            A.CallTo(() => push.ObtenerDispositivosUsuario("jefe", Constantes.Aplicaciones.NESTO_APP))
                .Returns(Task.FromResult(new List<DispositivoNotificacion> { new DispositivoNotificacion { Id = 9 } }));
            var avisosReales = new AvisosAutorizacionPedido(push, A.Fake<IAvisosTiempoReal>());
            var aviso = new NotificacionPushDTO { Titulo = "t", Cuerpo = "c" };

            await avisosReales.Resuelta("ana", "1", "JE", aviso);
            A.CallTo(() => push.EnviarAVendedor("1", "JE", aviso)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarAUsuario("ana", Constantes.Aplicaciones.NESTO_APP, aviso)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();

            // El que la pidió ES el vendedor del pedido: un solo push
            await avisosReales.Resuelta("jefe", "1", "JE", aviso);
            A.CallTo(() => push.EnviarAUsuario("jefe", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();

            // Desde Nesto (con dominio) y sin NestoApp: la campana, sin push suyo
            await avisosReales.Resuelta("NUEVAVISION\\Luis", "1", null, aviso);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Luis", Constantes.Aplicaciones.NESTO, aviso)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarAUsuario("NUEVAVISION\\Luis", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
        }

        private class FakeController : ApiController
        {
            public FakeController()
            {
                Request = new HttpRequestMessage();
                Configuration = new HttpConfiguration();
            }
        }
    }
}
