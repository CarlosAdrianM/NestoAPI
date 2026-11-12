using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>NestoAPI#857: la pasada del job (lector y repositorio falsos).</summary>
    [TestClass]
    public class ServicioCorreosSeguimientoComprasTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 12, 10, 0, 0);
        private const string BUZON = "buzon-prueba@ejemplo.test";

        private ILectorBuzonCompras lector;
        private IRepositorioCorreosSeguimiento repositorio;
        private List<CorreoSeguimientoFila> insertadas;
        private HashSet<string> yaEnTabla;
        private List<Exception> errores;
        private LecturaBuzonCompras lectura;

        [TestInitialize]
        public void Setup()
        {
            ServicioCorreosSeguimientoCompras.OlvidarSinTablaApuntado();
            errores = new List<Exception>();
            insertadas = new List<CorreoSeguimientoFila>();
            yaEnTabla = new HashSet<string>();
            lectura = new LecturaBuzonCompras();

            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns(BUZON);
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(lectura));

            repositorio = A.Fake<IRepositorioCorreosSeguimiento>();
            A.CallTo(() => repositorio.TablasExisten()).Returns(true);
            A.CallTo(() => repositorio.LeerEstado(BUZON)).Returns("delta-anterior");
            A.CallTo(() => repositorio.LeerRemitentes()).Returns(new List<RemitenteMarcado>());
            A.CallTo(() => repositorio.LeerPedidosEnAire(A<string>._)).Returns(new List<PedidoCompraEnAire>
            {
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912001, Proveedor = "9001", NombreProveedor = "COSMÉTICOS INVENTADOS, S.L." },
                new PedidoCompraEnAire { Empresa = "1", Pedido = 912007, Proveedor = "9001", NombreProveedor = "COSMÉTICOS INVENTADOS, S.L." }
            });
            A.CallTo(() => repositorio.Insertar(A<CorreoSeguimientoFila>._)).ReturnsLazily((CorreoSeguimientoFila f) =>
            {
                if (!yaEnTabla.Add(f.MensajeId))
                {
                    return Task.FromResult(false);
                }
                insertadas.Add(f);
                return Task.FromResult(true);
            });
            A.CallTo(() => repositorio.LeerPendientesDeMedir(A<DateTime>._)).Returns(new List<CorreoSeguimientoFila>());
        }

        private ServicioCorreosSeguimientoCompras Servicio(ILectorBuzonCompras conLector = null, bool sinLector = false)
            => new ServicioCorreosSeguimientoCompras(sinLector ? null : (conLector ?? lector), repositorio, errores.Add);

        private static CorreoCompras Seguimiento(string id, string texto = "Remitente: COSMETICOS INVENTADOS SL.")
            => new CorreoCompras
            {
                Id = id,
                MensajeId = "<" + id + "@ejemplo.test>",
                Recibido = AHORA.AddHours(-1),
                RemitenteDireccion = "Avisos@GLS-Spain.es",
                Asunto = "Su envío está en camino",
                Cuerpo = $"<p>{texto}</p><a href=\"https://www.gls-spain.es/es/recibir-paquetes/seguimiento-envio/?match=6177000{id.Length}11\">Seguir</a>",
                CuerpoEsHtml = true
            };

        private static CorreoCompras SinSeguimiento(string id) => new CorreoCompras
        {
            Id = id, MensajeId = "<" + id + "@ejemplo.test>", Recibido = AHORA, RemitenteDireccion = "facturas@proveedor-inventado.es",
            Asunto = "Factura", Cuerpo = "Adjunto la factura de octubre. Un saludo.", CuerpoEsHtml = false
        };

        [TestMethod]
        public async Task SinClaves_NoHaceNadaNiAvisa()
        {
            ResultadoPasadaCorreosSeguimiento resultado = await Servicio(sinLector: true).Pasada(AHORA);

            Assert.AreEqual(EstadoPasadaCorreosSeguimiento.SinConfiguracion, resultado.Estado);
            Assert.AreEqual(0, errores.Count, "Sin claves no ensucia ELMAH");
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinTabla_NoLeeElBuzonYLoApuntaUnaSolaVez()
        {
            A.CallTo(() => repositorio.TablasExisten()).Returns(false);

            ResultadoPasadaCorreosSeguimiento primera = await Servicio().Pasada(AHORA);
            _ = await Servicio().Pasada(AHORA.AddMinutes(30));

            Assert.AreEqual(EstadoPasadaCorreosSeguimiento.SinTabla, primera.Estado);
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).MustNotHaveHappened();
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "Issue857_ComprasCorreosSeguimiento.sql");
        }

        [TestMethod]
        public async Task Pasada_GuardaSoloLosDeSeguimientoConSuPropuestaYAvanzaElDelta()
        {
            lectura.Correos.Add(Seguimiento("m1"));
            lectura.Correos.Add(SinSeguimiento("m2"));
            lectura.SiguienteEnlace = "delta-nuevo";
            lectura.Completa = true;

            ResultadoPasadaCorreosSeguimiento resultado = await Servicio().Pasada(AHORA);

            Assert.AreEqual(EstadoPasadaCorreosSeguimiento.Hecha, resultado.Estado);
            Assert.AreEqual(2, resultado.Leidos);
            Assert.AreEqual(1, resultado.Guardados);
            CorreoSeguimientoFila fila = insertadas.Single();
            Assert.AreEqual("<m1@ejemplo.test>", fila.MensajeId);
            Assert.AreEqual("GLS", fila.Agencia);
            Assert.AreEqual("6177000211", fila.Codigo);
            Assert.AreEqual("avisos@gls-spain.es", fila.RemitenteDireccion);
            Assert.AreEqual("gls-spain.es", fila.RemitenteDominio);
            Assert.AreEqual("9001", fila.ProveedorPropuesto);
            CollectionAssert.AreEquivalent(new[] { 912001, 912007 },
                AsignadorPedidosCompra.Deserializar(fila.PedidosCandidatos).Select(c => c.Pedido).ToList());
            Assert.IsTrue(fila.Extracto.Length <= AnalizadorCorreoSeguimiento.LONGITUD_EXTRACTO);
            A.CallTo(() => lector.LeerNuevos("delta-anterior", AHORA.AddDays(-7))).MustHaveHappened();
            A.CallTo(() => repositorio.GuardarEstado(BUZON, "delta-nuevo", null)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public async Task Pasada_EsIdempotente_ElMismoCorreoDosVecesSeGuardaUnaSola()
        {
            lectura.Correos.Add(Seguimiento("m1"));
            lectura.SiguienteEnlace = "delta-nuevo";

            ResultadoPasadaCorreosSeguimiento primera = await Servicio().Pasada(AHORA);
            ResultadoPasadaCorreosSeguimiento segunda = await Servicio().Pasada(AHORA.AddMinutes(30));

            Assert.AreEqual(1, primera.Guardados);
            Assert.AreEqual(0, segunda.Guardados);
            Assert.AreEqual(1, segunda.YaGuardados);
            Assert.AreEqual(1, insertadas.Count);
        }

        [TestMethod]
        public async Task Pasada_SinNadaQueGuardar_NiSiquieraLeeLosPedidos()
        {
            lectura.Correos.Add(SinSeguimiento("m2"));
            lectura.SiguienteEnlace = "delta-nuevo";

            _ = await Servicio().Pasada(AHORA);

            A.CallTo(() => repositorio.LeerPedidosEnAire(A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.Insertar(A<CorreoSeguimientoFila>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Pasada_SiFallanLosPedidos_SeGuardaSinCandidatos()
        {
            A.CallTo(() => repositorio.LeerPedidosEnAire(A<string>._)).Throws(new Exception("timeout"));
            lectura.Correos.Add(Seguimiento("m1"));

            _ = await Servicio().Pasada(AHORA);

            Assert.IsNull(insertadas.Single().PedidosCandidatos);
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Pasada_ErrorDeGraph_LoApuntaGuardaLoLeidoYSigueDesdeLaPagina()
        {
            lectura.Correos.Add(Seguimiento("m1"));
            lectura.SiguienteEnlace = "pagina-2";
            lectura.Error = "Graph ha contestado 429 (TooManyRequests)";
            lectura.Estado = (HttpStatusCode)429;

            ResultadoPasadaCorreosSeguimiento resultado = await Servicio().Pasada(AHORA);

            Assert.AreEqual(EstadoPasadaCorreosSeguimiento.ConError, resultado.Estado);
            Assert.AreEqual(1, insertadas.Count);
            A.CallTo(() => repositorio.GuardarEstado(BUZON, "pagina-2", lectura.Error)).MustHaveHappenedOnceExactly();
            StringAssert.Contains(errores.Single().Message, "429");
        }

        [TestMethod]
        public async Task Pasada_401SinNadaLeido_NoTocaElDelta()
        {
            lectura.Error = "Graph ha contestado 401 (Unauthorized)";

            ResultadoPasadaCorreosSeguimiento resultado = await Servicio().Pasada(AHORA);

            Assert.AreEqual(EstadoPasadaCorreosSeguimiento.ConError, resultado.Estado);
            A.CallTo(() => repositorio.GuardarEstado(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Pasada_SiFallaUnaInsercion_NoAvanzaElDelta()
        {
            A.CallTo(() => repositorio.Insertar(A<CorreoSeguimientoFila>._)).Throws(new Exception("bloqueo"));
            lectura.Correos.Add(Seguimiento("m1"));
            lectura.SiguienteEnlace = "delta-nuevo";

            _ = await Servicio().Pasada(AHORA);

            A.CallTo(() => repositorio.GuardarEstado(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public async Task Pasada_410_BorraElDelta()
        {
            lectura.Reiniciar = true;
            lectura.Error = "Graph ha contestado 410 (Gone)";

            _ = await Servicio().Pasada(AHORA);

            A.CallTo(() => repositorio.GuardarEstado(BUZON, null, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Medir_ApuntaElCandidatoRecibido()
        {
            var pendiente = new CorreoSeguimientoFila
            {
                Id = 7,
                Recibido = AHORA.AddDays(-2),
                PedidosCandidatos = AsignadorPedidosCompra.Serializar(new List<CandidatoPedidoCompra>
                {
                    new CandidatoPedidoCompra { Empresa = "1", Pedido = 912001 }, new CandidatoPedidoCompra { Empresa = "1", Pedido = 912007 }
                })
            };
            A.CallTo(() => repositorio.LeerPendientesDeMedir(AHORA.AddDays(-ServicioCorreosSeguimientoCompras.DIAS_MEDICION)))
                .Returns(new List<CorreoSeguimientoFila> { pendiente });
            var albaran = new AlbaranPedidoCompra { Empresa = "1", Pedido = 912007, FechaAlbaran = AHORA.Date.AddDays(-1) };
            A.CallTo(() => repositorio.LeerAlbaranes(A<IEnumerable<CandidatoPedidoCompra>>._)).Returns(new List<AlbaranPedidoCompra> { albaran });

            ResultadoPasadaCorreosSeguimiento resultado = await Servicio().Pasada(AHORA);

            Assert.AreEqual(1, resultado.Medidos);
            A.CallTo(() => repositorio.GuardarReal(7, albaran)).MustHaveHappenedOnceExactly();
        }
    }
}
