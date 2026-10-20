using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#595 (slice 5): los pasos reales de la etiqueta del packing llaman a las acciones de EnviosAgencias y traducen
    /// su respuesta (200, 400, 409, 502 con texto o con HttpError). BD, agencia e impresora falsas: no sale nada.
    /// </summary>
    [TestClass]
    public class PasosEtiquetaPackingApiTests
    {
        private NVEntities db;
        private DbSet<EnviosAgencia> fakeEnvios;
        private IAgenciaRemota fakeAgencia;
        private IImpresoraEtiquetas impresora;
        private ILectorParametrosUsuario parametros;
        private IServicioPreparacionAlmacen preparacion;
        private PasosEtiquetaPackingApi pasos;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            DbSet<AgenciaLlamadaWeb> fakeLlamadas = A.Fake<DbSet<AgenciaLlamadaWeb>>(o => o.Implements<IQueryable<AgenciaLlamadaWeb>>().Implements<IDbAsyncEnumerable<AgenciaLlamadaWeb>>());
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.AgenciasLlamadasWeb).Returns(fakeLlamadas);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));

            fakeAgencia = A.Fake<IAgenciaRemota>();
            A.CallTo(() => fakeAgencia.Intercambios).Returns(new List<IntercambioRemoto>());
            IFabricaAgenciasRemotas fabrica = A.Fake<IFabricaAgenciasRemotas>();
            A.CallTo(() => fabrica.Crear(A<int>._)).Returns(null);
            A.CallTo(() => fabrica.Crear(Constantes.Agencias.AGENCIA_CTT)).Returns(fakeAgencia);
            impresora = A.Fake<IImpresoraEtiquetas>();
            parametros = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => parametros.LeerParametro("1", "Andre", "ImpresoraCodBarras")).Returns("ZEBRA-PACKING");

            var envios = new EnviosAgenciasController(db, fabrica)
            {
                LectorParametros = parametros,
                ImpresoraEtiquetas = impresora,
                GlsTramitarPorApi = () => false
            };
            preparacion = A.Fake<IServicioPreparacionAlmacen>();
            var usuario = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre") }, "Bearer"));
            pasos = new PasosEtiquetaPackingApi(usuario, preparacion, envios);
        }

        private void ConEnvio(int agencia, string codigoBarras)
        {
            var envio = new EnviosAgencia { Numero = 5001, Empresa = "1  ", Agencia = agencia, Pedido = 926940, Estado = 0, Bultos = 2, CodigoBarras = codigoBarras, CodPostal = "28001" };
            A.CallTo(() => fakeEnvios.FindAsync(5001)).Returns(Task.FromResult(envio));
        }

        [TestMethod]
        public async Task Imprimir_Bien_DevuelveLaImpresoraDelUsuario()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null)).Returns(Task.FromResult(new EtiquetaDataTrans { Contenido = "^XA^XZ" }));

            PasoEtiquetaAgencia paso = await pasos.Imprimir(5001, null);

            Assert.IsTrue(paso.Exito);
            Assert.AreEqual("ZEBRA-PACKING", paso.Impresora);
            Assert.AreEqual("CTT123", paso.CodigoBarras);
            A.CallTo(() => impresora.Imprimir("ZEBRA-PACKING", "^XA^XZ", A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Imprimir_SinAlbaran_409ConSuTexto()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "");

            PasoEtiquetaAgencia paso = await pasos.Imprimir(5001, "ZEBRA2");

            Assert.IsFalse(paso.Exito);
            Assert.AreEqual(HttpStatusCode.Conflict, paso.Estado);
            Assert.AreEqual("Primero hay que tramitar el envío.", paso.Mensaje);
            Assert.AreEqual("ZEBRA2", paso.Impresora);
        }

        [TestMethod]
        public async Task Imprimir_WindowsNoDeja_502ConElMotivo()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null)).Returns(Task.FromResult(new EtiquetaDataTrans { Contenido = "^XA^XZ" }));
            A.CallTo(() => impresora.Imprimir(A<string>._, A<string>._, A<string>._))
                .Throws(new ImpresionEtiquetasException("No se puede abrir la impresora ZEBRA-PACKING (error de Windows 5)."));

            PasoEtiquetaAgencia paso = await pasos.Imprimir(5001, null);

            Assert.IsFalse(paso.Exito);
            Assert.AreEqual(HttpStatusCode.BadGateway, paso.Estado);
            StringAssert.Contains(paso.Mensaje, "error de Windows 5");
        }

        [TestMethod]
        public async Task Imprimir_AgenciaNoResponde_502ConElMessageDelHttpError()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_CTT, "CTT123");
            A.CallTo(() => fakeAgencia.ReimprimirAsync("CTT123", null, null))
                .ThrowsAsync(new AgenciaRemotaException("CTT (Etiqueta) respondió HTTP 500: CONNECTIVITY") { EsTransitoria = true });

            PasoEtiquetaAgencia paso = await pasos.Imprimir(5001, null);

            Assert.IsFalse(paso.Exito);
            Assert.AreEqual(HttpStatusCode.BadGateway, paso.Estado);
            Assert.IsFalse(string.IsNullOrWhiteSpace(paso.Mensaje));
            Assert.IsFalse(paso.Mensaje.TrimStart().StartsWith("{"), "Se enseña el Message, no el JSON");
        }

        [TestMethod]
        public async Task Tramitar_AgenciaSinGestionRemota_400ConSuTexto()
        {
            ConEnvio(Constantes.Agencias.AGENCIA_GLS, "");

            PasoEtiquetaAgencia paso = await pasos.Tramitar(5001);

            Assert.IsFalse(paso.Exito);
            Assert.AreEqual(HttpStatusCode.BadRequest, paso.Estado);
            StringAssert.Contains(paso.Mensaje, "no tiene gestión remota");
        }

        [TestMethod]
        public async Task LeerEntrega_ConPacking_LaDelPickingEntero()
        {
            A.CallTo(() => preparacion.LeerPackingDePedido("1", 926940)).Returns(new PackingAlmacenDTO { Picking = 99633 });
            A.CallTo(() => preparacion.LeerPacking("1", 99633)).Returns(new PackingAlmacenDTO
            {
                Picking = 99633,
                Entregas = new List<EntregaPackingAlmacenDTO>
                {
                    new EntregaPackingAlmacenDTO
                    {
                        Pedidos = new List<PedidoPackingAlmacenDTO> { new PedidoPackingAlmacenDTO { Pedido = 926940 }, new PedidoPackingAlmacenDTO { Pedido = 926941 } },
                        Bultos = new List<BultoAlmacenDTO>
                        {
                            new BultoAlmacenDTO { Pedido = 926940, Picking = 99633, Bulto = 1, TieneFoto = true, RutaBlob = "a" },
                            new BultoAlmacenDTO { Pedido = 926941, Picking = 99633, Bulto = 2, TieneFoto = true, RutaBlob = "b" }
                        }
                    }
                }
            });

            EntregaParaEtiqueta entrega = await pasos.LeerEntrega("1", 926940);

            Assert.AreEqual(2, entrega.Bultos);
            CollectionAssert.AreEqual(new List<int> { 926941 }, entrega.OtrosPedidos);
        }

        [TestMethod]
        public async Task LeerEntrega_SinPacking_LosBultosDelPedidoONull()
        {
            A.CallTo(() => preparacion.LeerPackingDePedido("1", 926940)).Returns(Task.FromResult<PackingAlmacenDTO>(null));
            A.CallTo(() => preparacion.LeerBultos("1", 926940)).Returns(new List<BultoAlmacenDTO>());

            Assert.IsNull(await pasos.LeerEntrega("1", 926940));

            A.CallTo(() => preparacion.LeerBultos("1", 926940)).Returns(new List<BultoAlmacenDTO> { new BultoAlmacenDTO { Bulto = 1, TieneFoto = true, RutaBlob = "a" } });
            Assert.AreEqual(1, (await pasos.LeerEntrega("1", 926940)).Bultos);
        }
    }
}
