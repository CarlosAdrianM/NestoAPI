using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta.FotosFechaEntrega;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    [TestClass]
    public class FotosFechaEntregaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 11, 11);

        private static PartidaFechaEntregaAgenciaDTO Partida(string producto, string origen, DateTime? sale, int unidades = 1,
            int? pedidoProveedor = null, DateTime? prevista = null, DateTime? supuesta = null)
        {
            return new PartidaFechaEntregaAgenciaDTO
            {
                Producto = producto,
                Origen = origen,
                PuedeSalirEl = sale,
                Unidades = unidades,
                PedidoProveedor = pedidoProveedor,
                FechaPrevistaProveedor = prevista,
                LlegadaSupuesta = supuesta
            };
        }

        private static FechaEntregaAgenciaDTO Calculo(params PartidaFechaEntregaAgenciaDTO[] partidas)
        {
            return new FechaEntregaAgenciaDTO
            {
                FechaEntregaAgencia = new DateTime(2026, 11, 20),
                FechaPrometida = new DateTime(2026, 11, 13),
                Desglose = partidas.ToList()
            };
        }

        [TestMethod]
        public void Construir_LoQueSaleDelStockDeAlgete_NoTieneFoto()
        {
            List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir("1", 10, Calculo(
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, HOY.AddDays(1)),
                Partida("B", PartidaFechaEntregaAgenciaDTO.ORIGEN_PICKING, HOY),
                Partida("C", PartidaFechaEntregaAgenciaDTO.ORIGEN_KIT, HOY.AddDays(1))), HOY);

            Assert.AreEqual(0, fotos.Count);
        }

        [TestMethod]
        public void Construir_ProductoDelProveedor_GuardaFechaPedidoYPrevista()
        {
            List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir("1", 10, Calculo(
                Partida("A ", PartidaFechaEntregaAgenciaDTO.ORIGEN_ALGETE, HOY.AddDays(1), 2),
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, HOY.AddDays(6), 3, 220894, HOY.AddDays(5))), HOY);

            Assert.AreEqual(1, fotos.Count);
            FotoFechaEntrega foto = fotos[0];
            Assert.AreEqual("A", foto.Producto);
            Assert.AreEqual(5, foto.Unidades);
            Assert.AreEqual(HOY.AddDays(6), foto.FechaSalida);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, foto.Origen);
            Assert.AreEqual(220894, foto.PedidoProveedor);
            Assert.AreEqual(HOY.AddDays(5), foto.FechaPrevistaProveedor);
            Assert.IsFalse(foto.PrevistaVencida);
            Assert.AreEqual(CasosFotoFechaEntrega.PEDIDO_ENVIADO, foto.Caso);
            Assert.AreEqual(new DateTime(2026, 11, 20), foto.FechaEntregaAgencia);
            Assert.AreEqual(new DateTime(2026, 11, 13), foto.FechaPrometidaAlCrear);
            Assert.AreEqual(HOY, foto.FechaFoto);
        }

        [TestMethod]
        public void Construir_PartidaSinFecha_MandaYLaFechaQuedaNula()
        {
            List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir("1", 10, Calculo(
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, HOY.AddDays(2)),
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, null)), HOY);

            Assert.IsNull(fotos[0].FechaSalida);
            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_SIN_STOCK, fotos[0].Origen);
            Assert.AreEqual(CasosFotoFechaEntrega.SIN_PEDIR, fotos[0].Caso);
        }

        [TestMethod]
        public void Construir_FechasCentinela2099_SonSinFecha()
        {
            var centinela = new DateTime(2099, 12, 31);
            List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir("1", 10, Calculo(
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, centinela, 1, 220894, centinela)), HOY);

            Assert.IsNull(fotos[0].FechaSalida, "31/12/2099 es «sin determinar», no una fecha");
            Assert.IsNull(fotos[0].FechaPrevistaProveedor);
            Assert.AreEqual(220894, fotos[0].PedidoProveedor);
            Assert.IsNull(ReglasFotosFechaEntrega.SinFecha(new DateTime(2100, 1, 1), HOY));
            Assert.AreEqual(HOY.Date.AddYears(1), ReglasFotosFechaEntrega.SinFecha(HOY.AddYears(1), HOY));
        }

        [TestMethod]
        public void Construir_MandaLaPartidaMasTardia()
        {
            List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir("1", 10, Calculo(
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, HOY.AddDays(3), 1, 5, HOY.AddDays(2)),
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, HOY.AddDays(4))), HOY);

            Assert.AreEqual(PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, fotos[0].Origen);
            Assert.AreEqual(CasosFotoFechaEntrega.INTERNO, fotos[0].Caso);
            Assert.AreEqual(HOY.AddDays(4), fotos[0].FechaSalida);
            Assert.AreEqual(5, fotos[0].PedidoProveedor, "Se guarda igualmente de qué pedido a proveedor depende la otra parte");
        }

        [TestMethod]
        public void Construir_PrevistaVencida_SeMarca()
        {
            List<FotoFechaEntrega> fotos = ReglasFotosFechaEntrega.Construir("1", 10, Calculo(
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, HOY.AddDays(2), 1, 5, HOY.AddDays(-3), HOY.AddDays(1))), HOY);

            Assert.IsTrue(fotos[0].PrevistaVencida);
        }

        [TestMethod]
        public void Construir_SinCalculo_NoHaceFotos()
        {
            Assert.AreEqual(0, ReglasFotosFechaEntrega.Construir("1", 10, null, HOY).Count);
        }

        private static FotoFechaEntrega Foto(DateTime dia, DateTime? sale, string origen = "Proveedor", int unidades = 1)
        {
            return new FotoFechaEntrega { Id = 7, FechaFoto = dia, FechaSalida = sale, Origen = origen, Caso = CasosFotoFechaEntrega.De(origen), Unidades = unidades, PedidoProveedor = 5 };
        }

        [TestMethod]
        public void Decidir_SinFotoAnterior_Inserta()
        {
            Assert.AreEqual(AccionFotoFechaEntrega.Insertar, ReglasFotosFechaEntrega.Decidir(null, Foto(HOY, HOY.AddDays(3))));
        }

        [TestMethod]
        public void Decidir_MismaPromesa_NoHaceNadaAunqueCambienLasUnidades()
        {
            Assert.AreEqual(AccionFotoFechaEntrega.Nada,
                ReglasFotosFechaEntrega.Decidir(Foto(HOY.AddDays(-4), HOY.AddDays(3)), Foto(HOY, HOY.AddDays(3), unidades: 9)));
        }

        [TestMethod]
        public void Decidir_CambiaLaFechaOtroDia_Inserta()
        {
            Assert.AreEqual(AccionFotoFechaEntrega.Insertar,
                ReglasFotosFechaEntrega.Decidir(Foto(HOY.AddDays(-1), HOY.AddDays(3)), Foto(HOY, HOY.AddDays(5))));
        }

        [TestMethod]
        public void Decidir_CambiaElOrigenElMismoDia_Sustituye()
        {
            Assert.AreEqual(AccionFotoFechaEntrega.Sustituir,
                ReglasFotosFechaEntrega.Decidir(Foto(HOY, HOY.AddDays(3)), Foto(HOY, HOY.AddDays(3), "Tienda")));
        }

        [TestMethod]
        public void Decidir_DeSinFechaAConFecha_Inserta()
        {
            Assert.AreEqual(AccionFotoFechaEntrega.Insertar,
                ReglasFotosFechaEntrega.Decidir(Foto(HOY.AddDays(-2), null), Foto(HOY, HOY.AddDays(3))));
        }

        [TestMethod]
        public void Percentil_PorRangoMasCercano()
        {
            var valores = new List<int> { 5, 1, 3, 2, 4 };
            Assert.AreEqual(3, ReglasFotosFechaEntrega.Percentil(valores, 0.5));
            Assert.AreEqual(4, ReglasFotosFechaEntrega.Percentil(valores, 0.8));
            Assert.IsNull(ReglasFotosFechaEntrega.Percentil(new List<int>(), 0.5));
        }

        [TestMethod]
        public void Resumir_PorOrigenYProveedor()
        {
            var filas = new List<ErrorFechaEntregaFila>
            {
                new ErrorFechaEntregaFila { OrigenPrimera = "Proveedor", ProveedorPrimera = "P1 ", ErrorPrimeraDias = 0, ErrorUltimaDias = 0, Fotos = 1 },
                new ErrorFechaEntregaFila { OrigenPrimera = "Proveedor", ProveedorPrimera = "P1", ErrorPrimeraDias = 4, ErrorUltimaDias = 1, Fotos = 3 },
                new ErrorFechaEntregaFila { OrigenPrimera = "Proveedor", ProveedorPrimera = "P1", ErrorPrimeraDias = -1, ErrorUltimaDias = 0, Fotos = 2 },
                new ErrorFechaEntregaFila { OrigenPrimera = "Proveedor", ProveedorPrimera = "P1", ErrorPrimeraDias = null, ErrorUltimaDias = 2, Fotos = 2 },
                new ErrorFechaEntregaFila { OrigenPrimera = "Tienda", ProveedorPrimera = null, ErrorPrimeraDias = 1, ErrorUltimaDias = 1, Fotos = 1 }
            };

            List<ResumenErrorFechaEntregaDTO> resumen = ReglasFotosFechaEntrega.Resumir(filas);

            Assert.AreEqual(2, resumen.Count);
            ResumenErrorFechaEntregaDTO p1 = resumen[0];
            Assert.AreEqual("P1", p1.Proveedor);
            Assert.AreEqual(4, p1.Lineas);
            Assert.AreEqual(1, p1.SinFecha);
            Assert.AreEqual(0, p1.ErrorMediano);
            Assert.AreEqual(4, p1.ErrorP80);
            Assert.AreEqual(33, p1.PorcentajeTarde);
            Assert.AreEqual(33, p1.PorcentajeAntes);
            Assert.AreEqual(0, p1.ErrorMedianoUltima);
            Assert.AreEqual(2m, p1.FotosPorLinea);
            Assert.AreEqual("Tienda", resumen[1].Origen);
        }

        [TestMethod]
        public async Task Pasada_SinTabla_NoCalculaNada()
        {
            var repositorio = A.Fake<IRepositorioFotosFechaEntrega>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(false);
            bool calculado = false;
            var servicio = new ServicioFotosFechaEntrega(repositorio, (e, p) => { calculado = true; return Task.FromResult<FechaEntregaAgenciaDTO>(null); });

            ResultadoPasadaFotosFechaEntrega resultado = await servicio.Pasada("1", HOY);

            Assert.IsFalse(calculado);
            Assert.AreEqual(0, resultado.Pedidos);
            A.CallTo(() => repositorio.LeerPedidosPendientes(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Pasada_InsertaLoNuevoSustituyeLoDeHoyYNoRepiteLoIgual()
        {
            var repositorio = A.Fake<IRepositorioFotosFechaEntrega>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerPedidosPendientes("1")).Returns(new List<int> { 10 });
            A.CallTo(() => repositorio.LeerUltimas("1", 10)).Returns(new Dictionary<string, FotoFechaEntrega>(StringComparer.OrdinalIgnoreCase)
            {
                ["B"] = new FotoFechaEntrega { Id = 1, Producto = "B", FechaFoto = HOY.AddDays(-3), FechaSalida = HOY.AddDays(2), Origen = "Tienda", Caso = CasosFotoFechaEntrega.INTERNO,
                    FechaEntregaAgencia = new DateTime(2026, 11, 20) },
                ["C"] = new FotoFechaEntrega { Id = 2, Producto = "C", FechaFoto = HOY, FechaSalida = HOY.AddDays(2), Origen = "Tienda", Caso = CasosFotoFechaEntrega.INTERNO,
                    FechaEntregaAgencia = new DateTime(2026, 11, 20) }
            });
            var servicio = new ServicioFotosFechaEntrega(repositorio, (e, p) => Task.FromResult(Calculo(
                Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_PROVEEDOR, HOY.AddDays(6), 1, 5, HOY.AddDays(5)),
                Partida("B", PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, HOY.AddDays(2)),
                Partida("C", PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, HOY.AddDays(3)))));

            ResultadoPasadaFotosFechaEntrega resultado = await servicio.Pasada("1", HOY);

            Assert.AreEqual(1, resultado.Insertadas);
            Assert.AreEqual(1, resultado.Sustituidas);
            A.CallTo(() => repositorio.Insertar(A<FotoFechaEntrega>.That.Matches(f => f.Producto == "A"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Sustituir(2, A<FotoFechaEntrega>.That.Matches(f => f.Producto == "C"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Insertar(A<FotoFechaEntrega>.That.Matches(f => f.Producto == "B"))).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Pasada_UnPedidoQueFalla_NoParaLosDemasYSeRegistraUnaVez()
        {
            var repositorio = A.Fake<IRepositorioFotosFechaEntrega>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerPedidosPendientes("1")).Returns(new List<int> { 10, 11, 12 });
            var registrados = new List<Exception>();
            var servicio = new ServicioFotosFechaEntrega(repositorio, (e, p) =>
            {
                if (p != 12)
                {
                    throw new InvalidOperationException("roto");
                }
                return Task.FromResult(Calculo(Partida("A", PartidaFechaEntregaAgenciaDTO.ORIGEN_TIENDA, HOY.AddDays(2))));
            }, registrados.Add);

            ResultadoPasadaFotosFechaEntrega resultado = await servicio.Pasada("1", HOY);

            Assert.AreEqual(2, resultado.Errores);
            Assert.AreEqual(1, resultado.Insertadas);
            Assert.AreEqual(1, registrados.Count);
        }

        private static IPrincipal Usuario(params string[] grupos)
        {
            var identidad = new ClaimsIdentity("test");
            identidad.AddClaim(new Claim(ClaimTypes.Name, "prueba"));
            return new GenericPrincipal(identidad, grupos);
        }

        [TestMethod]
        public async Task GetError_SinDireccionNiInformatica_403()
        {
            var repositorio = A.Fake<IRepositorioFotosFechaEntrega>();
            var controller = new FechasEntregaErrorController(repositorio) { User = Usuario("Ventas") };

            var respuesta = await controller.GetError() as NegotiatedContentResult<string>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            A.CallTo(() => repositorio.LeerErrores(A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetError_Direccion_DevuelveElResumen()
        {
            var repositorio = A.Fake<IRepositorioFotosFechaEntrega>();
            A.CallTo(() => repositorio.TablaExiste()).Returns(true);
            A.CallTo(() => repositorio.LeerErrores(A<DateTime>._)).Returns(new List<ErrorFechaEntregaFila>
            {
                new ErrorFechaEntregaFila { OrigenPrimera = "Proveedor", ProveedorPrimera = "P1", ErrorPrimeraDias = 2, Fotos = 1 }
            });
            var controller = new FechasEntregaErrorController(repositorio) { User = Usuario(NestoAPI.Models.Constantes.GruposSeguridad.DIRECCION) };

            var respuesta = await controller.GetError(3) as OkNegotiatedContentResult<List<ResumenErrorFechaEntregaDTO>>;

            Assert.IsNotNull(respuesta);
            Assert.AreEqual(1, respuesta.Content.Count);
            Assert.AreEqual(2, respuesta.Content[0].ErrorMediano);
        }

        [TestMethod]
        public async Task GetError_MesesFueraDeRango_400()
        {
            var controller = new FechasEntregaErrorController(A.Fake<IRepositorioFotosFechaEntrega>())
            {
                User = Usuario(NestoAPI.Models.Constantes.GruposSeguridad.INFORMATICA)
            };

            Assert.IsInstanceOfType(await controller.GetError(0), typeof(BadRequestErrorMessageResult));
        }
    }
}
