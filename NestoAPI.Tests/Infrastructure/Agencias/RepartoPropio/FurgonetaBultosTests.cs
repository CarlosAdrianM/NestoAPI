using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Informes;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#772: lo que lleva cada furgoneta, bulto a bulto. «Lleva encima» = último movimiento Cargado o Recogido suyo; Cargar
    /// apunta además su Cargado; recoger el último bulto de la reposición la deja «Recogida»; entregar sin leer da por entregados los
    /// de la parada; descargar; la red de seguridad del almacén; terminar la jornada con y sin bultos; el job de las 22:00.
    /// </summary>
    [TestClass]
    public class FurgonetaBultosTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 10, 31, 9, 0, 0);
        private const string ANDRE = "Andre";
        private const string MIGUEL = @"NUEVAVISION\Miguel";
        private const int NOTA_RECOGIDA = 929554;
        private const int TRASPASO_TIENDA = 81010; // Reina → Algete, 7 bultos
        private const int TRASPASO_A_TIENDA = 81020; // Algete → Reina, 2 bultos

        private ServicioRepartoPropioTests.RepositorioFalso repositorio;
        private CargaFurgonetaTests.CargaFalsa cargas;
        private ParadasRepartoTests.NotasFalsas notas;
        private ParadasRepartoTests.ReposicionesFalsas reposiciones;
        private BultosConOrigen etiquetas;
        private ParadasRepartoTests.AvisosFalsos avisos;
        private IAvisosAlmacen avisosAlmacen;
        private IServicioParadasNotaEntrega resolutor;
        private FurgonetaFalsa furgoneta;
        private MarcadorFalso marcador;
        private AvisosFurgonetaFalsos avisosFurgoneta;
        private ParametrosFurgoneta parametros;
        private DateTime ahora;
        private ServicioRepartoPropio servicio;

        [TestInitialize]
        public void Preparar()
        {
            ahora = AHORA;
            repositorio = new ServicioRepartoPropioTests.RepositorioFalso();
            cargas = new CargaFurgonetaTests.CargaFalsa();
            notas = new ParadasRepartoTests.NotasFalsas();
            reposiciones = new ParadasRepartoTests.ReposicionesFalsas();
            etiquetas = new BultosConOrigen();
            avisos = new ParadasRepartoTests.AvisosFalsos();
            avisosAlmacen = A.Fake<IAvisosAlmacen>();
            furgoneta = new FurgonetaFalsa();
            marcador = new MarcadorFalso();
            avisosFurgoneta = new AvisosFurgonetaFalsos();
            parametros = new ParametrosFurgoneta();
            resolutor = A.Fake<IServicioParadasNotaEntrega>();
            A.CallTo(() => resolutor.Resolver(A<string>._)).ReturnsLazily((string codigo) => Task.FromResult(Resolver(codigo)));
            servicio = Servicio(furgoneta);

            for (short b = 1; b <= 7; b++)
            {
                etiquetas.Filas.Add(new FilaBultoReposicion { Empresa = "1", Traspaso = TRASPASO_TIENDA, Bulto = b, Total = 7, Codigo = $"R{TRASPASO_TIENDA}-{b}" });
            }
            etiquetas.Origenes[TRASPASO_TIENDA] = new OrigenDestinoReposicion { Origen = "REI", Destino = "ALG" };
            for (short b = 1; b <= 2; b++)
            {
                etiquetas.Filas.Add(new FilaBultoReposicion { Empresa = "1", Traspaso = TRASPASO_A_TIENDA, Bulto = b, Total = 2, Codigo = $"R{TRASPASO_A_TIENDA}-{b}" });
            }
            etiquetas.Origenes[TRASPASO_A_TIENDA] = new OrigenDestinoReposicion { Origen = "ALG", Destino = "REI" };
        }

        private ServicioRepartoPropio Servicio(IRepositorioFurgoneta repo)
            => new ServicioRepartoPropio(repositorio, () => ahora, null, parametros,
                () => A.Fake<IServicioPagos>(), () => A.Fake<IInformesService>(), cargas, new DependenciasParadasReparto
                {
                    Notas = resolutor,
                    NotasParada = notas,
                    Reposiciones = reposiciones,
                    BultosReposicion = etiquetas,
                    Avisos = avisos,
                    Furgoneta = repo,
                    RecogidaReposiciones = marcador,
                    AvisosAlmacen = avisosAlmacen,
                    AvisosFurgoneta = avisosFurgoneta
                });

        private ResolucionCodigoNota Resolver(string codigo)
        {
            if (!CodigoNotaEntrega.TryLeer(codigo, out string prefijo, out int pedido, out int? bulto))
            {
                return null;
            }
            var r = new ResolucionCodigoNota { Pedido = pedido, Bulto = bulto ?? 1, EsRecogida = prefijo == "NR" };
            if (pedido == NOTA_RECOGIDA)
            {
                r.EsNota = true;
                r.EnvioId = repositorio.Envios.Values.FirstOrDefault(e => e.Pedido == pedido)?.Numero;
            }
            return r;
        }

        private EnviosAgencia Envio(int numero = 251300, int? pedido = 928000, short bultos = 3)
        {
            EnviosAgencia envio = ServicioRepartoPropioTests.Envio(numero, bultos: bultos);
            envio.Pedido = pedido;
            envio.Fecha = AHORA.Date;
            return repositorio.Con(envio);
        }

        private void ConNotaRecogida()
        {
            Envio(251501, NOTA_RECOGIDA, bultos: 2);
            notas.Filas.Add(new NotaParadaReparto { Pedido = NOTA_RECOGIDA, Tipo = TiposNotaEntrega.RECOGIDA, NumeroNota = 5502, Texto = "Recoger el sillón" });
        }

        private static CargaBultoRepartoDTO Carga(string codigo, Guid? clave = null)
            => new CargaBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = clave ?? Guid.NewGuid() };

        private static MovimientoBultoRepartoDTO Lectura(string codigo, Guid? clave = null, DateTime? fechaHora = null)
            => new MovimientoBultoRepartoDTO { Codigo = codigo, ClaveIdempotencia = clave ?? Guid.NewGuid(), FechaHora = fechaHora };

        private static EntregaRepartoDTO Entrega(Guid? clave = null, params string[] bultos)
            => new EntregaRepartoDTO { ClaveIdempotencia = clave ?? Guid.NewGuid(), BultosEntregados = bultos.Length == 0 ? null : bultos.ToList() };

        private static IPrincipal Almacen(string nombre = "Andre")
            => new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, nombre), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN) }, "prueba"));

        private async Task<List<BultoFurgonetaDTO>> LoQueLleva(string usuario)
        {
            ResultadoReparto<FurgonetaRepartoDTO> resultado = await servicio.Furgoneta(usuario);
            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            return resultado.Contenido.Bultos;
        }

        // ================================================================ sin script

        [TestMethod]
        public async Task SinScript_LoNuevo400_YCargarComoSiempre()
        {
            furgoneta.Script = false;
            Envio();

            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Furgoneta(ANDRE)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-1"), ANDRE)).Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, (await servicio.Descargar(Lectura("RP251300-1"), ANDRE)).Tipo);
            var jornada = await servicio.TerminarJornada(new TerminarJornadaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() }, ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Invalido, jornada.Tipo);
            StringAssert.Contains(jornada.Mensaje, "Issue772_FurgonetaBultos.sql");

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Cargar(Carga("RP251300-1"), ANDRE)).Tipo);
            Assert.AreEqual(1, cargas.Filas.Count);
            Assert.AreEqual(0, furgoneta.Filas.Count);
            var entrega = await servicio.Entregar(251300, Entrega(null, "RP999-1"), ANDRE);
            Assert.AreEqual(TipoResultadoReparto.Ok, entrega.Tipo, "Sin la furgoneta no se miran los bultos leídos");
            Assert.IsNull(entrega.Contenido.BultosEntregados);
        }

        // ================================================================ cargar

        [TestMethod]
        public async Task Cargar_ApuntaCargadoConLaMismaClave_YSaleEnSuFurgoneta()
        {
            Envio();
            Guid clave = Guid.NewGuid();

            _ = await servicio.Cargar(Carga("rp251300-2 ", clave), ANDRE);

            MovimientoBultoReparto m = furgoneta.Filas.Single();
            Assert.AreEqual("RP251300-2", m.Codigo);
            Assert.AreEqual("RP", m.TipoCodigo);
            Assert.AreEqual(TiposMovimientoBulto.CARGADO, m.Tipo);
            Assert.AreEqual(251300, m.EnvioId);
            Assert.AreEqual(2, m.Bulto);
            Assert.AreEqual(3, m.Total);
            Assert.AreEqual(clave, m.ClaveIdempotencia, "La misma clave que EnviosAgenciaCargaBultos");
            Assert.AreEqual("PELUQUERÍA LA BONITA (ALGETE)", m.Descripcion);
            Assert.AreEqual(OrigenesMovimientoBulto.REPARTIDOR, m.Origen);
            List<BultoFurgonetaDTO> lleva = await LoQueLleva(ANDRE);
            Assert.AreEqual("RP251300-2", lleva.Single().Codigo);
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
        }

        [TestMethod]
        public async Task Cargar_ElMismoBultoOtraVez_NoDuplica_YReintentoConLaMismaClaveTampoco()
        {
            Envio();
            Guid clave = Guid.NewGuid();
            _ = await servicio.Cargar(Carga("RP251300-1", clave), ANDRE);
            _ = await servicio.Cargar(Carga("RP251300-1", clave), ANDRE);
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            Assert.AreEqual(1, furgoneta.Filas.Count);
        }

        [TestMethod]
        public async Task Cargar_Reasignado_PasaALaFurgonetaDelNuevo()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(new CargaBultoRepartoDTO { Codigo = "RP251300-1", ClaveIdempotencia = Guid.NewGuid(), Reasignar = true }, MIGUEL);

            Assert.AreEqual(0, (await LoQueLleva(ANDRE)).Count);
            Assert.AreEqual("Miguel", (await LoQueLleva(MIGUEL)).Single().Usuario);
        }

        [TestMethod]
        public async Task Cargar_NR_NoApuntaNada_ElBultoNoExisteHastaRecogerlo()
        {
            ConNotaRecogida();

            Assert.AreEqual(TipoResultadoReparto.Ok, (await servicio.Cargar(Carga($"NR{NOTA_RECOGIDA}-1"), ANDRE)).Tipo);

            Assert.AreEqual(1, cargas.Filas.Count);
            Assert.AreEqual(0, furgoneta.Filas.Count);
        }

        [TestMethod]
        public async Task Cargar_ReposicionAUnaTienda_ApuntaElBultoRConLaParada()
        {
            var resultado = await servicio.Cargar(Carga($"R{TRASPASO_A_TIENDA}-2"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo, resultado.Mensaje);
            MovimientoBultoReparto m = furgoneta.Filas.Single();
            Assert.AreEqual($"R{TRASPASO_A_TIENDA}-2", m.Codigo);
            Assert.AreEqual("R", m.TipoCodigo);
            Assert.AreEqual(resultado.Contenido.EnvioId, m.EnvioId, "El EnvioId negativo de la parada");
            Assert.IsTrue(m.EnvioId < 0);
            Assert.AreEqual(TRASPASO_A_TIENDA, m.NumTraspaso);
            Assert.AreEqual(2, m.Total);
        }

        [TestMethod]
        public async Task Cargar_SiFallaLaFurgoneta_LaCargaSigueHecha()
        {
            Envio();
            furgoneta.FallarAlRegistrar = true;

            var resultado = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, resultado.Tipo);
            Assert.AreEqual(1, cargas.Filas.Count);
        }

        // ================================================================ lleva encima

        [TestMethod]
        public void LlevaEncima_UltimoMovimientoPorFechaHora_CargadoORecogidoSuyo()
        {
            var movimientos = new List<MovimientoBultoReparto>
            {
                Mov(1, "RP1-1", TiposMovimientoBulto.CARGADO, ANDRE, AHORA.AddHours(-3)),
                Mov(2, "RP1-1", TiposMovimientoBulto.ENTREGADO, ANDRE, AHORA.AddHours(-1)),
                Mov(3, "RP1-2", TiposMovimientoBulto.CARGADO, ANDRE, AHORA.AddHours(-3)),
                Mov(4, "NR9-1", TiposMovimientoBulto.RECOGIDO, ANDRE, AHORA.AddHours(-2)),
                Mov(5, "R8-1", TiposMovimientoBulto.RECOGIDO, MIGUEL, AHORA.AddHours(-2)),
                // Sin cobertura: la recogida de las 15:00 sube DESPUÉS de que el almacén lo leyera a las 18:00. Manda la hora.
                Mov(7, "R8-2", TiposMovimientoBulto.RECOGIDO, ANDRE, AHORA.AddHours(-5)),
                Mov(6, "R8-2", TiposMovimientoBulto.DESCARGADO, "Alfredo", AHORA.AddHours(-4))
            };

            List<string> deAndre = NucleoFurgoneta.EnFurgoneta(movimientos, @"NUEVAVISION\andre").Select(m => m.Codigo).ToList();

            CollectionAssert.AreEqual(new[] { "RP1-2", "NR9-1" }, deAndre);
            Assert.AreEqual(3, NucleoFurgoneta.EnFurgoneta(movimientos).Count, "Sin usuario: todas las furgonetas");
        }

        [TestMethod]
        public async Task Furgoneta_DeOtroOTodos_SoloAlmacenYDireccion()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            var andre = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre"), new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ADMINISTRACION) }, "prueba"));
            RepartoPropioController Controlador(IPrincipal usuario) => new RepartoPropioController(null, servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = usuario
            };

            HttpResponseMessage suya = await (await Controlador(andre).GetFurgoneta()).ExecuteAsync(CancellationToken.None);
            HttpResponseMessage deOtro = await (await Controlador(andre).GetFurgoneta("Miguel")).ExecuteAsync(CancellationToken.None);
            HttpResponseMessage todas = await (await Controlador(Almacen("Alfredo")).GetFurgoneta(null, true)).ExecuteAsync(CancellationToken.None);

            Assert.AreEqual(HttpStatusCode.OK, suya.StatusCode);
            StringAssert.Contains(await suya.Content.ReadAsStringAsync(), "RP251300-1");
            Assert.AreEqual(HttpStatusCode.Forbidden, deOtro.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, todas.StatusCode);
            StringAssert.Contains(await todas.Content.ReadAsStringAsync(), "\"Usuario\":\"Andre\"");
        }

        // ================================================================ recoger

        [TestMethod]
        public async Task Recoger_SeisDeSiete_NoQuedaRecogida_ElSeptimoSi()
        {
            for (int b = 1; b <= 6; b++)
            {
                var r = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}'{b}"), ANDRE, null, Almacen());
                Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
                Assert.IsFalse(r.Contenido.ReposicionRecogida);
                Assert.AreEqual(b, r.Contenido.BultosRecogidos);
            }
            Assert.AreEqual(0, marcador.Marcadas.Count, "Con 6 de 7, todavía no");

            var ultimo = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-7"), ANDRE, null, Almacen());

            Assert.IsTrue(ultimo.Contenido.ReposicionRecogida);
            Assert.AreEqual(7, ultimo.Contenido.BultosRecogidos);
            Assert.AreEqual(7, ultimo.Contenido.Total);
            Assert.AreEqual(7, ultimo.Contenido.EnFurgoneta);
            Assert.AreEqual("REI", marcador.Marcadas.Single());
            StringAssert.Contains(ultimo.Contenido.Mensaje, "queda recogida");
            Assert.AreEqual("Reposición 81010 Reina → Algete", ultimo.Contenido.Descripcion);
            A.CallTo(() => avisosAlmacen.HaCambiado("Reposiciones:REI", ANDRE, A<string>._, A<string>._)).MustHaveHappened(7, Times.Exactly);
            Assert.IsTrue(avisos.Recibidos.All(a => a.Motivo == RepartoCambiadoDTO.MOTIVO_FURGONETA));
            Assert.AreEqual(7, (await LoQueLleva(ANDRE)).Count(b => b.Tipo == TiposMovimientoBulto.RECOGIDO));
        }

        [TestMethod]
        public async Task Recoger_ElUltimoOtraVez_NoVuelveAMarcarNiApunta()
        {
            for (int b = 1; b <= 7; b++)
            {
                _ = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-{b}"), ANDRE, null, Almacen());
            }
            int filas = furgoneta.Filas.Count;

            var otraVez = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-7"), ANDRE, null, Almacen());

            Assert.IsTrue(otraVez.Contenido.YaEstaba);
            Assert.IsFalse(otraVez.Contenido.ReposicionRecogida);
            Assert.AreEqual(filas, furgoneta.Filas.Count);
            Assert.AreEqual(1, marcador.Marcadas.Count);
        }

        [TestMethod]
        public async Task Recoger_IdempotentePorClave()
        {
            Guid clave = Guid.NewGuid();
            var primera = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-1", clave), ANDRE, null, Almacen());
            var reintento = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-1", clave), ANDRE, null, Almacen());
            var otraCosa = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-2", clave), ANDRE, null, Almacen());

            Assert.AreEqual(TipoResultadoReparto.Ok, reintento.Tipo);
            Assert.AreEqual(primera.Contenido.Codigo, reintento.Contenido.Codigo);
            Assert.AreEqual(1, furgoneta.Filas.Count);
            Assert.AreEqual(TipoResultadoReparto.Conflicto, otraCosa.Tipo);
        }

        [TestMethod]
        public async Task Recoger_LoQueNoSeRecoge()
        {
            Envio();
            etiquetas.Filas.First(f => f.Traspaso == TRASPASO_TIENDA && f.Bulto == 3).FechaLectura = AHORA;

            var rp = await servicio.Recoger(Lectura("RP251300-1"), ANDRE);
            var aTienda = await servicio.Recoger(Lectura($"R{TRASPASO_A_TIENDA}-1"), ANDRE);
            var recibida = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-3"), ANDRE);
            var sinEtiqueta = await servicio.Recoger(Lectura("R99999-1"), ANDRE);
            var notaQueNoEs = await servicio.Recoger(Lectura("NR111111-1"), ANDRE);
            var basura = await servicio.Recoger(Lectura("8437017506362"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Invalido, rp.Tipo);
            StringAssert.Contains(rp.Mensaje, "Cargar furgoneta");
            Assert.AreEqual(TipoResultadoReparto.Conflicto, aTienda.Tipo);
            StringAssert.Contains(aTienda.Mensaje, "va a Reina");
            Assert.AreEqual(TipoResultadoReparto.Conflicto, recibida.Tipo);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, sinEtiqueta.Tipo);
            Assert.AreEqual(TipoResultadoReparto.NoEncontrado, notaQueNoEs.Tipo);
            Assert.AreEqual(TipoResultadoReparto.Invalido, basura.Tipo);
            Assert.AreEqual(0, furgoneta.Filas.Count);
            Assert.AreEqual(0, avisos.Recibidos.Count);
        }

        [TestMethod]
        public async Task Recoger_NR_PasaASuFurgonetaConElEnvioDeLaParada()
        {
            ConNotaRecogida();

            var r = await servicio.Recoger(Lectura($"NR{NOTA_RECOGIDA}-2"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            MovimientoBultoReparto m = furgoneta.Filas.Single();
            Assert.AreEqual($"NR{NOTA_RECOGIDA}-2", m.Codigo);
            Assert.AreEqual("NR", m.TipoCodigo);
            Assert.AreEqual(NOTA_RECOGIDA, m.Pedido);
            Assert.AreEqual(251501, m.EnvioId);
            Assert.AreEqual(2, m.Total);
            Assert.AreEqual(1, r.Contenido.BultosRecogidos);
            StringAssert.StartsWith(m.Descripcion, "Recogida 929554: PELUQUERÍA");
        }

        [TestMethod]
        public async Task Recoger_SiNoSePuedeMarcar_ElBultoQuedaIgualEnLaFurgoneta()
        {
            marcador.Resultado = false;
            for (int b = 1; b <= 7; b++)
            {
                _ = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-{b}"), ANDRE, null, Almacen());
            }

            Assert.AreEqual(7, furgoneta.Filas.Count);
            Assert.AreEqual(1, marcador.Marcadas.Count);
        }

        [TestMethod]
        public async Task RecogidaReposicionesReparto_LoQueNoSePuedeMarcar_EsFalseSinLanzar()
        {
            var preparacion = A.Fake<NestoAPI.Infraestructure.Reposiciones.IServicioPreparacionReposicion>();
            A.CallTo(() => preparacion.MarcarRecogida(A<NestoAPI.Infraestructure.Reposiciones.MarcarRecogidaReposicionDTO>._, A<IPrincipal>._))
                .Throws(new NestoAPI.Infraestructure.Exceptions.NestoBusinessException("Ya está marcada"));
            var marcar = new RecogidaReposicionesReparto(() => preparacion);

            Assert.IsFalse(await marcar.Marcar("1", "REI", Almacen()));
            A.CallTo(() => preparacion.MarcarRecogida(A<NestoAPI.Infraestructure.Reposiciones.MarcarRecogidaReposicionDTO>.That.Matches(p => p.Origen == "REI" && p.Destino == "ALG"), A<IPrincipal>._))
                .MustHaveHappenedOnceExactly();
        }

        // ================================================================ descargar

        [TestMethod]
        public async Task Descargar_SaleDeLaFurgoneta_YOtraVezYaEstaba()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);

            var r = await servicio.Descargar(Lectura("RP251300-1"), ANDRE);
            var otraVez = await servicio.Descargar(Lectura("RP251300-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(TiposMovimientoBulto.DESCARGADO, r.Contenido.Tipo);
            Assert.AreEqual(0, r.Contenido.EnFurgoneta);
            Assert.IsTrue(otraVez.Contenido.YaEstaba);
            Assert.AreEqual(2, furgoneta.Filas.Count);
            Assert.AreEqual(0, (await LoQueLleva(ANDRE)).Count);
        }

        [TestMethod]
        public async Task Descargar_LoQueNoEstaEnNinguna_409NoEnFurgoneta()
        {
            var r = await servicio.Descargar(Lectura("RP251300-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Conflicto, r.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.CODIGO_NO_EN_FURGONETA, r.Codigo);
            StringAssert.Contains(r.Mensaje, "no está en ninguna furgoneta");
        }

        [TestMethod]
        public async Task Descargar_ElDeOtro_SeDescargaYLoDice()
        {
            _ = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-1"), MIGUEL, null, Almacen("Miguel"));

            var r = await servicio.Descargar(Lectura($"R{TRASPASO_TIENDA}-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo);
            StringAssert.Contains(r.Contenido.Mensaje, "lo llevaba Miguel");
            Assert.AreEqual(0, (await LoQueLleva(MIGUEL)).Count);
            A.CallTo(() => avisosAlmacen.HaCambiado("Reposiciones:REI", ANDRE, A<string>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        // ================================================================ entregar

        [TestMethod]
        public async Task Entregar_SinLeerNinguno_TodosLosDeLaParadaQueLlevaba()
        {
            Envio();
            Envio(251301);
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251300-2"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251301-1"), ANDRE);

            var r = await servicio.Entregar(251300, Entrega(), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(2, r.Contenido.BultosEntregados);
            CollectionAssert.AreEqual(new[] { "RP251301-1" }, (await LoQueLleva(ANDRE)).Select(b => b.Codigo).ToList());
        }

        [TestMethod]
        public async Task Entregar_LeyendoAlgunos_LosDemasSiguenEnLaFurgoneta_YReintentarNoDuplica()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Cargar(Carga("RP251300-2"), ANDRE);
            Guid clave = Guid.NewGuid();

            var r = await servicio.Entregar(251300, Entrega(clave, "rp251300-1"), ANDRE);
            int filas = furgoneta.Filas.Count;
            _ = await servicio.Entregar(251300, Entrega(clave, "RP251300-1"), ANDRE);

            Assert.AreEqual(1, r.Contenido.BultosEntregados);
            CollectionAssert.AreEqual(new[] { "RP251300-2" }, (await LoQueLleva(ANDRE)).Select(b => b.Codigo).ToList());
            Assert.AreEqual(filas, furgoneta.Filas.Count, "Misma clave de entrega: no se apunta otra vez");
        }

        [TestMethod]
        public async Task Entregar_UnBultoLeidoQueNoSeHabiaCargado_TambienQuedaEntregado()
        {
            Envio();

            var r = await servicio.Entregar(251300, Entrega(null, "RP251300-3"), ANDRE);

            Assert.AreEqual(1, r.Contenido.BultosEntregados);
            MovimientoBultoReparto m = furgoneta.Filas.Single();
            Assert.AreEqual(TiposMovimientoBulto.ENTREGADO, m.Tipo);
            Assert.AreEqual(251300, m.EnvioId);
        }

        [TestMethod]
        public async Task Entregar_UnBultoDeOtraParada_400SinEntregar()
        {
            Envio();

            var r = await servicio.Entregar(251300, Entrega(null, "RP251301-1"), ANDRE);
            var deMas = await servicio.Entregar(251300, Entrega(null, "RP251300-9"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Invalido, r.Tipo);
            StringAssert.Contains(r.Mensaje, "no es de esta parada");
            Assert.AreEqual(TipoResultadoReparto.Invalido, deMas.Tipo);
            Assert.AreEqual(0, repositorio.Registros.Count, "No se ha entregado");
        }

        [TestMethod]
        public async Task Entregar_ConElInterruptorEncendido_HayQueLeerlos()
        {
            Envio();
            parametros.Obligatorio = "1";

            var sinLeer = await servicio.Entregar(251300, Entrega(), ANDRE);
            var leyendo = await servicio.Entregar(251300, Entrega(null, "RP251300-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Invalido, sinLeer.Tipo);
            Assert.AreEqual(ServicioRepartoPropio.MENSAJE_ESCANEO_OBLIGATORIO, sinLeer.Mensaje);
            Assert.AreEqual(TipoResultadoReparto.Ok, leyendo.Tipo, leyendo.Mensaje);
        }

        [TestMethod]
        public async Task Entregar_ParadaDeReposicion_SalenSusBultosR()
        {
            var carga = await servicio.Cargar(Carga($"R{TRASPASO_A_TIENDA}-1"), ANDRE);
            _ = await servicio.Cargar(Carga($"R{TRASPASO_A_TIENDA}-2"), ANDRE);
            int parada = carga.Contenido.EnvioId;

            var otra = await servicio.Entregar(parada, Entrega(null, $"R{TRASPASO_TIENDA}-1"), ANDRE);
            var r = await servicio.Entregar(parada, Entrega(null, $"R{TRASPASO_A_TIENDA}-1"), ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Invalido, otra.Tipo, "Ese traspaso no es de esta parada");
            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, r.Mensaje);
            Assert.AreEqual(1, r.Contenido.BultosEntregados);
            CollectionAssert.AreEqual(new[] { $"R{TRASPASO_A_TIENDA}-2" }, (await LoQueLleva(ANDRE)).Select(b => b.Codigo).ToList());
        }

        [TestMethod]
        public async Task Entregar_NotaDeRecogidaSinLeerLasNR_LasApuntaRecogidas()
        {
            ConNotaRecogida();
            var entrega = new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), BultosRecogidos = 2, BultosEntregados = new List<string> { "RP1-1" } };

            var r = await servicio.Entregar(251501, entrega, ANDRE);
            _ = await servicio.Entregar(251501, entrega, ANDRE);

            Assert.AreEqual(TipoResultadoReparto.Ok, r.Tipo, "En una recogida los leídos de entrega se ignoran");
            CollectionAssert.AreEqual(new[] { $"NR{NOTA_RECOGIDA}-1", $"NR{NOTA_RECOGIDA}-2" }, (await LoQueLleva(ANDRE)).Select(b => b.Codigo).ToList());
            Assert.IsTrue(furgoneta.Filas.All(f => f.Tipo == TiposMovimientoBulto.RECOGIDO));
            Assert.AreEqual(2, furgoneta.Filas.Count);
        }

        [TestMethod]
        public async Task Entregar_NotaDeRecogidaConLasNRLeidas_NoApuntaMas()
        {
            ConNotaRecogida();
            _ = await servicio.Recoger(Lectura($"NR{NOTA_RECOGIDA}-1"), ANDRE);

            _ = await servicio.Entregar(251501, new EntregaRepartoDTO { ClaveIdempotencia = Guid.NewGuid(), BultosRecogidos = 2 }, ANDRE);

            Assert.AreEqual(1, furgoneta.Filas.Count);
        }

        // ================================================================ red de seguridad

        [TestMethod]
        public async Task RedDeSeguridad_ElAlmacenLeeLoQueSigueEnLaFurgoneta_Descargado()
        {
            _ = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-1"), ANDRE, null, Almacen());
            _ = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-2"), ANDRE, null, Almacen());
            _ = await servicio.Descargar(Lectura($"R{TRASPASO_TIENDA}-2"), ANDRE);

            int apuntados = await RedSeguridadFurgoneta.Leidos(furgoneta, new[] { $"R{TRASPASO_TIENDA}-1", $"R{TRASPASO_TIENDA}-2", "R1-1" }, "NUEVAVISION\\Alfredo", true, AHORA);
            int otraVez = await RedSeguridadFurgoneta.Leidos(furgoneta, new[] { $"R{TRASPASO_TIENDA}-1" }, "Alfredo", true, AHORA.AddMinutes(1));

            Assert.AreEqual(1, apuntados, "El 2 ya lo había descargado él; el R1-1 no está en ninguna");
            Assert.AreEqual(0, otraVez);
            MovimientoBultoReparto m = furgoneta.Filas.Last();
            Assert.AreEqual(TiposMovimientoBulto.DESCARGADO, m.Tipo);
            Assert.AreEqual(OrigenesMovimientoBulto.ALMACEN, m.Origen);
            Assert.AreEqual("NUEVAVISION\\Alfredo", m.Usuario);
            Assert.AreEqual(0, (await LoQueLleva(ANDRE)).Count);
        }

        [TestMethod]
        public async Task RedDeSeguridad_EnUnaTienda_Entregado_YLaNotaEnteraConNRSinBulto()
        {
            _ = await servicio.Cargar(Carga($"R{TRASPASO_A_TIENDA}-1"), ANDRE);
            ConNotaRecogida();
            _ = await servicio.Recoger(Lectura($"NR{NOTA_RECOGIDA}-1"), ANDRE);
            _ = await servicio.Recoger(Lectura($"NR{NOTA_RECOGIDA}-2"), ANDRE);

            Assert.AreEqual(1, await RedSeguridadFurgoneta.Leidos(furgoneta, new[] { $"R{TRASPASO_A_TIENDA}-1" }, "Paloma", false, AHORA));
            Assert.AreEqual(2, await RedSeguridadFurgoneta.Leidos(furgoneta, new[] { $"NR{NOTA_RECOGIDA}" }, "Alfredo", true, AHORA));

            Assert.AreEqual(TiposMovimientoBulto.ENTREGADO, furgoneta.Filas.Single(f => f.Codigo == $"R{TRASPASO_A_TIENDA}-1" && f.Origen == OrigenesMovimientoBulto.ALMACEN).Tipo);
            Assert.AreEqual(0, (await LoQueLleva(ANDRE)).Count);
        }

        [TestMethod]
        public async Task RedDeSeguridad_NuncaLanza()
        {
            furgoneta.FallarAlRegistrar = true;
            _ = await RedSeguridadFurgoneta.Leidos(furgoneta, new[] { "R1-1" }, "Alfredo", true, AHORA);
            Assert.AreEqual(0, await RedSeguridadFurgoneta.Leidos(null, new[] { "R1-1" }, "Alfredo", true, AHORA));
        }

        // ================================================================ terminar jornada

        [TestMethod]
        public async Task TerminarJornada_Vacia_TerminaSinAvisar()
        {
            var r = await servicio.TerminarJornada(new TerminarJornadaRepartoDTO { ClaveIdempotencia = Guid.NewGuid() }, ANDRE);

            Assert.IsTrue(r.Contenido.Terminada);
            Assert.AreEqual(0, r.Contenido.Avisados.Count);
            Assert.AreEqual(0, furgoneta.Jornadas.Single().BultosPendientes);
            Assert.AreEqual(0, avisosFurgoneta.Llamadas.Count);
            Assert.AreEqual(AHORA, (await servicio.Furgoneta(ANDRE)).Contenido.JornadaTerminada);
        }

        [TestMethod]
        public async Task TerminarJornada_ConBultos_SinConfirmarEnsenaLaLista_ConfirmandoAvisaUnaVez()
        {
            Envio();
            _ = await servicio.Cargar(Carga("RP251300-1"), ANDRE);
            _ = await servicio.Recoger(Lectura($"R{TRASPASO_TIENDA}-1"), ANDRE, null, Almacen());
            Guid clave = Guid.NewGuid();

            var pregunta = await servicio.TerminarJornada(new TerminarJornadaRepartoDTO { ClaveIdempotencia = clave }, ANDRE);
            Assert.IsFalse(pregunta.Contenido.Terminada);
            Assert.AreEqual(2, pregunta.Contenido.Pendientes.Count);
            StringAssert.Contains(pregunta.Contenido.Mensaje, "Quedan 2 bultos");
            Assert.AreEqual(0, furgoneta.Jornadas.Count, "Sin confirmar no se guarda nada");

            var confirmada = await servicio.TerminarJornada(new TerminarJornadaRepartoDTO { ClaveIdempotencia = clave, Confirmar = true }, ANDRE);
            var reintento = await servicio.TerminarJornada(new TerminarJornadaRepartoDTO { ClaveIdempotencia = clave, Confirmar = true }, ANDRE);

            Assert.IsTrue(confirmada.Contenido.Terminada);
            CollectionAssert.AreEqual(new[] { "Almacén", "Dirección", "Andre" }, confirmada.Contenido.Avisados);
            Assert.AreEqual(2, furgoneta.Jornadas.Single().BultosPendientes);
            Assert.AreEqual($"RP251300-1,R{TRASPASO_TIENDA}-1", furgoneta.Jornadas.Single().Pendientes);
            Assert.IsTrue(reintento.Contenido.Terminada);
            Assert.AreEqual(1, avisosFurgoneta.Llamadas.Count, "Misma clave: no avisa dos veces");
            Assert.IsTrue(avisosFurgoneta.Llamadas[0].AlTerminar);
        }

        [TestMethod]
        public async Task AvisosFurgoneta_AlmacenDireccionYElRepartidor_CadaUnoUnaVez()
        {
            var push = A.Fake<IServicioNotificacionesPush>();
            var guardados = new List<(string Usuario, NotificacionPushDTO Aviso)>();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string u, string app, NotificacionPushDTO n) => guardados.Add((u, n))).Returns(Task.CompletedTask);
            var miembros = new Dictionary<string, List<string>>
            {
                [Constantes.GruposSeguridad.ALMACEN] = new List<string> { "Alfredo", "Andre" },
                [Constantes.GruposSeguridad.DIRECCION] = new List<string> { "Carlos", "alfredo" }
            };
            var avisador = new AvisosFurgonetaSinDescargar(() => push, g => miembros[g]);
            var pendientes = new List<MovimientoBultoReparto> { Mov(1, "RP1-1", TiposMovimientoBulto.CARGADO, ANDRE, AHORA), Mov(2, "R8-2", TiposMovimientoBulto.RECOGIDO, ANDRE, AHORA) };

            List<string> avisados = await avisador.FurgonetaSinDescargar("NUEVAVISION\\Andre", pendientes, true);

            CollectionAssert.AreEquivalent(new[] { "NUEVAVISION\\Alfredo", "NUEVAVISION\\Andre", "NUEVAVISION\\Carlos" }, guardados.Select(g => g.Usuario).ToList());
            CollectionAssert.AreEqual(new[] { "Almacén", "Dirección", "Andre" }, avisados);
            NotificacionPushDTO aviso = guardados[0].Aviso;
            Assert.AreEqual(AvisosFurgonetaSinDescargar.TIPO_NOTIFICACION, aviso.Tipo);
            Assert.AreEqual("Furgoneta sin descargar: Andre", aviso.Titulo);
            StringAssert.Contains(aviso.Cuerpo, "Andre ha terminado la jornada con 2 bultos en la furgoneta: RP1-1, R8-2.");
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappened(3, Times.Exactly);
        }

        // ================================================================ job de las 22:00

        [TestMethod]
        public async Task Job22_AvisaSoloALosQueTienenBultosYNoHanTerminado()
        {
            furgoneta.Filas.Add(Mov(1, "RP1-1", TiposMovimientoBulto.CARGADO, ANDRE, AHORA));
            furgoneta.Filas.Add(Mov(2, "RP1-2", TiposMovimientoBulto.CARGADO, "NUEVAVISION\\Andre", AHORA));
            furgoneta.Filas.Add(Mov(3, "RP2-1", TiposMovimientoBulto.CARGADO, MIGUEL, AHORA));
            furgoneta.Filas.Add(Mov(4, "RP3-1", TiposMovimientoBulto.CARGADO, "Santi", AHORA));
            furgoneta.Filas.Add(Mov(5, "RP3-1", TiposMovimientoBulto.ENTREGADO, "Santi", AHORA.AddHours(1)));
            furgoneta.Jornadas.Add(new JornadaReparto { Usuario = "Miguel", Fecha = AHORA.Date, FechaFin = AHORA, BultosPendientes = 1 });
            var job = new FurgonetaSinDescargarJobsService(furgoneta, avisosFurgoneta, () => AHORA.Date.AddHours(22));

            int avisados = await job.AvisarSinDescargar();

            Assert.AreEqual(1, avisados);
            Assert.AreEqual(ANDRE, ServicioRepartoPropio.NombreRepartidor(avisosFurgoneta.Llamadas.Single().Repartidor));
            Assert.AreEqual(2, avisosFurgoneta.Llamadas.Single().Pendientes.Count);
            Assert.IsFalse(avisosFurgoneta.Llamadas.Single().AlTerminar);
            Assert.AreEqual("0 22 * * 1-5", FurgonetaSinDescargarJobsService.CRON);
        }

        [TestMethod]
        public async Task Job22_SinScript_NoHaceNada()
        {
            furgoneta.Script = false;
            furgoneta.Filas.Add(Mov(1, "RP1-1", TiposMovimientoBulto.CARGADO, ANDRE, AHORA));

            Assert.AreEqual(0, await new FurgonetaSinDescargarJobsService(furgoneta, avisosFurgoneta, () => AHORA).AvisarSinDescargar());
            Assert.AreEqual(0, avisosFurgoneta.Llamadas.Count);
        }

        // ================================================================ piezas

        [TestMethod]
        public void Codigos_SeNormalizan()
        {
            Assert.AreEqual("RP251300-1", CodigoBultoFurgoneta.Leer(" rp251300-1").Codigo);
            CodigoBultoFurgoneta nr = CodigoBultoFurgoneta.Leer("NR929554");
            Assert.AreEqual("NR929554-1", nr.Codigo);
            Assert.IsTrue(nr.SinBulto);
            Assert.AreEqual("R81010-2", CodigoBultoFurgoneta.Leer("]C1R81010'2").Codigo);
            Assert.IsNull(CodigoBultoFurgoneta.Leer("NE929667"), "Una nota de entrega es un documento, no un bulto");
            Assert.IsNull(CodigoBultoFurgoneta.Leer("8437017506362"));
            Assert.AreEqual(NucleoFurgoneta.ClaveDerivada(Guid.Empty, "rp1-1", "Entregado"), NucleoFurgoneta.ClaveDerivada(Guid.Empty, "RP1-1", "Entregado"));
            Assert.AreNotEqual(NucleoFurgoneta.ClaveDerivada(Guid.Empty, "RP1-1", "Entregado"), NucleoFurgoneta.ClaveDerivada(Guid.Empty, "RP1-2", "Entregado"));
        }

        [TestMethod]
        public void GrupoReposiciones_SeNormaliza()
        {
            Assert.AreEqual("Reposiciones:REI", AvisosAlmacen.GrupoReposiciones(" rei "));
            Assert.AreEqual("Reposiciones:ALC", AvisosAlmacen.Normalizar("reposiciones:alc"));
            Assert.IsNull(AvisosAlmacen.Normalizar("Reposiciones:"));
        }

        // ================================================================ dobles

        private static MovimientoBultoReparto Mov(int id, string codigo, string tipo, string usuario, DateTime fechaHora)
            => new MovimientoBultoReparto
            {
                Id = id, Codigo = codigo, TipoCodigo = codigo.StartsWith("RP") ? "RP" : codigo.StartsWith("NR") ? "NR" : "R", Tipo = tipo, Usuario = usuario,
                FechaHora = fechaHora, FechaRegistro = fechaHora, Bulto = 1, Origen = OrigenesMovimientoBulto.REPARTIDOR, ClaveIdempotencia = Guid.NewGuid()
            };

        internal class FurgonetaFalsa : IRepositorioFurgoneta
        {
            public bool Script = true;
            public bool FallarAlRegistrar;
            public readonly List<MovimientoBultoReparto> Filas = new List<MovimientoBultoReparto>();
            public readonly List<JornadaReparto> Jornadas = new List<JornadaReparto>();

            public bool ScriptAplicado() => Script;

            public Task<List<MovimientoBultoReparto>> LeerUltimos(DateTime desde)
                => Task.FromResult(NucleoFurgoneta.Ultimos(Filas.Where(f => Filas.Any(x => x.Codigo == f.Codigo && x.FechaRegistro >= desde))));

            public Task<List<MovimientoBultoReparto>> LeerHistoria(ConsultaMovimientosBulto consulta)
                => Task.FromResult(consulta == null || consulta.Vacia ? new List<MovimientoBultoReparto>()
                    : Filas.Where(consulta.Cumple).OrderBy(f => f.FechaHora).ThenBy(f => f.Id).ToList());

            public Task<MovimientoBultoReparto> LeerPorClave(Guid clave) => Task.FromResult(Filas.FirstOrDefault(f => f.ClaveIdempotencia == clave));

            public Task<int> Registrar(IReadOnlyList<MovimientoBultoReparto> movimientos)
            {
                if (FallarAlRegistrar)
                {
                    throw new InvalidOperationException("BD caída");
                }
                int n = 0;
                foreach (MovimientoBultoReparto m in movimientos)
                {
                    if (Filas.Any(f => f.ClaveIdempotencia == m.ClaveIdempotencia))
                    {
                        continue;
                    }
                    m.Id = Filas.Count == 0 ? 1 : Filas.Max(f => f.Id) + 1;
                    Filas.Add(m);
                    n++;
                }
                return Task.FromResult(n);
            }

            public Task<JornadaReparto> LeerJornadaPorClave(Guid clave) => Task.FromResult(Jornadas.FirstOrDefault(j => j.ClaveIdempotencia == clave));

            public Task<List<JornadaReparto>> LeerJornadas(DateTime dia) => Task.FromResult(Jornadas.Where(j => j.Fecha == dia.Date).ToList());

            public Task RegistrarJornada(JornadaReparto jornada)
            {
                if (Jornadas.Any(j => j.ClaveIdempotencia == jornada.ClaveIdempotencia))
                {
                    throw new ClaveIdempotenciaRepetidaException(null);
                }
                Jornadas.Add(jornada);
                return Task.CompletedTask;
            }
        }

        internal class BultosConOrigen : IRepositorioBultosReposicion
        {
            public readonly List<FilaBultoReposicion> Filas = new List<FilaBultoReposicion>();
            public readonly Dictionary<int, OrigenDestinoReposicion> Origenes = new Dictionary<int, OrigenDestinoReposicion>();

            public Task<bool> ExisteTabla() => Task.FromResult(true);
            public Task<List<FilaBultoReposicion>> Leer(string empresa, int traspaso) => Task.FromResult(Filas.Where(f => f.Traspaso == traspaso).ToList());
            public Task<FilaBultoReposicion> LeerPorCodigo(string empresa, string codigo)
                => Task.FromResult(Filas.FirstOrDefault(f => f.Codigo == CodigosBultoReposicion.Normalizar(codigo)));
            public Task<OrigenDestinoReposicion> LeerOrigenDestino(string empresa, int traspaso)
                => Task.FromResult(Origenes.TryGetValue(traspaso, out OrigenDestinoReposicion od) ? od : null);
            public Task<bool> Reemplazar(string empresa, int traspaso, int total, string usuario) => throw new NotSupportedException();
        }

        internal class MarcadorFalso : IRecogidaReposicionesReparto
        {
            public bool Resultado = true;
            public readonly List<string> Marcadas = new List<string>();

            public Task<bool> Marcar(string empresa, string tienda, IPrincipal quien)
            {
                Marcadas.Add(tienda);
                return Task.FromResult(Resultado);
            }
        }

        internal class AvisosFurgonetaFalsos : IAvisosFurgoneta
        {
            public readonly List<(string Repartidor, IReadOnlyList<MovimientoBultoReparto> Pendientes, bool AlTerminar)> Llamadas =
                new List<(string, IReadOnlyList<MovimientoBultoReparto>, bool)>();

            public Task<List<string>> FurgonetaSinDescargar(string repartidor, IReadOnlyList<MovimientoBultoReparto> pendientes, bool alTerminar)
            {
                Llamadas.Add((repartidor, pendientes, alTerminar));
                return Task.FromResult(new List<string> { "Almacén", "Dirección", ServicioRepartoPropio.NombreRepartidor(repartidor) });
            }
        }

        internal class ParametrosFurgoneta : NestoAPI.Infraestructure.ILectorParametrosUsuario
        {
            public string Obligatorio;

            public string LeerParametro(string empresa, string usuario, string parametro)
                => parametro == ServicioRepartoPropio.PARAMETRO_ESCANEO_ENTREGA_OBLIGATORIO && usuario == Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO ? Obligatorio : null;

            public string LeerParametroSinDefecto(string empresa, string usuario, string parametro) => null;
        }
    }
}
