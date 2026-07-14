using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Models;
using NestoAPI.Models.Ventas;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Controllers
{
    /// <summary>
    /// Tests del endpoint api/ventascliente/productos (ventas de un cliente agrupadas por
    /// producto, Nesto#340 1C.8: sustituye la consulta EF del grid de ventas de ClientesViewModel).
    /// </summary>
    [TestClass]
    public class VentasClienteControllerTests
    {
        private NVEntities db;
        private DbSet<LinPedidoVta> fakeLineas;
        private DbSet<SubGruposProducto> fakeSubgrupos;
        private VentasClienteController controller;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o
                .Implements<IQueryable<LinPedidoVta>>()
                .Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            fakeSubgrupos = A.Fake<DbSet<SubGruposProducto>>(o => o
                .Implements<IQueryable<SubGruposProducto>>()
                .Implements<IDbAsyncEnumerable<SubGruposProducto>>());
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.SubGruposProductoes).Returns(fakeSubgrupos);
            ConfigurarFakeDbSet(fakeSubgrupos, new[]
            {
                new SubGruposProducto { Empresa = "1", Grupo = "PEL", Número = "APA", Descripción = "Aparatos  " }
            }.AsQueryable());
            controller = new VentasClienteController(db);
        }

        private void Lineas(params LinPedidoVta[] lineas)
        {
            ConfigurarFakeDbSet(fakeLineas, lineas.AsQueryable());
        }

        private static LinPedidoVta Linea(string producto, short cantidad, DateTime fechaAlbaran,
            short estado = 2, string cliente = "15191", string contacto = "0")
        {
            return new LinPedidoVta
            {
                Empresa = "1",
                Nº_Cliente = cliente,
                Contacto = contacto,
                Producto = producto,
                Texto = "Producto " + producto,
                Cantidad = cantidad,
                Fecha_Albarán = fechaAlbaran,
                Estado = estado,
                Grupo = "PEL",
                SubGrupo = "APA",
                Familia = "Lisap  "
            };
        }

        [TestMethod]
        public void GetVentasPorProducto_AgrupaPorProductoConSumaYUltimaFecha()
        {
            Lineas(
                Linea("38697", 2, new DateTime(2026, 3, 1)),
                Linea("38697", 3, new DateTime(2026, 5, 20)),
                Linea("12345", 1, new DateTime(2026, 4, 10)));

            var resultado = controller.GetVentasPorProducto("15191", "0") as OkNegotiatedContentResult<List<VentaProductoClienteDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(2, resultado.Content.Count);
            var agrupada = resultado.Content.Single(v => v.Producto == "38697");
            Assert.AreEqual(5, agrupada.Cantidad);
            Assert.AreEqual(new DateTime(2026, 5, 20), agrupada.FechaUltVenta);
            Assert.AreEqual("Aparatos", agrupada.SubGrupo);   // descripción del subgrupo, con trim
            Assert.AreEqual("Lisap", agrupada.Familia);       // trim del padding legacy
        }

        [TestMethod]
        public void GetVentasPorProducto_ExcluyeLineasSinAlbaranYDeOtrosClientes()
        {
            Lineas(
                Linea("38697", 2, new DateTime(2026, 3, 1)),
                Linea("38697", 9, new DateTime(2026, 3, 1), estado: 1),          // en curso: fuera
                Linea("38697", 9, new DateTime(2026, 3, 1), cliente: "99999"),   // otro cliente: fuera
                Linea("38697", 9, new DateTime(2026, 3, 1), contacto: "1"));     // otro contacto: fuera

            var resultado = controller.GetVentasPorProducto("15191", "0") as OkNegotiatedContentResult<List<VentaProductoClienteDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(1, resultado.Content.Count);
            Assert.AreEqual(2, resultado.Content.Single().Cantidad);
        }

        [TestMethod]
        public void GetVentasPorProducto_ConFechaDesde_FiltraLasVentasAnteriores()
        {
            Lineas(
                Linea("38697", 2, new DateTime(2024, 1, 1)),
                Linea("38697", 3, new DateTime(2026, 5, 20)));

            var resultado = controller.GetVentasPorProducto("15191", "0", new DateTime(2025, 7, 14)) as OkNegotiatedContentResult<List<VentaProductoClienteDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(3, resultado.Content.Single().Cantidad);
        }

        [TestMethod]
        public void GetVentasPorProducto_SinFechaDesde_DevuelveLasVentasDeSiempre()
        {
            Lineas(
                Linea("38697", 2, new DateTime(2010, 1, 1)),
                Linea("38697", 3, new DateTime(2026, 5, 20)));

            var resultado = controller.GetVentasPorProducto("15191", "0") as OkNegotiatedContentResult<List<VentaProductoClienteDTO>>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(5, resultado.Content.Single().Cantidad);
        }

        [TestMethod]
        public void GetVentasPorProducto_SinCliente_DevuelveBadRequest()
        {
            var resultado = controller.GetVentasPorProducto(" ", "0");

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider).Returns(data.Provider);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }
    }
}
