using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// NestoAPI#847 (11/11/26): se reimprimió la etiqueta RP de la parada de una nota de RECOGIDA (que no tiene bulto que etiquetar:
    /// los de una recogida son NR{pedido}-{N}) y acabó en un bulto fantasma en la furgoneta. Ni ImprimirEtiqueta ni la reimpresión
    /// de Tramitar (la de Nesto) la sacan para una recogida o un cobro: 409 con el motivo.
    /// </summary>
    [TestClass]
    public class EtiquetaRepartoPropioNotaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 11);
        private const int PEDIDO = 930500;

        private readonly List<EnviosAgencia> envios = new List<EnviosAgencia>();
        private IImpresoraEtiquetas impresora;
        private string tipoNota;
        private EnviosAgenciasController controller;

        [TestInitialize]
        public void Setup()
        {
            envios.Clear();
            tipoNota = null;
            NVEntities db = A.Fake<NVEntities>();
            DbSet<EnviosAgencia> fakeEnvios = FakeDbSet(envios);
            A.CallTo(() => fakeEnvios.FindAsync(A<object[]>._))
                .ReturnsLazily((object[] claves) => Task.FromResult(envios.FirstOrDefault(e => e.Numero == (int)claves[0])));
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(A.Fake<DbSet<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            IFabricaAgenciasRemotas fabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fabrica.Crear(Constantes.Agencias.AGENCIA_REPARTO_PROPIO))
                .Returns(new AgenciaRemotaRepartoPropio(n => Task.FromResult(envios.FirstOrDefault(e => e.Numero == n)), () => HOY));
            impresora = A.Fake<IImpresoraEtiquetas>();

            controller = new EnviosAgenciasController(db, fabrica)
            {
                Request = new HttpRequestMessage { RequestUri = new Uri("http://localhost/api/EnviosAgencias/250900/ImprimirEtiqueta") },
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\Alfredo") }, "Test")),
                MotivoPedidoRetenido = (e, p, rp) => Task.FromResult<string>(null),
                HoyRepartoPropio = () => HOY,
                ImpresoraEtiquetas = impresora,
                TipoNotaDelPedido = p => Task.FromResult(tipoNota),
                RegistrarEnElmah = ex => { }
            };
            controller.Configuration = new HttpConfiguration();
        }

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            DbSet<T> fakeSet = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IQueryable<T>)fakeSet).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeSet).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeSet).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeSet).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            return fakeSet;
        }

        private EnviosAgencia Parada(int numero = 250900, int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
        {
            var envio = new EnviosAgencia
            {
                Numero = numero,
                Empresa = "1  ",
                Cliente = "1179      ",
                Contacto = "0  ",
                Pedido = PEDIDO,
                Vendedor = "NV ",
                Agencia = agencia,
                Estado = Constantes.Agencias.ESTADO_TRAMITADO,
                Fecha = HOY,
                FechaEntrega = HOY,
                Servicio = 1,
                Bultos = 1,
                Nombre = "CLIENTE",
                Direccion = "Calle Mayor 1",
                CodPostal = "28110",
                Poblacion = "ALGETE",
                Pais = 34,
                CodigoBarras = "RP" + numero
            };
            envios.Add(envio);
            return envio;
        }

        private static string Texto(IHttpActionResult resultado, HttpStatusCode estado)
        {
            var contenido = resultado as NegotiatedContentResult<string>;
            Assert.IsNotNull(contenido, $"Se esperaba un {(int)estado} con texto y ha llegado {resultado?.GetType().Name}");
            Assert.AreEqual(estado, contenido.StatusCode);
            return contenido.Content;
        }

        [TestMethod]
        public async Task ImprimirEtiqueta_ParadaDeUnaRecogida_409SinImprimir()
        {
            Parada();
            tipoNota = TiposNotaEntrega.RECOGIDA;

            IHttpActionResult resultado = await controller.ImprimirEtiqueta(250900, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA" });

            string texto = Texto(resultado, HttpStatusCode.Conflict);
            StringAssert.Contains(texto, "nota de recogida 930500");
            StringAssert.Contains(texto, "NR930500-1");
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ImprimirEtiqueta_ParadaDeUnCobro_409SinImprimir()
        {
            Parada();
            tipoNota = TiposNotaEntrega.COBRO;

            IHttpActionResult resultado = await controller.ImprimirEtiqueta(250900, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA" });

            StringAssert.Contains(Texto(resultado, HttpStatusCode.Conflict), "nota de cobro 930500");
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Tramitar_ReimpresionDeLaParadaDeUnaRecogida_409()
        {
            Parada();
            tipoNota = TiposNotaEntrega.RECOGIDA;

            IHttpActionResult resultado = await controller.TramitarEnvio(250900);

            StringAssert.Contains(Texto(resultado, HttpStatusCode.Conflict), "no lleva etiqueta RP");
        }

        [TestMethod]
        public async Task ImprimirEtiqueta_EnvioNormalONotaDeEntrega_SeImprimeComoAntes()
        {
            Parada();
            foreach (string tipo in new[] { null, TiposNotaEntrega.ENTREGA })
            {
                tipoNota = tipo;

                IHttpActionResult resultado = await controller.ImprimirEtiqueta(250900, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA" });

                Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ImprimirEtiquetaEnvioResultadoDTO>), tipo ?? "sin nota");
            }
            A.CallTo(() => impresora.Imprimir("ZEBRA", A<string>._, A<string>._)).MustHaveHappenedTwiceExactly();
        }

        [TestMethod]
        public async Task ImprimirEtiqueta_SiNoSePuedeSaberElTipoDeNota_SeImprime()
        {
            Parada();
            controller.TipoNotaDelPedido = p => throw new InvalidOperationException("sin conexión");

            IHttpActionResult resultado = await controller.ImprimirEtiqueta(250900, new ImprimirEtiquetaEnvioDTO { Impresora = "ZEBRA" });

            Assert.IsInstanceOfType(resultado, typeof(OkNegotiatedContentResult<ImprimirEtiquetaEnvioResultadoDTO>));
        }

        [TestMethod]
        public void MotivoSinEtiqueta_SoloLaRecogidaYElCobroDelRepartoPropio()
        {
            var propio = new EnviosAgencia { Numero = 1, Pedido = PEDIDO, Agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO };
            var otra = new EnviosAgencia { Numero = 2, Pedido = PEDIDO, Agencia = Constantes.Agencias.AGENCIA_INNOVATRANS };

            Assert.IsNotNull(EtiquetaRepartoPropioDeNota.MotivoSinEtiqueta(propio, TiposNotaEntrega.RECOGIDA));
            Assert.IsNotNull(EtiquetaRepartoPropioDeNota.MotivoSinEtiqueta(propio, TiposNotaEntrega.COBRO));
            Assert.IsNull(EtiquetaRepartoPropioDeNota.MotivoSinEtiqueta(propio, TiposNotaEntrega.ENTREGA));
            Assert.IsNull(EtiquetaRepartoPropioDeNota.MotivoSinEtiqueta(propio, null));
            Assert.IsNull(EtiquetaRepartoPropioDeNota.MotivoSinEtiqueta(otra, TiposNotaEntrega.RECOGIDA));
        }
    }
}
