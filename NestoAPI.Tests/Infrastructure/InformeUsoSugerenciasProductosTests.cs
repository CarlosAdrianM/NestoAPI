using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#669: informe de uso de las sugerencias y «acierto sin ofrecer» (sin pulsar nada y el producto acabó en el
    /// pedido: la predicción acertó, pero no consta que se ofreciera; nunca es «Anadido»).
    /// </summary>
    [TestClass]
    public class InformeUsoSugerenciasProductosTests
    {
        private static readonly DateTime DESDE = new DateTime(2026, 10, 1);
        private static readonly DateTime HASTA = new DateTime(2026, 10, 22);

        private static FilaUsoSugerencia Fila(Guid sesion, string vendedor, string aplicacion, int? pedido, int? id,
            string respuesta = null, bool? entro = null, string texto = "Lo compra cada ~30 días")
            => new FilaUsoSugerencia
            {
                SesionId = sesion,
                Vendedor = vendedor,
                Aplicacion = aplicacion,
                Pedido = pedido,
                IdMostrada = id,
                Respuesta = respuesta,
                EntroEnPedido = entro,
                Texto = id.HasValue ? texto : null
            };

        [TestMethod]
        public void EsAciertoSinOfrecer_SoloSinRespuestaOSustituidaYEnElPedido()
        {
            Assert.IsTrue(InformeUsoSugerenciasProductos.EsAciertoSinOfrecer(null, true));
            Assert.IsTrue(InformeUsoSugerenciasProductos.EsAciertoSinOfrecer(RespuestasSugerencia.SUSTITUIDA, true));
            Assert.IsFalse(InformeUsoSugerenciasProductos.EsAciertoSinOfrecer(null, false));
            Assert.IsFalse(InformeUsoSugerenciasProductos.EsAciertoSinOfrecer(null, null));
            Assert.IsFalse(InformeUsoSugerenciasProductos.EsAciertoSinOfrecer(RespuestasSugerencia.ANADIDO, true), "Una ✓ es añadida, no acierto sin ofrecer");
            Assert.IsFalse(InformeUsoSugerenciasProductos.EsAciertoSinOfrecer(RespuestasSugerencia.NO_LE_INTERESA, true));
            Assert.IsFalse(InformeUsoSugerenciasProductos.EsAciertoSinOfrecer(RespuestasSugerencia.DESCARTADO_VENDEDOR, true));
        }

        [TestMethod]
        public void Calcular_CuentaPorVendedorYAplicacionYElTotal()
        {
            Guid s1 = Guid.NewGuid(), s2 = Guid.NewGuid(), s3 = Guid.NewGuid(), s4 = Guid.NewGuid();
            var filas = new List<FilaUsoSugerencia>
            {
                // JE en Nesto, sesión con pedido: una ✓, una sin tocar que entró, una sin tocar que no, una 🚫 que entró igual
                Fila(s1, "JE ", "Nesto", 928235, 1, RespuestasSugerencia.ANADIDO, true),
                Fila(s1, "JE ", "Nesto", 928235, 2, null, true),
                Fila(s1, "JE ", "Nesto", 928235, 3, null, false),
                Fila(s1, "JE ", "Nesto", 928235, 4, RespuestasSugerencia.DESCARTADO_VENDEDOR, true),
                // JE en Nesto, sesión sin pedido: ✗, 🚫 provisional y una sustituida
                Fila(s2, "JE", "Nesto", null, 5, RespuestasSugerencia.NO_LE_INTERESA),
                Fila(s2, "JE", "Nesto", null, 6, RespuestasSugerencia.NO_LE_INTERESA, texto: RespuestasSugerencia.MARCA_DESCARTADO_VENDEDOR + "Va en el"),
                Fila(s2, "JE", "Nesto", null, 7, RespuestasSugerencia.SUSTITUIDA),
                // JE en NestoApp: sustituida provisional que acabó en el pedido (acierto)
                Fila(s3, "JE", "NestoApp", 928300, 8, null, true, RespuestasSugerencia.MARCA_SUSTITUIDA + "Lo compra"),
                // Sin vendedor y sin ninguna sugerencia enseñada
                Fila(s4, null, "NestoApp", null, null)
            };

            UsoSugerenciasDTO uso = InformeUsoSugerenciasProductos.Calcular(filas, DESDE, HASTA);

            Assert.AreEqual(3, uso.PorVendedor.Count);
            UsoSugerenciasGrupoDTO nesto = uso.PorVendedor.Single(g => g.Vendedor == "JE" && g.Aplicacion == "Nesto");
            Assert.AreEqual(2, nesto.Sesiones);
            Assert.AreEqual(1, nesto.SesionesConPedido);
            Assert.AreEqual(7, nesto.Mostradas);
            Assert.AreEqual(4, nesto.MostradasConPedido);
            Assert.AreEqual(1, nesto.Anadidas);
            Assert.AreEqual(1, nesto.NoLeInteresa, "La 🚫 provisional (NoLeInteresa con la marca) no es un ✗ del cliente");
            Assert.AreEqual(2, nesto.Quitadas);
            Assert.AreEqual(1, nesto.Sustituidas);
            Assert.AreEqual(1, nesto.AciertoSinOfrecer, "La 🚫 que entró en el pedido no es acierto sin ofrecer");
            Assert.AreEqual(2, nesto.Aciertos);
            Assert.AreEqual(28.6m, nesto.PorcentajeAcierto);          // 2 / 7
            Assert.AreEqual(50.0m, nesto.PorcentajeAciertoConPedido); // 2 / 4

            UsoSugerenciasGrupoDTO app = uso.PorVendedor.Single(g => g.Vendedor == "JE" && g.Aplicacion == "NestoApp");
            Assert.AreEqual(1, app.AciertoSinOfrecer);
            Assert.AreEqual(1, app.Sustituidas);
            Assert.AreEqual(100m, app.PorcentajeAcierto);

            UsoSugerenciasGrupoDTO sinVendedor = uso.PorVendedor.Single(g => g.Vendedor == InformeUsoSugerenciasProductos.SIN_VENDEDOR);
            Assert.AreEqual(1, sinVendedor.Sesiones);
            Assert.AreEqual(0, sinVendedor.Mostradas);
            Assert.IsNull(sinVendedor.PorcentajeAcierto);

            Assert.AreEqual(4, uso.Total.Sesiones);
            Assert.AreEqual(8, uso.Total.Mostradas);
            Assert.AreEqual(2, uso.Total.AciertoSinOfrecer);
            Assert.AreEqual(3, uso.Total.Aciertos);
            Assert.AreEqual(37.5m, uso.Total.PorcentajeAcierto);
            Assert.AreEqual(DESDE, uso.Desde);
            Assert.AreEqual(HASTA, uso.Hasta);
        }

        [TestMethod]
        public void Calcular_SinFilas_TotalACero()
        {
            UsoSugerenciasDTO uso = InformeUsoSugerenciasProductos.Calcular(null, DESDE, HASTA);

            Assert.AreEqual(0, uso.PorVendedor.Count);
            Assert.AreEqual(0, uso.Total.Sesiones);
            Assert.IsNull(uso.Total.PorcentajeAcierto);
        }

        [TestMethod]
        public void SqlUso_SoloLee()
        {
            string sql = InformeUsoSugerenciasProductos.SQL_USO.ToUpperInvariant();
            StringAssert.StartsWith(sql.Trim(), "SELECT");
            Assert.IsFalse(sql.Contains("INSERT") || sql.Contains("UPDATE") || sql.Contains("DELETE"));
        }

        /// <summary>
        /// Lo que excluye 60 días (y lo que leerá el modelo 2 como respuesta) es la columna Respuesta: el acierto sin ofrecer
        /// (EntroEnPedido sin respuesta) no debe aparecer ahí.
        /// </summary>
        [TestMethod]
        public void Rechazados_NoMiranSiEntroEnElPedido()
        {
            Assert.IsFalse(SugerenciasProductosSql.SQL_RECHAZADOS.Contains("EntroEnPedido"));
            StringAssert.Contains(SugerenciasProductosSql.SQL_RECHAZADOS, "m.Respuesta IN ('NoLeInteresa', 'DescartadoVendedor')");
        }

        [TestMethod]
        public async Task EnlazarPedido_MarcaQueEntroPeroNuncaGuardaUnaRespuesta()
        {
            var repositorio = A.Fake<IRepositorioSugerenciasProductos>();
            Guid sesion = Guid.NewGuid();
            A.CallTo(() => repositorio.ExistenTablas()).Returns(Task.FromResult(true));
            A.CallTo(() => repositorio.LeerSesion(sesion)).Returns(Task.FromResult(new SesionSugerencias { Id = sesion, Empresa = "1", Cliente = "26552" }));
            A.CallTo(() => repositorio.LeerMostradas(sesion)).Returns(Task.FromResult(new List<SugerenciaMostrada>
            {
                new SugerenciaMostrada { Id = 11, SesionId = sesion, Producto = "SUGERIDO" },
                new SugerenciaMostrada { Id = 12, SesionId = sesion, Producto = "OTRO" }
            }));
            var pedido = new PedidoVentaDTO
            {
                numero = 928235,
                IdSesionSugerencias = sesion,
                Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { Producto = "SUGERIDO", Cantidad = 2, tipoLinea = 1 } }
            };

            await new ServicioSugerenciasProductos(repositorio, registrarError: ex => throw ex).EnlazarPedido(pedido);

            A.CallTo(() => repositorio.MarcarEntradaEnPedido(11, true, 2)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.GuardarRespuesta(A<int>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarDescartadoVendedorProvisional(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarSustituida(A<int>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.GuardarSustituidaProvisional(A<int>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task GetUso_SinSerDireccionNiInformatica_Forbidden()
        {
            SugerenciasProductosController controller = Controller("NUEVAVISION\\Vendedores", (d, h) => Task.FromResult(new List<FilaUsoSugerencia>()));

            var resultado = await controller.GetUso() as StatusCodeResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.StatusCode);
        }

        [TestMethod]
        public async Task GetUso_PorDefecto_UltimosTreintaDiasConHoyIncluido()
        {
            DateTime? desdeLeido = null, hastaLeido = null;
            SugerenciasProductosController controller = Controller("NUEVAVISION\\Dirección", (d, h) =>
            {
                desdeLeido = d;
                hastaLeido = h;
                return Task.FromResult(new List<FilaUsoSugerencia> { Fila(Guid.NewGuid(), "JE", "Nesto", 1, 1, null, true) });
            });

            var resultado = await controller.GetUso() as OkNegotiatedContentResult<UsoSugerenciasDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(new DateTime(2026, 9, 21), desdeLeido);
            Assert.AreEqual(new DateTime(2026, 10, 22), hastaLeido);
            Assert.AreEqual(1, resultado.Content.Total.AciertoSinOfrecer);
        }

        [TestMethod]
        public async Task GetUso_HastaEsInclusivoYNoPuedeSerAnteriorADesde()
        {
            DateTime? hastaLeido = null;
            SugerenciasProductosController controller = Controller("NUEVAVISION\\Informatica", (d, h) =>
            {
                hastaLeido = h;
                return Task.FromResult(new List<FilaUsoSugerencia>());
            });

            Assert.IsInstanceOfType(await controller.GetUso(new DateTime(2026, 10, 10), new DateTime(2026, 10, 10)), typeof(OkNegotiatedContentResult<UsoSugerenciasDTO>));
            Assert.AreEqual(new DateTime(2026, 10, 11), hastaLeido);
            Assert.IsInstanceOfType(await controller.GetUso(new DateTime(2026, 10, 10), new DateTime(2026, 10, 9)), typeof(BadRequestErrorMessageResult));
        }

        private static SugerenciasProductosController Controller(string grupo, Func<DateTime, DateTime, Task<List<FilaUsoSugerencia>>> leer)
        {
            var controller = new SugerenciasProductosController(null, null, () => new DateTime(2026, 10, 21, 10, 0, 0))
            {
                LeerFilasUso = leer,
                Request = new System.Net.Http.HttpRequestMessage(),
                Configuration = new System.Web.Http.HttpConfiguration()
            };
            controller.User = new GenericPrincipal(new GenericIdentity("NUEVAVISION\\Carlos"), new[] { grupo });
            return controller;
        }
    }
}
