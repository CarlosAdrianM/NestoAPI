using FakeItEasy;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#646 (Carlos 20/10/26): sin sondeo. Cuando una PDA lee, da una falta, corrige, cierra un bulto o termina, la API
    /// avisa por SignalR al grupo del documento («ha cambiado», sin datos), a todos menos al usuario que lo ha hecho. El aviso
    /// nunca rompe la petición.
    /// </summary>
    [TestClass]
    public class AvisosAlmacenTests
    {
        private IServicioPreparacionAlmacen servicio;
        private IAvisosAlmacen avisos;
        private AlmacenController controller;

        [TestInitialize]
        public void Preparar()
        {
            ConexionesAlmacen.Vaciar();
            servicio = A.Fake<IServicioPreparacionAlmacen>();
            avisos = A.Fake<IAvisosAlmacen>();
            controller = new AlmacenController(servicio, A.Fake<IServicioUbicacionesAlmacen>(), A.Fake<IServicioRecepcionCompras>(),
                A.Fake<IServicioRecepcionReposiciones>(), avisosAlmacen: avisos)
            {
                User = new GenericPrincipal(new GenericIdentity("Andrey", "Bearer"), new string[0]),
                Request = new HttpRequestMessage()
            };
        }

        [TestCleanup]
        public void Limpiar() => ConexionesAlmacen.Vaciar();

        private static EscaneoAlmacenDTO Escaneo(string fase, string tipo = "PICK", int numero = 99633)
            => new EscaneoAlmacenDTO { IdCliente = Guid.NewGuid(), TipoOrigen = tipo, NumeroOrigen = numero, Producto = "A", Fase = fase, Cantidad = 1, Metodo = "SCAN" };

        [TestMethod]
        public async Task PostEscaneos_AvisaAlGrupoDeCadaDocumentoConSusLecturasYElUsuarioQueLoHaHecho()
        {
            var lote = new List<EscaneoAlmacenDTO> { Escaneo("PICK"), Escaneo("PICK"), Escaneo("PICK", "REPO", 80905), Escaneo("PACK", numero: 99640) };
            A.CallTo(() => servicio.GuardarEscaneos("1", lote, "Andrey")).Returns(new ResultadoEscaneosAlmacenDTO { Guardados = 4 });

            _ = await controller.PostEscaneos(lote);

            // NestoAPI#710: con los datos; el «haCambiado» de compatibilidad lo añade AvisosAlmacenSignalR según el Web.config
            A.CallTo(() => avisos.Lecturas("Recogida:PICK:99633", "Andrey", A<string>._, A<string>._,
                A<IReadOnlyList<LecturaAlmacenAvisoDTO>>.That.Matches(l => l.Count == 2))).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.Lecturas("Recogida:REPO:80905", "Andrey", A<string>._, A<string>._,
                A<IReadOnlyList<LecturaAlmacenAvisoDTO>>.That.Matches(l => l.Count == 1))).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.Lecturas("Packing:99640", "Andrey", A<string>._, A<string>._,
                A<IReadOnlyList<LecturaAlmacenAvisoDTO>>.That.Matches(l => l.Count == 1))).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.HaCambiado(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostEscaneos_LaLecturaLlevaLoQueSeHaGrabado()
        {
            // NestoAPI#710: lo mismo que se acaba de grabar en PreparacionEscaneos (y el usuario con el nombre que se guarda)
            var escaneo = new EscaneoAlmacenDTO
            {
                IdCliente = Guid.NewGuid(), Picking = 99941, Producto = " 45958 ", Fase = "pick", Cantidad = 3, Metodo = "tecla",
                Dispositivo = "Redmi 7A", FechaEscaneo = new DateTime(2026, 10, 27, 8, 46, 50)
            };
            var lote = new List<EscaneoAlmacenDTO> { escaneo };
            A.CallTo(() => servicio.GuardarEscaneos("1", lote, "Andrey")).Returns(new ResultadoEscaneosAlmacenDTO { Guardados = 1 });
            IReadOnlyList<LecturaAlmacenAvisoDTO> mandadas = null;
            A.CallTo(() => avisos.Lecturas(A<string>._, A<string>._, A<string>._, A<string>._, A<IReadOnlyList<LecturaAlmacenAvisoDTO>>._))
                .Invokes((string g, string u, string c, string d, IReadOnlyList<LecturaAlmacenAvisoDTO> l) => mandadas = l);

            _ = await controller.PostEscaneos(lote);

            A.CallTo(() => avisos.Lecturas("Recogida:PICK:99941", "Andrey", null, "Redmi 7A", A<IReadOnlyList<LecturaAlmacenAvisoDTO>>._))
                .MustHaveHappenedOnceExactly();
            LecturaAlmacenAvisoDTO lectura = mandadas.Single();
            Assert.AreEqual(escaneo.IdCliente, lectura.IdCliente);
            Assert.AreEqual("PICK", lectura.TipoOrigen);
            Assert.AreEqual(99941, lectura.NumeroOrigen);
            Assert.AreEqual("45958", lectura.Producto);
            Assert.AreEqual(3, lectura.Cantidad);
            Assert.AreEqual("TECLA", lectura.Metodo);
            Assert.AreEqual("PICK", lectura.Fase);
            Assert.AreEqual("Andrey", lectura.Usuario);
            Assert.AreEqual(escaneo.FechaEscaneo, lectura.FechaEscaneo);
        }

        [TestMethod]
        public async Task PostEscaneos_UnaFalta_ViajaConSuHueco()
        {
            var falta = Escaneo("PICK");
            falta.Metodo = "FALTA";
            falta.Cantidad = 2;
            falta.Motivo = "001/007/014";
            var lote = new List<EscaneoAlmacenDTO> { falta };
            A.CallTo(() => servicio.GuardarEscaneos("1", lote, "Andrey")).Returns(new ResultadoEscaneosAlmacenDTO { Guardados = 1 });

            _ = await controller.PostEscaneos(lote);

            A.CallTo(() => avisos.Lecturas("Recogida:PICK:99633", "Andrey", A<string>._, A<string>._,
                A<IReadOnlyList<LecturaAlmacenAvisoDTO>>.That.Matches(l => l.Single().Metodo == "FALTA" && l.Single().Cantidad == 2
                    && l.Single().Motivo == "001/007/014"))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostEscaneos_LoRechazadoNoSeManda_YUnReenvioDelMismoIdClienteSoloUnaVez()
        {
            var bueno = Escaneo("PICK");
            var malo = Escaneo("PICK");
            var repetido = new EscaneoAlmacenDTO { IdCliente = bueno.IdCliente, TipoOrigen = "PICK", NumeroOrigen = 99633, Producto = "A", Fase = "PICK", Cantidad = 1, Metodo = "SCAN" };
            var lote = new List<EscaneoAlmacenDTO> { bueno, malo, repetido };
            A.CallTo(() => servicio.GuardarEscaneos("1", lote, "Andrey")).Returns(new ResultadoEscaneosAlmacenDTO
            {
                Guardados = 1,
                Repetidos = 1,
                Rechazados = new List<EscaneoRechazadoDTO> { new EscaneoRechazadoDTO { IdCliente = malo.IdCliente, Motivo = "no vale" } }
            });

            _ = await controller.PostEscaneos(lote);

            A.CallTo(() => avisos.Lecturas("Recogida:PICK:99633", "Andrey", A<string>._, A<string>._,
                A<IReadOnlyList<LecturaAlmacenAvisoDTO>>.That.Matches(l => l.Count == 1 && l[0].IdCliente == bueno.IdCliente))).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostEscaneos_SinNadaGuardado_NoAvisa()
        {
            var lote = new List<EscaneoAlmacenDTO> { Escaneo("PICK") };
            A.CallTo(() => servicio.GuardarEscaneos("1", lote, "Andrey")).Returns(new ResultadoEscaneosAlmacenDTO { Repetidos = 1 });

            _ = await controller.PostEscaneos(lote);

            A.CallTo(avisos).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostEscaneos_SiFallaElAviso_LaPeticionVaBien()
        {
            var lote = new List<EscaneoAlmacenDTO> { Escaneo("PICK") };
            var guardado = new ResultadoEscaneosAlmacenDTO { Guardados = 1 };
            A.CallTo(() => servicio.GuardarEscaneos("1", lote, "Andrey")).Returns(guardado);
            A.CallTo(() => avisos.Lecturas(A<string>._, A<string>._, A<string>._, A<string>._, A<IReadOnlyList<LecturaAlmacenAvisoDTO>>._))
                .Throws(new InvalidOperationException("hub caído"));

            IHttpActionResult resultado = await controller.PostEscaneos(lote);

            Assert.AreSame(guardado, ((OkNegotiatedContentResult<ResultadoEscaneosAlmacenDTO>)resultado).Content);
        }

        [TestMethod]
        public async Task PostCorregirEscaneo_SoloAvisaSiSeHaCorregido_ConLoNecesarioParaDeshacerlaEnLocal()
        {
            var correccion = new CorregirEscaneoDTO
            {
                IdCliente = Guid.NewGuid(), TipoOrigen = "repo", NumeroOrigen = 80905, Producto = "A ", Hueco = "001/007/002", Orden = 3,
                Tipo = "falta", Unidades = 1, Dispositivo = "PDA-1"
            };
            A.CallTo(() => servicio.CorregirLectura("1", correccion, A<IPrincipal>._))
                .Returns(new ResultadoCorreccionLectura { Estado = EstadoCorreccionLectura.HaCambiado, Respuesta = new ResultadoCorregirEscaneoDTO() }).Once()
                .Then.Returns(new ResultadoCorreccionLectura { Estado = EstadoCorreccionLectura.Corregida, Respuesta = new ResultadoCorregirEscaneoDTO { Cogidas = 2, Faltas = 0 } });

            _ = await controller.PostCorregirEscaneo(correccion);
            A.CallTo(avisos).MustNotHaveHappened();
            _ = await controller.PostCorregirEscaneo(correccion);

            A.CallTo(() => avisos.Correccion("Recogida:REPO:80905", "Andrey", null, "PDA-1", A<CorreccionAlmacenAvisoDTO>.That.Matches(c =>
                c.IdCliente == correccion.IdCliente && c.TipoOrigen == "REPO" && c.NumeroOrigen == 80905 && c.Producto == "A" && c.Orden == 3
                && c.Hueco == "001/007/002" && c.Tipo == "FALTA" && c.Unidades == 1 && c.Cogidas == 2 && c.Faltas == 0 && c.Usuario == "Andrey")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => avisos.HaCambiado(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCorregirEscaneo_SiFallaElAviso_LaPeticionVaBien()
        {
            var correccion = new CorregirEscaneoDTO { TipoOrigen = "PICK", NumeroOrigen = 99633 };
            var respuesta = new ResultadoCorregirEscaneoDTO { Corregida = true };
            A.CallTo(() => servicio.CorregirLectura("1", correccion, A<IPrincipal>._))
                .Returns(new ResultadoCorreccionLectura { Estado = EstadoCorreccionLectura.Corregida, Respuesta = respuesta });
            A.CallTo(() => avisos.Correccion(A<string>._, A<string>._, A<string>._, A<string>._, A<CorreccionAlmacenAvisoDTO>._))
                .Throws(new InvalidOperationException("hub caído"));

            IHttpActionResult resultado = await controller.PostCorregirEscaneo(correccion);

            Assert.AreSame(respuesta, ((OkNegotiatedContentResult<ResultadoCorregirEscaneoDTO>)resultado).Content);
        }

        [TestMethod]
        public async Task PostAnularLecturas_ConAlgoAnulado_LasDemasPdaReleen()
        {
            A.CallTo(() => servicio.AnularLecturasRecogida("1", "PICK", 99633, "Pedro", A<IPrincipal>._))
                .Returns(new ResultadoAnularLecturas { Estado = EstadoAnularLecturas.Anuladas, Filas = 0 }).Once()
                .Then.Returns(new ResultadoAnularLecturas { Estado = EstadoAnularLecturas.Anuladas, Filas = 4 });

            _ = await controller.PostAnularLecturas("PICK", 99633, new AnularLecturasDTO { Usuario = "Pedro" });
            A.CallTo(avisos).MustNotHaveHappened();
            _ = await controller.PostAnularLecturas("PICK", 99633, new AnularLecturasDTO { Usuario = "Pedro" });

            A.CallTo(() => avisos.HaCambiado("Recogida:PICK:99633", "Andrey", A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostFotoBulto_AvisaAlPackingDelPicking()
        {
            controller.Request = new HttpRequestMessage { Content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF }) };
            A.CallTo(() => servicio.GuardarFotoBulto(A<FotoBultoAlmacen>._, "Andrey")).Returns(new BultoAlmacenDTO { Bulto = 1 });

            _ = await controller.PostFotoBulto(Guid.NewGuid(), 926940, 99633, 1);

            A.CallTo(() => avisos.HaCambiado("Packing:99633", "Andrey", A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PostTerminar_AvisaSiSeTermina_YNoEnUnEnsayo()
        {
            A.CallTo(() => servicio.TerminarRecogida("1", "PICK", 99633, A<IPrincipal>._, A<bool>._))
                .Returns(new ResultadoTerminarSalida { Estado = EstadoTerminarSalida.Terminada, Salida = new ResultadoTerminarSalidaDTO() });

            _ = await controller.PostTerminarRecogida("PICK", 99633, ensayo: true);
            A.CallTo(avisos).MustNotHaveHappened();
            _ = await controller.PostTerminarRecogida("PICK", 99633);

            A.CallTo(() => avisos.HaCambiado("Recogida:PICK:99633", "Andrey", A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void Normalizar_SoloLosGruposQueHay()
        {
            Assert.AreEqual("Recogida:PICK:99633", AvisosAlmacen.Normalizar(" recogida:pick:99633 "));
            Assert.AreEqual("Packing:99633", AvisosAlmacen.Normalizar("packing:99633"));
            Assert.IsNull(AvisosAlmacen.Normalizar("usuario:PEDRO"), "Un mozo no puede meterse en el grupo de otro usuario de Nesto");
            Assert.IsNull(AvisosAlmacen.Normalizar("Recogida:OTRO:1"));
            Assert.IsNull(AvisosAlmacen.Normalizar(null));
        }

        /// <summary>El contexto del hub con un grupo que apunta lo que recibe y a quién se ha excluido.</summary>
        private static (IHubContext Contexto, List<string> Recibidos, List<string[]> Excluidas) HubFalso()
        {
            var recibidos = new List<string>();
            var excluidas = new List<string[]>();
            dynamic grupo = new ExpandoObject();
            grupo.haCambiado = new Action<string, string>((g, d) => recibidos.Add(g + "|" + d));
            grupo.lectura = new Action<string, string, LecturaAlmacenAvisoDTO>((g, d, l) => recibidos.Add($"lectura|{g}|{d}|{l.Producto}|{l.SigueHaCambiado}"));
            grupo.lecturas = new Action<string, string, List<LecturaAlmacenAvisoDTO>>((g, d, l) => recibidos.Add($"lecturas|{g}|{d}|{l.Count}|{l.TrueForAll(x => x.SigueHaCambiado)}"));
            grupo.correccion = new Action<string, string, CorreccionAlmacenAvisoDTO>((g, d, c) => recibidos.Add($"correccion|{g}|{d}|{c.Producto}|{c.SigueHaCambiado}"));
            IHubContext contexto = A.Fake<IHubContext>();
            IHubConnectionContext<dynamic> clientes = A.Fake<IHubConnectionContext<dynamic>>();
            A.CallTo(() => contexto.Clients).Returns(clientes);
            A.CallTo(() => clientes.Group(A<string>._, A<string[]>._))
                .ReturnsLazily((string nombre, string[] fuera) => { excluidas.Add(fuera); return (object)grupo; });
            return (contexto, recibidos, excluidas);
        }

        [TestMethod]
        public void HaCambiado_ConLaCabeceraDeSuConexion_SeExcluyeEsaConexionYElAvisoLlevaSuDispositivo()
        {
            ConexionesAlmacen.Entrar("c1", "Andrey", "PDA-1", "Recogida:PICK:99633");
            ConexionesAlmacen.Entrar("c2", "Andrey", "PDA-2", "Recogida:PICK:99633"); // el mismo mozo con otra PDA: sí se le avisa
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Recogida:PICK:99633");
            ConexionesAlmacen.Entrar("c4", "Luis", "PDA-4", "Recogida:PICK:1");
            var (contexto, recibidos, excluidas) = HubFalso();

            new AvisosAlmacenSignalR(() => contexto).HaCambiado("Recogida:PICK:99633", "andrey", "c1", "PDA-1");

            CollectionAssert.AreEqual(new[] { "Recogida:PICK:99633|PDA-1" }, recibidos);
            CollectionAssert.AreEqual(new[] { "c1" }, excluidas[0]);
        }

        private static LecturaAlmacenAvisoDTO Lectura(string producto) => new LecturaAlmacenAvisoDTO { IdCliente = Guid.NewGuid(), Producto = producto, Cantidad = 1 };

        [TestMethod]
        public void Lecturas_Ambos_LaLecturaYDetrasElHaCambiadoDeSiempre_SinLaPdaQueLaHaHecho()
        {
            // NestoAPI#710: por defecto (Almacen:AvisosConDatos = Ambos), para que las Ariadna viejas sigan enterándose
            ConexionesAlmacen.Entrar("c1", "Andrey", "PDA-1", "Recogida:PICK:99633");
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Recogida:PICK:99633");
            var (contexto, recibidos, excluidas) = HubFalso();

            new AvisosAlmacenSignalR(() => contexto).Lecturas("Recogida:PICK:99633", "Andrey", "c1", "PDA-1", new List<LecturaAlmacenAvisoDTO> { Lectura("A") });

            CollectionAssert.AreEqual(new[] { "lectura|Recogida:PICK:99633|PDA-1|A|True", "Recogida:PICK:99633|PDA-1" }, recibidos);
            CollectionAssert.AreEqual(new[] { "c1" }, excluidas[0]);
        }

        [TestMethod]
        public void Lecturas_UnLote_VaEnUnSoloEventoLecturas()
        {
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Packing:99633");
            var (contexto, recibidos, _) = HubFalso();

            new AvisosAlmacenSignalR(() => contexto, () => ModoAvisosConDatos.SoloDatos)
                .Lecturas("Packing:99633", "Andrey", null, "PDA-1", new List<LecturaAlmacenAvisoDTO> { Lectura("A"), Lectura("B") });

            CollectionAssert.AreEqual(new[] { "lecturas|Packing:99633|PDA-1|2|False" }, recibidos, "SoloDatos: sin el haCambiado (y sin decir que viene)");
        }

        [TestMethod]
        public void Lecturas_SoloHaCambiado_ComoAntes()
        {
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Recogida:PICK:99633");
            var (contexto, recibidos, _) = HubFalso();

            new AvisosAlmacenSignalR(() => contexto, () => ModoAvisosConDatos.SoloHaCambiado)
                .Lecturas("Recogida:PICK:99633", "Andrey", null, "PDA-1", new List<LecturaAlmacenAvisoDTO> { Lectura("A") });

            CollectionAssert.AreEqual(new[] { "Recogida:PICK:99633|PDA-1" }, recibidos);
        }

        [TestMethod]
        public void Correccion_Ambos_LaCorreccionYElHaCambiado()
        {
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Recogida:REPO:80905");
            var (contexto, recibidos, _) = HubFalso();

            new AvisosAlmacenSignalR(() => contexto).Correccion("Recogida:REPO:80905", "Andrey", null, "PDA-1",
                new CorreccionAlmacenAvisoDTO { Producto = "A", Tipo = "FALTA", Unidades = 1 });

            CollectionAssert.AreEqual(new[] { "correccion|Recogida:REPO:80905|PDA-1|A|True", "Recogida:REPO:80905|PDA-1" }, recibidos);
        }

        [TestMethod]
        public void Lecturas_SolaOConElHubCaido_NoLanzaNiManda()
        {
            ConexionesAlmacen.Entrar("c1", "Andrey", "PDA-1", "Recogida:PICK:99633");
            bool usado = false;
            new AvisosAlmacenSignalR(() => { usado = true; throw new InvalidOperationException(); })
                .Lecturas("Recogida:PICK:99633", "Andrey", "c1", "PDA-1", new List<LecturaAlmacenAvisoDTO> { Lectura("A") });
            Assert.IsFalse(usado, "Sola: nada que mandar");

            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Recogida:PICK:99633");
            new AvisosAlmacenSignalR(() => throw new InvalidOperationException("sin hub"))
                .Lecturas("Recogida:PICK:99633", "Andrey", "c1", "PDA-1", new List<LecturaAlmacenAvisoDTO> { Lectura("A") });
            new AvisosAlmacenSignalR(() => throw new InvalidOperationException("sin hub"))
                .Correccion("Recogida:PICK:99633", "Andrey", "c1", "PDA-1", new CorreccionAlmacenAvisoDTO());
        }

        [TestMethod]
        public void LeerModo_PorDefectoAmbos()
        {
            Assert.AreEqual(ModoAvisosConDatos.Ambos, AvisosAlmacen.LeerModo(null));
            Assert.AreEqual(ModoAvisosConDatos.Ambos, AvisosAlmacen.LeerModo("lo que sea"));
            Assert.AreEqual(ModoAvisosConDatos.SoloDatos, AvisosAlmacen.LeerModo(" solodatos "));
            Assert.AreEqual(ModoAvisosConDatos.SoloHaCambiado, AvisosAlmacen.LeerModo("SoloHaCambiado"));
            Assert.AreEqual(ModoAvisosConDatos.Ambos, AvisosAlmacen.LeerModo("7"), "Un número no es un modo");
        }

        [TestMethod]
        public void HaCambiado_SinLaCabecera_SeExcluyenLasConexionesDeSuDispositivo()
        {
            ConexionesAlmacen.Entrar("c1", "Andrey", "PDA-1", "Packing:99633");
            ConexionesAlmacen.Entrar("c9", "Andrey", "PDA-1", "Packing:99633"); // la misma PDA, reconectada
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Packing:99633");
            var (contexto, _, excluidas) = HubFalso();

            new AvisosAlmacenSignalR(() => contexto).HaCambiado("Packing:99633", "Andrey", null, "pda-1");

            CollectionAssert.AreEquivalent(new[] { "c1", "c9" }, excluidas[0]);
        }

        [TestMethod]
        public void HaCambiado_SinCabeceraNiDispositivo_SeExcluyeAlMismoUsuario()
        {
            ConexionesAlmacen.Entrar("c1", "Andrey", null, "Packing:99633");
            ConexionesAlmacen.Entrar("c3", "Pedro", null, "Packing:99633");
            var (contexto, _, excluidas) = HubFalso();

            new AvisosAlmacenSignalR(() => contexto).HaCambiado("Packing:99633", "Andrey", null, null);

            CollectionAssert.AreEqual(new[] { "c1" }, excluidas[0]);
        }

        [TestMethod]
        public void HaCambiado_SolaOSinNadieMirando_NoMandaNada()
        {
            ConexionesAlmacen.Entrar("c1", "Andrey", "PDA-1", "Recogida:PICK:99633");
            bool usado = false;

            new AvisosAlmacenSignalR(() => { usado = true; throw new InvalidOperationException(); }).HaCambiado("Recogida:PICK:99633", "Andrey", "c1", "PDA-1");
            new AvisosAlmacenSignalR(() => { usado = true; throw new InvalidOperationException(); }).HaCambiado("Recogida:PICK:99633", "Andrey", null, "PDA-1");
            new AvisosAlmacenSignalR(() => { usado = true; throw new InvalidOperationException(); }).HaCambiado("Packing:5", "Andrey", null, null);

            Assert.IsFalse(usado, "Si la PDA está sola, no se manda nada");
        }

        [TestMethod]
        public void HaCambiado_SiElHubFalla_NoLanza()
        {
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Packing:99633");

            new AvisosAlmacenSignalR(() => throw new InvalidOperationException("sin hub")).HaCambiado("Packing:99633", "Andrey", null, "PDA-1");
        }

        [TestMethod]
        public void ConexionQueSale_YaNoRecibe()
        {
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Packing:99633");
            ConexionesAlmacen.Salir("c3", "Packing:99633");

            Assert.IsFalse(ConexionesAlmacen.HayOtros("Packing:99633", new string[0]));
            ConexionesAlmacen.Entrar("c3", "Pedro", "PDA-3", "Packing:99633");
            ConexionesAlmacen.Quitar("c3");
            Assert.IsFalse(ConexionesAlmacen.HayOtros("Packing:99633", new string[0]));
        }

        [TestMethod]
        public async Task Controlador_PasaLaConexionYElDispositivoDeLaPeticion()
        {
            controller.Request = new HttpRequestMessage { Content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF }) };
            controller.Request.Headers.Add(AvisosAlmacen.CABECERA_CONEXION, "c1");
            A.CallTo(() => servicio.GuardarFotoBulto(A<FotoBultoAlmacen>._, "Andrey")).Returns(new BultoAlmacenDTO { Bulto = 1 });

            _ = await controller.PostFotoBulto(Guid.NewGuid(), 926940, 99633, 1, dispositivo: "PDA-1");

            A.CallTo(() => avisos.HaCambiado("Packing:99633", "Andrey", "c1", "PDA-1")).MustHaveHappenedOnceExactly();
        }
    }
}
