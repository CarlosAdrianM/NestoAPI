using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Los avisos guardados en memoria (lo que haría la tabla AvisosFichaProducto).</summary>
    public class RepositorioAvisosFichaFalso : IRepositorioAvisosFicha, ITransaccionAvisosFicha
    {
        public Dictionary<string, DatosFichaActual> Fichas { get; } = new Dictionary<string, DatosFichaActual>(StringComparer.OrdinalIgnoreCase);
        public List<AvisoFicha> Avisos { get; } = new List<AvisoFicha>();

        public Task<DatosFichaActual> LeerFicha(string empresa, string producto)
            => Task.FromResult(Fichas.TryGetValue(producto?.Trim() ?? string.Empty, out DatosFichaActual ficha) ? ficha : null);

        public Task<T> EnTransaccion<T>(Func<ITransaccionAvisosFicha, Task<T>> trabajo) => trabajo(this);

        public Task<AvisoFicha> LeerAbierto(string empresa, string producto, string destino)
            => Task.FromResult(Avisos.SingleOrDefault(a => a.Producto == producto && a.Destino == destino && a.Estado == EstadosAvisoFicha.ABIERTO));

        public Task<AvisoFicha> Insertar(AvisoFicha aviso)
        {
            aviso.Id = Avisos.Count + 1;
            aviso.Clave = Guid.NewGuid();
            Avisos.Add(aviso);
            return Task.FromResult(aviso);
        }

        public Task Actualizar(AvisoFicha aviso) => Task.CompletedTask;

        public Task<List<AvisoFicha>> LeerAbiertos() => Task.FromResult(Avisos.Where(a => a.Estado == EstadosAvisoFicha.ABIERTO).ToList());
        public Task<AvisoFicha> LeerPorId(int id) => Task.FromResult(Avisos.SingleOrDefault(a => a.Id == id));
        public Task<AvisoFicha> LeerPorClave(Guid clave) => Task.FromResult(Avisos.SingleOrDefault(a => a.Clave == clave));

        public Task<bool> Cerrar(int id, string estado, string cerradoPor)
        {
            AvisoFicha aviso = Avisos.SingleOrDefault(a => a.Id == id && a.Estado == EstadosAvisoFicha.ABIERTO);
            if (aviso == null)
            {
                return Task.FromResult(false);
            }
            aviso.Estado = estado;
            aviso.CerradoPor = cerradoPor;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// Ariadna#8: el mozo informa de un dato mal en la ficha de un producto. La foto va a Tienda online y todo lo demás
    /// (precio, nombre, familia, subgrupo, tamaño, código de barras, otro) a Compras, por correo y buzón de Nesto. Se
    /// cierra a mano («Cambiado» / «Estaba bien») o solo al detectar que el dato ha cambiado; al cerrarse, el mozo lo ve
    /// en su buzón de Ariadna.
    /// </summary>
    [TestClass]
    public class AvisosFichaProductoTests
    {
        private const string EMPRESA = "1";
        private RepositorioAvisosFichaFalso repositorio;
        private IAvisadorFichaProducto avisador;
        private IFotosProductoAlmacen fotos;
        private ServicioAvisosFicha servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioAvisosFichaFalso();
            repositorio.Fichas["22624"] = new DatosFichaActual
            {
                Producto = "22624", Nombre = "LATA CERA ORO (GOLD)", Familia = "Starpil", Subgrupo = "Productos depilación ceras",
                Tamano = 500, UnidadMedida = "ml.", CodigoBarras = "8436000000001", Precio = 8.74M
            };
            avisador = A.Fake<IAvisadorFichaProducto>();
            fotos = A.Fake<IFotosProductoAlmacen>();
            A.CallTo(() => fotos.UrlActual("22624")).Returns("https://tienda.es/101089-home_default/lata.jpg");
            servicio = new ServicioAvisosFicha(repositorio, avisador, fotos);
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
            => new GenericPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, nombre) }, "prueba"), grupos);

        private Task<ResultadoInformarDatoMal> Informar(string mozo = "Pedro", string comentario = "La foto es de la lata pequeña", params string[] campos)
            => servicio.Informar(EMPRESA, new InformarDatoMalDTO
            {
                Producto = "22624 ",
                Campos = campos.Length == 0 ? new List<string> { "Foto" } : campos.ToList(),
                Comentario = comentario,
                Dispositivo = "OPPO Reno8 Lite 5G"
            }, mozo);

        [TestMethod]
        public async Task LaFoto_VaATiendaOnlineConLaFotoQueSeVioYSeAvisa()
        {
            ResultadoInformarDatoMal resultado = await Informar();

            Assert.AreEqual(EstadoInformarDatoMal.Guardado, resultado.Estado);
            AvisoFicha aviso = repositorio.Avisos.Single();
            Assert.AreEqual(DestinosAvisoFicha.TIENDA_ONLINE, aviso.Destino);
            Assert.AreEqual("22624", aviso.Producto);
            Assert.AreEqual("https://tienda.es/101089-home_default/lata.jpg", aviso.UrlFoto);
            CollectionAssert.AreEqual(new[] { "Pedro" }, aviso.Informantes);
            A.CallTo(() => avisador.AvisarEquipo(aviso, A<DatosFichaActual>._, A<IReadOnlyCollection<string>>.That.Contains("Foto"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task FotoYOtroDato_SonDosAvisosUnoParaCadaEquipo()
        {
            await Informar("Pedro", "La foto no es y el código tampoco", "Foto", "CodigoBarras", "Precio");

            Assert.AreEqual(2, repositorio.Avisos.Count);
            CollectionAssert.AreEqual(new[] { "Foto" }, repositorio.Avisos.Single(a => a.Destino == DestinosAvisoFicha.TIENDA_ONLINE).Campos);
            CollectionAssert.AreEquivalent(new[] { "CodigoBarras", "Precio" }, repositorio.Avisos.Single(a => a.Destino == DestinosAvisoFicha.COMPRAS).Campos);
            A.CallTo(() => avisador.AvisarEquipo(A<AvisoFicha>._, A<DatosFichaActual>._, A<IReadOnlyCollection<string>>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task MismoDatoYaAvisado_SeSumaAlAbiertoSinCrearOtroNiVolverAAvisar()
        {
            await Informar("Pedro");
            await Informar("Santiago", "Sigue mal");

            AvisoFicha aviso = repositorio.Avisos.Single();
            Assert.AreEqual(2, aviso.Veces);
            CollectionAssert.AreEqual(new[] { "Pedro", "Santiago" }, aviso.Informantes);
            StringAssert.Contains(aviso.Comentarios, "Sigue mal");
            A.CallTo(() => avisador.AvisarEquipo(A<AvisoFicha>._, A<DatosFichaActual>._, A<IReadOnlyCollection<string>>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task OtroDatoDelMismoEquipo_SeSumaYSeAvisaSoloDeLoNuevo()
        {
            await Informar("Pedro", "Precio raro", "Precio");
            await Informar("Santiago", "El código no lee", "CodigoBarras");

            AvisoFicha aviso = repositorio.Avisos.Single();
            CollectionAssert.AreEquivalent(new[] { "Precio", "CodigoBarras" }, aviso.Campos);
            A.CallTo(() => avisador.AvisarEquipo(aviso, A<DatosFichaActual>._, A<IReadOnlyCollection<string>>.That.IsSameSequenceAs(new[] { "CodigoBarras" })))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task SinNadaMarcadoOProductoQueNoExiste_NoValido()
        {
            ResultadoInformarDatoMal sinCampos = await servicio.Informar(EMPRESA, new InformarDatoMalDTO { Producto = "22624", Campos = new List<string>() }, "Pedro");
            ResultadoInformarDatoMal inventado = await servicio.Informar(EMPRESA, new InformarDatoMalDTO { Producto = "22624", Campos = new List<string> { "Color" } }, "Pedro");
            ResultadoInformarDatoMal noExiste = await servicio.Informar(EMPRESA, new InformarDatoMalDTO { Producto = "99999", Campos = new List<string> { "Foto" } }, "Pedro");

            Assert.AreEqual(EstadoInformarDatoMal.NoValido, sinCampos.Estado);
            Assert.AreEqual(EstadoInformarDatoMal.NoValido, inventado.Estado);
            Assert.AreEqual(EstadoInformarDatoMal.NoValido, noExiste.Estado);
            Assert.AreEqual(0, repositorio.Avisos.Count);
        }

        [TestMethod]
        public async Task CerrarAMano_QuienLoRecibe_AvisaAlMozoYSiEsLaFotoLaOlvidaDeLaCache()
        {
            await Informar("Pedro");
            AvisoFicha aviso = repositorio.Avisos.Single();

            ResultadoCerrarAvisoFicha resultado = await servicio.Cerrar(aviso.Id, "Cambiado", Usuario("NUEVAVISION\\Laura", "TiendaOnline"));

            Assert.AreEqual(EstadoCerrarAvisoFicha.Cerrado, resultado.Estado);
            Assert.AreEqual(EstadosAvisoFicha.CAMBIADO, aviso.Estado);
            Assert.AreEqual("NUEVAVISION\\Laura", aviso.CerradoPor);
            A.CallTo(() => avisador.AvisarMozos(aviso)).MustHaveHappenedOnceExactly();
            A.CallTo(() => fotos.Olvidar("22624")).MustHaveHappened();
        }

        [TestMethod]
        public async Task CerrarAMano_EstabaBien()
        {
            await Informar("Pedro", "Precio raro", "Precio");
            AvisoFicha aviso = repositorio.Avisos.Single();

            ResultadoCerrarAvisoFicha resultado = await servicio.Cerrar(aviso.Id, "EstabaBien", Usuario("NUEVAVISION\\Manuel", "Compras"));

            Assert.AreEqual(EstadoCerrarAvisoFicha.Cerrado, resultado.Estado);
            Assert.AreEqual(EstadosAvisoFicha.ESTABA_BIEN, aviso.Estado);
            A.CallTo(() => fotos.Olvidar(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cerrar_UnMozoDeAlmacen_NoPuede()
        {
            await Informar("Pedro");

            ResultadoCerrarAvisoFicha resultado = await servicio.Cerrar(repositorio.Avisos.Single().Id, "Cambiado", Usuario("Pedro", "Almacén"));

            Assert.AreEqual(EstadoCerrarAvisoFicha.SinPermiso, resultado.Estado);
            Assert.AreEqual(EstadosAvisoFicha.ABIERTO, repositorio.Avisos.Single().Estado);
        }

        [TestMethod]
        public async Task Cerrar_ResultadoQueNoExisteOAvisoYaCerrado()
        {
            await Informar("Pedro");
            int id = repositorio.Avisos.Single().Id;

            Assert.AreEqual(EstadoCerrarAvisoFicha.NoValido, (await servicio.Cerrar(id, "Quizás", Usuario("Carlos", "Dirección"))).Estado);
            Assert.AreEqual(EstadoCerrarAvisoFicha.Cerrado, (await servicio.Cerrar(id, "Cambiado", Usuario("Carlos", "Dirección"))).Estado);
            Assert.AreEqual(EstadoCerrarAvisoFicha.YaCerrado, (await servicio.Cerrar(id, "EstabaBien", Usuario("Carlos", "Dirección"))).Estado);
            A.CallTo(() => avisador.AvisarMozos(A<AvisoFicha>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CerrarConElEnlaceDelCorreo_SinUsuario()
        {
            await Informar("Pedro", "Precio raro", "Precio");
            AvisoFicha aviso = repositorio.Avisos.Single();

            ResultadoCerrarAvisoFicha resultado = await servicio.CerrarConEnlace(aviso.Clave, "EstabaBien");
            ResultadoCerrarAvisoFicha inventado = await servicio.CerrarConEnlace(Guid.NewGuid(), "Cambiado");

            Assert.AreEqual(EstadoCerrarAvisoFicha.Cerrado, resultado.Estado);
            Assert.AreEqual(EstadosAvisoFicha.ESTABA_BIEN, aviso.Estado);
            StringAssert.Contains(aviso.CerradoPor, "correo");
            Assert.AreEqual(EstadoCerrarAvisoFicha.NoExiste, inventado.Estado);
        }

        [TestMethod]
        public async Task Revisar_SiElDatoHaCambiado_SeCierraSoloComoCambiado()
        {
            await Informar("Pedro", "Precio raro", "Precio");
            await Informar("Pedro", "Foto vieja", "Foto");
            repositorio.Fichas["22624"].Precio = 9.20M;

            int cerrados = await servicio.RevisarCambios();

            Assert.AreEqual(1, cerrados);
            Assert.AreEqual(EstadosAvisoFicha.CAMBIADO, repositorio.Avisos.Single(a => a.Destino == DestinosAvisoFicha.COMPRAS).Estado);
            Assert.AreEqual(EstadosAvisoFicha.ABIERTO, repositorio.Avisos.Single(a => a.Destino == DestinosAvisoFicha.TIENDA_ONLINE).Estado);
            A.CallTo(() => avisador.AvisarMozos(A<AvisoFicha>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Revisar_FotoNueva_SeCierraYSeOlvidaDeLaCache()
        {
            await Informar("Pedro");
            A.CallTo(() => fotos.UrlActual("22624")).Returns("https://tienda.es/120000-home_default/lata-nueva.jpg");

            int cerrados = await servicio.RevisarCambios();

            Assert.AreEqual(1, cerrados);
            A.CallTo(() => fotos.Olvidar("22624")).MustHaveHappened();
        }

        [TestMethod]
        public async Task Revisar_SoloOtro_NoSePuedeDetectarYSeQuedaAbierto()
        {
            await Informar("Pedro", "La caja trae 12, no 10", "Otro");
            repositorio.Fichas["22624"].Precio = 1M;

            Assert.AreEqual(0, await servicio.RevisarCambios());
            Assert.AreEqual(EstadosAvisoFicha.ABIERTO, repositorio.Avisos.Single().Estado);
        }

        [TestMethod]
        public async Task Revisar_LaTiendaNoContesta_NoSeCierraPorEso()
        {
            // Sin respuesta de la tienda (null) no es «ha cambiado la foto»
            await Informar("Pedro");
            A.CallTo(() => fotos.UrlActual("22624")).Returns((string)null);

            Assert.AreEqual(0, await servicio.RevisarCambios());
        }
    }

    /// <summary>Ariadna#8: a quién llega cada aviso y con qué.</summary>
    [TestClass]
    public class AvisadorFichaProductoTests
    {
        private NestoAPI.Infraestructure.IServicioCorreoElectronico correo;
        private NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush notificaciones;
        private AvisadorFichaProducto avisador;
        private System.Net.Mail.MailMessage enviado;

        [TestInitialize]
        public void Preparar()
        {
            correo = A.Fake<NestoAPI.Infraestructure.IServicioCorreoElectronico>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<System.Net.Mail.MailMessage>._)).Invokes((System.Net.Mail.MailMessage m) => enviado = m).Returns(true);
            notificaciones = A.Fake<NestoAPI.Infraestructure.Notificaciones.IServicioNotificacionesPush>();
            avisador = new AvisadorFichaProducto(correo, notificaciones,
                grupo => grupo == "TiendaOnline" ? new List<string> { "Laura" } : new List<string> { @"NUEVAVISION\Manuel" },
                _ => Task.FromResult("https://tienda.es/lata-cera-oro"));
        }

        private static AvisoFicha Aviso(string destino, params string[] campos) => new AvisoFicha
        {
            Id = 7, Clave = new Guid("6f9c1d2e-0000-4000-8000-000000000001"), Empresa = "1", Producto = "22624", Destino = destino,
            Campos = campos.ToList(), Informantes = new List<string> { "Pedro", "Santiago" }, Comentarios = "Pedro: la foto es de la lata pequeña",
            UrlFoto = "https://tienda.es/101089-home_default/lata.jpg"
        };

        [TestMethod]
        public async Task LaFoto_CorreoATiendaOnlineConLosBotonesYBuzonDeNestoDeSuGrupo()
        {
            AvisoFicha aviso = Aviso(DestinosAvisoFicha.TIENDA_ONLINE, "Foto");

            await avisador.AvisarEquipo(aviso, new DatosFichaActual { Producto = "22624", Nombre = "LATA CERA ORO (GOLD)" }, new[] { "Foto" });

            Assert.AreEqual("tiendaonline@nuevavision.es", enviado.To.Single().Address);
            StringAssert.Contains(enviado.Subject, "22624");
            StringAssert.Contains(enviado.Body, "Enlace/6f9c1d2e-0000-4000-8000-000000000001?resultado=Cambiado");
            StringAssert.Contains(enviado.Body, "Enlace/6f9c1d2e-0000-4000-8000-000000000001?resultado=EstabaBien");
            StringAssert.Contains(enviado.Body, "lata.jpg");
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Laura", "Nesto", A<NestoAPI.Models.NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ElPrecio_VaACompras()
        {
            await avisador.AvisarEquipo(Aviso(DestinosAvisoFicha.COMPRAS, "Precio"), new DatosFichaActual { Producto = "22624", Precio = 8.74M }, new[] { "Precio" });

            Assert.AreEqual("compras@nuevavision.es", enviado.To.Single().Address);
            StringAssert.Contains(enviado.Body, "8.74");
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario(@"NUEVAVISION\Manuel", "Nesto", A<NestoAPI.Models.NotificacionPushDTO>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task AlCerrarse_LaRespuestaLlegaAlBuzonDeAriadnaDeCadaMozoQueAviso()
        {
            AvisoFicha aviso = Aviso(DestinosAvisoFicha.TIENDA_ONLINE, "Foto");
            aviso.Estado = EstadosAvisoFicha.ESTABA_BIEN;

            await avisador.AvisarMozos(aviso);

            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("Pedro", "Ariadna",
                A<NestoAPI.Models.NotificacionPushDTO>.That.Matches(n => n.Cuerpo.Contains("estaba bien")))).MustHaveHappenedOnceExactly();
            A.CallTo(() => notificaciones.GuardarEnBuzonDeUsuario("Santiago", "Ariadna", A<NestoAPI.Models.NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }
    }
}
