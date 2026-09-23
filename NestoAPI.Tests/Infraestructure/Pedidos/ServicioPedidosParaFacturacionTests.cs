using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infraestructure.Pedidos
{
    /// <summary>
    /// NestoAPI#313 (23/09/26): reescrito contra el servicio actual. El fichero anterior probaba un enum
    /// (TipoRutaFacturacion) y un filtro de visto bueno que ya no existen (el tipo de ruta sale de
    /// TipoRutaFactory y el visto bueno se valida al procesar, no al seleccionar) y su DbSet falso estaba
    /// vacío, así que no comprobaba nada.
    /// </summary>
    [TestClass]
    public class ServicioPedidosParaFacturacionTests
    {
        private static readonly DateTime Hoy = new DateTime(2026, 9, 23);

        private NVEntities db;
        private List<CabPedidoVta> pedidos;
        private ServicioPedidosParaFacturacion servicio;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            pedidos = new List<CabPedidoVta>();
            A.CallTo(() => db.CabPedidoVtas).Returns(DbSetCon(pedidos));
            servicio = new ServicioPedidosParaFacturacion(db);
        }

        private static DbSet<T> DbSetCon<T>(List<T> datos) where T : class
        {
            var fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            // Include(...) de EF acaba en DbQuery.Include(string): que devuelva el mismo DbSet con datos.
            A.CallTo(() => fake.Include(A<string>._)).Returns(fake);
            return fake;
        }

        private static CabPedidoVta Pedido(int numero, string ruta, DateTime fecha, params LinPedidoVta[] lineas) => new CabPedidoVta
        {
            Empresa = "1",
            Número = numero,
            Ruta = ruta,
            Fecha = fecha,
            LinPedidoVtas = lineas.ToList()
        };

        private static LinPedidoVta Linea(short estado, int? picking, DateTime fechaEntrega) => new LinPedidoVta
        {
            Estado = estado,
            Picking = picking,
            Fecha_Entrega = fechaEntrega
        };

        private static LinPedidoVta LineaLista() => Linea(Constantes.EstadosLineaVenta.EN_CURSO, 7, Hoy);

        #region Constructor y parámetros

        [TestMethod]
        public void Constructor_ConDbNull_LanzaArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new ServicioPedidosParaFacturacion(null));
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_TipoRutaVacio_LanzaArgumentException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.ObtenerPedidosParaFacturar(" ", Hoy));
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_TipoRutaDesconocido_LanzaArgumentException()
        {
            await Assert.ThrowsExceptionAsync<ArgumentException>(() => servicio.ObtenerPedidosParaFacturar("NO_EXISTE", Hoy));
        }

        #endregion

        #region Filtros

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_RutaPropia_SoloDevuelveSusRutas16YAT()
        {
            pedidos.AddRange(new[]
            {
                Pedido(101, "16", Hoy, LineaLista()),
                Pedido(102, "AT", Hoy, LineaLista()),
                Pedido(103, "FW", Hoy, LineaLista()),
                Pedido(104, "00", Hoy, LineaLista()),
                Pedido(105, "GLV", Hoy, LineaLista())
            });

            List<CabPedidoVta> resultado = await servicio.ObtenerPedidosParaFacturar("PROPIA", Hoy);

            CollectionAssert.AreEquivalent(new[] { 101, 102 }, resultado.Select(p => p.Número).ToList());
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_RutaAgencia_SoloDevuelveFWY00()
        {
            pedidos.AddRange(new[]
            {
                Pedido(101, "16", Hoy, LineaLista()),
                Pedido(103, "FW", Hoy, LineaLista()),
                Pedido(104, "00", Hoy, LineaLista())
            });

            List<CabPedidoVta> resultado = await servicio.ObtenerPedidosParaFacturar("agencia", Hoy);

            CollectionAssert.AreEquivalent(new[] { 103, 104 }, resultado.Select(p => p.Número).ToList(), "El id del tipo no distingue mayúsculas");
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_IncluyeEnCursoYAlbaran_ExcluyePendientesYFacturados()
        {
            pedidos.AddRange(new[]
            {
                Pedido(201, "16", Hoy, Linea(Constantes.EstadosLineaVenta.EN_CURSO, 1, Hoy)),
                Pedido(202, "16", Hoy, Linea(Constantes.EstadosLineaVenta.ALBARAN, 1, Hoy)),
                Pedido(203, "16", Hoy, Linea(Constantes.EstadosLineaVenta.PENDIENTE, 1, Hoy)),
                Pedido(204, "16", Hoy, Linea(Constantes.EstadosLineaVenta.FACTURA, 1, Hoy))
            });

            List<CabPedidoVta> resultado = await servicio.ObtenerPedidosParaFacturar("PROPIA", Hoy);

            CollectionAssert.AreEquivalent(new[] { 201, 202 }, resultado.Select(p => p.Número).ToList(),
                "El albarán sin factura también se incluye (re-facturar NRM con albarán)");
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_ExcluyeLineasSinPicking()
        {
            pedidos.AddRange(new[]
            {
                Pedido(301, "16", Hoy, Linea(Constantes.EstadosLineaVenta.EN_CURSO, null, Hoy)),
                Pedido(302, "16", Hoy, Linea(Constantes.EstadosLineaVenta.EN_CURSO, 0, Hoy)),
                Pedido(303, "16", Hoy, Linea(Constantes.EstadosLineaVenta.EN_CURSO, 5, Hoy))
            });

            List<CabPedidoVta> resultado = await servicio.ObtenerPedidosParaFacturar("PROPIA", Hoy);

            CollectionAssert.AreEquivalent(new[] { 303 }, resultado.Select(p => p.Número).ToList());
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_SoloLineasConEntregaHastaLaFecha()
        {
            pedidos.AddRange(new[]
            {
                Pedido(401, "16", Hoy, Linea(Constantes.EstadosLineaVenta.EN_CURSO, 1, Hoy.AddDays(-1))),
                Pedido(402, "16", Hoy, Linea(Constantes.EstadosLineaVenta.EN_CURSO, 1, Hoy)),
                Pedido(403, "16", Hoy, Linea(Constantes.EstadosLineaVenta.EN_CURSO, 1, Hoy.AddDays(1)))
            });

            List<CabPedidoVta> resultado = await servicio.ObtenerPedidosParaFacturar("PROPIA", Hoy);

            CollectionAssert.AreEquivalent(new[] { 401, 402 }, resultado.Select(p => p.Número).ToList());
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_BastaUnaLineaQueCumpla()
        {
            pedidos.Add(Pedido(501, "16", Hoy,
                Linea(Constantes.EstadosLineaVenta.PENDIENTE, null, Hoy.AddDays(5)),
                Linea(Constantes.EstadosLineaVenta.EN_CURSO, 3, Hoy)));

            List<CabPedidoVta> resultado = await servicio.ObtenerPedidosParaFacturar("PROPIA", Hoy);

            Assert.AreEqual(501, resultado.Single().Número);
        }

        [TestMethod]
        public async Task ObtenerPedidosParaFacturar_OrdenaPorFechaYNumero()
        {
            pedidos.AddRange(new[]
            {
                Pedido(603, "16", Hoy, LineaLista()),
                Pedido(601, "16", Hoy, LineaLista()),
                Pedido(602, "16", Hoy.AddDays(-2), LineaLista())
            });

            List<CabPedidoVta> resultado = await servicio.ObtenerPedidosParaFacturar("PROPIA", Hoy);

            CollectionAssert.AreEqual(new[] { 602, 601, 603 }, resultado.Select(p => p.Número).ToList());
        }

        #endregion
    }
}
