using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// «Dónde está lo que falta» (19/11/26, pedido 930942): David pasó el pedido; faltaba 1 unidad de la 43818, que salió de Alcobendas en la
    /// reposición 81463 el 18/11 a las 9:37, la recogió Miguel a las 12:53 y no se recibió en Algete hasta el 19/11 a las 8:06. A las 19:22
    /// del 18 no tenía forma de saberlo.
    /// </summary>
    [TestClass]
    public class DondeEstaLoQueFaltaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 18, 19, 22, 0);

        private IServicioFechaEntregaAgencia motor;
        private ILectorLineasDondeEstaLoQueFalta lector;
        private List<string> llamadasSeguimiento;
        private Dictionary<int, SeguimientoReposicionDTO> seguimientos;
        private bool fallaSeguimiento;

        [TestInitialize]
        public void Preparar()
        {
            motor = A.Fake<IServicioFechaEntregaAgencia>();
            lector = A.Fake<ILectorLineasDondeEstaLoQueFalta>();
            llamadasSeguimiento = new List<string>();
            seguimientos = new Dictionary<int, SeguimientoReposicionDTO>();
            fallaSeguimiento = false;
            A.CallTo(() => lector.LeerLineas("1", 930942)).Returns(new List<LineaDondeEstaLoQueFalta>
            {
                new LineaDondeEstaLoQueFalta { Producto = "43818", Nombre = "MULTIVIT GLOW CREMA RICA", Estado = -1, TipoLinea = 1 },
                new LineaDondeEstaLoQueFalta { Producto = "17404", Nombre = "CHAMPÚ", Estado = -1, TipoLinea = 1 }
            });
        }

        private ServicioDondeEstaLoQueFalta Servicio() => new ServicioDondeEstaLoQueFalta(motor, lector, (empresa, traspasos) =>
        {
            llamadasSeguimiento.Add(string.Join(",", traspasos.OrderBy(t => t)));
            if (fallaSeguimiento)
            {
                throw new InvalidOperationException("BD caída");
            }
            return Task.FromResult(seguimientos);
        }, () => HOY);

        private void Motor(params PartidaFechaEntregaAgenciaDTO[] partidas) =>
            A.CallTo(() => motor.CalcularPedido("1", 930942)).Returns(new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = new DateTime(2026, 11, 19),
                Desglose = partidas.ToList()
            });

        private static PartidaFechaEntregaAgenciaDTO EnCamino(int traspaso = 81463, string producto = "43818", int unidades = 1) => new PartidaFechaEntregaAgenciaDTO
        {
            Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO, Producto = producto, Unidades = unidades, TraspasoEnCamino = traspaso, TiendaEnCamino = "ALC",
            PuedeSalirEl = new DateTime(2026, 11, 19), Certeza = PartidaFechaEntregaAgenciaDTO.CERTEZA_CASI_SEGURA
        };

        private static PartidaFechaEntregaAgenciaDTO EnAlgete(string producto = "17404") => new PartidaFechaEntregaAgenciaDTO
        {
            Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, Producto = producto, Unidades = 2, PuedeSalirEl = new DateTime(2026, 11, 19)
        };

        /// <summary>El seguimiento de la 81463 a las 19:22 del 18/11: preparada por Paloma, recogida por Miguel, en su furgoneta.</summary>
        private static SeguimientoReposicionDTO Seguimiento81463(bool recibida = false)
        {
            var dto = new SeguimientoReposicionDTO { Empresa = "1", Traspaso = 81463, Origen = "ALC", Destino = "ALG" };
            dto.Eventos.Add(new EventoSeguimientoReposicionDTO { Tipo = EventoSeguimientoReposicionDTO.PREPARADA, FechaHora = new DateTime(2026, 11, 18, 9, 37, 0), Usuario = "Paloma" });
            dto.Eventos.Add(new EventoSeguimientoReposicionDTO { Tipo = EventoSeguimientoReposicionDTO.RECOGIDA, FechaHora = new DateTime(2026, 11, 18, 12, 53, 0), Usuario = "Miguel" });
            var bulto = new BultoSeguimientoReposicionDTO { Codigo = "R81463-1", Bulto = 1, Total = 1 };
            bulto.Eventos.Add(new EventoSeguimientoReposicionDTO { Tipo = EventoSeguimientoReposicionDTO.ETIQUETADO, FechaHora = new DateTime(2026, 11, 18, 9, 37, 0), Usuario = "Paloma" });
            bulto.Eventos.Add(new EventoSeguimientoReposicionDTO { Tipo = TiposMovimientoBulto.RECOGIDO, FechaHora = new DateTime(2026, 11, 18, 12, 53, 0), Usuario = "Miguel", Origen = OrigenesMovimientoBulto.REPARTIDOR });
            if (recibida)
            {
                bulto.Eventos.Add(new EventoSeguimientoReposicionDTO { Tipo = EventoSeguimientoReposicionDTO.RECIBIDO, FechaHora = new DateTime(2026, 11, 19, 8, 6, 0), Usuario = "Alfredo" });
                dto.Eventos.Add(new EventoSeguimientoReposicionDTO { Tipo = EventoSeguimientoReposicionDTO.RECIBIDA, FechaHora = new DateTime(2026, 11, 19, 8, 6, 0), Usuario = "Alfredo" });
            }
            dto.Bultos.Add(bulto);
            return dto;
        }

        [TestMethod]
        public async Task Caso930942_LaUnidadQueVieneEnLaReposicion_ConQuienLaRecogioYCuando()
        {
            Motor(EnCamino(), EnAlgete());
            seguimientos[81463] = Seguimiento81463();

            DondeEstaLoQueFaltaDTO r = await Servicio().Calcular("1", 930942);

            Assert.IsFalse(r.TodoEnAlgete);
            Assert.AreEqual("Falta 1 producto en Algete.", r.Mensaje);
            ProductoQueFaltaDTO p = r.Productos.Single();
            Assert.AreEqual("43818 MULTIVIT GLOW CREMA RICA (1 ud.): viene en la reposición 81463 de Alcobendas, recogida por Miguel el miércoles 18/11 a las 12:53; " +
                "pendiente de recibir en Algete.", p.Texto);
            SituacionQueFaltaDTO s = p.Situaciones.Single();
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_EN_CAMINO, s.Origen);
            Assert.AreEqual(81463, s.Traspaso);
            Assert.AreEqual("ALC", s.Tienda);
            Assert.AreEqual(new DateTime(2026, 11, 18, 9, 37, 0), s.Seguimiento.Preparada);
            Assert.AreEqual("Miguel", s.Seguimiento.RecogidaPor);
            Assert.AreEqual(new DateTime(2026, 11, 18, 12, 53, 0), s.Seguimiento.Recogida);
            Assert.AreEqual("Miguel", s.Seguimiento.EnFurgonetaDe);
            Assert.IsTrue(s.Seguimiento.PendienteDeRecibir);
            CollectionAssert.AreEqual(new[] { "81463" }, llamadasSeguimiento);
        }

        [TestMethod]
        public async Task SiFallaElSeguimiento_NoLanza_YLaFraseSaleSinElDetalleDelRepartidor()
        {
            Motor(EnCamino());
            fallaSeguimiento = true;

            DondeEstaLoQueFaltaDTO r = await Servicio().Calcular("1", 930942);

            Assert.AreEqual("43818 MULTIVIT GLOW CREMA RICA (1 ud.): viene en la reposición 81463 de Alcobendas; pendiente de recibir en Algete.", r.Productos.Single().Texto);
            Assert.IsNull(r.Productos.Single().Situaciones.Single().Seguimiento);
        }

        [TestMethod]
        public async Task VariasReposiciones_UnaSolaLecturaDelSeguimiento()
        {
            Motor(EnCamino(81463, "43818"), EnCamino(81470, "17404"), EnCamino(81463, "40010"));

            await Servicio().Calcular("1", 930942);

            CollectionAssert.AreEqual(new[] { "81463,81470" }, llamadasSeguimiento);
        }

        [TestMethod]
        public async Task SinNadaDeCamino_NiSePreguntaPorElSeguimiento()
        {
            Motor(EnAlgete());

            await Servicio().Calcular("1", 930942);

            Assert.AreEqual(0, llamadasSeguimiento.Count);
        }

        [TestMethod]
        public async Task TodoEnAlgete_ConPicking_ListaVaciaYDiceElPicking()
        {
            Motor(EnAlgete());
            A.CallTo(() => lector.LeerLineas("1", 930942)).Returns(new List<LineaDondeEstaLoQueFalta>
            {
                new LineaDondeEstaLoQueFalta { Producto = "17404", Estado = 1, TipoLinea = 1, Picking = 100120 },
                new LineaDondeEstaLoQueFalta { Producto = "PORTES", Estado = 1, TipoLinea = 2, Picking = 0 }
            });

            DondeEstaLoQueFaltaDTO r = await Servicio().Calcular("1", 930942);

            Assert.IsTrue(r.TodoEnAlgete);
            Assert.AreEqual(0, r.Productos.Count);
            Assert.AreEqual("Todo está en Algete: tiene el picking 100120.", r.Mensaje);
            CollectionAssert.AreEqual(new[] { 100120 }, r.Pickings);
        }

        [TestMethod]
        public async Task TodoEnAlgete_SinPicking_LoDice()
        {
            Motor(EnAlgete());

            Assert.AreEqual("Todo está en Algete; aún no tiene picking.", (await Servicio().Calcular("1", 930942)).Mensaje);
        }

        [TestMethod]
        public async Task NadaPendiente_LoDice()
        {
            Motor();
            A.CallTo(() => lector.LeerLineas("1", 930942)).Returns(new List<LineaDondeEstaLoQueFalta>
            {
                new LineaDondeEstaLoQueFalta { Producto = "17404", Estado = 4, TipoLinea = 1 }
            });

            Assert.AreEqual(ReglasDondeEstaLoQueFalta.NADA_PENDIENTE, (await Servicio().Calcular("1", 930942)).Mensaje);
        }

        [TestMethod]
        public async Task PedidoQueNoExiste_Null()
        {
            A.CallTo(() => motor.CalcularPedido("1", 1)).Returns(Task.FromResult<FechaEntregaAgenciaDTO>(null));

            Assert.IsNull(await Servicio().Calcular("1", 1));
        }

        // ---------- Textos (puros) ----------

        private static string Texto(PartidaFechaEntregaAgenciaDTO p, SeguimientoQueFaltaDTO s = null) =>
            ReglasDondeEstaLoQueFalta.TextoSituacion(p, s, false, HOY);

        [TestMethod]
        public void Texto_EnUnaTienda_ConLaLlegadaDeLaReposicion()
        {
            var p = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Producto = "43818", Unidades = 1, Tienda = "REI", LlegaAAlgete = new DateTime(2026, 11, 23, 10, 30, 0)
            };

            Assert.AreEqual("está en Reina y vendrá en la próxima reposición, que suele llegar a Algete el lunes 23/11 a las 10:30", Texto(p));
        }

        [TestMethod]
        public void Texto_DelProveedor_ConPedidoYPrevista_Vencida_YConSeguimientoDeLaAgencia()
        {
            var prevista = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, Producto = "43818", Unidades = 1, PedidoProveedor = 220923, FechaPrevistaProveedor = new DateTime(2026, 11, 20)
            };
            var vencida = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, Producto = "43818", Unidades = 1, PedidoProveedor = 220923,
                FechaPrevistaProveedor = new DateTime(2026, 11, 16), LlegadaSupuesta = new DateTime(2026, 11, 19)
            };
            var conAgencia = new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, Producto = "43818", Unidades = 1, PedidoProveedor = 220923,
                FechaPrevistaProveedor = new DateTime(2026, 11, 20), ConSeguimientoAgencia = true, AgenciaSeguimiento = "UPS"
            };

            Assert.AreEqual("viene del proveedor en el pedido de compra 220923, con llegada prevista el viernes 20/11", Texto(prevista));
            Assert.AreEqual("viene del proveedor en el pedido de compra 220923; la llegada prevista era el lunes 16/11 y aún no ha llegado " +
                "(suponemos que llega el jueves 19/11)", Texto(vencida));
            Assert.AreEqual("viene del proveedor en el pedido de compra 220923, ya enviado por UPS, que prevé entregarlo el viernes 20/11", Texto(conAgencia));
        }

        [TestMethod]
        public void Texto_SinFecha_ConYSinOrientativa()
        {
            var sinNada = new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, Producto = "43818", Unidades = 1 };
            var conMotivo = new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, Producto = "43818", Unidades = 1,
                MotivoSinFecha = "no hay stock ni fecha de llegada." };
            var orientativa = new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, Producto = "43818", Unidades = 1,
                FechaOrientativa = new DateTime(2026, 11, 24) };

            Assert.AreEqual("no hay stock ni fecha de llegada", Texto(sinNada));
            Assert.AreEqual("sin fecha: no hay stock ni fecha de llegada", Texto(conMotivo));
            StringAssert.StartsWith(Texto(orientativa), "sin fecha todavía: aún no hay pedido al proveedor que lo cubra; estimamos que podría salir sobre el martes 24/11");
        }

        [TestMethod]
        public void Texto_PackQueSeMonta_YKit()
        {
            var montaSinFalta = new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_MONTAJE, Producto = "44376", Unidades = 1 };
            var kit = new PartidaFechaEntregaAgenciaDTO { Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT, Producto = "40010", Unidades = 1, Kit = "44376" };

            Assert.AreEqual("es un pack que se monta en Algete con sus componentes, que ya están allí", Texto(montaSinFalta));
            Assert.AreEqual("se saca desmontando el kit 44376 en Algete", Texto(kit));
        }

        [TestMethod]
        public void Seguimiento_PreparadaSinRecoger_LaLlevaUnCompanero_EnLaFurgonetaDeOtro_YRecibida()
        {
            var preparada = new SeguimientoQueFaltaDTO { Preparada = new DateTime(2026, 11, 18, 9, 37, 0), PendienteDeRecibir = true };
            var companero = new SeguimientoQueFaltaDTO { LaLleva = "Manuel", Recogida = new DateTime(2026, 11, 18, 13, 10, 0), PendienteDeRecibir = true };
            var otro = new SeguimientoQueFaltaDTO { Recogida = new DateTime(2026, 11, 18, 12, 53, 0), RecogidaPor = "Miguel", EnFurgonetaDe = "Andre", PendienteDeRecibir = true };
            var descargada = new SeguimientoQueFaltaDTO { Recogida = new DateTime(2026, 11, 18, 12, 53, 0), RecogidaPor = "Miguel", Descargada = true, PendienteDeRecibir = true };
            var recibida = new SeguimientoQueFaltaDTO { Recibida = new DateTime(2026, 11, 19, 8, 6, 0) };

            Assert.AreEqual(", preparada el miércoles 18/11 a las 09:37; pendiente de que la recoja el repartidor", ReglasDondeEstaLoQueFalta.TextoSeguimiento(preparada, HOY));
            Assert.AreEqual(", la lleva Manuel desde el miércoles 18/11 a las 13:10; pendiente de recibir en Algete", ReglasDondeEstaLoQueFalta.TextoSeguimiento(companero, HOY));
            Assert.AreEqual(", recogida por Miguel el miércoles 18/11 a las 12:53; está en la furgoneta de Andre; pendiente de recibir en Algete",
                ReglasDondeEstaLoQueFalta.TextoSeguimiento(otro, HOY));
            Assert.AreEqual(", recogida por Miguel el miércoles 18/11 a las 12:53; descargada en Algete; pendiente de recibir en Algete",
                ReglasDondeEstaLoQueFalta.TextoSeguimiento(descargada, HOY));
            Assert.AreEqual(", recibida en Algete el jueves 19/11 a las 08:06", ReglasDondeEstaLoQueFalta.TextoSeguimiento(recibida, HOY));
        }

        [TestMethod]
        public void Resumir_ElBultoRecibido_YaNoEstaPendiente()
        {
            SeguimientoQueFaltaDTO s = ReglasDondeEstaLoQueFalta.Resumir(Seguimiento81463(recibida: true));

            Assert.IsFalse(s.PendienteDeRecibir);
            Assert.AreEqual(new DateTime(2026, 11, 19, 8, 6, 0), s.Recibida);
            Assert.IsNull(s.EnFurgonetaDe, "el último movimiento del bulto es la recepción");
            Assert.AreEqual(1, s.Bultos);
            Assert.AreEqual(1, s.BultosRecogidos);
        }

        [TestMethod]
        public async Task UnProductoRepartidoEnDosSitios_UnaFrase_ConCadaParte()
        {
            Motor(EnCamino(81463, "43818", 1), new PartidaFechaEntregaAgenciaDTO
            {
                Origen = PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, Producto = "43818", Unidades = 2, Tienda = "REI"
            });

            ProductoQueFaltaDTO p = (await Servicio().Calcular("1", 930942)).Productos.Single();

            Assert.AreEqual(3, p.Unidades);
            Assert.AreEqual("43818 MULTIVIT GLOW CREMA RICA (3 uds.): 1 ud.: viene en la reposición 81463 de Alcobendas; pendiente de recibir en Algete; " +
                "2 uds.: están en Reina y vendrán en la próxima reposición.", p.Texto);
        }

        // ---------- Controlador ----------

        private static DondeEstaLoQueFaltaController Controlador(IServicioDondeEstaLoQueFalta servicio, params Claim[] claims) =>
            new DondeEstaLoQueFaltaController(servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration(),
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "NUEVAVISION\\David") }.Concat(claims), "prueba"))
            };

        [TestMethod]
        public async Task Controlador_ClienteDeLaTienda_403_YNoCalcula()
        {
            var servicio = A.Fake<IServicioDondeEstaLoQueFalta>();

            IHttpActionResult r = await Controlador(servicio, new Claim("cliente", "15191")).GetDondeEstaLoQueFalta("1", 930942);

            Assert.AreEqual(HttpStatusCode.Forbidden, ((NegotiatedContentResult<HttpError>)r).StatusCode);
            A.CallTo(() => servicio.Calcular(A<string>._, A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Controlador_PedidoQueNoExiste_404_YElQueExiste_200()
        {
            var servicio = A.Fake<IServicioDondeEstaLoQueFalta>();
            A.CallTo(() => servicio.Calcular("1", 1)).Returns(Task.FromResult<DondeEstaLoQueFaltaDTO>(null));
            A.CallTo(() => servicio.Calcular("1", 930942)).Returns(new DondeEstaLoQueFaltaDTO { Pedido = 930942 });

            Assert.IsInstanceOfType(await Controlador(servicio).GetDondeEstaLoQueFalta("1", 1), typeof(NotFoundResult));
            Assert.AreEqual(930942, ((OkNegotiatedContentResult<DondeEstaLoQueFaltaDTO>)await Controlador(servicio).GetDondeEstaLoQueFalta("1", 930942)).Content.Pedido);
        }
    }
}
