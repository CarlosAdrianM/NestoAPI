using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>Sugerencia 1277 (Alfredo): envíos internos entre sedes (EI{id}-{n}), en oficina y en el reparto.</summary>
    [TestClass]
    public class EnviosInternosRepartoTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 20, 9, 0, 0);
        private const string ANDRE = @"NUEVAVISION\Andre";
        private const string MIGUEL = @"NUEVAVISION\Miguel";

        internal class EnviosInternosFalsos : IRepositorioEnviosInternos
        {
            public bool Script = true;
            public readonly List<EnvioInternoReparto> Filas = new List<EnvioInternoReparto>();
            public bool ScriptAplicado() => Script;
            public Task<int> Crear(EnvioInternoReparto e) { e.Id = Filas.Count + 12; Filas.Add(e); return Task.FromResult(e.Id); }
            public Task<EnvioInternoReparto> Leer(int id) => Task.FromResult(Filas.FirstOrDefault(f => f.Id == id));
            public Task<List<EnvioInternoReparto>> LeerLista(DateTime desde, bool soloVivos)
                => Task.FromResult(Filas.Where(f => soloVivos ? f.Estado == EstadosEnvioInterno.PENDIENTE : f.FechaAlta >= desde).ToList());
            public Task<List<EnvioInternoReparto>> LeerVivos(DateTime desde)
                => Task.FromResult(Filas.Where(f => f.Estado == EstadosEnvioInterno.PENDIENTE && f.FechaAlta >= desde).ToList());
            public Task<bool> Entregar(int id, Guid clave, DateTime fechaHora, string usuario, string receptor, string detalle)
            {
                EnvioInternoReparto e = Filas.FirstOrDefault(f => f.Id == id && f.Estado == EstadosEnvioInterno.PENDIENTE);
                if (e == null) return Task.FromResult(false);
                e.Estado = EstadosEnvioInterno.ENTREGADO; e.ClaveEntrega = clave; e.NombreReceptor = receptor; e.UsuarioRecepcion = usuario;
                e.FechaRecepcion = fechaHora; e.DetalleEntrega = detalle;
                return Task.FromResult(true);
            }
            public Task<bool> Anular(int id, string motivo, string usuario, string anterior)
            {
                EnvioInternoReparto e = Filas.FirstOrDefault(f => f.Id == id && f.Estado == EstadosEnvioInterno.PENDIENTE);
                if (e == null) return Task.FromResult(false);
                e.Estado = EstadosEnvioInterno.ANULADO;
                return Task.FromResult(true);
            }
        }

        private EnviosInternosFalsos internos;
        private FurgonetaBultosTests.FurgonetaFalsa furgoneta;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Setup()
        {
            internos = new EnviosInternosFalsos();
            furgoneta = new FurgonetaBultosTests.FurgonetaFalsa();
            servicio = new ServicioRepartoPropio(new ServicioRepartoPropioTests.RepositorioFalso(), () => AHORA, null, new ServicioRepartoPropioTests.ParametrosFalsos(),
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), null,
                new DependenciasParadasReparto { Furgoneta = furgoneta, EnviosInternos = internos })
            {
                CargaObligatoriaEncendida = () => false
            };
        }

        private EnvioInternoReparto Envio(string origen, string destino, int bultos = 2)
        {
            var e = new EnvioInternoReparto { Origen = origen, Destino = destino, Descripcion = "Máquina de demostración", Bultos = bultos,
                Estado = EstadosEnvioInterno.PENDIENTE, Usuario = "Alfredo", FechaAlta = AHORA.AddHours(-1) };
            internos.Crear(e).Wait();
            return e;
        }

        private static MovimientoBultoRepartoDTO Leer(string codigo) => new MovimientoBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = Guid.NewGuid() };
        private static CargaBultoRepartoDTO Cargar(string codigo) => new CargaBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = Guid.NewGuid() };

        // ================================================================ LAS REGLAS

        [TestMethod]
        public void ValidarAlta_SedesDistintasBultosYDescripcion()
        {
            Assert.IsNull(ReglasEnviosInternos.ValidarAlta("alc", "REI", "Café", 1));
            StringAssert.Contains(ReglasEnviosInternos.ValidarAlta("ALC", "ALC", "Café", 1), "misma sede");
            StringAssert.Contains(ReglasEnviosInternos.ValidarAlta("ALC", "MAD", "Café", 1), "sede");
            StringAssert.Contains(ReglasEnviosInternos.ValidarAlta("ALC", "REI", " ", 1), "descripción");
            StringAssert.Contains(ReglasEnviosInternos.ValidarAlta("ALC", "REI", "x", 51), "entre 1 y 50");
            StringAssert.Contains(ReglasEnviosInternos.ValidarAlta("ALC", "REI", new string('x', 201), 1), "200");
        }

        [TestMethod]
        public void Codigos_YParadas_NoChocanConEnviosNiReposiciones()
        {
            Assert.AreEqual("EI12-3", ReglasEnviosInternos.Codigo(12, 3));
            Assert.IsTrue(ReglasEnviosInternos.TryLeer(" ei12'3 ", out int id, out int bulto));
            Assert.AreEqual(12, id);
            Assert.AreEqual(3, bulto);
            Assert.IsFalse(ReglasEnviosInternos.TryLeer("R81010-1", out _, out _));
            Assert.AreEqual("EI", CodigoBultoFurgoneta.Leer("EI12-1").TipoCodigo);
            int entrega = ReglasEnviosInternos.EnvioIdEntrega(12);
            int recogida = ReglasEnviosInternos.EnvioIdRecogida(12);
            Assert.AreNotEqual(entrega, recogida);
            Assert.IsTrue(entrega < 0 && recogida < 0);
            Assert.IsTrue(ReglasEnviosInternos.EsParadaEnvioInterno(entrega) && ReglasEnviosInternos.EsParadaEnvioInterno(recogida));
            Assert.IsFalse(ReglasEnviosInternos.EsParadaEnvioInterno(-12), "Una parada de reposición");
            Assert.IsTrue(ReglasEnviosInternos.EsParadaRecogida(recogida));
            Assert.IsFalse(ReglasEnviosInternos.EsParadaRecogida(entrega));
            Assert.AreEqual(12, ReglasEnviosInternos.IdDeParada(entrega));
            Assert.AreEqual(12, ReglasEnviosInternos.IdDeParada(recogida));
        }

        [TestMethod]
        public void Paradas_DesdeAlgeteSoloEntrega_DesdeTiendaRecogidaYEntrega()
        {
            var desdeAlgete = new EnvioInternoReparto { Id = 1, Origen = "ALG", Destino = "REI", Bultos = 2, Estado = EstadosEnvioInterno.PENDIENTE };
            var desdeTienda = new EnvioInternoReparto { Id = 2, Origen = "ALC", Destino = "REI", Bultos = 2, Estado = EstadosEnvioInterno.PENDIENTE };

            List<ParadaEnvioInterno> a = ReglasEnviosInternos.Paradas(desdeAlgete, null);
            Assert.AreEqual(1, a.Count);
            Assert.IsFalse(a[0].EsRecogida);
            Assert.AreEqual("REI", a[0].Sede);

            List<ParadaEnvioInterno> t = ReglasEnviosInternos.Paradas(desdeTienda, null);
            Assert.AreEqual(2, t.Count);
            Assert.AreEqual("ALC", t.Single(p => p.EsRecogida).Sede);
            Assert.IsNull(t.Single(p => p.EsRecogida).Repartidor, "De nadie hasta que alguien recoge allí");

            // Recogidos los dos: ya no hay parada de recogida; la de entrega es de quien los lleva
            var historia = new[]
            {
                new MovimientoBultoReparto { Id = 1, Codigo = "EI2-1", TipoCodigo = "EI", Tipo = TiposMovimientoBulto.RECOGIDO, Usuario = ANDRE, FechaHora = AHORA },
                new MovimientoBultoReparto { Id = 2, Codigo = "EI2-2", TipoCodigo = "EI", Tipo = TiposMovimientoBulto.RECOGIDO, Usuario = ANDRE, FechaHora = AHORA }
            };
            List<ParadaEnvioInterno> despues = ReglasEnviosInternos.Paradas(desdeTienda, historia);
            Assert.AreEqual(1, despues.Count);
            Assert.AreEqual(ANDRE, despues[0].Repartidor);
            Assert.AreEqual(2, despues[0].BultosCargados);
            Assert.AreEqual(EstadosEnvioInterno.EN_CAMINO, ReglasEnviosInternos.Estado(desdeTienda, historia));
            Assert.AreEqual(EstadosEnvioInterno.PENDIENTE, ReglasEnviosInternos.Estado(desdeAlgete, historia));
            Assert.AreEqual(0, ReglasEnviosInternos.Paradas(new EnvioInternoReparto { Id = 3, Origen = "ALC", Destino = "REI", Bultos = 1, Estado = EstadosEnvioInterno.ANULADO }, null).Count);
        }

        [TestMethod]
        public void DondeEsta_CadaBulto()
        {
            var e = new EnvioInternoReparto { Id = 2, Origen = "ALC", Destino = "REI", Bultos = 3, Estado = EstadosEnvioInterno.PENDIENTE };
            var historia = new[]
            {
                new MovimientoBultoReparto { Id = 1, Codigo = "EI2-1", TipoCodigo = "EI", Tipo = TiposMovimientoBulto.RECOGIDO, Usuario = ANDRE, FechaHora = AHORA },
                new MovimientoBultoReparto { Id = 2, Codigo = "EI2-2", TipoCodigo = "EI", Tipo = TiposMovimientoBulto.ENTREGADO, Usuario = ANDRE, FechaHora = AHORA }
            };

            List<BultoEnvioInternoDTO> bultos = ReglasEnviosInternos.Bultos(e, historia);

            Assert.AreEqual("En la furgoneta de Andre", bultos[0].Texto);
            Assert.AreEqual(SituacionesBultoEnvioInterno.ENTREGADO, bultos[1].Situacion);
            Assert.AreEqual("En Alcobendas, sin cargar", bultos[2].Texto);
        }

        [TestMethod]
        public void MinutosDeParada_LosDeUnaReposicion()
        {
            var c = ConstantesTiemposReparto.PorDefecto();
            Assert.AreEqual(MinutosParadaReparto.DeTipo(TiposParadaReparto.REPOSICION, c), MinutosParadaReparto.DeTipo(TiposParadaReparto.ENVIO_INTERNO, c));
        }

        [TestMethod]
        public void Etiqueta_EnvioInternoConOrigenDestinoYCodigos()
        {
            string zpl = ReglasEnviosInternos.Zpl(new EnvioInternoReparto { Id = 12, Origen = "ALC", Destino = "REI", Descripcion = "Máquina", Bultos = 2 }, AHORA);

            StringAssert.Contains(zpl, "ENVÍO INTERNO");
            StringAssert.Contains(zpl, "DE ALCOBENDAS A REINA");
            StringAssert.Contains(zpl, "Envío: EI12");
            StringAssert.Contains(zpl, "^FDEI12-1^FS");
            StringAssert.Contains(zpl, "^FDEI12-2^FS");
            StringAssert.Contains(zpl, "Bulto 2/2");
        }

        // ================================================================ oficina

        private ServicioEnviosInternos Oficina(IImpresoraEtiquetas impresora, Func<string, string> impresoraDelUsuario)
            => new ServicioEnviosInternos(internos, furgoneta, impresora, impresoraDelUsuario, () => AHORA);

        [TestMethod]
        public async Task Crear_ImprimeUnaEtiquetaPorBulto_ConLaImpresoraDelUsuario()
        {
            var impresora = A.Fake<IImpresoraEtiquetas>();

            var r = await Oficina(impresora, u => "ZEBRA-ALG").Crear(new CrearEnvioInternoDTO { Origen = "ALC", Destino = "REI", Descripcion = " Máquina ", Bultos = 2 }, "Alfredo");

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            CollectionAssert.AreEqual(new[] { "EI12-1", "EI12-2" }, r.Contenido.Codigos);
            Assert.IsTrue(r.Contenido.EtiquetasImpresas);
            Assert.AreEqual("Máquina", r.Contenido.Descripcion);
            Assert.AreEqual(EstadosEnvioInterno.PENDIENTE, r.Contenido.Estado);
            A.CallTo(() => impresora.Imprimir("ZEBRA-ALG", A<string>.That.Contains("EI12-2"), A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Crear_SinImpresora_SeCreaIgualYEtiquetasImpresasFalse_YSinScript503()
        {
            var r = await Oficina(A.Fake<IImpresoraEtiquetas>(), u => throw new ArgumentException("sin impresora"))
                .Crear(new CrearEnvioInternoDTO { Origen = "ALG", Destino = "REI", Descripcion = "Folios", Bultos = 1 }, "Alfredo");
            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo);
            Assert.IsFalse(r.Contenido.EtiquetasImpresas);
            Assert.AreEqual(1, internos.Filas.Count);

            internos.Script = false;
            Assert.AreEqual(TipoResultadoReparto.NoDisponible, (await Oficina(null, null).Crear(new CrearEnvioInternoDTO(), "x")).Tipo);
        }

        [TestMethod]
        public async Task Anular_ConUnBultoEnLaFurgoneta_EnFurgoneta_YSinNada_Anulado()
        {
            EnvioInternoReparto e = Envio("ALG", "REI");
            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Cargar(Cargar("EI12-1"), ANDRE)).Tipo);

            var enFurgoneta = await Oficina(null, null).Anular(e.Id, "Ya no hace falta", "Alfredo");
            Assert.AreEqual(TipoResultadoReparto.Conflicto, enFurgoneta.Tipo);
            Assert.AreEqual("EN_FURGONETA", enFurgoneta.Codigo);
            StringAssert.Contains(enFurgoneta.Mensaje, "el envío interno EI12 lo lleva Andre");

            EnvioInternoReparto otro = Envio("ALG", "ALC");
            var anulado = await Oficina(null, null).Anular(otro.Id, null, "Alfredo");
            Assert.AreEqual(TipoResultadoReparto.Ok, anulado.Tipo, anulado.Mensaje);
            Assert.AreEqual(EstadosEnvioInterno.ANULADO, anulado.Contenido.Estado);
        }

        [TestMethod]
        public async Task Lista_ConDondeEstaCadaBulto()
        {
            Envio("ALG", "REI");
            await servicio.Cargar(Cargar("EI12-1"), ANDRE);

            var lista = await Oficina(null, null).Lista(7, true);

            EnvioInternoDTO dto = lista.Contenido.Single();
            Assert.AreEqual(EstadosEnvioInterno.EN_CAMINO, dto.Estado);
            Assert.AreEqual("En la furgoneta de Andre", dto.BultosDetalle[0].Texto);
            Assert.AreEqual("En Algete, sin cargar", dto.BultosDetalle[1].Texto);
        }

        // ================================================================ el reparto

        [TestMethod]
        public async Task Envios_DesdeTienda_RecogidaEnElOrigenYEntregaEnElDestino()
        {
            Envio("ALC", "REI");

            var lista = (await servicio.Envios(AHORA)).Contenido.Where(p => p.EsEnvioInterno).ToList();

            Assert.AreEqual(2, lista.Count);
            EnvioRepartoDTO recogida = lista.Single(p => p.FaseEnvioInterno == FasesEnvioInterno.RECOGIDA);
            EnvioRepartoDTO entrega = lista.Single(p => p.FaseEnvioInterno == FasesEnvioInterno.ENTREGA);
            Assert.AreEqual("EI12", recogida.CodigoDocumento);
            Assert.AreEqual("EI12", entrega.CodigoDocumento);
            Assert.AreNotEqual(recogida.EnvioId, entrega.EnvioId);
            Assert.AreEqual("ALC", recogida.SedeParada);
            Assert.AreEqual("REI", entrega.SedeParada);
            Assert.AreEqual("ALC", entrega.SedeOrigen);
            Assert.AreEqual("REI", entrega.SedeDestino);
            Assert.AreEqual("Máquina de demostración", entrega.DescripcionEnvioInterno);
            Assert.AreEqual(TiposParadaReparto.ENVIO_INTERNO, entrega.TipoParada);
            Assert.IsNotNull(entrega.Latitud, "La sede, con sus coordenadas");

            // Con «solo lo cargado», la recogida de nadie no sale en «Mis envíos»
            Assert.IsFalse((await servicio.Envios(AHORA, true, ANDRE, true)).Contenido.Any(p => p.EsEnvioInterno));
        }

        [TestMethod]
        public async Task Recoger_EnLaTienda_YEntregar_ConElNombreDeQuienLoRecibe()
        {
            EnvioInternoReparto e = Envio("ALC", "REI", 2);
            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Recoger(Leer("EI12-1"), ANDRE)).Tipo);

            // GET Bulto: el recogido, la de entrega; el que sigue en la tienda, la de recogida
            Assert.AreEqual(FasesEnvioInterno.ENTREGA, (await servicio.Bulto("EI12-1")).Contenido.FaseEnvioInterno);
            Assert.AreEqual(FasesEnvioInterno.RECOGIDA, (await servicio.Bulto("EI12-2")).Contenido.FaseEnvioInterno);
            // La parada de recogida ya es de quien recogió allí
            Assert.AreEqual("Andre", (await servicio.Envios(AHORA, true, ANDRE, true)).Contenido.Single(p => p.FaseEnvioInterno == FasesEnvioInterno.RECOGIDA).Repartidor);

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Recoger(Leer("EI12-2"), ANDRE)).Tipo);
            int entrega = ReglasEnviosInternos.EnvioIdEntrega(e.Id);
            var sinNombre = await servicio.Entregar(entrega, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() }, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Invalido, sinNombre.Tipo);
            StringAssert.Contains(sinNombre.Mensaje, "nombre de quien recibe");

            var peticion = new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Receptor = "Laura", BultosEntregados = new List<string> { "EI12-1" } };
            var r = await servicio.Entregar(entrega, peticion, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(EstadosEnvioInterno.ENTREGADO, e.Estado);
            Assert.AreEqual("Laura", e.NombreReceptor);
            Assert.AreEqual(2, furgoneta.Filas.Count(f => f.Tipo == TiposMovimientoBulto.ENTREGADO), "El leído y el que llevaba sin leer");
            Assert.IsTrue(furgoneta.Filas.Single(f => f.Tipo == TiposMovimientoBulto.ENTREGADO && f.Codigo == "EI12-2").Descripcion.EndsWith("(entregado sin leer)"));
            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Entregar(entrega, peticion, ANDRE)).Tipo, "El reintento: la misma respuesta");
            Assert.IsFalse((await servicio.Envios(AHORA)).Contenido.Any(p => p.EsEnvioInterno), "Entregado: sin paradas");
        }

        [TestMethod]
        public async Task Entregar_ConUnBultoQueNoSeCargo_EntregaIncompleta()
        {
            EnvioInternoReparto e = Envio("ALG", "REI", 2);
            await servicio.Cargar(Cargar("EI12-1"), ANDRE);

            var r = await servicio.Entregar(ReglasEnviosInternos.EnvioIdEntrega(e.Id),
                new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Receptor = "Laura" }, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            CollectionAssert.AreEqual(new[] { "EI12-2" }, r.Contenido.BultosSinEntregar);
            StringAssert.Contains(e.DetalleEntrega, "1 de 2");
        }

        [TestMethod]
        public async Task Cargar_DesdeAlgete_SiDesdeTienda_SeRecoge_YDeOtro409()
        {
            EnvioInternoReparto alg = Envio("ALG", "REI", 1);
            EnvioInternoReparto alc = Envio("ALC", "REI", 1);

            var carga = await servicio.Cargar(Cargar("EI12-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, carga.Tipo, carga.Mensaje);
            Assert.AreEqual(TiposParadaReparto.ENVIO_INTERNO, carga.Contenido.TipoParada);
            Assert.AreEqual(ReglasEnviosInternos.EnvioIdEntrega(alg.Id), carga.Contenido.EnvioId);
            Assert.AreEqual("Cargado", furgoneta.Filas.Single().Tipo);

            var deTienda = await servicio.Cargar(Cargar("EI13-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, deTienda.Tipo);
            StringAssert.Contains(deTienda.Mensaje, "«Recoger»");

            var deOtro = await servicio.Cargar(Cargar("EI12-1"), MIGUEL);
            Assert.AreEqual(ServicioRepartoPropio.CODIGO_ENVIO_DE_OTRO, deOtro.Codigo);

            var recogerDeAlgete = await servicio.Recoger(Leer("EI12-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Invalido, recogerDeAlgete.Tipo);
        }

        [TestMethod]
        public async Task Descargar_UnEI_ComoCualquierBulto_EIncidencia400()
        {
            EnvioInternoReparto e = Envio("ALG", "REI", 1);
            await servicio.Cargar(Cargar("EI12-1"), ANDRE);

            var r = await servicio.Descargar(Leer("EI12-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(TiposMovimientoBulto.DESCARGADO, furgoneta.Filas.Last().Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Incidencia(ReglasEnviosInternos.EnvioIdEntrega(e.Id),
                new IncidenciaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), Motivo = IncidenciaRepartoDTO.OTRO }, ANDRE)).Tipo);
        }

        [TestMethod]
        public async Task SinScript_ElRepartoComoAntes()
        {
            Envio("ALC", "REI");
            internos.Script = false;

            Assert.IsFalse((await servicio.Envios(AHORA)).Contenido.Any(p => p.EsEnvioInterno));
        }

        // ================================================================ controlador

        private RepartoPropioController Controlador(string grupo)
            => new RepartoPropioController(null, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                ServicioEnviosInternos = Oficina(A.Fake<IImpresoraEtiquetas>(), u => "ZEBRA"),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Alfredo"), new Claim(ClaimTypes.Role, grupo) }, "prueba"))
            };

        [TestMethod]
        public async Task Controlador_Crear201_Administracion403_Anular409ConElCuerpoDelCandado()
        {
            HttpResponseMessage creado = await (await Controlador(Constantes.GruposSeguridad.ALMACEN).CrearEnvioInterno(
                new CrearEnvioInternoDTO { Origen = "ALG", Destino = "REI", Descripcion = "Café", Bultos = 1 })).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Created, creado.StatusCode);
            StringAssert.Contains(await creado.Content.ReadAsStringAsync(), "\"Codigos\":[\"EI12-1\"]");

            HttpResponseMessage prohibido = await (await Controlador(Constantes.GruposSeguridad.ADMINISTRACION).CrearEnvioInterno(
                new CrearEnvioInternoDTO { Origen = "ALG", Destino = "REI", Descripcion = "Café", Bultos = 1 })).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);

            await servicio.Cargar(Cargar("EI12-1"), ANDRE);
            HttpResponseMessage anular = await (await Controlador(Constantes.GruposSeguridad.DIRECCION).AnularEnvioInterno(12)).ExecuteAsync(CancellationToken.None);
            Assert.AreEqual(HttpStatusCode.Conflict, anular.StatusCode);
            JObject cuerpo = JObject.Parse(await anular.Content.ReadAsStringAsync());
            Assert.AreEqual("EN_FURGONETA", (string)cuerpo["Codigo"]);
            Assert.AreEqual("Andre", (string)cuerpo["Repartidor"]);
        }
    }
}
